using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

internal sealed class CheckpointSweepOptions
{
    public TimeSpan Grace { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxDirectEntries { get; init; } = 512;
    public int MaxDescendantEntries { get; init; } = 10000;
    public int MaxRoots { get; init; } = 16;
    public long MaxBytes { get; init; } = 256L * 1024 * 1024;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(2);
}

internal sealed class CheckpointSweepReceipt
{
    public int Examined { get; set; }
    public int CompletedRoots { get; set; }
    public long ReclaimedBytes { get; set; }
    public int EligibleRemaining { get; set; }
    public int Retained { get; set; }
    public Dictionary<string, int> Skips { get; } = new(StringComparer.Ordinal);
    public bool BudgetExhausted { get; set; }
    public TimeSpan Elapsed { get; set; }
    public void Skip(string reason) { Retained++; Skips[reason] = Skips.GetValueOrDefault(reason) + 1; }
}

internal sealed class CheckpointTempRootSweep
{
    private sealed class CursorState
    {
        public long Cursor { get; set; }
        public DateTimeOffset LastSweep { get; set; }
    }

    private readonly string _temp;
    private readonly ProcessIdentityProbe _probe;
    private readonly CheckpointSweepOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private static readonly Regex Name = new("^c723-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    public CheckpointTempRootSweep(string? temp = null, ProcessIdentityProbe? probe = null,
        CheckpointSweepOptions? options = null, Func<DateTimeOffset>? clock = null)
    {
        _temp = Path.GetFullPath(temp ?? Path.GetTempPath());
        _probe = probe ?? new ProcessIdentityProbe();
        _options = options ?? new CheckpointSweepOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private string LockPath => Path.Combine(_temp, ".checkpoint-temp-coordinator.lock");
    private string IndexPath => Path.Combine(_temp, ".checkpoint-temp-roots.jsonl");
    private string StatePath => Path.Combine(_temp, ".checkpoint-temp-cursor.json");

    public void Register(string root)
    {
        if (!IsCandidate(root) || ReadMarker(root) is null)
            throw new InvalidOperationException("checkpoint root marker is invalid");
        FileStream? gate = null;
        var admission = Stopwatch.StartNew();
        while (gate is null && admission.Elapsed < TimeSpan.FromSeconds(30))
        {
            gate = OpenGate();
            if (gate is null) Thread.Sleep(20);
        }
        using var held = gate ?? throw new IOException("checkpoint coordinator is busy");
        using var index = new FileStream(IndexPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Path.GetFullPath(root)) + "\n");
        index.Write(bytes);
        index.Flush(flushToDisk: true);
    }

