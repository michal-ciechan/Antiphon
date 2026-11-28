using System.Net;
using System.Net.Http.Json;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RunSchedulerTests : CheckpointTestBase
{
    [Test]
    public void C2103_availability_is_bound_to_inputs_and_request_time()
    {
        var origin = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset At(int seconds) => origin.AddSeconds(seconds);
        var inputs = new BuildInputDescriptor("antiphon", new string('a', 40), "source-fingerprint",
            "tests/Antiphon.Tests", "effective-properties", "evaluated-graph", "toolchain", "runner-1", "linux", "x64");
        var request = new BuildAvailabilityRequest("consumer", At(20), inputs);
        var producer = new ObservedBuild("producer", "build-1", inputs, At(1), At(10), "ok", "generation-1", true);
        var custody = new BuildCustodyEvidence("runner-1", "build-1", "generation-1", true,
            At(5), At(40), null, At(5), At(25), At(25));

        Check("verified-ready", request, producer, custody, new("eligible-ready", BuildId: "build-1"));
        Check("settlement-boundary", request with { RequestedAt = At(10) }, producer, custody,
            new("eligible-ready", BuildId: "build-1"));
        var unknownGraph = inputs with { GraphDigest = null };
        Check("unknown-graph", request with { Inputs = unknownGraph }, producer with { Inputs = unknownGraph }, custody,
            new("unknown", "key-incomplete"));
        var changedInputs = new (string Label, BuildInputDescriptor Inputs)[]
        {
            ("same-sha-changed-properties", inputs with { PropertiesDigest = "different-properties" }),
            ("different-sha", inputs with { SourceSha = new string('b', 40) }),
            ("different-fingerprint", inputs with { Fingerprint = "different-fingerprint" }),
            ("different-graph", inputs with { GraphDigest = "different-graph" }),
            ("different-toolchain", inputs with { ToolchainDigest = "different-toolchain" }),
            ("different-runner", inputs with { Runner = "runner-2" }),
            ("different-os", inputs with { OperatingSystem = "windows" }),
            ("different-architecture", inputs with { Architecture = "arm64" }),
        };
        foreach (var (label, changed) in changedInputs)
            Check(label, request with { Inputs = changed }, producer, custody, new("ineligible", "key-mismatch"));
        Check("future-settlement", request, producer with { SettledAt = At(30) }, custody,
            new("in-flight-candidate", BuildId: "build-1"));
        Check("future-failure", request, producer with { SettledAt = At(30), Outcome = "failed" }, custody,
            new("in-flight-candidate", BuildId: "build-1"));
        Check("active-no-lease", request,
            producer with { SettledAt = null, Outcome = null, LeaseVerified = false }, custody,
            new("unknown", "lease-unverified"));
        Check("before-start", request with { RequestedAt = At(0) }, producer, custody,
            new("ineligible", "producer-not-started"));
        Check("failed-producer", request, producer with { Outcome = "failed" }, custody,
            new("ineligible", "producer-failed"));
        Check("canceled-producer", request, producer with { Outcome = "canceled" }, custody,
            new("ineligible", "producer-failed"));
        Check("revoked-custody", request, producer, custody with { RevokedAt = At(20) },
            new("ineligible", "custody-revoked"));
        Check("unverified-custody", request, producer, custody with { Verified = false },
            new("unknown", "custody-unverified"));
        Check("expired-custody", request, producer, custody with { Until = At(20) },
            new("ineligible", "custody-expired"));
        Check("removed-at-request", request with { RequestedAt = At(25) }, producer, custody,
            new("ineligible", "output-absent"));
        Check("different-output-generation", request, producer, custody with { OutputGeneration = "generation-2" },
            new("unknown", "custody-binding-mismatch"));
        Check("gap-in-presence-proof", request with { RequestedAt = At(25) }, producer, custody with { RemovedAt = null },
            new("unknown", "output-presence-unknown"));
        Check("unaligned-clock", request with { ClockAligned = false }, producer, custody,
            new("unknown", "clock-unaligned"));
    }

    private static void Check(string label, BuildAvailabilityRequest request, ObservedBuild producer,
        BuildCustodyEvidence? custody, BuildAvailabilityResult expected)
        => BuildAvailability.Classify(request, producer, custody).ShouldBe(expected, label);

    [Test]
    public async Task rows_wait_for_the_build_source_receipt_before_readiness()
    {
        var root = TempDir();
        var receiptEntered = new TaskCompletionSource<CheckpointBuildBinding>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReceipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentinelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manifest = TwoBuildRows();
        manifest.Checkpoints.Add(new CheckpointSpec { Id = "CP-3", Command = "sentinel" });
        foreach (var row in manifest.Checkpoints) row.Expect = [];
        foreach (var build in manifest.Builds)
            Directory.CreateDirectory(ProjectWriteRoots.DirectoryOf(Path.GetFullPath(Path.Combine(root, build.Project))));
        var state = new RunState { RunId = "receipt-order" };
        var driver = InstantDriver(driver =>
        {
            driver.When(request => CheckpointFixtures.IsRun(request) && HasOutput(request, "bin-a/"), async (request, token) =>
            {
                await releaseFirstRow.Task.WaitAsync(token);
                CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
                return new DriverResult(0, "", "");
            });
            driver.When(request => request.Arguments.Contains("sentinel"), (_, _) =>
            {
                sentinelEntered.TrySetResult();
                return Task.FromResult(new DriverResult(0, "", ""));
            });
        });
        var task = new RunScheduler(driver, new FakePlatform()).RunAsync(new SchedulerRequest
        {
            Manifest = manifest, Rows = manifest.Checkpoints, RunDirectory = root, WorkingDirectory = root,
            State = state, Slots = new FixedSlotClient("granted"), Width = 2,
            Commit = new string('a', 40), ExpectedSourceSha = new string('a', 40),
            BeforeBuildReceiptWrite = async (binding, token) =>
            {
                if (!binding.OutputPath.EndsWith("bin-b", StringComparison.Ordinal)) return;
                receiptEntered.TrySetResult(binding);
                await releaseReceipt.Task.WaitAsync(token);
            },
        }, watchdog.Token);
        CheckpointBuildBinding? heldBinding = null;
        string? heldState = null;
        string? heldReceipt = null;
        try
        {
            heldBinding = await receiptEntered.Task.WaitAsync(watchdog.Token);
            // The sentinel proves the scheduler has visited CP-2 while its receipt writer is held.
            await sentinelEntered.Task.WaitAsync(watchdog.Token);
            heldState = state.Builds.Single(build => build.Id == "bin-b").State;
            heldReceipt = heldBinding.Check();
        }
        finally
        {
            releaseReceipt.TrySetResult();
            releaseFirstRow.TrySetResult();
            await task;
        }
        var result = await task;
        result.Rows.Single(row => row.Id == "CP-2").State.ShouldBe("green",
            string.Join(Environment.NewLine, result.Rows.Select(row => row.Line + Environment.NewLine + row.Output)));
        heldState.ShouldBe("building");
        heldReceipt.ShouldBe("unknown");
        heldBinding.ShouldNotBeNull().Check().ShouldBe("verified");
        result.Rows.ShouldAllBe(row => row.State == "green");
        driver.InFlight.ShouldBe(0);
    }

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
        // The 2 s admission deadline runs on the controlled clock the retry delay advances, not on
        // a loaded host's real time (CARD-2072).
        var time = new ControlledTimeProvider(DateTimeOffset.UtcNow);
        var client = new BuildSlotClient(
            handler,
            "http://slots.test/build-slots",
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(2),
            delay: (span, _) => { time.Advance(span); return Task.CompletedTask; },
            holders: new SlotPids(11, 22, 33),
            timeProvider: time);
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

    private const string ObservationBuild = "bin-c2103-observe";
    private const string ObservationFilter = "/*/*/ExampleSurfaceTests/case";
    private static readonly DateTimeOffset ObservationEpoch = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid ObservationTask = Guid.Parse("9750517d-c558-46fd-913f-d0a530661490");
    private static readonly Guid ObservationSession = Guid.Parse("be8e637d-973b-4345-a6e1-30499ea89e84");
    private static readonly Guid ObservationLease = Guid.Parse("680dd065-b0f2-4c44-925f-6dda1e331d5e");
    private static readonly SourceObservation ObservationSource = new(new string('a', 40), 0,
        new string('b', 64), ObservationEpoch, "known");

    [Test]
    public async Task C2103_observation_preserves_command_and_receipt_bytes()
    {
        foreach (var grouped in new[] { true, false })
        foreach (var fails in new[] { false, true })
        {
            var root = TempDir();
            var repo = Path.Combine(root, "repo");
            var project = Path.Combine(repo, "sample");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var results = Path.Combine(repo, ".antiphon", "checkpoints");
            var baseline = await RunObservationArmAsync(repo, results, grouped, fails, null);
            var positive = await RunObservationArmAsync(repo, results, grouped, fails, TimingFault.None);
            var sinkFailure = await RunObservationArmAsync(repo, results, grouped, fails, TimingFault.BeforeSpool);
            var label = (grouped ? "grouped" : "standalone") + "-" + (fails ? "red" : "green");
            baseline.Exit.ShouldBe(fails ? 1 : 0, label + "-baseline-exit");
            positive.Exit.ShouldBe(baseline.Exit, label + "-exit");
            sinkFailure.Exit.ShouldBe(baseline.Exit, label + "-exit-with-sink-failure");
            baseline.Commands.Count.ShouldBe(2, label + "-build-and-test-invocation-count");
            baseline.Commands[0].Arguments.ShouldContain("-maxcpucount:6");
            baseline.Commands[1].Arguments.ShouldContain(ObservationFilter);
            baseline.Trx.Failed.ShouldBe(fails ? 1 : 0);
            baseline.Trx.Passed.ShouldBe(fails ? 0 : 1);
            baseline.Trx.Executed.ShouldBe(1);
            foreach (var other in new[] { positive, sinkFailure })
            {
                other.Commands.Count.ShouldBe(baseline.Commands.Count);
                other.Commands[0].Arguments.ShouldContain("-maxcpucount:6", label + "-argv");
                foreach (var (original, observed) in baseline.Commands.Zip(other.Commands))
                {
                    observed.FileName.ShouldBe(original.FileName);
                    observed.Arguments.ShouldBe(original.Arguments, label + "-argv");
                    observed.WorkingDirectory.ShouldBe(original.WorkingDirectory);
                    (observed.Environment ?? new Dictionary<string, string>()).ToArray()
                        .ShouldBe((original.Environment ?? new Dictionary<string, string>()).ToArray());
                    observed.ClearedEnvironmentPrefixes.ShouldBe(original.ClearedEnvironmentPrefixes);
                }
                other.Slots.ShouldBe(baseline.Slots);
                other.Json.ShouldBe(baseline.Json, label + "-json-bytes");
                other.Markdown.ShouldBe(baseline.Markdown, label + "-markdown-bytes");
                other.Line.ShouldBe(baseline.Line, label + "-checkpoint-line");
            }
            positive.Read.Events.Single().BuildPhase!.BuildId.ShouldBe(ObservationBuild);
            positive.Read.Events.Single().BuildPhase!.Invocation.ShouldBe(grouped ? "grouped-producer" : "standalone-composite");
            baseline.Read.CoverageReason.ShouldBe("observation-missing");
            sinkFailure.Read.CoverageReason.ShouldBe("observation-batch-incomplete");
            Console.WriteLine(FormattableString.Invariant(
                $"C2103 parity {label} offMs={baseline.ExecutionMilliseconds:F2} onMs={positive.ExecutionMilliseconds:F2} sinkFailureMs={sinkFailure.ExecutionMilliseconds:F2}"));
        }
    }

    [Test]
    public async Task C2103_stalled_sink_does_not_hold_checkpoint_completion()
    {
        foreach (var grouped in new[] { true, false })
        {
            var repo = TempDir();
            Directory.CreateDirectory(Path.Combine(repo, "sample"));
            File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"), """<Project Sdk="Microsoft.NET.Sdk" />""");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var publication = new BuildObservationPublication();
            var results = Path.Combine(repo, ".antiphon", "checkpoints");
            var arm = Task.Run(() => RunObservationArmAsync(repo, results,
                grouped, fails: true, TimingFault.None, journal => journal.FlushGate = () =>
                {
                    entered.TrySetResult();
                    release.Wait(TimeSpan.FromSeconds(8));
                    return Task.CompletedTask;
                }, stalledPublication: publication));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var result = await arm.WaitAsync(TimeSpan.FromSeconds(3));
                result.Exit.ShouldBe(ExitCodes.FailedTests);
                publication.Completion.IsCompleted.ShouldBeFalse("the verdict is available while the sink is blocked");
            }
            finally
            {
                release.Set();
                await arm;
                await publication.Completion;
            }
            var run = Directory.GetDirectories(results).Single();
            var read = await BuildObservationReplay.ReadAsync(run, CancellationToken.None);
            read.CoverageReason.ShouldBe("observation-batch-incomplete");
        }
    }

    [Test]
    public async Task C2103_produced_attempt_is_bound_to_the_lease()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, "sample"));
        File.WriteAllText(Path.Combine(repo, "sample", "sample.csproj"), """<Project Sdk="Microsoft.NET.Sdk" />""");
        var result = await RunObservationArmAsync(repo, Path.Combine(repo, ".antiphon", "checkpoints"),
            grouped: false, fails: false, TimingFault.None);
        var phase = result.Read.Events.Single().BuildPhase!;
        phase.TaskId.ShouldBe(ObservationTask.ToString("D"));
        phase.AttemptId.ShouldBe(ObservationSession.ToString("D"), "owner-attempt-from-the-production-row");
        phase.LeaseId.ShouldBe(ObservationLease.ToString("D"));
    }

    [Test]
    public async Task C2103_standalone_refusal_does_not_retain_raw_project_path()
    {
        var repo = TempDir();
        const string project = "customer-private/sample.csproj";
        var result = await RunObservationArmAsync(repo, Path.Combine(repo, ".antiphon", "checkpoints"),
            grouped: false, fails: false, TimingFault.None, project: project, refuse: true);
        result.Exit.ShouldBe(ExitCodes.Invalid);
        result.Commands.ShouldBeEmpty();
        var phase = result.Read.Events.Single().BuildPhase!;
        System.Text.Json.JsonSerializer.Serialize(phase, TimingJournal.Json).ShouldNotContain(project);
    }

    private static async Task<ObservationArm> RunObservationArmAsync(string repo, string results,
        bool grouped, bool fails, TimingFault? fault, Action<TimingJournal>? configureJournal = null,
        string project = "sample", bool refuse = false, BuildObservationPublication? stalledPublication = null)
    {
        var clock = new ControlledTimeProvider(ObservationEpoch);
        var driver = new FakeDriver();
        var slots = new ObservationSlotTrace(refuse);
        var console = new StringWriter();
        var runtime = new CheckpointApp.Runtime
        {
            RepoRoot = repo, Driver = driver, Slots = slots, SourceCapture = _ => ObservationSource,
            EnvironmentLookup = name => !grouped ? name switch
            {
                "ANTIPHON_TASK_ID" => ObservationTask.ToString("D"),
                "ANTIPHON_SESSION_ID" => ObservationSession.ToString("D"),
                _ => null,
            } : null, TimeProvider = clock, Output = console,
            CleanupContext = new CheckpointCleanupContext(new CheckpointNativeIo(), new CheckpointNativeGitRunner(), clock),
            RowStamp = () => "20261127-000000-abcd", RunStamp = () => "20261127-000000-abcd",
            ReportHost = new ReportHost { Os = "fixture", Cores = 6, MemAvailableMb = 65536, LoadAvg = "fixture" },
            HostSnapshotText = "fixture", GitSnapshotText = "fixture",
            BuildObservationPublication = stalledPublication ?? new(),
            BuildObservationJournal = path =>
            {
                var journal = TimingJournal.Open(path);
                journal.Fault = fault.GetValueOrDefault();
                configureJournal?.Invoke(journal);
                return journal;
            },
        };
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request),
                ("Antiphon.Tests.ExampleSurfaceTests.case", fails ? "Failed" : "Passed"));
            return Task.FromResult(new DriverResult(fails ? 1 : 0, "", ""));
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        string run;
        int exit;
        if (grouped)
        {
            var manifest = new CheckpointManifest
            {
                ResultsRoot = results,
                Builds = [new BuildSpec { Id = ObservationBuild, Project = "sample", OutputPath = ObservationBuild + "/" }],
                Checkpoints = [new CheckpointSpec
                {
                    Id = "CP-1", After = ["S0"], Build = ObservationBuild, Filter = ObservationFilter, MinExecuted = 1,
                }],
            };
            run = CheckpointApp.CreateRun(manifest, new RunRequest
            {
                Commit = ObservationSource.Commit!, Branch = "fixture", KeepOutputs = true, ObserveBuilds = fault.HasValue,
            }, repo, runtime);
            exit = await CheckpointApp.ExecuteAsync(run, deadline.Token, runtime);
            await runtime.BuildObservationPublication.FlushOnExitAsync();
        }
        else
        {
            string[] args = ["row", "--name", "CP-1", "--project", project, "--output-path", ObservationBuild + "/",
                "--filter", ObservationFilter, "--results-root", results, "--min-executed", "1"];
            if (fault.HasValue)
                args = [.. args, "--observe-builds"];
            exit = await Antiphon.Checkpoints.Program.MainAsync(args, runtimeFactory: _ => runtime);
            run = Path.Combine(results, "CP-1-20261127-000000-abcd");
        }
        var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var json = File.ReadAllBytes(Path.Combine(run, "report.json"));
        var markdown = File.ReadAllBytes(Path.Combine(run, "report.md"));
        var report = ReceiptJson.Parse<ReportModel>(System.Text.Encoding.UTF8.GetString(json), ReportWriter.Json);
        var row = report.Rows.Single();
        var trx = refuse ? new TrxParseResult() : TrxReport.Parse(row.Trx!);
        if (stalledPublication is not null)
            return new(exit, driver.Calls, slots.Events, json, markdown, row.Line!, trx, new([], null), milliseconds);
        var read = await BuildObservationReplay.ReadAsync(run, deadline.Token);
        if (grouped)
            RunOwnershipStore.Read(run, runtime.CleanupContext).ShouldNotBeNull().Launched.ShouldBeNull();
        // The in-process arm has joined and its bytes are in memory. Dispose its artifacts
        // through an owned scratch root so the next arm can use the same logical paths.
        var archive = OwnedTempRoot.CreateOwned();
        try
        {
            Directory.Move(run, Path.Combine(archive, "results"));
            if (!refuse)
                Directory.Move(Path.Combine(repo, "sample", ObservationBuild), Path.Combine(archive, "output"));
        }
        finally
        {
            OwnedTempRoot.Release(archive);
        }
        return new(exit, driver.Calls, slots.Events, json, markdown, row.Line!, trx, read, milliseconds);
    }

    private sealed record ObservationArm(int Exit, IReadOnlyList<DriverRequest> Commands, IReadOnlyList<string> Slots,
        byte[] Json, byte[] Markdown, string Line, TrxParseResult Trx, BuildObservationRead Read, double ExecutionMilliseconds);

    private sealed class ObservationSlotTrace(bool refuse) : IBuildSlotClient
    {
        internal List<string> Events { get; } = [];
        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(new SlotSession("enabled", 6));

        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            if (refuse)
                return Task.FromResult(new SlotLease { State = "refused", ExitCode = ExitCodes.Invalid });
            Events.Add("grant " + label);
            return Task.FromResult(new SlotLease
            {
                State = "granted", MaxCpuCount = session.MaxCpuCount, LeaseId = ObservationLease.ToString(),
                ReleaseAsync = _ =>
                {
                    Events.Add("release " + label);
                    return Task.CompletedTask;
                },
            });
        }
    }

    [Test]
    public async Task C2103_broker_telemetry_gap_is_explicit()
    {
        var (binding, history, request, producer, custody) = await ObserveBrokerAndCustodyAsync();
        var held = BuildObservationReplay.MeasureProducerHold(binding, history);
        held.ShouldBe(new BrokerCoverage("known", null, 5));
        BuildObservationReplay.Classify(producer, request, held, custody).Status.ShouldBe("eligible-ready");

        var unknown = BuildObservationReplay.MeasureProducerHold(binding, null);
        unknown.ShouldBe(new BrokerCoverage("unknown", "broker-telemetry-unreachable", null), "broker-405");
        BuildObservationReplay.Classify(producer, request, unknown, custody).Status.ShouldBe("unknown");
        BuildObservationReplay.MeasureProducerHold(binding, []).Reason.ShouldBe("broker-pair-missing");
    }

    [Test]
    public async Task C2103_custody_telemetry_gap_is_explicit()
    {
        var (binding, history, request, producer, custody) = await ObserveBrokerAndCustodyAsync();
        var held = BuildObservationReplay.MeasureProducerHold(binding, history);
        held.HeldSeconds.ShouldBe(5);
        BuildObservationReplay.Classify(producer, request, held, custody).Status.ShouldBe("eligible-ready");
        BuildObservationReplay.Classify(producer, request, held, null)
            .ShouldBe(new BuildAvailabilityResult("unknown", "custody-record-unreachable"), "custody-access-denied");
        BuildObservationReplay.Classify(producer, request, held, custody with { RemovedAt = request.RequestedAt })
            .ShouldBe(new BuildAvailabilityResult("ineligible", "output-absent"));
    }

    private async Task<(ProducerLeaseBinding Binding, ProducerLeaseTransition[] History, BuildAvailabilityRequest Request,
        ObservedBuild Producer, BuildCustodyEvidence Custody)> ObserveBrokerAndCustodyAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = new ControlledTimeProvider(ObservationEpoch);
        var transitions = new LeaseLog();
        await using var fixture = await BuildSlotBrokerFixture.StartAsync(clock: clock, log: transitions);
        var label = "build:" + ObservationBuild;
        using var acquired = await fixture.Http.PostAsJsonAsync("build-slots",
            new BuildSlotRequest(20, clock.GetUtcNow().UtcDateTime.AddSeconds(-1), label, TaskId: "producer"),
            deadline.Token);
        acquired.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lease = (await acquired.Content.ReadFromJsonAsync<BuildSlotGrant>(deadline.Token))!.LeaseId!.Value;
        var grant = fixture.Broker.List().Leases.Single();
        grant.LeaseId.ShouldBe(lease);
        grant.TaskId.ShouldBe("producer");
        clock.Advance(TimeSpan.FromSeconds(5));
        using var released = await fixture.Http.DeleteAsync("build-slots/" + lease, deadline.Token);
        released.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var terminal = transitions.Entries.Single(entry => entry.Id == lease && entry.Held is not null);
        terminal.Held.ShouldBe(5);
        fixture.Broker.List().Occupied.ShouldBe(0);
        using var denied = await fixture.Http.GetAsync("build-slots/" + lease, deadline.Token);
        denied.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);

        var binding = new ProducerLeaseBinding("fixture-broker", fixture.Broker.Metrics().BrokerStartedAtUtc.ToString("O"),
            lease, grant.TaskId!, "fixture-run", ObservationBuild);
        var grantedAt = new DateTimeOffset(grant.GrantedAtUtc);
        var releasedAt = grantedAt.AddSeconds(terminal.Held!.Value);
        var input = new BuildInputDescriptor("antiphon", ObservationSource.Commit!, ObservationSource.Fingerprint!,
            "sample", "requested", "graph", "toolchain", "fixture-runner", "linux", "x64");
        var output = Path.Combine(TempDir(), "fixture-runtime");
        File.WriteAllText(output, "fixture-owned runtime");
        File.Exists(output).ShouldBeTrue();
        var at = releasedAt.AddSeconds(1);
        return (binding, [new(binding, "grant", grantedAt), new(binding, "release", releasedAt)],
            new("consumer", at, input), new("producer", ObservationBuild, input, grantedAt, releasedAt, "ok", "generation", false),
            new(input.Runner, ObservationBuild, "generation", true, releasedAt, at.AddSeconds(2), null,
                releasedAt, at.AddSeconds(2), null));
    }

    private sealed class LeaseLog : ILoggerProvider, ILogger
    {
        public List<(Guid Id, double? Held)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> fields)
                return;
            if (fields.FirstOrDefault(field => field.Key == "LeaseId").Value is Guid id)
                Entries.Add((id, fields.FirstOrDefault(field => field.Key == "HeldSeconds").Value as double?));
        }
    }

    [Test]
    public async Task C2103_observation_reaches_the_reader_after_handoff()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = new ControlledTimeProvider(ObservationEpoch);
        TimingEvent? replaySample = null;
        foreach (var fault in new[] { TimingFault.AfterSpoolBeforeAppend, TimingFault.AfterFlushBeforeAck,
                     TimingFault.None, TimingFault.BeforeSpool })
        {
            var root = Directory.CreateDirectory(Path.Combine(TempDir(), "run")).FullName;
            var writer = new CheckpointBuildObservation(root, "run", null, null, clock);
            var attempted = writer.Begin("grouped-producer", "build-1", "sample", ObservationSource);
            writer.Complete(attempted, "ok", "verified");
            var diagnostics = new List<string>();
            await writer.PublishAsync(path =>
            {
                var journal = TimingJournal.Open(path);
                journal.Fault = fault;
                return journal;
            }, diagnostics.Add);
            var journalRoot = CheckpointBuildObservation.JournalRoot(root);
            var spooled = Directory.Exists(Path.Combine(journalRoot, "spool"))
                ? Directory.GetFiles(Path.Combine(journalRoot, "spool"), "*.json")
                : [];
            var before = TimingJournal.Read(journalRoot);
            if (fault == TimingFault.BeforeSpool)
            {
                diagnostics.Single().ShouldContain("coverage=unknown");
                (await BuildObservationReplay.ReadAsync(root, deadline.Token)).CoverageReason.ShouldBe("observation-batch-incomplete");
                continue;
            }
            if (fault == TimingFault.AfterSpoolBeforeAppend)
                before.Events.Count.ShouldBe(0, "after-spool-before-append");
            var eventId = spooled.Length == 1 ? Path.GetFileNameWithoutExtension(spooled[0]) : before.Events.Single().EventId;
            var reopened = await BuildObservationReplay.ReadAsync(root, deadline.Token);
            var recorded = reopened.Events.Single();
            recorded.EventId.ShouldBe(eventId, "after-spool-before-append");
            recorded.BuildPhase!.BuildId.ShouldBe("build-1");
            recorded.BuildPhase.RequestedAt.ShouldBe(ObservationEpoch);
            reopened.CoverageReason.ShouldBe(fault == TimingFault.None ? null : "observation-batch-incomplete");
            (await BuildObservationReplay.ReadAsync(root, deadline.Token)).Events.Count.ShouldBe(1);
            if (fault == TimingFault.None)
                replaySample = recorded;
            if (fault == TimingFault.AfterFlushBeforeAck)
            {
                var digest = recorded.PayloadIdentity;
                recorded.BuildPhase = recorded.BuildPhase with { RequestedPropertiesDigest = "different" };
                var conflict = await Should.ThrowAsync<TimingIntegrityException>(
                    () => TimingJournal.Open(journalRoot).AppendAsync(recorded, deadline.Token));
                conflict.Message.ShouldBe("event-conflict", "conflicting-replay");
                TimingJournal.Read(journalRoot).Events.Single().PayloadIdentity.ShouldBe(digest);
            }
        }

        var busyRoot = Directory.CreateDirectory(Path.Combine(TempDir(), "run")).FullName;
        var busyPath = CheckpointBuildObservation.JournalRoot(busyRoot);
        var heldShard = TimingJournal.Open(busyPath);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        heldShard.AfterSequenceAssigned = async () =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(deadline.Token);
        };
        var staging = TimingJournal.Open(busyPath);
        staging.Fault = TimingFault.AfterSpoolBeforeAppend;
        var staged = replaySample.ShouldNotBeNull();
        await Should.ThrowAsync<TimingPersistenceException>(() => staging.AppendAsync(staged, deadline.Token));
        File.Exists(Path.Combine(busyPath, "spool", staged.EventId + ".json")).ShouldBeTrue();

        var held = heldShard.AppendAsync(new TimingEvent
        {
            RunId = "run", EventId = "busy-fixture", EventKind = "fixture", StartedAt = ObservationEpoch,
        }, deadline.Token);
        Task<BuildObservationRead>? reading = null;
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            reading = BuildObservationReplay.ReadAsync(busyRoot, deadline.Token);
            reading.IsCompleted.ShouldBeFalse("busy-shard-awaits-release");
        }
        finally
        {
            release.TrySetResult();
            await held;
            if (reading is not null)
                await reading;
        }
        (await reading!).Events.Single().EventId.ShouldBe(replaySample!.EventId, "busy-shard-readback");

        var quotaRoot = Directory.CreateDirectory(Path.Combine(TempDir(), "run")).FullName;
        var capped = new CheckpointBuildObservation(quotaRoot, "quota", null, null, clock, eventLimit: 3);
        foreach (var build in new[] { "first", "second", "overflow" })
            capped.Complete(capped.Begin("grouped-producer", build, "sample", ObservationSource), "ok", "verified");
        await capped.PublishAsync(null, TextWriter.Null.WriteLine);
        var retained = await BuildObservationReplay.ReadAsync(quotaRoot, deadline.Token);
        retained.Events.Count.ShouldBe(2, "full-observation-shard");
        retained.Events.Select(item => item.BuildPhase!.BuildId).ShouldBe(["first", "second"]);
        retained.CoverageReason.ShouldBe("observation-truncated");

        var byteQuotaRoot = Directory.CreateDirectory(Path.Combine(TempDir(), "run")).FullName;
        var byteCapped = new CheckpointBuildObservation(byteQuotaRoot, "byte-quota", null, null, clock, byteLimit: 6000);
        foreach (var build in Enumerable.Range(0, 10))
            byteCapped.Complete(byteCapped.Begin("grouped-producer", $"build-{build}", new string('p', 240),
                ObservationSource), "ok", "verified");
        await byteCapped.PublishAsync(null, TextWriter.Null.WriteLine);
        var byteRetained = await BuildObservationReplay.ReadAsync(byteQuotaRoot, deadline.Token);
        byteRetained.Events.Count.ShouldBeGreaterThan(0);
        byteRetained.Events.Count.ShouldBeLessThan(10);
        byteRetained.CoverageReason.ShouldBe("observation-truncated");
    }
}
