using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

public sealed class CheckpointSweepOptions
{
    public TimeSpan Grace { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxDirectEntries { get; init; } = 512;
    public int MaxDescendantEntries { get; init; } = 10000;
    public int MaxRoots { get; init; } = 16;
    public long MaxBytes { get; init; } = 256L * 1024 * 1024;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed class CheckpointSweepReceipt
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

public sealed class CheckpointTempRootSweep
{
    private sealed class CursorState
    {
        public string? LastRootId { get; set; }
        public long LegacyCursor { get; set; }
        public DateTimeOffset LastSweep { get; set; }
    }

    private readonly string _temp;
    private readonly ProcessIdentityProbe _probe;
    private readonly CheckpointSweepOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CheckpointCleanupContext _context;
    private readonly ICheckpointCleanupIo _io;
    private static readonly Regex Name = new("^c723-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    public CheckpointTempRootSweep(string? temp = null, ProcessIdentityProbe? probe = null,
        CheckpointSweepOptions? options = null, Func<DateTimeOffset>? clock = null)
        : this(CheckpointCleanupDefaults.Context, temp ?? Path.GetTempPath(), probe, options, clock) { }

    public CheckpointTempRootSweep(CheckpointCleanupContext context, string temp, ProcessIdentityProbe? probe = null,
        CheckpointSweepOptions? options = null, Func<DateTimeOffset>? clock = null)
    {
        _context = context;
        _io = context.Io;
        _temp = Path.GetFullPath(temp);
        _probe = probe ?? new ProcessIdentityProbe(context);
        _options = options ?? new CheckpointSweepOptions();
        _clock = clock ?? context.Time.GetUtcNow;
    }

    private string LockPath => Path.Combine(_temp, ".checkpoint-temp-coordinator.lock");
    private string IndexDirectory => Path.Combine(_temp, ".checkpoint-temp-roots");
    private string LegacyIndexPath => Path.Combine(_temp, ".checkpoint-temp-roots.jsonl");
    private string StatePath => Path.Combine(_temp, ".checkpoint-temp-cursor.json");
    private string IndexFile(string root) => Path.Combine(IndexDirectory, Path.GetFileName(root) + ".json");

    public void Register(string root)
    {
        if (!IsCandidate(root) || ReadMarker(root) is null)
            throw new InvalidOperationException("checkpoint root marker is invalid");
        WriteIndexEntry(root);
    }

    private void WriteIndexEntry(string root)
    {
        var pending = "";
        var armed = false;
        try
        {
            _io.CreateDirectory(IndexDirectory);
            if ((_io.Attributes(IndexDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("checkpoint index directory is linked");
            pending = IndexFile(root) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var text = JsonSerializer.Serialize(Path.GetFullPath(root));
            WriteText(pending, text);
            armed = true;
            ReplacementPublication.Publish(_io, pending, IndexFile(root), Encoding.UTF8.GetBytes(text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The root's own marker is already durable enough to keep it visible to
            // inspection. Index failure must never turn a valid allocation into a failure.
            // A publication that lost the destination keeps its temporary for recovery.
            if (!armed && pending.Length > 0)
            {
                try { _io.Unlink(pending); }
                catch (IOException) { }
            }
        }
    }

    public void Unregister(string root)
    {
        if (!IsCandidate(root)) return;
        try { _io.Unlink(IndexFile(root)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public CheckpointSweepReceipt SweepOnce(TextWriter? output = null)
    {
        var receipt = new CheckpointSweepReceipt();
        var started = _context.Time.GetTimestamp();
        if (_clock() - ReadState().LastSweep < _options.Interval)
        { receipt.Skip("interval"); return receipt; }
        using var gate = OpenGate();
        if (gate is null) { receipt.Skip("coordinator-busy"); return receipt; }
        var state = ReadState();
        if (_clock() - state.LastSweep < _options.Interval)
        { receipt.Skip("interval"); return receipt; }
        state.LastSweep = _clock();
        MigrateLegacyIndex(state, started);
        if (!_io.DirectoryExists(IndexDirectory)) { WriteState(state); return receipt; }
        if ((_io.Attributes(IndexDirectory) & FileAttributes.ReparsePoint) != 0)
        { receipt.Skip("index-linked"); return receipt; }
        RemoveStaleIndexTemps(started);
        // Entries exist only while roots are live or retained. Sorting the small active
        // roster gives a stable cursor even as other test hosts add and remove roots.
        var entries = EnumerateIndexFiles("c723-*.json")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        var start = Array.FindIndex(entries, entry => string.CompareOrdinal(
            Path.GetFileName(entry), state.LastRootId) > 0);
        if (start < 0) start = 0;
        for (var scanned = 0; scanned < entries.Length && receipt.Examined < _options.MaxDirectEntries
             && receipt.CompletedRoots < _options.MaxRoots && receipt.ReclaimedBytes < _options.MaxBytes
             && Elapsed(started) < _options.MaxDuration; scanned++)
        {
            var entry = entries[(start + scanned) % entries.Length];
            state.LastRootId = Path.GetFileName(entry);
            receipt.Examined++;
            string root;
            try { root = JsonSerializer.Deserialize<string>(ReadText(entry)) ?? ""; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { receipt.Skip("index-malformed-or-removed"); continue; }
            if (!IsCandidate(root) || !RunOwnershipStore.SamePath(IndexFile(root), entry, _context))
            { receipt.Skip("name-or-depth"); continue; }
            if (!_io.DirectoryExists(root))
            { Unregister(root); receipt.Skip("absent"); continue; }
            if (!ContainedCleanup.SafeAncestors(root, _context)) { receipt.Skip("linked-root-or-ancestor"); continue; }
            // Preflight only excludes candidates: a live/uncertain owner must be
            // able to register a nested run without contending with this sweep.
            // None of these observations grants permission to delete. Eligible
            // re-reads all authority, identity and inventory facts under rootGate.
            var preflightMarker = ReadMarker(root);
            if (preflightMarker is null) { receipt.Skip("marker-invalid"); continue; }
            if (_clock() - preflightMarker.CreatedAt < _options.Grace)
            { receipt.Skip("grace"); continue; }
            if (Elapsed(started) >= _options.MaxDuration) { receipt.Skip("time-budget"); continue; }
            var preflightOwner = _probe.Observe(preflightMarker.Owner);
            if (preflightOwner.Verdict != ProcessVerdict.Dead)
            { receipt.Skip(preflightOwner.Reason); continue; }
            using var rootGate = TestRootGuard.TryLock(_context, root);
            if (rootGate is null) { receipt.Skip("root-busy"); continue; }
            var reason = Eligible(root, started);
            if (reason is not null) { receipt.Skip(reason); continue; }
            var marker = ReadMarker(root)!;
            if (marker.State is not ("active" or "deleting")) { receipt.Skip("marker-state"); continue; }
            marker.State = "deleting";
            try { TestRootGuard.MarkDeleting(_context, root, marker, rootGate, _probe); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { receipt.Skip("marker-write-" + ex.GetType().Name); continue; }
            receipt.EligibleRemaining++;
            var allowance = _options.MaxBytes - receipt.ReclaimedBytes;
            try
            {
                var (bytes, complete) = DeleteBounded(root, allowance, started);
                receipt.ReclaimedBytes += bytes;
                if (complete)
                {
                    rootGate.Dispose();
                    // The same nofollow walk as the payload. Kernel 4.15 has no openat2.
                    UnlinkEntry(Path.Combine(root, TestRootGuard.LockName), directory: false);
                    UnlinkEntry(Path.Combine(root, TestRootGuard.MarkerName), directory: false);
                    UnlinkEntry(root, directory: true);
                    Unregister(root);
                    receipt.CompletedRoots++;
                    receipt.EligibleRemaining--;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { receipt.Skip("delete-" + ex.GetType().Name); }
        }
        WriteState(state);
        receipt.BudgetExhausted = receipt.Examined >= _options.MaxDirectEntries
            || receipt.CompletedRoots >= _options.MaxRoots || receipt.ReclaimedBytes >= _options.MaxBytes
            || Elapsed(started) >= _options.MaxDuration;
        receipt.Elapsed = Elapsed(started);
        output?.WriteLine($"CHECKPOINT TEMP examined={receipt.Examined} deleted={receipt.CompletedRoots} "
            + $"bytes={receipt.ReclaimedBytes} remainingEligible={receipt.EligibleRemaining} "
            + $"retained={receipt.Retained} budget={receipt.BudgetExhausted} elapsedMs={receipt.Elapsed.TotalMilliseconds:F0}");
        return receipt;
    }

    private string? Eligible(string root, long started)
    {
        if (Elapsed(started) >= _options.MaxDuration) return "time-budget";
        if (!IsCandidate(root)) return "name-or-depth";
        if (!_io.DirectoryExists(root)) return "absent";
        if (!ContainedCleanup.SafeAncestors(root, _context) || !AnchoredInventory(root))
            return "inventory-incomplete-or-linked";
        var marker = ReadMarker(root);
        if (marker is null) return "marker-invalid";
        if (_clock() - marker.CreatedAt < _options.Grace) return "grace";
        var owner = _probe.Observe(marker.Owner);
        if (owner.Verdict != ProcessVerdict.Dead) return owner.Reason;
        foreach (var directory in EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var hasRun = _io.FileExists(Path.Combine(directory, RunOwnershipStore.FileName));
            var looksLikeRun = hasRun || _io.DirectoryExists(Path.Combine(directory, "tool"))
                || _io.FileExists(Path.Combine(directory, "manifest.resolved.yaml"))
                || _io.FileExists(Path.Combine(directory, "request.json"));
            if (!looksLikeRun) continue;
            if (!hasRun) return "nested-custody-missing";
            var journal = RunOwnershipStore.Read(directory, _context);
            if (journal is null) return "nested-custody-invalid";
            var observed = journal.Phase == "preparing"
                ? _probe.Observe(journal.Starter)
                : new ToolCopyCleanup(_probe, context: _context).ObserveExecutor(directory);
            if (observed.Verdict != ProcessVerdict.Dead) return "nested-" + observed.Reason;
        }
        return null;
    }

    private (long Bytes, bool Complete) DeleteBounded(string root, long allowance, long started)
    {
        if (allowance <= 0) return (0, false);
        var files = new List<(string Path, long Length)>();
        var directories = new List<string>();
        var specials = new List<string>();
        CollectAnchored(root, files, directories, specials);
        var bytes = 0L;
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var special in specials)
        {
            if (Elapsed(started) >= _options.MaxDuration) return (bytes, false);
            UnlinkEntry(special, directory: false);
            removed.Add(special);
        }
        foreach (var file in files
            .OrderBy(file => Path.GetFileName(file.Path) == TestRootGuard.MarkerName ? 2
                : Path.GetFileName(file.Path) is RunOwnershipStore.FileName or "executor-ownership.json" ? 1 : 0)
            .ThenBy(file => file.Path, StringComparer.Ordinal))
        {
            if (Elapsed(started) >= _options.MaxDuration) return (bytes, false);
            var name = Path.GetFileName(file.Path);
            if (name == TestRootGuard.MarkerName || name is RunOwnershipStore.FileName or "executor-ownership.json"
                or TestRootGuard.LockName or TestRootGuard.RunsName)
                continue;
            var length = file.Length;
            var left = allowance - bytes;
            if (left <= 0) return (bytes, false);
            if (length > left)
            {
                var directory = Path.GetDirectoryName(file.Path);
                if (string.IsNullOrEmpty(directory)) throw new IOException("anchored-path-unsafe");
                try
                {
                    _io.TruncateAnchored(directory, name, length - left);
                }
                catch (AnchoredHardLinkException)
                {
                    // The other name keeps the bytes, so this name is removed and not counted.
                    UnlinkEntry(file.Path, directory: false);
                    removed.Add(file.Path);
                    continue;
                }
                return (bytes + left, false);
            }
            UnlinkEntry(file.Path, directory: false);
            removed.Add(file.Path);
            bytes += length;
        }
        // Critical custody is retained until ordinary payload removal has finished.
        if (Elapsed(started) >= _options.MaxDuration) return (bytes, false);
        foreach (var file in files)
        {
            if (removed.Contains(file.Path)) continue;
            if (Path.GetFileName(file.Path) is TestRootGuard.MarkerName or TestRootGuard.LockName) continue;
            UnlinkEntry(file.Path, directory: false);
        }
        foreach (var directory in directories.OrderByDescending(path => path.Length))
            UnlinkEntry(directory, directory: true);
        return (bytes, true);
    }

    // Symlink, FIFO and socket names are leaves. A directory symlink is not entered.
    private bool AnchoredInventory(string root)
    {
        var count = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        try
        {
            while (pending.TryPop(out var directory))
            {
                if (++count > _options.MaxDescendantEntries) return false;
                foreach (var child in _io.ListAnchored(directory))
                {
                    if (child.Kind == "directory") pending.Push(Path.Combine(directory, child.Name));
                    else if (++count > _options.MaxDescendantEntries) return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private void CollectAnchored(string root, List<(string Path, long Length)> files, List<string> directories,
        List<string> specials)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in _io.ListAnchored(directory))
            {
                var path = Path.Combine(directory, child.Name);
                if (child.Kind == "directory")
                {
                    directories.Add(path);
                    pending.Push(path);
                }
                else if (child.Kind == "file") files.Add((path, child.Size));
                else specials.Add(path);
            }
        }
    }

    private void UnlinkEntry(string path, bool directory)
    {
        var parent = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new IOException("anchored-path-unsafe");
        _io.UnlinkAnchored(parent, name, directory);
    }

    private bool IsCandidate(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return RunOwnershipStore.SamePath(Path.GetDirectoryName(full)!, _temp, _context)
                && Name.IsMatch(Path.GetFileName(full));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }

    private CheckpointRootMarker? ReadMarker(string root)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<CheckpointRootMarker>(
                ReadText(Path.Combine(root, TestRootGuard.MarkerName)));
            return marker is { Version: 1, Owner: not null }
                && marker.RootId == Path.GetFileName(root)["c723-".Length..]
                && RunOwnershipStore.SamePath(marker.RootPath, root, _context) ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or ArgumentException or NotSupportedException) { return null; }
    }

    private Stream? OpenGate()
    {
        try { return _io.Open(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private CursorState ReadState()
    {
        try
        {
            using var stream = _io.Open(StatePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<CursorState>(stream) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    private void WriteState(CursorState state)
    {
        var temp = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var armed = false;
        try
        {
            var text = JsonSerializer.Serialize(state);
            WriteText(temp, text);
            armed = true;
            ReplacementPublication.Publish(_io, temp, StatePath, Encoding.UTF8.GetBytes(text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A reader or external handle may prevent replacement on Windows. Keep
            // the previous interval so the next sweep can retry without failing a test.
            // Do not delete a temporary whose destination was removed by the failure.
            if (!armed)
            {
                try { _io.Unlink(temp); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private void RemoveStaleIndexTemps(long started)
    {
        // Registration does not take the sweep gate. A fixed age protects an active
        // writer even when a test coordinator uses zero grace/interval overrides.
        foreach (var temp in EnumerateIndexFiles("*.tmp")
                     .Take(_options.MaxDirectEntries))
        {
            if (Elapsed(started) >= _options.MaxDuration) break;
            var name = Path.GetFileName(temp);
            if (!Regex.IsMatch(name, "^c723-[0-9a-f]{32}\\.json\\.[0-9a-f]{32}\\.tmp$",
                    RegexOptions.CultureInvariant)) continue;
            try
            {
                if (_clock().UtcDateTime - _io.LastWriteTimeUtc(temp) >= TimeSpan.FromMinutes(10))
                    _io.Unlink(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void MigrateLegacyIndex(CursorState state, long started)
    {
        if (!_io.FileExists(LegacyIndexPath)) return;
        // Earlier versions serialized all registrations into one JSONL file. Read it
        // under their coordinator gate, promoting only still-marked roots.
        using (var legacy = _io.Open(LegacyIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            legacy.Position = Math.Min(state.LegacyCursor, legacy.Length);
            while (Elapsed(started) < _options.MaxDuration)
            {
                var line = ReadLine(legacy);
                if (line is null) break;
                state.LegacyCursor = legacy.Position;
                string root;
                try { root = JsonSerializer.Deserialize<string>(line) ?? ""; }
                catch (JsonException) { continue; }
                if (IsCandidate(root) && _io.DirectoryExists(root))
                    WriteIndexEntry(root);
            }
            if (state.LegacyCursor < legacy.Length) return;
        }
        _io.Unlink(LegacyIndexPath);
        state.LegacyCursor = 0;
    }

    private TimeSpan Elapsed(long started) => _context.Time.GetElapsedTime(started);

    private string ReadText(string path)
    {
        using var stream = _io.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void WriteText(string path, string text) => _io.WriteBytes(path, Encoding.UTF8.GetBytes(text));

    private IEnumerable<string> EnumerateIndexFiles(string pattern)
    {
        foreach (var entry in _io.Enumerate(IndexDirectory))
        {
            FileAttributes attributes;
            try { attributes = _io.Attributes(entry); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Register/Unregister do not take the sweep gate. A listed index
                // entry or pending write may have disappeared before this read.
                continue;
            }
            if ((attributes & FileAttributes.Directory) == 0 && Matches(pattern, entry))
                yield return entry;
        }
    }

    private IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption search) =>
        Entries(path, search).Where(entry => (_io.Attributes(entry) & FileAttributes.Directory) != 0
            && Matches(pattern, entry));

    private bool Matches(string pattern, string path) => FileSystemName.MatchesSimpleExpression(
        pattern, Path.GetFileName(path), ignoreCase: _io.Platform == CheckpointPlatform.Windows);

    private IEnumerable<string> Entries(string root, SearchOption search)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in _io.Enumerate(directory))
            {
                yield return entry;
                if (search == SearchOption.AllDirectories && (_io.Attributes(entry) & FileAttributes.Directory) != 0)
                    pending.Push(entry);
            }
        }
    }

    private static string? ReadLine(Stream stream)
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
