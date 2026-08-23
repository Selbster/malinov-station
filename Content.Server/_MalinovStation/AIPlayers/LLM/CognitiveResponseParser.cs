using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a structured <see cref="LlmCognitiveDecision"/> - the original
/// AI Players 0.3 single-call combined schema (desire/intention/priority/confidence/reason/action/parameters
/// all from one response). AI Players 0.4's hierarchical two-stage decision (see
/// <see cref="IntentResponseParser"/>/<see cref="ActionSelectionResponseParser"/>) is what <see cref="Systems.LlmGatewaySystem"/>'s
/// live triggers actually drive now - this parser (and the <see cref="ILlmClient.DecideCognitiveAsync"/> method
/// it backs) has no production caller left, but stays exactly as it was: <see cref="LlmCognitiveDecision"/>
/// itself is still very much alive (every test that hand-builds one and calls
/// <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/> directly still exercises it, and it's what
/// the two-stage flow's own result gets synthesized into at the end), and this parser has its own direct unit
/// test (<c>CognitiveResponseParserTest</c>) - removing it would mean breaking or deleting a passing test for
/// no functional gain, not a real simplification.
/// </summary>
public static class CognitiveResponseParser
{
    public static LlmCognitiveDecision? Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var json = JsonTextUtils.StripCodeFences(rawText);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!LlmJsonParsing.TryGetNonBlankString(root, "desire", out var desire))
                return null;

            if (!LlmJsonParsing.TryGetNonBlankString(root, "intention", out var intention))
                return null;

            if (!LlmJsonParsing.TryGetUnitFloat(root, "priority", out var priority))
                return null;

            if (!LlmJsonParsing.TryGetUnitFloat(root, "confidence", out var confidence))
                return null;

            if (!LlmJsonParsing.TryGetNonBlankString(root, "action", out var action))
                return null;

            var reason = root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            var parameters = LlmJsonParsing.ParseParameters(root);

            return new LlmCognitiveDecision(desire, intention, priority, confidence, reason, action, parameters);
        }
    }
}
