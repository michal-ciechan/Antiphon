using System.Diagnostics;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.TestSupport;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Antiphon.Server.Infrastructure.Orchestration;
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
            inner.Correlation.ShouldBe(correlation, "the decorator forwards the land's correlation");
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

    /// <summary>
    /// CARD-1703 A-1: the fixture registers the evidence decorator over the real verifier and forwards
    /// the land's correlation (task, operation, request, artifacts, common directory, window) to it.
    /// Linux-runnable: only the C467 E2E boots the server, and it is Windows-only.
    /// </summary>
    [Test]
    public async Task Fixture_wraps_the_registered_verifier_and_forwards_its_correlation()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1703-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        // Program.cs registers the verifier by implementation type.
        services.AddSingleton<VerifierCalls>();
        services.AddSingleton<ILandingVerifier, RecordingVerifier>();
        new LandDeliveryOptions(root).ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        try
        {
            var verifier = provider.GetRequiredService<ILandingVerifier>();
            verifier.ShouldBeOfType<LandVerifierEvidence>("the land verifier is recorded as receipt-wait progress");
            var calls = provider.GetRequiredService<VerifierCalls>();
            var correlation = new LandingVerificationCorrelation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Path.Combine(root, "artifacts"), Path.Combine(root, "common"), Guid.NewGuid());
            (await verifier.VerifyAsync(Path.Combine(root, "land"), "filter", correlation, CancellationToken.None))
                .Passed.ShouldBeTrue();
            calls.Correlations.ShouldHaveSingleItem().ShouldBe(correlation, "the whole correlation reaches the real verifier");
            calls.Plain.ShouldBe(0);
            string[] Records() => Directory.GetFiles(root, "protocol-verify-*.json");
            var records = Records().Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement).ToArray();
            records.Select(r => r.GetProperty("phase").GetString()).Order().ShouldBe(["end", "start"]);
            foreach (var record in records)
            {
                record.GetProperty("operationId").GetGuid().ShouldBe(correlation.OperationId);
                record.GetProperty("requestId").GetGuid().ShouldBe(correlation.RequestId!.Value);
            }
            (await verifier.VerifyAsync(Path.Combine(root, "land"), null, CancellationToken.None)).Passed.ShouldBeTrue();
            calls.Plain.ShouldBe(1, "an uncorrelated verification stays uncorrelated");
            calls.Correlations.Count.ShouldBe(1);
            Records().Length.ShouldBe(4);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// rc a6410818 e2e-split-5: CARD-1759 filters the hosted land-notification page on
    /// NextAttemptAt against the scope's TimeProvider. The monitor stamps an Aged notice with the
    /// land clock, so a scan on the system clock never pages it and C467_V32(busy) reads
    /// "not yet enqueued". The scan's scopes must read the same land clock.
    /// </summary>
    [Test]
    public async Task Notification_scan_reads_the_land_clock()
    {
        var root = Path.Combine(Path.GetTempPath(), "c467-scan-clock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        // Program.cs registers these three this way.
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddHostedService<AgentTaskLandNotificationHostedService>();
        services.AddSingleton<VerifierCalls>();
        services.AddSingleton<ILandingVerifier, RecordingVerifier>();
        new LandDeliveryOptions(root).ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "land-clock-seconds.txt"), "301");
            var scan = provider.GetServices<IHostedService>().OfType<AgentTaskLandNotificationHostedService>().ShouldHaveSingleItem();
            var scopes = (IServiceScopeFactory)typeof(AgentTaskLandNotificationHostedService)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(field => field.FieldType == typeof(IServiceScopeFactory)).GetValue(scan)!;
            await using var scope = scopes.CreateAsyncScope();
            var ahead = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow() - DateTimeOffset.UtcNow;
            ahead.ShouldBeGreaterThan(TimeSpan.FromSeconds(300), "the due-page clock must be the clock that stamps NextAttemptAt");
            provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System, "only the scan reads the land clock");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// CARD-1703 A-2: on a coarse file clock the last records of one verification share a write
    /// time. The diagnostic names the last record written, whichever order the directory lists them.
    /// </summary>
    [Test]
    public async Task Stalled_receipt_names_the_last_verifier_record_when_write_times_tie()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        try
        {
            var verifier = new LandVerifierEvidence(f.Root, new HeldVerifier(new LandingVerification(true, "build OK")));
            var correlation = new LandingVerificationCorrelation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            (await verifier.VerifyAsync(Path.Combine(f.Root, "land"), null, correlation, CancellationToken.None)).Passed.ShouldBeTrue();
            var paths = Directory.GetFiles(f.Root, "protocol-verify-*.json");
            paths.Length.ShouldBe(2, "start and end; the inner verification completed before any heartbeat");
            var tie = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
            var contents = paths.Select(File.ReadAllBytes).ToArray();
            // Both assignments of the two records to the same two directory entries: one of them lists
            // the start record first.
            foreach (var swap in new[] { false, true })
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    File.WriteAllBytes(paths[i], contents[swap ? paths.Length - 1 - i : i]);
                    File.SetLastWriteTimeUtc(paths[i], tie);
                }
                var end = paths.Single(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement
                    .GetProperty("phase").GetString() == "end");
                var error = await f.ReceiptTimeoutAsync(new TimeoutException("quiet 60s"), LandNotificationKind.Outcome,
                    correlation.RequestId, null, _ => Task.FromResult("operation=Prepared"));
                error.Message.ShouldContain("lastProtocolVerify=" + end + " phase=end", customMessage: "swap=" + swap);
            }
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }

    private sealed class HeldVerifier(LandingVerification? completed = null) : ILandingVerifier
    {
        private readonly TaskCompletionSource<LandingVerification> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LandingVerificationCorrelation? Correlation { get; private set; }
        public void Complete(LandingVerification result) => _result.TrySetResult(result);
        public Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
            => completed is { } result ? Task.FromResult(result) : _result.Task;
        public Task<LandingVerification> VerifyAsync(string worktree, string? filter,
            LandingVerificationCorrelation correlation, CancellationToken ct)
        {
            Correlation = correlation;
            return VerifyAsync(worktree, filter, ct);
        }
    }

    private sealed class VerifierCalls
    {
        public List<LandingVerificationCorrelation> Correlations { get; } = [];
        public int Plain;
    }

    private sealed class RecordingVerifier(VerifierCalls calls) : ILandingVerifier
    {
        public Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        {
            calls.Plain++;
            return Task.FromResult(new LandingVerification(true, "build OK"));
        }

        public Task<LandingVerification> VerifyAsync(string worktree, string? filter,
            LandingVerificationCorrelation correlation, CancellationToken ct)
        {
            calls.Correlations.Add(correlation);
            return Task.FromResult(new LandingVerification(true, "build OK"));
        }
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
