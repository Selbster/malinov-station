using System.Linq;
using Content.Server.Power.Components;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Physics;
using Content.Shared.Prying.Systems;
using Content.Shared.Prying.Components;
using Content.Shared.Storage;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>What an AI player should do about a particular closed door.</summary>
public enum DoorPassability : byte
{
    /// <summary>Walk up and click it.</summary>
    Open,

    /// <summary>It will never open by hand, but this AI is carrying something that can lever it apart.</summary>
    Pry,

    /// <summary>A wall as far as this AI is concerned. Route around it and do not walk over to find out.</summary>
    Blocked,
}

/// <summary>
/// One door, judged. <see cref="Reason"/> is Russian and written in the AI's own voice, because it is what
/// reaches the decision trace and the AI's own memory.
/// </summary>
public readonly record struct DoorJudgement(DoorPassability Passability, string Reason, EntityUid? Tool = null);

/// <summary>
/// AI Players 0.6.3: the single ordered answer to "can I get through this door", asked before the AI takes a
/// step toward it.
///
/// This exists as one method used by every caller because the alternative was tried and failed. The long-range
/// sweep and the close-range check used to ask *different* questions - one tested access, the other called
/// <c>CanOpen</c> - so they disagreed about the same door, and the AI would write off a door it could open or
/// walk up to one it could not. The order below is the whole design; each step is asked only once the cheaper,
/// more permanent ones above it have been settled.
///
///   1. Can it be opened by hand at all? Shutters, window shutters and blast doors all move on a lever or a
///      signal (<c>clickOpen: false</c> in their shared BaseShutter parent) and cannot be clicked by anybody.
///      Asked first because no later question can change the answer.
///   2. Is it welded? Then it opens for nobody and cannot be pried either - vanilla's own
///      <see cref="Content.Shared.Doors.Systems.SharedDoorSystem"/> cancels prying on a welded door. This sits
///      above the power check deliberately: a door that is both unpowered and welded must not be mistaken for
///      a prying opportunity.
///   3. Has it lost power? An unpowered airlock is exactly the case an ordinary crowbar is for -
///      <see cref="Content.Shared.Doors.Systems.SharedAirlockSystem"/> cancels prying on a *powered* airlock
///      and allows it on an unpowered one. So this is a way through if the AI is carrying a prying tool, and a
///      wall if it is not.
///   4. Does its ID open it? Only now, with the door known to be a normal working airlock, is it worth
///      comparing what the door demands against what this character is carrying.
/// </summary>
public sealed partial class AiDoorApproachSystem
{
    [Dependency] private Content.Shared.Access.Systems.AccessReaderSystem _access = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private PryingSystem _prying = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>The tool quality a crowbar and its equivalents carry.</summary>
    private const string PryingQuality = "Prying";

