using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.DeviceLinking.Components;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Actively looks for something nearby matching a keyword, right now (AI Players 0.3's AI Search slice) -
/// distinct from every other action here in that there's nothing passive for the LLM to already see: this is
/// the fallback for when <c>NearbyItems</c>/<c>NearbyInteractables</c>/<c>KnownLocations</c> don't already
/// have what it wants (spec section 10's "Kitchen inaccessible - search vending machines"). On success, the
/// find becomes a <c>"search-result"</c> memory - the same <see cref="MemorySystem.FindKnownLocation"/>/
/// <see cref="MemorySystem.GetKnownLocationNames"/> rails <see cref="LandmarkPerceptionSystem"/> built for
/// Navigation - so a follow-up <c>GoToKnownLocation</c>/<c>UseInteractable</c>/<c>PickUpItem"</c> decision can
/// act on it next turn, without a new combined "search and grab" action.
/// </summary>
public sealed class SearchAreaAction : IAiAction
{
    public const string ActionName = "SearchArea";

    /// <summary>Roughly double <see cref="Components.InteractionOpportunityComponent.ScanRadius"/>/
    /// <see cref="Components.ItemOpportunityComponent.ScanRadius"/> - actively looking further afield, not
    /// passively noticing nearby.</summary>
    private const float SearchRadius = 20f;

    private readonly IEntityManager _entManager;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedInteractionSystem _interaction;
    private readonly SharedContainerSystem _container;
    private readonly MemorySystem _memory;
    private readonly MobStateSystem _mobState;

    public SearchAreaAction(
        IEntityManager entManager,
        EntityLookupSystem lookup,
        SharedInteractionSystem interaction,
        SharedContainerSystem container,
        MemorySystem memory,
        MobStateSystem mobState)
    {
        _entManager = entManager;
        _lookup = lookup;
        _interaction = interaction;
        _container = container;
        _memory = memory;
        _mobState = mobState;
    }

    public string Name => ActionName;
    public string Description => "Активно осмотреться в поисках чего-то поблизости по ключевому слову.";
    public string Category => AiActionCategories.Work;
    public bool IsExtended => false;

    /// <summary>Eligibility can only check ambient state here - unlike every other action's opportunity
    /// component, there's no passive candidate list to check ahead of time without a keyword already chosen
    /// (see this class's own doc comment), so "not incapacitated" is the whole precondition.</summary>
    public bool IsEligible(EntityUid uid) => !_mobState.IsIncapacitated(uid);

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not SearchAreaActionParams search)
        {
            failReason = $"{Name} требует {nameof(SearchAreaActionParams)}.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Сущность недееспособна.";
            return false;
        }

        if (FindMatch(uid, search.Keyword) is null)
        {
            failReason = $"Я осмотрелся (осмотрелась), но не нашёл (не нашла) поблизости ничего подходящего под «{search.Keyword}».";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var search = (SearchAreaActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention every other action here
        // already established.
        if (FindMatch(uid, search.Keyword) is not { } match)
            return;

        _memory.AddMemory(
            uid,
            content: $"Осмотревшись, нашёл (нашла) поблизости нечто под названием «{match.Name}».",
            importance: 0.3f,
            source: "search-result",
            participants: new[] { match.Uid },
            location: match.Coordinates,
            subject: match.Name);
    }

    /// <summary>
    /// One-shot, wider-radius scan for something matching <paramref name="keyword"/> - inlines the same two
    /// candidate shapes and exclusion rules <see cref="Systems.ItemOpportunitySystem"/> (loose
    /// <see cref="ItemComponent"/>, not <see cref="SharedContainerSystem.IsEntityInContainer"/>) and
    /// <see cref="Systems.InteractionOpportunitySystem"/> (<see cref="SignalSwitchComponent"/>, not
    /// <see cref="ActivatableUIComponent"/>) already enforce for their own passive scans, plus the same
    /// either-direction <c>Contains</c> name-match idiom every other action's <c>FindCandidate</c> uses. A
    /// one-shot helper rather than a per-tick system, since this only ever runs when the action is invoked.
    /// </summary>
    private (EntityUid Uid, string Name, EntityCoordinates Coordinates)? FindMatch(EntityUid uid, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || !_entManager.TryGetComponent<TransformComponent>(uid, out var xform))
            return null;

        var candidates = new HashSet<Entity<MetaDataComponent>>();
        _lookup.GetEntitiesInRange(xform.Coordinates, SearchRadius, candidates);

        foreach (var (candidate, meta) in candidates)
        {
            if (candidate == uid || _entManager.Deleted(candidate))
                continue;

            var isItem = _entManager.HasComponent<ItemComponent>(candidate) && !_container.IsEntityInContainer(candidate);
            var isInteractable = _entManager.HasComponent<SignalSwitchComponent>(candidate) && !_entManager.HasComponent<ActivatableUIComponent>(candidate);
            if (!isItem && !isInteractable)
                continue;

            if (!meta.EntityName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                !keyword.Contains(meta.EntityName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_interaction.InRangeUnobstructed(uid, candidate, SearchRadius, CollisionGroup.Opaque))
                continue;

            return (candidate, meta.EntityName, _entManager.GetComponent<TransformComponent>(candidate).Coordinates);
        }

        return null;
    }
}
