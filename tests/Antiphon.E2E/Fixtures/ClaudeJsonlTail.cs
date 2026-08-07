using System.Text.Json;

namespace Antiphon.E2E;

/// <summary>
/// Reads a live Claude session's own JSONL transcript — the same file the production session-runner
/// tails, and the only deterministic source of "the turn ended" and "here is what it said".
///
/// Screen scraping cannot answer either question reliably against current builds: the TUI animates
/// continuously so silence never arrives, and the rendered viewport shows only what fits. The JSONL
/// has an explicit turn boundary and the assistant's text in full, so it is the primary signal;
/// the screen is used to corroborate, never to decide.
/// </summary>
public sealed class ClaudeJsonlTail
{
    private readonly string _sessionId;
    private string? _path;

    public ClaudeJsonlTail(Guid sessionId) => _sessionId = sessionId.ToString("D");

    /// <summary>The transcript file, once Claude has created it (it is created lazily, on the first turn).</summary>
    public string? Path => _path ??= Locate(_sessionId);

    /// <summary>
    /// Wait until the transcript shows a completed turn — an assistant record whose stop reason
    /// ends the turn. Returns the assistant text of that turn, or null on timeout.
    /// </summary>
    public async Task<string?> WaitForTurnEndAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Path is not null && TryReadCompletedTurn(Path, out var text))
                return text;

            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    /// <summary>The assistant text of the last completed turn, or null when none has completed.</summary>
    public string? LastCompletedTurnText() =>
        Path is not null && TryReadCompletedTurn(Path, out var text) ? text : null;

    /// <summary>
    /// Records are appended, so a partially-written final line is normal — skip anything that does
    /// not parse rather than failing, and re-read on the next poll.
    /// </summary>
    private static bool TryReadCompletedTurn(string path, out string? text)
    {
        text = null;
        List<string> lines;
        try
        {
            // The file is open for append in another process; share everything or we cannot read it.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = [];
            while (reader.ReadLine() is { } line)
                lines.Add(line);
        }
        catch (IOException)
        {
            return false;
        }

        var assistantChunks = new List<string>();
        var sawTurnEnd = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement root;
            try
            {
                root = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            // A new user prompt starts a fresh turn — anything gathered before it belongs to an
            // earlier turn and must not be reported as this one's answer.
            if (type == "user" && !IsMeta(root))
            {
                assistantChunks.Clear();
                sawTurnEnd = false;
                continue;
            }

            if (type != "assistant" || !root.TryGetProperty("message", out var message))
                continue;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var blockType)
                        && blockType.GetString() == "text"
                        && block.TryGetProperty("text", out var blockText)
                        && blockText.GetString() is { Length: > 0 } value)
                    {
                        assistantChunks.Add(value);
                    }
                }
            }

            if (message.TryGetProperty("stop_reason", out var stop)
                && stop.GetString() is "end_turn" or "stop_sequence")
            {
                sawTurnEnd = true;
            }
        }

        if (!sawTurnEnd || assistantChunks.Count == 0)
            return false;

        text = string.Join("\n\n", assistantChunks).Trim();
        return text.Length > 0;
    }

    private static bool IsMeta(JsonElement root) =>
        root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Claude writes transcripts to ~/.claude/projects/&lt;encoded-cwd&gt;/&lt;session-id&gt;.jsonl.
    /// The directory encoding is not worth reproducing — searching for the session id is exact and
    /// cheap, and survives whatever encoding scheme the CLI uses.
    /// </summary>
    private static string? Locate(string sessionId)
    {
        var projects = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(projects))
            return null;

        try
        {
            return Directory
                .EnumerateFiles(projects, $"{sessionId}.jsonl", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
