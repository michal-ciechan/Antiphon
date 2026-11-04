using System.Diagnostics;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.TestSupport;
using System.Reflection;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[NotInParallel("C467LandDelivery")]
public class LandDeliveryFixtureProbeTests
{
    [Test]
    public async Task Polling_attention_accepts_the_actual_collapsed_aged_receipt()
    {
        await using var f = new LandDeliveryFixture();
        var request = Guid.NewGuid();
        var now = DateTime.UtcNow;
        AttentionItemDto Item(string kind, int age) => new(AttentionKind.LandOutcomeUnconfirmed,
            AlertSeverity.Error, f.TaskId, null, null, null, "owned task",
            $"Land {kind} notification: Enqueued; queued; caller busy", "", now.AddSeconds(-age), null, [],
            LandRequestId: request, LandNotificationId: Guid.NewGuid());
        var newest = Item("Aged", 301);
        List<AttentionItemDto> source = [Item("Outcome", 1202), Item("Aged", 901), newest];
        // Exercise the shipped projection without booting a database, runner or Windows PTY.
        var collapse = typeof(AttentionService).GetMethod("CollapseLandOutcomeReceipts", BindingFlags.NonPublic | BindingFlags.Static)!;
        var items = (List<AttentionItemDto>)collapse.Invoke(null, [source])!;
        items.Count.ShouldBe(1);
        items[0].Headline.ShouldContain("Land Aged");
        var selected = await f.PollingAttentionAsync(new(now, false, items), request);
        selected.LandNotificationId.ShouldBe(newest.LandNotificationId);
        selected.Severity.ShouldBe(AlertSeverity.Error);
        selected.Headline.ShouldContain("busy");
    }

