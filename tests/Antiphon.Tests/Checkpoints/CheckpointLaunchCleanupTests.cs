using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointLaunchCleanupTests : CheckpointTestBase
{
    [Test]
    public async Task preparing_journal_precedes_all_run_io()
    {
        var repo = TempDir();
        var copies = 0;
        var launches = 0;
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            OwnershipWrite = (_, _) => throw new IOException("preparing-denied"),
            CopyToolOutput = (_, _) => copies++,
            Launch = _ => { launches++; return Environment.ProcessId; },
        };
        (await Should.ThrowAsync<IOException>(async () =>
            await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime)))
            .Message.ShouldContain("preparing-denied");
        copies.ShouldBe(0);
        launches.ShouldBe(0);
        Runs(repo).ShouldBeEmpty();
    }

    [Test]
    public async Task launch_intent_failure_prevents_native_launch()
    {
        var repo = TempDir();
        var launches = 0;
        var runtime = FakeRuntime(ownershipWrite: (path, record) =>
        {
            if (record.Phase == "launch-attempted") throw new IOException("intent-denied");
            RunOwnershipStore.Write(path, record);
        }, launch: _ => { launches++; return Environment.ProcessId; });
        (await Should.ThrowAsync<IOException>(async () =>
            await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime)))
            .Message.ShouldContain("intent-denied");
        launches.ShouldBe(0);
        Runs(repo).ShouldBeEmpty();
    }

    [Test]
    public async Task unknown_launch_outcome_is_never_rolled_back()
    {
        var repo = TempDir();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            LaunchWithOutcome = _ => new LaunchOutcome(LaunchKind.Unknown),
        };
        var result = await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime);
        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        Directory.Exists(Path.Combine(result.RunDirectory, "tool")).ShouldBeTrue();
        RunOwnershipStore.Read(result.RunDirectory)!.Phase.ShouldBe("launch-attempted");
        ExecutorOwnershipStore.Write(result.RunDirectory, Dead());
    }

    [Test]
    public async Task post_launch_bookkeeping_failure_keeps_image()
    {
        var repo = TempDir();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            LaunchWithOutcome = _ => new LaunchOutcome(LaunchKind.Started, Environment.ProcessId,
                new ProcessIdentityProbe().Current()),
            LatestWrite = (_, _) => throw new IOException("latest-denied"),
        };
        var result = await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime);
        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        Directory.Exists(Path.Combine(result.RunDirectory, "tool")).ShouldBeTrue();
        RunOwnershipStore.Read(result.RunDirectory)!.Launched.ShouldNotBeNull();
    }

    [Test]
    public async Task executor_ack_is_checked_before_work()
    {
        var repo = TempDir();
        var run = CheckpointApp.CreateRun(Manifest(), new RunRequest { Slots = "off" }, repo);
        var driver = new FakeDriver();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ExecutorAckWrite = (_, _) => throw new IOException("ack-denied"),
            Driver = driver,
            Slots = new FixedSlotClient("off"),
        };
        (await CheckpointApp.ExecuteAsync(run, CancellationToken.None, runtime)).ShouldBe(ExitCodes.ExecutorCrashed);
        driver.Calls.ShouldBeEmpty();
        File.Exists(Path.Combine(run, "executor-ownership.json")).ShouldBeFalse();
    }

    [Test]
    public async Task starter_cannot_overwrite_child_progress()
    {
        var repo = TempDir();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            LaunchWithOutcome = request =>
            {
                var run = request.Arguments[^1];
                new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
                { RunId = Path.GetFileName(run), Phase = "done", ExitCode = 1 });
                return new LaunchOutcome(LaunchKind.Started, Environment.ProcessId,
                    new ProcessIdentityProbe().Current());
            },
        };
        var result = await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime);
        result.ExitCode.ShouldBe(0);
        var state = new RunStateStore().TryRead(Path.Combine(result.RunDirectory, "state.json"))!;
        state.Phase.ShouldBe("done");
        state.ExitCode.ShouldBe(1);
    }

    [Test]
    public async Task custody_write_failure_is_not_success()
    {
        var repo = TempDir();
        var runtime = FakeRuntime(ownershipWrite: (path, record) =>
        {
            if (record.Phase == "launched") throw new IOException("launched-denied");
            RunOwnershipStore.Write(path, record);
        });
        var result = await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime);
        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        Directory.Exists(Path.Combine(result.RunDirectory, "tool")).ShouldBeTrue();
        RunOwnershipStore.Read(result.RunDirectory)!.Phase.ShouldBe("launch-attempted");
        ExecutorOwnershipStore.Write(result.RunDirectory, Dead());
    }

    [Test]
    public async Task partial_copy_failure_rolls_back_owned_run()
    {
        var repo = TempDir();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            CopyToolOutput = (_, destination) =>
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "partial.dll"), "partial-bytes");
                throw new IOException("copy-cut");
            },
        };
        (await Should.ThrowAsync<IOException>(async () =>
            await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime)))
            .Message.ShouldContain("copy-cut");
        Runs(repo).ShouldBeEmpty();
    }

    [Test]
    public async Task definite_native_refusal_rolls_back_owned_run()
    {
        var repo = TempDir();
        var runtime = new CheckpointApp.Runtime
        {
            EnvironmentLookup = _ => null,
            ToolDirectory = TinyToolDirectory(),
            LaunchWithOutcome = _ => new LaunchOutcome(LaunchKind.NotStarted),
        };
        var result = await CheckpointApp.StartAsync(Manifest(), new RunRequest(), repo, TextWriter.Null, runtime);
        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        Directory.Exists(result.RunDirectory).ShouldBeFalse();
        Runs(repo).ShouldBeEmpty();
    }

    [Test]
    public async Task dead_executor_ack_recovers_without_progress()
    {
        var run = UnfinishedRun();
        ExecutorOwnershipStore.Write(run, Dead());
        var output = new StringWriter();
        var exit = await new WaitCommand().WaitAsync(run, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
            output, CancellationToken.None);
        exit.ShouldBe(ExitCodes.ExecutorCrashed);
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        output.ToString().ShouldContain("executor died without phase=done");
    }

    [Test]
    public void late_executor_ack_resolves_launch_unknown()
    {
        var run = UnfinishedRun();
        new ToolCopyCleanup().Remove(run).Reason.ShouldBe("launch-outcome-unknown");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
        ExecutorOwnershipStore.Write(run, Dead());
        new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
    }

    private string UnfinishedRun()
    {
        var repo = TempDir();
        var run = CheckpointApp.CreateRun(Manifest(), new RunRequest(), repo);
        var journal = RunOwnershipStore.Read(run)!;
        journal.Phase = "launch-attempted";
        RunOwnershipStore.Write(run, journal);
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        File.WriteAllText(Path.Combine(run, "tool", "sentinel.dll"), "keep");
        return run;
    }

    private CheckpointApp.Runtime FakeRuntime(Action<string, RunOwnership>? ownershipWrite = null,
        Func<LaunchRequest, int>? launch = null) => new()
    {
        EnvironmentLookup = _ => null,
        ToolDirectory = TinyToolDirectory(),
        OwnershipWrite = ownershipWrite,
        Launch = launch ?? (_ => Environment.ProcessId),
    };

    private static CheckpointManifest Manifest() => new()
    {
        Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Command = "true", EstimatedMinutes = 1 }],
    };

    private static ProcessIdentity Dead() => new ProcessIdentityProbe().Current() with
    { Pid = int.MaxValue, StartUtcTicks = 1 };

    private static string[] Runs(string repo)
    {
        var root = Path.Combine(repo, ".antiphon", "checkpoints");
        return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
    }
}
