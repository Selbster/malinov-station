using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Small JSON-field-validation helpers shared by <see cref="CognitiveResponseParser"/>,
/// <see cref="IntentResponseParser"/> and <see cref="ActionSelectionResponseParser"/> - extracted rather than
/// duplicated a third time when the hierarchical two-stage decision (AI Players 0.4) split
/// <see cref="CognitiveResponseParser"/>'s old single combined schema into two smaller ones. Every parser here
/// stays strict by design (spec section 17): a missing/wrong-typed/out-of-range field fails the whole parse
/// rather than falling back to a best-effort guess.
/// </summary>
internal static class LlmJsonParsing
{
    public static bool TryGetNonBlankString(JsonElement root, string property, out string value)
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

    public static bool TryGetUnitFloat(JsonElement root, string property, out float value)
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

    /// <summary>
    /// Reads the optional "parameters" object. Deliberately lenient rather than failing the whole parse:
    /// absent/wrong-typed "parameters" yields an empty dict, and any individual non-string value is dropped
    /// rather than rejecting the response - every parameter these actions need is a plain string, so a stray
    /// malformed extra key shouldn't sink an otherwise-valid decision.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseParameters(JsonElement root)
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
}
