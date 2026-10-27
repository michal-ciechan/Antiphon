using Antiphon.HostCleanup.Checkpoints;
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

    public static CheckpointRootMarker? Read(string root, CheckpointCleanupContext? context = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<CheckpointRootMarker>(ReceiptFile.ReadText(context ?? CheckpointCleanupDefaults.Context, Path.Combine(root, MarkerName), cancellationToken));
            return marker is { Version: 1, Owner: not null }
                && marker.RootId == Path.GetFileName(Path.GetFullPath(root))["c723-".Length..]
                && RunOwnershipStore.SamePath(marker.RootPath, root, context) ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or NotSupportedException) { return null; }
    }

    public static void Write(string root, CheckpointRootMarker marker, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var write = new CheckpointWrite(context);
        if (!RunOwnershipStore.SamePath(marker.RootPath, root, context))
            throw new InvalidOperationException("checkpoint root marker path mismatch");
        var path = Path.Combine(root, MarkerName);
        var reason = write.Text(path, JsonSerializer.Serialize(marker), write.Under(root));
        if (reason is not null)
            throw new IOException("refused: " + reason);
    }

    public static FileStream? TryLock(string root) => TryLock(CheckpointCleanupDefaults.Context, root) as FileStream;

    public static Stream? TryLock(CheckpointCleanupContext context, string root)
    {
        try
        {
            var reason = new CheckpointWrite(context).OpenGate(Path.Combine(root, LockName), SafeWrite.Under(root), out var stream);
            return reason is null ? stream : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // The sweep owns the root gate and has checked nested custody. This single
    // active/deleting -> deleting transition must work after the marker's creator
    // exits; it grants no ordinary overwrite or output-deletion authority.
    public static void MarkDeleting(string root, CheckpointRootMarker expected, FileStream gate,
        ProcessIdentityProbe probe) => MarkDeleting(CheckpointCleanupDefaults.Context, root, expected, gate, probe);

    public static void MarkDeleting(CheckpointCleanupContext context, string root, CheckpointRootMarker expected,
        Stream gate, ProcessIdentityProbe probe, CancellationToken cancellationToken = default)
    {
        var write = new CheckpointWrite(context);
        var path = Path.Combine(root, MarkerName);
        var current = Read(root, context, cancellationToken);
        if (!gate.CanWrite || !RunOwnershipStore.SamePath(context.Io.StreamPath(gate), Path.Combine(root, LockName), context)
            || current is null || current.State is not ("active" or "deleting")
            || expected.State != "deleting" || current.RootId != expected.RootId
            || current.AttemptId != expected.AttemptId || current.AssemblyInvocationId != expected.AssemblyInvocationId
            || current.CreatedAt != expected.CreatedAt || current.Owner != expected.Owner
            || !RunOwnershipStore.SamePath(current.RootPath, expected.RootPath, context)
            || probe.Observe(current.Owner).Verdict != ProcessVerdict.Dead)
            throw new IOException("refused: root-marker-transition-unproven");
        SafeWrite.Require(write.Preflight(root, [path]));
        var previous = ReceiptFile.ReadBytes(context, path, cancellationToken);
        // Bind the bytes used for replacement to the exact identity checked above.
        if (JsonSerializer.Serialize(JsonSerializer.Deserialize<CheckpointRootMarker>(previous))
            != JsonSerializer.Serialize(current))
            throw new IOException("refused: root-marker-changed");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var payload = JsonSerializer.Serialize(expected);
        SafeWrite.Require(write.Text(temporary, payload, write.Under(root)));
        var armed = false;
        try
        {
            SafeWrite.Require(write.Preflight(root, [path]));
            if (!ReceiptFile.ReadBytes(context, path, cancellationToken).SequenceEqual(previous))
                throw new IOException("refused: root-marker-changed");
            armed = true;
            ReplacementPublication.Publish(context.Io, temporary, path, System.Text.Encoding.UTF8.GetBytes(payload));
        }
        finally
        {
            // Publish keeps the temporary when the destination is missing. A refusal
            // before publication still removes the unused temporary.
            if (!armed && context.Io.FileExists(temporary)) new CheckpointDelete(context).DeleteCreatedFile(temporary);
        }
    }

    public static void RegisterRun(string runDirectory, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var write = new CheckpointWrite(context);
        var root = FindRoot(context, runDirectory);
        if (root is null) return;
        if (!ContainedCleanup.SafeAncestors(root, context))
            throw new IOException("checkpoint test root is linked");
        using var gate = TryLock(context, root) ?? throw new IOException("checkpoint test root is busy");
        var marker = Read(root, context);
        if (marker is null || marker.State != "active")
            throw new IOException("checkpoint test root is sealed or invalid");
        var normalized = Path.GetFullPath(runDirectory);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(prefix, context.PathComparison))
            throw new IOException("checkpoint run is outside test root");
        var recorded = write.Append(Path.Combine(root, RunsName), JsonSerializer.Serialize(normalized) + "\n", SafeWrite.Under(root));
        if (recorded is not null)
            throw new IOException("refused: " + recorded);
    }

    private static string? FindRoot(CheckpointCleanupContext context, string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (context.Io.FileExists(Path.Combine(current, MarkerName))) return current;
        return null;
    }
}
