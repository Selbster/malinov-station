using Content.Server._MalinovStation.AIPlayers.LLM;
using NUnit.Framework;

namespace Content.Tests.Server._MalinovStation.AIPlayers;

[Parallelizable]
[TestFixture]
[TestOf(typeof(IntentResponseParser))]
public sealed class IntentResponseParserTest
{
    [Test]
    public void Parse_ValidJsonWithCategory_ReturnsDecision()
    {
        var decision = IntentResponseParser.Parse(
            "{\"desire\": \"fatigue\", \"intention\": \"rest_up\", \"priority\": 0.75, \"confidence\": 0.9, " +
            "\"reason\": \"tired\", \"category\": \"Work\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.Desire, Is.EqualTo("fatigue"));
            Assert.That(decision.Intention, Is.EqualTo("rest_up"));
            Assert.That(decision.Priority, Is.EqualTo(0.75f));
            Assert.That(decision.Confidence, Is.EqualTo(0.9f));
            Assert.That(decision.Reason, Is.EqualTo("tired"));
            Assert.That(decision.Category, Is.EqualTo("Work"));
        });
    }

    [Test]
    public void Parse_MissingReason_YieldsEmptyStringNotFailure()
    {
        var decision = IntentResponseParser.Parse(
            "{\"desire\": \"boredom\", \"intention\": \"keep_going\", \"priority\": 0.2, \"confidence\": 0.5, " +
            "\"category\": \"General\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Reason, Is.Empty);
    }

    [Test]
    public void Parse_MarkdownCodeFence_IsStripped()
    {
        var decision = IntentResponseParser.Parse(
            "```json\n{\"desire\": \"idle\", \"intention\": \"wait\", \"priority\": 0.1, \"confidence\": 0.5, " +
            "\"reason\": \"nothing to do\", \"category\": \"General\"}\n```");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Intention, Is.EqualTo("wait"));
    }

    [TestCase("")]
    [TestCase("not json at all")]
    [TestCase("{\"intention\": \"rest\", \"priority\": 0.5, \"confidence\": 0.5, \"category\": \"General\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"priority\": 0.5, \"confidence\": 0.5, \"category\": \"General\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"confidence\": 0.5, \"category\": \"General\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 0.5, \"category\": \"General\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 1.5, \"confidence\": 0.5, \"category\": \"General\"}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 0.5, \"confidence\": 0.5}")]
    [TestCase("{\"desire\": \"fatigue\", \"intention\": \"rest\", \"priority\": 0.5, \"confidence\": 0.5, \"category\": \"\"}")]
    [TestCase("[1, 2, 3]")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.That(IntentResponseParser.Parse(input), Is.Null);
    }
}
