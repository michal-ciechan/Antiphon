namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0491. Fail-closed read of a Grok ready screen. Empty is the measured composer box
/// row (<c>│ &gt;</c> with only spaces before the closing border). The Shift+Tab / Ctrl+x
/// footer is chrome and is not the prompt.
/// </summary>
public enum GrokComposerState
{
    Empty = 0,
    Draft = 1,
    Unreadable = 2,
}

public static class GrokComposerScreen
{
    private const char Box = '\u2502';
    private const char TopLeft = '\u256D';
    private const char BottomLeft = '\u2570';

    public static GrokComposerState Classify(string? rendered)
    {
        if (string.IsNullOrWhiteSpace(rendered))
            return GrokComposerState.Unreadable;

        var lines = rendered.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var prompt = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsPromptLine(lines[i]))
                prompt = i;
        }

        if (prompt < 0)
            return GrokComposerState.Unreadable;

        for (var i = prompt + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || IsChrome(lines[i]))
                continue;
            return GrokComposerState.Unreadable;
        }

        return PromptBody(lines[prompt]).Length == 0
            ? GrokComposerState.Empty
            : GrokComposerState.Draft;
    }

    private static bool IsPromptLine(string line)
    {
        var box = line.IndexOf(Box);
        if (box < 0 || box + 1 >= line.Length)
            return false;
        var i = box + 1;
        while (i < line.Length && line[i] == ' ')
            i++;
        return i < line.Length && line[i] == '>';
    }

    private static bool IsChrome(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == TopLeft || trimmed[0] == BottomLeft))
            return true;
        return line.Contains("Shift+Tab", StringComparison.Ordinal)
            && line.Contains("Ctrl+x", StringComparison.Ordinal);
    }

    private static string PromptBody(string line)
    {
        var box = line.IndexOf(Box);
        var i = box + 1;
        while (i < line.Length && line[i] == ' ')
            i++;
        if (i < line.Length && line[i] == '>')
            i++;
        var rest = line[i..];
        var close = rest.LastIndexOf(Box);
        if (close >= 0)
            rest = rest[..close];
        return rest.Trim();
    }
}
