using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class SessionRuntimeActionRoutingTests
{
    [Test]
    [Arguments("review-bound")]
    [Arguments("task-bound")]
    [Arguments("parent-handoffs")]
    [Arguments("channel-bound")]
    [Arguments("prompt-window")]
    [Arguments("early-text")]
    [Arguments("trailing-text")]
    [Arguments("frontier-race")]
    public async Task C1259_buffered_turns_route_their_own_recipients_before_flush(string variant)
    {
        if (variant == "review-bound") { await VerifyReviewRoutingAsync(); return; }
        if (variant == "task-bound") { await VerifyTaskRoutingAsync(false); return; }
        if (variant == "parent-handoffs") { await VerifyParentRoutingAsync(); return; }
        var producer = new HeldRoutingProducer();
        await using var f = await RoutingFixture.CreateAsync(s => s.AddSingleton<IAntiphonMessagingProducer>(producer));
        var h = f.H;
        const string a = "[Telegram A] First question belongs to A";
        const string b = "[Telegram B] Second question belongs to B";
        await h.SeedChannelCorrelationAsync(a, "telegram:destination-a");
        await h.SeedChannelCorrelationAsync(b, "telegram:destination-b");
        var first = new List<SessionRunnerTranscriptEvent> { f.Entry("UserPrompt", a) };
        if (variant != "early-text") first.Add(f.Entry("AssistantText", "answer A"));
        if (variant == "prompt-window")
        {
            first.Add(f.Entry("QueuedUserPrompt", "in-turn injection must not steal A"));
            first.Add(f.Entry("AssistantText", "A after the in-turn injection"));
        }
        first.Add(f.Entry("TurnEnd"));
        await f.StreamAsync(first.ToArray());
        if (variant == "early-text")
        {
            await f.Actions.DrainPageAsync(h.SessionId, default);
            producer.Sent.ShouldBeEmpty();
            (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(2);
            await f.StreamAsync(f.Entry("AssistantText", "answer A"));
        }
        var drain = f.Actions.DrainPageAsync(h.SessionId, default);
        try
        {
            await producer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (variant == "trailing-text") await f.StreamAsync(f.Entry("AssistantText", "A trailing suffix"));
            await f.StreamAsync(f.Entry("UserPrompt", b), f.Entry("AssistantText", "answer B"), f.Entry("TurnEnd"));
            const string next = "Next queued question after all previously owed routing";
            var queued = await h.Queue.EnqueueAsync(h.SessionId, next, MessageSendMode.WhenIdle, default, deliverIfIdle: false);
            var expected = variant == "trailing-text" ? 3 : 2;
            h.Adapter.OnSubmitted = body =>
            {
                body.ShouldBe(next);
                producer.Sent.Count.ShouldBe(expected, "new input must follow routing through the newly committed frontier");
                f.Runner.Produce(f.Entry("UserPrompt", body));
                return Task.CompletedTask;
            };
            await using (var observer = h.CreateObserverContext())
            {
                (await observer.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId && t.Text == "answer B")).ShouldBe(1);
                producer.Sent.ShouldBeEmpty("the first recipient is still held while the second turn commits");
            }
            producer.Release.TrySetResult();
            await drain;
            producer.Sent.Select(r => (r.ConversationId, r.Text)).ShouldBe(variant == "trailing-text"
                ? new[] { ("destination-a", "answer A"), ("destination-a", "A trailing suffix"), ("destination-b", "answer B") }
                : new[] { ("destination-a", variant == "prompt-window" ? "answer A\n\nA after the in-turn injection" : "answer A"), ("destination-b", "answer B") });
            await using var verify = h.CreateObserverContext();
            var row = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Messages.Single().Id);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            row.DeliveryAttempts.ShouldBe(1);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId && t.Kind == "UserPrompt" && t.Text == next)).ShouldBe(1);
            await f.Actions.DrainPageAsync(h.SessionId, default);
            producer.Sent.Count.ShouldBe(expected);
            h.Adapter.SubmittedBodies.ShouldBe([next]);
            if (variant == "channel-bound")
            {
                const string errorPrompt = "[Telegram C] Interrupted question";
                await h.SeedChannelCorrelationAsync(errorPrompt, "telegram:destination-error");
                await f.StreamAsync(f.Entry("UserPrompt", errorPrompt),
                    f.Entry("AssistantText", "partial answer"),
                    f.Entry("AssistantText", "API Error: interrupted") with { IsApiError = true },
                    f.Entry("TurnEnd") with { IsApiError = true, StopReason = "error" });
                await f.Actions.DrainPageAsync(h.SessionId, default);
                producer.Sent.Count.ShouldBe(expected, "the whole mixed API-error turn is withheld");
                (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1);
            }
        }
        finally { producer.Release.TrySetResult(); await drain; }
    }

    [Test]
    [Arguments("empty-sync")]
    [Arguments("sent-refinement")]
    [Arguments("manual-compaction")]
    [Arguments("automatic-compaction")]
    [Arguments("blocked-exit")]
    public async Task C1259_compaction_refinement_and_status_cannot_block_ingestion(string variant)
    {
        if (variant == "empty-sync") { await VerifyTaskRoutingAsync(true); return; }
        await using var f = await RoutingFixture.CreateAsync(s => s.AddSingleton<CompactionRecoveryService>());
        var h = f.H;
        const string body = "The complete refinement or status request that must reach this recipient";
        Guid? queuedId = null;
        if (variant == "sent-refinement")
            queuedId = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: 2,
                status: QueuedMessageStatus.Sent, conversationKey: "refine:" + Guid.NewGuid());
        if (variant == "blocked-exit")
        {
            queuedId = await h.SeedPendingMessageAsync(body);
            await f.StreamAsync(f.Entry("UserPrompt", "still working before blocked status"));
        }
        if (variant.Contains("compaction"))
        {
            await using var db = h.CreateObserverContext();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.SystemPromptAppend, "Keep the workspace instructions."));
        }
        h.Adapter.OnSubmitted = text =>
        {
            f.Runner.Produce(f.Entry("UserPrompt", text));
            return Task.CompletedTask;
        };
        var gate = h.Queue.GetLock(h.SessionId);
        await gate.WaitAsync();
        Task? drain = null;
        try
        {
            if (variant == "blocked-exit")
                await h.Runtime.ObserveAgentStatusAsync(new(h.SessionId, "idle", "blocked", h.Now), default).WaitAsync(TimeSpan.FromSeconds(5));
            else
                await f.StreamAsync(variant == "sent-refinement" ? f.Entry("UserPrompt", body)
                    : f.Entry("CompactBoundary", variant == "manual-compaction" ? "Context compacted (manual)" : "Context compacted (auto)"));
            drain = f.Actions.DrainPageAsync(h.SessionId, default);
            var phase = variant == "sent-refinement" ? 2 : variant == "blocked-exit" ? 8 : 3;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = h.CreateObserverContext();
                return await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == h.SessionId && a.Phase == phase);
            });
            await f.StreamOtherSessionAsync();
            drain.IsCompleted.ShouldBeFalse("only the effect waits on the queue lock");
            h.Adapter.Inputs.ShouldBeEmpty();
            if (queuedId is { } id)
            {
                await using var db = h.CreateObserverContext();
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == id)).DeliveryVerdict.ShouldBeNull();
            }
        }
        finally { gate.Release(); if (drain is not null) await drain; }
        if (variant == "sent-refinement")
        {
            await using var db = h.CreateObserverContext();
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == queuedId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
            h.Adapter.Inputs.ShouldBeEmpty();
        }
        else if (variant == "automatic-compaction")
        {
            h.Adapter.Inputs.ShouldBeEmpty("automatic compaction must not request the manual narrow flush");
            await using var db = h.CreateObserverContext();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            row.RuntimeActionKey.ShouldEndWith(":recovery");
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBe(0);
        }
        else
        {
            if (variant == "blocked-exit")
            {
                (await h.Provider.GetRequiredService<SessionStateStore>().ReadAsync(h.SessionId, default)).Working.ShouldBeTrue();
                h.Adapter.Inputs.ShouldBeEmpty("status cannot turn committed Working into idle");
                await f.StreamAsync(f.Entry("TurnEnd"));
                await Task.Delay(TimeSpan.FromMilliseconds(1100));
                await f.Actions.RequestAsync(h.SessionId, "status", default);
                await f.Actions.DrainPageAsync(h.SessionId, default);
            }
            var expected = variant == "manual-compaction" ? ChannelPreamble.RecoveryNoteBody : body;
            h.Adapter.SubmittedBodies.ShouldBe([expected]);
            await using var db = h.CreateObserverContext();
            (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId && t.Kind == "UserPrompt" && t.Text == expected)).ShouldBe(1);
        }
        h.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionFinished");
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    [Arguments("absent")]
    [Arguments("wrong-body")]
    [Arguments("old-sequence")]
    [Arguments("old-generation")]
    [Arguments("old-time")]
    [Arguments("truncated")]
    public async Task C1259_missing_or_truncated_prompt_never_confirms(string variant)
    {
        await using var f = await RoutingFixture.CreateAsync();
        var h = f.H;
        var body = "C1259 exact requested body " + new string('x', 700);
        var cap = h.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification.MaxDeliveryAttempts;
        if (variant == "old-sequence") await f.StreamAsync(f.Entry("UserPrompt", body), f.Entry("TurnEnd"));
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: cap,
            baselineSequence: variant == "old-time" ? null : floor, lastDeliveryStartedAt: h.Now);
        if (variant == "old-generation") await f.StreamAsync(f.Entry("UserPrompt", body), f.Entry("TurnEnd"));
        await f.Actions.RequestAsync(h.SessionId, "status", default);
        if (variant == "old-generation")
        {
            await using var db = h.CreateObserverContext();
            var generation = await db.AgentSessions.Where(s => s.Id == h.SessionId).Select(s => s.StartedAt).SingleAsync();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, generation.AddMinutes(1)));
            // A complete receipt cannot give an old action authority in a replacement generation.
            // Keep only that admitted action; the positive current-generation request is separate.
            await f.Actions.DrainPageAsync(h.SessionId, default);
        }
        else
        {
            if (variant is "wrong-body" or "truncated" or "old-time")
            {
                var receipt = f.Entry("UserPrompt", variant == "wrong-body" ? "unrelated current body"
                    : variant == "truncated" ? body[..250] : body);
                if (variant == "old-time") receipt = receipt with { Timestamp = h.Clock.GetUtcNow().AddMinutes(-10) };
                await f.StreamAsync(receipt, f.Entry("TurnEnd"));
            }
            await h.Runtime.ObserveAgentStatusAsync(new(h.SessionId, "idle", "blocked", h.Now), default);
            await f.Actions.DrainPageAsync(h.SessionId, default);
        }
        await using (var db = h.CreateObserverContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryVerdict.ShouldBe(variant == "truncated" ? DeliveryVerdict.Truncated : null);
            row.DeliveryAttempts.ShouldBe(cap);
        }
        h.Adapter.Inputs.ShouldBeEmpty("screen/status/action completion cannot supply the missing complete prompt");
        if (variant == "truncated")
        {
            await f.Actions.RequestAsync(h.SessionId, "status", default);
            await f.Actions.DrainPageAsync(h.SessionId, default);
            h.Adapter.Inputs.ShouldBeEmpty("an incomplete identity stays parked without another body or Enter");
            return;
        }
        await f.StreamAsync(f.Entry("UserPrompt", body), f.Entry("TurnEnd"));
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        await f.Actions.RequestAsync(h.SessionId, "status", default);
        await f.Actions.DrainPageAsync(h.SessionId, default);
        await using var verify = h.CreateObserverContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        h.Adapter.Inputs.ShouldBeEmpty("a complete stored receipt confirms without retyping");
    }


    private static void AddTaskServices(IServiceCollection s)
    {
        s.AddSingleton<AgentTaskReplyService>();
        s.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.GetTempPath() });
    }

    private static async Task<AgentTask> SeedRoutingTaskAsync(RoutingFixture f, Guid? parent = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask { Id = id, RootTaskId = id, AgentSessionId = f.H.SessionId,
            Status = AgentTaskStatus.Working, Attempt = 1, Title = "bound routing", Role = AgentTaskRole.Docs,
            AgentId = f.H.AgentId, Workspace = WorkspaceMode.Shared, WorkingDirectory = f.H.TempRoot,
            CreatedAt = f.H.Now.AddMinutes(-2), DispatchedAt = f.H.Now.AddMinutes(-1),
            CommitOnSettle = CommitOnSettlePolicy.Never, ParentSessionId = parent,
            ReplyTo = AgentTaskReplyTo.Session };
        await using var db = f.H.CreateObserverContext();
        db.AgentTasks.Add(task); await db.SaveChangesAsync();
        return task;
    }

    private static async Task VerifyTaskRoutingAsync(bool emptySync)
    {
        await using var f = await RoutingFixture.CreateAsync(AddTaskServices);
        var task = await SeedRoutingTaskAsync(f);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = $"Original complete result needs a decision.\n[antiphon-report:{DelegationReportFormatter.Short(task.Id)} blocked]";
        if (emptySync)
        {
            // Pre-upgrade stored history has no transcript actions. Empty sync must still
            // admit catch-up settlement without replaying those historical UI rows.
            await f.H.InsertTurnAsync(marker, report);
            await f.H.Runtime.SyncTranscriptAsync(f.H.SessionId, default);
            await f.Actions.DrainPageAsync(f.H.SessionId, default);
            f.H.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionTranscript");
        }
        else
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = f.H.Provider.GetRequiredService<AgentTaskReplyService>();
            service.DelayAfterOpenTaskLoadedAsync = async (_, ct) =>
            { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
            await f.StreamAsync(f.Entry("UserPrompt", marker), f.Entry("AssistantText", report), f.Entry("TurnEnd"));
            var draining = f.Actions.DrainPageAsync(f.H.SessionId, default);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var unrelated = Guid.NewGuid();
                await f.StreamAsync(f.Entry("UserPrompt", DelegationReportFormatter.TaskMarker(unrelated)),
                    f.Entry("AssistantText", $"Wrong later result.\n[antiphon-report:{DelegationReportFormatter.Short(unrelated)} done]"), f.Entry("TurnEnd"));
                release.TrySetResult(); await draining;
            }
            finally { release.TrySetResult(); await draining; }
        }
        await using var verify = f.H.CreateObserverContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Blocked);
        settled.Result.ShouldBe(report, "the marked owning turn must survive a newer completed turn");
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
    }

    private static async Task VerifyReviewRoutingAsync()
    {
        var hold = new ReviewRoutingGate();
        await using var f = await RoutingFixture.CreateAsync(s =>
        {
            s.AddSingleton<ReviewReplyDispatcher>(); s.AddScoped<ReviewThreadService>(); s.AddScoped<AgentFilesService>();
        }, o => o.AddInterceptors(hold));
        using var scope = f.H.Provider.CreateScope();
        var reviews = scope.ServiceProvider.GetRequiredService<ReviewThreadService>();
        var first = (await reviews.CreateAsync(f.H.AgentId, new CreateReviewThreadRequest("first.md", 1, null, "First question", false), default))!;
        var second = (await reviews.CreateAsync(f.H.AgentId, new CreateReviewThreadRequest("second.md", 2, null, "Second question", false), default))!;
        await reviews.DispatchAsync(first.Id, default);
        await reviews.DispatchAsync(second.Id, default);
        f.H.Adapter.OnSubmitted = body => { f.Runner.Produce(f.Entry("UserPrompt", body)); return Task.CompletedTask; };
        await f.H.Queue.FlushIfIdleAsync(f.H.SessionId, default);
        await f.StreamAsync(f.Entry("AssistantText", "answer for first thread"), f.Entry("TurnEnd"));
        hold.Armed = true;
        var draining = f.Actions.DrainPageAsync(f.H.SessionId, default);
        try
        {
            await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await f.H.Queue.FlushIfIdleAsync(f.H.SessionId, default);
            await f.StreamAsync(f.Entry("AssistantText", "answer for second thread"), f.Entry("TurnEnd"));
            hold.Release.TrySetResult(); await draining;
        }
        finally { hold.Release.TrySetResult(); await draining; }
        await using var verify = f.H.CreateObserverContext();
        var comments = await verify.ReviewComments.Where(c => c.Author == ReviewCommentAuthor.Agent).ToListAsync();
        comments.Count.ShouldBe(2);
        comments.Single(c => c.ThreadId == first.Id).Body.ShouldBe("answer for first thread");
        comments.Single(c => c.ThreadId == second.Id).Body.ShouldBe("answer for second thread");
        (await verify.SessionQueuedMessages.Where(m => m.ReviewThreadId != null).ToListAsync())
            .ShouldAllBe(m => m.ReviewReplyConsumedAt != null && m.DeliveryVerdict == DeliveryVerdict.Delivered && m.ChannelReplySettledAt == null);
        f.H.Adapter.SubmittedBodies.Count.ShouldBe(2);
    }

    private static async Task VerifyParentRoutingAsync()
    {
        foreach (var busy in new[] { true, false })
        {
            await using var f = await RoutingFixture.CreateAsync(AddTaskServices);
            var parent = Guid.NewGuid();
            await using (var db = f.H.CreateObserverContext())
            {
                db.AgentSessions.Add(new AgentSession { Id = parent, DefinitionName = "fake", Cwd = f.H.TempRoot,
                    Status = SessionStatus.Running, StartedAt = f.H.Now, CreatedAt = f.H.Now });
                await db.SaveChangesAsync();
            }
            var adapter = new Antiphon.Tests.Agents.FakeAgentProtocolAdapter();
            f.H.Runtime.Register(parent, adapter);
            await f.StreamAsync(f.Entry("UserPrompt", "parent prior question") with { SessionId = parent, Sequence = 1 },
                f.Entry("TurnEnd") with { SessionId = parent, Sequence = 2 });
            if (busy) await f.StreamAsync(f.Entry("UserPrompt", "parent busy work") with { SessionId = parent });
            var task = await SeedRoutingTaskAsync(f, parent);
            var report = $"Complete report with worker table | one | two | and a caveat.\n--- next stage ---\nnext: review\nhandoff: inspect complete result\n[antiphon-report:{DelegationReportFormatter.Short(task.Id)} blocked]";
            await f.StreamAsync(f.Entry("UserPrompt", DelegationReportFormatter.TaskMarker(task.Id)),
                f.Entry("AssistantText", report), f.Entry("TurnEnd"));
            await f.Actions.DrainPageAsync(f.H.SessionId, default);
            await using (var db = f.H.CreateObserverContext())
            {
                (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).Result.ShouldBe(report);
                (await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == task.Id)).Body.ShouldContain(report);
            }
            adapter.OnSubmitted = body =>
            {
                body.ShouldContain(report);
                f.Runner.Produce(f.Entry("UserPrompt", body) with { SessionId = parent });
                return Task.CompletedTask;
            };
            await f.H.Queue.FlushIfIdleAsync(parent, default);
            if (busy)
            {
                adapter.Inputs.ShouldBeEmpty("completion must wait for the parent's actual idle boundary");
                await f.StreamAsync(f.Entry("TurnEnd") with { SessionId = parent });
                await f.Actions.DrainPageAsync(parent, default);
            }
            await using var verify = f.H.CreateObserverContext();
            var queued = await verify.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == task.Id);
            queued.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            queued.DeliveryAttempts.ShouldBe(1);
            (await verify.TranscriptEntries.Where(t => t.AgentSessionId == parent && t.Kind == "UserPrompt"
                && t.Text == queued.Body).CountAsync()).ShouldBe(1);
            adapter.SubmittedBodies.ShouldBe([queued.Body]);
        }
    }

    private sealed class ReviewRoutingGate : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<ReviewComment>().Any(e => e.State == EntityState.Added))
            { Armed = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
    }

    private sealed class HeldRoutingProducer : IAntiphonMessagingProducer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ChannelReply> Sent { get; } = [];
        public async Task SendAsync(ChannelReply reply, CancellationToken ct = default)
        {
            if (Sent.Count == 0) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            Sent.Add(reply);
        }
    }

    private sealed class RoutingFixture : IAsyncDisposable
    {
        public required BridgeQueueHarness H;
        public required ScriptedSessionRunnerClient Runner;
        public required SessionRunnerEventPump Pump;
        public required IsolatedTestSchema Schema;
        private long _sequence = 2;
        public SessionRuntimeActionCoordinator Actions => H.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        public static async Task<RoutingFixture> CreateAsync(Action<IServiceCollection>? configure = null,
            Action<DbContextOptionsBuilder>? configureDb = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var runner = new ScriptedSessionRunnerClient();
            var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                ConfigureDbContext = configureDb,
                Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
                ConfigureServices = s =>
                {
                    s.AddSingleton<ISessionRunnerClient>(runner);
                    configure?.Invoke(s);
                }
            });
            await h.InsertTurnAsync("previous completed question", "previous completed answer");
            var pump = new SessionRunnerEventPump(h.Provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new SessionRunnerSettings { Enabled = true }), NullLogger<SessionRunnerEventPump>.Instance, h.Clock);
            await pump.StartAsync(default);
            await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var fixture = new RoutingFixture { H = h, Runner = runner, Pump = pump, Schema = schema };
            fixture._sequence = await h.CurrentTranscriptMaxSequenceAsync();
            return fixture;
        }
        public SessionRunnerTranscriptEvent Entry(string kind, string? text = null) =>
            new(H.SessionId, Interlocked.Increment(ref _sequence), kind, Guid.NewGuid().ToString(), null,
                H.Clock.GetUtcNow(), kind == "UserPrompt" ? "user" : "assistant", text, null, null, null, null,
                kind == "TurnEnd" ? "end_turn" : null);
        public async Task StreamAsync(params SessionRunnerTranscriptEvent[] entries)
        {
            foreach (var entry in entries) Runner.Produce(entry);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = H.CreateObserverContext();
                return await db.TranscriptEntries.AnyAsync(t => t.Uuid == entries[^1].Uuid);
            });
        }
        public async Task StreamOtherSessionAsync()
        {
            var id = Guid.NewGuid();
            await using (var db = H.CreateObserverContext())
            {
                db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "fake", Cwd = H.TempRoot,
                    Status = SessionStatus.Running, StartedAt = H.Now, CreatedAt = H.Now });
                await db.SaveChangesAsync();
            }
            var rows = new[] { Entry("UserPrompt", "B prompt") with { SessionId = id, Sequence = 1 },
                Entry("AssistantText", "B answer") with { SessionId = id, Sequence = 2 },
                Entry("TurnEnd") with { SessionId = id, Sequence = 3 } };
            await StreamAsync(rows);
            await using var verify = H.CreateObserverContext();
            (await verify.TranscriptEntries.Where(t => t.AgentSessionId == id).OrderBy(t => t.Sequence)
                .Select(t => t.Kind).ToListAsync()).ShouldBe(["UserPrompt", "AssistantText", "TurnEnd"]);
        }
        public async ValueTask DisposeAsync()
        {
            await Pump.StopAsync(default); Pump.Dispose();
            await H.DisposeAsync(); await Schema.DisposeAsync();
        }
    }
}
