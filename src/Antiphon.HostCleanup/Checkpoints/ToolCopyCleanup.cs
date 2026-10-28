using Antiphon.HostCleanup.Checkpoints;
using System.Text.Json;
namespace Antiphon.Checkpoints;

public sealed record CleanupReceipt(string Outcome, string Reason, string Path)
{
    public bool Removed => Outcome is "Removed" or "AlreadyAbsent";
    public override string ToString() => $"TOOL CLEANUP {Outcome} path={Path} reason={Reason}";
}

public sealed class ToolCopyCleanup
{
    private readonly CheckpointCleanupContext _context;
    private readonly CheckpointWrite _write;
    private readonly CheckpointDelete _delete;
    private readonly CheckpointOutputLedger _ledger;
    private readonly ProcessIdentityProbe _probe;
    private readonly int _maxEntries;
    private readonly string? _imageDirectory;
    private readonly Action? _beforeLock;
    private readonly Action<string>? _beforeDelete;

    public ToolCopyCleanup(ProcessIdentityProbe? probe = null, int maxEntries = 10000,
        string? imageDirectory = null, Action? beforeLock = null, Action<string>? beforeDelete = null, CheckpointCleanupContext? context = null)
    {
        _context = context ?? CheckpointCleanupDefaults.Context;
        _write = new(_context);
        _delete = new(_context);
        _ledger = new(_context);
        _probe = probe ?? new(_context);
        _maxEntries = maxEntries;
        _imageDirectory = imageDirectory;
        _beforeLock = beforeLock;
        _beforeDelete = beforeDelete;
    }

    public ProcessObservation ObserveExecutor(string runDirectory, CancellationToken cancellationToken = default)
    {
        var record = RunOwnershipStore.Read(runDirectory, _context, cancellationToken);
        if (record is null)
            return new(ProcessVerdict.Unknown, "ownership-missing");
        var identity = ExecutorOwnershipStore.Read(runDirectory, _context, cancellationToken)?.Executor
            ?? (record.Phase is "launched" or "launch-attempted" ? record.Launched : null);
        if (identity is null && record.Phase == "not-started" && record.Launched is null
            && !_context.Io.FileExists(Path.Combine(runDirectory, "executor-ownership.json")))
            return new(ProcessVerdict.Dead, "launch-not-started");
        return identity is null
            ? new(ProcessVerdict.Unknown, "launch-outcome-unknown")
            : _probe.Observe(identity);
    }

