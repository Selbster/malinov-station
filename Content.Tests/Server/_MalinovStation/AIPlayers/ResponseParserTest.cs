using Content.Server._MalinovStation.AIPlayers.LLM;
using NUnit.Framework;

namespace Content.Tests.Server._MalinovStation.AIPlayers;

[Parallelizable]
[TestFixture]
[TestOf(typeof(ResponseParser))]
public sealed class ResponseParserTest
{
    [Test]
    public void Parse_ValidJson_ReturnsDecision()
    {
        var decision = ResponseParser.Parse("{\"intent\": \"Rest\", \"priority\": 0.75, \"reason\": \"tired\"}");

        Assert.That(decision, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decision!.Intent, Is.EqualTo("Rest"));
            Assert.That(decision.Priority, Is.EqualTo(0.75f));
            Assert.That(decision.Reason, Is.EqualTo("tired"));
        });
    }

    [Test]
    public void Parse_MarkdownCodeFence_IsStripped()
    {
        var decision = ResponseParser.Parse("```json\n{\"intent\": \"Idle\", \"priority\": 0.1, \"reason\": \"nothing to do\"}\n```");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Intent, Is.EqualTo("Idle"));
    }

    [Test]
    public void Parse_MissingReason_DefaultsToEmptyString()
    {
        var decision = ResponseParser.Parse("{\"intent\": \"Idle\", \"priority\": 0.2}");

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Reason, Is.EqualTo(string.Empty));
    }

    [TestCase("")]
    [TestCase("not json at all")]
    [TestCase("{\"priority\": 0.5, \"reason\": \"no intent field\"}")]
    [TestCase("{\"intent\": \"\", \"priority\": 0.5}")]
    [TestCase("{\"intent\": \"Rest\", \"priority\": 1.5}")]
    [TestCase("{\"intent\": \"Rest\", \"priority\": -0.1}")]
    [TestCase("{\"intent\": \"Rest\"}")]
    [TestCase("{\"intent\": \"Rest\", \"priority\": \"high\"}")]
    [TestCase("[1, 2, 3]")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.That(ResponseParser.Parse(input), Is.Null);
    }
}
