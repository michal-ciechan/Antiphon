using System.Text;

namespace Antiphon.SessionRunner;

/// <summary>
/// A bounded, normalized record of everything the runner has typed into one session — the evidence
/// behind adoption rule C4 (CARD-0006). The runner sees every byte written to the pty, and Claude
/// records a submitted prompt verbatim in its JSONL, so a candidate transcript containing a prompt
/// this session actually received IS this session's transcript. Nothing else available to the
/// runner positively identifies a file: no transcript record carries a pid, and cwd + recency —
/// all the old discovery had — matched the human operator's own conversation on 2026-08-09.
///
/// Text is stored normalized (see <see cref="Normalize"/>) so a match survives the escape wrapping
/// the delivery path adds and any re-wrapping Claude does before persisting the prompt.
///
/// Deliberately NOT persisted: after a runner restart the log is empty, which is fine because a
/// restarted session re-tails from its sidecar rather than re-running discovery.
/// </summary>
public sealed class SessionInputLog
{
    /// <summary>Bound on retained input. Old input ages out of the front; recent prompts are what match.</summary>
    public const int DefaultCapacityChars = 64 * 1024;

    /// <summary>
    /// Shortest candidate prompt that may satisfy C4. Short strings ("y", "ok", "continue") occur
    /// in unrelated conversations often enough to be worthless as identification.
    /// </summary>
    public const int MinMatchChars = 12;

    /// <summary>How much of a long prompt is compared — the log is bounded, so a whole 40 KB brief cannot be.</summary>
    public const int MatchWindowChars = 200;

    private const char Esc = '\u001b';

    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new();
    private readonly int _capacity;

    public SessionInputLog(int capacityChars = DefaultCapacityChars) =>
        _capacity = Math.Max(MatchWindowChars, capacityChars);

    public bool IsEmpty
    {
        get { lock (_gate) return _buffer.Length == 0; }
    }

    /// <summary>Records raw pty input (escape sequences and all); normalization happens here.</summary>
    public void Append(string input)
    {
        if (string.IsNullOrEmpty(input))
            return;

        var normalized = Normalize(input);
        if (normalized.Length == 0)
            return;

        lock (_gate)
        {
            if (_buffer.Length > 0)
                _buffer.Append(' ');
            _buffer.Append(normalized);
            if (_buffer.Length > _capacity)
                _buffer.Remove(0, _buffer.Length - _capacity);
        }
    }

    /// <summary>
    /// C4: does <paramref name="candidateText"/> (a user-prompt record read out of a candidate
    /// transcript) appear in what this session was actually sent? The caller passes raw record
    /// text; normalization and the length/window rules are applied here.
    /// </summary>
    public bool MatchesRecordedInput(string? candidateText)
    {
        if (string.IsNullOrEmpty(candidateText))
            return false;

        var needle = Normalize(candidateText);
        if (needle.Length < MinMatchChars)
            return false;
        if (needle.Length > MatchWindowChars)
            needle = needle[..MatchWindowChars];

        lock (_gate)
            return _buffer.Length > 0 && _buffer.ToString().Contains(needle, StringComparison.Ordinal);
    }

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

    // Returns the index of the LAST char of the escape sequence starting at `start` (the caller's
    // for-loop increments past it). Handles CSI (ESC [ ... final @-~), OSC (ESC ] ... BEL or ESC \)
    // and the two-character forms; an unterminated sequence swallows the rest of the string, which
    // is the right call for a truncated write.
    private static int SkipEscapeSequence(string text, int start)
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
}
