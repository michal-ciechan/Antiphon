using Antiphon.Agents.Pty.Tests;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexStartupDeliveryTests
{
    private static readonly SessionStatus[] NonRunning =
    [
        SessionStatus.Created, SessionStatus.Starting, SessionStatus.Stopping,
        SessionStatus.Stopped, SessionStatus.Failed,
    ];

    [Test]
    public async Task Starting_session_is_not_published_running_before_positive_ready()
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = hold };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var generation = await StartedAtAsync(h.SessionId);
        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, generation), null, false, null, CancellationToken.None, acceptedGeneration: generation);
        await WaitUntilAsync(() => adapter.Started);
        await using var db = BridgeQueueHarness.CreateContext();
        var stored = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        stored.Status.ShouldBe(SessionStatus.Starting, "R-41");
        hold.TrySetResult(true);
        await launch;
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Enqueue_during_boot_keeps_the_brief_pending_without_attempt(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = status == SessionStatus.Running;
        if (status == SessionStatus.Running)
        {
            var dto = await h.Queue.EnqueueAsync(h.SessionId, "boot brief", MessageSendMode.WhenIdle, CancellationToken.None);
            dto.Messages.ShouldBeEmpty("idle Running session delivers immediately");
            adapter.SubmittedBodies.ShouldContain(b => b.Contains("boot brief"));
            return;
        }

        var queued = await h.Queue.EnqueueAsync(h.SessionId, "boot brief", MessageSendMode.WhenIdle, CancellationToken.None);
        WorkWrites(adapter).ShouldBeEmpty("R-42");
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-42");
        row.DeliveryAttempts.ShouldBe(0, "R-42");
        queued.Messages.ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("session")]
    [Arguments("idle")]
    [Arguments("stranded")]
    [Arguments("missing")]
    public async Task Explicit_flush_during_boot_types_nothing(string which)
    {
        foreach (var status in NonRunning)
        {
            await using var h = await CreateCodexHarnessAsync(status);
            var adapter = h.Adapter;
            var id = await h.SeedPendingMessageAsync("flush brief", createdAtUtc: DateTime.UtcNow.AddMinutes(-10));
            if (which == "session")
                await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
            else if (which == "idle")
                await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
            else if (which == "stranded")
                await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            else
                await h.Queue.FlushSessionAsync(Guid.NewGuid(), CancellationToken.None);

            WorkWrites(adapter).ShouldBeEmpty("R-43");
            if (which != "missing")
            {
                await using var db = BridgeQueueHarness.CreateContext();
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
                row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-43");
                row.DeliveryAttempts.ShouldBe(0, "R-43");
            }
        }
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Turn_end_during_boot_types_nothing(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("turn-end brief");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        if (status == SessionStatus.Running)
        {
            adapter.SubmittedBodies.ShouldContain(b => b.Contains("turn-end brief"));
            return;
        }

        WorkWrites(adapter).ShouldBeEmpty("R-44");
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-44");
        row.DeliveryAttempts.ShouldBe(0, "R-44");
    }

    [Test]
    public async Task Launch_ownership_spans_the_entire_startup_gate()
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = hold };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var generation = await StartedAtAsync(h.SessionId);
        var queue = h.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        queue.EnqueueInteractiveSession(h.SessionId, h.AgentId, generation, Spec(h, generation), null);
        await WaitUntilAsync(() => adapter.Started);
        queue.Owns(h.SessionId).ShouldBeTrue("R-45");
        hold.TrySetResult(true);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("mismatch")]
    [Arguments("not-found")]
    public async Task Readiness_failure_targets_only_the_accepted_generation(string variant)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = BlockedRunner();
        client.SnapshotHold = hold;
        if (variant == "not-found")
            client.KillGenerationNotFound = true;
        await using var h = await CreateCodexHarnessAsync(
            SessionStatus.Starting, runner: client, ready: ReadySettings(maxMs: 1200, settleMs: 50));
        var g1 = SessionGeneration.Normalize(await StartedAtAsync(h));
        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, g1), null, false, null,
                CancellationToken.None, acceptedGeneration: g1);
        await WaitUntilAsync(() => client.WaitingOnSnapshot);
        var g2 = SessionGeneration.Next(g1, DateTime.UtcNow);
        if (variant is "replacement" or "mismatch")
        {
            await using (var db = OpenDb(h))
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                session.StartedAt = g2;
                await db.SaveChangesAsync();
            }

            client.RestampLive(g2, reported: variant == "mismatch");
        }

        hold.TrySetResult();
        await Should.ThrowAsync<Exception>(() => launch);
        client.UnconditionalKills.ShouldBe(0, "R-46");
        client.KillGenerationCalls.ShouldBe([g1], "R-46");
        if (variant != "not-found")
            client.ReplacementAlive.ShouldBeTrue("R-46 replacementAlive");
    }

    [Test]
    public async Task Readiness_failure_kills_before_disposal()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var generation = await StartedAtAsync(h.SessionId);
        using var scope = h.Provider.CreateScope();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, generation), null, false, null, CancellationToken.None, acceptedGeneration: generation));
        adapter.Lifecycle.ShouldBe(["KillGeneration", "Dispose"], "R-47");
    }

    [Test]
    public async Task Readiness_timeout_does_not_consume_delivery_or_boot_wedge_attempts()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        var id = await h.SeedPendingMessageAsync("wedge brief");
        var generation = await StartedAtAsync(h.SessionId);
        using var scope = h.Provider.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .LaunchInteractiveAsync(h.SessionId, h.AgentId, Spec(h, generation), null, false, null, CancellationToken.None, acceptedGeneration: generation);
        }
        catch (InvalidOperationException)
        {
            // expected not-ready
        }

        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryAttempts.ShouldBe(0, "R-48");
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBe(0, "R-48");
        (await db.AgentTasks.Where(t => t.AgentId == h.AgentId).SumAsync(t => t.BootWedgeRelaunchCount))
            .ShouldBe(0, "R-48");
    }

    [Test]
    public async Task Restart_during_boot_reverifies_before_delivering()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting, adapter);
        await h.SeedPendingMessageAsync("resume brief");
        using var scope = h.Provider.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(h.SessionId, h.AgentId, CancellationToken.None);
        }
        catch (Exception)
        {
            // not-ready or attach
        }

        WorkWrites(adapter).ShouldBeEmpty("R-49");
    }

    [Test]
    public async Task A_marker_or_prefix_receipt_does_not_confirm_the_brief()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "[antiphon-task:deadbeef]");
        await h.Queue.EnqueueAsync(h.SessionId, "full body that must match", MessageSendMode.WhenIdle, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered, "R-50");
    }

    [Test]
    public async Task Old_receipt_does_not_confirm_this_attempt()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "same full body";
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered, "R-51");
    }

    [Test]
    public async Task Wrong_session_receipt_does_not_confirm_this_attempt()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        adapter.OnSubmitted = _ => Task.CompletedTask;
        adapter.EchoTypedInputToScreen = false;
        adapter.SwallowSubmits = 99;
        const string body = "cross-session body";
        var otherSessionId = await AddSessionAsync(h.TempRoot);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, body, sessionId: otherSessionId, timestamp: DateTime.UtcNow);
        var id = await h.SeedPendingMessageAsync(body);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        await using var db = BridgeQueueHarness.CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered, "R-52");
        row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed, "R-52");
    }

    [Test]
    [Arguments("starting")]
    [Arguments("running")]
    public async Task Crash_at_running_handoff_preserves_the_queued_brief(string cut)
    {
        await using var h = await CreateCodexHarnessAsync(
            cut == "starting" ? SessionStatus.Starting : SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("handoff brief");
        if (cut == "starting")
        {
            WorkWrites(adapter).ShouldBeEmpty();
            await using var db = BridgeQueueHarness.CreateContext();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBe(0);
            return;
        }

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain(b => b.Contains("handoff brief"));
        await using var verify = BridgeQueueHarness.CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id)).Id.ShouldBe(id);
    }

    [Test]
    public async Task Committed_brief_is_recovered_after_service_recreation()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "recovered brief";
        var id = await h.SeedPendingMessageAsync(body);
        long sequence;
        await using (var db = OpenDb(h))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status = SessionStatus.Running;
            await db.SaveChangesAsync();
            sequence = (await db.SessionQueuedMessages.SingleAsync(m => m.Id == id)).Sequence;
        }

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-53");
        await using var verify = OpenDb(h);
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Id.ShouldBe(id, "R-53");
        row.Sequence.ShouldBe(sequence, "R-53");
        row.Body.ShouldBe(body, "R-53");
    }

    [Test]
    public async Task Interrupted_typed_brief_recovers_with_enter_only()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        adapter.EchoTypedInputToScreen = true;
        const string body = "held composer body";
        adapter.PrimeComposer(body);
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        adapter.Inputs.ShouldBe(["\r"], "R-54 recoveredWrites");
    }

    [Test]
    public async Task Accepted_prompt_before_verdict_commit_is_not_typed_again()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "already accepted";
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        var before = adapter.Inputs.Count;
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        adapter.Inputs.Skip(before).ToArray().ShouldBeEmpty("R-55");
        WorkWrites(adapter).ShouldBeEmpty("R-55");
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-55");
    }

    [Test]
    [Arguments(false, "dispatch-committed")]
    [Arguments(true, "dispatch-committed")]
    [Arguments(false, "brief-insert")]
    [Arguments(true, "brief-insert")]
    public async Task Enqueue_failure_is_reported_to_the_original_caller(bool busyParent, string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var workspace = Directory.CreateTempSubdirectory("c574-r58").FullName;
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace);
        var (_, callerId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace, agentId, AgentModelLevel.High, "lost Codex startup brief");
        var dispatchedAt = DateTime.UtcNow.AddMinutes(-12);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            (await db.Agents.SingleAsync(a => a.Id == agentId)).ModelLevel = AgentModelLevel.High;
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.Attempt = 3;
            stored.ParentSessionId = callerId;
            stored.ReplyTo = AgentTaskReplyTo.Session;
            stored.AgentKind = AgentKind.Codex;
            if (cut == "dispatch-committed")
            {
                stored.Status = AgentTaskStatus.Dispatched;
                stored.DispatchedAt = dispatchedAt;
                stored.AgentSessionId = sessionId;
            }

            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.AgentKind = AgentKind.Codex;
            session.Status = SessionStatus.Starting;
            session.StartedAt = dispatchedAt;
            await db.SaveChangesAsync();
        }

        var clock = new OffsetClock();
        IInterceptor? fault = cut == "brief-insert" ? new BriefInsertHang(task.Id) : null;
        await using var producer = CreateFailureProvider(schema.ConnectionString, fault, clock);
        var caller = new FakeAgentProtocolAdapter { ReadyResult = true };
        caller.OnSubmitted = async submitted =>
        {
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.UserPrompt, submitted,
                timestamp: clock.GetUtcNow().UtcDateTime, connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
        };
        producer.GetRequiredService<AgentSessionRuntime>().Register(callerId, caller);
        if (busyParent)
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.AssistantText,
                "caller is busy", connectionString: schema.ConnectionString);

        if (cut == "brief-insert")
        {
            using (var scope = producer.CreateScope())
            {
                var capacity = producer.GetRequiredService<CapacityRecoveryService>();
                await capacity.EnsureWaitAsync(new CapacityWaitRegistration
                {
                    ConsumerKey = $"task:{task.Id:N}",
                    ConsumerKind = CapacityWaitConsumerKind.QueuedTask,
                    ExecutionKind = AgentKind.Codex,
                    RequestedKind = AgentKind.Codex,
                    RequestedAlias = "codex",
                    TaskId = task.Id,
                    HoldAlreadyCleared = true,
                    BlockedAt = clock.GetUtcNow().UtcDateTime,
                }, CancellationToken.None);
                await capacity.GrantReadyAsync(CancellationToken.None);
                await Should.ThrowAsync<IOException>(() =>
                    scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default));
            }

            clock.Advance(TimeSpan.FromMinutes(11));
        }

        using (var scope = producer.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .FailNeverStartedAsync(default)).ShouldBe(1, "R-58");

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed, "R-58");
        var failureObligation = await verify.AgentTaskLandNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.TaskId == task.Id && n.Kind == LandNotificationKind.DeliveryFailure);
        failureObligation.ShouldNotBeNull("R-58");
        if (busyParent)
        {
            caller.SubmittedBodies.ShouldBeEmpty("R-58 busy parent holds Pending");
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
            await producer.GetRequiredService<SessionMessageQueueService>().OnTurnEndAsync(callerId, default);
        }

        await producer.GetRequiredService<SessionMessageQueueService>().FlushStrandedQueuesAsync(default);
        var note = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == task.Id);
        note.SourceLandNotificationId.ShouldBe(failureObligation.Id);
        (await CompleteReceiptCountAsync(schema.ConnectionString, callerId, note.Body)).ShouldBe(1, "R-58");
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("superseded-exit")]
    [Arguments("null-generation")]
    public async Task Resumed_readiness_failure_cannot_kill_replacement(string variant)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = BlockedRunner();
        client.SnapshotHold = hold;
        if (variant == "null-generation")
            client.ReportNullAcceptedStartedAt = true;
        await using var h = await CreateCodexHarnessAsync(
            SessionStatus.Starting, runner: client, ready: ReadySettings(maxMs: 1200, settleMs: 50),
            dispatchedTask: true);
        var g1 = SessionGeneration.Normalize(await StartedAtAsync(h));
        client.PrimeAttach(h.SessionId, variant == "null-generation" ? null : g1);
        using var scope = h.Provider.CreateScope();
        var resume = scope.ServiceProvider.GetRequiredService<AgentSessionService>()
            .ResumeInterruptedLaunchAsync(h.SessionId, h.AgentId, CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => client.WaitingOnSnapshot);
            var g2 = SessionGeneration.Next(g1, DateTime.UtcNow);
            if (variant is "replacement" or "superseded-exit")
            {
                await using (var db = OpenDb(h))
                {
                    var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                    session.StartedAt = g2;
                    await db.SaveChangesAsync();
                }

                client.RestampLive(g2, reported: variant == "superseded-exit");
            }

            hold.TrySetResult();
            await Should.ThrowAsync<Exception>(() => resume);
            client.UnconditionalKills.ShouldBe(0, "R-61");
            if (variant == "null-generation")
                client.KillGenerationCalls.ShouldBeEmpty("R-61 null AcceptedStartedAt makes no runner kill call");
            else
                client.KillGenerationCalls.ShouldBe([g1], "R-61");
            if (variant != "null-generation")
                client.ReplacementAlive.ShouldBeTrue("R-61");
        }
        finally
        {
            hold.TrySetResult();
            try { await resume.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* already observed */ }
        }
    }

    [Test]
    public async Task Crash_after_attempt_commit_recovers_an_untyped_brief()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "untyped after commit";
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, status: QueuedMessageStatus.Sent);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-62");
        await using var db = OpenDb(h);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Id.ShouldBe(id, "R-62");
        PromptSubmissionMatch.IsCompleteIn(body, row.Body).ShouldBeTrue("R-62");
    }

    [Test]
    public async Task Producer_brief_reaches_an_already_eligible_recipient_whole()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "eligible brief";
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-63");
        await using var db = OpenDb(h);
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        PromptSubmissionMatch.IsCompleteIn(body, row.Body).ShouldBeTrue("R-63");
    }

    [Test]
    public async Task Producer_brief_waits_for_busy_recipient_and_arrives_whole()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Running);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        await h.MarkWorkingAsync();
        const string body = "busy brief";
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            deliverIfIdle: true);
        WorkWrites(adapter).ShouldBeEmpty();
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(0, "R-64 before turn-end");
        adapter.TurnCompleted = true;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-64");
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Send_now_during_boot_is_refused_without_input(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        var id = await h.SeedPendingMessageAsync("send-now brief");
        const string body = "send-now brief";
        if (status == SessionStatus.Running)
        {
            await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
            (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-65");
            return;
        }

        var ex = await Should.ThrowAsync<ConflictException>(
            () => h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None));
        ex.Message.ShouldContain("is still starting");
        WorkWrites(adapter).ShouldBeEmpty("R-65");
        await using (var db = OpenDb(h))
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending, "R-65");
            row.DeliveryAttempts.ShouldBe(0, "R-65");
        }

        await using (var db = OpenDb(h))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status = SessionStatus.Running;
            await db.SaveChangesAsync();
        }

        adapter.ReadyResult = true;
        await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
        (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-65 later Running send");
        await using var verify = OpenDb(h);
        var delivered = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        delivered.Id.ShouldBe(id, "R-65");
        PromptSubmissionMatch.IsCompleteIn(body, delivered.Body).ShouldBeTrue("R-65");
    }

    [Test]
    public async Task Send_now_during_boot_beats_a_closed_grok_rules_barrier()
    {
        await using var h = await CreateCodexHarnessAsync(SessionStatus.Starting);
        await using (var db = OpenDb(h))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.AgentKind = AgentKind.Grok;
            session.GrokRulesState = GrokRulesState.Pending;
            await db.SaveChangesAsync();
        }

        var id = await h.SeedPendingMessageAsync("grok send-now");
        var ex = await Should.ThrowAsync<ConflictException>(
            () => h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None));
        ex.Message.ShouldContain("is still starting");
        WorkWrites(h.Adapter).ShouldBeEmpty("R-65");
    }

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Stopping)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    [Arguments(SessionStatus.Running)]
    public async Task Mode_now_during_boot_is_refused_without_input(SessionStatus status)
    {
        await using var h = await CreateCodexHarnessAsync(status);
        var adapter = h.Adapter;
        adapter.ReadyResult = true;
        const string body = "mode-now brief";
        if (status == SessionStatus.Running)
        {
            await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);
            (await CompleteReceiptCountAsync(h, body)).ShouldBe(1, "R-66");
            return;
        }

        var before = await CountQueueAsync(h);
        var ex = await Should.ThrowAsync<ConflictException>(
            () => h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None));
        ex.Message.ShouldContain("is still starting");
        WorkWrites(adapter).ShouldBeEmpty("R-66");
        (await CountQueueAsync(h)).ShouldBe(before, "R-66");
    }

    [Test]
    public async Task Crash_cut_worker_entry()
    {
        // Assembly-load entry for ANTIPHON_C574_STARTUP_WORKER; the fixture hijacks Before(Assembly).
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("failed-committed")]
    [Arguments("note-committed")]
    [Arguments("prompt-accepted")]
    public async Task Readiness_failure_notice_survives_worker_crash(string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var workspace = Directory.CreateTempSubdirectory("c574-v7").FullName;
        var (agentId, sessionId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace);
        var (_, callerId) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace);
        var task = await ModelAvailabilityDispatcherTests.SeedQueuedTaskAsync(
            schema.ConnectionString, workspace, agentId, AgentModelLevel.High, "readiness failure notice");
        var dispatchedAt = DateTime.UtcNow.AddMinutes(-12);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            stored.Status = AgentTaskStatus.Dispatched;
            stored.DispatchedAt = dispatchedAt;
            stored.AgentSessionId = sessionId;
            stored.ParentSessionId = callerId;
            stored.ReplyTo = AgentTaskReplyTo.Session;
            stored.Attempt = 1;
            stored.AgentKind = AgentKind.Codex;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status = SessionStatus.Starting;
            session.AgentKind = AgentKind.Codex;
            session.StartedAt = dispatchedAt;
            await db.SaveChangesAsync();
        }

        var retained = new ScriptedCodexRunnerClient { StartupScreens = [CodexStartupFixtures.N1] };
        retained.PrimeAttach(sessionId, dispatchedAt);
        var settings = CodexStartupDeliveryWorker.CreateSettings(
            schema.ConnectionString, cut, sessionId, agentId, task.Id, callerId);
        await CodexStartupDeliveryWorker.CrashAsync(
            settings, retained, typeof(CodexStartupDeliveryTests).Assembly.Location);

        var clock = new OffsetClock();
        clock.Advance(TimeSpan.FromMinutes(11));
        await using var recovered = CreateFailureProvider(schema.ConnectionString, null, clock);
        var caller = new FakeAgentProtocolAdapter { ReadyResult = true };
        caller.OnSubmitted = async submitted =>
        {
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.UserPrompt, submitted,
                timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(callerId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: schema.ConnectionString);
        };
        recovered.GetRequiredService<AgentSessionRuntime>().Register(callerId, caller);
        using (var scope = recovered.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.FailNeverStartedAsync(default);
            await dispatcher.RemindUnacknowledgedFailuresAsync(default);
        }

        await recovered.GetRequiredService<SessionMessageQueueService>().FlushStrandedQueuesAsync(default);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var obligation = await verify.AgentTaskLandNotifications.AsNoTracking()
            .SingleAsync(n => n.TaskId == task.Id && n.Kind == LandNotificationKind.DeliveryFailure);
        var note = await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.SourceLandNotificationId == obligation.Id);
        (await CompleteReceiptCountAsync(schema.ConnectionString, callerId, note.Body)).ShouldBe(1);
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == task.Id)).ShouldBe(1);
        (await verify.AgentTasks.CountAsync(t => t.Id == task.Id)).ShouldBe(1);
    }

    [Test]
    public async Task Incident_startup_frames_never_receive_work()
    {
        var client = new ScriptedCodexRunnerClient
        {
            StartupScreens = [CodexStartupFixtures.N1, CodexStartupFixtures.N2],
        };
        var adapter = new RunnerCodexAdapter(client, ReadySettings(maxMs: 300, settleMs: 50));
        await adapter.StartAsync(SpecWithGeneration(DateTime.UtcNow), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        client.Writes.ShouldBeEmpty();
    }

    private static async Task<BridgeQueueHarness> CreateCodexHarnessAsync(
        SessionStatus status,
        FakeAgentProtocolAdapter? launchAdapter = null,
        ScriptedCodexRunnerClient? runner = null,
        IOptions<AgentRegistrySettings>? ready = null,
        bool dispatchedTask = false)
    {
        var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureServices = s =>
            {
                if (launchAdapter is not null)
                    s.AddSingleton<IAgentProtocolAdapterFactory>(new OneAdapterFactory(launchAdapter));
                else if (runner is not null)
                {
                    var options = ready ?? ReadySettings();
                    s.AddSingleton<ISessionRunnerClient>(runner);
                    s.AddSingleton(options);
                    s.AddSingleton<IAgentProtocolAdapterFactory>(new RunnerCodexFactory(runner, options));
                }
            },
        });
        await using var db = OpenDb(h);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Codex;
        session.DefinitionName = "codex";
        session.Status = status;
        session.StartedAt = SessionGeneration.Normalize(session.StartedAt);
        if (dispatchedTask)
        {
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "c574 resume",
                Goal = "Do the thing.",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.Codex,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = h.TempRoot,
                AgentSessionId = h.SessionId,
                AgentId = h.AgentId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = session.StartedAt,
                DispatchedAt = session.StartedAt,
            });
        }

        await db.SaveChangesAsync();
        return h;
    }

    private static AppDbContext OpenDb(BridgeQueueHarness h) =>
        new(TestDbFixture.CreateDbContextOptions(h.ConnectionString));

    private static IReadOnlyList<string> WorkWrites(FakeAgentProtocolAdapter adapter) =>
        adapter.Inputs.Where(i => i != "\r" && i.Length > 0).ToArray();

    private static AgentLaunchSpec Spec(BridgeQueueHarness h, DateTime? generation = null) =>
        SpecWithGeneration(generation ?? DateTime.UtcNow) with { Cwd = h.TempRoot, SessionId = h.SessionId };

    private static Task<DateTime> StartedAtAsync(BridgeQueueHarness h) => StartedAtAsync(h.SessionId, h.ConnectionString);

    private static async Task<DateTime> StartedAtAsync(Guid sessionId, string? connection = null)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync();
    }

    private static AgentLaunchSpec SpecWithGeneration(DateTime generation) => new(
        DefinitionName: "codex",
        Kind: AgentKind.Codex,
        Exe: "codex.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid(),
        AcceptedStartedAt: generation);

    private static IOptions<AgentRegistrySettings> ReadySettings(int settleMs = 50, int maxMs = 400) =>
        Options.Create(new AgentRegistrySettings
        {
            CodexReadyQuietPeriodMs = settleMs,
            CodexReadyMaxWaitMs = maxMs,
            CodexBootStatusMaxWaitMs = 0,
        });

    private static ScriptedCodexRunnerClient BlockedRunner() => new()
    {
        StartupScreens = [CodexStartupFixtures.N1],
    };

    private static async Task<int> CountQueueAsync(BridgeQueueHarness h)
    {
        await using var db = OpenDb(h);
        return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId);
    }

    private static Task<int> CompleteReceiptCountAsync(BridgeQueueHarness h, string body) =>
        CompleteReceiptCountAsync(h.ConnectionString, h.SessionId, body);

    private static async Task<int> CompleteReceiptCountAsync(string connection, Guid sessionId, string body)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        return prompts.Count(p => p.Text is not null && PromptSubmissionMatch.IsCompleteIn(body, p.Text));
    }

    private static async Task<Guid> AddSessionAsync(string cwd)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = BridgeQueueHarness.CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "codex",
            AgentKind = AgentKind.Codex,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(15))
                throw new TimeoutException("condition not met");
            await Task.Delay(20);
        }
    }

    private static ServiceProvider CreateFailureProvider(
        string connection, IInterceptor? fault, TimeProvider clock) =>
        CapacityRecoveryTaskTests.CreateDispatcherProvider(connection, fault, clock,
            new DeliveryVerificationSettings
            {
                EvidenceTimeoutSeconds = 1,
                PollIntervalMs = 50,
                PostSubmitAdvanceTimeoutSeconds = 1,
                StrandedAgeSeconds = 0,
                TranscriptConfirmTimeoutSeconds = 3,
                ReEnterIntervalSeconds = 1,
                PostFailureConfirmGraceSeconds = 3,
                UnobservableBaselineConfirmClockToleranceSeconds = 30,
                BootPromptRetryDelaySeconds = 0,
            });

    private sealed class OneAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RunnerCodexFactory(
        ISessionRunnerClient client, IOptions<AgentRegistrySettings> options) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => new RunnerCodexAdapter(client, options);
    }

    private sealed class OffsetClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan amount) => _offset += amount;
    }

    private sealed class BriefInsertHang(Guid taskId) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var queue = eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                .SingleOrDefault(e => e.Entity.ExecutionTaskId == taskId);
            if (queue is { State: EntityState.Added })
                throw new IOException("cut before brief insert commit");
            return ValueTask.FromResult(result);
        }
    }
}
