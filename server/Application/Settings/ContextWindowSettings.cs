namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Per-model context-window ceilings (CARD-0082). Claude's JSONL carries usage but no limit
/// field anywhere, so configuration is the only honest source. Editable without a deploy when
/// Anthropic changes limits or the account gains a bigger window.
/// </summary>
public sealed class ContextWindowSettings
{
    /// <summary>Used when no <see cref="ModelOverrides"/> key matches the model id.</summary>
    public int DefaultContextTokens { get; set; } = 200_000;

    /// <summary>
    /// Substring → token ceiling, matched case-insensitively against the model id
    /// (e.g. <c>"[1m]" → 1_000_000</c> for a 1M-beta model string). When several keys match,
    /// the longest key wins; a length tie takes the larger ceiling so a 1M-beta marker is not
    /// beaten by an equally-short family token like "opus".
    /// </summary>
    public Dictionary<string, int> ModelOverrides { get; set; } = new();

    /// <summary>
    /// Resolve the ceiling for <paramref name="modelId"/>. An empty/null id, or no matching
    /// override, falls through to <see cref="DefaultContextTokens"/>.
    /// </summary>
    public int ResolveCeiling(string? modelId)
    {
        var fallback = DefaultContextTokens > 0 ? DefaultContextTokens : 200_000;
        return ResolveOverrideOrNull(modelId) ?? fallback;
    }

    /// <summary>
    /// The matching <see cref="ModelOverrides"/> ceiling, or null when no key matches.
    /// Extracted so CARD-0157 can insert a catalog self-reported constant between the
    /// operator override and the 200K default: override beats catalog beats default.
    /// </summary>
    public int? ResolveOverrideOrNull(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId) || ModelOverrides.Count == 0)
            return null;

        string? bestKey = null;
        var bestTokens = 0;
        foreach (var (key, tokens) in ModelOverrides)
        {
            if (key.Length == 0 || tokens <= 0)
                continue;
            if (!modelId.Contains(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (bestKey is null
                || key.Length > bestKey.Length
                || (key.Length == bestKey.Length && tokens > bestTokens))
            {
                bestKey = key;
                bestTokens = tokens;
            }
        }

        return bestKey is not null ? bestTokens : null;
    }
}