    public CheckpointSweepReceipt SweepOnce(TextWriter? output = null)
    {
        var receipt = new CheckpointSweepReceipt();
        var timer = Stopwatch.StartNew();
        using var gate = OpenGate();
        if (gate is null) { receipt.Skip("coordinator-busy"); return receipt; }
        var state = ReadState();
        if (_clock() - state.LastSweep < _options.Interval)
        { receipt.Skip("interval"); return receipt; }
        state.LastSweep = _clock();
        if (!File.Exists(IndexPath)) { WriteState(state); return receipt; }
        using var index = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (state.Cursor >= index.Length) state.Cursor = 0;
        var stopAt = state.Cursor;
        index.Position = state.Cursor;
        var wrapped = false;
        while (receipt.Examined < _options.MaxDirectEntries && receipt.CompletedRoots < _options.MaxRoots
               && receipt.ReclaimedBytes < _options.MaxBytes && timer.Elapsed < _options.MaxDuration)
        {
            if (wrapped && index.Position >= stopAt) break;
            var line = ReadLine(index);
            if (line is null)
            {
                if (wrapped || index.Length == 0) break;
                index.Position = 0;
                wrapped = true;
                continue;
            }
            receipt.Examined++;
            string root;
            try { root = JsonSerializer.Deserialize<string>(line) ?? ""; }
            catch (JsonException) { receipt.Skip("index-malformed"); continue; }
            if (!IsCandidate(root)) { receipt.Skip("name-or-depth"); continue; }
            if (!Directory.Exists(root)) { receipt.Skip("absent"); continue; }
            if (!ContainedCleanup.SafeAncestors(root)) { receipt.Skip("linked-root-or-ancestor"); continue; }
            if (ReadMarker(root) is null) { receipt.Skip("marker-invalid"); continue; }
            using var rootGate = TestRootGuard.TryLock(root);
            if (rootGate is null) { receipt.Skip("root-busy"); continue; }
            var reason = Eligible(root, timer);
            if (reason is not null) { receipt.Skip(reason); continue; }
            var marker = ReadMarker(root)!;
            if (marker.State is not ("active" or "deleting")) { receipt.Skip("marker-state"); continue; }
            marker.State = "deleting";
            try { TestRootGuard.Write(root, marker); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { receipt.Skip("marker-write-" + ex.GetType().Name); continue; }
            receipt.EligibleRemaining++;
            var allowance = _options.MaxBytes - receipt.ReclaimedBytes;
            try
            {
                var (bytes, complete) = DeleteBounded(root, allowance, timer);
                receipt.ReclaimedBytes += bytes;
                if (complete)
                {
                    rootGate.Dispose();
                    File.Delete(Path.Combine(root, TestRootGuard.LockName));
                    File.Delete(Path.Combine(root, CheckpointTestScope.MarkerName));
                    Directory.Delete(root);
                    receipt.CompletedRoots++;
                    receipt.EligibleRemaining--;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { receipt.Skip("delete-" + ex.GetType().Name); }
        }
        state.Cursor = index.Position;
        WriteState(state);
        receipt.BudgetExhausted = receipt.Examined >= _options.MaxDirectEntries
            || receipt.CompletedRoots >= _options.MaxRoots || receipt.ReclaimedBytes >= _options.MaxBytes
            || timer.Elapsed >= _options.MaxDuration;
        receipt.Elapsed = timer.Elapsed;
        output?.WriteLine($"CHECKPOINT TEMP examined={receipt.Examined} deleted={receipt.CompletedRoots} "
            + $"bytes={receipt.ReclaimedBytes} remainingEligible={receipt.EligibleRemaining} "
            + $"retained={receipt.Retained} budget={receipt.BudgetExhausted} elapsedMs={receipt.Elapsed.TotalMilliseconds:F0}");
        return receipt;
    }

    private string? Eligible(string root, Stopwatch timer)
    {
        if (timer.Elapsed >= _options.MaxDuration) return "time-budget";
        if (!IsCandidate(root)) return "name-or-depth";
        if (!Directory.Exists(root)) return "absent";
        if (!ContainedCleanup.SafeAncestors(root) || !ContainedCleanup.SafeTree(root, _options.MaxDescendantEntries))
            return "inventory-incomplete-or-linked";
        var marker = ReadMarker(root);
        if (marker is null) return "marker-invalid";
        if (_clock() - marker.CreatedAt < _options.Grace) return "grace";
        var owner = _probe.Observe(marker.Owner);
        if (owner.Verdict != ProcessVerdict.Dead) return owner.Reason;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var hasRun = File.Exists(Path.Combine(directory, RunOwnershipStore.FileName));
            var looksLikeRun = hasRun || Directory.Exists(Path.Combine(directory, "tool"))
                || File.Exists(Path.Combine(directory, "manifest.resolved.yaml"))
                || File.Exists(Path.Combine(directory, "request.json"));
            if (!looksLikeRun) continue;
            if (!hasRun) return "nested-custody-missing";
            var journal = RunOwnershipStore.Read(directory);
            if (journal is null) return "nested-custody-invalid";
            var observed = journal.Phase == "preparing"
                ? _probe.Observe(journal.Starter)
                : new ToolCopyCleanup(_probe).ObserveExecutor(directory);
            if (observed.Verdict != ProcessVerdict.Dead) return "nested-" + observed.Reason;
        }
        return null;
    }

    private (long Bytes, bool Complete) DeleteBounded(string root, long allowance, Stopwatch timer)
    {
        if (allowance <= 0) return (0, false);
        var bytes = 0L;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetFileName(path) == CheckpointTestScope.MarkerName ? 2
                : Path.GetFileName(path) is RunOwnershipStore.FileName or "executor-ownership.json" ? 1 : 0)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToList();
        foreach (var file in files)
        {
            if (timer.Elapsed >= _options.MaxDuration) return (bytes, false);
            var name = Path.GetFileName(file);
            if (name == CheckpointTestScope.MarkerName || name is RunOwnershipStore.FileName or "executor-ownership.json"
                or TestRootGuard.LockName or TestRootGuard.RunsName)
                continue;
            var length = new FileInfo(file).Length;
            var left = allowance - bytes;
            if (left <= 0) return (bytes, false);
            if (length > left)
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None);
                stream.SetLength(length - left);
                stream.Flush(flushToDisk: true);
                return (bytes + left, false);
            }
            File.Delete(file);
            bytes += length;
        }
        // Critical custody is retained until ordinary payload removal has finished.
        if (timer.Elapsed >= _options.MaxDuration) return (bytes, false);
        foreach (var file in files.Where(File.Exists))
        {
            if (Path.GetFileName(file) is CheckpointTestScope.MarkerName or TestRootGuard.LockName) continue;
            File.Delete(file);
        }
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            Directory.Delete(directory);
        return (bytes, true);
    }

