using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a structured <see cref="LlmIntentDecision"/> - the
/// hierarchical decision's first stage (AI Players 0.4 Milestone 3). Strict by design, same convention as
/// <see cref="CognitiveResponseParser"/>: a missing/wrong-typed/out-of-range field fails the whole parse.
/// <see cref="LlmIntentDecision.Category"/> is validated for non-blankness only here - whether it's actually
/// one of the categories offered is checked by <see cref="Systems.LlmGatewaySystem"/>, which is the one that
/// knows what was offered.
/// </summary>
public static class IntentResponseParser
{
    public static LlmIntentDecision? Parse(string? rawText)
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

            if (!LlmJsonParsing.TryGetNonBlankString(root, "category", out var category))
                return null;

            var reason = root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            return new LlmIntentDecision(desire, intention, priority, confidence, reason, category);
        }
    }
}
