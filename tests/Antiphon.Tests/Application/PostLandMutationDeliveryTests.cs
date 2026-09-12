using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PostLandMutationDeliveryTests
{
    [Test]
    public async Task C478_V09a_LandProducerToCaller()
    {
        await using var land = new LandingSafetyHarness();
        await land.InitializeAsync();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = land.Schema.ConnectionString,
        });
        land.Messages = bridge.Queue;
        await using (var db = land.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == land.Fixture.TaskId);
            task.ParentSessionId = bridge.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            await db.SaveChangesAsync();
        }
        await land.AddSourceAsync();
        await land.RunAsync();
        await using var observer = land.CreateContext();
        var note = await observer.AgentTaskLandNotifications.SingleAsync(n =>
            n.TaskId == land.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
        note.Body.ShouldContain("publication=");
        note.Body.ShouldContain(land.Fixture.TaskId.ToString("N"));
        note.LandingOperationId.ShouldNotBeNull();
        (await observer.AgentTaskLandings.SingleAsync(o => o.Id == note.LandingOperationId)).VerifiedSourceSha.ShouldNotBeNull();
        await ConfirmLandReceiptAsync(land.Schema.ConnectionString, bridge, note, busy: false);
    }

    [Test]
    public async Task C478_V09b_AcceptedTaskToWorker()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var capture = new MutationDispatchTestsCapture();
        await using var bridge = await BridgeQueueHarness.CreateAsync(DispatchOptions(world, capture));
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Queued;
            task.AgentId = null;
            task.AgentSessionId = null;
            task.AgentKind = AgentKind.Raw;
            await db.SaveChangesAsync();
        }
        var (sessionId, queued, adapter) = await DispatchWorkerBriefAsync(world, bridge, capture);
        await ConfirmQueuedReceiptAsync(world.Host.Schema.ConnectionString, bridge, queued, sessionId, busy: false,
            adapter: adapter);
    }

    [Test]
    public async Task C478_V09c_SettledMutationToCaller()
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = world.Host.Schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(world.Host.Fixture.Root, "trees") });
                services.AddSingleton<ILandingGit>(world.Host.Fixture.Git);
            },
        });
        var worker = Guid.NewGuid();
        var report = "Mutation complete.\n--- next stage ---\nnext: none\n"
            + DelegationReportFormatter.ReportToken(world.TaskId, "done");
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            db.AgentSessions.Add(new AgentSession
            {
                Id = worker, Status = SessionStatus.Running, Cwd = task.WorktreePath!,
                AgentKind = AgentKind.Raw, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            task.AgentSessionId = worker;
            task.Status = AgentTaskStatus.Working;
            task.ParentSessionId = bridge.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            var seq = 1L;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = seq++,
                Kind = TranscriptKinds.UserPrompt, Text = DelegationReportFormatter.TaskMarker(world.TaskId),
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = seq++,
                Kind = TranscriptKinds.AssistantText, Text = report,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = seq,
                Kind = TranscriptKinds.TurnEnd, Text = null, StopReason = TranscriptKinds.StopReasons.EndTurn,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var replies = new AgentTaskReplyService(
            bridge.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings()),
            bridge.EventBus, TimeProvider.System, NullLogger<AgentTaskReplyService>.Instance);
        await replies.OnTurnEndAsync(worker, default);
        await using var observer = world.Host.CreateContext();
        var settled = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded, settled.FailureReason);
        settled.Result.ShouldNotBeNull();
        var queued = await observer.SessionQueuedMessages.SingleAsync(m =>
            m.AgentSessionId == bridge.SessionId && m.SourceTaskId == world.TaskId);
        queued.Body.ShouldContain("[task " + DelegationReportFormatter.Short(world.TaskId) + " done]");
        settled.Result.ShouldContain("Mutation complete");
        await ConfirmQueuedReceiptAsync(world.Host.Schema.ConnectionString, bridge, queued, bridge.SessionId, busy: false);
    }

    [Test]
    [Arguments("before-enqueue", true)]
    [Arguments("before-enqueue", false)]
    [Arguments("queue-inserted", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", true)]
    [Arguments("lost-wakeup", false)]
    [Arguments("receipt-before-save", true)]
    [Arguments("receipt-before-save", false)]
    [Arguments("after-receipt", true)]
    [Arguments("after-receipt", false)]
    [Arguments("publication-commit", false)]
    [Arguments("post-submit-process", false)]
    public async Task C478_V09a_LandCrashMatrix(string cut, bool busy) =>
        await LandCrashAsync(cut, busy);

    [Test]
    [Arguments("before-submit", true)]
    [Arguments("before-submit", false)]
    [Arguments("after-prompt", true)]
    [Arguments("after-prompt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("after-receipt", false)]
    public async Task C478_V09b_WorkerCrashMatrix(string cut, bool busy) =>
        await WorkerCrashAsync(cut, busy);

    [Test]
    [Arguments("before-submit", true)]
    [Arguments("before-submit", false)]
    [Arguments("after-prompt", true)]
    [Arguments("after-prompt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("after-receipt", false)]
    public async Task C478_V09c_SettlementCrashMatrix(string cut, bool busy) =>
        await SettlementCrashAsync(cut, busy);

    [Test] public Task C478_G134_LandAtomic() => LandCrashAsync("queue-inserted", false);
    [Test] public Task C478_G135_LandEnqueue() => LandCrashAsync("before-enqueue", false);
    [Test] public Task C478_G136_LandQueueKey() => LandCrashAsync("queue-inserted", true);
    [Test] public Task C478_G137_LandWakeup() => LandCrashAsync("lost-wakeup", false);
    [Test] public Task C478_G138_LandBusy() => LandCrashAsync("lost-wakeup", true);
    [Test] public Task C478_G139_LandReceipt() => LandCrashAsync("after-receipt", false);
    [Test] public Task C478_G140_LandDestination() => LandWrongSessionAsync();
    [Test] public Task C478_G141_LandIdentity() => LandWrongIdentityAsync();
    [Test] public Task C478_G142_LandCompleteness() => LandPartialPromptAsync();
    [Test] public Task C478_G143_LandFreshness() => LandStalePromptAsync();
    [Test] public Task C478_G144_LandReceiptSave() => LandCrashAsync("receipt-before-save", false);
    [Test] public Task C478_G145_LaunchPersist() => WorkerCrashAsync("before-submit", false);
    [Test] public Task C478_G146_LaunchRecovery() => WorkerCrashAsync("before-submit", true);
    [Test] public Task C478_G147_LaunchReceipt() => WorkerCrashAsync("after-prompt", false);
    [Test] public Task C478_G148_LaunchGeneration() => WorkerCrashAsync("after-receipt", true);
    [Test] public Task C478_G155_CompletionIdentity() => CompletionWrongIdentityAsync();
    [Test] public Task C478_G157_LandNotReport() => LandDoesNotSatisfyCompletionNoteAsync();
    [Test] public Task C478_G160_CompletionSession() => CompletionWrongSessionAsync();

    [Test]
    public async Task C478_G149_CompletionPersist()
    {
        var fault = new QueueInsertFault();
        await using var settled = await SettleMutationAsync(
            new() { ConfigureDbContext = o => o.AddInterceptors(fault) }, fault: fault);
        await using var observer = settled.World.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId);
        task.Status.ShouldBe(AgentTaskStatus.Succeeded, task.FailureReason);
        task.Result.ShouldContain("Mutation complete");
        task.Result.ShouldContain("--- next stage ---");
        task.SourceLandingOperationId.ShouldBe(settled.World.Operation);
        task.SourceLandingSha.ShouldBe(settled.World.Host.Fixture.SeedSha);
        task.NextStage.ShouldBe(PipelineHandoffKind.None);
        (await observer.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
        fault.Triggered.ShouldBeTrue();
    }

    [Test]
    public async Task C478_CompletionReplayAfterRetention()
    {
        await using var settled = await SettleMutationAsync();
        await using (var db = settled.World.Host.CreateContext())
        {
            var queued = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
            queued.Status = QueuedMessageStatus.Sent;
            queued.SentAt = DateTime.UtcNow.AddDays(-40);
            queued.CreatedAt = DateTime.UtcNow.AddDays(-40);
            queued.DeliveryAttempts = Math.Max(1, queued.DeliveryAttempts);
            await db.SaveChangesAsync();
            var audit = Options.Create(new AuditSettings());
            await new DataRetentionService(db, Options.Create(new RetentionSettings()), audit, TimeProvider.System,
                NullLogger<DataRetentionService>.Instance, new AuditService(db, audit))
                .PruneQueuedMessagesAsync(CancellationToken.None);
        }

        await using (var observer = settled.World.Host.CreateContext())
        {
            (await observer.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
            (await AgentTaskCheckService.HasCompletionNoteAsync(observer, settled.Bridge.SessionId,
                settled.World.TaskId, CancellationToken.None)).ShouldBeTrue();
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId);
            task.CompletionNoteQueuedAt.ShouldNotBeNull();
            task.CompletionNoteDigest.ShouldBe(DelegationNoteDigest.Compute(task.Result!));
        }

        var typed = settled.Bridge.Adapter.Inputs.Count;
        using var hosted = await StartCompletionRecoveryAsync(settled.Bridge);
        try
        {
            await UntilAsync(async () =>
            {
                await using var db = settled.World.Host.CreateContext();
                return (await db.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId))
                    .CompletionNoteQueuedAt is not null;
            }, "completion stamp should already exist before the scanner runs");
            await Task.Delay(1500);
            await using var recovered = settled.World.Host.CreateContext();
            (await recovered.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
            (await AgentTaskCheckService.HasCompletionNoteAsync(recovered, settled.Bridge.SessionId,
                settled.World.TaskId, CancellationToken.None)).ShouldBeTrue();
            settled.Bridge.Adapter.Inputs.Count.ShouldBe(typed);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
            hosted.Dispose();
        }
    }

    [Test]
    public async Task C478_CompletionReplayAfterPartialEnqueueRetention()
    {
        await using var settled = await SettleMutationAsync();
        await using (var db = settled.World.Host.CreateContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId);
            task.CompletionNoteQueuedAt.ShouldNotBeNull();
            task.CompletionNoteDigest.ShouldNotBeNull();
            task.CompletionNoteQueuedAt = null;
            task.CompletionNoteDigest = null;
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            await db.SaveChangesAsync();
        }

        SessionQueuedMessage pending;
        await using (var db = settled.World.Host.CreateContext())
            pending = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);

        await ConfirmQueuedReceiptAsync(
            settled.World.Host.Schema.ConnectionString, settled.Bridge, pending,
            settled.Bridge.SessionId, busy: false, cut: "after-receipt");

        await using (var db = settled.World.Host.CreateContext())
        {
            var sent = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
            sent.Status.ShouldBe(QueuedMessageStatus.Sent);
            sent.DeliveryVerdict.ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
            sent.SentAt = DateTime.UtcNow.AddDays(-40);
            sent.CreatedAt = DateTime.UtcNow.AddDays(-40);
            sent.DeliveryAttempts = Math.Max(1, sent.DeliveryAttempts);
            await db.SaveChangesAsync();
            var audit = Options.Create(new AuditSettings());
            await new DataRetentionService(db, Options.Create(new RetentionSettings()), audit, TimeProvider.System,
                NullLogger<DataRetentionService>.Instance, new AuditService(db, audit))
                .PruneQueuedMessagesAsync(CancellationToken.None);
        }

        await using (var observer = settled.World.Host.CreateContext())
        {
            (await observer.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId);
            task.CompletionNoteQueuedAt.ShouldNotBeNull();
            task.CompletionNoteDigest.ShouldBe(DelegationNoteDigest.Compute(task.Result!));
            (await AgentTaskCheckService.HasCompletionNoteAsync(observer, settled.Bridge.SessionId,
                settled.World.TaskId, CancellationToken.None)).ShouldBeTrue();
        }

        var typed = settled.Bridge.Adapter.Inputs.Count;
        using var hosted = await StartCompletionRecoveryAsync(settled.Bridge);
        try
        {
            await UntilAsync(async () =>
            {
                await using var db = settled.World.Host.CreateContext();
                return (await db.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId))
                    .CompletionNoteQueuedAt is not null
                    && await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId) == 0;
            }, "partial-enqueue stamp repair must survive retention without a replayed queue row");
            await Task.Delay(1500);
            await using var recovered = settled.World.Host.CreateContext();
            (await recovered.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
            (await AgentTaskCheckService.HasCompletionNoteAsync(recovered, settled.Bridge.SessionId,
                settled.World.TaskId, CancellationToken.None)).ShouldBeTrue();
            settled.Bridge.Adapter.Inputs.Count.ShouldBe(typed);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
            hosted.Dispose();
        }
    }

    [Test]
    public async Task C478_G150_CompletionEnqueue()
    {
        var fault = new QueueInsertFault();
        await using var settled = await SettleMutationAsync(
            new() { ConfigureDbContext = o => o.AddInterceptors(fault) }, fault: fault);
        fault.Triggered.ShouldBeTrue();
        await using (var observer = settled.World.Host.CreateContext())
        {
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Succeeded, task.FailureReason);
            task.Result.ShouldContain("Mutation complete");
            (await observer.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(0);
        }

        SessionQueuedMessage? queued = null;
        using (var hosted = await StartCompletionRecoveryAsync(settled.Bridge))
        {
            try
            {
                await UntilAsync(async () =>
                {
                    await using var db = settled.World.Host.CreateContext();
                    queued = await db.SessionQueuedMessages.AsNoTracking()
                        .SingleOrDefaultAsync(m => m.SourceTaskId == settled.World.TaskId);
                    return queued is not null;
                }, "missing completion note was not recovered from the durable task");
            }
            finally
            {
                await hosted.StopAsync(CancellationToken.None);
            }
        }

        await ConfirmQueuedReceiptAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge, queued!,
            settled.Bridge.SessionId, busy: false, cut: "after-receipt");
        await using var recovered = settled.World.Host.CreateContext();
        (await recovered.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId
            && m.Status != QueuedMessageStatus.Canceled)).ShouldBe(1);
        (await recovered.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId)).Result
            .ShouldContain("Mutation complete");
    }

    [Test]
    public async Task C478_G151_CompletionQueueKey()
    {
        await using var settled = await SettleMutationAsync();
        await using var observer = settled.World.Host.CreateContext();
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        await settled.Bridge.Queue.EnqueueAsync(settled.Bridge.SessionId, queued.Body, MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Delegation, queued.ConversationKey, settled.World.TaskId,
            queued.ContentDigest, queued.NoteHeader, deliverIfIdle: false);
        (await observer.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == settled.World.TaskId)).ShouldBe(1);
        await ConfirmQueuedReceiptAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge, queued,
            settled.Bridge.SessionId, busy: false, cut: "after-prompt");
    }

    [Test]
    public async Task C478_G152_CompletionWakeup()
    {
        await using var settled = await SettleMutationAsync();
        await using var observer = settled.World.Host.CreateContext();
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        settled.Bridge.Adapter.Inputs.ShouldBeEmpty();

        using var hosted = await StartCompletionRecoveryAsync(settled.Bridge);
        try
        {
            await UntilAsync(() => Task.FromResult(settled.Bridge.Adapter.SubmittedBodies.Count > 0),
                "hosted completion scan did not flush the persisted idle note");
            await ConfirmQueuedReceiptAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge, queued,
                settled.Bridge.SessionId, busy: false, cut: "after-receipt");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
            hosted.Dispose();
        }
    }

    [Test]
    public async Task C478_G153_CompletionBusy()
    {
        await using var settled = await SettleMutationAsync();
        await using var observer = settled.World.Host.CreateContext();
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        await ConfirmQueuedReceiptAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge, queued,
            settled.Bridge.SessionId, busy: true, cut: "before-submit");
    }

    [Test]
    public async Task C478_G154_CompletionReceipt()
    {
        await using var settled = await SettleMutationAsync();
        await using var db = settled.World.Host.CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        StampParkedSentAttempt(queued);
        await db.SaveChangesAsync();
        (await db.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == settled.Bridge.SessionId
            && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        var typed = settled.Bridge.Adapter.Inputs.Count;
        await settled.Bridge.Queue.OnTurnEndAsync(settled.Bridge.SessionId, CancellationToken.None);
        settled.Bridge.Adapter.Inputs.Count.ShouldBe(typed);
        await using var recovered = settled.World.Host.CreateContext();
        (await recovered.TranscriptEntries.CountAsync(e => e.AgentSessionId == settled.Bridge.SessionId
            && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        var saved = await recovered.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        saved.DeliveryVerdict.ShouldBeNull();
        saved.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
        saved.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task C478_G156_CompletionRestore()
    {
        await using var settled = await SettleMutationAsync();
        await using var observer = settled.World.Host.CreateContext();
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        var result = (await observer.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId)).Result;
        await ConfirmQueuedReceiptAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge, queued,
            settled.Bridge.SessionId, busy: false, cut: "after-receipt");
        await using var recovered = settled.World.Host.CreateContext();
        (await recovered.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId)).Result.ShouldBe(result);
        (await recovered.TranscriptEntries.CountAsync(e => e.AgentSessionId == settled.Bridge.SessionId
            && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
    }

    [Test]
    public async Task C478_G158_CompletionCompleteness()
    {
        await using var settled = await SettleMutationAsync();
        await using var db = settled.World.Host.CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        StampParkedSentAttempt(queued);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = settled.Bridge.SessionId, Sequence = 5,
            Kind = TranscriptKinds.UserPrompt, Text = queued.Body[..Math.Min(200, queued.Body.Length)] + queued.Body[^Math.Min(100, queued.Body.Length)..],
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = settled.Bridge.SessionId, Sequence = 6,
            Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = settled.Bridge.Adapter.Inputs.Count;
        await settled.Bridge.Queue.OnTurnEndAsync(settled.Bridge.SessionId, CancellationToken.None);
        await using var recovered = settled.World.Host.CreateContext();
        var saved = await recovered.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        saved.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
        saved.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered);
        PromptSubmissionMatch.IsCompleteIn(queued.Body, (await recovered.TranscriptEntries
            .SingleAsync(e => e.AgentSessionId == settled.Bridge.SessionId && e.Kind == TranscriptKinds.UserPrompt)).Text!)
            .ShouldBeFalse();
        settled.Bridge.Adapter.Inputs.Count.ShouldBe(typed);
    }

    [Test]
    public async Task C478_G159_CompletionFreshness()
    {
        await using var settled = await SettleMutationAsync();
        await using var db = settled.World.Host.CreateContext();
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        StampParkedSentAttempt(queued, baseline: 10);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = settled.Bridge.SessionId, Sequence = 10,
            Kind = TranscriptKinds.UserPrompt, Text = queued.Body,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = settled.Bridge.SessionId, Sequence = 11,
            Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = settled.Bridge.Adapter.Inputs.Count;
        await settled.Bridge.Queue.OnTurnEndAsync(settled.Bridge.SessionId, CancellationToken.None);
        await using var recovered = settled.World.Host.CreateContext();
        (await recovered.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id)).DeliveryVerdict.ShouldBeNull();
        settled.Bridge.Adapter.Inputs.Count.ShouldBe(typed);
    }

    [Test]
    public async Task C478_G161_CompletionSpill()
    {
        var matrix = "Mutation complete.\n" + string.Join('\n', Enumerable.Range(0, 400).Select(i =>
            $"pc-{i:000} restored-green sha=c478-spill-{i:x4}")) + "\n--- next stage ---\nnext: none\n";
        await using var settled = await SettleMutationAsync(report: matrix);
        await using var observer = settled.World.Host.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == settled.World.TaskId);
        task.Result.ShouldContain("pc-000 restored-green");
        task.Result.ShouldContain("pc-399 restored-green");
        task.Result.Length.ShouldBeGreaterThan(3000);
        task.ResultFilePath.ShouldNotBeNull();
        (await File.ReadAllTextAsync(task.ResultFilePath)).ShouldBe(task.Result);
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == settled.World.TaskId);
        queued.Body.Length.ShouldBeLessThan(task.Result.Length);
        queued.Body.ShouldContain("THIS REPORT IS AN EXCERPT");
        queued.Body.ShouldContain(task.ResultFilePath);
        await ConfirmQueuedReceiptAsync(settled.World.Host.Schema.ConnectionString, settled.Bridge, queued,
            settled.Bridge.SessionId, busy: false, cut: "after-receipt");
        (await File.ReadAllTextAsync(task.ResultFilePath)).ShouldBe(task.Result);
    }

    private static async Task LandCrashAsync(string cut, bool busy)
    {
        if (cut == "publication-commit")
        {
            await PublicationCommitCrashAsync();
            return;
        }
        await using var land = new LandingSafetyHarness();
        await land.InitializeAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = land.Schema.ConnectionString });
        land.Messages = h.Queue;
        await using (var db = land.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == land.Fixture.TaskId);
            task.ParentSessionId = h.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            await db.SaveChangesAsync();
        }
        await land.AddSourceAsync();
        await land.RunAsync();
        await using var seeded = new AppDbContext(TestDbFixture.CreateDbContextOptions(land.Schema.ConnectionString));
        var note = await seeded.AgentTaskLandNotifications.SingleAsync(n =>
            n.TaskId == land.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
        if (cut == "post-submit-process")
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var service = new AgentTaskLandNotificationService(seeded, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, clock);
            await service.ReconcileAsync(note.Id, CancellationToken.None);
            var queued = await seeded.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
            queued.Status = QueuedMessageStatus.Sent;
            queued.DeliveryAttempts = 1;
            queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
            queued.LastDeliveryBaselineSequence = 10;
            seeded.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10,
                Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow.AddMinutes(-1),
            });
            seeded.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11,
                Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
            });
            await seeded.SaveChangesAsync();
            await PostLandMutationDeliveryWorker.CrashAtReceiptSaveAsync(land.Schema.ConnectionString, note.Id,
                typeof(PostLandMutationDeliveryTests).Assembly.Location);
            await ConfirmLandReceiptAsync(land.Schema.ConnectionString, h, note, busy: false, alreadySubmitted: true);
            return;
        }

        var cutClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cutBoundary = new DeliveryCut(cut);
        var cutService = new AgentTaskLandNotificationService(seeded, h.Queue, new CompletionNoteFlushQueue(), h.Runtime,
            cutClock, cutBoundary);
        if (cut is "before-enqueue" or "queue-inserted")
        {
            await cutService.ReconcileAsync(note.Id, CancellationToken.None);
            var afterFault = await seeded.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            afterFault.ConfirmedAt.ShouldBeNull();
            afterFault.State.ShouldBe(LandNotificationState.RetryPending);
            if (cut == "before-enqueue") afterFault.QueueMessageId.ShouldBeNull();
            await seeded.AgentTaskLandNotifications.Where(n => n.Id == note.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
            await ConfirmLandReceiptAsync(land.Schema.ConnectionString, h, note, busy);
            return;
        }

        await cutService.ReconcileAsync(note.Id, CancellationToken.None);
        if (cut == "lost-wakeup")
        {
            var dropped = await seeded.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
            dropped.QueueMessageId.ShouldNotBeNull();
            dropped.ConfirmedAt.ShouldBeNull();
            h.Adapter.Inputs.ShouldBeEmpty();
            await ConfirmLandReceiptAsync(land.Schema.ConnectionString, h, note, busy, alreadySubmitted: false);
            return;
        }

        if (cut == "receipt-before-save")
        {
            var queuedId = (await seeded.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id)).QueueMessageId;
            queuedId.ShouldNotBeNull();
            var queued = await seeded.SessionQueuedMessages.SingleAsync(m => m.Id == queuedId);
            queued.Status = QueuedMessageStatus.Sent;
            queued.DeliveryAttempts = 1;
            queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
            queued.LastDeliveryBaselineSequence = 10;
            if (!await seeded.TranscriptEntries.AnyAsync(e => e.AgentSessionId == h.SessionId && e.Sequence == 10))
            {
                seeded.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10,
                    Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
                    Timestamp = DateTime.UtcNow.AddMinutes(-1),
                });
            }
            seeded.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11,
                Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
            });
            await seeded.SaveChangesAsync();
            await cutService.ReconcileAsync(note.Id, CancellationToken.None);
            cutBoundary.ReceiptSaveReached.ShouldBeTrue();
            (await seeded.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id))
                .ConfirmedAt.ShouldBeNull();
            await ConfirmLandReceiptAsync(land.Schema.ConnectionString, h, note, busy, alreadySubmitted: true);
            return;
        }

        await ConfirmLandReceiptAsync(land.Schema.ConnectionString, h, note, busy,
            alreadySubmitted: cut == "after-receipt");
    }

    private static async Task PublicationCommitCrashAsync()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = h.Schema.ConnectionString });
        h.Messages = bridge.Queue;
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.ParentSessionId = bridge.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            await db.SaveChangesAsync();
        }
        await h.AddSourceAsync();
        var ready = Path.Combine(h.Fixture.Root, "worker-ready.json");
        var script = Path.Combine(h.Fixture.Root, "protocol-worker.ps1");
        await File.WriteAllTextAsync(script, """
            $ErrorActionPreference = 'Stop'
            $assembly = [Reflection.Assembly]::LoadFrom($args[0])
            $type = $assembly.GetType('Antiphon.Tests.TestHelpers.LandingSafetyHarness', $true)
            $method = $type.GetMethod('RunCrashWorkerAsync', [Reflection.BindingFlags]'Public,Static')
            $task = $method.Invoke($null, [object[]]@($args[1], $args[2], $args[3], $args[4]))
            $task.GetAwaiter().GetResult()
            """);
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-File", script, typeof(LandingSafetyHarness).Assembly.Location,
                     h.Fixture.Root, h.Fixture.TaskId.ToString(), "C14", ready }) start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_C448_TEST_CONNECTION"] = h.Schema.ConnectionString;
        using var worker = System.Diagnostics.Process.Start(start)!;
        var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (!File.Exists(ready) && !worker.HasExited) await Task.Delay(100, budget.Token);
            File.Exists(ready).ShouldBeTrue(worker.HasExited ? await stderr : "publication-commit cut not reached");
            worker.Kill(entireProcessTree: false);
            await worker.WaitForExitAsync();
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(true);
        }
        start.ArgumentList[6] = "resume";
        using (var resumed = System.Diagnostics.Process.Start(start)!)
        {
            using var resumedBudget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await resumed.WaitForExitAsync(resumedBudget.Token); }
            finally
            {
                if (!resumed.HasExited) resumed.Kill(true);
                await resumed.WaitForExitAsync();
            }
        }
        await using var observer = h.CreateContext();
        var note = await observer.AgentTaskLandNotifications.SingleAsync(n =>
            n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
        note.Body.ShouldContain("publication=");
        await ConfirmLandReceiptAsync(h.Schema.ConnectionString, bridge, note, busy: false);
    }

    private static async Task WorkerCrashAsync(string cut, bool busy)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        var capture = new MutationDispatchTestsCapture();
        await using var h = await BridgeQueueHarness.CreateAsync(DispatchOptions(world, capture));
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Queued;
            task.AgentId = null;
            task.AgentSessionId = null;
            task.AgentKind = AgentKind.Raw;
            await db.SaveChangesAsync();
        }
        var (session, queued, adapter) = await DispatchWorkerBriefAsync(world, h, capture, busy);
        await using var recovered = await BridgeQueueHarness.CreateAsync(DispatchOptions(world, capture));
        using (var scope = recovered.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
        await recovered.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
        await using (var observer = world.Host.CreateContext())
        {
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            task.AgentSessionId.ShouldBe(session);
            (await observer.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == world.TaskId)).ShouldBe(1);
        }
        recovered.Runtime.Register(session, adapter);
        await ConfirmQueuedReceiptAsync(world.Host.Schema.ConnectionString, recovered, queued, session, busy, cut,
            adapter: adapter);
    }

    private static async Task SettlementCrashAsync(string cut, bool busy)
    {
        await using var world = await PostLandMutationWorld.CreateAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = world.Host.Schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(world.Host.Fixture.Root, "trees") });
                services.AddSingleton<ILandingGit>(world.Host.Fixture.Git);
            },
        });
        var worker = Guid.NewGuid();
        var report = "Mutation complete.\n--- next stage ---\nnext: none\n"
            + DelegationReportFormatter.ReportToken(world.TaskId, "done");
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            db.AgentSessions.Add(new AgentSession
            {
                Id = worker, Status = SessionStatus.Running, Cwd = task.WorktreePath!,
                AgentKind = AgentKind.Raw, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            task.AgentSessionId = worker;
            task.Status = AgentTaskStatus.Working;
            task.ParentSessionId = h.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = DelegationReportFormatter.TaskMarker(world.TaskId),
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = 2,
                Kind = TranscriptKinds.AssistantText, Text = report,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = 3,
                Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await new AgentTaskReplyService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings()),
            h.EventBus, TimeProvider.System, NullLogger<AgentTaskReplyService>.Instance)
            .OnTurnEndAsync(worker, default);
        await using var observer = world.Host.CreateContext();
        (await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        var queued = await observer.SessionQueuedMessages.SingleAsync(m =>
            m.AgentSessionId == h.SessionId && m.SourceTaskId == world.TaskId);
        await ConfirmQueuedReceiptAsync(world.Host.Schema.ConnectionString, h, queued, h.SessionId, busy, cut);
    }

    private static async Task ConfirmLandReceiptAsync(string connection, BridgeQueueHarness h,
        AgentTaskLandNotification note, bool busy, bool alreadySubmitted = false)
    {
        if (busy) await SetWorkingAsync(connection, h.SessionId, true);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var saved = await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == note.Id);
        if (saved.QueueMessageId is null)
        {
            saved.State.ShouldBe(LandNotificationState.RetryPending);
            return;
        }
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == saved.QueueMessageId);
        queued.Body.ShouldContain("publication=");
        if (busy && !alreadySubmitted)
        {
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
            h.Adapter.Inputs.ShouldBeEmpty();
            queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
            queued.Status.ShouldBe(QueuedMessageStatus.Pending);
            await SetWorkingAsync(connection, h.SessionId, false);
        }
        else if (busy && alreadySubmitted)
            await SetWorkingAsync(connection, h.SessionId, false);
        if (!alreadySubmitted && queued.Status != QueuedMessageStatus.Sent)
        {
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
            h.Adapter.SubmittedBodies.ShouldContain(queued.Body);
        }
        var typed = h.Adapter.Inputs.Count;
        queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = Math.Max(1, queued.DeliveryAttempts);
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence ??= 10;
        if (!await db.TranscriptEntries.AnyAsync(e => e.AgentSessionId == h.SessionId && e.Sequence == 10))
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10,
                Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow.AddMinutes(-1),
            });
        }
        await db.SaveChangesAsync();
        h.Runner.SetTranscript(new(h.SessionId, [new SessionRunnerTranscriptEvent(h.SessionId, 11, TranscriptKinds.UserPrompt,
            "c478-land-" + note.Id.ToString("N"), null, DateTimeOffset.UtcNow, "user", queued.Body.Replace("\n", ""),
            null, null, null, null, null)], 11));
        await using var recovered = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var recovery = new AgentTaskLandNotificationService(recovered, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        await recovery.ReconcileAsync(note.Id, CancellationToken.None);
        var confirmed = await recovered.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        confirmed.State.ShouldBe(LandNotificationState.Confirmed);
        confirmed.ConfirmingPromptSequence.ShouldBe(11);
        (await recovered.TranscriptEntries.CountAsync(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
        h.Adapter.Inputs.Count.ShouldBe(typed);
    }

    private static async Task ConfirmQueuedReceiptAsync(string connection, BridgeQueueHarness h,
        SessionQueuedMessage queued, Guid sessionId, bool busy, string cut = "after-receipt",
        FakeAgentProtocolAdapter? adapter = null)
    {
        adapter ??= h.Adapter;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        if (busy) await SetWorkingAsync(connection, sessionId, true);

        if (queued.Status == QueuedMessageStatus.Pending && queued.DeliveryAttempts == 0)
        {
            await h.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
            if (busy)
            {
                adapter.Inputs.ShouldBeEmpty();
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id))
                    .Status.ShouldBe(QueuedMessageStatus.Pending);
                await SetWorkingAsync(connection, sessionId, false);
                await h.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
            }
            queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
            await AssertSubmittedAsync(h, queued, adapter);
        }

        var typedBody = adapter.SubmittedBodies.LastOrDefault() ?? queued.Body;
        await ConfirmPersistedQueuedReceiptAsync(connection, h, queued, sessionId, typedBody,
            alreadyConfirmed: cut == "after-receipt", adapter: adapter);
    }

    private static async Task<string> AssertSubmittedAsync(
        BridgeQueueHarness h, SessionQueuedMessage queued, FakeAgentProtocolAdapter adapter)
    {
        adapter.SubmittedBodies.Count.ShouldBeGreaterThan(0);
        var typed = adapter.SubmittedBodies[^1];
        if (typed == queued.Body) return typed;
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        var relative = typed.Split('\n').Select(l => l.Trim().Trim('\'', '`'))
            .First(l => l.Contains(".antiphon") && l.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        var absolute = Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(h.TempRoot, "workspace", relative.Replace('/', Path.DirectorySeparatorChar)));
        (await File.ReadAllTextAsync(absolute)).ShouldBe(queued.Body);
        return typed;
    }

    private static async Task ConfirmPersistedQueuedReceiptAsync(string connection, BridgeQueueHarness h,
        SessionQueuedMessage queued, Guid sessionId, string typedBody, bool alreadyConfirmed,
        FakeAgentProtocolAdapter? adapter = null)
    {
        adapter ??= h.Adapter;
        var typed = adapter.Inputs.Count;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        var floor = queued.LastDeliveryBaselineSequence ?? 4;
        var promptSequence = floor + 1;
        var events = new SessionRunnerTranscriptDto(sessionId,
        [
            new SessionRunnerTranscriptEvent(sessionId, promptSequence, TranscriptKinds.UserPrompt,
                "c478-queued-" + queued.Id.ToString("N"), null, DateTimeOffset.UtcNow, "user",
                typedBody.Replace("\n", ""), null, null, null, null, null),
            new SessionRunnerTranscriptEvent(sessionId, promptSequence + 1, TranscriptKinds.TurnEnd,
                "c478-queued-end-" + queued.Id.ToString("N"), null, DateTimeOffset.UtcNow, "assistant",
                null, null, null, null, null, TranscriptKinds.StopReasons.EndTurn),
        ], promptSequence + 1);
        h.Runner.SetTranscript(events);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = Math.Max(1, queued.DeliveryAttempts);
        queued.LastDeliveryBaselineSequence = floor;
        if (alreadyConfirmed)
        {
            queued.DeliveryVerdict ??= DeliveryVerdict.Delivered;
            queued.DeliveryVerdictAt ??= DateTime.UtcNow;
            queued.LastDeliveryStartedAt ??= DateTime.UtcNow.AddSeconds(-1);
        }
        else
        {
            queued.DeliveryVerdict = null;
            queued.DeliveryVerdictAt = null;
            queued.LastDeliveryStartedAt = EligibleInterruptedAt();
        }
        if (!await db.TranscriptEntries.AnyAsync(e => e.AgentSessionId == sessionId && e.Sequence == floor))
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = floor,
                Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow.AddMinutes(-1),
            });
        }
        await db.SaveChangesAsync();

        await using var recovery = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = connection,
        });
        recovery.Runtime.Register(sessionId, adapter);
        recovery.Runner.SetTranscript(events);
        try { await recovery.Runtime.CatchUpTranscriptAsync(sessionId, CancellationToken.None); }
        catch (NotSupportedException) { /* EmptyRunnerClient always has a snapshot. */ }
        if (!await db.TranscriptEntries.AnyAsync(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt))
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = promptSequence,
                Kind = TranscriptKinds.UserPrompt, Text = typedBody,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = promptSequence + 1,
                Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await recovery.Queue.OnTurnEndAsync(sessionId, CancellationToken.None);
        await recovery.Queue.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using var recovered = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var confirmed = await recovered.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
        confirmed.Status.ShouldBe(QueuedMessageStatus.Sent);
        confirmed.DeliveryVerdict.ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
        var prompts = await recovered.TranscriptEntries.Where(p => p.AgentSessionId == sessionId
            && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        prompts.Count.ShouldBeGreaterThanOrEqualTo(1);
        prompts.Any(p => p.Text != null && PromptSubmissionMatch.IsCompleteIn(typedBody, p.Text)).ShouldBeTrue();
        adapter.Inputs.Count.ShouldBe(typed);
    }

    private static async Task SetWorkingAsync(string connection, Guid sessionId, bool working)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = seq,
            Kind = working ? TranscriptKinds.AssistantText : TranscriptKinds.TurnEnd,
            Text = working ? "busy" : null,
            StopReason = working ? null : TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SettledMutation> SettleMutationAsync(BridgeQueueHarness.HarnessOptions? options = null,
        string? report = null, QueueInsertFault? fault = null)
    {
        var world = await PostLandMutationWorld.CreateAsync();
        options ??= new();
        var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = world.Host.Schema.ConnectionString,
            ConfigureDbContext = options.ConfigureDbContext,
            ConfigureServices = services =>
            {
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(world.Host.Fixture.Root, "trees") });
                services.AddSingleton<ILandingGit>(world.Host.Fixture.Git);
                options.ConfigureServices?.Invoke(services);
            },
        });
        var worker = Guid.NewGuid();
        var body = (report ?? "Mutation complete.\n--- next stage ---\nnext: none\n")
            + DelegationReportFormatter.ReportToken(world.TaskId, "done");
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            db.AgentSessions.Add(new AgentSession
            {
                Id = worker, Status = SessionStatus.Running, Cwd = task.WorktreePath!,
                AgentKind = AgentKind.Raw, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            task.AgentSessionId = worker;
            task.Status = AgentTaskStatus.Working;
            task.DispatchedAt = DateTime.UtcNow.AddMinutes(-5);
            task.ParentSessionId = bridge.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = DelegationReportFormatter.TaskMarker(world.TaskId),
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = 2,
                Kind = TranscriptKinds.AssistantText, Text = body,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = worker, Sequence = 3,
                Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        if (fault is not null) fault.Armed = true;
        await new AgentTaskReplyService(
            bridge.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings()),
            bridge.EventBus, TimeProvider.System, NullLogger<AgentTaskReplyService>.Instance)
            .OnTurnEndAsync(worker, default);
        if (fault is not null) fault.Armed = false;
        return new(world, bridge, body);
    }

    private sealed record SettledMutation(PostLandMutationWorld World, BridgeQueueHarness Bridge, string Report)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            await World.DisposeAsync();
        }
    }

    private sealed class QueueInsertFault : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added))
            {
                Triggered = true;
                throw new InvalidOperationException("owned completion enqueue failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task LandDoesNotSatisfyCompletionNoteAsync()
    {
        await using var land = new LandingSafetyHarness();
        await land.InitializeAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = land.Schema.ConnectionString });
        land.Messages = h.Queue;
        await using (var db = land.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == land.Fixture.TaskId);
            task.ParentSessionId = h.SessionId;
            task.ReplyTo = AgentTaskReplyTo.Session;
            await db.SaveChangesAsync();
        }
        await land.AddSourceAsync();
        await land.RunAsync();
        await using var observer = land.CreateContext();
        var note = await observer.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == land.Fixture.TaskId);
        var service = new AgentTaskLandNotificationService(observer, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        (await AgentTaskCheckService.HasCompletionNoteAsync(observer, h.SessionId, land.Fixture.TaskId, CancellationToken.None))
            .ShouldBeFalse();
    }

    private static async Task CompletionWrongIdentityAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        var body = "Mutation complete.\n--- next stage ---\nnext: none\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: taskId, deliverIfIdle: false);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == taskId);
        StampParkedSentAttempt(queued);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 5, Kind = TranscriptKinds.UserPrompt,
            Text = body.Replace(
                DelegationReportFormatter.ReportToken(taskId, "done"),
                DelegationReportFormatter.ReportToken(Guid.NewGuid(), "done")),
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 6,
            Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.Inputs.Count.ShouldBe(typed);
        await db.Entry(queued).ReloadAsync();
        queued.DeliveryVerdict.ShouldBeNull();
        queued.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
        queued.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered);
    }

    private static async Task CompletionWrongSessionAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        var body = "Mutation complete.\n--- next stage ---\nnext: none\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            QueuedMessageOrigin.Delegation, sourceTaskId: taskId, deliverIfIdle: false);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == taskId);
        StampParkedSentAttempt(queued);
        var other = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = other, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = other, Sequence = 5, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.Inputs.Count.ShouldBe(typed);
        await db.Entry(queued).ReloadAsync();
        queued.DeliveryVerdict.ShouldBeNull();
        queued.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
        queued.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered);
    }

    private static async Task LandWrongSessionAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        var other = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession { Id = other, CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = other, Sequence = 11, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    private static async Task LandWrongIdentityAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body.Replace(note.Id.ToString("N"), Guid.NewGuid().ToString("N")),
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
    }

    private static async Task LandPartialPromptAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 11, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body[..200] + queued.Body[^100..], CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
    }

    private static async Task LandStalePromptAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await AgentTaskLandReceiptTests.SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        var queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 10;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 10, Kind = TranscriptKinds.UserPrompt,
            Text = queued.Body, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmedAt.ShouldBeNull();
    }

    private static async Task<(Guid SessionId, SessionQueuedMessage Queued, FakeAgentProtocolAdapter Adapter)>
        DispatchWorkerBriefAsync(
            PostLandMutationWorld world, BridgeQueueHarness h, MutationDispatchTestsCapture capture,
            bool busy = false)
    {
        var ready = busy
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        capture.Configure = adapter =>
        {
            adapter.RegisterOnStart = h.Runtime;
            if (ready is not null) adapter.ReadyHold = ready;
            adapter.OnSubmitted = async submitted =>
            {
                if (adapter.StartedSessionId is not Guid sid) return;
                await InsertWorkerTranscriptAsync(world.Host.Schema.ConnectionString, sid, submitted);
            };
        };

        using (var scope = h.Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);

        Guid sessionId = Guid.Empty;
        await UntilAsync(async () =>
        {
            await using var db = world.Host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            if (task.Status == AgentTaskStatus.Failed)
                throw new InvalidOperationException(task.FailureReason ?? "dispatch failed");
            if (task.AgentSessionId is not Guid id) return false;
            sessionId = id;
            return true;
        }, "dispatcher did not persist a worker session");

        if (busy)
        {
            await SetWorkingAsync(world.Host.Schema.ConnectionString, sessionId, true);
            ready!.TrySetResult(true);
        }

        await h.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

        await using var observer = world.Host.CreateContext();
        var launched = await observer.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        launched.Status.ShouldBe(AgentTaskStatus.Dispatched, launched.FailureReason);
        launched.AgentSessionId.ShouldBe(sessionId);
        launched.SourceLandingOperationId.ShouldBe(world.Operation);
        launched.SourceLandingSha.ShouldBe(world.Host.Fixture.SeedSha);
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.ExecutionTaskId == world.TaskId);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.AgentSessionId.ShouldBe(sessionId);
        var durable = queued.Body;
        if (!durable.Contains("SourceLanding", StringComparison.Ordinal))
        {
            queued.Body.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
            var spill = Path.Combine(launched.WorkingDirectory!, ".antiphon",
                $"task-{DelegationReportFormatter.Short(launched.Id)}-brief.md");
            File.Exists(spill).ShouldBeTrue();
            durable = await File.ReadAllTextAsync(spill);
        }
        durable.ShouldContain("SourceLanding");
        durable.ShouldContain(world.Operation.ToString("D"));
        durable.ShouldContain(world.Host.Fixture.SeedSha);
        capture.Adapters.ShouldHaveSingleItem().Started.ShouldBeTrue();
        return (sessionId, queued, capture.Adapters[0]);
    }

    private static async Task InsertWorkerTranscriptAsync(string connection, Guid sessionId, string submitted)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = seq,
            Kind = TranscriptKinds.UserPrompt, Text = submitted,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = seq + 1,
            Kind = TranscriptKinds.TurnEnd, StopReason = TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static BridgeQueueHarness.HarnessOptions DispatchOptions(
        PostLandMutationWorld world, MutationDispatchTestsCapture capture) => new()
    {
        AlwaysOn = false,
        ConnectionString = world.Host.Schema.ConnectionString,
        ConfigureServices = services =>
        {
            services.RemoveAll<IWorktreeManager>();
            services.RemoveAll<IAgentProtocolAdapterFactory>();
            services.RemoveAll<ISessionRunnerClient>();
            services.AddSingleton<ISessionRunnerClient>(world.Runner);
            services.AddSingleton<ILandingGit>(world.Host.Fixture.Git);
            services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(world.Host.Fixture.Root, "trees") });
            services.AddSingleton(world.Runner);
            services.AddScoped<SourceLandingAdmission>();
            services.AddScoped<VerificationExecutionService>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<IAgentProtocolAdapterFactory>(capture);
            services.AddSingleton<DelegationWorkspaceResolver>();
        },
    };

    private static DateTime EligibleInterruptedAt() => DateTime.UtcNow.AddSeconds(-40);

    private static void StampParkedSentAttempt(SessionQueuedMessage queued, long baseline = 4)
    {
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 3;
        queued.DeliveryVerdict = null;
        queued.DeliveryVerdictAt = null;
        queued.LastDeliveryStartedAt = EligibleInterruptedAt();
        queued.LastDeliveryBaselineSequence = baseline;
    }

    private static async Task<CompletionNoteWorkHostedService> StartCompletionRecoveryAsync(BridgeQueueHarness h)
    {
        var hosted = new CompletionNoteWorkHostedService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            new CompletionNoteFlushQueue(),
            new SpecialistFailureQueue(),
            TimeProvider.System,
            NullLogger<CompletionNoteWorkHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        return hosted;
    }

    private static async Task UntilAsync(Func<Task<bool>> condition, string because, int seconds = 20)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!budget.Token.IsCancellationRequested)
        {
            if (await condition()) return;
            try { await Task.Delay(50, budget.Token); }
            catch (OperationCanceledException) { break; }
        }

        throw new TimeoutException(because);
    }

    private sealed class DeliveryCut(string cut) : LandDeliveryBoundary
    {
        public bool ReceiptSaveReached { get; private set; }

        public override bool DropWakeup(string boundary, Guid identity) =>
            cut == "lost-wakeup" && boundary == "completion";

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (cut == "receipt-before-save" && boundary == "receipt-before-save")
            {
                ReceiptSaveReached = true;
                return Task.FromException(new IOException("owned receipt save failure"));
            }

            return (cut, boundary) switch
            {
                ("before-enqueue", "before-enqueue") => Task.FromException(new IOException("owned enqueue failure")),
                ("queue-inserted", "queue-inserted") => Task.FromException(new IOException("owned queue-ack failure")),
                _ => Task.CompletedTask,
            };
        }
    }
}
