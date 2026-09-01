using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Canonical family-alias vocabulary for <c>ModelAvailabilityHold.ModelAlias</c> (CARD-0022).
/// Pure: no clock, no database. The stub row's <c>message.model</c> is <c>&lt;synthetic&gt;</c>
/// — never pass that in as <paramref name="raw"/>.
/// </summary>
public static class ModelAlias
{
    /// <summary>CARD-0309 kind-wide hold. AutoDetected never writes this.</summary>
    public const string KindWide = "*";

    public const string Fable = "fable";
    public const string Opus = "opus";
    public const string Sonnet = "sonnet";
    public const string Haiku = "haiku";
    public const string Grok46 = "grok-4.6";
    public const string Gpt56Sol = "gpt-5.6-sol";
    public const string Gpt56Terra = "gpt-5.6-terra";
    public const string Gpt56Luna = "gpt-5.6-luna";

    /// <summary>
    /// Every alias <see cref="ModelLevelAliases"/> launches for <see cref="AgentTaskService.DelegatableKinds"/>.
    /// <c>ListAvailable</c> is this set minus currently held rows.
    /// </summary>
    public static readonly IReadOnlyList<(AgentKind Kind, string Alias)> DelegatableAliases =
    [
        (AgentKind.ClaudeCode, Fable),
        (AgentKind.ClaudeCode, Opus),
        (AgentKind.ClaudeCode, Sonnet),
        (AgentKind.ClaudeCode, Haiku),
        (AgentKind.Grok, Grok46),
        (AgentKind.Codex, Gpt56Sol),
        (AgentKind.Codex, Gpt56Terra),
        (AgentKind.Codex, Gpt56Luna),
    ];

    /// <summary>
    /// PUT/DELETE alias vocabulary (CARD-0309): a known <see cref="DelegatableAliases"/> value
    /// or <see cref="KindWide"/>, case-insensitive. Unknown text (including TUI names like
    /// <c>claude-fable-5</c>) returns null so the operator must use the canonical list.
    /// </summary>
    public static string? CanonicalHoldAlias(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed == KindWide)
            return KindWide;

        var lower = trimmed.ToLowerInvariant();
        foreach (var (_, alias) in DelegatableAliases)
        {
            if (lower == alias)
                return alias;
        }

        return null;
    }

    /// <summary>
    /// Map TUI / launch text onto a canonical alias. Unknown text returns null so the caller
    /// can fall back to the session's launch alias rather than pausing a guessed model.
    /// </summary>
    public static string? Normalize(AgentKind kind, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed == KindWide)
            return KindWide;

        var folded = Fold(trimmed);
        if (folded.Length == 0)
            return null;

        // Kind-agnostic: the hold table is keyed (Kind, Alias), so "fable" on a Grok row is
        // legal but unused. Recognition is by the string, not by kind, so a Claude stub that
        // names "Fable 5" still maps while the session kind is being resolved.
        if (IsFable(folded)) return Fable;
        if (IsOpus(folded)) return Opus;
        if (IsSonnet(folded)) return Sonnet;
        if (IsHaiku(folded)) return Haiku;
        if (IsGrok46(folded)) return Grok46;
        if (IsSol(folded)) return Gpt56Sol;
        if (IsTerra(folded)) return Gpt56Terra;
        if (IsLuna(folded)) return Gpt56Luna;

        // Already-canonical aliases survive a second pass.
        foreach (var (_, alias) in DelegatableAliases)
        {
            if (folded == alias)
                return alias;
        }

        _ = kind;
        return null;
    }

    /// <summary>Lowercase, collapse whitespace, turn separators into spaces.</summary>
    internal static string Fold(string raw)
    {
        Span<char> buffer = stackalloc char[raw.Length];
        var n = 0;
        var lastSpace = true;
        foreach (var ch in raw)
        {
            if (ch is '-' or '_' or '/' or '.')
            {
                if (!lastSpace)
                {
                    buffer[n++] = ' ';
                    lastSpace = true;
                }
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace)
                {
                    buffer[n++] = ' ';
                    lastSpace = true;
                }
                continue;
            }

            buffer[n++] = char.ToLowerInvariant(ch);
            lastSpace = false;
        }

        if (n > 0 && buffer[n - 1] == ' ')
            n--;
        return new string(buffer[..n]);
    }

    private static bool IsFable(string folded) =>
        folded is "fable" or "fable 5" or "claude fable" or "claude fable 5";

    private static bool IsOpus(string folded) =>
        folded is "opus" or "opus 5" or "claude opus" or "claude opus 5";

    private static bool IsSonnet(string folded) =>
        folded is "sonnet" or "sonnet 5" or "claude sonnet" or "claude sonnet 5";

    private static bool IsHaiku(string folded) =>
        folded is "haiku" or "haiku 4 5" or "haiku 45" or "claude haiku" or "claude haiku 4 5" or "claude haiku 45";

    private static bool IsGrok46(string folded) =>
        folded is "grok 4 6" or "grok 46" or "grok";

    private static bool IsSol(string folded) =>
        folded is "gpt 5 6 sol" or "gpt 56 sol" or "sol";

    private static bool IsTerra(string folded) =>
        folded is "gpt 5 6 terra" or "gpt 56 terra" or "terra";

    private static bool IsLuna(string folded) =>
        folded is "gpt 5 6 luna" or "gpt 56 luna" or "luna";
}
