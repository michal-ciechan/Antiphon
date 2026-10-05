namespace Antiphon.SessionRunner;

/// <summary>Reader-owned end-of-file proof, never inferred from a session status or quiet timer.</summary>
internal sealed class TerminalTranscriptCompletion
{
    private volatile bool _complete;
    private bool _parseFailed;
    internal bool IsComplete => _complete;
    internal void Invalidate() => _complete = false;
    internal void ParseFailed() { _parseFailed = true; _complete = false; }
    internal void ObserveLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try { using var parsed = System.Text.Json.JsonDocument.Parse(line); }
        catch (System.Text.Json.JsonException) { ParseFailed(); }
    }

    // Called after processing the bytes, under the tailer's read gate. Missing files, unread
    // suffixes, partial lines and parser faults remain incomplete, even after the exit grace.
    internal bool Observe(string path, long consumed, int pending, DateTime? exitedAt, TimeSpan settle,
        bool currentFile)
    {
        _complete = false;
        if (exitedAt is null || DateTime.UtcNow - exitedAt < settle) return false;
        try
        {
            var file = new FileInfo(path);
            _complete = currentFile && !_parseFailed && pending == 0 && file.Exists && file.Length == consumed;
            return !file.Exists || file.Length <= consumed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }
}
