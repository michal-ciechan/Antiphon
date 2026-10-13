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
            var marker = JsonSerializer.Deserialize<CheckpointRootMarker>(ReceiptFile.ReadText(Path.Combine(root, MarkerName)));
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
        var reason = SafeWrite.Text(path, JsonSerializer.Serialize(marker), SafeWrite.Under(root));
        if (reason is not null)
            throw new IOException("refused: " + reason);
    }

    public static FileStream? TryLock(string root)
    {
        try
        {
            var reason = SafeWrite.OpenGate(Path.Combine(root, LockName), SafeWrite.Under(root), out var stream);
            return reason is null ? stream : null;
        }
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
        var recorded = SafeWrite.Append(Path.Combine(root, RunsName), JsonSerializer.Serialize(normalized) + "\n", SafeWrite.Under(root));
        if (recorded is not null)
            throw new IOException("refused: " + recorded);
    }

    private static string? FindRoot(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(Path.Combine(current, MarkerName))) return current;
        return null;
    }
}
