// The live-lane gate scan's source stripper (W2-nightly S23): the inline C# of Initialize-LiveLaneSourceStripper
// (live-lane-manifest.ps1), moved as it was into the compiled core. Comments are dropped (a block comment keeps its
// line ends and leaves one space); string and character literals keep their text with every brace blanked, so a
// brace count over the result sees only the code's own braces. Interpolation holes stay code. ASCII-only.
using System;
using System.Text;

namespace Antiphon.Nightly
{
public static class LiveLaneSource
{
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        Code(text, sb, 0, text.Length, false);
        return sb.ToString();
    }

    static int Code(string text, StringBuilder sb, int i, int n, bool hole)
    {
        int depth = hole ? 1 : 0;
        while (i < n)
        {
            char c = text[i];
            if (i + 1 < n && c == '/' && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            if (i + 1 < n && c == '/' && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    if (text[i] == '\n') sb.Append('\n');
                    i++;
                }
                if (i + 1 < n) i += 2;
                sb.Append(' ');
                continue;
            }
            if (IsVerbatimInterp(text, i))
            {
                i = Interpolated(text, sb, i + 3, n, true);
                continue;
            }
            if (i + 1 < n && c == '@' && text[i + 1] == '"')
            {
                i = Verbatim(text, sb, i + 2, n);
                continue;
            }
            if (c == '$' && i + 1 < n && QuoteRun(text, i + 1) >= 3)
            {
                i = Raw(text, sb, i + 1, n, QuoteRun(text, i + 1));
                continue;
            }
            int run = QuoteRun(text, i);
            if (run >= 3)
            {
                i = Raw(text, sb, i, n, run);
                continue;
            }
            if (i + 1 < n && c == '$' && text[i + 1] == '"')
            {
                i = Interpolated(text, sb, i + 2, n, false);
                continue;
            }
            if (c == '"')
            {
                i = Normal(text, sb, i + 1, n);
                continue;
            }
            if (c == '\'')
            {
                i = CharLit(text, sb, i, n);
                continue;
            }
            if (hole && c == '{') { depth++; sb.Append(c); i++; continue; }
            if (hole && c == '}')
            {
                depth--;
                if (depth == 0) return i + 1;
                sb.Append(c);
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return i;
    }

    static bool IsVerbatimInterp(string text, int i)
    {
        if (i + 2 >= text.Length) return false;
        return (text[i] == '$' && text[i + 1] == '@' && text[i + 2] == '"')
            || (text[i] == '@' && text[i + 1] == '$' && text[i + 2] == '"');
    }

    static int QuoteRun(string text, int i)
    {
        int count = 0;
        while (i + count < text.Length && text[i + count] == '"') count++;
        return count;
    }

    static void Lit(StringBuilder sb, char c)
    {
        sb.Append(c == '{' || c == '}' ? ' ' : c);
    }

    static int Normal(string text, StringBuilder sb, int i, int n)
    {
        while (i < n)
        {
            char c = text[i];
            if (c == '\\')
            {
                Lit(sb, c);
                if (i + 1 < n) Lit(sb, text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '"') return i + 1;
            Lit(sb, c);
            i++;
        }
        return i;
    }

    static int Verbatim(string text, StringBuilder sb, int i, int n)
    {
        while (i < n)
        {
            if (text[i] == '"')
            {
                if (i + 1 < n && text[i + 1] == '"') { Lit(sb, '"'); i += 2; continue; }
                return i + 1;
            }
            Lit(sb, text[i]);
            i++;
        }
        return i;
    }

    static int Raw(string text, StringBuilder sb, int quoteAt, int n, int quotes)
    {
        int i = quoteAt + quotes;
        int end = n;
        int k = i;
        while (k < n && text[k] != '\n')
        {
            if (text[k] == '"')
            {
                int run = QuoteRun(text, k);
                if (run >= quotes) { end = k + run; break; }
                k += Math.Max(run, 1);
                continue;
            }
            k++;
        }
        if (end == n)
        {
            i = k;
            while (i < n)
            {
                if (text[i] == '\n')
                {
                    int j = i + 1;
                    while (j < n && (text[j] == ' ' || text[j] == '\t')) j++;
                    if (QuoteRun(text, j) >= quotes)
                    {
                        end = j;
                        while (end < n && text[end] == '"') end++;
                        break;
                    }
                }
                i++;
            }
        }
        for (int t = quoteAt; t < end; t++) Lit(sb, text[t]);
        return end;
    }

    static int Interpolated(string text, StringBuilder sb, int i, int n, bool verbatim)
    {
        while (i < n)
        {
            char c = text[i];
            if (!verbatim && c == '\\')
            {
                Lit(sb, c);
                if (i + 1 < n) Lit(sb, text[i + 1]);
                i += 2;
                continue;
            }
            if (verbatim && c == '"' && i + 1 < n && text[i + 1] == '"') { Lit(sb, '"'); i += 2; continue; }
            if (c == '"') return i + 1;
            if (c == '{' && i + 1 < n && text[i + 1] == '{') { Lit(sb, ' '); Lit(sb, ' '); i += 2; continue; }
            if (c == '}' && i + 1 < n && text[i + 1] == '}') { Lit(sb, ' '); Lit(sb, ' '); i += 2; continue; }
            if (c == '{')
            {
                sb.Append('{');
                i = Code(text, sb, i + 1, n, true);
                sb.Append('}');
                continue;
            }
            Lit(sb, c);
            i++;
        }
        return i;
    }

    static int CharLit(string text, StringBuilder sb, int i, int n)
    {
        i++;
        if (i < n && text[i] == '\\')
        {
            i++;
            if (i < n) i++;
        }
        else if (i < n)
        {
            if (text[i] == '{' || text[i] == '}') sb.Append(' ');
            i++;
        }
        if (i < n && text[i] == '\'') i++;
        return i;
    }
}
}
