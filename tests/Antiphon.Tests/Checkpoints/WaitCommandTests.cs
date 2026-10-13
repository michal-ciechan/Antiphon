using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class WaitCommandTests : CheckpointTestBase
{

    // case: completed state with lost reports; mutation: fall back to heartbeat and green;
    // red: wait returns zero without the mandatory receipts.
    [Test]
    public async Task completed_wait_requires_report_receipts()
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: false);
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState { Source = fixtureSource, RunId = Path.GetFileName(dir), Phase = "done", ExitCode = 0, StartedAt = DateTimeOffset.UtcNow });
        var output = new StringWriter();
        var exit = await new WaitCommand(liveness: new Alive(false), delay: Fast).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        exit.ShouldBe(2, output.ToString());
        output.ToString().ShouldContain("receipt");
    }

    [Test]
    public void heartbeat_lists_a_queued_build_after_the_building_one()
    {
        var fixtureSource = new SourceEvidence();
        var now = DateTimeOffset.Parse("2026-09-30T12:00:00Z");
        var state = new RunState { Source = fixtureSource,
            RunId = "live",
            StartedAt = now.AddMinutes(-1),
            Builds =
            [
                new BuildProgress { Id = "bin-a", State = "building", StartedAt = now.AddSeconds(-30) },
                new BuildProgress { Id = "bin-b", State = "pending" },
                new BuildProgress { Id = "bin-c", State = "unused" },
            ],
        };
        var beat = state.Heartbeat(now);
        beat.ShouldContain("bin-a building");
        beat.ShouldContain("bin-b queued");
        beat.IndexOf("bin-a building", StringComparison.Ordinal).ShouldBeLessThan(beat.IndexOf("bin-b queued", StringComparison.Ordinal));
        beat.ShouldNotContain("bin-c");
    }

    [Test]
    public async Task prints_a_heartbeat_per_interval()
    {
        var fixtureSource = new SourceEvidence();
        var dir = Running(pid: 42);
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(true), delay: Fast).WaitAsync(
            dir, TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(40), output, CancellationToken.None);
        code.ShouldBe(75);
        Count(output, "HEARTBEAT").ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task returns_75_with_progress_at_max_wait()
    {
        var fixtureSource = new SourceEvidence();
        var dir = Running(pid: 7);
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(true), delay: Fast).WaitAsync(
            dir, TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(75);
        output.ToString().ShouldContain("STILL RUNNING");
    }

    [Test]
    public async Task prints_report_and_run_exit_when_done()
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: false);
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState { Source = fixtureSource,
            RunId = "done-1",
            Phase = "done",
            ExitCode = 1,
            ExecutorPid = 9,
            StartedAt = DateTimeOffset.UtcNow,
        });
        ReportWriter.WriteFiles(dir, new ReportModel { Source = fixtureSource, RunId = "done-1", ExitCode = 1, Verdict = "RED" });
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(false), delay: Fast).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(1);
        output.ToString().ShouldContain("verdict: RED exit=1");
    }

    [Test]
    public async Task executor_finish_leaves_the_image_and_wait_deletes_it_once_the_pid_is_gone()
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: false);
        var tool = Path.Combine(dir, "tool");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(tool, SafeWrite.Under(dir)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "x", SafeWrite.Under(tool)));
        SafeWrite.Require(OwnedOutputLedger.Seal(tool));
        ReportWriter.WriteFiles(dir, new ReportModel { Source = fixtureSource, RunId = "done-tool", ExitCode = 0, Verdict = "GREEN" });
        var state = new RunState { Source = fixtureSource,
            RunId = "done-tool",
            Phase = "running",
            ExecutorPid = 42,
            ExitCode = 0,
            StartedAt = DateTimeOffset.UtcNow,
        };
        CheckpointApp.Finish(
            state,
            0,
            () => new RunStateStore().Write(Path.Combine(dir, "state.json"), state),
            () => EvidenceFolder.TryRemoveToolCopy(dir, tool),
            _ => { });
        state.Phase.ShouldBe("done");
        Directory.Exists(tool).ShouldBeTrue();
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(false), delay: Fast).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(0);
        Directory.Exists(tool).ShouldBeFalse();
        output.ToString().ShouldContain("GREEN");
    }

    [Test]
    public async Task wait_leaves_the_tool_copy_while_the_finished_executor_is_still_alive()
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: true);
        var tool = Path.Combine(dir, "tool");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(tool, SafeWrite.Under(dir)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "x", SafeWrite.Under(tool)));
        SafeWrite.Require(OwnedOutputLedger.Seal(tool));
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState { Source = fixtureSource,
            RunId = "exiting",
            Phase = "done",
            ExitCode = 0,
            ExecutorPid = 42,
            StartedAt = DateTimeOffset.UtcNow,
        });
        ReportWriter.WriteFiles(dir, new ReportModel { Source = fixtureSource, RunId = "exiting", ExitCode = 0, Verdict = "GREEN" });
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(true), delay: Fast).WaitAsync(
            dir, TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(75);
        Directory.Exists(tool).ShouldBeTrue();
    }

    [Test]
    public async Task returns_6_when_executor_died()
    {
        var fixtureSource = new SourceEvidence();
        var dir = Running(pid: 99);
        File.WriteAllText(Path.Combine(dir, "executor.log"), "boom tail\n");
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(false), delay: Fast).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(6);
        output.ToString().ShouldContain("boom tail");
    }

    [Test]
    public async Task dead_exit_rereads_terminal_state_before_reporting_death()
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: false);
        var tool = Path.Combine(dir, "tool");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(tool, SafeWrite.Under(dir)));
        SafeWrite.Require(SafeWrite.Text(Path.Combine(tool, "Antiphon.Checkpoints.dll"), "x", SafeWrite.Under(tool)));
        SafeWrite.Require(OwnedOutputLedger.Seal(tool));
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState { Source = fixtureSource,
            RunId = "race-1",
            Phase = "running",
            ExecutorPid = 99,
            StartedAt = DateTimeOffset.Parse("2026-10-05T19:16:00Z"),
        });
        ReportWriter.WriteFiles(dir, new ReportModel { Source = fixtureSource, RunId = "race-1", ExitCode = 1, Verdict = "RED" });
        File.WriteAllText(Path.Combine(dir, "executor.log"), "2026-10-05T19:17:02.7348556+00:00 done exit=0\n");
        var probe = new TerminalOnDeath(Path.Combine(dir, "state.json"));
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(false), delay: Fast, probe: probe).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        var text = output.ToString();
        code.ShouldBe(1, $"phaseAtDeath={probe.PhaseAtDeath} observations={probe.Observations} output={text}");
        probe.PhaseAtDeath.ShouldBe("running");
        text.ShouldContain("verdict: RED exit=1");
        text.ShouldNotContain("executor died without phase=done");
        Directory.Exists(tool).ShouldBeFalse();
    }

    [Test]
    public void running_row_heartbeat_uses_started_at()
    {
        var fixtureSource = new SourceEvidence();
        var now = DateTimeOffset.Parse("2026-09-26T12:00:00Z");
        var state = new RunState { Source = fixtureSource,
            RunId = "live",
            StartedAt = now.AddMinutes(-2),
            Rows =
            [
                new RowProgress
                {
                    Id = "CP-1",
                    State = "running",
                    StartedAt = now.AddSeconds(-90),
                    Seconds = 0,
                },
            ],
        };
        var beat = state.Heartbeat(now);
        beat.ShouldContain("CP-1 running 1m30s");
        beat.ShouldNotContain("running 0m00s");
    }

    [Test]
    public async Task empty_progress_does_not_print_a_blank_line()
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: true);
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState { Source = fixtureSource,
            RunId = "live",
            Phase = "running",
            ExecutorPid = 4,
            StartedAt = DateTimeOffset.UtcNow,
        });
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(true), delay: Fast).WaitAsync(
            dir, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5), output, CancellationToken.None);
        code.ShouldBe(75);
        var text = output.ToString().Replace("\r\n", "\n");
        var first = text.Split('\n')[0];
        first.Length.ShouldBeGreaterThan(0);
        first.ShouldStartWith("HEARTBEAT");
        first.ShouldNotEndWith("|");
        text.ShouldContain("STILL RUNNING");
    }

    private string Running(int pid)
    {
        var fixtureSource = new SourceEvidence();
        var dir = TempDir();
        CheckpointFixtures.MarkRun(dir, alive: pid != 99);
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState { Source = fixtureSource,
            RunId = "live",
            Phase = "running",
            ExecutorPid = pid,
            StartedAt = DateTimeOffset.UtcNow,
            Rows = [new RowProgress { Id = "CP-1", State = "running" }],
        });
        return dir;
    }

    private static Task Fast(TimeSpan _, CancellationToken token) => Task.Delay(5, token);

    private static int Count(StringWriter output, string token) =>
        output.ToString().Split('\n').Count(line => line.Contains(token, StringComparison.Ordinal));

    private sealed class Alive(bool alive) : IProcessLiveness
    {
        public bool IsAlive(int pid) => alive;
    }

    private sealed class TerminalOnDeath(string statePath) : ProcessIdentityProbe
    {
        public string? PhaseAtDeath { get; private set; }
        public int Observations { get; private set; }

        public override ProcessObservation Observe(ProcessIdentity? expected)
        {
            Observations++;
            var store = new RunStateStore();
            var state = store.TryRead(statePath);
            PhaseAtDeath ??= state?.Phase;
            store.Write(statePath, new RunState
            {
                Source = state?.Source ?? new SourceEvidence(),
                RunId = state?.RunId ?? "",
                Phase = "done",
                ExitCode = 1,
                ExecutorPid = state?.ExecutorPid ?? 0,
                StartedAt = state?.StartedAt ?? DateTimeOffset.UnixEpoch,
                EndedAt = state?.StartedAt ?? DateTimeOffset.UnixEpoch,
            });
            return new ProcessObservation(ProcessVerdict.Dead, "identity-dead");
        }
    }
}
