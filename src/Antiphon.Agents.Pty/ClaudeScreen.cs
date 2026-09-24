using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Separates the Claude TUI's CONSTANTLY-ANIMATING chrome from its actual content.
///
/// This is what makes "has the screen settled?" a usable signal. Two things defeat naive
/// approaches against current builds:
///
///  * the pty never goes quiet — the status line animates continuously (spinner glyph, a randomly
///    chosen gerund, an elapsed counter, a live token count), so a quiet-window detector waits out
///    its whole budget on a turn that finished minutes ago;
///  * two consecutive raw snapshots are never equal for the same reason, so comparing snapshots
///    directly can never report stability either.
///
/// Strip the animated parts and both problems go away: what remains changes only when the model
/// actually writes something.
/// </summary>
public static partial class ClaudeScreen
{
    /// <summary>
    /// The screen with every animating element removed. Two calls a second apart return the same
    /// string when the model is idle, and differ when it is producing output.
    /// </summary>
    public static string Stable(string screen)
    {
        if (string.IsNullOrEmpty(screen))
            return string.Empty;

        var builder = new StringBuilder(screen.Length);
        foreach (var raw in screen.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = StripLine(raw);
            if (line.Length == 0)
                continue;
            builder.Append(line).Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>True when both snapshots have the same non-animating content.</summary>
    public static bool IsSettled(string before, string after) =>
        string.Equals(Stable(before), Stable(after), StringComparison.Ordinal);

    /// <summary>
    /// True while the model is visibly working. The interrupt hint is the only reliable marker:
    /// an elapsed counter also appears on COMPLETED tool annotations ("Listing 1 directory… (21s)")
    /// which never leave the screen, so keying on it makes a turn look like it runs forever.
    /// </summary>
    public static bool IsWorking(string screen) =>
        Compact(screen).Contains("esctointerrupt", StringComparison.Ordinal);

    /// <summary>True when the composer is present and accepting input.</summary>
    public static bool ComposerIsLive(string screen)
    {
        var compact = Compact(screen);
        return compact.Contains("forshortcuts", StringComparison.Ordinal)
            || compact.Contains("bypasspermissionson", StringComparison.Ordinal);
    }

    /// <summary>
    /// CARD-0650 S4 repair 3. The text inside Claude's bottom composer, when the screen shows one
    /// that can be read: the rows between the last two horizontal rules (or the last rounded box),
    /// the first of them carrying the prompt glyph, with at most a few hint rows under the lower
    /// rule. The glyph, box sides and surrounding blanks are removed. False when no such region is
    /// on screen (a mid-redraw frame, a menu open under the composer, a composer taller than the
    /// screen); the caller then has no composer evidence and must not treat the composer as empty.
    /// </summary>
    public static bool TryReadComposer(string screen, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(screen))
            return false;

        var rows = screen.ReplaceLineEndings("\n").Split('\n');
        var bottom = LastRuleRow(rows, rows.Length - 1);
        if (bottom < 0)
            return false;
        var below = 0;
        for (var r = bottom + 1; r < rows.Length; r++)
        {
            if (rows[r].Trim().Length > 0)
                below++;
        }
        if (below > MaxHintRowsBelowComposer)
            return false;
        var top = LastRuleRow(rows, bottom - 1);
        if (top < 0 || bottom - top < 2)
            return false;

        var first = rows[top + 1].Trim().Trim('│').Trim();
        if (first.Length == 0 || first[0] is not ('❯' or '>'))
            return false;

        var lines = new List<string> { first[1..].Trim() };
        for (var r = top + 2; r < bottom; r++)
            lines.Add(rows[r].Trim().Trim('│').Trim());
        content = string.Join('\n', lines).Trim();
        return true;
    }

    /// <summary>
    /// True when <see cref="TryReadComposer"/>'s content holds no input: blank, or only Claude's
    /// idle suggestion (<c>Try "how do I log an error?"</c>).
    /// </summary>
    public static bool ComposerContentIsEmpty(string content)
    {
        var text = content.Trim();
        return text.Length == 0
            || (!text.Contains('\n')
                && text.StartsWith("Try \"", StringComparison.Ordinal)
                && text.EndsWith('"'));
    }

    /// <summary>The hint bar, and at most a usage banner and an effort row, sit under the composer.</summary>
    private const int MaxHintRowsBelowComposer = 3;

    private static int LastRuleRow(string[] rows, int from)
    {
        for (var r = from; r >= 0; r--)
        {
            if (IsRuleRow(rows[r]))
                return r;
        }
        return -1;
    }

    /// <summary>
    /// A full rule ("────"), a labelled one ("──── task-f418105f ─"), or a rounded box edge
    /// ("╭────╮", "╰────╯"): mostly '─', starting and ending on rule characters.
    /// </summary>
    private static bool IsRuleRow(string row)
    {
        var t = row.Trim();
        if (t.Length < 10 || t[0] is not ('─' or '╭' or '╰') || t[^1] is not ('─' or '╮' or '╯'))
            return false;
        var dashes = t.Count(c => c == '─');
        return dashes * 2 >= t.Length;
    }

    private static string StripLine(string raw)
    {
        var line = raw.TrimEnd();
        if (line.Trim().Length == 0)
            return string.Empty;

        // The status/spinner row: a spinner glyph, a randomly chosen gerund, and a live counter.
        // Nothing on it is content, and every part of it changes between frames.
        if (SpinnerLineRegex().IsMatch(line))
            return string.Empty;

        // Elapsed/token counters that ride ALONGSIDE real content — "(4s · ↓ 126 tokens)" appended
        // to a tool annotation. Drop the counter, keep the annotation.
        line = CounterRegex().Replace(line, string.Empty);

        // Hint bars and footers that toggle between variants as modes change.
        var compact = Compact(line);
        if (compact.Contains("esctointerrupt", StringComparison.Ordinal)
            || compact.Contains("forshortcuts", StringComparison.Ordinal)
            || compact.Contains("ctrlgtoedit", StringComparison.Ordinal)
            || compact.Contains("shifttabtocycle", StringComparison.Ordinal)
            || compact.Contains("effort", StringComparison.Ordinal) && compact.Contains("xhigh", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Box drawing carries no content and reflows as panels resize.
        var trimmed = line.Trim(' ', '─', '│', '╭', '╮', '╰', '╯', '━', '═');
        return trimmed.Length == 0 ? string.Empty : trimmed;
    }

    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// A spinner row: an animation glyph, then a word ending in the ellipsis Claude uses for its
    /// working verbs ("Inferring…", "Scurrying…", "Cultivating…" — the verb is chosen at random per
    /// frame, so matching the WORD is hopeless; match the shape).
    /// </summary>
    [GeneratedRegex(@"^\s*[✻✽✢✳✶✷·●○◐◓◑◒*∗⁕⏵▪]+\s*\S*…", RegexOptions.Compiled)]
    private static partial Regex SpinnerLineRegex();

    /// <summary>"(4s · ↓ 126 tokens)", "(21s)", "(1m 4s · ↑ 12 tokens)".</summary>
    [GeneratedRegex(@"\(\s*\d+[a-z]?\s*[a-z]*\s*(?:[·|].*?)?\)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CounterRegex();
}
