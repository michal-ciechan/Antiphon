using System.Diagnostics;
using Antiphon.Tests.Scripts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Integration"), Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DirectoryLinkFixtureWindowsTests
{
    [Test]
    public async Task C1061_Junction_fixture_creates_moves_and_disposes_owned_link()
    {
        using var scratch = new NativeJunctionScratch();
        var target = Directory.CreateDirectory(Path.Combine(scratch.Root, "target caf\u00e9 space")).FullName;
        var sentinel = Path.Combine(target, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "target survives");

        var syncPath = Path.Combine(scratch.Root, "sync caf\u00e9 link");
        var sync = await scratch.CreateSyncAsync(syncPath, target);
        sync.ShouldNotBeNull();
        AssertJunction(syncPath, "target survives");
        var moved = Path.Combine(scratch.Root, "moved caf\u00e9 link");
        scratch.ExpectAbsent(moved);
        try { sync.MoveTo(moved); AssertJunction(moved, "target survives"); }
        finally { sync.Dispose(); }
        DirectoryLink.IsLink(syncPath).ShouldBeFalse();
        DirectoryLink.IsLink(moved).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("target survives");

        var asyncPath = Path.Combine(scratch.Root, "async caf\u00e9 link");
        scratch.ExpectAbsent(asyncPath);
        await scratch.RunOwnedAsync(() => WithNativeAsync(async invocation =>
        {
            var run = invocation.StartJunction(asyncPath, target);
            var link = await run;
            link.ShouldNotBeNull();
            try
            {
                AssertJunction(asyncPath, "target survives");
                invocation.AssertClean();
                (await invocation.Windows.Root!.WaitForExitAsync(default)).ShouldBe(0);
            }
            finally { link.Dispose(); }
        }));
        File.ReadAllText(sentinel).ShouldBe("target survives");

        var ordinary = Directory.CreateDirectory(Path.Combine(scratch.Root, "ordinary")).FullName;
        await File.WriteAllTextAsync(Path.Combine(ordinary, "own.txt"), "ordinary survives");
        await scratch.RunOwnedAsync(() => WithNativeAsync(async invocation =>
        {
            (await invocation.StartJunction(ordinary, target)).ShouldBeNull();
            invocation.AssertClean();
        }));
        File.ReadAllText(Path.Combine(ordinary, "own.txt")).ShouldBe("ordinary survives");
        DirectoryLink.IsLink(ordinary).ShouldBeFalse();

        var bodyPath = Path.Combine(scratch.Root, "body failure link");
        var bodyError = new InvalidOperationException("intentional body sentinel");
        var actual = await CaptureAsync(Task.Run(async () =>
        {
            var link = await scratch.CreateAsync(bodyPath, target);
            link.ShouldNotBeNull();
            try { AssertJunction(bodyPath, "target survives"); throw bodyError; }
            finally { link.Dispose(); }
        }));
        ReferenceEquals(actual, bodyError).ShouldBeTrue();
        DirectoryLink.IsLink(bodyPath).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("target survives");

        var partial = Path.Combine(scratch.Root, "partial link");
        scratch.ExpectAbsent(partial);
        try
        {
            await scratch.RunOwnedAsync(() => WithNativeAsync(async invocation =>
            {
                var result = await invocation.StartCommand(partial, target);
                result.ExitCode.ShouldBe(0);
                invocation.AssertClean();
                DirectoryLink.IsLink(partial).ShouldBeTrue();
                DirectoryLink.Complete(partial, result with { ExitCode = 37 }).ShouldBeNull();
            }));
        }
        finally { if (scratch.CleanupConfirmed) scratch.RemoveCreatedLink(partial); }
        DirectoryLink.IsLink(partial).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("target survives");
    }

    [Test]
    public async Task C1061_Junction_command_joins_live_root_and_pipe_holders()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows required.");
        using var outer = new ScriptHarnessWindowsProcessFixture.OuterJob();
        foreach (var scenario in new[] { "LiveRoot", "ExitedStdout", "ExitedStderr", "HighVolume", "Nonzero" })
        {
            await WithNativeAsync(async invocation =>
            {
                invocation.Windows.Hooks.BeforeAssign = (job, root) =>
                {
                    outer.Assign(root);
                    outer.Sentinel.IsInJob(job).ShouldBeFalse("Outer-only sentinel must not join the private job.");
                };
                var elapsed = Stopwatch.StartNew();
                var run = invocation.StartCommand("unused", "unused", scenario, substituteScript: true);
                var held = scenario is "LiveRoot" or "ExitedStdout" or "ExitedStderr";
                if (held)
                {
                    var tree = await invocation.WaitReadyAsync(run);
                    if (scenario != "LiveRoot")
                    {
                        (await invocation.Windows.Root!.WaitForExitAsync(invocation.Token)).ShouldBe(0);
                        tree.Child.Executing().ShouldBeTrue("Root exit must leave the fixture pipe holder alive.");
                        tree.Grandchild.Executing().ShouldBeTrue();
                        run.IsCompleted.ShouldBeFalse("Root-only completion is insufficient.");
                        await WaitUntilAsync(() => scenario == "ExitedStdout"
                            ? invocation.Windows.Hooks.StderrEof : invocation.Windows.Hooks.StdoutEof, invocation.Token);
                    }
                    var error = await CaptureAsync(run);
                    error.ShouldBeOfType<TimeoutException>();
                    error!.Message.ShouldContain("execution budget");
                    elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
                    tree.Root.Executing().ShouldBeFalse();
                    tree.Child.Executing().ShouldBeFalse();
                    tree.Grandchild.Executing().ShouldBeFalse();
                }
                else
                {
                    var result = await run;
                    result.ExitCode.ShouldBe(scenario == "Nonzero" ? 37 : 0);
                    result.Stdout.ShouldContain(scenario == "Nonzero" ? "C806 stdout marker" : "C806 stdout end");
                    result.Stderr.ShouldContain(scenario == "Nonzero" ? "C806 stderr marker" : "C806 stderr end");
                }
                invocation.AssertClean();
                outer.Sentinel.Executing().ShouldBeTrue("Private cleanup must preserve the outer-only sentinel.");
            }, watchdogSeconds: 30);
        }
        outer.Sentinel.Executing().ShouldBeTrue();
    }

    private static void AssertJunction(string path, string contents)
    {
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
        File.ReadAllText(Path.Combine(path, "sentinel.txt")).ShouldBe(contents);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        while (!predicate()) await Task.Delay(10, deadline.Token);
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try { await task; return null; }
        catch (Exception error) { return error; }
    }

    private static async Task WithNativeAsync(Func<NativeInvocation, Task> body, int watchdogSeconds = 45)
    {
        await using var invocation = new NativeInvocation();
        var work = body(invocation);
        try { await work.WaitAsync(TimeSpan.FromSeconds(watchdogSeconds)); }
        finally
        {
            // Retain the body as well as the adapter task; a watchdog is never abandonment.
            invocation.Cancel();
            if (!work.IsCompleted)
            {
                await invocation.DisposeAsync();
                await CaptureAsync(work.WaitAsync(TimeSpan.FromSeconds(5)));
                work.IsCompleted.ShouldBeTrue("Native fixture body did not join after rescue.");
            }
        }
    }

    private sealed class NativeInvocation : IAsyncDisposable
    {
        internal ScriptHarnessWindowsProcessFixture Windows { get; } = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly List<uint> _accounting = [];
        private ScriptProcessRequest? _request;
        private Task? _run;
        private bool _disposed;
        internal CancellationToken Token => _cancel.Token;
        internal NativeInvocation()
        {
            Windows.Hooks.BeforeAccounting = job => _accounting.Add(ScriptHarnessWindowsProcessFixture.ReadActiveJobMembers(job));
            Windows.Hooks.BeforeCloseJob = () => _accounting.Add(ScriptHarnessWindowsProcessFixture.ReadActiveJobMembers(
                Windows.Hooks.Handles.Single(entry => entry.Name == "job").Handle));
        }
        private ScriptHarnessOptions Options(bool substituteScript)
        {
            var options = substituteScript
                ? ScriptHarnessProcessFixture.Options() with { AdditionalArguments = Windows.Arguments() }
                : DirectoryLinkCommand.DefaultOptions;
            return options with
            {
                ExecutablePath = ScriptHarnessProcessFixture.ResolveInstalledPowerShell(),
                OwnerFactory = request =>
                {
                    _request = request;
                    return new WindowsScriptHarnessProcess(request, Windows.Hooks);
                },
            };
        }
        internal Task<DirectoryLink?> StartJunction(string path, string target)
        {
            if (_run is not null) throw new InvalidOperationException("One invocation per fixture.");
            var run = Task.Run(() => DirectoryLink.TryCreateWindowsAsync(path, target, Options(false), ct: Token));
            _run = run;
            return run;
        }
        internal Task<ScriptHarnessResult> StartCommand(string path, string target,
            string scenario = "Create", bool substituteScript = false)
        {
            if (_run is not null) throw new InvalidOperationException("One invocation per fixture.");
            var run = Task.Run(() => DirectoryLinkCommand.RunAsync(path, target, Options(substituteScript), scenario, Token));
            _run = run;
            return run;
        }
        internal async Task<ScriptHarnessProcessFixture.ObservedTree> WaitReadyAsync(Task run)
        {
            await WaitUntilAsync(() => _request is not null, Token);
            return await Windows.WaitReadyAsync(_request!, run);
        }
        internal void AssertClean()
        {
            Windows.AssertStoppedBeforeDispose();
            Windows.Hooks.StdoutEof.ShouldBeTrue();
            Windows.Hooks.StderrEof.ShouldBeTrue();
            _accounting.ShouldNotBeEmpty();
            _accounting.Last().ShouldBe(0u, "Independent job accounting must reach zero before emergency cleanup.");
            _request.ShouldNotBeNull();
            Directory.Exists(_request.ResultsDirectory).ShouldBeFalse();
            Directory.Exists(_request.ControlDirectory).ShouldBeFalse();
        }
        internal void Cancel() => _cancel.Cancel();
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            var cleanup = Stopwatch.StartNew();
            _cancel.Cancel();
            Exception? failure = null;
            try
            {
                if (_run is not null)
                    try { await _run.WaitAsync(TimeSpan.FromSeconds(2)); }
                    catch (Exception error) when (_run.IsCompleted) { _ = error; }
            }
            catch (Exception error) { failure = error; }
            try { Windows.Dispose(TimeSpan.FromSeconds(5) - cleanup.Elapsed); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            finally { _cancel.Dispose(); }
            if (_run is { IsCompleted: false })
                await CaptureAsync(_run.WaitAsync(TimeSpan.FromSeconds(5) - cleanup.Elapsed));
            if (_run is not null) _run.IsCompleted.ShouldBeTrue("Owned adapter invocation must join.");
            if (failure is not null) throw failure;
        }
    }
}

