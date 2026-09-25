using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Provider allowlist for transcript turns that are housekeeping, not a task prompt
/// (CARD-0714). The shapes are measured protocol text. Anything else stays a real prompt.
/// </summary>
internal static class TaskReportHousekeeping
{
    internal enum Shape
    {
        None = 0,
        GrokBackgroundCompletion = 1,
        ClaudeTaskNotification = 2,
    }

    /// <summary>
    /// Grok background-completion envelope, or a Claude notification, for
    /// <paramref name="provider"/>. Codex has no prompt-text exemption.
    /// </summary>
    internal static Shape Classify(AgentKind provider, string? text)
    {
        if (provider == AgentKind.ClaudeCode && IsClaudeTaskNotification(text))
            return Shape.ClaudeTaskNotification;
        if (provider == AgentKind.Grok && IsMeasuredCompletionEnvelope(text))
            return Shape.GrokBackgroundCompletion;
        return Shape.None;
    }

    internal static bool IsAllowlistedPrompt(AgentKind provider, string? text, IReadOnlySet<Guid> backedRulesIds)
    {
        // The measured completion envelope is Grok's shape. A backed rules header is
        // housekeeping on every provider: the refresh is typed into Claude sessions too.
        if (provider == AgentKind.Grok && IsMeasuredCompletionEnvelope(text))
            return true;
        return TryReadRulesMessageId(text) is Guid id && backedRulesIds.Contains(id);
    }

    internal static bool IsClaudeTaskNotification(string? text) =>
        TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, text);

    /// <summary>
    /// The queue id inside a leading <c>[antiphon-grok-rules:&lt;id&gt;]</c> header, or null.
    /// Backing is a separate queue-row check; the header alone is not housekeeping.
    /// </summary>
    internal static Guid? TryReadRulesMessageId(string? text)
    {
        if (text is null || !text.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal))
            return null;
        var close = text.IndexOf(']');
        if (close < 21 || !Guid.TryParseExact(text[21..close], "N", out var id))
            return null;
        return id;
    }

    /// <summary>
    /// Entire trimmed <c>&lt;system-reminder&gt;</c> envelope whose body is the measured
    /// three-line background-completion shape. CRLF is normalized. Outer whitespace is
    /// ignored. Internal whitespace is not.
    /// </summary>
    internal static bool IsMeasuredCompletionEnvelope(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        const string open = "<system-reminder>";
        const string close = "</system-reminder>";
        if (!normalized.StartsWith(open, StringComparison.Ordinal)
            || !normalized.EndsWith(close, StringComparison.Ordinal))
            return false;

        var body = normalized[open.Length..^close.Length].Trim();
        if (body.Contains(open, StringComparison.Ordinal) || body.Contains(close, StringComparison.Ordinal))
            return false;

        var lines = body.Split('\n');
        if (lines.Length != 3)
            return false;

        if (!TryReadCompletionId(lines[0].Trim(), out var opened))
            return false;
        if (!IsDescriptionLine(lines[1].Trim()))
            return false;
        if (!TryReadOutputId(lines[2].Trim(), out var closed))
            return false;
        return opened.Length > 0 && opened.Equals(closed, StringComparison.Ordinal);
    }

    private static bool TryReadCompletionId(string line, out string id)
    {
        id = "";
        const string prefix = "Background task \"";
        const string middle = "\" completed (exit code: ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var idEnd = line.IndexOf(middle, prefix.Length, StringComparison.Ordinal);
        if (idEnd <= prefix.Length)
            return false;
        id = line[prefix.Length..idEnd];
        if (id.Length == 0)
            return false;
        var rest = line[(idEnd + middle.Length)..];
        if (!rest.EndsWith(").", StringComparison.Ordinal))
            return false;
        return IsInteger(rest[..^2]);
    }

    private static bool IsDescriptionLine(string line)
    {
        const string prefix = "Description: ";
        const string marker = " | Duration: ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var at = line.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
        if (at <= prefix.Length)
            return false;
        var duration = line[(at + marker.Length)..];
        if (duration.Length < 2 || duration[^1] != 's')
            return false;
        return IsDurationNumber(duration[..^1]);
    }

    private static bool TryReadOutputId(string line, out string id)
    {
        id = "";
        const string prefix = "Use get_command_or_subagent_output(\"";
        const string suffix = "\") to see the full output.";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        id = line[prefix.Length..^suffix.Length];
        return id.Length > 0 && !id.Contains('"', StringComparison.Ordinal);
    }

    private static bool IsInteger(string value)
    {
        if (value.Length == 0)
            return false;
        var i = value[0] == '-' ? 1 : 0;
        if (i == value.Length)
            return false;
        for (; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]))
                return false;
        }

        return true;
    }

    private static bool IsDurationNumber(string value)
    {
        if (value.Length == 0 || value[0] == '.' || value[^1] == '.')
            return false;
        var dots = 0;
        foreach (var ch in value)
        {
            if (ch == '.')
            {
                dots++;
                if (dots > 1)
                    return false;
                continue;
            }

            if (!char.IsDigit(ch))
                return false;
        }

        return true;
    }
}
