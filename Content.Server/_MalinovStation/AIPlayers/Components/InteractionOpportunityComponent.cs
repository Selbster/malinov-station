namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks the AI player's currently-visible, single-step, no-UI interactable objects (AI Players 0.3's first
/// AI Interaction slice, spec section 16) - e.g. a wall light switch/button. Populated by
/// <see cref="Systems.InteractionOpportunitySystem"/>'s own line-of-sight-gated scan, mirroring
/// <see cref="RepairOpportunityComponent"/>'s shape but as a list: unlike a repair target (react to the one
/// nearest opportunity), the LLM needs to see and choose among several simultaneous candidates by name (spec
/// section 16's "Inspect candidates" step). Kept separate from <see cref="PerceptionComponent"/> since these
/// aren't characters, and from <see cref="LandmarkPerceptionComponent"/> since this is deliberately NOT
/// memory-backed - an interactable is either in reach right now or it isn't, never "remembered" for later.
/// </summary>
[RegisterComponent]
public sealed partial class InteractionOpportunityComponent : Component
{
    /// <summary>Every currently-visible candidate as of the last scan - rebuilt fresh each time, not an
    /// incremental/deduped set (see the system's own doc comment for why that's fine here).</summary>
    [ViewVariables]
    public List<EntityUid> NearbyInteractables = new();

    [ViewVariables]
    public float ScanAccumulator;

    [DataField]
    public float ScanCooldown = 3f;

    /// <summary>Shorter than <see cref="RepairOpportunityComponent.ScanRadius"/>/landmark's 10f - this is an
    /// arm's-reach discretionary interaction, not "notice something across the room".</summary>
    [DataField]
    public float ScanRadius = 5f;
}
