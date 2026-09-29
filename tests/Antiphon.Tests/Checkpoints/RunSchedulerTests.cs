using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RunSchedulerTests : CheckpointTestBase
{
    [Test]
    public async Task builds_never_overlap_even_with_free_slots_and_width_two()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = InstantDriver(driver =>
        {
            driver.When(request => CheckpointFixtures.IsBuild(request) && request.Arguments.Contains("--property:OutputPath=bin-a/"),
                async (_, token) => { await first.Task.WaitAsync(token); return new DriverResult(0, "", ""); });
            driver.When(CheckpointFixtures.IsBuild,
                async (_, token) => { await second.Task.WaitAsync(token); return new DriverResult(0, "", ""); });
        });
        var manifest = TwoBuildRows();
        var task = Schedule(driver, manifest, manifest.Checkpoints, 2);
        try
        {
            await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsBuild) >= 1);
            await Task.Delay(100);
            driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
            first.TrySetResult();
            await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsBuild) == 2);
        }
        finally { first.TrySetResult(); second.TrySetResult(); }
        var result = await task;
        driver.MaxInFlightFor(CheckpointFixtures.IsBuild).ShouldBe(1);
        result.State.MaxConcurrentBuilds.ShouldBe(1);
        result.Rows.Count(row => row.State == "green").ShouldBe(2);
    }

    [Test]
    public async Task rows_of_a_finished_build_run_while_the_next_build_is_in_flight()
    {
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(request => CheckpointFixtures.IsBuild(request) && request.Arguments.Contains("--property:OutputPath=bin-b/"),
            async (_, token) => { await second.Task.WaitAsync(token); return new DriverResult(0, "", ""); });
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var manifest = TwoBuildRows();
        var task = Schedule(driver, manifest, manifest.Checkpoints, 2);
        try { await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsRun) == 1); }
        finally { second.TrySetResult(); }
        var result = await task;
        driver.Starts.Single(start => CheckpointFixtures.IsRun(start.Request) && start.Request.Arguments.Contains("--property:OutputPath=bin-a/"))
            .InFlight.Any(CheckpointFixtures.IsBuild).ShouldBeTrue();
        result.Rows.Count(row => row.State == "green").ShouldBe(2);
    }

    [Test]
    public async Task serial_runs_one_activity_at_a_time_builds_included()
    {
        var driver = InstantDriver();
        var manifest = TwoBuildRows();
        var result = await Schedule(driver, manifest, manifest.Checkpoints, 2, serialAll: true);
        result.Rows.Count(row => row.State == "green").ShouldBe(2);
        driver.Starts.Select(start => CheckpointFixtures.IsBuild(start.Request) ? "build" : "run")
            .ShouldBe(["build", "run", "build", "run"]);
        driver.MaxInFlight.ShouldBe(1);
    }

    [Test]
    public async Task an_exclusive_row_keeps_the_build_lane_idle_until_it_finishes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = InstantDriver(driver => driver.When(CheckpointFixtures.IsRun, async (request, token) =>
        {
            if (request.Arguments.Contains("--property:OutputPath=bin-a/")) await release.Task.WaitAsync(token);
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return new DriverResult(0, "", "");
        }));
        var manifest = TwoBuildRows();
        manifest.Checkpoints[0].Serial = true;
        var task = Schedule(driver, manifest, manifest.Checkpoints, 2);
        try
        {
            await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsRun) == 1);
            await Task.Delay(100);
            driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
        }
        finally { release.TrySetResult(); }
        var result = await task;
        result.Rows.Count(row => row.State == "green").ShouldBe(2);
        driver.MaxInFlight.ShouldBe(1);
    }

    [Test]
    public async Task an_exclusive_row_waits_for_the_in_flight_build_of_another_row()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = new FakeDriver();
        driver.When(request => CheckpointFixtures.IsBuild(request) && request.Arguments.Contains("--property:OutputPath=bin-b/"),
            async (_, token) => { await release.Task.WaitAsync(token); return new DriverResult(0, "", ""); });
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var manifest = TwoBuildRows();
        manifest.Checkpoints.Insert(1, CheckpointFixtures.Row("CP-3", "bin-a", serial: true));
        var task = Schedule(driver, manifest, manifest.Checkpoints, 2);
        try
        {
            await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsBuild) == 2);
            await Task.Delay(100);
            driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
        }
        finally { release.TrySetResult(); }
        await task;
        driver.Starts.Single(start => CheckpointFixtures.IsRun(start.Request) && start.Request.Arguments.Any(arg => arg.Contains("CP-3Tests")))
            .InFlight.Any(CheckpointFixtures.IsBuild).ShouldBeFalse();
    }

    [Test]
    public async Task builds_start_in_the_order_rows_first_need_them()
    {
        var driver = InstantDriver();
        var manifest = TwoBuildRows();
        manifest.Builds.Reverse();
        await Schedule(driver, manifest, manifest.Checkpoints, 2);
        driver.Starts.Where(start => CheckpointFixtures.IsBuild(start.Request))
            .Select(start => start.Request.Arguments.Single(arg => arg.StartsWith("--property:OutputPath=")))
            .ShouldBe(["--property:OutputPath=bin-a/", "--property:OutputPath=bin-b/"]);
    }

    [Test]
    public async Task unreferenced_builds_end_unused_and_state_records_one_concurrent_build()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = InstantDriver(driver =>
        {
            driver.When(request => CheckpointFixtures.IsBuild(request) && request.Arguments.Contains("--property:OutputPath=bin-a/"),
                async (_, token) => { await first.Task.WaitAsync(token); return new DriverResult(0, "", ""); });
            driver.When(CheckpointFixtures.IsBuild,
                async (_, token) => { await second.Task.WaitAsync(token); return new DriverResult(0, "", ""); });
        });
        var manifest = TwoBuildRows();
        manifest.Builds.Add(new BuildSpec { Id = "bin-unused", Project = "tests/Antiphon.Tests", OutputPath = "bin-unused/" });
        var task = Schedule(driver, manifest, manifest.Checkpoints, 2);
        try
        {
            await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsBuild) >= 1);
            await Task.Delay(100);
            first.TrySetResult();
            await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsBuild) >= 2);
        }
        finally { first.TrySetResult(); second.TrySetResult(); }
        var result = await task;
        result.State.Builds.Single(build => build.Id == "bin-unused").State.ShouldBe("unused");
        result.State.Builds.Count(build => build.State == "ok").ShouldBe(2);
        result.State.MaxConcurrentBuilds.ShouldBe(1);
    }

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

    [Test]
    public async Task concurrent_rows_hold_distinct_leases_and_their_lines_say_granted()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var driver = BlockingDriver(release);
        var handler = new PidIdempotentSlotHandler();
        var manifest = OneBuildTwoRows();
        var root = TempDir();
        foreach (var row in manifest.Checkpoints)
            row.Expect = [];
        var client = new BuildSlotClient(
            handler,
            "http://slots.test/build-slots",
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(2),
            () => DateTimeOffset.UtcNow,
            (_, _) => Task.CompletedTask,
            holders: new SlotPids(11, 22, 33));
        var task = new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest,
            Rows = manifest.Checkpoints,
            RunDirectory = root,
            WorkingDirectory = root,
            State = new RunState { RunId = "test", StartedAt = DateTimeOffset.UtcNow },
            Slots = client,
            Commit = new string('a', 40),
            Width = 2,
            TotalTimeout = TimeSpan.FromMinutes(5),
        }, CancellationToken.None);
        await CheckpointFixtures.WaitUntil(() => driver.Count(CheckpointFixtures.IsRun) >= 2 && handler.LiveCount >= 2);
        handler.LiveCount.ShouldBe(2);
        release.TrySetResult();
        var result = await task;
        result.Rows.Count.ShouldBe(2);
        foreach (var row in result.Rows)
        {
            row.Line.ShouldContain("slot=granted");
            row.Line.ShouldNotContain("slot=unleased");
        }

        handler.LiveCount.ShouldBe(0);
    }

    private static CheckpointManifest OneBuildTwoRows()
    {
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-a"));
        return manifest;
    }

    private static CheckpointManifest TwoBuildRows()
    {
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" });
        manifest.Builds.Add(new BuildSpec { Id = "bin-b", Project = "tests/Antiphon.Tests", OutputPath = "bin-b/" });
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-1", "bin-a"));
        manifest.Checkpoints.Add(CheckpointFixtures.Row("CP-2", "bin-b"));
        return manifest;
    }

    private static FakeDriver InstantDriver(Action<FakeDriver>? beforeDefaults = null)
    {
        var driver = new FakeDriver();
        beforeDefaults?.Invoke(driver);
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

    private sealed class SlotPids(params int[] pids) : ILeaseHolderSource
    {
        private readonly Queue<int> _pids = new(pids);

        public ILeaseHolder Open() => new Holder(_pids.Dequeue());

        private sealed class Holder(int pid) : ILeaseHolder
        {
            public int Pid { get; } = pid;

            public string? ProcessStartUtc => null;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private Task<SchedulerResult> Schedule(FakeDriver driver, CheckpointManifest manifest, IReadOnlyList<CheckpointSpec> rows, int width, bool serialAll = false)
    {
        var root = TempDir();
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
            SerialAll = serialAll,
            TotalTimeout = TimeSpan.FromMinutes(5),
        }, CancellationToken.None);
    }
}
