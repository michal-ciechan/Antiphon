using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        await using var bridge = await BridgeQueueHarness.CreateAsync(DispatchOptions(world));
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Queued;
            task.AgentId = null;
            task.AgentSessionId = null;
            task.AgentKind = AgentKind.Raw;
            await db.SaveChangesAsync();
        }
        var (sessionId, queued) = await DispatchWorkerBriefAsync(world, bridge);
        await ConfirmQueuedReceiptAsync(world.Host.Schema.ConnectionString, bridge, queued, sessionId, busy: false);
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
    [Test] public Task C478_G149_CompletionPersist() => SettlementCrashAsync("before-submit", false);
    [Test] public Task C478_G150_CompletionEnqueue() => SettlementCrashAsync("before-submit", true);
    [Test] public Task C478_G151_CompletionQueueKey() => SettlementCrashAsync("after-prompt", true);
    [Test] public Task C478_G152_CompletionWakeup() => SettlementCrashAsync("before-submit", false);
    [Test] public Task C478_G153_CompletionBusy() => SettlementCrashAsync("before-submit", true);
    [Test] public Task C478_G154_CompletionReceipt() => SettlementCrashAsync("after-prompt", false);
    [Test] public Task C478_G155_CompletionIdentity() => CompletionWrongIdentityAsync();
    [Test] public Task C478_G156_CompletionRestore() => SettlementCrashAsync("after-receipt", true);
    [Test] public Task C478_G157_LandNotReport() => LandDoesNotSatisfyCompletionNoteAsync();
    [Test] public Task C478_G158_CompletionCompleteness() => SettlementCrashAsync("after-prompt", false);
    [Test] public Task C478_G159_CompletionFreshness() => SettlementCrashAsync("after-receipt", false);
    [Test] public Task C478_G160_CompletionSession() => CompletionWrongSessionAsync();
    [Test] public Task C478_G161_CompletionSpill() => C478_V09c_SettledMutationToCaller();

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
        var cutService = new AgentTaskLandNotificationService(seeded, h.Queue, new CompletionNoteFlushQueue(), h.Runtime,
            cutClock, new DeliveryCut(cut));
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
        await ConfirmLandReceiptAsync(land.Schema.ConnectionString, h, note, busy,
            alreadySubmitted: cut is "receipt-before-save" or "after-receipt" or "lost-wakeup");
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
        await using var h = await BridgeQueueHarness.CreateAsync(DispatchOptions(world));
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Status = AgentTaskStatus.Queued;
            task.AgentId = null;
            task.AgentSessionId = null;
            task.AgentKind = AgentKind.Raw;
            await db.SaveChangesAsync();
        }
        var (session, queued) = await DispatchWorkerBriefAsync(world, h);
        await ConfirmQueuedReceiptAsync(world.Host.Schema.ConnectionString, h, queued, session, busy, cut);
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
        if (busy)
        {
            h.Adapter.Inputs.ShouldBeEmpty();
            return;
        }
        if (!alreadySubmitted && queued.Status != QueuedMessageStatus.Sent)
        {
            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            h.Adapter.SubmittedBodies.ShouldContain(queued.Body);
        }
        var typed = h.Adapter.Inputs.Count;
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
        SessionQueuedMessage queued, Guid sessionId, bool busy, string cut = "after-receipt")
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        var start = h.Adapter.Inputs.Count;
        if (cut == "before-submit")
        {
            if (busy) h.Adapter.Inputs.Count.ShouldBe(start);
            else
            {
                await h.Queue.OnTurnEndAsync(sessionId, CancellationToken.None);
                h.Adapter.SubmittedBodies.ShouldContain(queued.Body);
            }
            return;
        }

        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        queued.LastDeliveryBaselineSequence = 4;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 4,
            Kind = TranscriptKinds.AssistantText, Text = "baseline", CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
        });
        if (cut is "after-prompt" or "after-receipt")
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 5,
                Kind = TranscriptKinds.UserPrompt, Text = queued.Body, CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        var typedAfter = h.Adapter.Inputs.Count;
        await using var recovered = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        await h.Queue.OnTurnEndAsync(sessionId, CancellationToken.None);
        await h.Queue.OnTurnEndAsync(sessionId, CancellationToken.None);
        (await recovered.TranscriptEntries.CountAsync(p => p.AgentSessionId == sessionId && p.Kind == TranscriptKinds.UserPrompt))
            .ShouldBe(cut is "after-prompt" or "after-receipt" ? 1 : 0);
        h.Adapter.Inputs.Count.ShouldBe(typedAfter);
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
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 4;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 5, Kind = TranscriptKinds.UserPrompt,
            Text = body.Replace(taskId.ToString("N"), Guid.NewGuid().ToString("N")),
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var typed = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.Inputs.Count.ShouldBe(typed);
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
        queued.Status = QueuedMessageStatus.Sent;
        queued.DeliveryAttempts = 1;
        queued.LastDeliveryBaselineSequence = 4;
        queued.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
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

    private static async Task<(Guid SessionId, SessionQueuedMessage Queued)> DispatchWorkerBriefAsync(
        PostLandMutationWorld world, BridgeQueueHarness h)
    {
        var sessionId = Guid.NewGuid();
        await using (var db = world.Host.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, Status = SessionStatus.Running, Cwd = task.WorktreePath!,
                AgentKind = AgentKind.Raw, SessionBackend = SessionBackend.PtyHost,
                StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            task.AgentSessionId = sessionId;
            task.Status = AgentTaskStatus.Dispatched;
            task.AgentKind = AgentKind.Raw;
            await db.SaveChangesAsync();
            var settings = h.Provider.GetRequiredService<IOptions<DelegationSettings>>().Value;
            var brief = AgentTaskDispatcher.FitBriefForTyping(task, settings, agentKind: AgentKind.Raw);
            var durable = brief;
            if (!durable.Contains("SourceLanding", StringComparison.Ordinal))
            {
                brief.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
                var spill = Path.Combine(task.WorkingDirectory!, ".antiphon",
                    $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
                File.Exists(spill).ShouldBeTrue();
                durable = await File.ReadAllTextAsync(spill);
            }
            durable.ShouldContain("SourceLanding");
            durable.ShouldContain(world.Operation.ToString("D"));
            durable.ShouldContain(world.Host.Fixture.SeedSha);
            h.Runtime.Register(sessionId, h.Adapter);
            await h.Queue.EnqueueAsync(sessionId, brief, MessageSendMode.WhenIdle, CancellationToken.None,
                QueuedMessageOrigin.Delegation, executionDeadlineAt: task.ExecutionDeadlineAt,
                executionTaskId: task.Id, deliverIfIdle: false);
        }
        await using var observer = world.Host.CreateContext();
        var queued = await observer.SessionQueuedMessages.SingleAsync(m => m.ExecutionTaskId == world.TaskId);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        return (sessionId, queued);
    }

    private static BridgeQueueHarness.HarnessOptions DispatchOptions(PostLandMutationWorld world) => new()
    {
        AlwaysOn = false,
        ConnectionString = world.Host.Schema.ConnectionString,
        ConfigureServices = services =>
        {
            services.RemoveAll<IWorktreeManager>();
            services.RemoveAll<IAgentProtocolAdapterFactory>();
            services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(world.Host.Fixture.Root, "trees") });
            services.AddSingleton<ILandingGit>(world.Host.Fixture.Git);
            services.AddSingleton(world.Runner);
            services.AddScoped<SourceLandingAdmission>();
            services.AddScoped<VerificationExecutionService>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<IAgentProtocolAdapterFactory>(new MutationDispatchTestsCapture());
            services.AddSingleton<DelegationWorkspaceResolver>();
        },
    };

    private sealed class DeliveryCut(string cut) : LandDeliveryBoundary
    {
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            (cut, boundary) switch
            {
                ("before-enqueue", "before-enqueue") => Task.FromException(new IOException("owned enqueue failure")),
                ("queue-inserted", "queue-inserted") => Task.FromException(new IOException("owned queue-ack failure")),
                ("receipt-before-save", "receipt-before-save") => Task.FromException(new IOException("owned receipt save failure")),
                _ => Task.CompletedTask,
            };
    }
}
