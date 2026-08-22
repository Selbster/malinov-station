namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks the AI player's currently-visible, loose (not held/worn/contained) items it could pick up (AI
/// Players 0.3's first AI Inventory slice). Populated by <see cref="Systems.ItemOpportunitySystem"/>'s own
/// line-of-sight-gated scan, mirroring <see cref="InteractionOpportunityComponent"/>'s shape exactly - the
/// same "live candidate list, not memory-backed" reasoning applies: an item is either in reach right now or
/// it isn't, never "remembered" for later. Kept separate from <see cref="InteractionOpportunityComponent"/>
/// since picking something up (vanilla's hands system) and using something in place (vanilla's interaction
/// system) are different subsystems with different feasibility checks.
/// </summary>
[RegisterComponent]
public sealed partial class ItemOpportunityComponent : Component
{
    /// <summary>Every currently-visible loose item as of the last scan - rebuilt fresh each time, not an
    /// incremental/deduped set (see the system's own doc comment for why that's fine here).</summary>
    [ViewVariables]
    public List<EntityUid> NearbyItems = new();

    [ViewVariables]
    public float ScanAccumulator;

    [DataField]
    public float ScanCooldown = 3f;

    /// <summary>Same arm's-reach scale as <see cref="InteractionOpportunityComponent.ScanRadius"/> - there's
    /// no "walk toward it first" step, so noticing an item and being able to pick it up are meant to coincide
    /// most of the time.</summary>
    [DataField]
    public float ScanRadius = 5f;
}
