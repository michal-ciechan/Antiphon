using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class WaitCommandTests
{
    [Test]
    public async Task prints_a_heartbeat_per_interval()
    {
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
        var dir = CheckpointFixtures.TempDir();
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState
        {
            RunId = "done-1",
            Phase = "done",
            ExitCode = 1,
            ExecutorPid = 9,
            StartedAt = DateTimeOffset.UtcNow,
        });
        File.WriteAllText(Path.Combine(dir, "report.md"), "--- checkpoint report ---\nverdict: RED exit=1\n");
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(false), delay: Fast).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(1);
        output.ToString().ShouldContain("verdict: RED exit=1");
    }

    [Test]
    public async Task returns_6_when_executor_died()
    {
        var dir = Running(pid: 99);
        File.WriteAllText(Path.Combine(dir, "executor.log"), "boom tail\n");
        var output = new StringWriter();
        var code = await new WaitCommand(liveness: new Alive(false), delay: Fast).WaitAsync(
            dir, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), output, CancellationToken.None);
        code.ShouldBe(6);
        output.ToString().ShouldContain("boom tail");
    }

    private static string Running(int pid)
    {
        var dir = CheckpointFixtures.TempDir();
        new RunStateStore().Write(Path.Combine(dir, "state.json"), new RunState
        {
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
}
