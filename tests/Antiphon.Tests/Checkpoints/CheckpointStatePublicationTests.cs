using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointStatePublicationTests : CheckpointTestBase
{
    [Test]
    public async Task concurrent_state_publications_preserve_each_verified_generation()
    {
        var path = Path.Combine(TempDir(), "state.json");
        using var firstPublished = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondPublished = new ManualResetEventSlim();
        var io = new InterleavedPublicationIo(path, firstPublished, releaseFirst, secondPublished);
        var store = new RunStateStore(new CheckpointCleanupContext(io, new CheckpointNativeGitRunner()));
        store.Write(path, new RunState { RunId = "concurrent", Marker = "initial" }).ShouldBeNull();
        io.Armed = true;

        Exception? Write(string generation)
        {
            try
            {
                store.Write(path, new RunState { RunId = "concurrent", Marker = generation }).ShouldBeNull();
                return null;
            }
            catch (Exception error) { return error; }
        }

        // Dedicated producers keep this filesystem interleaving independent of pool load.
        var first = Task.Factory.StartNew(() => Write("first"), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<Exception?>? second = null;
        try
        {
            firstPublished.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("first native replacement completed");
            second = Task.Factory.StartNew(() => Write("second"), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // Without serialization, the second replacement completes before the first
            // verifies its bytes. With serialization it waits until we release the first.
            secondPublished.Wait(TimeSpan.FromSeconds(2));
        }
        finally
        {
            releaseFirst.Set();
            await first;
            if (second is not null) await second;
        }

        (await first).ShouldBeNull("a sibling publication must not invalidate the first generation");
        (await second!).ShouldBeNull();
        store.TryRead(path)!.Marker.ShouldBe("second");
        File.ReadAllLines(Path.Combine(Path.GetDirectoryName(path)!, "state.history.jsonl"))
            .Select(line => JsonSerializer.Deserialize<RunState>(line, RunStateStore.Json)!.Marker)
            .ShouldBe(new[] { "initial", "first", "second" });
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").ShouldBeEmpty();
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak").ShouldBeEmpty();
    }

    private sealed class InterleavedPublicationIo(string path, ManualResetEventSlim firstPublished,
        ManualResetEventSlim releaseFirst, ManualResetEventSlim secondPublished) : ForwardingNativeIo
    {
        private int _moves;
        public bool Armed { get; set; }

        public override void Move(string source, string destination, bool overwrite)
        {
            if (!Armed || destination != path) { base.Move(source, destination, overwrite); return; }
            var generation = JsonSerializer.Deserialize<RunState>(File.ReadAllText(source), RunStateStore.Json)!.Marker;
            var number = Interlocked.Increment(ref _moves);
            base.Move(source, destination, overwrite);
            Console.WriteLine($"PUBLICATION_NATIVE pid={Environment.ProcessId} thread={Environment.CurrentManagedThreadId} "
                + $"move={number} generation={generation} result=success path={destination}");
            if (number == 1)
            {
                firstPublished.Set();
                releaseFirst.Wait();
                using var stream = new FileStream(destination, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                Console.WriteLine("PUBLICATION_NATIVE first-verification-observes="
                    + JsonSerializer.Deserialize<RunState>(stream, RunStateStore.Json)!.Marker);
            }
            else secondPublished.Set();
        }
    }

    [Test]
    public async Task state_publication_survives_contention_and_preserves_completed_rows()
    {
        foreach (var failure in new Exception[]
        {
            new IOException("access", unchecked((int)0x80070005)),
            new IOException("sharing", unchecked((int)0x80070020)),
            new IOException("lock", unchecked((int)0x80070021)),
            new UnauthorizedAccessException("access"),
            new IOException("receipt context", new IOException("sharing", unchecked((int)0x80070020))),
        })
            StoreScenario(failure, failures: 1, expectedAttempts: 2, succeeds: true);
        StoreScenario(new IOException("sharing", unchecked((int)0x80070020)), 6, 6, false);
        StoreScenario(new IOException("disk full", unchecked((int)0x80070070)), 6, 1, false);
        StoreScenario(new IOException("foreign facility", unchecked((int)0x80130020)), 6, 1, false);
        StoreScenario(new UnauthorizedAccessException("read only"), 6, 0, false, readOnly: true);
        StoreScenario(new IOException("sharing", unchecked((int)0x80070020)), 6, 0, false, foreign: true);
        if (OperatingSystem.IsWindows()) NativeContention();
        await ExecuteScenario(final: false, transient: false);
        await ExecuteScenario(final: true, transient: false);
        await ExecuteScenario(final: false, transient: true);
        await ExecuteScenario(final: false, transient: false, draining: true);
        await ExecuteScenario(final: true, transient: false, corrective: true);
    }

    private void StoreScenario(Exception failure, int failures, int expectedAttempts, bool succeeds,
        bool readOnly = false, bool foreign = false)
    {
        var directory = TempDir();
        var path = Path.Combine(directory, "state.json");
        var io = new FaultIo();
        var clock = new ImmediateClock();
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner(), clock);
        var store = new RunStateStore(context);
        store.Write(path, new RunState { RunId = "fixture", Marker = "old" }).ShouldBeNull();
        var old = File.ReadAllText(path);
        var history = File.ReadAllLines(Path.Combine(directory, "state.history.jsonl")).Length;
        if (foreign)
        {
            // A fresh context cannot replace an unowned document.
            File.WriteAllText(path, "{\"foreign\":true}");
            old = File.ReadAllText(path);
            store = new RunStateStore(new CheckpointCleanupContext(io, new CheckpointNativeGitRunner(), clock));
        }
        io.ReadOnlyPath = readOnly ? path : null;
        var attempts = 0;
        io.BeforeMove = (source, destination) =>
        {
            if (destination != path) return;
            File.ReadAllText(path).ShouldBe(old, "atomic old state during denial");
            if (++attempts <= failures) throw failure;
        };
        Exception? caught = null;
        string? refusal = null;
        try { refusal = store.Write(path, new RunState { RunId = "fixture", Marker = "new" }); }
        catch (Exception ex) { caught = ex; }
        if (succeeds)
        {
            caught.ShouldBeNull("transient state publication must recover");
            refusal.ShouldBeNull();
            JsonSerializer.Deserialize<RunState>(File.ReadAllText(path), RunStateStore.Json)!.Marker.ShouldBe("new");
        }
        else
        {
            (caught is not null || refusal is not null).ShouldBeTrue("permanent failure must remain refused");
            File.ReadAllText(path).ShouldBe(old);
        }
        attempts.ShouldBe(expectedAttempts);
        clock.Delays.Select(t => (int)t.TotalMilliseconds).ShouldBe(
            expectedAttempts == 6 ? new[] { 50, 100, 200, 400, 800 } : expectedAttempts == 2 ? new[] { 50 } : []);
        File.ReadAllLines(Path.Combine(directory, "state.history.jsonl")).Length.ShouldBe(history + (succeeds ? 1 : 0));
        Directory.GetFiles(directory, "*.tmp").ShouldBeEmpty();
    }

    private void NativeContention()
    {
        var path = Path.Combine(TempDir(), "state.json");
        var io = new FaultIo();
        var store = new RunStateStore(new CheckpointCleanupContext(io, new CheckpointNativeGitRunner()));
        store.Write(path, new RunState { RunId = "native", Marker = "old" }).ShouldBeNull();
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var denied = 0;
        io.MoveFailed = () => { denied++; held.Dispose(); };
        Exception? caught = null;
        try { store.Write(path, new RunState { RunId = "native", Marker = "new" }).ShouldBeNull(); }
        catch (Exception ex) { caught = ex; }
        caught.ShouldBeNull("native contention must recover after the failed move releases the handle");
        denied.ShouldBe(1);
        store.TryRead(path)!.Marker.ShouldBe("new");
        File.ReadAllLines(Path.Combine(Path.GetDirectoryName(path)!, "state.history.jsonl")).Length.ShouldBe(2);
        Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").ShouldBeEmpty();
    }

    private async Task ExecuteScenario(bool final, bool transient, bool draining = false, bool corrective = false)
    {
        var repo = TempDir();
        CheckpointFixtureProcess.Git(repo, "init", "-b", "card1370");
        Directory.CreateDirectory(Path.Combine(repo, "sample"));
        File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var source = CheckpointFixtures.CleanSource();
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-c1370-fixture", Project = "sample", OutputPath = "bin-c1370-fixture/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-first", After = ["all"], Build = "bin-c1370-fixture",
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 2, Expect = ["ExampleSurfaceTests"],
            }],
        };
        if (draining)
            manifest.Checkpoints.Add(new CheckpointSpec
            {
                Id = "CP-draining", After = ["all"], Build = "bin-c1370-fixture",
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 2, Expect = ["ExampleSurfaceTests"],
            });
        if (!final && !transient)
            manifest.Checkpoints.Add(new CheckpointSpec
            {
                Id = "CP-never", After = ["all"], Build = "bin-c1370-fixture", Serial = true,
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 2, Expect = ["ExampleSurfaceTests"],
            });
        var io = new FaultIo();
        var context = new CheckpointCleanupContext(io, new CheckpointNativeGitRunner(), new ImmediateClock());
        var driver = new FakeDriver();
        string? trx = null;
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = false;
        driver.When(CheckpointFixtures.IsRun, async (request, token) =>
        {
            var path = CheckpointFixtures.TrxFile(request);
            if (draining && path.Contains("CP-draining", StringComparison.Ordinal))
            {
                secondStarted.SetResult();
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { joined = true; }
                joined.ShouldBeTrue("publication failure must cancel and join the live sibling");
            }
            else
            {
                if (draining) await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                trx = path;
            }
            CheckpointFixtures.WriteResults(path, ("Antiphon.Tests.ExampleSurfaceTests.one", "Passed"),
                ("Antiphon.Tests.ExampleSurfaceTests.two", "Passed"));
            return new DriverResult(0, "", "");
        });
        var output = new StringWriter();
        var runtime = new CheckpointApp.Runtime
        {
            CleanupContext = context, Driver = driver, Slots = new FixedSlotClient("off"),
            Platform = new FakePlatform(), EnvironmentLookup = _ => null,
            SourceCapture = _ => source, Output = output,
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = source.Commit!, Branch = "fixture", KeepOutputs = !corrective, Slots = "off", Serial = !draining,
            Baseline = transient ? null : "must-not-launch",
        }, repo, runtime);
        var attempts = 0;
        string? completedLine = null;
        string? phaseAtCleanup = null;
        io.BeforeFileExists = path =>
        {
            if (!corrective || path != Path.Combine(repo, "sample", "bin-c1370-fixture",
                    ".checkpoint-output-ledger.json")) return;
            phaseAtCleanup = new RunStateStore(context).TryRead(Path.Combine(run, "state.json"))!.Phase;
            throw new IOException("injected output cleanup failure");
        };
        io.BeforeMove = (temporary, destination) =>
        {
            if (destination != Path.Combine(run, "state.json")) return;
            var state = JsonSerializer.Deserialize<RunState>(File.ReadAllText(temporary), RunStateStore.Json)!;
            var completed = state.Rows.FirstOrDefault(row => row.Id == "CP-first" && row.State == "green");
            if (completed is null || final && state.Phase != "done") return;
            completedLine ??= completed.Line;
            // Let the first successful Finish publish on the defective path, then deny
            // every corrective publication after cleanup changes the verdict to red.
            if (corrective && (state.Phase != "done" || state.ExitCode == 0)) return;
            if (++attempts <= (transient ? 1 : int.MaxValue))
                throw new IOException("injected sharing violation", unchecked((int)0x80070020));
        };
        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime);
        exit.ShouldBe(transient ? 0 : 2, "publication exit");
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), CheckpointApp.Json)!;
        Console.WriteLine($"publication final={final} transient={transient} draining={draining}: " + ReportWriter.JsonText(report));
        report.ExitCode.ShouldBe(exit);
        report.Verdict.ShouldBe(transient ? "GREEN" : "RED");
        if (!transient) report.Reason.ShouldContain("receipt-write-failed");
        report.Rows.Count.ShouldBe(draining ? 2 : 1);
        var row = report.Rows.Single(item => item.Id == "CP-first");
        row.Executed.ShouldBe(2); row.Passed.ShouldBe(2); row.Failed.ShouldBe(0); row.Skipped.ShouldBe(0);
        row.Trx.ShouldBe(trx); File.Exists(trx).ShouldBeTrue();
        row.Line.ShouldBe(completedLine);
        row.Source.BuildSource.ShouldBe("verified"); row.Source.Start.Commit.ShouldBe(source.Commit);
        report.Builds.Single().Source.BuildSource.ShouldBe("verified");
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(draining ? 2 : 1);
        driver.Calls.Count.ShouldBe(draining ? 3 : 2, "only the build and admitted rows may launch");
        driver.InFlight.ShouldBe(0);
        report.Unlisted.ShouldBeEmpty("no baseline launch on publication failure");
        if (!transient)
        {
            ReportValidator.Validate(report, source.Commit!).ShouldNotBeNull("failed state receipt cannot qualify clean");
            output.ToString().ShouldContain(row.Line);
            output.ToString().ShouldContain("receipt-write-failed");
            attempts.ShouldBe(6);
            Directory.Exists(Path.Combine(repo, "sample", "bin-c1370-fixture")).ShouldBeTrue("retain outputs on publication failure");
            var start = new System.Diagnostics.ProcessStartInfo("pwsh") { WorkingDirectory = CheckpointFixtures.RepoRoot };
            foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot,
                "scripts", "validate-checkpoint-receipt.ps1"), "-Evidence", Path.Combine(run, "report.json"),
                "-ExpectedSourceSha", source.Commit! }) start.ArgumentList.Add(argument);
            var validation = CheckpointFixtureProcess.Run(start, 30_000);
            validation.Exit.ShouldBe(2, validation.Stdout + validation.Stderr);
            validation.Stdout.ShouldContain(corrective ? "cleanup_refused" : "report_failed");
            var dead = new ProcessIdentityProbe(context).Current() with { Pid = int.MaxValue, StartUtcTicks = 1 };
            ExecutorOwnershipStore.Write(run, dead, context);
            var waited = new StringWriter();
            (await new WaitCommand(context: context).WaitAsync(run, TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(60), waited, CancellationToken.None, source.Commit)).ShouldBe(2);
            if (corrective)
            {
                var persisted = new RunStateStore(context).TryRead(Path.Combine(run, "state.json"));
                (persisted?.Phase == "done" && persisted.ExitCode == 0)
                    .ShouldBeFalse("a failed corrective publication must not leave a green terminal receipt");
                phaseAtCleanup.ShouldBe("finishing", "cleanup must settle before terminal publication");
            }
            waited.ToString().ShouldContain(row.Line);
            waited.ToString().ShouldContain("receipt-write-failed");
        }
        else new RunStateStore(context).TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldBe("done");
        if (draining)
        {
            joined.ShouldBeTrue();
            var drained = report.Rows.Single(item => item.Id == "CP-draining");
            drained.Executed.ShouldBe(2); drained.Passed.ShouldBe(2);
            drained.Failed.ShouldBe(0); drained.Skipped.ShouldBe(0);
            drained.Line.ShouldContain("executed=2 passed=2 failed=0 skipped=0");
            File.Exists(drained.Trx).ShouldBeTrue();
        }
    }

    private sealed class ImmediateClock : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime >= TimeSpan.FromSeconds(1))
                return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            Delays.Add(dueTime);
            return TimeProvider.System.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    private sealed class FaultIo : ICheckpointCleanupIo
    {
        private readonly ICheckpointCleanupIo _inner = new CheckpointNativeIo();
        public Action<string, string>? BeforeMove { get; set; }
        public Action? MoveFailed { get; set; }
        public Action<string>? BeforeFileExists { get; set; }
        public string? ReadOnlyPath { get; set; }
        public void Move(string source, string destination, bool overwrite)
        {
            BeforeMove?.Invoke(source, destination);
            try { _inner.Move(source, destination, overwrite); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MoveFailed?.Invoke(); throw; }
        }
        public FileAttributes Attributes(string path) => _inner.Attributes(path) | (path == ReadOnlyPath ? FileAttributes.ReadOnly : 0);
        public int CurrentProcessId => _inner.CurrentProcessId;
        public string MachineName => _inner.MachineName;
        public string BootId => _inner.BootId;
        public string PidNamespace => _inner.PidNamespace;
        public string ImageDirectory => _inner.ImageDirectory;
        public string CurrentDirectory => _inner.CurrentDirectory;
        public CheckpointPlatform Platform => _inner.Platform;
        public uint Owner => _inner.Owner;
        public CheckpointProcessSnapshot Process(int pid) => _inner.Process(pid);
        public DateTime CreationTimeUtc(string path) => _inner.CreationTimeUtc(path);
        public DateTime LastWriteTimeUtc(string path) => _inner.LastWriteTimeUtc(path);
        public string StreamPath(Stream stream) => _inner.StreamPath(stream);
        public bool FileExists(string path)
        {
            BeforeFileExists?.Invoke(path);
            return _inner.FileExists(path);
        }
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public string? LinkTarget(string path) => _inner.LinkTarget(path);
        public long Length(string path) => _inner.Length(path);
        public IEnumerable<string> Enumerate(string path) => _inner.Enumerate(path);
        public CheckpointStat Stat(string path, bool portable = false) => _inner.Stat(path, portable);
        public CheckpointStat CustodyStat(string path) => _inner.CustodyStat(path);
        public int CreateExclusiveDirectory(string path) => _inner.CreateExclusiveDirectory(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public Stream Open(string path, FileMode mode, FileAccess access, FileShare share) => _inner.Open(path, mode, access, share);
        public ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request) => _inner.OpenRead(path, request);
        public void WriteBytes(string path, ReadOnlySpan<byte> bytes) => _inner.WriteBytes(path, bytes);
        public void AppendText(string path, string text) => _inner.AppendText(path, text);
        public void Copy(string source, string destination, bool overwrite) => _inner.Copy(source, destination, overwrite);
        public void Unlink(string path) => _inner.Unlink(path);
        public void RemoveDirectory(string path, bool recursive) => _inner.RemoveDirectory(path, recursive);
        public void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory) =>
            _inner.DeleteAnchored(path, expectedEntry, directory);
        public IReadOnlyList<AnchoredChild> ListAnchored(string directory) => _inner.ListAnchored(directory);
        public void UnlinkAnchored(string directory, string name, bool directoryEntry, ulong device = 0, ulong inode = 0) =>
            _inner.UnlinkAnchored(directory, name, directoryEntry, device, inode);
        public void TruncateAnchored(string directory, string name, long length) =>
            _inner.TruncateAnchored(directory, name, length);
        public string? EnvironmentVariable(string name) => _inner.EnvironmentVariable(name);
    }
}
