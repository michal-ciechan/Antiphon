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
}
