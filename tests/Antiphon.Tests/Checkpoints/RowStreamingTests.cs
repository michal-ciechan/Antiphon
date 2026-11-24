using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-0578 through the row verb, which scripts/run-checkpoint.ps1 launches since unit 20 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the C578 cases of the deleted
/// scripts/test-run-checkpoint.ps1, in process. The real ProcessDriver runs each child through a scripted process handle
/// that the test holds open, so what is on disk while the child lives is observed, not inferred: the build and run logs
/// are written as lines arrive, a failed build keeps its output and its exit and never runs the tests, and a build cut
/// off by the row deadline is killed, writes no success and starts no run.
/// </summary>
[Category("Unit")]
public sealed class RowStreamingTests : CheckpointTestBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Test]
    public async Task C578_build_and_run_logs_are_visible_while_their_child_lives()
    {
        var row = Start(out var factory, out var results);
        var build = await factory.NextAsync();
        build.Phase.ShouldBe("build");
        build.Out("c578-build-stdout");
        build.Err("c578-build-stderr");
        var buildLog = Path.Combine(results, "build.log");
        File.Exists(buildLog).ShouldBeTrue("the build log exists before the child exits");
        ReadShared(buildLog).ShouldContain("c578-build-stdout");
        ReadShared(buildLog).ShouldContain("c578-build-stderr");
        row.IsCompleted.ShouldBeFalse("the row waits for its build");
        factory.Count.ShouldBe(1, "a held build runs no tests");
        File.Exists(Path.Combine(results, "run.trx")).ShouldBeFalse();
        build.Exit(0);

        var run = await factory.NextAsync();
        run.Phase.ShouldBe("run");
        run.Out("c578-run-stdout");
        var runLog = Path.Combine(results, "console.log");
        ReadShared(runLog).ShouldContain("c578-run-stdout", customMessage: "the run log is written while the run lives");
        row.IsCompleted.ShouldBeFalse();
        CheckpointFixtures.WriteResults(Path.Combine(results, "run.trx"), ("Antiphon.Tests.Sample.runs", "Passed"));
        run.Exit(0);

        var (exit, text) = await row.WaitAsync(Patience);
        exit.ShouldBe(0, text);
        text.ShouldContain(" build=ok ");
        text.ShouldContain(" executed=1 passed=1 failed=0 skipped=0 ");
        factory.All.ShouldAllBe(handle => handle.Disposed, "every owned child handle is released");
    }

    [Test]
    public async Task C578_a_failed_build_keeps_its_output_and_exit_and_never_runs_the_tests()
    {
        var row = Start(out var factory, out var results);
        var build = await factory.NextAsync();
        build.Out("c578-failed-stdout");
        build.Err("c578-failed-stderr");
        build.Exit(37);

        var (exit, text) = await row.WaitAsync(Patience);
        exit.ShouldBe(ExitCodes.Invalid, text);
        var log = File.ReadAllText(Path.Combine(results, "build.log"));
        log.ShouldContain("c578-failed-stdout");
        log.ShouldContain("c578-failed-stderr");
        var lines = text.Replace("\r\n", "\n").Split('\n');
        lines.Count(line => line.Contains(" exit=37 ", StringComparison.Ordinal)).ShouldBe(1, "the build's actual exit, once: " + text);
        lines.Single(line => line.Contains(" exit=37 ", StringComparison.Ordinal)).ShouldContain(" build=failed ");
        lines.Single(line => line.StartsWith("CHECKPOINT CP-1 commit=", StringComparison.Ordinal)).ShouldContain(" build=failed ");
        text.ShouldEndWith("CHECKPOINT CP-1 EXIT CODE: 2" + Environment.NewLine);
        factory.Count.ShouldBe(1, "a failed build never invokes the tests");
        File.Exists(Path.Combine(results, "run.trx")).ShouldBeFalse();
        build.Disposed.ShouldBeTrue();
    }

    [Test]
    public async Task C578_a_build_cut_off_by_the_row_deadline_is_killed_and_reports_no_success()
    {
        var row = Start(out var factory, out var results, "--row-timeout", "2s");
        var build = await factory.NextAsync();
        build.Out("c578-partial-stdout");
        ReadShared(Path.Combine(results, "build.log")).ShouldContain("c578-partial-stdout");

        var (exit, text) = await row.WaitAsync(Patience);
        build.Killed.ShouldBeTrue("the live build is killed at the deadline");
        exit.ShouldBe(ExitCodes.Timeout, text);
        text.ShouldNotContain(" build=ok ");
        text.ShouldNotContain("GREEN");
        File.ReadAllText(Path.Combine(results, "build.log")).ShouldContain("c578-partial-stdout", customMessage: "the partial log survives");
        factory.Count.ShouldBe(1, "an interrupted build invokes no tests");
        File.Exists(Path.Combine(results, "run.trx")).ShouldBeFalse();
        build.Disposed.ShouldBeTrue();
    }

    private Task<(int Exit, string Text)> Start(out ScriptedFactory factory, out string results, params string[] extra)
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "tests", "Antiphon.Tests"));
        results = Path.Combine(repo, "results", "CP-1-c578");
        var scripted = factory = new ScriptedFactory();
        var output = new StringWriter();
        var synchronized = TextWriter.Synchronized(output);
        string[] args =
        [
            "row", "--repo-root", repo, "--name", "CP-1", "--project", "tests/Antiphon.Tests", "--output-path", "bin-c578/",
            "--filter", "/*/*/Sample/*", "--no-slot", "--results-root", "results", .. extra,
        ];
        return Task.Run(async () =>
        {
            var exit = await Antiphon.Checkpoints.Program.RunAsync(args, new CheckpointApp.Runtime
            {
                Output = synchronized, Driver = new ProcessDriver(scripted) { DotnetShim = "fake-dotnet" }, Platform = new FakePlatform(),
                SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null, RowStamp = () => "c578",
            });
            return (exit, output.ToString());
        });
    }

    // The log is open for append by the driver while the child lives; read it as another process would.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class ScriptedFactory : IProcessHandleFactory
    {
        private readonly object _gate = new();
        private readonly List<ScriptedHandle> _handles = [];
        private readonly List<TaskCompletionSource<ScriptedHandle>> _waiters = [];
        private int _taken;

        public int Count { get { lock (_gate) return _handles.Count; } }
        public IReadOnlyList<ScriptedHandle> All { get { lock (_gate) return _handles.ToList(); } }

        public IProcessHandle Create(ProcessStartInfo startInfo)
        {
            var handle = new ScriptedHandle(startInfo.ArgumentList.FirstOrDefault() ?? "");
            lock (_gate)
            {
                _handles.Add(handle);
                foreach (var waiter in _waiters) waiter.TrySetResult(handle);
                _waiters.Clear();
            }
            return handle;
        }

        // The next child the driver started, once it reads its output (so a line written now reaches the log).
        public async Task<ScriptedHandle> NextAsync()
        {
            ScriptedHandle? next = null;
            Task<ScriptedHandle>? pending = null;
            lock (_gate)
            {
                if (_taken < _handles.Count) next = _handles[_taken];
                else
                {
                    var waiter = new TaskCompletionSource<ScriptedHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters.Add(waiter);
                    pending = waiter.Task;
                }
                _taken++;
            }
            next ??= await pending!.WaitAsync(Patience);
            await next.Reading.Task.WaitAsync(Patience);
            return next;
        }
    }

    private sealed class ScriptedHandle(string phase) : IProcessHandle
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<string?>? _stdout, _stderr;

        public string Phase { get; } = phase;
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }

        public void Out(string line) => _stdout!(line);
        public void Err(string line) => _stderr!(line);
        public void Exit(int code) => _exit.TrySetResult(code);

        public bool Start() => true;
        public void BeginRead(Action<string?> stdout, Action<string?> stderr)
        {
            _stdout = stdout;
            _stderr = stderr;
            Reading.TrySetResult();
        }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);
        public bool HasExited => _exit.Task.IsCompleted;
        public int ExitCode => _exit.Task.Result;
        public void Kill(bool entireProcessTree)
        {
            Killed = true;
            _exit.TrySetResult(137);
        }
        public void Dispose() => Disposed = true;
    }
}
