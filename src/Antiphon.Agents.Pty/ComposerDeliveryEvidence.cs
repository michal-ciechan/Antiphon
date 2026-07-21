using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Decides whether a message body that was just typed into Claude's composer is actually visible
/// on the rendered screen — the delivery-time liveness check that replaced the periodic TUI echo
/// probe (which false-positive-killed healthy sessions on 2026-07-20 by asking "did ANYTHING
/// change?" instead of "is the expected content there?").
///
/// The rendering contract is pinned by <c>ClaudeComposerRenderCanaryTests</c> (observed against
/// real Claude, 2026-07-21):
///  * short single line  → rendered verbatim.
///  * huge single line   → only the SUFFIX near the cursor stays visible (start scrolls away).
///  * multi-line body    → non-deterministic: either the PREFIX lines plus a
///    "[Pasted text #N +X lines]" placeholder, or the TAIL lines with no placeholder.
///
/// Evidence is therefore: the body's tail fragment, OR its head fragment, OR a paste placeholder
/// that was NOT already on the screen before typing (transcript history can contain placeholders
/// from previously submitted pastes, so only an INCREASE counts).
///
/// Matching must survive two rendering hazards observed on real screens:
///  * the composer wraps long lines at arbitrary points — including mid-token — and rows are
///    trimmed of trailing spaces, so ALL whitespace is stripped before comparing; and
///  * mid-scroll captures can interleave GHOST rows (stale prompt hints, border rows) inside the
///    wrapped body text, so a single contiguous substring match is too brittle. Instead the
///    head/tail fragments are split into small windows and a quorum of windows must be present —
///    an interleaved artifact row breaks at most the one window it lands in.
/// TUI chrome (box-drawing characters, the prompt glyph) is stripped along with whitespace.
/// </summary>
public static class ComposerDeliveryEvidence
{
    /// <summary>How much of the body's head/tail is considered (after normalisation).</summary>
    public const int FragmentSpan = 40;

    /// <summary>Window size for quorum matching within a fragment.</summary>
    public const int WindowLength = 10;

    // Whitespace-stripped form of the "[Pasted text #N +X lines]" placeholder's stable core.
    private const string PastePlaceholder = "astedtext";

    /// <summary>
    /// True when <paramref name="screenAfter"/> shows evidence that <paramref name="body"/> landed
    /// in the composer. <paramref name="screenBefore"/> is the rendered screen captured before the
    /// body was typed — used to discount paste placeholders that were already visible.
    /// </summary>
    public static bool IsVisible(string screenBefore, string screenAfter, string body)
    {
        var normalizedBody = Normalize(body);
        if (normalizedBody.Length == 0)
            return true; // nothing to verify

        var after = Normalize(screenAfter);
        if (after.Length == 0)
            return false;

        var tail = normalizedBody.Length <= FragmentSpan
            ? normalizedBody
            : normalizedBody[^FragmentSpan..];
        if (FragmentVisible(after, tail))
            return true;

        var head = normalizedBody.Length <= FragmentSpan
            ? normalizedBody
            : normalizedBody[..FragmentSpan];
        if (FragmentVisible(after, head))
            return true;

        var before = Normalize(screenBefore);
        return CountOccurrences(after, PastePlaceholder) > CountOccurrences(before, PastePlaceholder);
    }

    // Sliding-window match: any WindowLength-sized window of the fragment (stride 5, walking from
    // the end so the cursor-adjacent characters are covered first) counts as evidence. A single
    // window suffices — observed on real Claude (2026-07-21): the SETTLED composer for a huge
    // single line shows only the final wrapped fragment (as little as ~14 characters), so any
    // multi-window quorum false-positives a wedge verdict on perfectly healthy deliveries. The
    // post-submit output-advance check is the second layer that catches what a 10-char
    // coincidence match would let through.
    private static bool FragmentVisible(string screen, string fragment)
    {
        if (fragment.Length <= WindowLength)
            return screen.Contains(fragment, StringComparison.Ordinal);

        for (var end = fragment.Length; end >= WindowLength; end -= WindowLength / 2)
        {
            var window = fragment.Substring(end - WindowLength, WindowLength);
            if (screen.Contains(window, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // Strip whitespace and TUI chrome so wrap points, trimmed rows, border rows and prompt glyphs
    // cannot break (or fake) a match. Applied to BOTH the screen and the body, so a body that
    // itself contains such characters stays consistent with what is searched for.
    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
                continue;
            if (c is >= '─' and <= '╿') // box drawing (composer borders)
                continue;
            if (c is '❯' or '…' or '⏵')
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
