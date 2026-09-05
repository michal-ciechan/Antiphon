using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Frozen reply-contract semantics between an agent's turn output and the channel dispatcher.
/// </summary>
public static partial class ChannelContracts
{
    /// <summary>The token an agent replies with to suppress channel delivery for a turn.</summary>
    public const string NoReplyToken = "NO_REPLY";

    /// <summary>
    /// True when a turn's response is a silent turn: the ENTIRE response, trimmed, is exactly
    /// <see cref="NoReplyToken"/> (case-insensitive). Leading or trailing prose defeats it —
    /// a real answer that merely mentions NO_REPLY must still be delivered.
    /// </summary>
    public static bool IsNoReply(string? turnResponse) =>
        turnResponse is not null
        && string.Equals(turnResponse.Trim(), NoReplyToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The attachment marker an agent puts on its own line to send a file down the channel:
    /// <c>[[attach: C:\absolute\path\to\file.pdf]]</c>. Documented in the Telegram preamble preset.
    /// </summary>
    public const string AttachMarkerFormat = "[[attach: <absolute file path>]]";

    [GeneratedRegex(@"^\s*\[\[attach:\s*(?<path>[^\]]+?)\s*\]\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AttachMarkerRegex();

    /// <summary>
    /// Splits a turn response into its deliverable text and the attachment paths the agent marked.
    /// Marker lines are removed from the text; blank runs the removal leaves behind are collapsed.
    /// A response that was ONLY markers yields empty text (still a deliverable turn — the files ARE
    /// the reply). Paths come back verbatim (unvalidated) — existence/size checks are the caller's.
    /// </summary>
    public static (string Text, IReadOnlyList<string> AttachmentPaths) ExtractAttachments(string turnResponse)
    {
        var paths = new List<string>();
        var text = AttachMarkerRegex().Replace(turnResponse, m =>
        {
            var path = m.Groups["path"].Value.Trim();
            if (path.Length > 0)
                paths.Add(path);
            return string.Empty;
        });

        if (paths.Count == 0)
            return (turnResponse, paths);

        // Collapse the 3+ newline runs that removing whole marker lines leaves behind.
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return (text, paths);
    }

    /// <summary>
    /// Tokens that mark an Antiphon-composed injection. Contains, not StartsWith: a SUPERSEDED
    /// check note opens with the banner, and Grok joins that onto the <c>[check</c> header.
    /// Channel envelopes (<c>[Telegram</c> / <c>[Slack</c> / <c>[Discord</c>) do not collide.
    /// </summary>
    private static readonly string[] InjectionPromptTokens =
    [
        "[task ",
        "[check ",
        "[antiphon-",
        "[scheduled:",
        "[System note from Antiphon:",
        "[session ",
    ];

    /// <summary>
    /// True when <paramref name="promptText"/> looks like an Antiphon injection (completion /
    /// check / brief / scheduled / system / session-tagged note), not a channel envelope or
    /// operator-typed turn.
    /// </summary>
    public static bool IsAntiphonInjectionPrompt(string? promptText)
    {
        if (string.IsNullOrEmpty(promptText))
            return false;
        foreach (var token in InjectionPromptTokens)
        {
            if (promptText.Contains(token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>8-hex ids parsed from <c>[task …]</c> / <c>[check …]</c> headers in a prompt.</summary>
    public readonly record struct InjectionShortIds(
        IReadOnlySet<string> TaskIds,
        IReadOnlySet<string> CheckIds);

    [GeneratedRegex(@"\[task ([0-9a-fA-F]{8})\b")]
    private static partial Regex TaskInjectionShortIdRegex();

    [GeneratedRegex(@"\[check ([0-9a-fA-F]{8})\b")]
    private static partial Regex CheckInjectionShortIdRegex();

    /// <summary>
    /// Collects 8-hex ids from <c>[task &lt;8-hex&gt;</c> and <c>[check &lt;8-hex&gt;</c> in the
    /// owning prompt. Does not treat <c>[antiphon-task:]</c> as a task-id match.
    /// </summary>
    public static InjectionShortIds CollectInjectionShortIds(string? promptText)
    {
        var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(promptText))
            return new InjectionShortIds(taskIds, checkIds);

        foreach (Match match in TaskInjectionShortIdRegex().Matches(promptText))
            taskIds.Add(match.Groups[1].Value.ToLowerInvariant());
        foreach (Match match in CheckInjectionShortIdRegex().Matches(promptText))
            checkIds.Add(match.Groups[1].Value.ToLowerInvariant());
        return new InjectionShortIds(taskIds, checkIds);
    }

    /// <summary>
    /// First non-empty line of the queued body after CRLF → <c>\n</c> + trim. The stable header;
    /// never the report body, deliverable block, or excerpt banner.
    /// </summary>
    public static string HeaderProbe(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return "";
        var normalized = body.ReplaceLineEndings("\n").Trim();
        if (normalized.Length == 0)
            return "";
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length > 0)
                return line;
        }

        return "";
    }
}
