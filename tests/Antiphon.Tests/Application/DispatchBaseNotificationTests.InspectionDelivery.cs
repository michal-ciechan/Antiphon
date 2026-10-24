using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DispatchBaseNotificationTests
{
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("claim-committed")]
    [Arguments("projection-before-commit")]
    [Arguments("before-enqueue")]
    [Arguments("enqueue-fails")]
    [Arguments("queue-inserted-before-link")]
    [Arguments("lost-wakeup")]
    [Arguments("prompt-before-verdict")]
    [Arguments("receipt-before-save")]
    public async Task C1349_InspectionWarningsReachCaller(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-warning-receipt");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var boundary = new WarningCut(scenario);
        var git = new InspectionGit();
        var inspectionClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var slot = new DispatcherSiblingInspectionSchedulingTests.InspectionAdapterSlot();
        var sink = new WarningLaunchSink();
        var h = await OpenAsync();
        var parent = h.SessionId; var parentAgent = h.AgentId;
        await using var replacement = await ClaudeDialogFixture.CreateBridgeAsync(new()
        { AlwaysOn = true, PreserveDatabaseOnDispose = true, ConnectionString = schema.ConnectionString });
        Guid taskId;
        List<AgentTask> siblings;
        try
        {
            await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var card = await DispatchSiblingInspectionTests.CardAsync(seed);
                var a = await DispatchSiblingInspectionTests.SourceAsync(seed, repo, card.Id, "a", "master");
                var b = await DispatchSiblingInspectionTests.SourceAsync(seed, repo, card.Id, "b", "master");
                var alias = await DispatchSiblingInspectionTests.SourceAsync(seed, repo, card.Id, "alias", a.WorktreeBranch!, false);
                siblings = [a, b, alias]; foreach (var sibling in siblings) sibling.Status = AgentTaskStatus.Blocked;
                var task = DispatcherSiblingInspectionSchedulingTests.QueueTask(repo, card.Id);
                task.ParentSessionId = parent; task.ReplyTo = AgentTaskReplyTo.Session; taskId = task.Id;
                seed.AgentTasks.Add(task); await seed.SaveChangesAsync();
                await seed.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentKind, AgentKind.Raw));
            }
            boundary.TaskId = taskId;
            var worker = h.Provider.GetRequiredService<CardSiblingInspectionQueue>(); worker.Start();
            await using (var scope = h.Provider.CreateAsyncScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                await dispatcher.TickAsync(boundary.Token);
                await worker.WhenIdleAsync();
                if (scenario is "claim-committed" or "projection-before-commit")
                    await Should.ThrowAsync<OperationCanceledException>(() => dispatcher.TickAsync(boundary.Token));
                else await dispatcher.TickAsync(boundary.Token);
            }
            List<AgentTaskDispatchWarningIntent> originals;
            await using (var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                originals = await read.AgentTaskDispatchWarningIntents.AsNoTracking().Where(i => i.TaskId == taskId).ToListAsync();
                originals.Count.ShouldBe(2);
                originals.ShouldAllBe(i => i.ParentSessionId == parent && i.ReplyTo == AgentTaskReplyTo.Session);
                originals.ShouldContain(i => i.Detail.Contains("also covers 1 other", StringComparison.Ordinal));
                if (scenario is "claim-committed" or "projection-before-commit")
                {
                    boundary.Fired.ShouldBeTrue(); originals.ShouldAllBe(i => i.MaterializedAt == null);
                    (await read.AgentTaskLandNotifications.CountAsync(n => n.TaskId == taskId)).ShouldBe(0);
                    (await read.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
                }
            }
            if (scenario is not ("claim-committed" or "projection-before-commit"))
            {
                if (scenario == "busy") await h.MarkWorkingAsync();
                foreach (var intent in originals)
                {
                    try
                    {
                        await using var scope = h.Provider.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>()
                            .ReconcileAsync(intent.NotificationId, boundary.Token);
                    }
                    catch (OperationCanceledException) when (boundary.Fired) { break; }
                }
                if (scenario is "eligible" or "busy" or "prompt-before-verdict" or "receipt-before-save")
                {
                    try { await PumpTwoAsync(h, boundary.Token); }
                    catch (OperationCanceledException) when (boundary.Fired) { }
                    if (scenario == "receipt-before-save")
                    {
                        await using var scope = h.Provider.CreateAsyncScope();
                        await Should.ThrowAsync<OperationCanceledException>(() => scope.ServiceProvider
                            .GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(originals[0].NotificationId, boundary.Token));
                    }
                }
            }
            var originalQueues = new Dictionary<Guid, Guid>();
            var originalAttempts = new Dictionary<Guid, int>();
            DateTime? interruptedDue = null;
            await using (var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var rows = await read.SessionQueuedMessages.AsNoTracking().Where(q => q.SourceTaskId == taskId && q.SourceLandNotificationId != null).ToListAsync();
                foreach (var row in rows) originalQueues[row.SourceLandNotificationId!.Value] = row.Id;
                var prompts = await read.TranscriptEntries.Where(p => p.AgentSessionId == parent && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
                if (scenario == "busy")
                {
                    rows.Count.ShouldBe(2); rows.ShouldAllBe(r => r.Status == QueuedMessageStatus.Pending && r.DeliveryAttempts == 0);
                    prompts.ShouldBeEmpty();
                }
                if (scenario == "queue-inserted-before-link")
                {
                    rows.ShouldHaveSingleItem().DeliveryAttempts.ShouldBe(0);
                    (await read.AgentTaskLandNotifications.SingleAsync(n => n.Id == rows[0].SourceLandNotificationId)).QueueMessageId.ShouldBeNull();
                    prompts.ShouldBeEmpty();
                }
                if (scenario == "before-enqueue") { rows.ShouldBeEmpty(); prompts.ShouldBeEmpty(); }
                if (scenario == "lost-wakeup") { rows.Count.ShouldBe(2); prompts.ShouldBeEmpty(); boundary.Fired.ShouldBeTrue(); }
                if (scenario == "enqueue-fails")
                {
                    (await read.AgentTaskLandNotifications.Where(n => n.TaskId == taskId).ToListAsync())
                        .ShouldAllBe(n => n.LastErrorCode != null && n.LastErrorCode.Contains("IOException"));
                }
                if (scenario == "prompt-before-verdict")
                {
                    var interrupted = rows.Where(r => r.Status == QueuedMessageStatus.Sent && r.DeliveryVerdict == null).ToList();
                    interrupted.ShouldNotBeEmpty();
                    foreach (var row in interrupted)
                    {
                        originalAttempts[row.Id] = row.DeliveryAttempts;
                        interruptedDue = row.LastDeliveryStartedAt!.Value.AddSeconds(37);
                        prompts.ShouldContain(p => PromptSubmissionMatch.IsCompleteIn(row.Body, p.Text ?? ""));
                    }
                }
                if (scenario is not ("eligible" or "busy")) boundary.Fired.ShouldBeTrue(scenario);
                await read.AgentTasks.Where(t => t.Id == taskId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ParentSessionId, replacement.SessionId));
            }
            foreach (var sibling in siblings) await repo.GitAsync("update-ref", "refs/heads/" + sibling.WorktreeBranch!, "master");
            boundary.Disable();
            await h.DisposeAsync();
            h = await OpenAsync(parent, parentAgent);
            if (scenario == "busy") await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            if (scenario == "enqueue-fails") await Task.Delay(TimeSpan.FromSeconds(6));
            if (interruptedDue is DateTime due && due > DateTime.UtcNow) await Task.Delay(due - DateTime.UtcNow);
            await ScanAsync(h, boundary);
            if (scenario is "prompt-before-verdict" or "lost-wakeup")
            {
                await h.Queue.FlushStrandedQueuesAsync(default);
                await h.Queue.FlushStrandedQueuesAsync(default);
            }
            else if (scenario is "eligible" or "receipt-before-save") { }
            else if (scenario == "busy")
            {
                await h.Queue.OnTurnEndAsync(parent, default);
                await h.Queue.OnTurnEndAsync(parent, default);
            }
            else await PumpTwoAsync(h, default);
            // A second real scan confirms the delivered rows; it does not recompute warning payloads.
            await ScanAsync(h, boundary);
            await h.Queue.FlushStrandedQueuesAsync(default);
            await using var final = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var received = await final.TranscriptEntries.Where(p => p.AgentSessionId == parent && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            foreach (var original in originals)
            {
                var kept = await final.AgentTaskDispatchWarningIntents.SingleAsync(i => i.Id == original.Id);
                kept.Body.ShouldBe(original.Body); kept.ContentDigest.ShouldBe(original.ContentDigest);
                kept.ParentSessionId.ShouldBe(parent); kept.NotificationId.ShouldBe(original.NotificationId);
                var note = await final.AgentTaskLandNotifications.SingleAsync(n => n.Id == original.NotificationId);
                note.State.ShouldBe(LandNotificationState.Confirmed); note.ConfirmedAt.ShouldNotBeNull();
                var row = await final.SessionQueuedMessages.SingleAsync(q => q.SourceLandNotificationId == note.Id);
                row.AgentSessionId.ShouldBe(parent); row.ContentDigest.ShouldBe(original.ContentDigest);
                if (originalQueues.TryGetValue(note.Id, out var previous)) row.Id.ShouldBe(previous);
                if (originalAttempts.TryGetValue(row.Id, out var attempts))
                { row.DeliveryAttempts.ShouldBe(attempts); row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed); }
                received.Count(p => PromptSubmissionMatch.IsCompleteIn(original.Body, p.Text ?? "")).ShouldBe(1);
                var prompt = received.Single(p => PromptSubmissionMatch.IsCompleteIn(original.Body, p.Text ?? ""));
                if (row.LastDeliveryBaselineSequence is long floor) prompt.Sequence.ShouldBeGreaterThan(floor);
                else prompt.Timestamp.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(row.LastDeliveryStartedAt.ShouldNotBeNull());
            }
            received.Count.ShouldBe(2);
            (await final.TranscriptEntries.CountAsync(p => p.AgentSessionId == replacement.SessionId && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        }
        finally { boundary.Disable(); await h.DisposeAsync(); }

        async Task<BridgeQueueHarness> OpenAsync(Guid? session = null, Guid? agent = null)
        {
            var harness = await ClaudeDialogFixture.CreateBridgeAsync(new()
            {
                AlwaysOn = true, PreserveDatabaseOnDispose = true, ValidateScopes = true,
                ConnectionString = schema.ConnectionString, AttachSessionId = session, AttachAgentId = agent,
                Delegation = new DelegationSettings { MaxConcurrentTasks = 32, AllowedRoots = ["/", "C:\\"] },
                ConfigureServices = services =>
                {
                    DispatcherSiblingInspectionSchedulingTests.ConfigureInspectionDelivery(services, repo, git, inspectionClock, slot);
                    services.AddSingleton<LandDeliveryBoundary>(boundary); services.AddSingleton<IAgentTaskLaunchSink>(sink);
                },
            });
            slot.Adapter = harness.Adapter;
            return harness;
        }
    }

    private static async Task PumpTwoAsync(BridgeQueueHarness h, CancellationToken ct)
    {
        var flushes = h.Provider.GetRequiredService<CompletionNoteFlushQueue>();
        await using var wakes = flushes.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        for (var i = 0; i < 2; i++)
        {
            var next = wakes.MoveNextAsync();
            next.IsCompletedSuccessfully.ShouldBeTrue("the committed notifications must supply their real flush wakeup");
            (await next).ShouldBeTrue(); wakes.Current.ShouldBe(h.SessionId);
            await h.Queue.FlushIfIdleAsync(wakes.Current, ct);
            flushes.Complete(wakes.Current);
        }
    }
    private static async Task ScanAsync(BridgeQueueHarness h, WarningCut boundary)
    {
        var scanned = boundary.NewScan();
        using var host = new AgentTaskLandNotificationHostedService(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        await host.StartAsync(default);
        try { await scanned.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { await host.StopAsync(default); }
    }
    private sealed class WarningLaunchSink : IAgentTaskLaunchSink
    {
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) { }
    }
    private sealed class WarningCut(string scenario) : LandDeliveryBoundary
    {
        private readonly CancellationTokenSource _cut = new();
        private TaskCompletionSource _scan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _enabled = true;
        public Guid TaskId { get; set; }
        public bool Fired { get; private set; }
        public CancellationToken Token => _cut.Token;
        public void Disable() => _enabled = false;
        public Task NewScan() { _scan = new(TaskCreationOptions.RunContinuationsAsynchronously); return _scan.Task; }
        public override bool DropWakeup(string name, Guid id)
        {
            if (_enabled && scenario == "lost-wakeup") { Fired = true; return true; }
            return false;
        }
        public override Task ReachedAsync(string boundary, Guid task, Guid identity, CancellationToken ct)
        {
            if (boundary == "notification-scan") _scan.TrySetResult();
            if (!_enabled || task != TaskId) return Task.CompletedTask;
            var wanted = scenario switch
            {
                "claim-committed" => "dispatch-warning-claim-committed",
                "projection-before-commit" => "dispatch-warning-before-commit",
                "before-enqueue" or "enqueue-fails" => "before-enqueue",
                "queue-inserted-before-link" => "queue-inserted",
                "prompt-before-verdict" => "queue-before-verdict",
                "receipt-before-save" => "receipt-before-save",
                _ => "none",
            };
            if (boundary != wanted) return Task.CompletedTask;
            Fired = true;
            if (scenario == "enqueue-fails") throw new IOException("owned warning enqueue failure");
            _cut.Cancel(); throw new OperationCanceledException(_cut.Token);
        }
    }
}
