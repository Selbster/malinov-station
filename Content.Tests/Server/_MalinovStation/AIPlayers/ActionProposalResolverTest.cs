using System.Collections.Generic;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.LLM;
using NUnit.Framework;

namespace Content.Tests.Server._MalinovStation.AIPlayers;

[Parallelizable]
[TestFixture]
[TestOf(typeof(ActionProposalResolver))]
public sealed class ActionProposalResolverTest
{
    private static LlmCognitiveDecision MakeDecision(string action, IReadOnlyDictionary<string, string>? parameters = null) =>
        new("desire", "intention", 0.5f, 0.5f, "reason", action, parameters ?? new Dictionary<string, string>());

    [Test]
    public void TryResolve_UnknownActionName_Fails()
    {
        var ok = ActionProposalResolver.TryResolve(MakeDecision("FlyToTheMoon"), out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TryResolve_PursueGoalMissingGoalParameter_Fails()
    {
        var ok = ActionProposalResolver.TryResolve(MakeDecision(PursueGoalAction.ActionName), out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Does.Contain("goal"));
        });
    }

    [Test]
    public void TryResolve_PursueGoalBlankGoalParameter_Fails()
    {
        var decision = MakeDecision(PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = "   " });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TryResolve_PursueGoalValid_ReturnsCorrectParams()
    {
        var decision = MakeDecision(PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = "Rest" });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.That(ok, Is.True, failReason);
        Assert.That(proposal!.ActionName, Is.EqualTo(PursueGoalAction.ActionName));
        var pursueParams = (PursueGoalActionParams)proposal.Parameters;
        Assert.Multiple(() =>
        {
            Assert.That(pursueParams.GoalName, Is.EqualTo("Rest"));
            Assert.That(pursueParams.Priority, Is.EqualTo(decision.Priority));
            Assert.That(pursueParams.Reason, Is.EqualTo(decision.Reason));
        });
    }

    [Test]
    public void TryResolve_ContinueActivity_ReturnsCorrectParamsRegardlessOfExtraneousParameters()
    {
        var decision = MakeDecision(ContinueActivityAction.ActionName, new Dictionary<string, string> { ["unused"] = "value" });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.That(ok, Is.True, failReason);
        Assert.That(proposal!.ActionName, Is.EqualTo(ContinueActivityAction.ActionName));
        Assert.That(proposal.Parameters, Is.InstanceOf<ContinueActivityActionParams>());
    }

    [Test]
    public void TryResolve_GoToKnownLocationMissingLocationParameter_Fails()
    {
        var ok = ActionProposalResolver.TryResolve(MakeDecision(GoToKnownLocationAction.ActionName), out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Does.Contain("location"));
        });
    }

    [Test]
    public void TryResolve_GoToKnownLocationBlankLocationParameter_Fails()
    {
        var decision = MakeDecision(GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "   " });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TryResolve_GoToKnownLocationValid_ReturnsCorrectParams()
    {
        var decision = MakeDecision(GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Kitchen" });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.That(ok, Is.True, failReason);
        Assert.That(proposal!.ActionName, Is.EqualTo(GoToKnownLocationAction.ActionName));
        var goToParams = (GoToKnownLocationActionParams)proposal.Parameters;
        Assert.That(goToParams.LocationHint, Is.EqualTo("Kitchen"));
    }

    [Test]
    public void TryResolve_UseInteractableMissingTargetParameter_Fails()
    {
        var ok = ActionProposalResolver.TryResolve(MakeDecision(UseInteractableAction.ActionName), out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Does.Contain("target"));
        });
    }

    [Test]
    public void TryResolve_UseInteractableBlankTargetParameter_Fails()
    {
        var decision = MakeDecision(UseInteractableAction.ActionName, new Dictionary<string, string> { ["target"] = "   " });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(proposal, Is.Null);
            Assert.That(failReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TryResolve_UseInteractableValid_ReturnsCorrectParams()
    {
        var decision = MakeDecision(UseInteractableAction.ActionName, new Dictionary<string, string> { ["target"] = "Light Switch" });
        var ok = ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason);

        Assert.That(ok, Is.True, failReason);
        Assert.That(proposal!.ActionName, Is.EqualTo(UseInteractableAction.ActionName));
        var useParams = (UseInteractableActionParams)proposal.Parameters;
        Assert.That(useParams.Target, Is.EqualTo("Light Switch"));
    }
}
