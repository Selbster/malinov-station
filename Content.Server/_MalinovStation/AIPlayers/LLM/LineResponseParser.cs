using System.Text.Json;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Parses and validates a raw LLM response into a single line of dialogue. Same strictness policy as
/// <see cref="ResponseParser"/> (structured JSON only, malformed input -> null), just a smaller schema:
/// <c>{"line": "..."}</c>.
/// </summary>
public static class LineResponseParser
{
    private const int MaxLength = 300;

    public static string? Parse(string? rawText)
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

            if (!root.TryGetProperty("line", out var lineProp) || lineProp.ValueKind != JsonValueKind.String)
                return null;

            var line = lineProp.GetString();
            if (string.IsNullOrWhiteSpace(line))
                return null;

            line = line.Trim();
            return line.Length > MaxLength ? line[..MaxLength] : line;
        }
    }
}
