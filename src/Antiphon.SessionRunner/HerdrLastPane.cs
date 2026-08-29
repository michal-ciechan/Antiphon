using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0224: runner-side record of the pane a herdr session last lived in, kept after an exit
/// that left the pane standing. Stored at
/// <c>&lt;SessionLogPath&gt;/herdr/last-pane/&lt;sessionId:N&gt;.json</c> — a different directory
/// from <see cref="HerdrPaneSidecar"/> so <see cref="HerdrPaneSidecar.LoadAll"/>, adoption, the
/// allocator, and the event pump stay byte-for-byte unaffected. Only the next launch of that
/// session id (or <c>ReusePaneOfSessionId</c>) reads this.
/// </summary>
public sealed record HerdrLastPane
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SessionId { get; init; }
    public required string WorkspaceKey { get; init; }
    public required string WorkspaceId { get; init; }
    public required string TabId { get; init; }
    public required string PaneId { get; init; }
    /// <summary>For the log line only — never trusted as identity.</summary>
    public int? LastChildPid { get; init; }
    public string? Cwd { get; init; }
    public string? AgentKind { get; init; }
    public required string Origin { get; init; }
    public required string ExitReason { get; init; }
    public required DateTime ExitedAtUtc { get; init; }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string DirectoryFor(string sessionLogPath) =>
        Path.Combine(HerdrPaneSidecar.DirectoryFor(sessionLogPath), "last-pane");

    public static string PathFor(string sessionLogPath, Guid sessionId) =>
        Path.Combine(DirectoryFor(sessionLogPath), $"{sessionId:N}.json");

    public static HerdrLastPane FromSidecar(HerdrPaneSidecar sidecar, string exitReason) => new()
    {
        SessionId = sidecar.SessionId,
        WorkspaceKey = sidecar.WorkspaceKey,
        WorkspaceId = sidecar.WorkspaceId,
        TabId = sidecar.TabId,
        PaneId = sidecar.PaneId,
        LastChildPid = sidecar.ChildPid,
        Cwd = sidecar.Cwd,
        AgentKind = sidecar.AgentKind,
        Origin = string.IsNullOrWhiteSpace(sidecar.Origin)
            ? HerdrPaneOrigins.Launched
            : sidecar.Origin,
        ExitReason = exitReason,
        ExitedAtUtc = DateTime.UtcNow,
    };

    /// <summary>Temp + rename, so a concurrent restore never observes a torn file.</summary>
    public void SaveAtomic(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static HerdrLastPane? TryLoad(string sessionLogPath, Guid sessionId) =>
        TryLoad(PathFor(sessionLogPath, sessionId));

    public static HerdrLastPane? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<HerdrLastPane>(File.ReadAllText(path), Options);
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
            // Best-effort; a stale record is pruned on the next adoption sweep.
        }
    }

    /// <summary>Every last-pane record under a session-log root (CARD-0213 bound-pane check).</summary>
    public static IEnumerable<HerdrLastPane> LoadAll(string sessionLogPath)
    {
        var dir = DirectoryFor(sessionLogPath);
        if (!Directory.Exists(dir))
            yield break;

        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }

        foreach (var file in files)
        {
            if (TryLoad(file) is { } record && record.SessionId != Guid.Empty)
                yield return record;
        }
    }

    /// <summary>CARD-0224: drop last-pane records older than <paramref name="retention"/>.</summary>
    public static int DeleteOlderThan(string sessionLogPath, TimeSpan retention)
    {
        var dir = DirectoryFor(sessionLogPath);
        if (!Directory.Exists(dir))
            return 0;

        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }

        var cutoff = DateTime.UtcNow - retention;
        var deleted = 0;
        foreach (var file in files)
        {
            var record = TryLoad(file);
            if (record is null || record.ExitedAtUtc > cutoff)
                continue;
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort prune.
            }
        }

        return deleted;
    }
}
