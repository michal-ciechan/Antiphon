using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RunSchedulerTests
{
    [Test]
    public async Task builds_each_output_once()
    {
        var driver = InstantDriver();
        var manifest = OneBuildTwoRows();
        await Schedule(driver, manifest, manifest.Checkpoints, width: 2);
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(2);
    }

    [Test]
    public async Task rows_wait_for_their_build()
    {
        DateTimeOffset? buildEnded = null;
        DateTimeOffset? runStarted = null;
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) =>
        {
            buildEnded = DateTimeOffset.UtcNow;
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            runStarted ??= DateTimeOffset.UtcNow;
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        await Schedule(driver, OneBuildTwoRows(), OneBuildTwoRows().Checkpoints, width: 1);
        runStarted.ShouldNotBeNull();
        buildEnded.ShouldNotBeNull();
        runStarted.Value.ShouldBeGreaterThanOrEqualTo(buildEnded.Value);
    }

    [Test]
    public async Task width_two_runs_two_rows_at_once()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = BlockingDriver(release);
        var manifest = OneBuildTwoRows();
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-3", "bin-a"));
        var task = Schedule(driver, manifest, manifest.Checkpoints, width: 2);
        await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsRun) >= 2);
        driver.MaxInFlight.ShouldBeGreaterThanOrEqualTo(2);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(2);
        release.TrySetResult();
        var result = await task;
        result.Rows.Count(row => row.ExitCode == 0).ShouldBe(3);
        driver.MaxInFlight.ShouldBeLessThanOrEqualTo(3);
    }

    [Test]
    public async Task pty_project_rows_run_alone()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = BlockingDriver(release);
        var manifest = OneBuildTwoRows();
        manifest.Builds[0].Project = "tests/Antiphon.PtyHost.Tests";
        var task = Schedule(driver, manifest, manifest.Checkpoints, width: 2);
        await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsRun) >= 1);
        await Task.Delay(150);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        release.TrySetResult();
        await task;
        driver.MaxInFlight.ShouldBe(1);
    }

    [Test]
    public async Task serial_rows_run_alone()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = BlockingDriver(release);
        var manifest = OneBuildTwoRows();
        manifest.Checkpoints[0].Serial = true;
        var task = Schedule(driver, manifest, manifest.Checkpoints, width: 2);
        await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsRun) >= 1);
        await Task.Delay(150);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        release.TrySetResult();
        await task;
        driver.MaxInFlight.ShouldBe(1);
    }

    [Test]
    public async Task failed_build_fails_only_its_rows()
    {
        var driver = new FakeDriver();
        driver.When(request => CheckpointFixtures.IsBuild(request) && request.Arguments.Contains("tests/Broken.Tests"),
            (_, _) => Task.FromResult(new DriverResult(3, "", "fail")));
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Broken.Tests", OutputPath = "bin-a/" });
        manifest.Builds.Add(new BuildSpec { Id = "bin-b", Project = "tests/Antiphon.Tests", OutputPath = "bin-b/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-b"));
        var result = await Schedule(driver, manifest, manifest.Checkpoints, width: 2);
        result.Rows.Single(row => row.Id == "CP-1").State.ShouldBe("build-failed");
        result.Rows.Single(row => row.Id == "CP-1").ExitCode.ShouldBe(2);
        result.Rows.Single(row => row.Id == "CP-2").ExitCode.ShouldBe(0);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
    }

    private static CheckpointManifest OneBuildTwoRows()
    {
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-a"));
        return manifest;
    }

    private static FakeDriver InstantDriver()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return driver;
    }

    private static FakeDriver BlockingDriver(TaskCompletionSource release)
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, async (request, token) =>
        {
            await release.Task.WaitAsync(token);
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return new DriverResult(0, "", "");
        });
        return driver;
    }

    private static Task<SchedulerResult> Schedule(FakeDriver driver, CheckpointManifest manifest, IReadOnlyList<CheckpointSpec> rows, int width)
    {
        var root = CheckpointFixtures.TempDir();
        foreach (var row in rows)
            row.Expect = [];
        var scheduler = new RunScheduler(driver, new FakePlatform());
        return scheduler.RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = rows,
            RunDirectory = root,
            WorkingDirectory = root,
            State = new RunState { RunId = "test", StartedAt = DateTimeOffset.UtcNow },
            Slots = new FixedSlotClient("unavailable"),
            Commit = new string('a', 40),
            Width = width,
            TotalTimeout = TimeSpan.FromMinutes(5),
        }, CancellationToken.None);
    }
}
