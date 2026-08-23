using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a structured <see cref="LlmActionSelectionDecision"/> - the
/// hierarchical decision's second stage (AI Players 0.4 Milestone 3). Strict on <c>action</c>, lenient on
/// <c>parameters</c> - same conventions as <see cref="CognitiveResponseParser"/>. Whether <c>action</c> is
/// actually one of the eligible actions this request offered is checked by
/// <see cref="Systems.LlmGatewaySystem"/>, not here - this parser only knows JSON shape, not what was offered.
/// </summary>
public static class ActionSelectionResponseParser
{
    public static LlmActionSelectionDecision? Parse(string? rawText)
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

            if (!LlmJsonParsing.TryGetNonBlankString(root, "action", out var action))
                return null;

            var reason = root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            var parameters = LlmJsonParsing.ParseParameters(root);

            return new LlmActionSelectionDecision(action, parameters, reason);
        }
    }
}
