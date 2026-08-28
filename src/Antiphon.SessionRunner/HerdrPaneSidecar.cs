using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Runner-side record of a herdr-hosted session's workspace/tab/pane ids, at
/// <c>&lt;SessionLogPath&gt;/herdr/&lt;sessionId:N&gt;.json</c> (CARD-0160).
///
/// Sidecar, not DB columns: Layer A adoption runs before the runner's HTTP API listens, so it
/// structurally cannot read the server's DB. Layer B never talks to herdr — the server has no
/// herdr client — so DB-resident pane ids would have no reader. The authoritative record of
/// "which workspace/tab/pane is session X" is this file; herdr metadata tokens are best-effort
/// identity only (TTL capped at 24h; restart survival unverified).
/// </summary>
public sealed record HerdrPaneSidecar
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SessionId { get; init; }
    public required string WorkspaceKey { get; init; }
    public required string WorkspaceId { get; init; }
    public required string TabId { get; init; }
    public required string PaneId { get; init; }
    /// <summary>The agent child's pid from pane.process_info after launch (leaf under a wrapper, <c>cmd.exe</c> for a <c>.cmd</c> launcher).</summary>
    public int? ChildPid { get; init; }
    /// <summary>The pane's shell pid, if herdr wraps one.</summary>
    public int? ShellPid { get; init; }
    /// <summary>C3 epoch and staleness judge — runner UTC at launch success.</summary>
    public DateTime LaunchedAtUtc { get; init; }
    public string? Cwd { get; init; }
    /// <summary>Herdr-detected kind at launch (claude/grok/codex); optional, operator/S3 only.</summary>
    public string? AgentKind { get; init; }
    /// <summary>
    /// CARD-0224 / CARD-0213: <see cref="HerdrPaneOrigins.Launched"/> (default / pre-field files)
    /// or <see cref="HerdrPaneOrigins.Attached"/>. Attached exits write no last-pane record.
    /// </summary>
    public string? Origin { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string DirectoryFor(string sessionLogPath) => Path.Combine(sessionLogPath, "herdr");

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

    public static HerdrPaneSidecar? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<HerdrPaneSidecar>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static void TryDelete(string sessionLogPath, Guid sessionId)
    {
        var path = PathFor(sessionLogPath, sessionId);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup on stop; a stale sidecar is cleaned on the next adoption sweep.
        }
    }

    /// <summary>
    /// CARD-0224: move this session's sidecar to a last-pane record (so the next launch of this
    /// id can target the standing pane) then delete the sidecar. An attached-origin sidecar is
    /// deleted without a last-pane record — Antiphon never types into a pane it did not create.
    /// </summary>
    public static void Retire(string sessionLogPath, Guid sessionId, string exitReason)
    {
        var path = PathFor(sessionLogPath, sessionId);
        var sidecar = TryLoad(path);
        if (sidecar is not null
            && !string.Equals(sidecar.Origin, HerdrPaneOrigins.Attached, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                HerdrLastPane.FromSidecar(sidecar, exitReason)
                    .SaveAtomic(HerdrLastPane.PathFor(sessionLogPath, sessionId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort; the next launch will allocate if the record is missing.
            }
        }

        TryDelete(sessionLogPath, sessionId);
    }

    /// <summary>Every sidecar under a session-log root (the herdr adoption sweep reads these).</summary>
    public static IEnumerable<HerdrPaneSidecar> LoadAll(string sessionLogPath)
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
