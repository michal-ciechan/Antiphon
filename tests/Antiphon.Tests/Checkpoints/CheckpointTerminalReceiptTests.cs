using System.Diagnostics;
using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[NativeLinuxOnly("native signal and shell process witnesses require Linux")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointTerminalReceiptTests : CheckpointTestBase
{
    // B-1: a real waiter must not return during the gap between executor death
    // and the stopper's terminal publication. Deliberately hold that gap open.
    [Test]
    [Timeout(180_000)]
    public async Task C1734_Stop_publishes_before_the_executor_waiter_returns(CancellationToken cancellationToken)
    {
        var (root, run, git) = Fixture("sleep 60");
        using var executor = Start(git, root, "execute", "--run", run);
        await Ready(run, cancellationToken);
        using var waiter = Start(git, root, "wait", "--run", run, "--max-wait", "30s");
        (await waiter.StandardOutput.ReadLineAsync(cancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)).ShouldStartWith("HEARTBEAT");
        var control = new HeldStop();
        var stop = Task.Run(() => Antiphon.Checkpoints.Program.RunAsync(
            ["stop", "--repo-root", root, "--run", run],
            new CheckpointApp.Runtime { ProcessControl = control, Output = TextWriter.Null }), cancellationToken);
        RegisterCheckpointWork(stop);
        try
        {
            await control.Joined.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            waiter.HasExited.ShouldBeFalse("wait must await terminal publication after joined teardown");
            File.Exists(Path.Combine(run, "report.json")).ShouldBeFalse();
            control.Release.TrySetResult();
            (await stop.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)).ShouldBe(0);
            await waiter.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            waiter.ExitCode.ShouldBe(130);
            AssertReceipt(run, 130, "supported-stop");
        }
        finally
        {
            control.Release.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(15));
            await Join(executor);
            await Join(waiter);
        }
    }

    [Test]
    [Arguments("cancel", 130)]
    [Arguments("failure", 6)]
    [Arguments("timeout", 5)]
    [Arguments("build-failure", 2)]
    [Arguments("build-timeout", 5)]
    [Timeout(180_000)]
    public async Task C1734_Executor_terminal_receipt_reaches_a_real_waiter(string mode, int exit, CancellationToken cancellationToken)
    {
        if (mode == "cancel" && !OperatingSystem.IsLinux()) Skip.Test("native SIGINT witness requires Linux");
        var (root, run, git) = Fixture(mode == "failure" ? "true" : "sleep 120",
            timeout: mode is "timeout" or "build-timeout", build: mode.StartsWith("build-", StringComparison.Ordinal) ? mode : null);
        if (mode == "failure") Directory.CreateDirectory(Path.Combine(run, "executor.log"));
        using var executor = Start(git, root, "execute", "--run", run);
        if (mode is not ("failure" or "build-failure")) await Ready(run, cancellationToken, build: mode.StartsWith("build-", StringComparison.Ordinal));
        else
        {
            // Execute acknowledges ownership before attempting to open its log.
            await executor.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        using var waiter = Start(git, root, "wait", "--run", run, "--max-wait", "90s");
        try
        {
            if (mode == "cancel")
            {
                var signal = git.Command("kill", root);
                signal.ArgumentList.Add("-INT");
                signal.ArgumentList.Add(executor.Id.ToString());
                (await CheckpointFixtureProcess.RunAsync(signal, cancellationToken)).Exit.ShouldBe(0);
            }
            await waiter.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
            waiter.ExitCode.ShouldBe(exit);
            File.Exists(Path.Combine(run, "report.md")).ShouldBeTrue("the receipt must exist when wait returns");
            AssertReceipt(run, exit, mode == "cancel" ? "caller-requested" : null);
            await executor.WaitForExitAsync(cancellationToken);
            executor.ExitCode.ShouldBe(exit);
        }
        finally { await Join(executor); await Join(waiter); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task C1734_Stop_replaces_only_its_bound_provisional_reports(bool foreign, CancellationToken cancellationToken)
    {
        var (root, run, git) = Fixture("true");
        using var executor = Start(git, root, "execute", "--run", run);
        await executor.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        executor.ExitCode.ShouldBe(0);
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        state.Phase = "finishing"; state.ExitCode = null; state.EndedAt = null;
        new RunStateStore().Write(Path.Combine(run, "state.json"), state).ShouldBeNull();
        if (foreign) File.WriteAllText(Path.Combine(run, "report.md"), "foreign receipt sentinel");
        var before = File.ReadAllBytes(Path.Combine(run, "report.json"));
        (await Antiphon.Checkpoints.Program.RunAsync(["stop", "--repo-root", root, "--run", run],
            new CheckpointApp.Runtime { Output = TextWriter.Null })).ShouldBe(foreign ? 2 : 0);
        if (foreign)
        {
            File.ReadAllText(Path.Combine(run, "report.md")).ShouldBe("foreign receipt sentinel");
            File.ReadAllBytes(Path.Combine(run, "report.json")).ShouldBe(before);
            new RunStateStore().TryRead(Path.Combine(run, "state.json"))!.Phase.ShouldBe("finishing");
        }
        else AssertReceipt(run, 130, "supported-stop");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task C1734_Partial_counts_survive_failure_and_cancel(bool cancel, CancellationToken cancellationToken)
    {
        var (root, _, _) = Fixture("true");
        Directory.CreateDirectory(Path.Combine(root, "sample"));
        File.WriteAllText(Path.Combine(root, "sample/sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var manifest = new CheckpointManifest
        {
            Builds = [new BuildSpec { Id = "bin-partial", Project = "sample", OutputPath = "bin-partial/" }],
            Checkpoints = [CheckpointFixtures.Row("CP-known", "bin-partial"), CheckpointFixtures.Row("CP-pending", "bin-partial")],
        };
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var path = CheckpointFixtures.TrxFile(request);
            if (path.Contains("CP-pending", StringComparison.Ordinal))
            {
                if (cancel) { caller.Cancel(); caller.Token.ThrowIfCancellationRequested(); }
                throw new IOException("partial-result executor failure");
            }
            CheckpointFixtures.WriteResults(path, ("Antiphon.Tests.FixtureTests.one", "Passed"),
                ("Antiphon.Tests.FixtureTests.two", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var runtime = new CheckpointApp.Runtime
        {
            SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null,
            Driver = driver, Slots = new FixedSlotClient("off"), Output = TextWriter.Null,
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest { Slots = "off", Serial = true, KeepOutputs = true }, root, runtime);
        (await CheckpointApp.ExecuteAsync(run, caller.Token, runtime)).ShouldBe(cancel ? 130 : 6);
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json"))!;
        ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull();
        var report = ReceiptJson.Read<ReportModel>(Path.Combine(run, "report.json"), ReportWriter.Json);
        var known = report.Rows.Single(row => row.Id == "CP-known");
        known.Executed.ShouldBe(2); known.Passed.ShouldBe(2); known.Failed.ShouldBe(0); known.Skipped.ShouldBe(0);
        report.Rows.Single(row => row.Id == "CP-pending").Executed.ShouldBeNull();
        report.Builds.Single().Source.BuildSource.ShouldBe("verified");
        driver.InFlight.ShouldBe(0);
    }

    private (string Root, string Run, CheckpointFixtureGitEnvironment Git) Fixture(string command, bool timeout = false, string? build = null)
    {
        var root = Path.Combine(TempDir(), "repo");
        Directory.CreateDirectory(root);
        var git = CheckpointFixtureGitEnvironment.Create(TempDir());
        git.Git(root, "init", "-b", "terminal-receipt");
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".antiphon/\n");
        if (build is not null)
            File.WriteAllText(Path.Combine(root, "probe.csproj"), "<Project><Target Name=\"Restore\" /><Target Name=\"Build\">"
                + (build == "build-failure" ? "<Error Text=\"C1734 expected build failure\" />" : "<Exec Command=\"sleep 120\" />")
                + "</Target></Project>");
        git.Git(root, "add", ".");
        git.Git(root, "-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "--no-verify", "-m", "fixture");
        var run = CheckpointApp.CreateRun(new CheckpointManifest
        {
            Builds = build is null ? [] : [new BuildSpec { Id = "bin-probe", Project = "probe.csproj", OutputPath = "bin-probe/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["all"], Command = build is null ? command : null,
                Build = build is null ? null : "bin-probe", Filter = build is null ? null : "/*/*/Probe/*", EstimatedMinutes = 1 }],
        }, new RunRequest { Slots = "off", KeepOutputs = true, RowTimeoutMinutes = timeout ? 1 : null,
            ExpectedSourceSha = git.Git(root, "rev-parse", "HEAD").Trim() }, root);
        return (root, run, git);
    }

    private Process Start(CheckpointFixtureGitEnvironment git, string root, params string[] args)
    {
        // Detached shells can leave SIGINT ignored. Reset its disposition before
        // exec so this witness reaches the production Console.CancelKeyPress handler.
        var start = git.Command("env", root);
        start.ArgumentList.Add("--default-signal=INT");
        start.ArgumentList.Add("dotnet");
        start.RedirectStandardOutput = args[0] == "wait";
        start.ArgumentList.Add(typeof(Antiphon.Checkpoints.Program).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--repo-root"); start.ArgumentList.Add(root);
        foreach (var name in new[] { "ANTIPHON_TASK_ID", "ANTIPHON_SESSION_ID", "ANTIPHON_TASK_TOKEN" }) start.Environment.Remove(name);
        var process = Process.Start(start)!;
        RegisterCheckpointChild(process);
        if (args[0] == "execute")
        {
            var record = RunOwnershipStore.Read(args[2])!;
            record.Phase = "launched";
            record.Launched = new ProcessIdentityProbe().Capture(process.Id);
            RunOwnershipStore.Write(args[2], record);
        }
        return process;
    }

    private static async Task Ready(string run, CancellationToken cancellationToken, bool build = false)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        while (build
            ? new RunStateStore().TryRead(Path.Combine(run, "state.json"))?.Builds.Any(row => row.State == "building") != true
            : new RunStateStore().TryRead(Path.Combine(run, "state.json"))?.Rows.Any(row => row.State == "running") != true)
            await Task.Delay(50, limit.Token);
    }

    private static void AssertReceipt(string run, int exit, string? reason)
    {
        var state = new RunStateStore().TryRead(Path.Combine(run, "state.json")).ShouldNotBeNull();
        state.Phase.ShouldBe("done"); state.ExitCode.ShouldBe(exit); state.EndedAt.ShouldNotBeNull();
        ReportWriter.RequiredReceiptRefusal(run, state).ShouldBeNull();
        var report = ReceiptJson.Read<ReportModel>(Path.Combine(run, "report.json"), ReportWriter.Json);
        report.ExitCode.ShouldBe(exit); report.Verdict.ShouldBe("RED");
        report.StartedAt.ShouldBe(state.StartedAt); report.EndedAt.ShouldBe(state.EndedAt.Value);
        report.EndedAt.ShouldBeGreaterThanOrEqualTo(report.StartedAt);
        report.Branch.ShouldNotBeNullOrWhiteSpace(); report.Worktree.ShouldNotBeNullOrWhiteSpace();
        report.ManifestHash.ShouldNotBeNullOrWhiteSpace(); report.Host.Os.ShouldNotBeNullOrWhiteSpace();
        report.Rows.Single().Id.ShouldBe("CP-1");
        report.Rows.Single().Executed.ShouldBeNull("a command has no test counts");
        if (reason is not null) report.Reason.ShouldBe(reason);
        ReportValidator.Validate(report, report.Commit).ShouldBe("report_failed");
        // Forging the top-level result must not turn unknown/unrun rows into green.
        report.ExitCode = 0; report.Verdict = "GREEN";
        ReportValidator.Validate(report, report.Commit).ShouldNotBeNull();
    }

    private static async Task Join(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class HeldStop : IProcessControl
    {
        public TaskCompletionSource Joined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StopAndWait(ProcessIdentity identity, TimeSpan timeout)
        {
            var stopped = new ProcessControl().StopAndWait(identity, timeout);
            stopped.ShouldBeTrue();
            Joined.TrySetResult();
            Release.Task.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            return stopped;
        }
    }
}