    private bool IsCandidate(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return RunOwnershipStore.SamePath(Path.GetDirectoryName(full)!, _temp)
                && Name.IsMatch(Path.GetFileName(full));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }

    private CheckpointRootMarker? ReadMarker(string root)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<CheckpointRootMarker>(
                File.ReadAllText(Path.Combine(root, CheckpointTestScope.MarkerName)));
            return marker is { Version: 1, Owner: not null }
                && marker.RootId == Path.GetFileName(root)["c723-".Length..]
                && RunOwnershipStore.SamePath(marker.RootPath, root) ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or NotSupportedException) { return null; }
    }

    private FileStream? OpenGate()
    {
        try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private CursorState ReadState()
    {
        try { return JsonSerializer.Deserialize<CursorState>(File.ReadAllText(StatePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    private void WriteState(CursorState state)
    {
        var temp = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, StatePath, true);
    }

    private static string? ReadLine(FileStream stream)
    {
        using var buffer = new MemoryStream();
        while (buffer.Length < 4096)
        {
            var next = stream.ReadByte();
            if (next < 0) return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            if (next == '\n') return Encoding.UTF8.GetString(buffer.ToArray());
            buffer.WriteByte((byte)next);
        }
        throw new IOException("checkpoint temp index line exceeds 4096 bytes");
    }
}

internal static class CheckpointTempSweepAssemblyHook
{
    [Before(Assembly)]
    public static void Sweep()
    {
        if (Environment.GetEnvironmentVariable("C804_ORPHAN_SWEEP_ROOT") is not null) return;
        new CheckpointTempRootSweep().SweepOnce(Console.Out);
    }

    [Before(Assembly)]
    public static void RecordSelectedRoster(AssemblyHookContext context)
    {
        var target = Environment.GetEnvironmentVariable("C804_ROSTER_FILE");
        if (string.IsNullOrWhiteSpace(target)) return;
        var selected = context.AllTests.Select(test => new
        {
            id = test.Id,
            className = test.Metadata.TestDetails.ClassType.FullName,
            method = test.Metadata.TestDetails.MethodName,
            displayName = test.Metadata.TestDetails.TestName,
        }).OrderBy(test => test.id).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, selected);
            stream.Flush(flushToDisk: true);
        }
        catch (IOException) when (File.Exists(target))
        {
            // A nested TUnit child inherits the observer environment. The first
            // assembly owns this roster; a later child cannot replace it.
        }
    }
}
