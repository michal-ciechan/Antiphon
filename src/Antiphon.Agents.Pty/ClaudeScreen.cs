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
