using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Actions;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// The schema-validation boundary between a raw <see cref="LlmCognitiveDecision"/>'s Action/ActionParameters
/// and a real <see cref="ActionProposal"/> (AI Players 0.3, spec section 6's "LLM → ActionProposal → Schema
/// validation → ActionRegistry" flow). A closed switch over exactly the small set of actions the LLM is
/// allowed to select this milestone (now including <see cref="UseInteractableAction"/>, spec section 16's
/// first AI Interaction slice) - deliberately smaller than the full
/// <see cref="Systems.AiActionRegistrySystem"/> catalog (e.g. <c>Talk</c>/<c>MoveTo</c> itself are registered
/// and usable programmatically, but not directly LLM-selectable - <see cref="GoToKnownLocationAction"/> is how
/// the LLM reaches the same underlying movement without ever handling raw coordinates). Only checks structural
/// validity (known action name, required parameter keys present) - semantic
/// validity (e.g. is a proposed goal name actually a real goal) is deliberately left to the action's own
/// <see cref="IAiAction.CanDo"/> downstream, so the goal-name whitelist isn't duplicated in two places.
/// </summary>
public static class ActionProposalResolver
{
    public static bool TryResolve(LlmCognitiveDecision decision, [NotNullWhen(true)] out ActionProposal? proposal, [NotNullWhen(false)] out string? failReason)
    {
        switch (decision.Action)
        {
            case PursueGoalAction.ActionName:
                if (!decision.ActionParameters.TryGetValue("goal", out var goal) || string.IsNullOrWhiteSpace(goal))
                {
                    proposal = null;
                    failReason = $"{PursueGoalAction.ActionName} requires a non-empty \"goal\" parameter.";
                    return false;
                }

                proposal = new ActionProposal(PursueGoalAction.ActionName, new PursueGoalActionParams(goal, decision.Priority, decision.Reason));
                failReason = null;
                return true;

            case ContinueActivityAction.ActionName:
                proposal = new ActionProposal(ContinueActivityAction.ActionName, new ContinueActivityActionParams());
                failReason = null;
                return true;

            case GoToKnownLocationAction.ActionName:
                if (!decision.ActionParameters.TryGetValue("location", out var location) || string.IsNullOrWhiteSpace(location))
                {
                    proposal = null;
                    failReason = $"{GoToKnownLocationAction.ActionName} requires a non-empty \"location\" parameter.";
                    return false;
                }

                proposal = new ActionProposal(GoToKnownLocationAction.ActionName, new GoToKnownLocationActionParams(location));
                failReason = null;
                return true;

            case UseInteractableAction.ActionName:
                if (!decision.ActionParameters.TryGetValue("target", out var target) || string.IsNullOrWhiteSpace(target))
                {
                    proposal = null;
                    failReason = $"{UseInteractableAction.ActionName} requires a non-empty \"target\" parameter.";
                    return false;
                }

                proposal = new ActionProposal(UseInteractableAction.ActionName, new UseInteractableActionParams(target));
                failReason = null;
                return true;

            default:
                proposal = null;
                failReason = $"\"{decision.Action}\" is not an LLM-selectable action.";
                return false;
        }
    }
}
