using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a structured <see cref="LlmCognitiveDecision"/>. Mirrors
/// <see cref="ResponseParser"/>'s strictness exactly: malformed JSON, a missing/wrong-typed field, or an
/// out-of-range Priority/Confidence all result in null rather than a best-effort guess. Whitelisting
/// Intention happens one layer up in the gateway (via the existing <c>TryApplyDecision</c>), same split as
/// the legacy parser.
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

            if (!TryGetNonBlankString(root, "desire", out var desire))
                return null;

            if (!TryGetNonBlankString(root, "intention", out var intention))
                return null;

            if (!TryGetUnitFloat(root, "priority", out var priority))
                return null;

            if (!TryGetUnitFloat(root, "confidence", out var confidence))
                return null;

            var reason = root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            return new LlmCognitiveDecision(desire, intention, priority, confidence, reason);
        }
    }

    private static bool TryGetNonBlankString(JsonElement root, string property, out string value)
    {
        value = string.Empty;

        if (!root.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;

        var s = prop.GetString();
        if (string.IsNullOrWhiteSpace(s))
            return false;

        value = s;
        return true;
    }

    private static bool TryGetUnitFloat(JsonElement root, string property, out float value)
    {
        value = 0f;

        if (!root.TryGetProperty(property, out var prop) ||
            prop.ValueKind != JsonValueKind.Number ||
            !prop.TryGetSingle(out var parsed) ||
            parsed is < 0f or > 1f)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
