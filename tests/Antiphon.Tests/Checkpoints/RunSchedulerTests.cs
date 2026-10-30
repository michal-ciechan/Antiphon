using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
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
        result.Rows.Count(row => row.State == "green").ShouldBe(2,
            string.Join(Environment.NewLine, result.Rows.Select(row =>
                $"{row.Id}: {row.State}; terminal={row.TerminalReason}; slot={row.SlotReason}; "
                + $"trx={row.Trx?.Error}; {row.Line}; {row.Output}")));
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
        driver.Starts.Single(start => CheckpointFixtures.IsRun(start.Request) && HasOutput(start.Request, "bin-a/"))
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
            if (HasOutput(request, "bin-a/")) await release.Task.WaitAsync(token);
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
        var buildArgs = driver.Starts.Single(start => CheckpointFixtures.IsBuild(start.Request)).Request.Arguments;
        var runArgs = driver.Starts.First(start => CheckpointFixtures.IsRun(start.Request)).Request.Arguments;
        var artifacts = buildArgs.Single(arg => arg.StartsWith("--property:ArtifactsPath=", StringComparison.Ordinal));
        runArgs.ShouldContain(artifacts);
        buildArgs.ShouldContain("--property:UseArtifactsOutput=true");
        runArgs.ShouldContain("--property:UseArtifactsOutput=true");
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
        foreach (var build in manifest.Builds) Directory.CreateDirectory(ProjectWriteRoots.DirectoryOf(Path.GetFullPath(Path.Combine(root, build.Project))));
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

    [Test]
    public async Task records_output_directories_the_build_created()
    {
        var driver = InstantDriver(driver => driver.When(CheckpointFixtures.IsBuild, (request, _) =>
        {
            if (request.Arguments.Contains("--property:OutputPath=bin-a/"))
            {
                Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, "proj", "bin-a"));
                Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, "tests"));
            }

            return Task.FromResult(new DriverResult(0, "", ""));
        }));
        var manifest = TwoBuildRows();
        var result = await Schedule(driver, manifest, manifest.Checkpoints, 1, serialAll: true);
        result.State.CreatedOutputPaths.ShouldContain(path =>
            path.EndsWith(Path.Combine("proj", "bin-a"), StringComparison.Ordinal));
        result.State.CreatedOutputPaths.ShouldAllBe(path =>
            Path.GetFileName(path) == "bin-a" || Path.GetFileName(path) == "bin-b");
        result.State.CreatedOutputPaths.Any(path => Path.GetFileName(path) == "tests").ShouldBeFalse();
        result.Rows.Count(row => row.State == "green").ShouldBe(2);
    }

    [Test]
    public void build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it()
    {
        // red (CARD-1356): the build deadline is max(15, timeouts.rowMinutes) whatever its rows need, and
        // --row-timeout never reaches it.
        var manifest = TwoBuildRows();
        manifest.Checkpoints[0].TimeoutMinutes = 40;            // CP-1 on bin-a: explicit 40
        manifest.Checkpoints[1].Build = "bin-a";                // CP-2 on bin-a: estimate 10, derived 30
        manifest.Checkpoints[1].TimeoutMinutes = null;
        manifest.Checkpoints[1].EstimatedMinutes = 10;
        var third = CheckpointFixtures.Row("CP-3", "bin-b");    // CP-3 on bin-b: estimate 1, derived floor 15
        third.TimeoutMinutes = null;
        third.EstimatedMinutes = 1;
        manifest.Checkpoints.Add(third);

        RunScheduler.BuildDeadlineMinutes(manifest, manifest.Checkpoints, "bin-a", null).ShouldBe(40);
        RunScheduler.BuildDeadlineMinutes(manifest, manifest.Checkpoints, "bin-b", null).ShouldBe(15);
        RunScheduler.BuildDeadlineMinutes(manifest, manifest.Checkpoints, "bin-a", TimeSpan.FromMinutes(7)).ShouldBe(7);
        RunScheduler.BuildDeadlineMinutes(manifest, manifest.Checkpoints, "bin-b", TimeSpan.FromMinutes(7)).ShouldBe(7);
        manifest.Timeouts.RowMinutes = 20;
        RunScheduler.BuildDeadlineMinutes(manifest, manifest.Checkpoints, "bin-b", null).ShouldBe(20);
        RunScheduler.BuildDeadlineMinutes(manifest, manifest.Checkpoints, "bin-a", null).ShouldBe(40);
    }

    [Test]
    public async Task scheduler_passes_the_derived_build_deadline_and_records_it()
    {
        // red (CARD-1356): BuildProgress.DeadlineMinutes stays 0 and report.json records 0; the build ran under
        // the 15-minute floor whatever CP-1 needs.
        var driver = InstantDriver();
        var manifest = TwoBuildRows();
        manifest.Checkpoints[0].TimeoutMinutes = 40;
        var result = await Schedule(driver, manifest, manifest.Checkpoints, 2);
        result.Rows.Count(row => row.State == "green").ShouldBe(2);
        result.State.Builds.Single(build => build.Id == "bin-a").DeadlineMinutes.ShouldBe(40);
        result.State.Builds.Single(build => build.Id == "bin-b").DeadlineMinutes.ShouldBe(15);

        var run = TempDir();
        File.WriteAllText(Path.Combine(run, "manifest.resolved.yaml"), ManifestLoader.ToYaml(manifest)); // the report hashes the resolved manifest
        var report = CheckpointApp.BuildReport(run, run, new RunRequest { Commit = new string('a', 40), Branch = "fixture" }, manifest, result.State, result);
        report.Builds.Single(build => build.Id == "bin-a").DeadlineMinutes.ShouldBe(40);
        report.Builds.Single(build => build.Id == "bin-b").DeadlineMinutes.ShouldBe(15);
    }

    [Test]
    public async Task build_timer_uses_selected_row_deadlines_and_override()
    {
        // First arm is the behavioral witness for the former fixed 15-minute build deadline.
        await BuildArm(null, 15, null, 30, 16, 20);
        await BuildArm(15, 15, null, 40, 35, 35, shared: true);
        await BuildArm(null, 15, 40, 40, 35, 35);
        await BuildArm(40, 20, 7, 7, 6, 7, expires: true);

        // Caller cancellation must propagate even when the deadline is disabled.
        foreach (var minutes in new[] { 40, 0, -1 })
        {
            var root = TempDir();
            var clock = new ControlledTimeProvider();
            using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var driver = new FakeDriver();
            driver.When(_ => true, async (_, token) =>
            {
                entered.TrySetResult(token);
                await release.Task.WaitAsync(token);
                return new DriverResult(0, "", "");
            });
            var task = RowTimeout.RunWithDeadlineAsync(driver,
                new DriverRequest("dotnet", ["build"], root, Path.Combine(root, "build.log")),
                TimeSpan.FromMinutes(minutes), clock, caller.Token);
            try
            {
                var token = await entered.Task.WaitAsync(caller.Token);
                clock.Advance(TimeSpan.FromMinutes(1));
                token.IsCancellationRequested.ShouldBeFalse();
                if (minutes <= 0)
                    clock.Events.ShouldBeEmpty();
                caller.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(async () => await task);
            }
            finally
            {
                release.TrySetResult();
                caller.Cancel();
                try { await task; }
                catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
            }
            driver.InFlight.ShouldBe(0);
            driver.KillCount.ShouldBe(0);
        }

        async Task BuildArm(int? rowMinutes, int manifestMinutes, int? overrideMinutes,
            int expectedMinutes, int aliveAt, int finishAt, bool shared = false, bool expires = false)
        {
            var root = TempDir();
            var clock = new ControlledTimeProvider();
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var driver = InstantDriver(driver => driver.When(CheckpointFixtures.IsBuild, async (_, token) =>
            {
                entered.TrySetResult(token);
                await release.Task.WaitAsync(token);
                return new DriverResult(0, "", "");
            }));
            var manifest = OneBuildTwoRows();
            manifest.Timeouts.RowMinutes = manifestMinutes;
            manifest.Checkpoints[0].TimeoutMinutes = rowMinutes;
            manifest.Checkpoints[0].EstimatedMinutes = 10;
            manifest.Checkpoints[1].TimeoutMinutes = 40;
            var unselected = CheckpointFixtures.Row("CP-3", "bin-a");
            unselected.TimeoutMinutes = 90;
            manifest.Checkpoints.Add(unselected);
            var selected = manifest.Checkpoints.Take(shared ? 2 : 1).ToList();
            foreach (var row in selected)
                row.Expect = [];
            foreach (var build in manifest.Builds)
                Directory.CreateDirectory(ProjectWriteRoots.DirectoryOf(Path.GetFullPath(Path.Combine(root, build.Project))));
            var task = new RunScheduler(driver, new FakePlatform(), clock).RunAsync(new SchedulerRequest
            {
                Manifest = manifest,
                Rows = selected,
                RunDirectory = root,
                WorkingDirectory = root,
                State = new RunState { RunId = "deadline", StartedAt = DateTimeOffset.UtcNow },
                Slots = new FixedSlotClient("granted"),
                Commit = new string('a', 40),
                ExpectedSourceSha = new string('a', 40),
                RowTimeoutOverride = overrideMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : null,
                Width = 1,
            }, watchdog.Token);
            SchedulerResult result;
            try
            {
                var token = await entered.Task.WaitAsync(watchdog.Token);
                var timer = await clock.WaitForTimerAsync(e => e.Action == "create" && e.DueTime == TimeSpan.FromMinutes(expectedMinutes), ct: watchdog.Token);
                clock.Advance(TimeSpan.FromMinutes(aliveAt));
                token.IsCancellationRequested.ShouldBeFalse("the held build is still within its selected row deadline");
                timer.DueTime.ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
                clock.Advance(TimeSpan.FromMinutes(finishAt - aliveAt));
                token.IsCancellationRequested.ShouldBe(expires);
                if (!expires)
                    release.TrySetResult();
                result = await task;
            }
            finally
            {
                release.TrySetResult();
                await task;
            }
            driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1);
            driver.InFlight.ShouldBe(0);
            driver.KillCount.ShouldBe(0);
            result.State.Builds.Single().DeadlineMinutes.ShouldBe(expectedMinutes);
            result.State.Builds.Single().State.ShouldBe(expires ? "failed" : "ok");
            result.ExitCode.ShouldBe(expires ? ExitCodes.Timeout : ExitCodes.Green);
            driver.Count(CheckpointFixtures.IsRun).ShouldBe(expires ? 0 : selected.Count);
            result.Rows.Count.ShouldBe(selected.Count);
            if (!expires)
                result.Rows.ShouldAllBe(row => row.State == "green");
            clock.TimerStates.Values.ShouldAllBe(state => state == "dispose");
        }
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

            public string? ProcessStartUtc => "2026-09-30T00:00:00.0000000Z";

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private Task<SchedulerResult> Schedule(FakeDriver driver, CheckpointManifest manifest, IReadOnlyList<CheckpointSpec> rows, int width, bool serialAll = false)
    {
        var root = TempDir();
        foreach (var row in rows)
            row.Expect = [];
        foreach (var build in manifest.Builds) Directory.CreateDirectory(ProjectWriteRoots.DirectoryOf(Path.GetFullPath(Path.Combine(root, build.Project))));
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
            ExpectedSourceSha = new string('a', 40),
            Width = width,
            SerialAll = serialAll,
            TotalTimeout = TimeSpan.FromMinutes(5),
        }, CancellationToken.None);
    }

    private static bool HasOutput(DriverRequest request, string output) =>
        request.Arguments.Any(arg => arg.StartsWith("--property:OutputPath=", StringComparison.Ordinal)
            && arg.Replace('\\', '/').EndsWith(output, StringComparison.Ordinal));
}
