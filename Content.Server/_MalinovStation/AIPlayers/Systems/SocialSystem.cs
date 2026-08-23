using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Perception;
using Content.Shared.Dataset;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Bounded, purposeful NPC-to-NPC small talk (spec sections 20/21): when an AI player's current goal is
/// Socialize and it can see another idle AI player, the two exchange exactly one greeting and one reply -
/// never an open-ended chatbot loop. Lines come from the LLM when available (via
/// <see cref="LlmGatewaySystem.TryRequestLine"/>), falling back to a small localized line dataset when the
/// LLM is disabled, busy, or fails, so conversations always happen either way. Each exchange leaves both
/// participants with a memory of it and a small relationship nudge.
/// Milestone 7 adds rumor spreading: if the initiator has an unshared, important firsthand memory (e.g.
/// "was attacked by X"), the opening line relays it instead of small talk, and the listener gains a
/// secondhand version of that memory plus a small relationship nudge toward whoever it's about.
/// </summary>
public sealed partial class SocialSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private LlmGatewaySystem _llmGateway = default!;
    [Dependency] private ContextBuilderSystem _contextBuilder = default!;
    [Dependency] private AiActionRegistrySystem _actions = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private RelationshipSystem _relationships = default!;
    [Dependency] private BeliefSystem _belief = default!;
    [Dependency] private EmotionSystem _emotion = default!;
    [Dependency] private AiLodSystem _lod = default!;

    private ISawmill _sawmill = default!;

    private static readonly ProtoId<LocalizedDatasetPrototype> GreetingLines = "AIPlayerGreetingLines";
    private static readonly ProtoId<LocalizedDatasetPrototype> ReplyLines = "AIPlayerReplyLines";

    /// <summary>Minimum memory importance worth spreading as a rumor.</summary>
    private const float RumorImportanceThreshold = 0.5f;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("aiplayers.social");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GoalComponent, ConversationComponent, PerceptionComponent, AIPlayerComponent>();
        while (query.MoveNext(out var uid, out var goal, out var conversation, out var perception, out _))
        {
            if (conversation.State != ConversationState.None || conversation.Partner != null)
                continue;

            if (goal.CurrentGoal != AIGoals.Socialize)
                continue;

            if (_timing.CurTime < conversation.CooldownUntil)
                continue;

            // Don't start new conversations for AI players nobody is anywhere near - no one would witness
            // it, and it would waste an LLM request slot budgeted for AI players someone might actually
            // notice (spec Milestone 8). Conversations already in progress still run to completion.
            if (_lod.IsBackground(uid))
                continue;

            if (perception.LastObservation is not { } observation)
                continue;

            var partner = FindPartner(uid, observation);
            if (partner is null)
                continue;

            TryInitiateConversation(uid, partner.Value);
        }
    }

    private EntityUid? FindPartner(EntityUid uid, WorldObservation observation)
    {
        foreach (var other in observation.VisibleCharacters)
        {
            if (other == uid)
                continue;

            if (!TryComp<ConversationComponent>(other, out var otherConversation))
                continue;

            if (otherConversation.State != ConversationState.None || otherConversation.Partner != null)
                continue;

            if (_timing.CurTime < otherConversation.CooldownUntil)
                continue;

            return other;
        }

        return null;
    }

    /// <summary>
    /// Starts a bounded greeting exchange between <paramref name="initiator"/> and <paramref name="partner"/> -
    /// the single shared entry point both this system's own reactive <see cref="Update"/> loop and the
    /// cognitive <c>TalkTo</c> action call into (spec section 15's AI Social slice), mirroring
    /// <see cref="GoalSystem.TrySetExternalGoal"/>'s own "one shared write path for reflex and deliberate
    /// callers alike" precedent. <paramref name="reason"/> is null for the reactive trigger (it never has one
    /// to give) and the LLM's own stated reason when a cognitive AI deliberately approaches someone.
    /// </summary>
    public void TryInitiateConversation(EntityUid initiator, EntityUid partner, string? reason = null)
    {
        var convA = Comp<ConversationComponent>(initiator);
        var convB = Comp<ConversationComponent>(partner);

        convA.Partner = partner;
        convA.IsInitiator = true;
        convA.State = ConversationState.AwaitingOpeningLine;
        convA.Reason = reason;

        // convB.State stays None until A actually speaks - it's just reserved via Partner so nothing else
        // starts a second conversation with it in the meantime.
        convB.Partner = initiator;
        convB.IsInitiator = false;

        RequestLine(initiator, partnerJustSaid: null);
    }

    private void RequestLine(EntityUid speaker, string? partnerJustSaid)
    {
        if (!TryComp<ConversationComponent>(speaker, out var conversation) ||
            conversation.Partner is not { } partner ||
            Deleted(partner))
        {
            EndConversation(speaker);
            return;
        }

        // Only the opening line can carry a rumor or a deliberate reason - keeps the exchange simple and
        // bounded.
        var rumor = partnerJustSaid is null ? FindRumorToShare(speaker, partner) : null;
        var reason = partnerJustSaid is null ? conversation.Reason : null;

        var context = _contextBuilder.BuildDialogueContext(speaker, partner, partnerJustSaid, rumor?.Content, reason);
        if (context is null)
        {
            EndConversation(speaker);
            EndConversation(partner);
            return;
        }

        if (_llmGateway.TryRequestLine(speaker, context, (uid, line) => OnLineGenerated(uid, line, rumor)))
            return;

        // LLM unavailable/disabled/over budget - fall back to a canned line immediately.
        var fallback = rumor is not null ? BuildFallbackRumorLine(rumor) : PickFallbackLine(partnerJustSaid is null);
        SpeakAndAdvance(speaker, fallback, rumor);
    }

    private void OnLineGenerated(EntityUid speaker, string? line, AiMemory? rumor)
    {
        if (Deleted(speaker) || !TryComp<ConversationComponent>(speaker, out var conversation) || conversation.Partner is null)
            return;

        var isOpening = conversation.State == ConversationState.AwaitingOpeningLine;
        var finalLine = line ?? (rumor is not null ? BuildFallbackRumorLine(rumor) : PickFallbackLine(isOpening));
        SpeakAndAdvance(speaker, finalLine, rumor);
    }

    private void SpeakAndAdvance(EntityUid speaker, string line, AiMemory? rumor = null)
    {
        if (!TryComp<ConversationComponent>(speaker, out var conversation) || conversation.Partner is not { } partner)
            return;

        var wasOpening = conversation.State == ConversationState.AwaitingOpeningLine;

        if (!_actions.TryDoAction(speaker, "Talk", new TalkActionParams(line), out var failReason))
        {
            _sawmill.Debug($"{ToPrettyString(speaker)} could not deliver a conversation line: {failReason}");
            EndConversation(speaker);
            EndConversation(partner);
            return;
        }

        RecordExchange(speaker, partner, line);

        if (rumor is not null && !Deleted(partner))
            ShareRumor(partner, rumor);

        EndConversation(speaker);

        // The opening line landing is what kicks off the partner's reply turn.
        if (wasOpening &&
            !Deleted(partner) &&
            TryComp<ConversationComponent>(partner, out var partnerConversation) &&
            partnerConversation.Partner == speaker)
        {
            partnerConversation.State = ConversationState.AwaitingReplyLine;
            RequestLine(partner, partnerJustSaid: line);
        }
    }

    /// <summary>
    /// Finds the speaker's single most important firsthand memory that the listener doesn't already have a
    /// memory about, above <see cref="RumorImportanceThreshold"/>, if any.
    /// </summary>
    private AiMemory? FindRumorToShare(EntityUid speaker, EntityUid listener)
    {
        if (!TryComp<MemoryComponent>(speaker, out var memory))
            return null;

        foreach (var candidate in memory.Memories.OrderByDescending(m => m.Importance))
        {
            if (candidate.Importance < RumorImportanceThreshold || candidate.Source != "danger")
                continue;

            if (candidate.Participants.Count == 0)
                continue;

            var about = candidate.Participants[0];
            if (about == listener || Deleted(about))
                continue;

            if (_memory.GetMemoriesAbout(listener, about).Count > 0)
                continue; // listener already knows about this one way or another

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Copies a rumor to the listener - as a secondhand <see cref="MemoryComponent"/> entry (reduced
    /// importance/emotional weight, unchanged behaviour) for a legacy AI player, or as a
    /// <see cref="BeliefComponent"/> entry (AI Players 2.0 Milestone 1) for a cognitive-mode one, since a
    /// rumor is exactly the kind of secondhand information that could be wrong and shouldn't be treated as
    /// ground truth the way every existing memory reader treats <see cref="MemoryComponent"/>. Either way,
    /// nudges their feelings toward whoever it's about - smaller than a firsthand experience would.
    /// </summary>
    private void ShareRumor(EntityUid listener, AiMemory rumor)
    {
        if (HasComp<CognitiveModeComponent>(listener))
        {
            var subject = rumor.Participants.Count > 0 && !Deleted(rumor.Participants[0])
                ? Comp<MetaDataComponent>(rumor.Participants[0]).EntityName
                : "someone";
            _belief.AddBelief(listener, subject: subject, content: rumor.Content, confidence: 0.5f, source: "rumor", participants: rumor.Participants);
        }
        else
        {
            _memory.AddMemory(
                listener,
                content: rumor.Content,
                importance: rumor.Importance * 0.6f,
                source: "rumor",
                participants: rumor.Participants,
                emotionalWeight: rumor.EmotionalWeight * 0.7f);
        }

        if (rumor.Participants.Count == 0)
            return;

        var about = rumor.Participants[0];
        if (about != listener && !Deleted(about))
            _relationships.ModifyRelationship(listener, about, fearDelta: 0.05f, trustDelta: -0.05f);
    }

    private static string BuildFallbackRumorLine(AiMemory rumor)
    {
        return $"Ты слышал(а)? {rumor.Content}";
    }

    private void RecordExchange(EntityUid speaker, EntityUid listener, string line)
    {
        TryComp<PersonalityComponent>(speaker, out var personality);
        var empathy = personality?.Empathy ?? 0.5f;

        _memory.AddMemory(
            speaker,
            content: $"Поговорил(а) с {Comp<MetaDataComponent>(listener).EntityName}: \"{line}\"",
            importance: 0.1f,
            source: "conversation",
            participants: new[] { listener },
            emotionalWeight: 0.2f);

        _relationships.ModifyRelationship(
            speaker,
            listener,
            trustDelta: 0.01f * (0.5f + empathy),
            friendshipDelta: 0.02f * (0.5f + empathy));

        // AI Players 2.0 Milestone 1: no-op for a legacy AI player (no EmotionComponent).
        _emotion.Modify(speaker, joyDelta: 0.1f * (0.5f + empathy));
    }

    private void EndConversation(EntityUid uid)
    {
        if (!TryComp<ConversationComponent>(uid, out var conversation))
            return;

        conversation.Partner = null;
        conversation.State = ConversationState.None;
        conversation.Reason = null;
        conversation.CooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(conversation.PostConversationCooldownSeconds);
    }

    private string PickFallbackLine(bool isOpening)
    {
        var dataset = _proto.Index(isOpening ? GreetingLines : ReplyLines);
        return _random.Pick(dataset);
    }
}
