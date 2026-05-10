using System.Text;

namespace Antiphon.Agents.Pty;

public static class AnsiStripper
{
    private const char Esc = '';
    private const char Bel = '';

    public static string? Clean(string? input)
    {
        if (input is null) return null;
        if (input.Length == 0) return input;

        var sb = new StringBuilder(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (c != Esc)
            {
                sb.Append(c);
                i++;
                continue;
            }

            // ESC at i
            if (i + 1 >= input.Length) { i++; continue; }
            char next = input[i + 1];

            switch (next)
            {
                case '[': // CSI: ESC [ params intermediates final(0x40-0x7E)
                    i = SkipCsi(input, i + 2);
                    break;
                case ']': // OSC: ESC ] ... (BEL or ESC \)
                    i = SkipOsc(input, i + 2);
                    break;
                case 'P': case 'X': case '^': case '_': // DCS/SOS/PM/APC: ESC X ... ESC \
                    i = SkipUntilSt(input, i + 2);
                    break;
                default:
                    if (next >= '@' && next <= '_') { i += 2; break; } // single-char ESC seq
                    if (next == '\\') { i += 2; break; }                // string terminator stray
                    i += 2;
                    break;
            }
        }
        return sb.ToString();
    }

    private static int SkipCsi(string s, int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c >= 0x40 && c <= 0x7E) return i + 1;
            i++;
        }
        return i;
    }

    private static int SkipOsc(string s, int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (c == Bel) return i + 1;
            if (c == Esc && i + 1 < s.Length && s[i + 1] == '\\') return i + 2;
            i++;
        }
        return i;
    }

    private static int SkipUntilSt(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == Esc && i + 1 < s.Length && s[i + 1] == '\\') return i + 2;
            i++;
        }
        return i;
    }
}