    public CleanupReceipt Remove(string runDirectory, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(runDirectory);
        var tool = Path.Combine(full, "tool");
        if (!_context.Io.DirectoryExists(tool))
            return new("AlreadyAbsent", "absent", tool);
        if (ExecutorImageGuard.IsExecutorImage(_context, full, _imageDirectory))
            return new("Retained", "current-executor-image", tool);
        if (!ContainedCleanup.SafeAncestors(full, _context) || !ContainedCleanup.SafeTree(full, _maxEntries, _context))
            return new("Retained", "inventory-incomplete-or-linked", tool);
        _beforeLock?.Invoke();
        try
        {
            // The file remains as a lock rendezvous even after a successful removal.
            var gateReason = _write.OpenGate(Path.Combine(full, ".cleanup.lock"), _write.Under(full), out var gate);
            if (gateReason is not null || gate is null)
                throw new IOException("refused: " + gateReason);
            using var held = gate!;
            if (!ContainedCleanup.SafeAncestors(full, _context) || !ContainedCleanup.SafeTree(full, _maxEntries, _context))
                return new("Retained", "inventory-incomplete-or-linked", tool);
            var record = RunOwnershipStore.Read(full, _context, cancellationToken);
            if (record is null)
                return new("Retained", "ownership-missing", tool);
            ProcessObservation observed;
            if (ExecutorOwnershipStore.Read(full, _context, cancellationToken) is not null)
                observed = ObserveExecutor(full, cancellationToken);
            else if (record.Phase == "preparing")
                observed = _probe.Observe(record.Starter);
            else if (record.Phase is "launch-attempted" or "launched" or "not-started")
            {
                observed = ObserveExecutor(full, cancellationToken);
            }
            else
                return new("Retained", "launch-outcome-unknown", tool);
            if (observed.Verdict != ProcessVerdict.Dead)
                return new("Retained", observed.Reason, tool);
            ToolCopyAuthority? authority = null;
            string? ledger;
            if (record.ToolCopy is not null)
            {
                // A prior attempt is diagnostic only: never re-admit its possibly
                // incomplete inventory or use this marker as deletion authority.
                var marker = Path.Combine(full, "tool-removal-started.json");
                if (_context.Io.FileExists(marker) || _context.Io.DirectoryExists(marker))
                    return PreviousRemoval(full, tool, marker, record, cancellationToken);
                authority = ToolCopyAuthority.Load(full, record, out ledger, _context, cancellationToken);
            }
            else
                ledger = _ledger.Check(tool);
            if (ledger is not null) return new("Refused", ledger + "; left in place: " + tool + "; remove it manually", tool);
            var deletion = new DeleteOwnership
            {
                IsolatedRoot = full,
                CreatedPaths = [tool],
                RequireInventory = true,
                RequireBinName = false,
                AllowOutsideRepository = true,
            };
            if (authority is not null)
                return RemoveSealed(full, tool, record, authority, deletion);

            // Legacy, unsealed copies retain their current-process ledger behavior.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var removed = _delete.Apply(tool, deletion, () => _beforeDelete?.Invoke(tool), () => _ledger.Check(tool));
                    if (removed.Disposition == DeleteDisposition.Refused)
                        return new("Refused", removed.Reason ?? "refused", tool);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 9)
                { _context.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1))); }
            }
            return new("Removed", "identity-dead", tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("Failed", ex.GetType().Name, tool);
        }
    }

    private CleanupReceipt PreviousRemoval(string run, string tool, string marker, RunOwnership owner,
        CancellationToken cancellationToken)
    {
        try
        {
            var preflight = _write.Preflight(run, [marker]);
            if (preflight is not null) return new("Refused", preflight, tool);
            using var json = JsonDocument.Parse(ReceiptFile.ReadBytes(_context, marker, cancellationToken));
            var value = json.RootElement;
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != 1
                || !value.TryGetProperty("runId", out var runId) || runId.ValueKind != JsonValueKind.String || runId.GetString() != owner.RunId
                || !value.TryGetProperty("toolPath", out var path) || path.ValueKind != JsonValueKind.String || path.GetString() != tool
                || !value.TryGetProperty("authorityDigest", out var digest) || digest.ValueKind != JsonValueKind.String
                || digest.GetString() != owner.ToolCopy!.Value.Sha256)
                return new("Refused", "tool-removal-marker-invalid: " + marker, tool);
            return Incomplete(tool, marker, "previous-removal-attempt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new("Refused", "tool-removal-marker-unreadable: " + marker + "; " + ex.Message, tool);
        }
    }

    private CleanupReceipt RemoveSealed(string run, string tool, RunOwnership owner,
        ToolCopyAuthority authority, DeleteOwnership deletion)
    {
        var marker = Path.Combine(run, "tool-removal-started.json");
        var admission = authority.AdmitRemoval(tool);
        if (admission is not null)
            return new("Refused", admission + "; left in place: " + tool + "; remove it manually", tool);
        var legacy = authority.LegacyUnbound;
        try
        {
            // Publish under the cleanup gate after authority/death admission, before
            // any unlink. Ordinary ownership refuses a foreign existing marker.
            var reason = _write.Text(marker, JsonSerializer.Serialize(new
            {
                version = 1, runId = owner.RunId, toolPath = tool,
                authorityDigest = owner.ToolCopy!.Value.Sha256,
            }), _write.Under(run));
            if (reason is not null)
                return new("Refused", "tool-removal-marker-write-refused: " + marker + "; " + reason, tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("Refused", "tool-removal-marker-write-failed: " + marker + "; " + ex.Message, tool);
        }
        try
        {
            var removed = _delete.ApplyToolCopy(tool, deletion, authority, () => _beforeDelete?.Invoke(tool));
            return removed.Disposition == DeleteDisposition.Refused
                ? Incomplete(tool, marker, removed.Reason ?? "refused")
                : new("Removed", legacy ? "identity-dead; legacy-unbound-record" : "identity-dead", tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Sequential unlinks cannot be rolled back. Retain the original reason
            // and the marker; retrying the original full inventory cannot repair it.
            return Incomplete(tool, marker, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static CleanupReceipt Incomplete(string tool, string marker, string reason) => new("Refused",
        "tool-removal-incomplete: " + reason + "; tool may be partially removed: " + tool
        + "; marker: " + marker + "; manual inspection required: establish executor death and inspect containment, links"
        + " and remaining files before removing this tool residue; retain run receipts, ownership seal and marker", tool);

    public int Sweep(string resultsRoot, TextWriter? output = null, int maxEntries = 512, TimeSpan? budget = null)
    {
        if (!_context.Io.DirectoryExists(resultsRoot)) return 0;
        var started = _context.Time.GetTimestamp();
        var removed = 0;
        var count = 0;
        foreach (var directory in _context.Io.Enumerate(resultsRoot).Where(_context.Io.DirectoryExists))
        {
            if (++count > maxEntries || _context.Time.GetElapsedTime(started) > (budget ?? TimeSpan.FromSeconds(2))) break;
            if (!_context.Io.DirectoryExists(Path.Combine(directory, "tool"))) continue;
            var receipt = Remove(directory);
            output?.WriteLine(receipt);
            if (receipt.Outcome == "Removed") removed++;
        }
        return removed;
    }
}

public sealed class ExecutorOwnership
{
    public int Version { get; set; } = 1;
    public string RunId { get; set; } = "";
    public string RunDirectory { get; set; } = "";
    public ProcessIdentity? Executor { get; set; }
}

public static class ExecutorOwnershipStore
{
    private const string FileName = "executor-ownership.json";
    public static void Write(string runDirectory, ProcessIdentity identity, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var write = new CheckpointWrite(context);
        var record = new ExecutorOwnership { RunId = Path.GetFileName(runDirectory),
            RunDirectory = Path.GetFullPath(runDirectory), Executor = identity };
        var path = Path.Combine(runDirectory, FileName);
        var json = System.Text.Json.JsonSerializer.Serialize(record);
        var reason = write.Text(path, json, write.Under(Path.GetFullPath(runDirectory)));
        if (reason is not null)
            throw new IOException("refused: " + reason);
    }

    public static ExecutorOwnership? Read(string runDirectory, CheckpointCleanupContext? context = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = CheckpointReceiptJson.Read<ExecutorOwnership>(context ?? CheckpointCleanupDefaults.Context, Path.Combine(runDirectory, FileName), new System.Text.Json.JsonSerializerOptions(), cancellationToken);
            return record is { Version: 1, Executor: not null }
                && record.RunId == Path.GetFileName(Path.GetFullPath(runDirectory))
                && RunOwnershipStore.SamePath(record.RunDirectory, runDirectory, context) ? record : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                   or ArgumentException or NotSupportedException)
        { return null; }
    }
}

public static class ContainedCleanup
{
    public static bool SafeAncestors(string path, CheckpointCleanupContext? context = null)
    {
        try
        {
            for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            {
                if (((context ?? CheckpointCleanupDefaults.Context).Io.Attributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }

    public static bool SafeTree(string root, int maxEntries, CheckpointCleanupContext? context = null)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        var count = 0;
        try
        {
            while (pending.Count > 0)
            {
                var path = pending.Pop();
                if (++count > maxEntries) return false;
                var attributes = (context ?? CheckpointCleanupDefaults.Context).Io.Attributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) == 0) continue;
                foreach (var child in (context ?? CheckpointCleanupDefaults.Context).Io.Enumerate(path)) pending.Push(child);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }
}
