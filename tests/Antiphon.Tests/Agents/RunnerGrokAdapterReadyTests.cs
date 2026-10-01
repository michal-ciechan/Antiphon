using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class RunnerGrokAdapterReadyTests
{
    private static readonly string Ready = GrokStartupFixture.ReadyScreen();
    private const string Trust = "Do you trust the contents of this directory?\nYes, proceed  y\nNo, quit  n";
    private const string SignIn = "Approve in your browser to finish signing in.\nWaiting for approval...";

    [Test]
    public async Task Spinner_sequence_advance_does_not_prevent_positive_ready()
    {
        using var document = GrokStartupFixture.Read();
        var idle = GrokStartupFixture.Capture(document, "idle-");
        var animated = idle.GetProperty("checkpoints").EnumerateArray()
            .Where(x => x.GetProperty("expectedReason").GetString() == "Ready")
            .Select(x => x.GetProperty("screen").GetString()!).ToArray();
        var client = new ScriptedClient(animated, loop: true);
        await using var adapter = NewAdapter(client, max: 5000, settle: 60);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        var ready = await adapter.WaitForReadyAsync(CancellationToken.None);
        ready.ShouldBeTrue("ready must survive every advancing spinner frame");
        client.SnapshotReads.ShouldBeGreaterThanOrEqualTo(2);
        client.BufferReads.ShouldBe(0);
        client.Writes.ShouldBeEmpty();
        // With zero settlement there are exactly two completed observations. A
        // second DTO fetch inside either decision violates snapshot coherence.
        var coherent = new ScriptedClient([Ready]);
        await using var coherentAdapter = NewAdapter(coherent, max: 5000, settle: 0);
        await coherentAdapter.StartAsync(Spec(), CancellationToken.None);
        (await coherentAdapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        coherent.SnapshotReads.ShouldBe(2, "snapshotReads must equal completedDecisions");
        coherent.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Animated_sign_in_blocks_without_input_and_sets_launch_block()
    {
        var client = new ScriptedClient([SignIn + "\n" + Trust]);
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        adapter.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
        adapter.LaunchBlock.Reason.ShouldContain("grok login");
        client.Writes.ShouldBeEmpty();
        client.SnapshotReads.ShouldBe(1);
    }

    [Test]
    public async Task Current_trust_is_answered_once_before_positive_ready()
    {
        var client = new ScriptedClient([Trust, Trust, Ready, Ready], raw: Trust);
        await using var adapter = NewAdapter(client, max: 10000, settle: 50, trust: 5000);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.Writes.ShouldBe(["y"]);
        var stale = new ScriptedClient([Ready], raw: Trust);
        await using var staleAdapter = NewAdapter(stale, max: 5000, settle: 50);
        await staleAdapter.StartAsync(Spec(), CancellationToken.None);
        (await staleAdapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        stale.Writes.ShouldBeEmpty();
        var questionOnly = new ScriptedClient(["Do you trust the contents of this directory?"]);
        await using var questionAdapter = NewAdapter(questionOnly, max: 5000);
        await questionAdapter.StartAsync(Spec(), CancellationToken.None);
        (await questionAdapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        questionOnly.Writes.ShouldBeEmpty();
    }

    [Test]
    public async Task Post_trust_blank_or_sign_in_is_not_ready()
    {
        var blank = new ScriptedClient([Trust, ""]);
        await using var adapter = NewAdapter(blank, max: 5000, trust: 0);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        blank.Writes.ShouldBe(["y"]);
        var signin = new ScriptedClient([Trust, SignIn]);
        await using var second = NewAdapter(signin, max: 5000);
        await second.StartAsync(Spec(), CancellationToken.None);
        (await second.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        second.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
        signin.Writes.ShouldBe(["y"]);
        var trustClock = new PollGateClock();
        var trustStarted = trustClock.GetTimestamp();
        var trustReads = 0;
        var lateTrustRead = new TaskCompletionSource<GrokStartupSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTrustRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GrokStartupReason? trustOutcome = null;
        var trustReady = GrokReadyWait.WaitAsync(_ =>
            {
                if (++trustReads == 1)
                    return Task.FromResult<GrokStartupSnapshot?>(new(Trust, Trust, 1, DateTime.UtcNow));
                secondTrustRead.TrySetResult();
                return lateTrustRead.Task;
            },
            new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
                TrustSettle = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(1),
                TimeProvider = trustClock,
                OnFailure = (outcome, _, _, _, _, _, _) => trustOutcome = outcome },
            (_, _) => Task.CompletedTask);
        await trustClock.PollInstalled(1).WaitAsync(TimeSpan.FromSeconds(5));
        trustClock.Advance(TimeSpan.FromMilliseconds(1));
        await secondTrustRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        trustClock.Advance(TimeSpan.FromMilliseconds(499));
        if (await Task.WhenAny(trustReady, Task.Delay(5000)) != trustReady)
        {
            trustClock.Advance(TimeSpan.FromSeconds(2));
            lateTrustRead.TrySetResult(new GrokStartupSnapshot(Ready, "", 2, DateTime.UtcNow));
            trustClock.Advance(TimeSpan.FromSeconds(5));
        }
        var trustCompletion = await trustReady.WaitAsync(TimeSpan.FromSeconds(5));
        var trustCompletionElapsed = trustClock.GetElapsedTime(trustStarted);
        lateTrustRead.TrySetResult(new GrokStartupSnapshot(Ready, "", 2, DateTime.UtcNow));
        trustCompletionElapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(500),
            "trustCompletionElapsed");
        trustOutcome.ShouldBe(GrokStartupReason.Trust, "trustExpiryOutcome");
        trustCompletion.ShouldBeFalse();
    }

    [Test]
    public async Task One_deadline_covers_reads_trust_and_minimum_age()
    {
        var frame = new GrokStartupSnapshot(Ready, "historical", 1, DateTime.UtcNow);
        var clock = new JumpClock();
        var lateReads = 0;
        var late = await GrokReadyWait.WaitAsync(async _ =>
        {
            if (++lateReads == 2)
            {
                await Task.Yield();
                clock.Advance(TimeSpan.FromSeconds(6));
            }
            return frame;
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
            Settle = TimeSpan.Zero, PollInterval = TimeSpan.FromMilliseconds(5), TimeProvider = clock });
        lateReads.ShouldBe(2);
        late.ShouldBeFalse("lateReadReady");
        var hung = new TaskCompletionSource<GrokStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var boundClock = new FakeTimeProvider();
        var boundStarted = boundClock.GetTimestamp();
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bounded = GrokReadyWait.WaitAsync(_ => { readEntered.TrySetResult(); return hung.Task; },
            new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5), TimeProvider = boundClock });
        await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        boundClock.Advance(TimeSpan.FromSeconds(5));
        if (await Task.WhenAny(bounded, Task.Delay(5000)) != bounded)
        {
            boundClock.Advance(TimeSpan.FromSeconds(1));
            hung.TrySetResult(frame);
        }
        var boundedResult = await bounded.WaitAsync(TimeSpan.FromSeconds(5));
        var completionElapsed = boundClock.GetElapsedTime(boundStarted);
        hung.TrySetResult(frame);
        boundedResult.ShouldBeFalse();
        completionElapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(5), "completionElapsed");
        (await GrokReadyWait.WaitAsync(_ => Task.FromResult<GrokStartupSnapshot?>(frame),
            new GrokReadyWaitOptions { MaxWait = TimeSpan.Zero })).ShouldBeFalse();
        var minimumClock = new PollGateClock();
        var minimumWait = GrokReadyWait.WaitAsync(
            _ => Task.FromResult<GrokStartupSnapshot?>(frame),
            new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
                MinimumAgeRemaining = TimeSpan.FromSeconds(10), PollInterval = TimeSpan.FromMilliseconds(1),
                Settle = TimeSpan.Zero, TimeProvider = minimumClock });
        await minimumClock.PollInstalled(1).WaitAsync(TimeSpan.FromSeconds(5));
        minimumClock.Advance(TimeSpan.FromMilliseconds(1));
        var minimumNext = await Task.WhenAny(minimumWait, minimumClock.PollInstalled(2))
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (minimumNext != minimumWait) minimumClock.Advance(TimeSpan.FromSeconds(5));
        var readyBeforeMinimumAge = await minimumWait.WaitAsync(TimeSpan.FromSeconds(5));
        readyBeforeMinimumAge.ShouldBeFalse("readyBeforeMinimumAge");
        var trustClock = new PollGateClock();
        var trustBudgetStarted = trustClock.GetTimestamp();
        var trustReads = 0;
        var trustLateRead = new TaskCompletionSource<GrokStartupSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTrustRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trustBudgetWait = GrokReadyWait.WaitAsync(_ =>
        {
            if (++trustReads == 1)
                return Task.FromResult<GrokStartupSnapshot?>(new(Trust, "", 1, DateTime.UtcNow));
            secondTrustRead.TrySetResult();
            return trustLateRead.Task;
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
            Settle = TimeSpan.Zero, TrustSettle = TimeSpan.Zero,
            PollInterval = TimeSpan.FromMilliseconds(1), TimeProvider = trustClock },
            (_, _) => { trustClock.Advance(TimeSpan.FromSeconds(4)); return Task.CompletedTask; });
        await trustClock.PollInstalled(1).WaitAsync(TimeSpan.FromSeconds(5));
        trustClock.Advance(TimeSpan.FromMilliseconds(1));
        await secondTrustRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        trustClock.Advance(TimeSpan.FromMilliseconds(999));
        if (await Task.WhenAny(trustBudgetWait, Task.Delay(5000)) != trustBudgetWait)
        {
            trustClock.Advance(TimeSpan.FromSeconds(1));
            trustLateRead.TrySetResult(frame);
            trustClock.Advance(TimeSpan.FromSeconds(5));
        }
        var trustBudgetReady = await trustBudgetWait.WaitAsync(TimeSpan.FromSeconds(5));
        var trustBudgetElapsed = trustClock.GetElapsedTime(trustBudgetStarted);
        trustLateRead.TrySetResult(frame);
        trustBudgetElapsed.ShouldBeLessThan(
            TimeSpan.FromMilliseconds(5500), "trustCompletionElapsed inside originalMax");
        trustBudgetReady.ShouldBeFalse("trustCompletionElapsed must stay inside originalMax");
    }

    [Test]
    public async Task Floor_modal_invalidates_stale_positive_at_minimum_age()
    {
        var floorClock = new PollGateClock();
        var floorReads = 0;
        var floorWait = GrokReadyWait.WaitAsync(_ =>
        {
            floorReads++;
            if (floorReads == 3) floorClock.Advance(TimeSpan.FromMilliseconds(80));
            return Task.FromResult<GrokStartupSnapshot?>(new(
                floorReads < 3 ? Ready : SignIn, "", floorReads, DateTime.UtcNow));
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
            MinimumAgeRemaining = TimeSpan.FromMilliseconds(80), Settle = TimeSpan.Zero,
            PollInterval = TimeSpan.FromMilliseconds(1), TimeProvider = floorClock });
        // Advance only after each poll timer exists. If readiness instead
        // sleeps to the floor after the second positive observation, release
        // that timer so the stale-positive result reaches the named assertion.
        await floorClock.PollInstalled(1).WaitAsync(TimeSpan.FromSeconds(5));
        floorClock.Advance(TimeSpan.FromMilliseconds(1));
        var secondPoll = floorClock.PollInstalled(2);
        var next = await Task.WhenAny(floorWait, secondPoll, floorClock.FloorSleepInstalled)
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (next == floorWait)
        {
            (await floorWait).ShouldBeFalse("readyWithFloorModal");
            return;
        }
        floorClock.Advance(next == secondPoll
            ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(80));
        var floor = await floorWait.WaitAsync(TimeSpan.FromSeconds(5));
        floor.ShouldBeFalse("readyWithFloorModal");
        floorReads.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task Utc_jump_does_not_advance_monotonic_settle()
    {
        var frame = new GrokStartupSnapshot(Ready, "", 1, DateTime.UtcNow);
        var utcClock = new UtcJumpClock();
        var utcReads = 0;
        var wait = GrokReadyWait.WaitAsync(_ =>
        {
            utcReads++;
            return Task.FromResult<GrokStartupSnapshot?>(frame);
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
            Settle = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(5),
            TimeProvider = utcClock });
        await utcClock.FirstPollInstalled.WaitAsync(TimeSpan.FromSeconds(5));
        utcClock.AdvanceUtc(TimeSpan.FromSeconds(3));
        for (var tick = 0; tick < 100 && utcReads < 3 && !wait.IsCompleted; tick++)
        {
            utcClock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(1);
        }
        utcReads.ShouldBeGreaterThanOrEqualTo(2);
        // Let a completed read settle its continuation without moving the fake
        // monotonic clock. A UTC-based mutant must finish at this point.
        await Task.WhenAny(wait, Task.Delay(5000));
        wait.IsCompleted.ShouldBeFalse("readyAfterUtcJumpWithoutElapsed");
        for (var tick = 0; tick < 60 && !wait.IsCompleted; tick++)
        {
            utcClock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(1);
        }
        (await wait.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
    }

    [Test]
    public async Task Exit_and_cancellation_stop_without_input()
    {
        var reads = 0;
        (await GrokReadyWait.WaitAsync(_ => { reads++; return Task.FromResult<GrokStartupSnapshot?>(null); },
            new GrokReadyWaitOptions(), isExited: () => true)).ShouldBeFalse();
        reads.ShouldBe(0);
        var exitReads = 0;
        var exited = false;
        var exitedReady = await GrokReadyWait.WaitAsync(_ =>
        {
            exitReads++;
            if (exitReads == 2) exited = true;
            return Task.FromResult<GrokStartupSnapshot?>(new(Ready, "", exitReads, DateTime.UtcNow));
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5), Settle = TimeSpan.Zero,
            PollInterval = TimeSpan.FromMilliseconds(5) }, isExited: () => exited);
        exitReads.ShouldBe(2);
        exitedReady.ShouldBeFalse("exitedReady");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => GrokReadyWait.WaitAsync(
            _ => Task.FromResult<GrokStartupSnapshot?>(null), new GrokReadyWaitOptions(), ct: cancel.Token));
    }

    [Test]
    public async Task Cancellation_during_held_read_escapes_without_more_io()
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var writes = 0;
        var wait = GrokReadyWait.WaitAsync(async token =>
        {
            reads++;
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new GrokStartupSnapshot(Ready, "", reads, DateTime.UtcNow);
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5) },
            (_, _) => { writes++; return Task.CompletedTask; }, ct: cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5)));
        reads.ShouldBe(1);
        writes.ShouldBe(0);
    }

    [Test]
    public async Task Cancellation_during_pending_poll_escapes_without_more_io()
    {
        using var cancel = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var reads = 0;
        var writes = 0;
        var wait = GrokReadyWait.WaitAsync(_ =>
        {
            reads++;
            return Task.FromResult<GrokStartupSnapshot?>(new(Ready, "", reads, DateTime.UtcNow));
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
            Settle = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(50),
            TimeProvider = clock },
            (_, _) => { writes++; return Task.CompletedTask; }, ct: cancel.Token);
        reads.ShouldBe(1);
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5)));
        reads.ShouldBe(1);
        writes.ShouldBe(0);
    }

    [Test]
    public async Task Cancellation_during_held_trust_write_escapes_without_more_io()
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var writes = 0;
        var wait = GrokReadyWait.WaitAsync(_ =>
        {
            reads++;
            return Task.FromResult<GrokStartupSnapshot?>(new(Trust, Trust, reads, DateTime.UtcNow));
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5) },
            async (_, token) =>
            {
                writes++;
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, ct: cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5)));
        reads.ShouldBe(1);
        writes.ShouldBe(1);
    }

    [Test]
    public async Task Timeout_captures_last_frame_and_io_failure_preserves_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "c778-v12-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string sentinel = "C778_PRIVATE_SCREEN_SENTINEL";
            var logger = new TestLogger();
            var deadlineClock = new PollGateClock();
            var client = new ScriptedClient(["Starting session… " + sentinel,
                "Second startup frame " + sentinel],
                onSnapshot: count => { if (count == 2) deadlineClock.Advance(TimeSpan.FromSeconds(6)); });
            await using var adapter = NewAdapter(client, max: 5000, captureDirectory: root,
                logger: logger, time: deadlineClock);
            await adapter.StartAsync(Spec(), CancellationToken.None);
            var deadlineWait = adapter.WaitForReadyAsync(CancellationToken.None);
            await deadlineClock.PollInstalled(1).WaitAsync(TimeSpan.FromSeconds(5));
            deadlineClock.Advance(TimeSpan.FromMilliseconds(50));
            (await deadlineWait.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
            var file = Directory.GetFiles(root, "grok-startup-*.txt").Single();
            var content = File.ReadAllText(file);
            content.ShouldContain("outcome: Deadline");
            content.ShouldContain($"frameSequence: {client.SnapshotReads}");
            client.BufferReads.ShouldBe(0);
            logger.Messages.ShouldHaveSingleItem();
            string.Join("\n", logger.Messages).ShouldNotContain(sentinel);
            var blockedPath = Path.Combine(root, "not-a-directory-" + sentinel);
            File.WriteAllText(blockedPath, "sentinel");
            var badClock = new PollGateClock();
            var bad = new ScriptedClient([sentinel],
                onSnapshot: _ => badClock.Advance(TimeSpan.FromSeconds(6)));
            await using var second = NewAdapter(bad, max: 5000, captureDirectory: blockedPath,
                logger: logger, time: badClock);
            await second.StartAsync(Spec(), CancellationToken.None);
            (await second.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            logger.Messages.Count.ShouldBe(2);
            string.Join("\n", logger.Messages).ShouldNotContain(sentinel);
            var lastFrameRoot = Path.Combine(root, "last-frame");
            var lastFrameClock = new JumpClock();
            var lastFrameClient = new ScriptedClient(["first observed frame", "extra forbidden read"],
                onSnapshot: count => { if (count == 1) lastFrameClock.Advance(TimeSpan.FromSeconds(6)); });
            await using var lastFrameAdapter = NewAdapter(lastFrameClient, max: 5000,
                captureDirectory: lastFrameRoot, time: lastFrameClock);
            await lastFrameAdapter.StartAsync(Spec(), CancellationToken.None);
            (await lastFrameAdapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            var lastCapture = File.ReadAllText(Directory.GetFiles(lastFrameRoot,
                "grok-startup-*.txt").Single());
            lastFrameClient.SnapshotReads.ShouldBe(1, "readsAfterFailureDecision");
            lastCapture.ShouldContain("frameSequence: 1");
            var readyFrame = Ready; // Load the fixture before entering the timed wait.
            var failureClock = new PollGateClock();
            var failureReads = 0;
            GrokStartupReason? failureOutcome = null;
            var snapshotFailureWait = GrokReadyWait.WaitAsync(_ =>
            {
                if (++failureReads == 2) throw new IOException("snapshot failure sentinel");
                return Task.FromResult<GrokStartupSnapshot?>(new(readyFrame, "", failureReads, DateTime.UtcNow));
            }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromSeconds(5),
                Settle = TimeSpan.Zero, PollInterval = TimeSpan.FromMilliseconds(1),
                TimeProvider = failureClock,
                OnFailure = (outcome, _, _, _, _, _, _) => failureOutcome = outcome });
            await failureClock.PollInstalled(1).WaitAsync(TimeSpan.FromSeconds(5));
            failureClock.Advance(TimeSpan.FromMilliseconds(1));
            var snapshotFailureReady = await snapshotFailureWait.WaitAsync(TimeSpan.FromSeconds(5));
            failureReads.ShouldBe(2, "snapshotFailureReads");
            failureOutcome.ShouldBe(GrokStartupReason.SnapshotFailure, "snapshotFailureOutcome");
            snapshotFailureReady.ShouldBeFalse("snapshotFailureReady");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static RunnerGrokAdapter NewAdapter(ScriptedClient client, int max = 5000,
        int settle = 50, int trust = 150, string? captureDirectory = null,
        ILogger? logger = null, TimeProvider? time = null) => new(client,
        Options.Create(new AgentRegistrySettings
        {
            GrokReadyMaxWaitMs = max, GrokReadyQuietPeriodMs = settle,
            GrokReadyMinTotalWaitMs = 0, GrokTrustPromptSettleMs = trust,
            GrokStartupCaptureDirectory = captureDirectory,
        }), logger: logger, time: time);

    internal static async Task<bool> AnimatedAdapterReadyAsync()
    {
        using var document = GrokStartupFixture.Read();
        var screens = GrokStartupFixture.Capture(document, "idle-").GetProperty("checkpoints")
            .EnumerateArray().Where(x => x.GetProperty("expectedReason").GetString() == "Ready")
            .Select(x => x.GetProperty("screen").GetString()!).ToArray();
        var client = new ScriptedClient(screens, loop: true);
        await using var adapter = NewAdapter(client, max: 5000, settle: 60);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        return await adapter.WaitForReadyAsync(CancellationToken.None);
    }

    private static AgentLaunchSpec Spec() => new("grok", AgentKind.Grok, "grok.exe", [],
        new Dictionary<string, string>(), "/tmp", 120, 30, SessionId: Guid.NewGuid());

    private sealed class ScriptedClient(IReadOnlyList<string> screens, string? raw = null,
        bool loop = false, Action<int>? onSnapshot = null) : ISessionRunnerClient
    {
        private int _index;
        public int SnapshotReads { get; private set; }
        public int BufferReads { get; private set; }
        public List<string> Writes { get; } = [];
        private string Screen => screens[loop ? _index % screens.Count : Math.Min(_index, screens.Count - 1)];
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(id, 12, DateTime.UtcNow.AddMinutes(-1), "Running", null,
                AgentExitReason.Unknown, 0));
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => StartAsync(id, Spec(), ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct)
        {
            BufferReads++;
            // The legacy quiet gate reads this DTO. Keep its sequence moving even
            // when the modern adapter correctly reads only coherent snapshots.
            return Task.FromResult(new SessionRunnerBufferDto(id, Screen, BufferReads));
        }
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct)
        {
            var screen = Screen;
            _index++;
            SnapshotReads++;
            onSnapshot?.Invoke(SnapshotReads);
            return Task.FromResult(new SessionRunnerSnapshotDto(id, raw ?? screen, screen,
                SnapshotReads, DateTime.UtcNow.AddMinutes(-1)));
        }
        public Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            Writes.Add(input);
            return Task.CompletedTask;
        }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(id, null, DateTime.UtcNow, "Exited", 0,
                AgentExitReason.KilledByRequest, SnapshotReads));
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; yield break; }
    }

    private sealed class JumpClock : TimeProvider
    {
        private long _timestampOffset;
        public override long GetTimestamp() => Stopwatch.GetTimestamp() + Interlocked.Read(ref _timestampOffset);
        public void Advance(TimeSpan amount) => Interlocked.Add(ref _timestampOffset,
            (long)(amount.TotalSeconds * Stopwatch.Frequency));
    }

    private sealed class UtcJumpClock : TimeProvider
    {
        private readonly FakeTimeProvider _timer = new();
        private readonly TaskCompletionSource _firstPoll =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TimeSpan _utcOffset;
        public Task FirstPollInstalled => _firstPoll.Task;
        public override DateTimeOffset GetUtcNow() => _timer.GetUtcNow() + _utcOffset;
        public override long GetTimestamp() => _timer.GetTimestamp();
        public override long TimestampFrequency => _timer.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = _timer.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromMilliseconds(5)) _firstPoll.TrySetResult();
            return timer;
        }
        public void Advance(TimeSpan amount) => _timer.Advance(amount);
        public void AdvanceUtc(TimeSpan amount) => _utcOffset += amount;
    }

    private sealed class PollGateClock : TimeProvider
    {
        private readonly FakeTimeProvider _timer = new();
        private readonly TaskCompletionSource[] _polls =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously)
        ];
        private readonly TaskCompletionSource _floorSleep =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pollCount;
        public Task FloorSleepInstalled => _floorSleep.Task;
        public override DateTimeOffset GetUtcNow() => _timer.GetUtcNow();
        public override long GetTimestamp() => _timer.GetTimestamp();
        public override long TimestampFrequency => _timer.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = _timer.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromMilliseconds(1)
                || dueTime == TimeSpan.FromMilliseconds(50))
            {
                var index = Interlocked.Increment(ref _pollCount) - 1;
                if (index < _polls.Length) _polls[index].TrySetResult();
            }
            else if (dueTime > TimeSpan.FromMilliseconds(1)
                && dueTime <= TimeSpan.FromMilliseconds(80))
                _floorSleep.TrySetResult();
            return timer;
        }
        public Task PollInstalled(int ordinal) => _polls[ordinal - 1].Task;
        public void Advance(TimeSpan amount) => _timer.Advance(amount);
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception?.Message);
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
