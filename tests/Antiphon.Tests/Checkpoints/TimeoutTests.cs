using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class TimeoutTests
{
    [Test]
    public async Task row_deadline_kills_the_driver_tree_and_marks_timeout()
    {
        var driver = new FakeDriver();
        driver.When(_ => true, (_, token) => Task.Delay(Timeout.Infinite, token).ContinueWith(
            _ => new DriverResult(0, "", ""), TaskScheduler.Default));
        var result = await RowTimeout.RunWithDeadlineAsync(
            driver,
            new DriverRequest("dotnet", ["run"], CheckpointFixtures.TempDir()),
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);
        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldBe(5);
        driver.KillCount.ShouldBe(1);
        driver.LastKillEntireTree.ShouldBeTrue();
    }

    [Test]
    public async Task total_deadline_kills_running_and_skips_queued()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new DriverResult(0, "", "");
        });
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-a"));
        var scheduler = new RunScheduler(driver, new FakePlatform());
        var result = await scheduler.RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = manifest.Checkpoints,
            RunDirectory = CheckpointFixtures.TempDir(),
            WorkingDirectory = CheckpointFixtures.TempDir(),
            State = new RunState { RunId = "t", StartedAt = DateTimeOffset.UtcNow },
            Slots = new FixedSlotClient("unavailable"),
            Commit = new string('b', 40),
            Width = 1,
            TotalTimeout = TimeSpan.FromMilliseconds(200),
        }, CancellationToken.None);
        result.Rows.Select(row => row.State).OrderBy(state => state).ShouldBe(["skipped", "timeout"]);
    }

    [Test]
    public void row_deadline_is_max_15_or_3x_estimate()
    {
        RowTimeout.DeriveRowMinutes(3, null, 15).ShouldBe(15);
        RowTimeout.DeriveRowMinutes(10, null, 15).ShouldBe(30);
        RowTimeout.DeriveRowMinutes(null, null, 15).ShouldBe(15);
        RowTimeout.DeriveRowMinutes(10, 7, 15).ShouldBe(7);
    }
}