    /// <summary>
    /// Judges <paramref name="doorUid"/> for <paramref name="uid"/>.
    ///
    /// <paramref name="includeMomentaryState"/> separates what is knowable from across the room from what is
    /// only knowable up close. Bolts, pause state, a firelock's pressure reading - and power - all change from
    /// second to second; judged from twenty tiles away they produce denials that are already stale by the time
    /// the AI would have arrived, and a door written off in advance is never approached again for a minute and
    /// a half. That is how an AI locks itself out of a corridor it could have walked straight through.
    ///
    /// Power belongs firmly on that list, which cost three passing tests to rediscover.
    /// <see cref="AirlockComponent.Powered"/> begins life false and only becomes true when the first
    /// PowerChangedEvent arrives, so every door has a window after it comes into existence during which it
    /// honestly reports itself as dead. A sweep that believed it wrote off perfectly good airlocks.
    ///
    /// So the long-range sweep asks only the three genuinely permanent questions - is this a doorway, can it
    /// be opened by hand, is it welded - plus access, which is a property of the door and the ID rather than
    /// of the moment. Power and everything else is settled at arm's length, where the answer is current and a
    /// wrong one costs a few steps rather than a minute and a half.
    /// </summary>
    public DoorJudgement JudgeDoor(EntityUid uid, EntityUid doorUid, DoorComponent door, bool includeMomentaryState)
    {
        // 1a. Not a doorway at all. A windoor is the glass hatch set into a service counter - you pass a
        //     parcel through it, you do not walk through it - and routing a character into one means routing
        //     them into the counter it is mounted on.
        if (IsCounterHatch(doorUid))
            return new DoorJudgement(DoorPassability.Blocked, "это окошко на стойке, а не проход");

        // 1b. Levers and signals only - nobody opens these by clicking, so this is a wall for everyone.
        if (!door.ClickOpen)
            return new DoorJudgement(DoorPassability.Blocked, "эту створку руками не открыть — она ходит от рычага");

        // 2. Welded shut. Also rules out prying, which is why it is asked before power.
        if (IsWelded(doorUid, door))
            return new DoorJudgement(DoorPassability.Blocked, "она заварена");

        // 3. Unpowered airlock: the one case a crowbar solves. Judged only up close, because power is not
        //    knowable in advance the way the rungs above it are - see includeMomentaryState.
        // A confirmed unpowered door with a crowbar is traversable even without ID access. Include
        // this positive capability in route checks too, otherwise the access scan rejects it before
        // the actor can ever approach. Missing tools still only cause a power-related denial up close.
        if (IsGenuinelyUnpowered(doorUid) &&
            (includeMomentaryState || (door.CanPry && TryFindPryingTool(uid, out _))))
        {
            if (!door.CanPry)
                return new DoorJudgement(DoorPassability.Blocked, "она обесточена, и вскрыть её не выйдет");

            if (!TryFindPryingTool(uid, out var tool) ||
                !TryComp<PryingComponent>(tool, out var prying) || !prying.Enabled)
                return new DoorJudgement(DoorPassability.Blocked, "она обесточена, а вскрыть её нечем");

            // Use the same permission event as PryingSystem, including bolts and force-capable tools.
            var attempt = new BeforePryEvent(uid, prying.PryPowered, prying.Force, true);
            RaiseLocalEvent(doorUid, ref attempt);
            return attempt.Cancelled
                ? new DoorJudgement(DoorPassability.Blocked, "вскрыть её сейчас нельзя")
                : new DoorJudgement(DoorPassability.Pry, "она обесточена — можно вскрыть", tool);
        }

        // 4. Access. A stable fact about this door and this ID, so it is asked at any range.
        //    Asked through the door system rather than by intersecting tag sets by hand: that also
        // resolves a reader living on an inserted electronics board, emergency access, deny tags and station
        // records, all of which a raw comparison would silently get wrong in both directions. The *reason*
        // still names what the door wants, so the AI's refusal stays inspectable.
        if (!_door.HasAccess(doorUid, uid, door))
            return new DoorJudgement(DoorPassability.Blocked, DescribeMissingAccess(doorUid));

        // 5. Everything momentary, and only when the answer will still be current when acted on.
        if (includeMomentaryState && !_door.CanOpen(doorUid, door, uid, quiet: true))
            return new DoorJudgement(DoorPassability.Blocked, "она заперта");

        return new DoorJudgement(DoorPassability.Open, "она открывается");
    }

    /// <summary>
    /// Whether this door really has lost power, as opposed to merely looking that way.
    ///
    /// <see cref="AirlockComponent.Powered"/> is a cache written only when a <c>PowerChangedEvent</c> arrives,
    /// and it starts out false - so a door that has not been told about its own power yet reads as unpowered.
    /// Believing that flag alone would have the AI write off perfectly good doors as "обесточена, а вскрыть
    /// нечем", and a door written off is not approached again for ninety seconds. So the receiver itself has
    /// to agree, and a door with no receiver at all is not power-gated in the first place.
    /// </summary>
    private bool IsGenuinelyUnpowered(EntityUid doorUid) =>
        TryComp<AirlockComponent>(doorUid, out var airlock) && !airlock.Powered &&
        TryComp<ApcPowerReceiverComponent>(doorUid, out var receiver) && !receiver.Powered;

