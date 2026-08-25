using System.Text.Json;

namespace Antiphon.SessionRunner;

/// <summary>
/// The runner's own record of a session's transcript binding, at
/// <c>&lt;SessionLogPath&gt;/transcripts/&lt;sessionId:N&gt;.json</c>. Written at launch and updated on
/// every adopt or fork switch.
///
/// It exists because a restarted runner must re-tail a live session's transcript WITHOUT guessing:
/// the old code let a pre-existing, actively-written, cwd-matching file be adopted after 30s, and
/// on 2026-08-09 the busiest such file was the human operator's own conversation (CARD-0006). With
/// a sidecar the restart path needs no discovery at all — it re-opens the exact file it was
/// already reading — so that heuristic is deleted rather than tightened.
///
/// Separate from <c>PtyHostManifest</c> deliberately: the manifest is written by the HOST and
/// deliberately carries no runner-side extras.
/// </summary>
public sealed record TranscriptSidecar
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SessionId { get; init; }

    /// <summary>The session's working directory — what cwd matching (rule C2) compares against.</summary>
    public string? Cwd { get; init; }

    /// <summary>Value of the launch's <c>--name</c>, for the agent-name reject filter (rule C2b).</summary>
    public string? AgentName { get; init; }

    /// <summary>Child process start (rule C3's epoch). Survives runner restarts; the tailer's own start does not.</summary>
    public DateTime? ChildStartUtc { get; init; }

    /// <summary>When input first reached this session's child. Survives runner restarts for unbound-fault reporting.</summary>
    public DateTime? FirstInputAtUtc { get; init; }

    /// <summary>True when the launch was <c>--resume</c>/<c>--continue</c>: rule C3 is waived (copied history predates it).</summary>
    public bool ResumeLaunch { get; init; }

    /// <summary>The bound transcript, or null while the session has not produced one yet.</summary>
    public string? TranscriptPath { get; init; }

    /// <summary>
    /// Which tailer reads this session's transcript (see <c>TranscriptFormats</c>). Null means
    /// Claude — every sidecar written before Grok transcripts existed is a Claude one, so absence
    /// keeps its old meaning and a restart re-tails with the right machinery.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>How it was bound — see <c>TranscriptBindMethods</c>.</summary>
    public string? How { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string DirectoryFor(string sessionLogPath) => Path.Combine(sessionLogPath, "transcripts");

    public static string PathFor(string sessionLogPath, Guid sessionId) =>
        Path.Combine(DirectoryFor(sessionLogPath), $"{sessionId:N}.json");

    /// <summary>Temp + rename, so a concurrent restore never observes a torn file.</summary>
    public void SaveAtomic(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static TranscriptSidecar? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<TranscriptSidecar>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Every sidecar under a session-log root (the claim-restore sweep reads these).</summary>
    public static IEnumerable<TranscriptSidecar> LoadAll(string sessionLogPath)
    {
        var dir = DirectoryFor(sessionLogPath);
        if (!Directory.Exists(dir))
            yield break;

        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }

        foreach (var file in files)
        {
            if (TryLoad(file) is { } sidecar && sidecar.SessionId != Guid.Empty)
                yield return sidecar;
        }
    }
}
