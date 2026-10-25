using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

internal sealed class CheckpointTestScope : IAsyncDisposable
{
    internal const string MarkerName = TestRootGuard.MarkerName;
    private readonly List<string> _roots = [];
    private readonly List<Task> _work = [];
    private readonly List<ProcessIdentity> _children = [];
    private readonly HashSet<ProcessIdentity> _syntheticChildren = [];
    private readonly CheckpointCleanupContext _context;
    private readonly ICheckpointCleanupIo _io;
    private readonly string _temp;
    private readonly ProcessIdentityProbe _probe;
    private readonly Action<string>? _beforeMarker;
    private readonly Action<string, IReadOnlyList<string>>? _beforeReturn;
    private readonly Action<string>? _beforeDelete;
    private readonly TimeProvider _cleanupClock;
    private readonly string _attemptId = Guid.NewGuid().ToString("N");
    private bool _sealed;

    public CheckpointTestScope(ProcessIdentityProbe? probe = null, Action<string>? beforeMarker = null,
        Action<string, IReadOnlyList<string>>? beforeReturn = null, Action<string>? beforeDelete = null,
        TimeProvider? cleanupClock = null)
        : this(CheckpointCleanupDefaults.Context, Path.GetTempPath(), probe, beforeMarker,
            beforeReturn, beforeDelete, cleanupClock) { }

    public CheckpointTestScope(CheckpointCleanupContext context, string temp,
        ProcessIdentityProbe? probe = null, Action<string>? beforeMarker = null,
        Action<string, IReadOnlyList<string>>? beforeReturn = null, Action<string>? beforeDelete = null,
        TimeProvider? cleanupClock = null)
    {
        _context = context;
        _io = context.Io;
        _temp = Path.GetFullPath(temp);
        _probe = probe ?? new ProcessIdentityProbe(context);
        _beforeMarker = beforeMarker;
        _beforeReturn = beforeReturn;
        _beforeDelete = beforeDelete;
        _cleanupClock = cleanupClock ?? context.Time;
    }

    public IReadOnlyList<string> Roots => _roots;

    public string TempDir()
    {
        if (_sealed) throw new InvalidOperationException("checkpoint test scope is sealed");
        var id = Guid.CreateVersion7().ToString("N");
        var path = Path.Combine(_temp, "c723-" + id);
        _io.CreateDirectory(path);
        try
        {
            var owner = _probe.Current();
            var marker = new CheckpointRootMarker
            {
                RootId = id, AttemptId = _attemptId,
                AssemblyInvocationId = $"{owner.Host}:{owner.Boot}:{owner.PidNamespace}:{owner.Pid}:{owner.StartUtcTicks}",
                CreatedAt = _context.Time.GetUtcNow(), RootPath = Path.GetFullPath(path), Owner = owner,
            };
            var markerPath = Path.Combine(path, MarkerName);
            _beforeMarker?.Invoke(path);
            var write = new CheckpointWrite(_context);
            SafeWrite.Require(write.OpenCreate(markerPath, write.Under(path), out var markerFile));
            using (markerFile)
                JsonSerializer.Serialize(markerFile!, marker);
            var coordinator = new CheckpointTempRootSweep(_context, _temp);
            coordinator.Register(path);
            // Registration precedes the caller's first observation of the root.
            _roots.Add(path);
            CheckpointUsageEvent.Write("root-create", path, context: _context);
            _beforeReturn?.Invoke(path, _roots);
            coordinator.SweepOnce(Console.Out);
            return path;
        }
        catch
        {
            var marker = Path.Combine(path, MarkerName);
            if (_io.FileExists(marker)) _io.Unlink(marker);
            if (_io.DirectoryExists(path) && !_io.Enumerate(path).Any()) _io.RemoveDirectory(path, recursive: false);
            throw;
        }
    }

    public void Register(Task task)
    {
        if (_sealed) throw new InvalidOperationException("checkpoint test scope is sealed");
        _work.Add(task);
    }

    public void Register(Process process)
    {
        if (_sealed) throw new InvalidOperationException("checkpoint test scope is sealed");
        try
        {
            _children.Add(_probe.Capture(process.Id));
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or ArgumentException)
        {
            // The child exited before its start token could be read. Nothing remains to kill.
            // A non-positive token would fail dispose as identity-missing, so the child is not recorded.
        }
    }

    public void Register(ProcessIdentity child)
    {
        if (_sealed) throw new InvalidOperationException("checkpoint test scope is sealed");
        _children.Add(child);
        _syntheticChildren.Add(child);
    }

    public ValueTask DisposeAsync() => DisposeAsync(null);

