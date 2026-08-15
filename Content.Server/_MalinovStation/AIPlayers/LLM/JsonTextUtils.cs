namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Small text-cleanup helpers shared by <see cref="ResponseParser"/> and <see cref="LineResponseParser"/>.
/// </summary>
internal static class JsonTextUtils
{
    /// <summary>
    /// Some models wrap JSON in markdown code fences even when told not to. Strip a single leading/trailing
    /// fence (optionally tagged ```json) if present; otherwise return the text unchanged.
    /// </summary>
    public static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
            return trimmed;

        var withoutOpenFence = trimmed[(firstNewline + 1)..];
        var closingFence = withoutOpenFence.LastIndexOf("```", StringComparison.Ordinal);
        return closingFence < 0 ? withoutOpenFence.Trim() : withoutOpenFence[..closingFence].Trim();
    }
}
