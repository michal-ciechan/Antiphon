using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// CARD-1508. A fatal pre-prompt Codex account/read bootstrap timeout.
/// All three phrases must be present. A bare <c>-32603</c> is not this failure
/// and is not a sign-in or quota hold.
/// </summary>
public static class CodexAccountBootstrap
{
    public const string Marker = "account/read failed during TUI bootstrap";
    public const string DiscoveryTimeout = "workspace routing discovery timed out";
    public const string JsonRpcCode = "-32603";
    public const int DiagnosticMaxChars = 240;
    public const int OutcomeMaxChars = 500;

    public static bool IsFatal(string? rendered, string? raw) =>
        ContainsAll(Flatten(rendered)) || ContainsAll(Flatten(raw));

    /// <summary>
    /// The account/read sentence only. A welcome banner, cwd, or other screen text stays out.
    /// </summary>
    public static string? TryDiagnostic(string? rendered, string? raw)
    {
        var fromRendered = Extract(Flatten(rendered));
        if (fromRendered is not null)
            return Bound(fromRendered, DiagnosticMaxChars);
        var fromRaw = Extract(Flatten(raw));
        return fromRaw is null ? null : Bound(fromRaw, DiagnosticMaxChars);
    }

    /// <summary>
    /// Null unless classification is <see cref="CodexStartupReason.AccountBootstrap"/>.
    /// A rendered sign-in screen wins, so the raw bootstrap sentence is not copied out.
    /// </summary>
    public static string? TryOutcome(
        string? rendered,
        string? raw,
        string? notReadyDiagnostic,
        string? capturePath,
        int? exitCode)
    {
        if (CodexStartupScreen.Classify(rendered, raw).Reason != CodexStartupReason.AccountBootstrap)
            return null;
        var sentence = TryDiagnostic(rendered, raw);
        if (sentence is null)
            return null;
        return FormatOutcome(sentence, TryReadElapsedMs(notReadyDiagnostic), capturePath, exitCode);
    }

    public static string FormatOutcome(string diagnostic, int? elapsedMs, string? capturePath, int? exitCode)
    {
        var elapsed = elapsedMs?.ToString() ?? "unknown";
        var capture = string.IsNullOrWhiteSpace(capturePath) ? "unavailable" : capturePath.Trim();
        var text = $"AccountBootstrap: {diagnostic}; elapsedMs={elapsed}; capture={capture}";
        if (exitCode is int code)
            text += $"; exitCode={code}";
        return Bound(text, OutcomeMaxChars);
    }

    /// <summary>
    /// Keeps an existing process-exit sentence and appends <paramref name="outcome"/> once.
    /// A blank head is synthesized so the exit code is not dropped.
    /// </summary>
    public static string MergeFailureReason(string? existing, string outcome, int? exitCode)
    {
        var clause = outcome.Trim();
        var head = string.IsNullOrWhiteSpace(existing)
            ? $"Process exited (ProcessExited, code {(exitCode is int known ? known.ToString() : "unknown")})."
            : existing.Trim();
        if (head.Contains(clause, StringComparison.Ordinal))
            return head;
        if (exitCode is int code && !MentionsCode(head, code) && !MentionsCode(clause, code))
            head += $" exit code {code}.";
        return head + " " + clause;
    }

    public static string Flatten(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool ContainsAll(string flat) =>
        flat.Contains(Marker, StringComparison.Ordinal)
        && flat.Contains(DiscoveryTimeout, StringComparison.Ordinal)
        && flat.Contains(JsonRpcCode, StringComparison.Ordinal);

    private static string? Extract(string flat)
    {
        if (!ContainsAll(flat))
            return null;
        var markerAt = flat.IndexOf(Marker, StringComparison.Ordinal);
        var start = markerAt;
        const string errorPrefix = "Error: ";
        if (markerAt >= errorPrefix.Length
            && flat.AsSpan(markerAt - errorPrefix.Length, errorPrefix.Length).SequenceEqual(errorPrefix))
        {
            start = markerAt - errorPrefix.Length;
        }

        var codeAt = flat.IndexOf(JsonRpcCode, markerAt, StringComparison.Ordinal);
        if (codeAt < 0)
            codeAt = flat.IndexOf(JsonRpcCode, StringComparison.Ordinal);
        var end = codeAt + JsonRpcCode.Length;
        if (end < flat.Length && flat[end] == ')')
            end++;
        if (end <= start)
            return null;
        return flat[start..end];
    }

    private static int? TryReadElapsedMs(string? diagnostic)
    {
        if (string.IsNullOrEmpty(diagnostic))
            return null;
        const string key = "elapsedMs=";
        var at = diagnostic.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
            return null;
        var start = at + key.Length;
        var end = start;
        while (end < diagnostic.Length && char.IsDigit(diagnostic[end]))
            end++;
        if (end == start)
            return null;
        return int.TryParse(diagnostic.AsSpan(start, end - start), out var elapsed) ? elapsed : null;
    }

    private static bool MentionsCode(string text, int code) =>
        text.Contains($"code {code}", StringComparison.Ordinal)
        || text.Contains($"exitCode={code}", StringComparison.Ordinal);

    private static string Bound(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        if (cut < 0)
            cut = 0;
        return string.Concat(text.AsSpan(0, cut), "…");
    }
}
