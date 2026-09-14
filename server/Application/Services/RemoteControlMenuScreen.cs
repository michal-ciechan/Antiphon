using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0514 D-4: conservative recognizer for Claude Code's <c>/remote-control</c> management
/// menu. Actionable presence requires a coherent ordered block on the current rendered grid:
/// heading, Disconnect, Show QR, Continue, Esc footer. A partial remnant is never Esc authority
/// and never a clear composer. Match rendered grids, not append-only ANSI history.
/// </summary>
public static class RemoteControlMenuScreen
{
    public const string HeadingLiteral = "Remote Control";
    public const string DisconnectLiteral = "Disconnect this session";
    public const string ShowQrLiteral = "Show QR code";
    public const string ContinueLiteral = "Continue";
    public const string FooterLiteral = "Esc to continue";

    public readonly record struct Classification(bool IsPresent, bool HasRemnant)
    {
        public bool IsClear => !IsPresent && !HasRemnant;
    }

    public static bool IsPresent(string? renderedScreen) => Classify(renderedScreen).IsPresent;

    public static bool HasRemnant(string? renderedScreen) => Classify(renderedScreen).HasRemnant;

    public static bool IsClear(string? renderedScreen) => Classify(renderedScreen).IsClear;

    public static Classification Classify(string? renderedScreen)
    {
        if (string.IsNullOrEmpty(renderedScreen))
            return default;

        var lines = NormalizeLines(renderedScreen);
        if (lines.Count == 0)
            return default;

        var heading = IndexOfLine(lines, static line => LineEquals(line, HeadingLiteral), 0);
        var disconnect = heading >= 0
            ? IndexOfContains(lines, DisconnectLiteral, heading + 1)
            : -1;
        var showQr = disconnect >= 0
            ? IndexOfContains(lines, ShowQrLiteral, disconnect + 1)
            : -1;
        var cont = showQr >= 0
            ? IndexOfContinueRow(lines, showQr + 1)
            : -1;
        var footer = cont >= 0
            ? IndexOfContains(lines, FooterLiteral, cont + 1)
            : -1;

        if (heading >= 0 && disconnect >= 0 && showQr >= 0 && cont >= 0 && footer >= 0)
            return new Classification(IsPresent: true, HasRemnant: false);

        var anyToken = ContainsAnyMenuToken(lines);
        return anyToken
            ? new Classification(IsPresent: false, HasRemnant: true)
            : default;
    }

    internal static IReadOnlyList<string> NormalizeLines(string renderedScreen)
    {
        var result = new List<string>();
        foreach (var raw in renderedScreen.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var normalized = NormalizeLine(raw);
            if (normalized.Length == 0)
                continue;
            result.Add(normalized);
        }

        return result;
    }

    private static string NormalizeLine(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '\u2500' and <= '\u257F' or '\u2500' or '|' or '+' )
                continue;
            if (char.IsControl(ch) && ch is not '\t')
                continue;
            builder.Append(ch);
        }

        var collapsed = builder.ToString().Trim();
        if (collapsed.Length == 0)
            return "";
        builder.Clear();
        var previousSpace = false;
        foreach (var ch in collapsed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (previousSpace)
                    continue;
                builder.Append(' ');
                previousSpace = true;
                continue;
            }

            previousSpace = false;
            builder.Append(ch);
        }

        var line = builder.ToString().Trim();
        if (line.StartsWith("> ", StringComparison.Ordinal))
            line = line[2..].Trim();
        else if (line.StartsWith('>') && line.Length > 1 && !char.IsLetterOrDigit(line[1]))
            line = line[1..].Trim();
        return line;
    }

    private static bool LineEquals(string line, string expected) =>
        string.Equals(line, expected, StringComparison.Ordinal);

    private static int IndexOfLine(IReadOnlyList<string> lines, Func<string, bool> predicate, int start)
    {
        for (var i = start; i < lines.Count; i++)
        {
            if (predicate(lines[i]))
                return i;
        }

        return -1;
    }

    private static int IndexOfContains(IReadOnlyList<string> lines, string token, int start) =>
        IndexOfLine(lines, line => line.Contains(token, StringComparison.Ordinal), start);

    private static int IndexOfContinueRow(IReadOnlyList<string> lines, int start)
    {
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Contains(FooterLiteral, StringComparison.Ordinal))
                continue;
            if (LineEquals(line, ContinueLiteral) || line.StartsWith(ContinueLiteral + " ", StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static bool ContainsAnyMenuToken(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (LineEquals(line, HeadingLiteral)
                || line.Contains(DisconnectLiteral, StringComparison.Ordinal)
                || line.Contains(ShowQrLiteral, StringComparison.Ordinal)
                || line.Contains(FooterLiteral, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
