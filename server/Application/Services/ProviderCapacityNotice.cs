using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0281: one channel-safe sentence for a terminal provider-capacity death. Provider kind +
/// alias + status + reason phrase + a bounded, scrubbed detail; never the raw <c>agent_result</c>,
/// never file paths, never a key-shaped token.
/// </summary>
public static partial class ProviderCapacityNotice
{
    public static string Format(
        AgentKind kind,
        string? alias,
        int? status,
        string? reasonPhrase,
        bool fallbackDeclared,
        string? detail = null)
    {
        var provider = ProviderLabel(kind);
        var who = string.IsNullOrWhiteSpace(alias) ? provider : $"{provider} {alias.Trim()}";
        var scrubbedDetail = Scrub(detail);
        var scrubbedPhrase = Scrub(reasonPhrase);

        var refusal = status is int s
            ? $"the {provider} provider refused the request (HTTP {s}{PhraseBit(scrubbedPhrase)}{DetailBit(scrubbedDetail)})"
            : $"the {provider} provider refused the request{PhraseBit(scrubbedPhrase, leadingDash: true)}{DetailBit(scrubbedDetail)}";

        var first = $"⚠️ I can't answer right now: {refusal}. Your message is kept.";
        var second = fallbackDeclared
            ? $"A fallback ({who}) is taking over; the next reply comes from it."
            : "Someone needs to restore capacity or clear the hold before I can continue.";
        return $"{first} {second}";
    }

    private static string ProviderLabel(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => "Claude",
        AgentKind.Grok => "Grok",
        AgentKind.Codex => "Codex",
        _ => kind.ToString(),
    };

    private static string PhraseBit(string? phrase, bool leadingDash = false)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return "";
        return leadingDash ? $" — {phrase}" : $" {phrase}";
    }

    private static string DetailBit(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "";
        return $" — {detail}";
    }

    /// <summary>
    /// Drop anything shaped like a key, bearer token, or URL. Bounded leftover.
    /// </summary>
    internal static string? Scrub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var scrubbed = SecretShapedRegex().Replace(text.Trim(), "…");
        if (scrubbed.Length > 80)
            scrubbed = scrubbed[..80].Trim();
        return string.IsNullOrWhiteSpace(scrubbed) ? null : scrubbed;
    }

    [GeneratedRegex(
        @"https?://\S+|bearer\s+\S+|xai-[A-Za-z0-9_-]+|sk-[A-Za-z0-9_-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretShapedRegex();
}
