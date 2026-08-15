using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Assembles the <see cref="AiContext"/> sent to the LLM for one AI player. This is the enforcement point
/// for "don't leak hidden information" (spec section 16): it only reads what Perception already recorded
/// (LastObservation/memories/relationships), never a fresh omniscient query over the whole station.
/// </summary>
public sealed partial class ContextBuilderSystem : EntitySystem
{
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private RelationshipSystem _relationships = default!;

    private static readonly (string Name, Func<PersonalityComponent, float> Value)[] Traits =
    {
        ("Sociability", p => p.Sociability),
        ("Courage", p => p.Courage),
        ("Curiosity", p => p.Curiosity),
        ("Laziness", p => p.Laziness),
        ("Greed", p => p.Greed),
        ("Aggression", p => p.Aggression),
        ("Loyalty", p => p.Loyalty),
        ("RiskTolerance", p => p.RiskTolerance),
        ("AuthorityRespect", p => p.AuthorityRespect),
        ("Professionalism", p => p.Professionalism),
        ("Empathy", p => p.Empathy),
        ("Honesty", p => p.Honesty),
        ("Impulsiveness", p => p.Impulsiveness),
    };

    /// <summary>
    /// Builds the context for <paramref name="uid"/>, or null if it isn't a fully set-up AI player.
    /// </summary>
    public AiContext? BuildContext(EntityUid uid)
    {
        if (!TryComp<AIPlayerComponent>(uid, out var aiPlayer) ||
            !TryComp<PersonalityComponent>(uid, out var personality) ||
            !TryComp<NeedsComponent>(uid, out var needs) ||
            !TryComp<GoalComponent>(uid, out var goal))
        {
            return null;
        }

        var visible = new List<PerceivedCharacterContext>();

        if (TryComp<PerceptionComponent>(uid, out var perception) && perception.LastObservation is { } observation)
        {
            foreach (var other in observation.VisibleCharacters)
            {
                if (Deleted(other))
                    continue;

                var relationship = _relationships.GetRelationship(uid, other);
                var recent = _memory.GetMemoriesAbout(uid, other, max: 1).FirstOrDefault();

                visible.Add(new PerceivedCharacterContext(
                    Comp<MetaDataComponent>(other).EntityName,
                    relationship.Trust,
                    relationship.Respect,
                    relationship.Fear,
                    relationship.Friendship,
                    recent?.Content));
            }
        }

        return new AiContext(
            Comp<MetaDataComponent>(uid).EntityName,
            aiPlayer.Job?.Id ?? "Unknown",
            SummarizePersonality(personality),
            needs.Fatigue,
            needs.Stress,
            needs.SocialNeed,
            goal.CurrentGoal,
            goal.CurrentPriority,
            goal.Reason,
            visible);
    }

    /// <summary>
    /// Builds the context for one line of <paramref name="speaker"/>'s side of a conversation with
    /// <paramref name="partner"/>, or null if either is missing required components/has been deleted.
    /// </summary>
    public DialogueContext? BuildDialogueContext(EntityUid speaker, EntityUid partner, string? partnerJustSaid, string? rumorToShare = null)
    {
        if (Deleted(partner) || !TryComp<PersonalityComponent>(speaker, out var personality))
            return null;

        var relationship = _relationships.GetRelationship(speaker, partner);
        var recent = _memory.GetMemoriesAbout(speaker, partner, max: 1).FirstOrDefault();

        return new DialogueContext(
            Comp<MetaDataComponent>(speaker).EntityName,
            SummarizePersonality(personality),
            Comp<MetaDataComponent>(partner).EntityName,
            relationship.Trust,
            relationship.Respect,
            relationship.Friendship,
            relationship.Fear,
            recent?.Content,
            partnerJustSaid,
            rumorToShare);
    }

    /// <summary>
    /// The 3 traits furthest from "average" (0.5), so the prompt gets a compact, characterful summary
    /// instead of 13 raw numbers.
    /// </summary>
    private static string SummarizePersonality(PersonalityComponent personality)
    {
        var notable = Traits
            .Select(t => (t.Name, Value: t.Value(personality)))
            .OrderByDescending(t => MathF.Abs(t.Value - 0.5f))
            .Take(3)
            .Select(t => $"{t.Name}={t.Value:0.00}");

        return string.Join(", ", notable);
    }
}
