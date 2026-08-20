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
/// from previously submitted pastes, so only a NEW one counts).
///
/// <para><b>On the modern pseudoconsole the placeholder arm stops being the fallback and becomes
/// the usual case</b> (CARD-0037). With the bracketed-paste markers actually reaching the TUI, a
/// large multi-line body is no longer typed — it is PASTED, and the composer collapses it to
/// <c>[Pasted text #N +M lines]</c> alone. Neither the head nor the tail of the body is on the
/// screen at all, by design, so head-or-tail matching would report a perfectly healthy 43 KB
/// delivery as "no composer evidence", withhold the Enter, revert the message and — for an
/// always-on agent — kill the session as wedged. Every large delivery, every time.</para>
///
/// <para>Counting placeholders was not enough for that load. The count is taken from the RENDERED
/// SCREEN, which holds only the visible rows: a tall paste pushes the previous placeholder off the
/// top as it adds its own, leaving the count equal and the delivery unverifiable. The placeholders
/// are therefore identified by their <c>#N</c> index — Claude numbers them per session and never
/// reuses one — so evidence is an index that was not there before. The count comparison is kept
/// underneath as a fallback for a placeholder whose index cannot be read.</para>
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

        // A collapsed paste shows none of the body. Match on the placeholder's own identity: an
        // index that was not on the screen before is a paste this delivery caused, and unlike a
        // count it survives the older placeholder scrolling out of the viewport.
        var indicesBefore = PlaceholderIndices(screenBefore);
        if (PlaceholderIndices(screenAfter).Any(i => !indicesBefore.Contains(i)))
            return true;

        var before = Normalize(screenBefore);
        return CountOccurrences(after, PastePlaceholder) > CountOccurrences(before, PastePlaceholder);
    }

    /// <summary>
    /// Is a short, self-chosen <paramref name="fragment"/> on the rendered screen, by the SAME
    /// normalisation and window matching <see cref="IsVisible"/> applies to a body's head/tail?
    ///
    /// <para>CARD-0103's input probe is the caller: it writes a ~10-character token and asks whether
    /// the TUI actually rendered it, which is the only signal that distinguishes a composer that is
    /// painted from one that is READING. A token that short takes the direct-contains arm of
    /// <see cref="FragmentVisible"/>, so this is <see cref="IsVisible"/>'s first arm and nothing
    /// else — deliberately without the paste-placeholder arms, which exist for a collapsed body and
    /// would answer "yes" to a placeholder left over from someone else's paste.</para>
    ///
    /// <para>Exposed rather than reimplemented: the matching rules were replayed correct against
    /// the failing session's own ANSI log (CARD-0103's investigation) and there must not be a second
    /// copy of them to drift.</para>
    /// </summary>
    public static bool FragmentIsVisible(string screen, string fragment)
    {
        var needle = Normalize(fragment);
        if (needle.Length == 0)
            return true;
        var haystack = Normalize(screen);
        return haystack.Length != 0 && FragmentVisible(haystack, needle);
    }

    /// <summary>
    /// The <c>#N</c> of every <c>[Pasted text #N +M lines]</c> on a rendered screen. Read from the
    /// RAW screen, not the whitespace-stripped form, because the number is what identifies the
    /// paste and the surrounding spaces are what delimit it. Tolerant of the composer's wrapping:
    /// only "Pasted text #" followed by digits has to survive on one row, which it does — the
    /// placeholder is short and the composer never breaks it across the ~20 characters involved in
    /// any capture taken so far.
    /// </summary>
    private static HashSet<int> PlaceholderIndices(string screen)
    {
        var found = new HashSet<int>();
        if (string.IsNullOrEmpty(screen))
            return found;

        const string prefix = "asted text #";
        for (var i = screen.IndexOf(prefix, StringComparison.Ordinal);
             i >= 0;
             i = screen.IndexOf(prefix, i + prefix.Length, StringComparison.Ordinal))
        {
            var at = i + prefix.Length;
            var value = 0;
            var digits = 0;
            while (at < screen.Length && char.IsAsciiDigit(screen[at]))
            {
                value = value * 10 + (screen[at] - '0');
                at++;
                digits++;
            }
            if (digits > 0)
                found.Add(value);
        }
        return found;
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
