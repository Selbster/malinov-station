using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.DeviceLinking.Components;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Tools.Components;
using Content.Shared.Physics;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>Finds visible people, consumables, tools or named objects and remembers the nearest match.</summary>
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
    public string Description => "Осмотреться в поисках видимой еды, воды, людей, инструментов или конкретного предмета по названию.";
    public string Category => AiActionCategories.Work;
    public bool IsExtended => false;

    /// <summary>The query is only known after selection, so eligibility checks whether the actor can search.</summary>
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

    public AiActionResult Do(EntityUid uid, IAiActionParams parameters)
    {
        var search = (SearchAreaActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention every other action here
        // already established.
        if (FindMatch(uid, search.Keyword) is not { } match)
            return AiActionResult.NoTarget($"Осмотревшись, ты не нашёл (нашла) ничего похожего на «{search.Keyword}».");

        _memory.AddMemory(
            uid,
            content: $"Осмотревшись, нашёл (нашла) поблизости нечто под названием «{match.Name}».",
            importance: 0.3f,
            source: "search-result",
            participants: new[] { match.Uid },
            location: match.Coordinates,
            subject: match.Name);

        return AiActionResult.Completed($"Ты нашёл (нашла) «{match.Name}».");
    }

    /// <summary>Queries the current world, respecting containment, visibility and the actor's diet.</summary>
    private (EntityUid Uid, string Name, EntityCoordinates Coordinates)? FindMatch(EntityUid uid, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || !_entManager.TryGetComponent<TransformComponent>(uid, out var xform))
            return null;

        var candidates = new HashSet<Entity<MetaDataComponent>>();
        _lookup.GetEntitiesInRange(xform.Coordinates, SearchRadius, candidates);

        (EntityUid Uid, string Name, EntityCoordinates Coordinates)? best = null;
        var nearest = float.MaxValue;
        var kind = keyword.Trim().ToLowerInvariant();
        var ingestion = _entManager.System<IngestionSystem>();
        foreach (var (candidate, meta) in candidates)
        {
            if (candidate == uid || _entManager.Deleted(candidate))
                continue;

            if (_container.IsEntityInContainer(candidate))
                continue;
            var transform = _entManager.GetComponent<TransformComponent>(candidate);
            var isPerson = _entManager.HasComponent<MobStateComponent>(candidate) && !_mobState.IsDead(candidate);
            var isItem = _entManager.HasComponent<ItemComponent>(candidate) && !transform.Anchored;
            var isInteractable = _entManager.HasComponent<SignalSwitchComponent>(candidate) && !_entManager.HasComponent<ActivatableUIComponent>(candidate);
            if (!isPerson && !isItem && !isInteractable)
                continue;

            var matches = kind switch
            {
                "еда" or "пища" or "food" => isItem && _entManager.HasComponent<EdibleComponent>(candidate) &&
                    ingestion.TotalNutrition(candidate, uid) > 0 && ingestion.CanConsume(uid, candidate),
                "напитки" or "напиток" or "вода" or "drink" or "drinks" or "water" => isItem &&
                    _entManager.HasComponent<EdibleComponent>(candidate) && ingestion.CanIngest(uid, candidate) && ingestion.TotalHydration(candidate) > 0 &&
                    ingestion.CanConsume(uid, candidate),
                "люди" or "человек" or "персонажи" or "people" or "person" => isPerson,
                "инструмент" or "инструменты" or "tool" or "tools" => isItem && _entManager.HasComponent<ToolComponent>(candidate),
                _ => meta.EntityName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    keyword.Contains(meta.EntityName, StringComparison.OrdinalIgnoreCase),
            };
            if (!matches)
                continue;

            if (!_interaction.InRangeUnobstructed(uid, candidate, SearchRadius, CollisionGroup.Opaque))
                continue;

            if (xform.Coordinates.TryDistance(_entManager, transform.Coordinates, out var distance) && distance < nearest)
            {
                nearest = distance;
                best = (candidate, meta.EntityName, transform.Coordinates);
            }
        }

        return best;
    }
}
