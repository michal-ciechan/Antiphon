using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointToolCopyCleanupTests : CheckpointTestBase
{
    [Test]
    public void nested_live_executor_vetoes_whole_root()
    {
        var (sandbox, root, sweep) = Candidate();
        var run = Path.Combine(root, "custom", "run");
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        CheckpointFixtures.MarkRun(run, alive: true);
        File.WriteAllText(Path.Combine(root, "sentinel"), "keep");
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        File.ReadAllText(Path.Combine(root, "sentinel")).ShouldBe("keep");
    }

    [Test]
    public void unknown_nested_custody_vetoes_whole_root()
    {
        var (_, root, sweep) = Candidate();
        var tool = Path.Combine(root, "custom-results", "run", "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "sentinel"), "keep");
        sweep.SweepOnce().CompletedRoots.ShouldBe(0);
        File.ReadAllText(Path.Combine(tool, "sentinel")).ShouldBe("keep");
    }

    [Test]
    public async Task wait_uses_custody_not_progress_pid()
    {
        var run = Run();
        var journal = RunOwnershipStore.Read(run)!;
        journal.Phase = "launch-attempted";
        journal.Launched = null;
        RunOwnershipStore.Write(run, journal);
        var report = Path.Combine(run, "report.md");
        File.WriteAllText(report, "report-sentinel");
        new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
        { RunId = Path.GetFileName(run), Phase = "done", ExecutorPid = int.MaxValue, ExitCode = 0 });
        var output = new StringWriter();
        try
        {
            (await new WaitCommand().WaitAsync(run, TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(1),
                output, CancellationToken.None)).ShouldBe(ExitCodes.StillRunning);
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
            File.ReadAllText(report).ShouldBe("report-sentinel");
        }
        finally { ExecutorOwnershipStore.Write(run, Dead()); }
    }

    [Test]
    public void next_start_uses_the_shared_veto()
    {
        var repo = TempDir();
        var results = Path.Combine(repo, ".antiphon", "checkpoints");
        var dead = Path.Combine(results, "dead-run");
        var unknown = Path.Combine(results, "unknown-run");
        Directory.CreateDirectory(Path.Combine(dead, "tool"));
        Directory.CreateDirectory(Path.Combine(unknown, "tool"));
        CheckpointFixtures.MarkRun(dead, alive: false);
        CheckpointFixtures.MarkRun(unknown, alive: false);
        var journal = RunOwnershipStore.Read(unknown)!;
        journal.Phase = "launch-attempted";
        journal.Launched = null;
        RunOwnershipStore.Write(unknown, journal);
        var other = Path.Combine(repo, "other-results", "run", "tool");
        Directory.CreateDirectory(other);
        try
        {
            CheckpointApp.CreateRun(Manifest(), new RunRequest(), repo);
            Directory.Exists(Path.Combine(dead, "tool")).ShouldBeFalse();
            Directory.Exists(Path.Combine(unknown, "tool")).ShouldBeTrue();
            Directory.Exists(other).ShouldBeTrue();
        }
        finally { ExecutorOwnershipStore.Write(unknown, Dead()); }
    }

    [Test]
    public async Task stop_validates_generation_before_signaling()
    {
        var repo = TempDir();
        var run = CheckpointApp.CreateRun(Manifest(), new RunRequest(), repo);
        var journal = RunOwnershipStore.Read(run)!;
        journal.Phase = "launched";
        journal.Launched = new ProcessIdentityProbe().Current() with { StartUtcTicks = 1 };
        RunOwnershipStore.Write(run, journal);
        var control = new CountingControl();
        try
        {
            var exit = await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", run, "--repo-root", repo],
                new CheckpointApp.Runtime { ProcessControl = control });
            exit.ShouldBe(ExitCodes.StillRunning);
            control.Calls.ShouldBe(0);
            new ProcessIdentityProbe().Observe(new ProcessIdentityProbe().Current()).Verdict.ShouldBe(ProcessVerdict.AliveSame);
        }
        finally { CheckpointFixtures.MarkRun(run, alive: false); }
    }

    [Test]
    public async Task stop_waits_for_confirmed_exit()
    {
        var repo = TempDir();
        var run = RunUnder(repo);
        var control = new CountingControl { Result = false };
        var probe = new ScriptedProbe(ProcessVerdict.AliveSame);
        var state = new RunState { RunId = Path.GetFileName(run), Phase = "running", ExecutorPid = Environment.ProcessId };
        new RunStateStore().Write(Path.Combine(run, "state.json"), state);
        var exit = await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", run, "--repo-root", repo],
            new CheckpointApp.Runtime { ProcessProbe = probe, ProcessControl = control });
        exit.ShouldBe(ExitCodes.StillRunning);
        control.Calls.ShouldBe(1);
        new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldBe("running");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
    }

    [Test]
    public void age_clean_cannot_bypass_live_custody()
    {
        var repo = TempDir();
        var results = Path.Combine(repo, "results");
        Directory.CreateDirectory(results);
        var dead = Path.Combine(results, "dead");
        var live = Path.Combine(results, "live");
        var reused = Path.Combine(results, "reused");
        var unknown = Path.Combine(results, "unknown");
        foreach (var path in new[] { dead, live, reused, unknown })
        {
            Directory.CreateDirectory(path);
            CheckpointFixtures.MarkRun(path, alive: path == live);
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
        }
        var changed = RunOwnershipStore.Read(reused)!;
        changed.Launched = new ProcessIdentityProbe().Current() with { StartUtcTicks = 1 };
        RunOwnershipStore.Write(reused, changed);
        changed = RunOwnershipStore.Read(unknown)!;
        changed.Launched = changed.Launched! with { Host = "foreign-host" };
        RunOwnershipStore.Write(unknown, changed);
        Directory.SetLastWriteTimeUtc(dead, DateTime.UtcNow.AddDays(-8));
        Directory.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddDays(-8));
        Directory.SetLastWriteTimeUtc(reused, DateTime.UtcNow.AddDays(-8));
        Directory.SetLastWriteTimeUtc(unknown, DateTime.UtcNow.AddDays(-8));
        try
        {
            var removed = OutputCleanup.RemoveOlderRuns(results, TimeSpan.FromDays(7), dryRun: false);
            removed.ShouldBe([dead]);
            Directory.Exists(live).ShouldBeTrue();
            Directory.Exists(reused).ShouldBeTrue();
            Directory.Exists(unknown).ShouldBeTrue();
        }
        finally
        {
            CheckpointFixtures.MarkRun(reused, alive: false);
            CheckpointFixtures.MarkRun(unknown, alive: false);
        }
    }

    [Test]
    public void evidence_write_has_no_raw_delete_bypass()
    {
        var run = TempDir();
        var tool = Path.Combine(run, "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "sentinel"), "keep");
        EvidenceFolder.Write(run, new ReportModel { RunId = Path.GetFileName(run) }, removeToolCopy: true);
        Directory.Exists(tool).ShouldBeTrue();
    }

    [Test]
    public void automatic_recovery_preserves_all_evidence()
    {
        var run = Run();
        foreach (var name in new[] { "request.json", "manifest.resolved.yaml", "report.md", "run.trx", "executor.log" })
            File.WriteAllText(Path.Combine(run, name), "evidence-" + name);
        var before = Directory.EnumerateFiles(run).Where(path => Path.GetFileName(path) is not RunOwnershipStore.FileName)
            .ToDictionary(path => path, path => Hash(path));
        new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        foreach (var (path, hash) in before) File.Exists(path).ShouldBeTrue();
        foreach (var (path, hash) in before) Hash(path).ShouldBe(hash);
    }

    [Test]
    public void sweep_never_signals_a_process()
    {
        var repo = TempDir();
        var results = Path.Combine(repo, "results");
        var run = Path.Combine(results, "live");
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        CheckpointFixtures.MarkRun(run, alive: true);
        new ToolCopyCleanup().Sweep(results).ShouldBe(0);
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
        new ProcessIdentityProbe().Observe(new ProcessIdentityProbe().Current()).Verdict.ShouldBe(ProcessVerdict.AliveSame);
    }

    [Test]
    public void failed_delete_receipt_is_truthful_and_retryable()
    {
        var run = Run();
        var failed = new ToolCopyCleanup(beforeDelete: _ => throw new IOException("delete-denied")).Remove(run);
        failed.Outcome.ShouldBe("Failed", "failed-delete-truthful: a denied delete must not report removal");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
        new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    public async Task completed_wait_removes_only_tool(int exitCode, bool keepOutputs)
    {
        var run = Run();
        var report = Path.Combine(run, "report.md");
        File.WriteAllText(report, "exit=" + exitCode);
        File.WriteAllText(Path.Combine(run, "request.json"), JsonSerializer.Serialize(new RunRequest { KeepOutputs = keepOutputs }));
        var hash = Hash(report);
        new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
        { RunId = Path.GetFileName(run), Phase = "done", ExitCode = exitCode, ExecutorPid = int.MaxValue });
        var output = new StringWriter();
        (await new WaitCommand().WaitAsync(run, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
            output, CancellationToken.None)).ShouldBe(exitCode);
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        Hash(report).ShouldBe(hash);
        output.ToString().ShouldContain("exit=" + exitCode);
    }

    [Test]
    public void already_absent_is_idempotent()
    {
        var run = Run();
        new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
        new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("AlreadyAbsent");
    }

    private string Run()
    {
        var run = TempDir();
        CheckpointFixtures.MarkRun(run, alive: false);
        var tool = Path.Combine(run, "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "sentinel.dll"), "keep");
        return run;
    }

    private string RunUnder(string repo)
    {
        var run = Path.Combine(repo, ".antiphon", "checkpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        CheckpointFixtures.MarkRun(run, alive: true);
        return run;
    }

    private (string Sandbox, string Root, CheckpointTempRootSweep Sweep) Candidate()
    {
        var sandbox = TempDir();
        var root = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var marker = new CheckpointRootMarker
        {
            RootId = Path.GetFileName(root)[5..], RootPath = root,
            AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "test",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), Owner = Dead(),
        };
        TestRootGuard.Write(root, marker);
        var sweep = new CheckpointTempRootSweep(sandbox,
            options: new CheckpointSweepOptions { Grace = TimeSpan.Zero, Interval = TimeSpan.Zero });
        sweep.Register(root);
        return (sandbox, root, sweep);
    }

    private static CheckpointManifest Manifest() => new()
    {
        Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 }],
    };

    private static ProcessIdentity Dead() => new ProcessIdentityProbe().Current() with
    { Pid = int.MaxValue, StartUtcTicks = 1 };

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class ScriptedProbe(ProcessVerdict verdict) : ProcessIdentityProbe
    {
        public override ProcessObservation Observe(ProcessIdentity? expected) => new(verdict, "scripted");
    }

    private sealed class CountingControl : IProcessControl
    {
        public int Calls { get; private set; }
        public bool Result { get; init; }
        public bool StopAndWait(ProcessIdentity identity, TimeSpan timeout) { Calls++; return Result; }
    }
}
