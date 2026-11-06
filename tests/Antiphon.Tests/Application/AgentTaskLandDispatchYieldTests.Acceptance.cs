using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskLandDispatchYieldTests
{
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("restart")]
    [Arguments("enqueue-failure")]
    public async Task C1581_hosted_land_and_dispatch_reach_complete_recipient_prompts(string arm)
    {
        await using var h = await StartAsync();
        var capture = new MutationDispatchTestsCapture();
        var delivery = new AcceptanceBoundary();
        await using var caller = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false, ConnectionString = h.Harness.Schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.RemoveAll<IWorktreeManager>();
                services.RemoveAll<IAgentProtocolAdapterFactory>();
                services.AddSingleton<ILandingGit>(h.Harness.Fixture.Git);
                services.AddDelegationWorktreeGraph(new GitSettings
                    { WorktreeBasePath = Path.Combine(h.Harness.Fixture.Root, "dispatch-trees") });
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new()
                    {
                        DefaultDefinition = "claude",
                        Definitions = { ["claude"] = new() { Kind = "ClaudeCode", Exe = "claude" } },
                    }));
                services.AddSingleton<IAgentProtocolAdapterFactory>(capture);
                services.AddSingleton(h.Waiters);
                services.AddSingleton<DelegationWorkspaceResolver>();
                services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                services.AddScoped<AgentTaskService>();
                services.AddScoped<AgentTaskDispatcher>();
                services.AddSingleton<CompletionNoteFlushQueue>();
                services.AddSingleton<LandDeliveryBoundary>(delivery);
                services.AddScoped<AgentTaskLandNotificationService>();
            },
        });
        capture.Configure = adapter =>
        {
            adapter.WithClaudeDialogObservation();
            adapter.RegisterOnStart = caller.Runtime;
            adapter.OnSubmitted = async submitted =>
            {
                var session = adapter.StartedSessionId.ShouldNotBeNull();
                await StoreAwareTranscript.WriteAsync(caller.Provider, session, async () =>
                {
                    await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.UserPrompt, submitted,
                        timestamp: DateTime.UtcNow, connectionString: caller.ConnectionString);
                    await BridgeQueueHarness.InsertEntryAsync(session, TranscriptKinds.TurnEnd,
                        stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: caller.ConnectionString);
                });
            };
        };
        h.Harness.Messages = caller.Queue;
        await using (var db = h.Harness.CreateContext())
        {
            await db.AgentTasks.Where(t => t.Id == h.Harness.Fixture.TaskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ParentSessionId, caller.SessionId).SetProperty(t => t.ReplyTo, AgentTaskReplyTo.Session));
        }
        var runnable = await AddWaiterAsync(h, AgentTaskRole.Code);
        var dependent = await AddWaiterAsync(h, AgentTaskRole.Investigate);
        await using (var db = h.Harness.CreateContext())
            await db.AgentTasks.Where(t => t.Id == runnable.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.WorktreeBaseRequestedRef, (h.Harness.Fixture.SourceRef)));
        h.Waiters.Register(await h.CommonAsync(), runnable.Id, RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());
        h.Waiters.Register(await h.CommonAsync(), dependent.Id, RepositoryLeasePurposes.Dispatch, h.Clock.GetUtcNow());

        // The first hosted pass must stand aside for the runnable sibling, without spending an attempt.
        await RunHostedPassAsync(h, LandRequestState.Held);
        (await h.RequestAsync()).Attempt.ShouldBe(0);
        (await h.RequestAsync()).HoldDetail.ShouldNotBeNull().ShouldContain(DelegationReportFormatter.Short(runnable.Id));
        await DispatchAndConfirmAsync(runnable.Id);
        h.Waiters.Clear(runnable.Id);

        if (arm == "restart")
        {
            await h.Harness.RestartServicesAsync(); // discard the volatile land queue; durable request replays
            h.Harness.Queue.IsActive(h.Harness.Fixture.TaskId).ShouldBeFalse();
        }
        var busy = arm is "busy" or "enqueue-failure";
        if (busy) await caller.MarkWorkingAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await RunHostedPassAsync(h, LandRequestState.Completed);
        await h.AssertLandedAsync();
        var request = await h.RequestAsync();
        request.Attempt.ShouldBe(1);
        request.IsPending.ShouldBeFalse();
        await DispatchAndConfirmAsync(dependent.Id);

        await using var observer = h.Harness.CreateContext();
        var note = await observer.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n =>
            n.TaskId == h.Harness.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
        note.RequestId.ShouldBe(request.Id);
        note.SourceEventId.ShouldBe(request.TerminalEventId.ShouldNotBeNull());
        if (arm == "enqueue-failure") delivery.Cut = "queue-inserted";
        await ScanNotificationsAsync();
        if (arm == "enqueue-failure")
        {
            delivery.CutReached.ShouldBeTrue();
            delivery.Cut = null;
            await observer.AgentTaskLandNotifications.Where(n => n.Id == note.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
            await ScanNotificationsAsync(); // new hosted worker/context recovers the committed keyed enqueue
        }
        await caller.Queue.FlushIfIdleAsync(caller.SessionId, default);
        if (busy)
        {
            caller.Adapter.SubmittedBodies.ShouldBeEmpty("busy recipient must keep the complete obligation owed");
            await caller.MarkIdleAsync(stopReason: TranscriptKinds.StopReasons.EndTurn);
            await caller.Queue.OnTurnEndAsync(caller.SessionId, default);
        }
        await ScanNotificationsAsync();
        var saved = await observer.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        var message = await observer.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceLandNotificationId == note.Id);
        saved.QueueMessageId.ShouldBe(message.Id);
        message.AgentSessionId.ShouldBe(caller.SessionId);
        message.Body.ShouldBe(note.Body);
        var prompt = await observer.TranscriptEntries.AsNoTracking().SingleAsync(t =>
            t.AgentSessionId == caller.SessionId && t.Sequence == saved.ConfirmingPromptSequence);
        prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt);
        PromptSubmissionMatch.IsCompleteIn(note.Body, prompt.Text!).ShouldBeTrue();
        caller.Adapter.SubmittedBodies.ShouldHaveSingleItem().ShouldBe(prompt.Text);
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Harness.Fixture.TaskId && e.IsLandTerminal)).ShouldBe(1);
        h.Harness.Fixture.Git.Trace.Count(a => a[0] == "push"
            && a.Any(value => value.EndsWith(":" + h.Harness.Fixture.TargetRef, StringComparison.Ordinal))).ShouldBe(1);

        async Task DispatchAndConfirmAsync(Guid id)
        {
            await using (var scope = caller.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(default);
            await caller.Provider.GetRequiredService<AgentSessionLaunchQueue>()
                .WaitForIdleAsync(TimeSpan.FromSeconds(30), default);
            await using var db = h.Harness.CreateContext();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == id);
            row.AgentSessionId.ShouldBe(task.AgentSessionId.ShouldNotBeNull());
            row.Body.ShouldContain(DelegationReportFormatter.TaskMarker(id));
            var prompts = await db.TranscriptEntries.AsNoTracking().Where(t =>
                t.AgentSessionId == task.AgentSessionId && t.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            prompts.Where(t => t.Text is not null && PromptSubmissionMatch.IsCompleteIn(row.Body, t.Text)).ShouldHaveSingleItem();
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == id && e.Type == AgentTaskEventType.Dispatched)).ShouldBe(1);
        }

        async Task ScanNotificationsAsync()
        {
            delivery.Scanned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var worker = new AgentTaskLandNotificationHostedService(caller.Provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AgentTaskLandNotificationHostedService>.Instance);
            await worker.StartAsync(default);
            try { await delivery.Scanned.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { await worker.StopAsync(default); }
        }
    }

    private static async Task RunHostedPassAsync(World h, LandRequestState expected)
    {
        var signal = new LandPassSignal(h);
        h.Harness.Events = signal;
        await h.Harness.SweepAsync(); // the normal sweep re-arms a previously attempted Held request
        using var worker = new AgentTaskLandHostedService(h.Harness.Queue,
            h.Harness.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentTaskLandHostedService>.Instance);
        await worker.StartAsync(default);
        try { (await signal.Finished.Task.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBe(expected); }
        finally { await worker.StopAsync(default); }
    }

    private sealed class LandPassSignal(World h) : IEventBus
    {
        public TaskCompletionSource<LandRequestState> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PublishToGroupAsync(string group, string name, object body, CancellationToken ct = default) => ObserveAsync();
        public Task PublishToAllAsync(string name, object body, CancellationToken ct = default) => ObserveAsync();
        private async Task ObserveAsync()
        {
            var request = await h.RequestAsync();
            if (!request.IsPending || request.State == LandRequestState.Held && request.LastEvaluatedAt == h.Clock.GetUtcNow().UtcDateTime)
                Finished.TrySetResult(request.State);
        }
    }

    private sealed class AcceptanceBoundary : LandDeliveryBoundary
    {
        public TaskCompletionSource Scanned { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Cut { get; set; }
        public bool CutReached { get; private set; }
        public override Task ReachedAsync(string boundary, Guid task, Guid identity, CancellationToken ct)
        {
            if (boundary == Cut) { CutReached = true; throw new IOException("owned acceptance enqueue interruption"); }
            if (boundary == "notification-scan") Scanned.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
