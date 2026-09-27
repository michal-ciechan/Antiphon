using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>One PTY session's UTF-8 state, retained between byte reads.</summary>
internal sealed class PtyUtf8Decoder
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    internal string Decode(ReadOnlySpan<byte> bytes, bool flush = false)
    {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var count = _decoder.GetChars(bytes, chars, flush);
        return count == 0 ? string.Empty : new string(chars, 0, count);
    }
}
