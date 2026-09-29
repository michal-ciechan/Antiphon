namespace Antiphon.Checkpoints;

public sealed record CleanupReceipt(string Outcome, string Reason, string Path)
{
    public bool Removed => Outcome is "Removed" or "AlreadyAbsent";
    public override string ToString() => $"TOOL CLEANUP {Outcome} path={Path} reason={Reason}";
}

public sealed class ToolCopyCleanup
{
    private readonly ProcessIdentityProbe _probe;
    private readonly int _maxEntries;
    private readonly string? _imageDirectory;
    private readonly Action? _beforeLock;
    private readonly Action<string>? _beforeDelete;

    public ToolCopyCleanup(ProcessIdentityProbe? probe = null, int maxEntries = 10000,
        string? imageDirectory = null, Action? beforeLock = null, Action<string>? beforeDelete = null)
    {
        _probe = probe ?? new();
        _maxEntries = maxEntries;
        _imageDirectory = imageDirectory;
        _beforeLock = beforeLock;
        _beforeDelete = beforeDelete;
    }

    public ProcessObservation ObserveExecutor(string runDirectory)
    {
        var record = RunOwnershipStore.Read(runDirectory);
        if (record is null)
            return new(ProcessVerdict.Unknown, "ownership-missing");
        var identity = ExecutorOwnershipStore.Read(runDirectory)?.Executor
            ?? (record.Phase is "launched" or "launch-attempted" ? record.Launched : null);
        return identity is null
            ? new(ProcessVerdict.Unknown, "launch-outcome-unknown")
            : _probe.Observe(identity);
    }

    public CleanupReceipt Remove(string runDirectory)
    {
        var full = Path.GetFullPath(runDirectory);
        var tool = Path.Combine(full, "tool");
        if (!Directory.Exists(tool))
            return new("AlreadyAbsent", "absent", tool);
        if (EvidenceFolder.IsExecutorImage(full, _imageDirectory))
            return new("Retained", "current-executor-image", tool);
        if (!ContainedCleanup.SafeAncestors(full) || !ContainedCleanup.SafeTree(full, _maxEntries))
            return new("Retained", "inventory-incomplete-or-linked", tool);
        _beforeLock?.Invoke();
        try
        {
            // The file remains as a lock rendezvous even after a successful removal.
            using var gate = new FileStream(Path.Combine(full, ".cleanup.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            if (!ContainedCleanup.SafeAncestors(full) || !ContainedCleanup.SafeTree(full, _maxEntries))
                return new("Retained", "inventory-incomplete-or-linked", tool);
            var record = RunOwnershipStore.Read(full);
            if (record is null)
                return new("Retained", "ownership-missing", tool);
            ProcessObservation observed;
            if (ExecutorOwnershipStore.Read(full) is not null)
                observed = ObserveExecutor(full);
            else if (record.Phase == "preparing")
                observed = _probe.Observe(record.Starter);
            else if (record.Phase is "launch-attempted" or "launched")
            {
                observed = ObserveExecutor(full);
            }
            else
                return new("Retained", "launch-outcome-unknown", tool);
            if (observed.Verdict != ProcessVerdict.Dead)
                return new("Retained", observed.Reason, tool);
            for (var attempt = 0; ; attempt++)
            {
                try { _beforeDelete?.Invoke(tool); Directory.Delete(tool, recursive: true); break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 9)
                { Thread.Sleep(50 * (attempt + 1)); }
            }
            return new("Removed", "identity-dead", tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("Failed", ex.GetType().Name, tool);
        }
    }

    public int Sweep(string resultsRoot, TextWriter? output = null, int maxEntries = 512, TimeSpan? budget = null)
    {
        if (!Directory.Exists(resultsRoot)) return 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var removed = 0;
        var count = 0;
        foreach (var directory in Directory.EnumerateDirectories(resultsRoot))
        {
            if (++count > maxEntries || clock.Elapsed > (budget ?? TimeSpan.FromSeconds(2))) break;
            if (!Directory.Exists(Path.Combine(directory, "tool"))) continue;
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
    public static void Write(string runDirectory, ProcessIdentity identity)
    {
        var record = new ExecutorOwnership { RunId = Path.GetFileName(runDirectory),
            RunDirectory = Path.GetFullPath(runDirectory), Executor = identity };
        var path = Path.Combine(runDirectory, FileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            System.Text.Json.JsonSerializer.Serialize(stream, record);
            stream.Flush(flushToDisk: true);
        }
        try { File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ExecutorOwnership? Read(string runDirectory)
    {
        try
        {
            var record = System.Text.Json.JsonSerializer.Deserialize<ExecutorOwnership>(
                File.ReadAllText(Path.Combine(runDirectory, FileName)));
            return record is { Version: 1, Executor: not null }
                && record.RunId == Path.GetFileName(Path.GetFullPath(runDirectory))
                && RunOwnershipStore.SamePath(record.RunDirectory, runDirectory) ? record : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                   or ArgumentException or NotSupportedException)
        { return null; }
    }
}

public static class ContainedCleanup
{
    public static bool SafeAncestors(string path)
    {
        try
        {
            for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }

    public static bool SafeTree(string root, int maxEntries)
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
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) == 0) continue;
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) pending.Push(child);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }
}