    public async ValueTask DisposeAsync(Exception? originalFailure)
    {
        _sealed = true;
        var failures = new List<string>();
        if (originalFailure is not null) failures.Add("original test failure: " + originalFailure.Message);
        var unsafeChild = false;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10), _cleanupClock);
        foreach (var work in _work)
        {
            try { await work.WaitAsync(cleanup.Token); }
            catch (Exception ex) { failures.Add("registered work: " + ex.Message); }
        }
        // A failed join is not permission to remove paths that work can still write.
        var unfinishedWork = _work.Any(work => !work.IsCompleted);
        foreach (var child in _children)
        {
            var observed = _probe.Observe(child);
            if (observed.Verdict == ProcessVerdict.AliveSame)
            {
                if (_syntheticChildren.Contains(child))
                { failures.Add("child " + child.Pid + ": " + observed.Reason); unsafeChild = true; continue; }
                try
                {
                    using var process = Process.GetProcessById(child.Pid);
                    if (_probe.Observe(child).Verdict == ProcessVerdict.AliveSame)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(cleanup.Token);
                    }
                }
                catch (Exception ex) { failures.Add("child " + child.Pid + ": " + ex.Message); unsafeChild = true; }
            }
            else if (observed.Verdict is ProcessVerdict.Unknown or ProcessVerdict.ReusedPid)
            { failures.Add("child " + child.Pid + ": " + observed.Reason); unsafeChild = true; }
        }
        foreach (var root in _roots)
        {
            try
            {
                if (!_io.DirectoryExists(root)) continue;
                if (unfinishedWork) { failures.Add(root + ": unfinished registered work; root retained"); continue; }
                if (unsafeChild) { failures.Add(root + ": live or uncertain child"); continue; }
                if (!ContainedCleanup.SafeAncestors(root, _context) || !ContainedCleanup.SafeTree(root, 100000, _context))
                    throw new IOException("linked or incomplete root inventory");
                foreach (var journal in Journals(root))
                {
                    var run = Path.GetDirectoryName(journal)!;
                    var record = RunOwnershipStore.Read(run, _context);
                    if (record is null) throw new IOException("invalid nested run custody: " + run);
                    if (record.Phase is "launched" or "launch-attempted")
                    {
                        var status = new ToolCopyCleanup(_probe, context: _context).ObserveExecutor(run);
                        var child = ExecutorOwnershipStore.Read(run, _context)?.Executor ?? record.Launched;
                        var inline = child is not null && child == _probe.Current();
                        if (status.Verdict != ProcessVerdict.Dead && !inline)
                            throw new IOException("nested executor " + status.Reason + ": " + run);
                    }
                }
                _beforeDelete?.Invoke(root);
                _io.RemoveDirectory(root, recursive: true);
                new CheckpointTempRootSweep(_context, _temp).Unregister(root);
                CheckpointUsageEvent.Write("root-delete", root, context: _context);
            }
            catch (Exception ex) { failures.Add(root + ": " + ex.Message); }
        }
        if (failures.Count > 0)
            throw new IOException("checkpoint fixture teardown failed: " + string.Join("; ", failures));
    }

    private IEnumerable<string> Journals(string root)
    {
        foreach (var entry in _io.Enumerate(root))
        {
            if ((_io.Attributes(entry) & FileAttributes.Directory) != 0)
            {
                foreach (var journal in Journals(entry)) yield return journal;
            }
            else if (string.Equals(Path.GetFileName(entry), RunOwnershipStore.FileName, _context.PathComparison))
                yield return entry;
        }
    }
}

public abstract class CheckpointTestBase
{
    private CheckpointTestScope? _scope;

    [Before(Test)]
    public void OpenCheckpointScope() => _scope = new CheckpointTestScope();

    [After(Test)]
    public async Task CloseCheckpointScopeAsync()
    {
        if (_scope is not null)
        {
            CheckpointNativeDiagnostics.Capture(GetType().Name, _scope.Roots);
            await _scope.DisposeAsync();
        }
    }

    protected string TempDir() => (_scope ?? throw new InvalidOperationException("checkpoint test has no scope")).TempDir();
    protected string TinyToolDirectory()
    {
        var source = Path.Combine(TempDir(), "tool-source");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "Antiphon.Checkpoints.dll"), [0x43, 0x38, 0x30, 0x34]);
        File.WriteAllText(Path.Combine(source, "Antiphon.Checkpoints.deps.json"), "{}");
        File.WriteAllText(Path.Combine(source, "sentinel.txt"), "tiny-tool-source");
        return source;
    }
    protected void RegisterCheckpointWork(Task work) => (_scope ?? throw new InvalidOperationException("checkpoint test has no scope")).Register(work);
    protected void RegisterCheckpointChild(Process child) => (_scope ?? throw new InvalidOperationException("checkpoint test has no scope")).Register(child);
}