/// <summary>Unique NTFS scratch; unknown process cleanup retains paths for diagnosis.</summary>
internal sealed class NativeJunctionScratch : IDisposable
{
    internal string Root { get; }
    private readonly HashSet<string> _createdEntries = [];
    internal bool CleanupConfirmed { get; private set; } = true;
    internal NativeJunctionScratch()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows qualification required.");
        Root = Directory.CreateTempSubdirectory("c1061-junction-").FullName;
        NativeHardLink.RequireNtfs(Root);
    }
    internal void ExpectAbsent(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) || DirectoryLink.IsLink(path))
            throw new IOException("Fixture link name must be absent.");
        _createdEntries.Add(path);
    }
    internal async Task<DirectoryLink?> CreateAsync(string path, string target)
    {
        ExpectAbsent(path);
        CleanupConfirmed = false;
        var result = await DirectoryLink.TryCreateWindowsAsync(path, target);
        CleanupConfirmed = true;
        return result;
    }
    internal async Task<DirectoryLink?> CreateSyncAsync(string path, string target)
    {
        ExpectAbsent(path);
        CleanupConfirmed = false;
        var run = Task.Run(() => DirectoryLink.TryCreate(path, target));
        // The underlying owned command is bounded by its 30+10 second allowances.
        DirectoryLink? result;
        try { result = await run.WaitAsync(TimeSpan.FromSeconds(45)); }
        finally
        {
            if (!run.IsCompleted) await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        CleanupConfirmed = true;
        return result;
    }
    internal async Task RunOwnedAsync(Func<Task> action)
    {
        CleanupConfirmed = false;
        await action();
        CleanupConfirmed = true;
    }
    internal void RemoveCreatedLink(string path)
    {
        if (!CleanupConfirmed) throw new IOException("Unconfirmed junction command cleanup; retained scratch: " + Root);
        if (!_createdEntries.Contains(path)) throw new InvalidOperationException("Not a fixture-owned link name.");
        if (DirectoryLink.IsLink(path)) Directory.Delete(path, recursive: false);
    }
    public void Dispose()
    {
        if (!CleanupConfirmed)
        {
            // The invocation error remains primary. Do not replace it during using/finally.
            Console.WriteLine("Unconfirmed junction command cleanup; retained scratch: " + Root);
            return;
        }
        foreach (var path in _createdEntries) RemoveCreatedLink(path);
        Directory.Delete(Root, true);
    }
}
