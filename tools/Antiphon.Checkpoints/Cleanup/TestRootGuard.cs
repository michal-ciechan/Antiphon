using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed class CheckpointRootMarker
{
    public int Version { get; set; } = 1;
    public string RootId { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public string AssemblyInvocationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string RootPath { get; set; } = "";
    public ProcessIdentity? Owner { get; set; }
    public string State { get; set; } = "active";
}

public static class TestRootGuard
{
    public const string MarkerName = ".checkpoint-test-root.json";
    public const string LockName = ".checkpoint-root.lock";
    public const string RunsName = ".checkpoint-runs.jsonl";

    public static CheckpointRootMarker? Read(string root)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<CheckpointRootMarker>(File.ReadAllText(Path.Combine(root, MarkerName)));
            return marker is { Version: 1, Owner: not null }
                && marker.RootId == Path.GetFileName(Path.GetFullPath(root))["c723-".Length..]
                && RunOwnershipStore.SamePath(marker.RootPath, root) ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or NotSupportedException) { return null; }
    }

    public static void Write(string root, CheckpointRootMarker marker)
    {
        if (!RunOwnershipStore.SamePath(marker.RootPath, root))
            throw new InvalidOperationException("checkpoint root marker path mismatch");
        var path = Path.Combine(root, MarkerName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, marker);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, true);
    }

    public static FileStream? TryLock(string root)
    {
        try { return new FileStream(Path.Combine(root, LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void RegisterRun(string runDirectory)
    {
        var root = FindRoot(runDirectory);
        if (root is null) return;
        if (!ContainedCleanup.SafeAncestors(root))
            throw new IOException("checkpoint test root is linked");
        using var gate = TryLock(root) ?? throw new IOException("checkpoint test root is busy");
        var marker = Read(root);
        if (marker is null || marker.State != "active")
            throw new IOException("checkpoint test root is sealed or invalid");
        var normalized = Path.GetFullPath(runDirectory);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("checkpoint run is outside test root");
        using var index = new FileStream(Path.Combine(root, RunsName), FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized) + "\n");
        index.Write(bytes);
        index.Flush(flushToDisk: true);
    }

    private static string? FindRoot(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(Path.Combine(current, MarkerName))) return current;
        return null;
    }
}
