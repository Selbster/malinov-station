using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a structured <see cref="LlmDecision"/>. Strict by design
/// (spec section 17): malformed JSON, missing fields, or an out-of-range priority all result in null rather
/// than a best-effort guess. Whitelisting the intent against <see cref="Components.AIGoals.All"/> happens
/// one layer up (in the gateway) since that list is game-state, not a parsing concern.
/// </summary>
public static class ResponseParser
{
    public static LlmDecision? Parse(string? rawText)
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

            if (!root.TryGetProperty("intent", out var intentProp) || intentProp.ValueKind != JsonValueKind.String)
                return null;

            var intent = intentProp.GetString();
            if (string.IsNullOrWhiteSpace(intent))
                return null;

            if (!root.TryGetProperty("priority", out var priorityProp) ||
                priorityProp.ValueKind != JsonValueKind.Number ||
                !priorityProp.TryGetSingle(out var priority))
            {
                return null;
            }

            if (priority is < 0f or > 1f)
                return null;

            var reason = root.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            return new LlmDecision(intent, priority, reason);
        }
    }
}
