using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

public sealed partial class LlmGatewaySystem
{
    // A name from another prompt section is malformed model output, not an experienced action failure.
    private bool HasValidTarget(EntityUid uid, IAiActionParams parameters)
    {
        return parameters switch
        {
            PickUpItemActionParams pick => TryComp<ItemOpportunityComponent>(uid, out var items) &&
                HasNamedTarget(items.NearbyItems, pick.Target),
            UseInteractableActionParams use => TryComp<InteractionOpportunityComponent>(uid, out var objects) &&
                HasNamedTarget(objects.NearbyInteractables, use.Target),
            TalkToActionParams talk => TryComp<PerceptionComponent>(uid, out var perception) &&
                perception.LastObservation is { } observation && HasNamedTarget(observation.VisibleCharacters, talk.Target),
            _ => true,
        };
    }

    private bool HasNamedTarget(IEnumerable<EntityUid> candidates, string name)
    {
        foreach (var candidate in candidates)
        {
            if (!Deleted(candidate) && TryComp<MetaDataComponent>(candidate, out var meta) &&
                string.Equals(meta.EntityName, name.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
