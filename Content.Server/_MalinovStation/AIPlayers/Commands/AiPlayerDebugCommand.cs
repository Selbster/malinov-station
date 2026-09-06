using System.Linq;
using System.Text;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Administration;
using Content.Server.Hands.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Administration;
using Content.Shared.Tools.Components;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Commands;

/// <summary>
/// Dumps an AI player's internal state (personality, needs, goal, HTN, memory/relationship counts) to the
/// shell for debugging. See spec section 29 (debug tools).
///
/// Output is Russian, matching this subsystem's own first language (every desire, intent, memory and dialogue
/// line it prints is already Russian, so an English frame around them only made the dump harder to read).
/// Values that are genuine code identifiers - goal and action names, HTN task and operator type names,
/// conversation state - deliberately stay as they are: they are what you would grep the source for.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class AiPlayerDebugCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MemorySystem _memory = default!;

    public override string Command => "aiplayer_debug";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Loc.GetString("shell-need-exactly-one-argument"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var entNet) || !EntityManager.TryGetEntity(entNet, out var uid) || !EntityManager.EntityExists(uid))
        {
            shell.WriteLine(Loc.GetString("shell-could-not-find-entity-with-uid", ("uid", args[0])));
            return;
        }

        if (!EntityManager.HasComponent<AIPlayerComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-aiplayer_debug-not-ai-player", ("entity", EntityManager.ToPrettyString(uid.Value))));
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(EntityManager.ToPrettyString(uid.Value));

        if (EntityManager.TryGetComponent<AIPlayerComponent>(uid, out var aiPlayer))
            sb.AppendLine($"Профессия: {aiPlayer.Job}");

        if (EntityManager.TryGetComponent<GoalComponent>(uid, out var goal))
        {
            var since = (_timing.CurTime - goal.CurrentGoalSince).TotalSeconds;
            sb.AppendLine($"Цель: {goal.CurrentGoal} (приоритет {goal.CurrentPriority:0.00}, причина: {goal.Reason}, держится {since:0} с)");

            if (goal.LastLlmDecision is { } lastDecision)
            {
                var decisionAge = (_timing.CurTime - goal.LastLlmDecisionAt).TotalSeconds;
                sb.AppendLine($"Последнее решение LLM: {lastDecision} ({decisionAge:0} с назад)");
            }
            else
            {
                sb.AppendLine("Последнее решение LLM: не было");
            }
        }

        if (EntityManager.TryGetComponent<NeedsComponent>(uid, out var needs))
        {
            sb.AppendLine(
                $"Потребности: усталость={needs.Fatigue:0.00} стресс={needs.Stress:0.00} " +
                $"безопасность={needs.Safety:0.00} общение={needs.SocialNeed:0.00} скука={needs.Boredom:0.00}");
        }

        if (EntityManager.TryGetComponent<PersonalityComponent>(uid, out var personality))
        {
            sb.AppendLine(
                $"Характер: общительность={personality.Sociability:0.00} смелость={personality.Courage:0.00} " +
                $"любопытство={personality.Curiosity:0.00} лень={personality.Laziness:0.00} " +
                $"профессионализм={personality.Professionalism:0.00} эмпатия={personality.Empathy:0.00} " +
                $"агрессия={personality.Aggression:0.00}");
        }

        if (EntityManager.TryGetComponent<HTNComponent>(uid, out var htn))
        {
            var planning = htn.Planning ? "да" : "нет";
            var hasPlan = htn.Plan != null ? "да" : "нет";
            sb.AppendLine($"HTN: корневая задача={htn.RootTask.Task} планирует={planning} есть план={hasPlan}");

            if (htn.Plan is { } plan)
            {
                sb.AppendLine(
                    $"Текущий план: шаг {plan.Index + 1} из {plan.Tasks.Count}, " +
                    $"операция={plan.CurrentOperator.GetType().Name}");
            }
        }

        if (EntityManager.TryGetComponent<PerceptionComponent>(uid, out var perception))
        {
            var visible = perception.LastObservation?.VisibleCharacters.Count ?? 0;
            var lastEvent = perception.LastObservation is { } observation
                ? $"{(_timing.CurTime - observation.Timestamp).TotalSeconds:0} с назад"
                : "ни разу";
            sb.AppendLine(
                $"Восприятие: радиус обзора={perception.VisionRadius} видит сейчас={visible} " +
                $"знает в лицо={perception.LastSeen.Count} последний осмотр={lastEvent}");
        }

        if (EntityManager.TryGetComponent<MemoryComponent>(uid, out var memory))
            sb.AppendLine($"Память: {memory.Memories.Count} из {memory.MaxMemories}");

        if (EntityManager.TryGetComponent<RelationshipComponent>(uid, out var relationships))
            sb.AppendLine($"Отношения: знает {relationships.Relationships.Count} существ");

        // AI Players 2.0 Milestone 1 - only present on a cognitive-mode AI player (aiplayer_spawn_cognitive).
        if (EntityManager.HasComponent<Components.CognitiveModeComponent>(uid))
        {
            sb.AppendLine("Когнитивный режим: включён");

            if (EntityManager.TryGetComponent<EmotionComponent>(uid, out var emotion))
            {
                sb.AppendLine(
                    $"Эмоции: страх={emotion.Fear:0.00} гнев={emotion.Anger:0.00} грусть={emotion.Sadness:0.00} " +
                    $"тревога={emotion.Anxiety:0.00} радость={emotion.Joy:0.00} уверенность={emotion.Confidence:0.00}");
            }

            if (EntityManager.TryGetComponent<DesireComponent>(uid, out var desire) && desire.Current.Count > 0)
            {
                sb.AppendLine("Желания:");
                foreach (var d in desire.Current)
                    sb.AppendLine($"  - {d.Name} (сила {d.Priority:0.00}) - {d.Reason}");
            }

            if (EntityManager.TryGetComponent<IntentComponent>(uid, out var intent))
            {
                sb.AppendLine(
                    $"Намерение: {intent.Name} (приоритет {intent.Priority:0.00}, уверенность {intent.Confidence:0.00}, " +
                    $"служит желанию {intent.DesireServed}) - живёт отдельно от цели выше, см. IntentComponent");
            }

            // AI Players 0.6.3, spec section 3: the whole decision chain for the most recent decision, so
            // "where does the behaviour stop?" is answered by reading one block rather than by correlating
            // scattered log lines across several AI players. The first "-" is the broken link.
            if (EntityManager.System<AiTraceSystem>().DescribeLastDecision(uid.Value) is { } decisionTrace)
                sb.AppendLine(decisionTrace);

            if (EntityManager.TryGetComponent<BeliefComponent>(uid, out var belief))
                sb.AppendLine($"Убеждения: {belief.Beliefs.Count} из {belief.MaxBeliefs}");

            // AI Players 0.3 - Navigation/Interaction/Inventory slices' live-perceived/remembered candidates,
            // exactly what the cognitive prompt itself would see (see PromptBuilder.BuildCognitiveUserPrompt).
            //
            // AI Players 0.6.2: this deliberately mirrors the prompt's own ranked candidate list
            // (MemorySystem.GetExplorationCandidates) rather than GetKnownLocationNames. The latter orders by
            // memory importance, which SeedKnownBeacons gives every station beacon identically - so it printed
            // the same arbitrary five names for the entire round no matter where the AI had actually been,
            // which is misleading precisely when you are trying to work out whether exploration is doing
            // anything.
            var knownLocations = _memory.GetExplorationCandidates(uid.Value);
            sb.AppendLine(knownLocations.Count > 0
                ? $"Куда стоит сходить (лучшее первым): {string.Join(", ", knownLocations)}"
                : "Куда стоит сходить: некуда");

            // The actual visit history behind that ranking. Without this there is no way to tell "familiarity
            // is not updating" from "familiarity is updating and the ranking simply has not changed yet".
            if (EntityManager.TryGetComponent<LocationKnowledgeComponent>(uid, out var locationKnowledge))
            {
                if (locationKnowledge.Places.Count == 0)
                {
                    sb.AppendLine("Где побывал: пока нигде (ни одна зона не была засчитана)");
                }
                else
                {
                    var visited = locationKnowledge.Places
                        .OrderByDescending(p => p.Value.LastVisitedAt)
                        .Select(p => $"{p.Key} x{p.Value.VisitCount} (знакомость {p.Value.Familiarity:0.00})");
                    sb.AppendLine($"Где побывал: {string.Join(", ", visited)}");
                }
            }

            if (EntityManager.TryGetComponent<LandmarkPerceptionComponent>(uid, out var landmarkPerception))
                sb.AppendLine($"Текущая зона: {landmarkPerception.CurrentAreaLabel ?? "(не в именованной зоне)"}");

            if (EntityManager.TryGetComponent<ExplorationComponent>(uid, out var exploration))
            {
                var cooling = exploration.NoTargetCooldownUntil > _timing.CurTime
                    ? $", исследование на паузе ещё {(exploration.NoTargetCooldownUntil - _timing.CurTime).TotalSeconds:0} с"
                    : string.Empty;
                sb.AppendLine($"Цель исследования: {exploration.CurrentTargetName ?? "(нет либо безымянный фронтир)"}{cooling}");
            }

            if (EntityManager.TryGetComponent<InteractionOpportunityComponent>(uid, out var interactionOpportunity))
            {
                var names = interactionOpportunity.NearbyInteractables
                    .Where(e => !EntityManager.Deleted(e))
                    .Select(e => EntityManager.GetComponent<MetaDataComponent>(e).EntityName);
                sb.AppendLine($"Рядом можно взаимодействовать: {(interactionOpportunity.NearbyInteractables.Count > 0 ? string.Join(", ", names) : "не с чем")}");
            }

            if (EntityManager.TryGetComponent<ItemOpportunityComponent>(uid, out var itemOpportunity))
            {
                var names = itemOpportunity.NearbyItems
                    .Where(e => !EntityManager.Deleted(e))
                    .Select(e => EntityManager.GetComponent<MetaDataComponent>(e).EntityName);
                sb.AppendLine($"Предметы рядом: {(itemOpportunity.NearbyItems.Count > 0 ? string.Join(", ", names) : "нет")}");
            }
        }

        if (EntityManager.TryGetComponent<ConversationComponent>(uid, out var conversation))
        {
            var partnerName = conversation.Partner is { } partner && !EntityManager.Deleted(partner)
                ? EntityManager.GetComponent<MetaDataComponent>(partner).EntityName
                : "нет";
            sb.AppendLine($"Разговор: состояние={conversation.State} собеседник={partnerName} причина={conversation.Reason ?? "нет"}");
        }

        if (EntityManager.TryGetComponent<DangerComponent>(uid, out var danger))
        {
            sb.AppendLine(
                $"Опасность: источник угрозы={danger.ThreatSource?.ToString() ?? "нет"} " +
                $"раненый рядом={danger.NearbyInjured?.ToString() ?? "нет"} " +
                $"очаг пожара={danger.FireHazardLocation?.ToString() ?? "нет"}");
        }

        if (EntityManager.TryGetComponent<RepairOpportunityComponent>(uid, out var repair))
            sb.AppendLine($"Что можно починить рядом: {repair.NearbyRepairTarget?.ToString() ?? "нечего"}");

        var hands = EntityManager.System<HandsSystem>();
        var activeHand = hands.GetActiveHand(uid.Value);
        foreach (var handName in hands.EnumerateHands(uid.Value))
        {
            var held = hands.GetHeldItem(uid.Value, handName);
            var qualities = held is { } item && EntityManager.TryGetComponent<ToolComponent>(item, out var tool)
                ? string.Join(",", tool.Qualities)
                : "нет";
            var marker = handName == activeHand ? " (активная)" : "";
            sb.AppendLine($"Рука '{handName}'{marker}: {held?.ToString() ?? "пусто"} (качества инструмента: {qualities})");
        }

        if (EntityManager.TryGetComponent<AiLodComponent>(uid, out var lod))
            sb.AppendLine($"Уровень детализации: {lod.Level} (множитель x{lod.GetMultiplier():0.#})");

        shell.WriteLine(sb.ToString());
    }
}
