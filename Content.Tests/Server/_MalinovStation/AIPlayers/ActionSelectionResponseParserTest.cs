using Content.Server._MalinovStation.AIPlayers.LLM;
using NUnit.Framework;

namespace Content.Tests.Server._MalinovStation.AIPlayers;

[Parallelizable]
[TestFixture]
[TestOf(typeof(ActionSelectionResponseParser))]
public sealed class ActionSelectionResponseParserTest
{
    [Test]
    public void Parse_ValidJsonWithActionAndParameters_ReturnsDecision()
    {
        var decision = ActionSelectionResponseParser.Parse(
            "{\"action\": \"PickUpItem\", \"parameters\": {\"target\": \"toolbox\"}, \"reason\": \"need it for the repair\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.Action, Is.EqualTo("PickUpItem"));
            Assert.That(decision.ActionParameters["target"], Is.EqualTo("toolbox"));
            Assert.That(decision.Reason, Is.EqualTo("need it for the repair"));
        });
    }

    [Test]
    public void Parse_MissingParameters_YieldsEmptyDictNotFailure()
    {
        var decision = ActionSelectionResponseParser.Parse("{\"action\": \"ContinueActivity\", \"reason\": \"nothing new\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.ActionParameters, Is.Empty);
    }

    [Test]
    public void Parse_MissingReason_YieldsEmptyStringNotFailure()
    {
        var decision = ActionSelectionResponseParser.Parse("{\"action\": \"ContinueActivity\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Reason, Is.Empty);
    }

    [Test]
    public void Parse_NonStringParameterValue_IsDroppedNotFatal()
    {
        var decision = ActionSelectionResponseParser.Parse(
            "{\"action\": \"PickUpItem\", \"parameters\": {\"target\": \"toolbox\", \"extra\": 5}, \"reason\": \"grab it\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.ActionParameters["target"], Is.EqualTo("toolbox"));
            Assert.That(decision.ActionParameters.ContainsKey("extra"), Is.False);
        });
    }

    [Test]
    public void Parse_MarkdownCodeFence_IsStripped()
    {
        var decision = ActionSelectionResponseParser.Parse("```json\n{\"action\": \"ContinueActivity\", \"reason\": \"waiting\"}\n```");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Action, Is.EqualTo("ContinueActivity"));
    }

    [TestCase("")]
    [TestCase("not json at all")]
    [TestCase("{\"reason\": \"no action given\"}")]
    [TestCase("{\"action\": \"\"}")]
    [TestCase("[1, 2, 3]")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.That(ActionSelectionResponseParser.Parse(input), Is.Null);
    }
}
