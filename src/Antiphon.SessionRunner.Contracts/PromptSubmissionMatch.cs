using System.Text;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// "Is this transcript record the prompt we typed?" — the one text-identity rule shared by the two
/// places that ask it, in opposite directions:
///
/// <list type="bullet">
/// <item>CARD-0006 rule C4 (runner, <c>SessionInputLog</c>): does a candidate transcript's prompt
/// appear in what this session was actually sent? Positive evidence that the file is ours.</item>
/// <item>CARD-0055 delivery confirmation (server, <c>SessionMessageQueueService</c>): does a
/// <c>UserPrompt</c> row that arrived after we typed carry the body we typed? Positive evidence
/// that the prompt was really submitted, rather than merely redrawn on screen.</item>
/// </list>
///
/// It lives in Contracts because the server references Contracts and not the runner project, and
/// because the two callers MUST stay in lockstep: a normalization the runner tightens without the
/// server is a delivery that silently stops confirming (or, worse, starts confirming a stranger's
/// text). The rules themselves were earned against real Claude for C4 and are not re-derived here.
///
/// The head window is deliberate on the delivery side (D1): the pty's known loss mode keeps TAILS
/// (CARD-0027), so matching a tail could certify a clipped body as delivered whole. Matching the
/// head cannot — the head is the first thing lost when a body is clipped, and a batch body's head
/// is stable framing that Claude records verbatim.
/// </summary>
public static class PromptSubmissionMatch
{
    /// <summary>
    /// Shortest text that may identify a prompt. Short strings ("y", "ok", "Continue.") occur in
    /// unrelated conversations often enough to be worthless as identification — a body below this
    /// takes the weak arm (see <see cref="IsConfirmedBy"/>) rather than a text match.
    /// </summary>
    public const int MinMatchChars = 12;

    /// <summary>
    /// How much of a long prompt is compared. The runner's input log is bounded, so a whole 40 KB
    /// brief cannot be; on the delivery side a bounded compare is what keeps the match cheap and
    /// keeps it anchored at the head.
    /// </summary>
    public const int MatchWindowChars = 200;

    private const char Esc = '\u001b';

    /// <summary>
    /// Strips ANSI/escape sequences (including the bracketed-paste wrappers the delivery path adds),
    /// drops the remaining control characters, and collapses every whitespace run to one space.
    /// Line endings therefore stop mattering — which they must: the delivery path rewrites them
    /// (<c>ReplaceLineEndings("\n")</c> + a separate submit <c>\r</c>) and the TUI may re-wrap the
    /// body again before Claude persists it.
    /// </summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == Esc)
            {
                i = SkipEscapeSequence(text, i);
                continue;
            }

            if (char.IsWhiteSpace(c) || char.IsControl(c))
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

    /// <summary>
    /// Returns the index of the LAST char of the escape sequence starting at <paramref name="start"/>
    /// (a caller's for-loop increments past it). Handles CSI (ESC [ ... final @-~), OSC (ESC ] ...
    /// BEL or ESC \) and the two-character forms; an unterminated sequence swallows the rest of the
    /// string, which is the right call for a truncated write.
    /// </summary>
    public static int SkipEscapeSequence(string text, int start)
    {
        var i = start + 1;
        if (i >= text.Length)
            return i;

        var kind = text[i];
        if (kind == '[')
        {
            for (i++; i < text.Length; i++)
            {
                if (text[i] >= '@' && text[i] <= '~')
                    return i;
            }
            return text.Length;
        }

        if (kind == ']')
        {
            for (i++; i < text.Length; i++)
            {
                if (text[i] == '\a')
                    return i;
                if (text[i] == Esc && i + 1 < text.Length && text[i + 1] == '\\')
                    return i + 1;
            }
            return text.Length;
        }

        return i; // ESC + one char (e.g. ESC 7, ESC =)
    }

    /// <summary>
    /// Normalizes <paramref name="text"/> and clips it to the head window, yielding the string that
    /// is actually compared. False — no needle — when the normalized text is shorter than
    /// <see cref="MinMatchChars"/>: too short to identify anything, so the caller must decide what
    /// a non-identifiable body means (C4 refuses; delivery confirmation takes the weak arm).
    /// </summary>
    public static bool TryBuildNeedle(string? text, out string needle)
    {
        needle = string.Empty;
        if (string.IsNullOrEmpty(text))
            return false;

        var normalized = Normalize(text);
        if (normalized.Length < MinMatchChars)
            return false;

        needle = normalized.Length > MatchWindowChars ? normalized[..MatchWindowChars] : normalized;
        return true;
    }

    /// <summary>
    /// True when <paramref name="body"/> is identifiable by text at all — i.e. a text match is
    /// available and a bare "some record showed up" would be a weaker claim. Callers log the
    /// difference so a weak confirmation is never mistaken for a strong one.
    /// </summary>
    public static bool RequiresTextMatch(string? body) => TryBuildNeedle(body, out _);

    /// <summary>
    /// CARD-0055's confirmation rule: does <paramref name="recordText"/> — the text of a
    /// <c>UserPrompt</c> transcript record that arrived after we started typing — carry the body we
    /// typed?
    ///
    /// Strong arm: the normalized record contains the normalized body's HEAD window. This is what
    /// rejects the measured 15c9150e shape, where a new record DID appear carrying the previous
    /// delivery's stale body while ours died in the composer — arrival is not confirmation, text is.
    ///
    /// Weak arm: a body too short to identify (<see cref="MinMatchChars"/>) — the auto-continue
    /// "Continue." and friends — is confirmed by the record's existence alone. Weaker than a text
    /// match, but strictly stronger than the screen-redraw signal it replaces.
    /// </summary>
    public static bool IsConfirmedBy(string? body, string? recordText)
    {
        if (!TryBuildNeedle(body, out var needle))
            return true; // weak arm: nothing distinctive to look for; the record itself is the evidence

        if (string.IsNullOrEmpty(recordText))
            return false;

        return Normalize(recordText).Contains(needle, StringComparison.Ordinal);
    }
}
