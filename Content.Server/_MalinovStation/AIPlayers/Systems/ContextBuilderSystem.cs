using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Assembles the <see cref="AiContext"/> sent to the LLM for one AI player. This is the enforcement point
/// for "don't leak hidden information" (spec section 16): it only reads what Perception already recorded
/// (LastObservation/memories/relationships), never a fresh omniscient query over the whole station.
/// Also assembles the richer <see cref="CognitiveState"/> for cognitive-mode AI players (AI Players 2.0
/// Milestone 1) - kept in the same system rather than a new one so this stays the single "don't leak hidden
/// information" enforcement point instead of forking it.
/// </summary>
public sealed partial class ContextBuilderSystem : EntitySystem
{
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private RelationshipSystem _relationships = default!;
    [Dependency] private SatiationSystem _satiation = default!;

    // Same threshold keys GoalSystem/vanilla FoodCompound gate on - deliberately duplicated (not shared via a
    // public field) rather than reaching into GoalSystem's private state, same "each file mirrors the exact
    // vanilla threshold it needs" precedent GoalSystem's own doc comments already establish.
    private static readonly SatiationValue PeckishThreshold = "Peckish";
    private static readonly SatiationValue ParchedThreshold = "Parched";
    private static readonly SatiationValue? NoLowerBound = null;

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
    /// Builds the <see cref="CognitiveState"/> for a cognitive-mode AI player, or null if it isn't a fully
    /// set-up AI player. Works for any AI player component-wise (doesn't require <see cref="CognitiveModeComponent"/>
    /// itself), but is only ever called for one today - see <see cref="LlmGatewaySystem"/>.
    /// </summary>
    public CognitiveState? BuildCognitiveState(EntityUid uid)
    {
        if (!TryComp<AIPlayerComponent>(uid, out var aiPlayer) ||
            !TryComp<PersonalityComponent>(uid, out var personality) ||
            !TryComp<NeedsComponent>(uid, out var needs) ||
            !TryComp<GoalComponent>(uid, out var goal))
        {
            return null;
        }

        var hasSatiation = TryComp<SatiationComponent>(uid, out var satiation);
        var isHungry = hasSatiation &&
            _satiation.IsValueInRange((uid, satiation!), SatiationSystem.Hunger, above: NoLowerBound, below: PeckishThreshold);
        var isThirsty = hasSatiation &&
            _satiation.IsValueInRange((uid, satiation!), SatiationSystem.Thirst, above: NoLowerBound, below: ParchedThreshold);

        var emotion = TryComp<EmotionComponent>(uid, out var emotionComp)
            ? new CognitiveEmotion(emotionComp.Fear, emotionComp.Anger, emotionComp.Sadness, emotionComp.Anxiety, emotionComp.Joy, emotionComp.Confidence)
            : new CognitiveEmotion(0f, 0f, 0f, 0f, 0f, 0f);

        var desires = TryComp<DesireComponent>(uid, out var desireComp)
            ? (IReadOnlyList<Desire>)desireComp.Current
            : Array.Empty<Desire>();

        var intent = TryComp<IntentComponent>(uid, out var intentComp)
            ? new Desire(intentComp.Name, intentComp.Priority, intentComp.DesireServed)
            : new Desire(goal.CurrentGoal, goal.CurrentPriority, goal.Reason);
        var intentConfidence = intentComp?.Confidence ?? 1f;

        var visible = new List<CognitivePerceivedCharacter>();
        var relevantMemories = new List<string>();

        if (TryComp<PerceptionComponent>(uid, out var perception) && perception.LastObservation is { } observation)
        {
            foreach (var other in observation.VisibleCharacters)
            {
                if (Deleted(other))
                    continue;

                var relationship = _relationships.GetRelationship(uid, other);
                var recent = _memory.GetMemoriesAbout(uid, other, max: 3);

                visible.Add(new CognitivePerceivedCharacter(
                    Comp<MetaDataComponent>(other).EntityName,
                    relationship.Trust,
                    relationship.Respect,
                    relationship.Fear,
                    relationship.Friendship,
                    relationship.Anger,
                    relationship.Loyalty,
                    recent.Count > 0 ? recent[0].Content : null));

                relevantMemories.AddRange(recent.Select(m => m.Content));
            }
        }

        // "landmark" memories get their own structured channel below (KnownLocations) rather than appearing
        // here too as generic prose - avoids telling the LLM the same thing twice in two different shapes.
        var knownFacts = _memory.GetMostImportant(uid, max: 5)
            .Where(m => m.Source != "rumor" && m.Source != "landmark")
            .Select(m => m.Content)
            .ToList();

        var beliefs = TryComp<BeliefComponent>(uid, out var beliefComp)
            ? beliefComp.Beliefs
                .OrderByDescending(b => b.Confidence)
                .ThenByDescending(b => b.Timestamp)
                .Take(5)
                .Select(b => new BeliefSummary(b.Content, b.Confidence, b.Source))
                .ToList()
            : new List<BeliefSummary>();

        var knownLocations = _memory.GetKnownLocationNames(uid, max: 5);

        // AI Players 0.3, spec section 16: names resolved fresh off each live EntityUid, same convention as
        // VisibleWorld above - this list is transient (InteractionOpportunitySystem's own scan), never memory,
        // so there's nothing to look up but the entity itself.
        var nearbyInteractables = TryComp<InteractionOpportunityComponent>(uid, out var interactionOpportunity)
            ? interactionOpportunity.NearbyInteractables
                .Where(e => !Deleted(e))
                .Select(e => Comp<MetaDataComponent>(e).EntityName)
                .ToList()
            : new List<string>();

        // AI Players 0.3, spec section 15/18: same live-EntityUid-fresh-name convention as NearbyInteractables
        // above - ItemOpportunitySystem's own scan, never memory.
        var nearbyItems = TryComp<ItemOpportunityComponent>(uid, out var itemOpportunity)
            ? itemOpportunity.NearbyItems
                .Where(e => !Deleted(e))
                .Select(e => Comp<MetaDataComponent>(e).EntityName)
                .ToList()
            : new List<string>();

        // AI Players 0.4 Milestone 5: null unless actually mid-commitment (AiBusyStateComponent.CurrentAction
        // is only ever populated for an IsExtended action - see AiActionRegistrySystem.TryDoAction).
        var busy = TryComp<AiBusyStateComponent>(uid, out var busyComp) && busyComp.CurrentAction is { } currentAction
            ? new CognitiveBusyState(currentAction, busyComp.Reason)
            : null;

        return new CognitiveState(
            Comp<MetaDataComponent>(uid).EntityName,
            aiPlayer.Job?.Id ?? "Unknown",
            SummarizePersonality(personality),
            new CognitiveNeeds(needs.Fatigue, needs.Stress, needs.Safety, needs.SocialNeed, isHungry, isThirsty),
            emotion,
            goal.CurrentGoal,
            desires,
            intent,
            intentConfidence,
            visible,
            relevantMemories,
            knownFacts,
            beliefs,
            knownLocations,
            nearbyInteractables,
            nearbyItems,
            busy);
    }

    /// <summary>
    /// Builds the context for one line of <paramref name="speaker"/>'s side of a conversation with
    /// <paramref name="partner"/>, or null if either is missing required components/has been deleted.
    /// </summary>
    public DialogueContext? BuildDialogueContext(EntityUid speaker, EntityUid partner, string? partnerJustSaid, string? rumorToShare = null, string? reasonForApproaching = null)
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
            rumorToShare,
            reasonForApproaching);
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
