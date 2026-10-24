using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.RolePolicyToolBundleDispatchTests;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel("AgentQueue")]
public sealed class PreDispatchFailureDeliveryTests
{
    private const string UnknownKey = "tool-does-not-exist";

    [Test]
    [Timeout(120_000)]
    public async Task Pre_dispatch_failed_commit_keeps_the_caller_obligation(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            PreserveDatabaseOnDispose = true,
        });
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fault = new FailedCommitCut(interrupted);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sink = new RecordingSink();
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
            taskId = (await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId)).Id;

        await using (var producer = BuildProvider(schema.ConnectionString, sink, UnknownKey,
            parent, clock, fault, checkEnabled: false))
            await Should.ThrowAsync<OperationCanceledException>(() => TickAsync(producer, interrupted.Token));

        fault.Hits.ShouldBe(1, "cut is triggered by the Failed transition, independent of an outbox insert");
        sink.Items.ShouldBeEmpty();
        await using var verify = parent.CreateObserverContext();
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.DispatchedAt.ShouldBeNull();
        var failedEvent = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Failed, ct);
        failedEvent.Detail.ShouldContain("stage_tool_bundle_unknown");
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        (await NotePromptsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        var obligation = (await verify.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == taskId).ToListAsync(ct)).ShouldHaveSingleItem(
                "the first committed Failed save must already own durable caller delivery");
        obligation.SourceEventId.ShouldBe(failedEvent.Id);
        obligation.Kind.ShouldBe(LandNotificationKind.DeliveryFailure);
        obligation.IsLegacy.ShouldBeFalse();
        obligation.Body.ShouldContain("stage_tool_bundle_unknown");
        obligation.Body.ShouldContain("Delegation:RolePolicy:Review:Bundles");
        obligation.Body.ShouldContain(UnknownKey);
        failed.NextCheckAt.ShouldBeNull();

        await using var recovered = BuildProvider(schema.ConnectionString, sink, UnknownKey,
            parent, clock, checkEnabled: false);
        await RecoverAsync(recovered, ct);
        await parent.Queue.FlushStrandedQueuesAsync(ct);
        await RecoverAsync(recovered, ct);
        var row = (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldHaveSingleItem();
        row.SourceLandNotificationId.ShouldBe(obligation.Id);
        row.Body.ShouldBe(obligation.Body);
        await AssertDeliveredOnceAsync(parent, taskId, row.Id, "recovered committed failure");
        (await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == obligation.Id, ct))
            .State.ShouldBe(LandNotificationState.Confirmed);
        await RecoverAsync(recovered, ct);
        await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
        await AssertDeliveredOnceAsync(parent, taskId, row.Id, "replay");
    }

    [Test]
    [Timeout(120_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Pre_dispatch_failed_insert_recovers_with_checks_disabled(bool busy, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var fault = new QueueInsertFault { Remaining = 2 };
        var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = o => o.AddInterceptors(fault),
        });
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sink = new RecordingSink();
        try
        {
            Guid taskId;
            await using (var db = parent.CreateObserverContext())
                taskId = (await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId)).Id;
            if (busy) await parent.MarkWorkingAsync();
            await using (var producer = BuildProvider(schema.ConnectionString, sink, UnknownKey,
                parent, clock, checkEnabled: false))
                await TickAsync(producer, ct);
            fault.Hits.ShouldBe(1, "the immediate insert reached its fault");
            (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
            (await NotePromptsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
            var failed = await ReadTaskAsync(schema.ConnectionString, taskId);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.CompletionNoteQueuedAt.ShouldBeNull();
            failed.NextCheckAt.ShouldBeNull();
            var sessionId = parent.SessionId;
            var agentId = parent.AgentId;
            await parent.DisposeAsync();
            parent = await CreateParentAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                PreserveDatabaseOnDispose = true,
                AttachSessionId = sessionId,
                AttachAgentId = agentId,
                ConfigureDbContext = o => o.AddInterceptors(fault),
            });
            await using var recovered = BuildProvider(schema.ConnectionString, sink, UnknownKey,
                parent, clock, checkEnabled: false);
            await RecoverAsync(recovered, ct);
            fault.Hits.ShouldBe(2, "the independent reconciler insert also failed");
            await using var verify = parent.CreateObserverContext();
            var debt = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId, ct);
            debt.EnqueueAttempts.ShouldBe(1);
            debt.State.ShouldBe(LandNotificationState.RetryPending);
            (debt.NextAttemptAt - debt.LastErrorAt!.Value).ShouldBe(TimeSpan.FromSeconds(5));
            clock.SetUtcNow(new DateTimeOffset(debt.NextAttemptAt.AddTicks(-1), TimeSpan.Zero));
            await RecoverAsync(recovered, ct);
            await using (var scope = recovered.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(debt.Id, ct);
            (await RowsAsync(schema.ConnectionString, sessionId)).ShouldBeEmpty("neither recovery owner retries early");
            (await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == debt.Id, ct))
                .EnqueueAttempts.ShouldBe(1);
            clock.Advance(TimeSpan.FromTicks(1));
            await RecoverAsync(recovered, ct);
            var row = (await RowsAsync(schema.ConnectionString, sessionId)).ShouldHaveSingleItem();
            row.SourceLandNotificationId.ShouldBe(debt.Id);
            row.Body.ShouldBe(debt.Body);
            if (busy)
            {
                await parent.Queue.FlushStrandedQueuesAsync(ct);
                (await NotePromptsAsync(schema.ConnectionString, sessionId)).ShouldBeEmpty();
                row.DeliveryAttempts.ShouldBe(0);
                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            }
            await parent.Queue.OnTurnEndAsync(sessionId, ct);
            await RecoverAsync(recovered, ct);
            await AssertDeliveredOnceAsync(parent, taskId, row.Id, "checks disabled");
            var confirmed = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == debt.Id, ct);
            confirmed.State.ShouldBe(LandNotificationState.Confirmed);
            confirmed.ConfirmingPromptSequence.ShouldBe(
                (await NotePromptsAsync(schema.ConnectionString, sessionId)).Single().Sequence);
            recovered.GetRequiredService<IOptions<DelegationSettings>>().Value.CheckEnabled = true;
            clock.Advance(TimeSpan.FromHours(2));
            await RecoverAsync(recovered, ct);
            await parent.Queue.OnTurnEndAsync(sessionId, ct);
            await AssertDeliveredOnceAsync(parent, taskId, row.Id, "no legacy duplicate");
            (await ReadTaskAsync(schema.ConnectionString, taskId)).CheckCount.ShouldBe(0);
            sink.Items.ShouldBeEmpty();
        }
        finally { await parent.DisposeAsync(); }
    }

    [Test]
    [Timeout(120_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Preflight_failure_obligation_commits_with_its_claim(bool interruptClaim, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        using var repo = new ScratchGitRepo("c1232-claim");
        await repo.CommitFileAsync("README.md", "isolated preflight claim\n");
        var sink = new RecordingSink();
        using var interrupted = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var cut = new FailedCommitCut(interrupted);
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
        {
            var task = await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Docs, parent.SessionId);
            taskId = task.Id;
            task.AgentId = parent.AgentId;
            task.Ephemeral = false;
            task.Workspace = WorkspaceMode.Worktree;
            task.WorkingDirectory = repo.Path;
            task.RepoPath = repo.Path;
            await db.SaveChangesAsync(ct);
        }
        if (interruptClaim)
        {
            await using (var producer = BuildProvider(schema.ConnectionString, sink, UnknownKey,
                parent, fault: cut, checkEnabled: false))
                await Should.ThrowAsync<OperationCanceledException>(() => TickAsync(producer, interrupted.Token));
            cut.Hits.ShouldBe(1);
            await using var rolledBack = parent.CreateObserverContext();
            (await rolledBack.AgentTasks.SingleAsync(t => t.Id == taskId, ct)).Status.ShouldBe(AgentTaskStatus.Queued);
            (await rolledBack.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
                && e.Type == AgentTaskEventType.Failed, ct)).ShouldBe(0);
            (await rolledBack.AgentTaskLandNotifications.CountAsync(n => n.TaskId == taskId, ct)).ShouldBe(0);
            (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        }
        await using (var producer = BuildProvider(schema.ConnectionString, sink, UnknownKey,
            parent, checkEnabled: false))
            await TickAsync(producer, ct);
        await using var verify = parent.CreateObserverContext();
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull().ShouldContain("workspace_existing_agent_conflict");
        failed.DispatchedAt.ShouldBeNull();
        var debt = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId, ct);
        debt.SourceEventId.ShouldBe((await verify.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.Failed, ct)).Id);
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty("direct preflight commits without enqueue");
        await using var recovered = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent, checkEnabled: false);
        await RecoverAsync(recovered, ct);
        await parent.Queue.FlushStrandedQueuesAsync(ct);
        await RecoverAsync(recovered, ct);
        await AssertReceiptAsync(parent, debt, taskId, ct);
        sink.Items.ShouldBeEmpty();
    }

    [Test]
    [Arguments(AgentTaskReplyTo.None)]
    [Arguments(AgentTaskReplyTo.Session)]
    public async Task Pre_dispatch_excluded_routes_create_no_obligation(AgentTaskReplyTo route)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        var sink = new RecordingSink();
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
        {
            var task = await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId);
            taskId = task.Id;
            task.ReplyTo = route;
            if (route == AgentTaskReplyTo.Session) task.ParentSessionId = null;
            await db.SaveChangesAsync();
        }
        await using var provider = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent);
        await TickAsync(provider, CancellationToken.None);
        await using var verify = parent.CreateObserverContext();
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.NextCheckAt.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Failed)).ShouldBe(1);
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == taskId)).ShouldBe(0);
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        sink.Items.ShouldBeEmpty();
    }

    [Test]
    public async Task Pre_dispatch_snapshot_survives_task_retry()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        await using var other = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        await parent.MarkWorkingAsync();
        var sink = new RecordingSink();
        Guid taskId;
        Guid rootId;
        await using (var db = parent.CreateObserverContext())
        {
            var root = await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Docs);
            rootId = root.Id;
            root.Status = AgentTaskStatus.Succeeded;
            var child = await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId);
            taskId = child.Id;
            child.RootTaskId = rootId;
            child.Title = "first immutable failure title";
            await db.SaveChangesAsync();
        }
        await using var first = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent, checkEnabled: false);
        await TickAsync(first, CancellationToken.None);
        await using var verify = parent.CreateObserverContext();
        var original = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId);
        original.Body.ShouldContain("first immutable failure title");
        await using (var scope = first.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(taskId, CancellationToken.None);
        await verify.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(
            s => s.SetProperty(t => t.Title, "second distinct failure title")
                .SetProperty(t => t.ParentSessionId, other.SessionId));
        await using var second = BuildProvider(schema.ConnectionString, sink, UnknownKey + "-later", other, checkEnabled: false);
        await TickAsync(second, CancellationToken.None);
        var debts = await verify.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == taskId).ToListAsync();
        debts.Count.ShouldBe(2);
        var later = debts.Single(n => n.Id != original.Id);
        later.SourceEventId.ShouldNotBe(original.SourceEventId);
        later.Body.ShouldContain("second distinct failure title");
        later.Body.ShouldContain(UnknownKey + "-later");
        later.ContentDigest.ShouldNotBe(original.ContentDigest);
        var unchanged = debts.Single(n => n.Id == original.Id);
        unchanged.Body.ShouldBe(original.Body);
        unchanged.ContentDigest.ShouldBe(original.ContentDigest);
        unchanged.ParentSessionId.ShouldBe(original.ParentSessionId);
        await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
        await RecoverAsync(second, CancellationToken.None);
        await AssertReceiptAsync(parent, original, rootId, CancellationToken.None);
        await AssertReceiptAsync(other, later, rootId, CancellationToken.None);
        await RecoverAsync(second, CancellationToken.None);
        await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).Count.ShouldBe(1);
        (await RowsAsync(schema.ConnectionString, other.SessionId)).Count.ShouldBe(1);
        parent.Adapter.SubmittedBodies.Count.ShouldBe(1);
        other.Adapter.SubmittedBodies.Count.ShouldBe(1);
        sink.Items.ShouldBeEmpty();
    }

    [Test]
    public async Task Pre_dispatch_receipt_requires_complete_user_prompt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        var sink = new RecordingSink();
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
            taskId = (await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId)).Id;
        await using var provider = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent, checkEnabled: false);
        await TickAsync(provider, CancellationToken.None);
        await using var verify = parent.CreateObserverContext();
        var debt = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId);
        var prompt = (await NotePromptsAsync(schema.ConnectionString, parent.SessionId)).ShouldHaveSingleItem();
        var original = prompt.Text!;
        var headerOnly = original.Split('\n')[0];
        await verify.TranscriptEntries.Where(t => t.Id == prompt.Id).ExecuteUpdateAsync(
            s => s.SetProperty(t => t.Text, headerOnly));
        await RecoverAsync(provider, CancellationToken.None);
        (await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == debt.Id))
            .ConfirmedAt.ShouldBeNull("a header and Sent stamp cannot confirm the complete failure");
        await verify.TranscriptEntries.Where(t => t.Id == prompt.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Text, original));
        await RecoverAsync(provider, CancellationToken.None);
        await AssertReceiptAsync(parent, debt, taskId, CancellationToken.None);
    }

    [Test]
    [Arguments("canceled")]
    [Arguments("missing")]
    [Arguments("exhausted")]
    public async Task Pre_dispatch_recovery_retains_authoritative_row(string state)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        await parent.MarkWorkingAsync();
        var sink = new RecordingSink();
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
            taskId = (await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId)).Id;
        await using var provider = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent, checkEnabled: false);
        await TickAsync(provider, CancellationToken.None);
        await RecoverAsync(provider, CancellationToken.None);
        var original = (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldHaveSingleItem();
        await using var verify = parent.CreateObserverContext();
        var debt = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId);
        if (state == "canceled")
            await parent.Queue.CancelAsync(parent.SessionId, original.Id, CancellationToken.None);
        else if (state == "missing")
            await verify.SessionQueuedMessages.Where(m => m.Id == original.Id).ExecuteDeleteAsync();
        else
        {
            var floor = await parent.CurrentTranscriptMaxSequenceAsync();
            var attemptedAt = DateTime.UtcNow;
            await verify.SessionQueuedMessages.Where(m => m.Id == original.Id).ExecuteUpdateAsync(
                s => s.SetProperty(m => m.DeliveryAttempts, 3)
                    .SetProperty(m => m.LastDeliveryBaselineSequence, floor)
                    .SetProperty(m => m.LastDeliveryStartedAt, attemptedAt));
        }
        await RecoverAsync(provider, CancellationToken.None);
        await RecoverAsync(provider, CancellationToken.None);
        var held = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == debt.Id);
        held.QueueMessageId.ShouldBe(original.Id);
        held.ConfirmedAt.ShouldBeNull();
        held.LastErrorCode.ShouldBe(state switch
        {
            "canceled" => "queue_canceled_unconfirmed",
            "missing" => "keyed_queue_row_missing",
            _ => "queue_parked_unconfirmed",
        });
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).Count.ShouldBe(state == "missing" ? 0 : 1);
        parent.Adapter.SubmittedBodies.ShouldBeEmpty();
        // Fixture control restores this same row; production must never replace it.
        if (state == "missing") verify.SessionQueuedMessages.Add(original);
        else await verify.SessionQueuedMessages.Where(m => m.Id == original.Id).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.Status, QueuedMessageStatus.Pending).SetProperty(m => m.DeliveryAttempts, 0));
        await verify.SaveChangesAsync();
        await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
        await RecoverAsync(provider, CancellationToken.None);
        await AssertReceiptAsync(parent, debt, taskId, CancellationToken.None);
    }

    [Test]
    public async Task Pre_dispatch_recovery_waits_for_destination()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await CreateParentAsync(new()
        {
            ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
        });
        using var interrupted = new CancellationTokenSource();
        var cut = new FailedCommitCut(interrupted);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sink = new RecordingSink();
        Guid taskId;
        await using (var db = parent.CreateObserverContext())
            taskId = (await SeedQueuedAsync(db, parent.TempRoot, AgentTaskRole.Review, parent.SessionId)).Id;
        await using (var producer = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent, clock, cut, false))
            await Should.ThrowAsync<OperationCanceledException>(() => TickAsync(producer, interrupted.Token));
        cut.Hits.ShouldBe(1);
        await using var verify = parent.CreateObserverContext();
        var debt = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId);
        await verify.AgentSessions.Where(s => s.Id == parent.SessionId).ExecuteUpdateAsync(s => s.SetProperty(s => s.Status, SessionStatus.Stopped));
        await using var recovered = BuildProvider(schema.ConnectionString, sink, UnknownKey, parent, clock, checkEnabled: false);
        await RecoverAsync(recovered, CancellationToken.None);
        var held = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == debt.Id);
        held.State.ShouldBe(LandNotificationState.DestinationUnavailable);
        (held.NextAttemptAt - held.LastErrorAt!.Value).ShouldBe(TimeSpan.FromMinutes(5));
        held.Body.ShouldBe(debt.Body);
        (await RowsAsync(schema.ConnectionString, parent.SessionId)).ShouldBeEmpty();
        await verify.AgentSessions.Where(s => s.Id == parent.SessionId).ExecuteUpdateAsync(s => s.SetProperty(s => s.Status, SessionStatus.Running));
        clock.SetUtcNow(new DateTimeOffset(held.NextAttemptAt, TimeSpan.Zero));
        await RecoverAsync(recovered, CancellationToken.None);
        await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await RecoverAsync(recovered, CancellationToken.None);
        await AssertReceiptAsync(parent, debt, taskId, CancellationToken.None);
    }

    private static async Task AssertReceiptAsync(BridgeQueueHarness parent, AgentTaskLandNotification debt,
        Guid rootTaskId, CancellationToken ct)
    {
        await using var db = parent.CreateObserverContext();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceLandNotificationId == debt.Id, ct);
        row.SourceTaskId.ShouldBe(debt.TaskId);
        row.ConversationKey.ShouldBe($"task:{rootTaskId:N}");
        row.ContentDigest.ShouldBe(debt.ContentDigest);
        row.Body.ShouldBe(debt.Body);
        row.DeliveryAttempts.ShouldBeGreaterThan(0);
        var prompts = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == parent.SessionId
            && t.Kind == TranscriptKinds.UserPrompt).ToListAsync(ct);
        var prompt = prompts.Where(p => PromptSubmissionMatch.IsCompleteIn(debt.Body, p.Text)).ShouldHaveSingleItem();
        prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence.ShouldNotBeNull());
        parent.Adapter.SubmittedBodies.Count(b => PromptSubmissionMatch.IsCompleteIn(debt.Body, b)).ShouldBe(1);
        var confirmed = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == debt.Id, ct);
        confirmed.State.ShouldBe(LandNotificationState.Confirmed);
        confirmed.ConfirmingPromptSequence.ShouldBe(prompt.Sequence);
    }

    private static async Task<BridgeQueueHarness> CreateParentAsync(BridgeQueueHarness.HarnessOptions options)
    {
        var parent = await ClaudeDialogFixture.CreateBridgeAsync(options);
        if (options.AttachSessionId is null)
        {
            await parent.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "caller baseline before delegation");
            await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        }
        return parent;
    }

    private static async Task RecoverAsync(ServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().RemindUnacknowledgedFailuresAsync(ct);
    }

    private sealed class FailedCommitCut(CancellationTokenSource interrupted) : SaveChangesInterceptor
    {
        public int Hits { get; private set; }
        private bool _armed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            _armed = Hits == 0 && data.Context!.ChangeTracker.Entries<AgentTask>().Any(
                e => e.State == EntityState.Modified && e.Entity.Status == AgentTaskStatus.Failed);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_armed)
            {
                Hits++;
                interrupted.Cancel();
                throw new OperationCanceledException(interrupted.Token);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class QueueInsertFault : SaveChangesInterceptor
    {
        public int Remaining { get; set; }
        public int Hits { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Remaining > 0 && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(e => e.State == EntityState.Added))
            {
                Remaining--;
                Hits++;
                throw new IOException("CARD-1232 injected queue insert failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
