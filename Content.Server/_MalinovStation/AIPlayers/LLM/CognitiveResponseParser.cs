using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a structured <see cref="LlmCognitiveDecision"/>. Mirrors
/// <see cref="ResponseParser"/>'s strictness exactly for every field except <c>parameters</c> (see its own
/// remarks below): malformed JSON, a missing/wrong-typed field, or an out-of-range Priority/Confidence all
/// result in null rather than a best-effort guess. Neither Intention nor Action's parameters are whitelisted
/// here (AI Players 0.3) - Intention is deliberately free-form (never validated), and Action/parameters are
/// schema-checked one layer up by <see cref="ActionProposalResolver"/>, which needs the gateway's action
/// registry to do so.
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

            if (!TryGetNonBlankString(root, "action", out var action))
                return null;

            var reason = root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            var parameters = ParseParameters(root);

            return new LlmCognitiveDecision(desire, intention, priority, confidence, reason, action, parameters);
        }
    }

    /// <summary>
    /// Reads the optional "parameters" object. Deliberately lenient rather than failing the whole parse:
    /// absent/wrong-typed "parameters" yields an empty dict, and any individual non-string value is dropped
    /// rather than rejecting the response - the only parameter this milestone needs (PursueGoal's "goal") is
    /// a plain string, so a stray malformed extra key shouldn't sink an otherwise-valid decision. Revisit if a
    /// future action needs a required (not just best-effort) parameter type beyond string.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseParameters(JsonElement root)
    {
        if (!root.TryGetProperty("parameters", out var prop) || prop.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();

        var parameters = new Dictionary<string, string>();
        foreach (var property in prop.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value)
                parameters[property.Name] = value;
        }

        return parameters;
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
