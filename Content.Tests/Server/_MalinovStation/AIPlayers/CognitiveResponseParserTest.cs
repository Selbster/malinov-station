using Content.Server._MalinovStation.AIPlayers.LLM;
using NUnit.Framework;

namespace Content.Tests.Server._MalinovStation.AIPlayers;

[Parallelizable]
[TestFixture]
[TestOf(typeof(CognitiveResponseParser))]
public sealed class CognitiveResponseParserTest
{
    [Test]
    public void Parse_ValidJsonWithActionAndParameters_ReturnsDecision()
    {
        var decision = CognitiveResponseParser.Parse(
            "{\"desire\": \"fatigue\", \"intention\": \"rest_up\", \"priority\": 0.75, \"confidence\": 0.9, " +
            "\"reason\": \"tired\", \"action\": \"PursueGoal\", \"parameters\": {\"goal\": \"Rest\"}}");

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.Desire, Is.EqualTo("fatigue"));
            Assert.That(decision.Intention, Is.EqualTo("rest_up"));
            Assert.That(decision.Priority, Is.EqualTo(0.75f));
            Assert.That(decision.Confidence, Is.EqualTo(0.9f));
            Assert.That(decision.Reason, Is.EqualTo("tired"));
            Assert.That(decision.Action, Is.EqualTo("PursueGoal"));
            Assert.That(decision.ActionParameters["goal"], Is.EqualTo("Rest"));
        });
    }

    [Test]
    public void Parse_MissingParameters_YieldsEmptyDictNotFailure()
    {
        var decision = CognitiveResponseParser.Parse(
            "{\"desire\": \"boredom\", \"intention\": \"keep_going\", \"priority\": 0.2, \"confidence\": 0.5, " +
            "\"reason\": \"nothing new\", \"action\": \"ContinueActivity\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.ActionParameters, Is.Empty);
    }

    [Test]
    public void Parse_NonObjectParameters_YieldsEmptyDictNotFailure()
    {
        var decision = CognitiveResponseParser.Parse(
            "{\"desire\": \"boredom\", \"intention\": \"keep_going\", \"priority\": 0.2, \"confidence\": 0.5, " +
            "\"reason\": \"nothing new\", \"action\": \"ContinueActivity\", \"parameters\": \"not an object\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.ActionParameters, Is.Empty);
    }

    [Test]
    public void Parse_NonStringParameterValue_IsDroppedNotFatal()
    {
        var decision = CognitiveResponseParser.Parse(
            "{\"desire\": \"fatigue\", \"intention\": \"rest_up\", \"priority\": 0.75, \"confidence\": 0.9, " +
            "\"reason\": \"tired\", \"action\": \"PursueGoal\", \"parameters\": {\"goal\": \"Rest\", \"extra\": 5}}");

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.ActionParameters["goal"], Is.EqualTo("Rest"));
            Assert.That(decision.ActionParameters.ContainsKey("extra"), Is.False);
        });
    }

    [Test]
    public void Parse_MarkdownCodeFence_IsStripped()
    {
        var decision = CognitiveResponseParser.Parse(
            "```json\n{\"desire\": \"idle\", \"intention\": \"wait\", \"priority\": 0.1, \"confidence\": 0.5, " +
            "\"reason\": \"nothing to do\", \"action\": \"ContinueActivity\"}\n```");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Intention, Is.EqualTo("wait"));
    }

    [TestCase("")]
    [TestCase("not json at all")]
    [TestCase("{\"intention\": \"rest\", \"priority\": 0.5, \"confidence\": 0.5, \"action\": \"ContinueActivity\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"priority\": 0.5, \"confidence\": 0.5, \"action\": \"ContinueActivity\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"confidence\": 0.5, \"action\": \"ContinueActivity\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 0.5, \"action\": \"ContinueActivity\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 1.5, \"confidence\": 0.5, \"action\": \"ContinueActivity\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 0.5, \"confidence\": 0.5}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 0.5, \"confidence\": 0.5, \"action\": \"\"}")]
    [TestCase("[1, 2, 3]")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.That(CognitiveResponseParser.Parse(input), Is.Null);
    }
}