    /// <summary>
    /// Whether this "door" is really a hatch mounted on a counter rather than a way through a wall.
    ///
    /// Told apart by what it is built to collide with, not by its name. An airlock or a shutter carries
    /// <c>FullTileMask</c>; a windoor carries <c>TabletopMachineMask</c>, which omits
    /// <see cref="CollisionGroup.MidImpassable"/> - the layer people occupy - because it is mounted on a
    /// counter and is not meant to be pushing anybody around.
    ///
    /// One thing this does NOT mean, and an earlier version of this comment wrongly claimed it did: a windoor
    /// still stops people. What a mob walks into is decided by the windoor's collision *layer*
    /// (<c>GlassAirlockLayer</c>, which does include MidImpassable) against the mob's own mask - not by the
    /// windoor's mask, which is read here. So this identifies a counter hatch; it does not make it passable.
    /// Treating one as a wall is the point, per the request this was built for.
    ///
    /// Reading the collision mask rather than matching prototype names means every windoor variant is covered,
    /// including ones added later, and nothing that genuinely is a doorway is caught by accident - among all
    /// the game's doors, only windoors carry this mask.
    /// </summary>
    private bool IsCounterHatch(EntityUid doorUid)
    {
        if (!TryComp<FixturesComponent>(doorUid, out var fixtures) || fixtures.Fixtures.Count == 0)
            return false;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionMask & (int) CollisionGroup.MidImpassable) != 0)
                return false;
        }

        return true;
    }

    private bool IsWelded(EntityUid doorUid, DoorComponent door) =>
        door.State == DoorState.Welded ||
        (TryComp<WeldableComponent>(doorUid, out var weldable) && weldable.IsWelded);

    /// <summary>
    /// A prying tool this AI could actually bring to bear: any hand, any equipped slot, or one container deep
    /// inside something equipped (the crowbar in a belt or backpack). Mirrors
    /// <see cref="HTN.Preconditions.HasToolQualityPrecondition"/>'s search so "the AI believes it can pry" and
    /// "the AI can pry" cannot drift apart.
    /// </summary>
    private bool TryFindPryingTool(EntityUid uid, out EntityUid tool)
    {
        foreach (var handName in _hands.EnumerateHands(uid))
        {
            if (_hands.GetHeldItem(uid, handName) is { } held && _tools.HasQuality(held, PryingQuality))
            {
                tool = held;
                return true;
            }
        }

        tool = default;

        if (!TryComp<InventoryComponent>(uid, out var inventory))
            return false;

        var slots = _inventory.GetSlotEnumerator((uid, inventory));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } equipped)
                continue;

            if (_tools.HasQuality(equipped, PryingQuality))
            {
                tool = equipped;
                return true;
            }

            if (!TryComp<StorageComponent>(equipped, out var storage))
                continue;

            foreach (var stored in storage.Container.ContainedEntities)
            {
                if (!_tools.HasQuality(stored, PryingQuality))
                    continue;

                tool = stored;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Names what the door wants, so the denial the AI remembers is "нужен доступ: Карго" rather than a bare
    /// "нельзя". A door can accept any one of several sets, so they are joined with "или".
    /// </summary>
    private string DescribeMissingAccess(EntityUid doorUid)
    {
        if (!_access.GetMainAccessReader(doorUid, out var readerEnt) || readerEnt.Value.Comp.AccessLists.Count == 0)
            return "у тебя нет доступа";

        var wanted = readerEnt.Value.Comp.AccessLists
            .Select(set => string.Join(" и ", set.Select(NameAccess)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct()
            .ToList();

        return wanted.Count == 0
            ? "у тебя нет доступа"
            : $"нужен доступ: {string.Join(" или ", wanted)} — у тебя его нет";
    }

    private string NameAccess(ProtoId<AccessLevelPrototype> id) =>
        _prototypes.TryIndex(id, out var proto) ? proto.GetAccessLevelName() : id.Id;
}
