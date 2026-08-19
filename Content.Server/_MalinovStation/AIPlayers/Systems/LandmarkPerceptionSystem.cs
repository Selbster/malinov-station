using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Pinpointer;
using Content.Shared.Interaction;
using Content.Shared.Physics;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Periodically scans for the nearest vanilla station beacon (<c>NavMapBeaconComponent</c> - Kitchen, Medbay,
/// Engineering etc, placed on every station map, see <c>NavMapSystem.TryGetNearestBeacon</c>) within
/// unobstructed line of sight, and remembers a genuinely new one by name (AI Navigation Controller v1),
/// mirroring <see cref="RepairOpportunitySystem"/>'s own scan-and-remember shape and reusing the same
/// non-omniscience discipline <see cref="PerceptionSystem"/> uses for characters: an AI player can only come
/// to know a place it could actually see, never a fresh station-wide query at decision time (see
/// <see cref="MemorySystem.FindKnownLocation"/>, which is the only thing that ever reads this memory back).
/// Deliberately its own system rather than folded into <see cref="PerceptionSystem"/>, whose own doc comment
/// scopes it to characters only.
///
/// Only ever added to AI players spawned in cognitive mode (see <see cref="AIPlayerSystem.SpawnAiPlayer"/>) -
/// a legacy AI player never has <see cref="LandmarkPerceptionComponent"/>, so this scan never runs for it at
/// all (matches the "new memory-writing side effects are cognitive-only" convention the last milestone
/// established, and avoids the added spatial-scan cost for every legacy/background AI player).
/// </summary>
public sealed partial class LandmarkPerceptionSystem : EntitySystem
{
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private AiLodSystem _lod = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LandmarkPerceptionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var landmark, out var xform))
        {
            landmark.ScanAccumulator -= frameTime;
            if (landmark.ScanAccumulator > 0f)
                continue;

            landmark.ScanAccumulator = landmark.ScanCooldown * _lod.GetMultiplier(uid);
            Scan(uid, landmark, xform);
        }
    }

    private void Scan(EntityUid uid, LandmarkPerceptionComponent landmark, TransformComponent xform)
    {
        if (!_navMap.TryGetNearestBeacon((uid, xform), out var beacon, out _))
            return;

        var beaconUid = beacon.Value.Owner;
        var text = beacon.Value.Comp.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!_interaction.InRangeUnobstructed(uid, beaconUid, landmark.ScanRadius, CollisionGroup.Opaque))
            return;

        // Already remembered this exact beacon - nothing new to learn. Query-based rather than tracked in a
        // separate "known beacons" set: if this memory later gets evicted under MemoryComponent.MaxMemories,
        // re-noticing and re-adding it here is fine/self-healing, not a bug to design around.
        if (TryComp<MemoryComponent>(uid, out var memory) &&
            memory.Memories.Any(m => m.Source == "landmark" && m.Participants.Contains(beaconUid)))
        {
            return;
        }

        _memory.AddMemory(
            uid,
            content: $"There's a place nearby called \"{text}\".",
            importance: 0.25f,
            source: "landmark",
            participants: new[] { beaconUid },
            location: Transform(beaconUid).Coordinates,
            subject: text);
    }
}
