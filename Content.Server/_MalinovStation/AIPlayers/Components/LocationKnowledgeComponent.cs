namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// How well an AI player actually knows one place it knows *by name* (see <see cref="MemoryComponent"/>'s
/// "landmark"/"search-result" memories, which is what makes a place appear in <see cref="LocationKnowledgeComponent"/>
/// at all) - "known" and "familiar" are deliberately different concepts here (AI Players 0.6 spec section 6's
/// own example: "Cargo: known=true, visits=1, familiarity=0.2"). A place seeded at spawn via
/// <see cref="Systems.LandmarkPerceptionSystem.SeedKnownBeacons"/> starts out known-by-name only - no entry
/// here yet, i.e. <see cref="VisitCount"/> 0 / <see cref="Familiarity"/> 0 - until the AI actually walks there.
/// </summary>
public sealed class LocationKnowledge
{
    public int VisitCount;

    /// <summary>0 (name known, never been there) to 1 (thoroughly familiar). See
    /// <see cref="Systems.LandmarkPerceptionSystem.Scan"/> for the formula.</summary>
    public float Familiarity;

    public TimeSpan FirstVisitedAt;

    public TimeSpan LastVisitedAt;
}

/// <summary>
/// AI Players 0.6: per-AI-player personal familiarity with the places it knows by name, keyed by the same
/// <see cref="AiMemory.Subject"/> text <see cref="MemoryComponent"/>'s landmark memories already use. Only
/// ever added to AI players spawned in cognitive mode (see <see cref="Systems.AIPlayerSystem.SpawnAiPlayer"/>) -
/// matches the same "new cognitive-only side effect" convention <see cref="LandmarkPerceptionComponent"/>/
/// <see cref="ItemOpportunityComponent"/>/<see cref="InteractionOpportunityComponent"/> already established.
/// Deliberately not folded into <see cref="AiMemory"/> itself: familiarity is a running per-place tally, not a
/// discrete episodic event the way a memory is.
/// </summary>
[RegisterComponent]
public sealed partial class LocationKnowledgeComponent : Component
{
    [ViewVariables]
    public Dictionary<string, LocationKnowledge> Places = new();
}
