using System.Linq;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// AI Players 0.4 Milestone 8: deterministic memory-importance scoring for the obvious cases (spec: "prefer
/// deterministic scoring for obvious cases... do NOT invoke the LLM for every event"). A trivial door opening
/// should score low, a friend getting hurt should score high, without asking an LLM to judge either one.
/// Every call site that used to hardcode a literal importance for an "outcome" memory (see
/// <see cref="AiTraceSystem"/>) goes through this instead, so the same kind of event is scored consistently
/// rather than each call site guessing its own number. Not wired up for ambiguous/genuinely-judgment-call
/// events yet - that's <see cref="LLM.LlmRole.MemoryEvaluation"/>'s reserved role (see its own doc comment),
/// deliberately not given an automatic caller this milestone.
/// </summary>
public static class MemoryImportanceScorer
{
    private static readonly IReadOnlyDictionary<string, float> BaseImportanceBySource = new Dictionary<string, float>
    {
        ["danger"] = 0.7f,
        ["search-result"] = 0.3f,
        ["outcome"] = 0.25f,
        ["landmark"] = 0.25f,
        ["rumor"] = 0.2f,
        ["conversation"] = 0.15f,
        ["perception"] = 0.02f,
    };

    private const float DefaultBaseImportance = 0.2f;

    /// <summary>Content mentioning one of these is treated as more urgent than the source alone would
    /// suggest - a rough, cheap stand-in for genuine natural-language understanding, good enough for the
    /// obvious cases this milestone is scoped to.</summary>
    private static readonly string[] UrgentKeywords = { "fire", "injured", "emergency", "attack", "attacked", "dying", "dead" };

    /// <param name="source">The memory's <see cref="Components.AiMemory.Source"/> - the dominant signal.</param>
    /// <param name="emotionalWeight">-1 to 1; a stronger reaction (either direction) makes an event more
    /// memorable regardless of source.</param>
    /// <param name="content">Scanned for <see cref="UrgentKeywords"/>; optional.</param>
    /// <param name="involvesTrustedRelationship">Whether a participant is someone this AI has a strongly
    /// positive relationship with - spec's "trusted person helped NPC -&gt; high importance" example.</param>
    public static float Score(
        string source,
        float emotionalWeight = 0f,
        string? content = null,
        bool involvesTrustedRelationship = false)
    {
        var score = BaseImportanceBySource.GetValueOrDefault(source, DefaultBaseImportance);

        score += MathF.Abs(Math.Clamp(emotionalWeight, -1f, 1f)) * 0.3f;

        if (content is not null && UrgentKeywords.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase)))
            score += 0.2f;

        if (involvesTrustedRelationship)
            score += 0.1f;

        return Math.Clamp(score, 0f, 1f);
    }
}
