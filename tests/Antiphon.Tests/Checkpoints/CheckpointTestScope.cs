using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

internal sealed class CheckpointTestScope : IAsyncDisposable
{
    internal const string MarkerName = TestRootGuard.MarkerName;
    private readonly List<string> _roots = [];
    private readonly List<Task> _work = [];
    private readonly List<ProcessIdentity> _children = [];
    private readonly ProcessIdentityProbe _probe;
    private readonly Action<string>? _beforeMarker;
    private readonly Action<string, IReadOnlyList<string>>? _beforeReturn;
    private readonly Action<string>? _beforeDelete;
    private readonly string _attemptId = Guid.NewGuid().ToString("N");
    private bool _sealed;

    public CheckpointTestScope(ProcessIdentityProbe? probe = null, Action<string>? beforeMarker = null,
        Action<string, IReadOnlyList<string>>? beforeReturn = null, Action<string>? beforeDelete = null)
    {
        _probe = probe ?? new ProcessIdentityProbe();
        _beforeMarker = beforeMarker;
        _beforeReturn = beforeReturn;
        _beforeDelete = beforeDelete;
    }

    public IReadOnlyList<string> Roots => _roots;

    public string TempDir()
    {
        if (_sealed) throw new InvalidOperationException("checkpoint test scope is sealed");
        var id = Guid.CreateVersion7().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), "c723-" + id);
        Directory.CreateDirectory(path);
        try
        {
            var owner = _probe.Current();
            var marker = new CheckpointRootMarker
            {
                RootId = id, AttemptId = _attemptId,
                AssemblyInvocationId = $"{owner.Host}:{owner.Boot}:{owner.PidNamespace}:{owner.Pid}:{owner.StartUtcTicks}",
                CreatedAt = DateTimeOffset.UtcNow, RootPath = Path.GetFullPath(path), Owner = owner,
            };
            var markerPath = Path.Combine(path, MarkerName);
            _beforeMarker?.Invoke(path);
            using (var file = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(file, marker);
            var coordinator = new CheckpointTempRootSweep();
            coordinator.Register(path);
            // Registration precedes the caller's first observation of the root.
            _roots.Add(path);
            CheckpointUsageEvent.Write("root-create", path);
            _beforeReturn?.Invoke(path, _roots);
            coordinator.SweepOnce(Console.Out);
            return path;
        }
        catch
        {
            var marker = Path.Combine(path, MarkerName);
            if (File.Exists(marker)) File.Delete(marker);
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
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
        _children.Add(_probe.Capture(process.Id));
    }

    public ValueTask DisposeAsync() => DisposeAsync(null);

    public async ValueTask DisposeAsync(Exception? originalFailure)
    {
        _sealed = true;
        var failures = new List<string>();
        if (originalFailure is not null) failures.Add("original test failure: " + originalFailure.Message);
        var unsafeChild = false;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var work in _work)
        {
            try { await work.WaitAsync(cleanup.Token); }
            catch (Exception ex) { failures.Add("registered work: " + ex.Message); }
        }
        foreach (var child in _children)
        {
            var observed = _probe.Observe(child);
            if (observed.Verdict == ProcessVerdict.AliveSame)
            {
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
                if (!Directory.Exists(root)) continue;
                if (unsafeChild) { failures.Add(root + ": live or uncertain child"); continue; }
                if (!ContainedCleanup.SafeAncestors(root) || !ContainedCleanup.SafeTree(root, 100000))
                    throw new IOException("linked or incomplete root inventory");
                foreach (var journal in Directory.EnumerateFiles(root, RunOwnershipStore.FileName, SearchOption.AllDirectories))
                {
                    var run = Path.GetDirectoryName(journal)!;
                    var record = RunOwnershipStore.Read(run);
                    if (record is null) throw new IOException("invalid nested run custody: " + run);
                    if (record.Phase is "launched" or "launch-attempted")
                    {
                        var status = new ToolCopyCleanup(_probe).ObserveExecutor(run);
                        var child = ExecutorOwnershipStore.Read(run)?.Executor ?? record.Launched;
                        var inline = child is not null && child == _probe.Current();
                        if (status.Verdict != ProcessVerdict.Dead && !inline)
                            throw new IOException("nested executor " + status.Reason + ": " + run);
                    }
                }
                _beforeDelete?.Invoke(root);
                Directory.Delete(root, recursive: true);
                new CheckpointTempRootSweep().Unregister(root);
                CheckpointUsageEvent.Write("root-delete", root);
            }
            catch (Exception ex) { failures.Add(root + ": " + ex.Message); }
        }
        if (failures.Count > 0)
            throw new IOException("checkpoint fixture teardown failed: " + string.Join("; ", failures));
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
            await _scope.DisposeAsync();
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
