using System.Text;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Administration;
using Content.Server.Hands.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Administration;
using Content.Shared.Tools.Components;
using Robust.Shared.Console;

namespace Content.Server._MalinovStation.AIPlayers.Commands;

/// <summary>
/// Dumps an AI player's internal state (personality, needs, goal, HTN, memory/relationship counts) to the
/// shell for debugging. See spec section 29 (debug tools).
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class AiPlayerDebugCommand : LocalizedEntityCommands
{
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
            sb.AppendLine($"Job: {aiPlayer.Job}");

        if (EntityManager.TryGetComponent<GoalComponent>(uid, out var goal))
            sb.AppendLine($"Goal: {goal.CurrentGoal} (priority {goal.CurrentPriority:0.00}, reason: {goal.Reason})");

        if (EntityManager.TryGetComponent<NeedsComponent>(uid, out var needs))
            sb.AppendLine($"Needs: Fatigue={needs.Fatigue:0.00} Stress={needs.Stress:0.00} Safety={needs.Safety:0.00} Social={needs.SocialNeed:0.00}");

        if (EntityManager.TryGetComponent<PersonalityComponent>(uid, out var personality))
        {
            sb.AppendLine(
                $"Personality: Sociability={personality.Sociability:0.00} Courage={personality.Courage:0.00} " +
                $"Laziness={personality.Laziness:0.00} Professionalism={personality.Professionalism:0.00} " +
                $"Empathy={personality.Empathy:0.00} Aggression={personality.Aggression:0.00}");
        }

        if (EntityManager.TryGetComponent<HTNComponent>(uid, out var htn))
            sb.AppendLine($"HTN: rootTask={htn.RootTask.Task} planning={htn.Planning} hasPlan={htn.Plan != null}");

        if (EntityManager.TryGetComponent<PerceptionComponent>(uid, out var perception))
        {
            var visible = perception.LastObservation?.VisibleCharacters.Count ?? 0;
            sb.AppendLine($"Perception: visionRadius={perception.VisionRadius} currentlyVisible={visible} everSeen={perception.LastSeen.Count}");
        }

        if (EntityManager.TryGetComponent<MemoryComponent>(uid, out var memory))
            sb.AppendLine($"Memory: {memory.Memories.Count}/{memory.MaxMemories}");

        if (EntityManager.TryGetComponent<RelationshipComponent>(uid, out var relationships))
            sb.AppendLine($"Relationships: {relationships.Relationships.Count} known entities");

        if (EntityManager.TryGetComponent<DangerComponent>(uid, out var danger))
            sb.AppendLine($"Danger: threatSource={danger.ThreatSource} nearbyInjured={danger.NearbyInjured}");

        if (EntityManager.TryGetComponent<RepairOpportunityComponent>(uid, out var repair))
            sb.AppendLine($"RepairOpportunity: nearbyTarget={repair.NearbyRepairTarget}");

        var hands = EntityManager.System<HandsSystem>();
        var activeHand = hands.GetActiveHand(uid.Value);
        foreach (var handName in hands.EnumerateHands(uid.Value))
        {
            var held = hands.GetHeldItem(uid.Value, handName);
            var qualities = held is { } item && EntityManager.TryGetComponent<ToolComponent>(item, out var tool)
                ? string.Join(",", tool.Qualities)
                : "none";
            var marker = handName == activeHand ? " (active)" : "";
            sb.AppendLine($"Hand '{handName}'{marker}: {held?.ToString() ?? "empty"} (qualities: {qualities})");
        }

        if (EntityManager.TryGetComponent<AiLodComponent>(uid, out var lod))
            sb.AppendLine($"LOD: {lod.Level} (multiplier x{lod.GetMultiplier():0.#})");

        shell.WriteLine(sb.ToString());
    }
}