    [Test]
    public async Task Missing_polling_attention_reports_sequence_and_producer_without_payloads()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var request = Guid.NewGuid();
        var otherRequest = Guid.NewGuid();
        var otherNote = Guid.NewGuid();
        const string privatePayload = "fixture-private-attention-payload";
        AttentionItemDto other = new(AttentionKind.LandHeld, AlertSeverity.Warning, f.TaskId,
            null, null, null, privatePayload, privatePayload, privatePayload, DateTime.UtcNow, null, [],
            LandRequestId: otherRequest, LandNotificationId: otherNote);
        try
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => f.PollingAttentionAsync(
                new(DateTime.UtcNow, false, [other]), request,
                _ => Task.FromResult("operation=Published; notification=Enqueued; producer=held")));
            error.Message.ShouldContain("expected exactly one polling attention item; matches=0");
            error.Message.ShouldContain("taskId=" + f.TaskId);
            error.Message.ShouldContain("requestId=" + request);
            error.Message.ShouldContain(otherRequest.ToString());
            error.Message.ShouldContain(otherNote.ToString());
            error.Message.ShouldContain("LandHeld");
            error.Message.ShouldContain("operation=Published; notification=Enqueued; producer=held");
            error.Message.ShouldNotContain(privatePayload);
            error.InnerException.ShouldBeOfType<InvalidOperationException>();
            File.Exists(Path.Combine(f.Root, "polling-attention-" + request.ToString("N") + ".txt")).ShouldBeTrue();
            var matching = other with { Kind = AttentionKind.LandOutcomeUnconfirmed, LandRequestId = request };
            foreach (var (items, count) in new (AttentionItemDto[], int)[] {
                ([], 0), ([other with { Kind = AttentionKind.LandOutcomeUnconfirmed }], 0),
                ([matching, matching with { LandNotificationId = Guid.NewGuid() }], 2) })
            {
                var rejected = await Should.ThrowAsync<InvalidOperationException>(() => f.PollingAttentionAsync(
                    new(DateTime.UtcNow, false, items), request, _ => throw new IOException(privatePayload)));
                rejected.Message.ShouldContain("matches=" + count);
                rejected.Message.ShouldContain("producerState=unavailable:IOException");
                rejected.Message.ShouldNotContain(privatePayload);
            }
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }

    [Test]
    public async Task Stalled_receipt_names_identity_last_progress_and_safe_log_tail()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var request = Guid.NewGuid();
        var notification = Guid.NewGuid();
        var git = Path.Combine(f.Root, "protocol-git-last.json");
        var logDirectory = Path.Combine(f.Root, "server-logs");
        Directory.CreateDirectory(logDirectory);
        var log = Path.Combine(logDirectory, "antiphon-test.log");
        const string privatePayload = "fixture-private-payload-do-not-echo";
        await File.WriteAllTextAsync(git, JsonSerializer.Serialize(new {
            arguments = new[] { "push", privatePayload }, ExitCode = 0,
            at = DateTime.UtcNow, pid = Environment.ProcessId }));
        await File.WriteAllTextAsync(log,
            "2026-10-31 00:00:00.000 +00:00 [INF] Landing verifier BUILD SLOT waiting " + privatePayload + "\n");
        // Held beyond the quiet window; no delayed task or child survives the witness.
        var producer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var original = await Should.ThrowAsync<TimeoutException>(() => f.UntilProtocolAsync(
                () => Task.FromResult(producer.Task.IsCompleted), "complete native Land receipt", quietSeconds: 1));
            original.Message.ShouldContain("quiet 1s");
            var error = await f.ReceiptTimeoutAsync(original, LandNotificationKind.Outcome, request, notification,
                _ => Task.FromResult("operation=Prepared; notification=missing; producer=held"));
            error.InnerException.ShouldBeSameAs(original);
            error.Message.ShouldContain("kind=Outcome");
            error.Message.ShouldContain("requestId=" + request);
            error.Message.ShouldContain("notificationId=" + notification);
            error.Message.ShouldContain("operation=Prepared; notification=missing; producer=held");
            error.Message.ShouldContain("lastProtocolGit=" + git);
            error.Message.ShouldContain("gitCommand=push exit=0");
            error.Message.ShouldContain("logTail=" + log);
            error.Message.ShouldContain("build-slot-waiting");
            error.Message.ShouldNotContain(privatePayload);
            File.Exists(Path.Combine(f.Root, "receipt-timeout-" + notification.ToString("N") + ".txt")).ShouldBeTrue();
            var unavailable = await f.ReceiptTimeoutAsync(original, LandNotificationKind.Outcome, request, notification,
                _ => throw new IOException(privatePayload));
            unavailable.InnerException.ShouldBeSameAs(original);
            unavailable.Message.ShouldContain("producerState=unavailable:IOException");
            unavailable.Message.ShouldContain("lastProtocolGit=" + git);
            unavailable.Message.ShouldNotContain(privatePayload);
            producer.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            producer.TrySetResult(true);
            await producer.Task;
            Directory.Delete(f.Root, recursive: true);
        }
    }

    [Test]
    public async Task ProtocolProgress_counts_committed_protocol_git_records_only()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        try
        {
            f.ProtocolProgress().ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json.tmp"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"attention-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(1, "a partial .tmp record and a foreign evidence file are not progress");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(2);
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }

    /// <summary>
    /// rc 0b05506b e2e-all-split-2: after the land worktree's last <c>git status</c> the land runs its
    /// verifier (a build slot, then a real <c>dotnet build</c>), which spawns no protocol Git. A
    /// committed verifier record is land progress; a partial one is not.
    /// </summary>
    [Test]
    public async Task ProtocolProgress_counts_committed_protocol_verify_records()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        try
        {
            f.ProtocolProgress().ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-verify-{Guid.NewGuid():N}.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-verify-{Guid.NewGuid():N}.json.tmp"), "{}");
            f.ProtocolProgress().ShouldBe(1, "a committed land-verifier record is land progress; a partial one is not");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(2, "verifier and Git records both count");
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }

    /// <summary>
    /// The verifier evidence records start, a running heartbeat while the inner verification is in
    /// flight, no heartbeat past its budget (a stalled verifier still ends one quiet window later),
    /// and end with the verdict.
    /// </summary>
    [Test]
    public async Task Verifier_evidence_heartbeats_in_flight_stops_at_its_budget_and_records_the_end()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var inner = new HeldVerifier();
        var verifier = new LandVerifierEvidence(f.Root, inner,
            heartbeat: TimeSpan.FromMilliseconds(50), budget: TimeSpan.FromMilliseconds(400));
        var correlation = new LandingVerificationCorrelation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var run = verifier.VerifyAsync(Path.Combine(f.Root, "land"), null, correlation, CancellationToken.None);
        try
        {
            string[] Phase(string phase) => Directory.GetFiles(f.Root, "protocol-verify-*.json")
                .Where(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("phase").GetString() == phase)
                .ToArray();
            var wall = Stopwatch.StartNew();
            while (Phase("running").Length < 2 && wall.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
            Phase("start").ShouldHaveSingleItem("the verification start is recorded");
            Phase("running").Length.ShouldBeGreaterThanOrEqualTo(2, "an in-flight verification heartbeats");
            run.IsCompleted.ShouldBeFalse();
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            var settled = Phase("running").Length;
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            Phase("running").Length.ShouldBe(settled, "no heartbeat past the budget");
            Phase("end").ShouldBeEmpty();
            inner.Complete(new LandingVerification(true, "build OK"));
            (await run).Passed.ShouldBeTrue();
            var end = JsonDocument.Parse(File.ReadAllText(Phase("end").ShouldHaveSingleItem())).RootElement;
            end.GetProperty("passed").GetBoolean().ShouldBeTrue();
            end.GetProperty("operationId").GetGuid().ShouldBe(correlation.OperationId);
            end.GetProperty("requestId").GetGuid().ShouldBe(correlation.RequestId!.Value);
            f.ProtocolProgress().ShouldBe(Directory.GetFiles(f.Root, "protocol-verify-*.json").Length);
        }
        finally
        {
            inner.Complete(new LandingVerification(false, "witness ended"));
            await run;
            Directory.Delete(f.Root, recursive: true);
        }
    }

    [Test]
    public async Task Stalled_receipt_names_the_last_verifier_phase()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var verify = Path.Combine(f.Root, $"protocol-verify-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(verify, JsonSerializer.Serialize(new {
            phase = "running", operationId = Guid.NewGuid(), requestId = Guid.NewGuid(), elapsedSeconds = 61.5,
            passed = (bool?)null, at = DateTime.UtcNow, pid = Environment.ProcessId }));
        try
        {
            var original = await Should.ThrowAsync<TimeoutException>(() => f.UntilProtocolAsync(
                () => Task.FromResult(false), "complete native Land receipt", quietSeconds: 1));
            var error = await f.ReceiptTimeoutAsync(original, LandNotificationKind.Outcome, null, null,
                _ => Task.FromResult("operation=Prepared"));
            error.Message.ShouldContain("lastProtocolVerify=" + verify);
            error.Message.ShouldContain("phase=running elapsedSeconds=61.5");
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }

    private sealed class HeldVerifier : ILandingVerifier
    {
        private readonly TaskCompletionSource<LandingVerification> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(LandingVerification result) => _result.TrySetResult(result);
        public Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct) => _result.Task;
    }

    [Test]
    public async Task Busy_queued_outcome_follows_protocol_git_past_the_quiet_window()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var clock = new StepClock();
        // 70 s of virtual time: past the plain 60 s deadline, inside the 600 s cap.
        const int target = 700;
        var wait = f.WaitForBusyCallerQueuedOutcomeAsync(
            () => Task.FromResult(f.ProtocolProgress() >= target), clock: clock);
        try
        {
            var steps = await DriveAsync(wait, clock, step =>
                File.WriteAllText(Path.Combine(f.Root, $"protocol-git-{step:D4}.json"), "{}"));
            var detail = wait.IsFaulted ? wait.Exception!.GetBaseException().Message : "running after " + steps;
            wait.IsCompletedSuccessfully.ShouldBeTrue(detail);
            steps.ShouldBeGreaterThan(600, "the queued outcome arrives after the plain quiet window");
            steps.ShouldBeLessThan(6000, "renewal stays inside the absolute cap");
        }
        finally
        {
            await FinishAsync(wait, clock);
            Directory.Delete(f.Root, recursive: true);
        }
    }

    [Test]
    public async Task Silent_busy_queued_outcome_names_recipient_and_last_evidence()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "caller-busy.held"), "held");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "execute.release"), "release");
        var git = Path.Combine(f.Root, "protocol-git-last.json");
        var logDirectory = Path.Combine(f.Root, "server-logs");
        Directory.CreateDirectory(logDirectory);
        var log = Path.Combine(logDirectory, "antiphon-test.log");
        const string privatePayload = "fixture-private-payload-do-not-echo";
        await File.WriteAllTextAsync(git, JsonSerializer.Serialize(new {
            arguments = new[] { "rev-parse", privatePayload }, ExitCode = 0,
            at = DateTime.UtcNow, pid = Environment.ProcessId }));
        await File.WriteAllTextAsync(log,
            "2026-10-31 00:00:00.000 +00:00 [INF] Landing verifier joined " + privatePayload + "\n");
        var clock = new StepClock();
        var wait = f.WaitForBusyCallerQueuedOutcomeAsync(
            () => Task.FromResult(false), clock: clock,
            producerState: _ => Task.FromResult("notification=missing; queue=absent; producer=held"));
        try
        {
            var steps = await DriveAsync(wait, clock, _ => { });
            steps.ShouldBe(600, "a silent land still ends one quiet window later");
            var error = await Should.ThrowAsync<TimeoutException>(() => wait);
            error.Message.ShouldContain("busy caller has queued outcome");
            error.Message.ShouldContain("quiet 60s");
            error.Message.ShouldContain("progress 1");
            error.Message.ShouldContain("recipientBusyHeld=true");
            error.Message.ShouldContain("recipientBusyReleased=false");
            error.Message.ShouldContain("executionReleased=true");
            error.Message.ShouldContain("notification=missing; queue=absent; producer=held");
            error.Message.ShouldContain("lastProtocolGit=" + git);
            error.Message.ShouldContain("gitCommand=rev-parse");
            error.Message.ShouldContain("logTail=" + log);
            error.Message.ShouldContain("land-verifier-joined");
            error.Message.ShouldNotContain(privatePayload);
            error.InnerException.ShouldBeOfType<TimeoutException>();
            error.InnerException!.Message.ShouldContain("quiet 60s");
            File.Exists(Path.Combine(f.Root, "queued-outcome-" + f.TaskId.ToString("N") + ".txt")).ShouldBeTrue();
        }
        finally
        {
            await FinishAsync(wait, clock);
            Directory.Delete(f.Root, recursive: true);
        }
    }

    /// <summary>
    /// One poll per step. The file write happens before the clock advances, so the wait observes
    /// the new protocol-git record when the armed delay completes.
    /// </summary>
    private static async Task<int> DriveAsync(Task wait, StepClock clock, Action<int> beforeAdvance)
    {
        var wall = Stopwatch.StartNew();
        while (Volatile.Read(ref clock.Timers) == 0 && !wait.IsCompleted && wall.Elapsed < TimeSpan.FromSeconds(2))
            await Task.Yield();
        var steps = 0;
        while (!wait.IsCompleted && steps < 1000 && wall.Elapsed < TimeSpan.FromSeconds(15))
        {
            var armed = Volatile.Read(ref clock.Timers);
            steps++;
            beforeAdvance(steps);
            clock.Advance(TimeSpan.FromMilliseconds(ProgressAwareWait.PollMilliseconds));
            var spin = Stopwatch.StartNew();
            while (!wait.IsCompleted && Volatile.Read(ref clock.Timers) == armed && spin.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Yield();
            if (!wait.IsCompleted && Volatile.Read(ref clock.Timers) == armed) break;
        }
        return steps;
    }

    private static async Task FinishAsync(Task wait, StepClock clock)
    {
        var extra = 0;
        while (!wait.IsCompleted && extra < 700)
        {
            extra++;
            clock.Advance(TimeSpan.FromMilliseconds(ProgressAwareWait.PollMilliseconds));
            await Task.Yield();
        }
    }

    /// <summary>Fires the single armed delay when virtual time moves. No wall-clock sleep.</summary>
    private sealed class StepClock : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        private TimerCallback? _callback;
        private object? _state;
        public int Timers;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utc;
        public override long GetTimestamp() => _utc.UtcTicks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref Timers);
            _callback = callback;
            _state = state;
            return new NoopTimer();
        }

        public void Advance(TimeSpan step)
        {
            _utc += step;
            var callback = _callback;
            var state = _state;
            _callback = null;
            callback?.Invoke(state);
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
