using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Strips instruction payloads out of text that may be copied onto the HTTP problem body.
/// The raw command line stays off that body: prompt arguments and any argument longer than
/// <see cref="MaxVisibleArgumentChars"/> become <c>&lt;redacted N chars&gt;</c>.
/// </summary>
public static class CommandLineDisclosure
{
    public const int MaxVisibleArgumentChars = 200;
    public const int ProblemDetailMaxChars = 512;

    private static readonly string[] PromptFlags =
    [
        "--append-system-prompt",
        "--system-prompt",
        "--rules",
    ];

    /// <summary>Type and message only, redacted, then clipped to <see cref="ProblemDetailMaxChars"/>.</summary>
    public static string ForProblemDetail(string? message) =>
        Bound(Redact(message), ProblemDetailMaxChars);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        var sb = new StringBuilder(Math.Min(text.Length, 1024));
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                var whitespace = i;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                sb.Append(text, whitespace, i - whitespace);
                continue;
            }

            var start = i;
            var decoded = ReadArgument(text, ref i);
            if (IsPromptFlag(decoded))
            {
                sb.Append(text, start, i - start);
                if (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    var whitespace = i;
                    while (i < text.Length && char.IsWhiteSpace(text[i]))
                        i++;
                    sb.Append(text, whitespace, i - whitespace);
                    if (i < text.Length && !char.IsWhiteSpace(text[i]))
                    {
                        var promptArgument = ReadArgument(text, ref i);
                        sb.Append(Marker(promptArgument.Length));
                    }
                }

                continue;
            }

            if (TrySplitPromptEquals(decoded, out var flag, out var equalsValue))
            {
                sb.Append(flag).Append('=').Append(Marker(equalsValue.Length));
                continue;
            }

            if (decoded.Length > MaxVisibleArgumentChars)
            {
                sb.Append(Marker(decoded.Length));
                continue;
            }

            sb.Append(text, start, i - start);
        }

        return sb.ToString();
    }

    public static string Bound(string text, int max)
    {
        if (max <= 0)
            return string.Empty;
        if (text.Length <= max)
            return text;

        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        if (cut < 0)
            cut = 0;
        return string.Concat(text.AsSpan(0, cut), "…");
    }

    private static string Marker(int chars) => $"<redacted {chars} chars>";

    private static bool IsPromptFlag(string token)
    {
        foreach (var flag in PromptFlags)
        {
            if (string.Equals(token, flag, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool TrySplitPromptEquals(string token, out string flag, out string value)
    {
        var eq = token.IndexOf('=');
        if (eq <= 0)
        {
            flag = "";
            value = "";
            return false;
        }

        flag = token[..eq];
        if (!IsPromptFlag(flag))
        {
            value = "";
            return false;
        }

        value = token[(eq + 1)..];
        return true;
    }

    /// <summary>
    /// One CRT/<c>CommandLineToArgvW</c> argument. Quotes and backslash-escaped quotes are
    /// consumed so a quoted instructions bundle is one argument, not a whitespace split.
    /// </summary>
    private static string ReadArgument(string text, ref int i)
    {
        var sb = new StringBuilder();
        var quoted = false;
        while (i < text.Length)
        {
            if (!quoted && char.IsWhiteSpace(text[i]))
                break;

            if (text[i] == '\\')
            {
                var slashes = 0;
                while (i < text.Length && text[i] == '\\')
                {
                    slashes++;
                    i++;
                }

                if (i < text.Length && text[i] == '"')
                {
                    sb.Append('\\', slashes / 2);
                    if ((slashes & 1) == 1)
                        sb.Append('"');
                    else
                        quoted = !quoted;
                    i++;
                    continue;
                }

                sb.Append('\\', slashes);
                continue;
            }

            if (text[i] == '"')
            {
                quoted = !quoted;
                i++;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }
}
