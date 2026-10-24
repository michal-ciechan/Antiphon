using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// The remaining routing variants are deliberately not represented by empty cases.
// The continuation evidence lists the still-open matrices.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class SessionRuntimeActionRoutingTests
{
    [Test]
    [Arguments("working")]
    [Arguments("herdr-blocked")]
    [Arguments("rules-held")]
    [Arguments("generation-changed")]
    [Arguments("task-attempt-changed")]
    [Arguments("action-during-flush")]
    public async Task C1259_new_working_or_blocked_state_defers_old_idle_action(string variant)
    {
        if (variant == "task-attempt-changed")
        {
            await SessionRuntimeActionRecoveryTests.VerifyTaskAttemptReplacementAsync(false);
            await SessionRuntimeActionRecoveryTests.VerifyTaskAttemptReplacementAsync(true);
            return;
        }
        if (variant == "action-during-flush") { await VerifyActionDuringFlushAsync(); return; }
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedSessionRunnerClient
        {
            Capabilities = new("test", "test", "test", false, SessionBackends: [SessionBackends.Herdr])
        };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton<ISessionRunnerClient>(runner);
                s.AddSingleton<PtyDeliveryProfile>();
                s.AddSingleton<SessionDeliveryProfile>();
            }
        });
        await h.InsertTurnAsync("previous prompt", "previous answer");
        if (variant == "herdr-blocked")
        {
            await using var db = h.CreateObserverContext();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.SessionBackend, SessionBackend.Herdr));
        }
        const string body = "C1259 queued original first line\nC1259 complete second line";
        var queued = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, default, deliverIfIdle: false);
        var messageId = queued.Messages.Single().Id;
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await actions.RequestAsync(h.SessionId, "status", default);
        Guid actionId;
        DateTime originalGeneration;
        await using (var db = h.CreateObserverContext())
        {
            var action = await db.SessionRuntimeActions.SingleAsync();
            actionId = action.Id;
            originalGeneration = action.AcceptedGeneration!.Value;
        }
        long sequence = 10;
        SessionRunnerTranscriptEvent Entry(string kind, string? text) => new(h.SessionId,
            Interlocked.Increment(ref sequence), kind, Guid.NewGuid().ToString(), null, h.Clock.GetUtcNow(),
            kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text, null, null, null, null,
            kind == TranscriptKinds.TurnEnd ? "end_turn" : null);
        async Task PersistAsync(SessionRunnerTranscriptEvent entry)
        {
            runner.Produce(entry);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var observer = h.CreateObserverContext();
                return await observer.TranscriptEntries.AnyAsync(t => t.Uuid == entry.Uuid);
            });
        }
        using var pump = new SessionRunnerEventPump(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }), NullLogger<SessionRunnerEventPump>.Instance, h.Clock);
        await pump.StartAsync(default);
        try
        {
            await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (variant == "working")
            {
                await PersistAsync(Entry(TranscriptKinds.UserPrompt, "new working turn after admission"));
                (await h.Provider.GetRequiredService<SessionStateStore>().ReadAsync(h.SessionId, default)).Working.ShouldBeTrue();
            }
            if (variant == "herdr-blocked") h.Runtime.SetTestAgentStatus(h.SessionId, "blocked");
            if (variant == "rules-held")
            {
                await using var db = h.CreateObserverContext();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.GrokRulesState, GrokRulesState.Pending));
            }
            if (variant == "generation-changed")
            {
                var nextGeneration = originalGeneration.AddMinutes(1);
                await using var db = h.CreateObserverContext();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, nextGeneration));
                h.Runtime.SetTestAcceptedStartedAt(h.SessionId, nextGeneration);
            }
            await actions.DrainPageAsync(h.SessionId, default);
            h.Adapter.Inputs.ShouldBeEmpty("the admitted idle intent must recheck authoritative current eligibility");
            await using (var db = h.CreateObserverContext())
            {
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
                row.DeliveryAttempts.ShouldBe(0);
                row.DeliveryVerdict.ShouldBeNull();
                var retained = await db.SessionRuntimeActions.SingleOrDefaultAsync(a => a.Id == actionId);
                if (variant == "generation-changed") retained.ShouldBeNull("stale generation cannot be rebound");
                else { retained.ShouldNotBeNull(); retained.Phase.ShouldBe(SessionRuntimeActionCoordinator.QueuePhase); }
            }
            if (variant == "working") await PersistAsync(Entry(TranscriptKinds.TurnEnd, null));
            if (variant == "herdr-blocked") h.Runtime.SetTestAgentStatus(h.SessionId, "idle");
            if (variant == "rules-held")
            {
                await using var db = h.CreateObserverContext();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.GrokRulesState, GrokRulesState.Ready));
            }
            var receipt = Entry(TranscriptKinds.UserPrompt, body);
            h.Adapter.OnSubmitted = sent => { sent.ShouldBe(body); runner.Produce(receipt); return Task.CompletedTask; };
            // This is the existing one-second deferred-action schedule, not an extended
            // delivery timeout. The next request is captured in the current generation.
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            await actions.RequestAsync(h.SessionId, "status", default);
            await actions.DrainPageAsync(h.SessionId, default);
            await using (var verify = h.CreateObserverContext())
            {
                var row = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
                row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
                row.DeliveryAttempts.ShouldBe(1);
                (await verify.TranscriptEntries.SingleAsync(t => t.Uuid == receipt.Uuid)).Text.ShouldBe(body);
            }
            h.Adapter.SubmittedBodies.ShouldBe([body]);
            h.Adapter.Killed.ShouldBeFalse();
            if (variant == "working")
            {
                var cap = h.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification.MaxDeliveryAttempts;
                var capped = await h.SeedPendingMessageAsync("C1259 parked body must never be typed again", deliveryAttempts: cap,
                    baselineSequence: receipt.Sequence);
                await PersistAsync(Entry(TranscriptKinds.TurnEnd, null));
                await Task.Delay(TimeSpan.FromMilliseconds(1100));
                await actions.RequestAsync(h.SessionId, "status", default);
                await actions.DrainPageAsync(h.SessionId, default);
                h.Adapter.SubmittedBodies.ShouldBe([body], "eligibility must not bypass the delivery attempt cap");
                await using var observer = h.CreateObserverContext();
                var parked = await observer.SessionQueuedMessages.SingleAsync(m => m.Id == capped);
                parked.Status.ShouldBe(QueuedMessageStatus.Pending);
                parked.DeliveryAttempts.ShouldBe(cap);
            }
        }
        finally { await pump.StopAsync(default); }
    }

    private static async Task VerifyActionDuringFlushAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedSessionRunnerClient();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton<ISessionRunnerClient>(runner)
        });
        await h.InsertTurnAsync("previous prompt", "previous answer");
        const string first = "C1259 first complete queued body";
        const string second = "C1259 second complete queued body";
        await h.Queue.EnqueueAsync(h.SessionId, first, MessageSendMode.WhenIdle, default, deliverIfIdle: false);
        await h.Queue.EnqueueAsync(h.SessionId, second, MessageSendMode.WhenIdle, default, deliverIfIdle: false);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await actions.RequestAsync(h.SessionId, "status", default);
        long sequence = 10;
        SessionRunnerTranscriptEvent Entry(string kind, string? text) => new(h.SessionId,
            Interlocked.Increment(ref sequence), kind, Guid.NewGuid().ToString(), null, h.Clock.GetUtcNow(),
            kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text, null, null, null, null,
            kind == TranscriptKinds.TurnEnd ? "end_turn" : null);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Adapter.OnSubmitted = async body =>
        {
            if (body == first) { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            else body.ShouldBe(second);
            runner.Produce(Entry(TranscriptKinds.UserPrompt, body));
        };
        using var pump = new SessionRunnerEventPump(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }), NullLogger<SessionRunnerEventPump>.Instance, h.Clock);
        await pump.StartAsync(default);
        Task? draining = null;
        try
        {
            await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
            draining = actions.DrainPageAsync(h.SessionId, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var during = Entry(TranscriptKinds.TurnEnd, null);
            runner.Produce(during);
            Guid newAction = Guid.Empty;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var observer = h.CreateObserverContext();
                var row = await observer.TranscriptEntries.SingleOrDefaultAsync(t => t.Uuid == during.Uuid);
                if (row is null) return false;
                newAction = await observer.SessionRuntimeActions.Where(a => a.TranscriptEntryId == row.Id).Select(a => a.Id).SingleAsync();
                return true;
            });
            draining.IsCompleted.ShouldBeFalse();
            release.TrySetResult();
            await draining;
            await using (var observer = h.CreateObserverContext())
            {
                (await observer.SessionRuntimeActions.SingleAsync(a => a.Id == newAction)).Phase.ShouldBe(SessionRuntimeActionCoordinator.QueuePhase);
                var pending = await observer.SessionQueuedMessages.SingleAsync(m => m.Body == second);
                pending.Status.ShouldBe(QueuedMessageStatus.Pending);
                pending.DeliveryAttempts.ShouldBe(0);
            }
            h.Adapter.SubmittedBodies.ShouldBe([first]);
            var finished = Entry(TranscriptKinds.TurnEnd, null);
            runner.Produce(finished);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var observer = h.CreateObserverContext();
                return await observer.TranscriptEntries.AnyAsync(t => t.Uuid == finished.Uuid);
            });
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            await actions.DrainPageAsync(h.SessionId, default);
            h.Adapter.SubmittedBodies.ShouldBe([first, second]);
            // The newly submitted second prompt is now Working. Complete that actual
            // turn before requiring every retained idle intent to be acknowledged.
            var secondFinished = Entry(TranscriptKinds.TurnEnd, null);
            runner.Produce(secondFinished);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var observer = h.CreateObserverContext();
                return await observer.TranscriptEntries.AnyAsync(t => t.Uuid == secondFinished.Uuid);
            });
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            await actions.DrainPageAsync(h.SessionId, default);
            h.Adapter.SubmittedBodies.ShouldBe([first, second]);
            await using var verify = h.CreateObserverContext();
            (await verify.SessionRuntimeActions.AnyAsync(a => a.Id == newAction)).ShouldBeFalse();
            foreach (var body in new[] { first, second })
            {
                var queue = await verify.SessionQueuedMessages.SingleAsync(m => m.Body == body);
                queue.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
                queue.DeliveryAttempts.ShouldBe(1);
                (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)).ShouldBe(1);
            }
        }
        finally
        {
            release.TrySetResult();
            if (draining is not null) await draining;
            await pump.StopAsync(default);
        }
    }

    [Test]
    [Arguments("map-free-restart")]
    [Arguments("forged-target")]
    [Arguments("wrong-session")]
    [Arguments("wrong-origin")]
    [Arguments("incomplete-body")]
    [Arguments("atomic-comment")]
    [Arguments("ttl")]
    [Arguments("api-error")]
    [Arguments("legacy-upgrade")]
    [Arguments("enqueue-failure")]
    public async Task C1259_review_recovery_uses_authorized_queue_identity_once(string variant)
    {
        if (variant == "enqueue-failure") await VerifyReviewHandoffCutsAsync();
        if (variant == "atomic-comment") await VerifyCommittedReviewBeforeActionAckAsync();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedSessionRunnerClient();
        var fault = new CommentCommitFault();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureDbContext = o => o.AddInterceptors(fault),
            ConfigureServices = s =>
            {
                s.AddSingleton<ISessionRunnerClient>(runner);
                s.AddSingleton<ReviewReplyDispatcher>();
                s.AddScoped<ReviewThreadService>();
                s.AddScoped<AgentFilesService>();
            }
        });
        // Seed only pre-existing idle history. Every prompt/answer under test enters
        // through the hosted event pump, including the queue's confirming receipt.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "earlier completed work");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        using var reviewScope = h.Provider.CreateScope();
        var service = reviewScope.ServiceProvider.GetRequiredService<ReviewThreadService>();
        var thread = await service.CreateAsync(h.AgentId,
            new CreateReviewThreadRequest("notes/report.md", 12, "a quoted line", "Please check the complete figure.", false), default);
        thread.ShouldNotBeNull();
        var decoy = await service.CreateAsync(h.AgentId,
            new CreateReviewThreadRequest("notes/other.md", 2, null, "A different question", false), default);
        decoy.ShouldNotBeNull();
        if (variant == "enqueue-failure")
        {
            fault.QueueArmed = true;
            await Should.ThrowAsync<IOException>(() => service.DispatchAsync(thread.Id, default));
            fault.Hits.ShouldBe(1, "the real review queue admission save must fail");
            await using var failed = h.CreateObserverContext();
            (await failed.SessionQueuedMessages.CountAsync()).ShouldBe(0);
            (await failed.SessionRuntimeActions.CountAsync()).ShouldBe(0);
            (await failed.ReviewThreads.SingleAsync(t => t.Id == thread.Id)).Status.ShouldBe(ReviewThreadStatus.Open);
        }
        if (variant == "legacy-upgrade")
        {
            (await service.DispatchAsync(thread.Id, default)).ShouldBeTrue();
            Guid legacyId;
            await using (var db = h.CreateObserverContext())
            {
                legacyId = (await db.SessionQueuedMessages.SingleAsync()).Id;
                var previous = db.Database.GetMigrations().TakeWhile(m => m != "20261015181237_SessionRuntimeActions").Last();
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(previous);
                await migrator.MigrateAsync();
            }
            await using (var upgraded = h.CreateObserverContext())
            {
                var legacy = await upgraded.SessionQueuedMessages.SingleAsync(m => m.Id == legacyId);
                legacy.ReviewThreadId.ShouldBeNull("the real upgrade cannot infer authority from the legacy key");
                legacy.ReviewReplyConsumedAt.ShouldBeNull();
                (await upgraded.SessionRuntimeActions.CountAsync()).ShouldBe(0, "migration cannot replay historical transcript effects");
                (await upgraded.ReviewComments.CountAsync(c => c.Author == ReviewCommentAuthor.Agent)).ShouldBe(0);
                // Explicit redispatch retires the unknown legacy request, then uses the real
                // producer to create new durable authority. No target is backfilled.
                legacy.Status = QueuedMessageStatus.Canceled;
                await upgraded.SaveChangesAsync();
            }
        }
        (await service.DispatchAsync(thread.Id, default)).ShouldBeTrue();
        SessionQueuedMessage source;
        await using (var db = h.CreateObserverContext())
        {
            source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ReviewThreadId == thread.Id);
            source.Origin.ShouldBe(QueuedMessageOrigin.Channel);
            source.ConversationKey.ShouldBe($"review:{thread.Id}");
            source.Status.ShouldBe(QueuedMessageStatus.Pending);
            (await db.ReviewThreads.SingleAsync(t => t.Id == thread.Id)).Status.ShouldBe(ReviewThreadStatus.AwaitingAgent);
        }
        long sequence = 2;
        if (variant == "incomplete-body")
        {
            source.Body.Length.ShouldBeGreaterThan(250);
            PromptSubmissionMatch.IsConfirmedBy(source.Body, source.Body[..250]).ShouldBeTrue("the clipped receipt must retain identity");
            PromptSubmissionMatch.IsCompleteIn(source.Body, source.Body[..250]).ShouldBeFalse();
        }
        SessionRunnerTranscriptEvent Entry(string kind, string? text, Guid? session = null, bool error = false) =>
            new(session ?? h.SessionId, Interlocked.Increment(ref sequence), kind, Guid.NewGuid().ToString(), null,
                h.Clock.GetUtcNow(), kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text,
                null, null, null, null, kind == TranscriptKinds.TurnEnd ? "end_turn" : null, IsApiError: error);
        h.Adapter.OnSubmitted = body =>
        {
            body.ShouldBe(source.Body);
            runner.Produce(Entry(TranscriptKinds.UserPrompt,
                variant == "incomplete-body" ? body[..250] : body));
            return Task.CompletedTask;
        };
        using var pump = new SessionRunnerEventPump(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionRunnerSettings { Enabled = true }), NullLogger<SessionRunnerEventPump>.Instance, h.Clock);
        await pump.StartAsync(default);
        try
        {
            await runner.Streaming.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(h.SessionId, default);
            await using (var db = h.CreateObserverContext())
            {
                var delivered = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == source.Id);
                delivered.DeliveryVerdict.ShouldBe(variant == "incomplete-body" ? DeliveryVerdict.Truncated : DeliveryVerdict.Delivered);
                var receipt = await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == h.SessionId && t.Sequence == 3);
                receipt.Kind.ShouldBe(TranscriptKinds.UserPrompt);
                receipt.Text.ShouldBe(variant == "incomplete-body" ? source.Body[..250] : source.Body);
            }
            var destination = h.SessionId;
            await using (var db = h.CreateObserverContext())
            {
                if (variant == "wrong-origin")
                    await db.SessionQueuedMessages.Where(m => m.Id == source.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.Origin, QueuedMessageOrigin.Ui));
                if (variant == "ttl")
                    await db.SessionQueuedMessages.Where(m => m.Id == source.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.CreatedAt, h.Now.AddMinutes(-61)));
                if (variant == "wrong-session")
                {
                    destination = Guid.NewGuid();
                    db.AgentSessions.Add(new AgentSession { Id = destination, Cwd = h.TempRoot, DefinitionName = "fake",
                        Status = SessionStatus.Running, AgentKind = AgentKind.ClaudeCode, StartedAt = h.Now, CreatedAt = h.Now });
                    await db.SaveChangesAsync();
                    runner.Produce(Entry(TranscriptKinds.UserPrompt, source.Body, destination));
                }
            }
            var answer = variant == "forged-target"
                ? $"{ReviewPromptFormat.EnvelopePrefix(decoy.Id)} malicious destination claim; the complete answer is 42."
                : "The complete answer is 42, with the original source checked.";
            runner.Produce(Entry(TranscriptKinds.AssistantText, answer, destination));
            if (variant == "api-error") runner.Produce(Entry(TranscriptKinds.AssistantText, "API Error: interrupted", destination, error: true));
            var end = Entry(TranscriptKinds.TurnEnd, null, destination, error: variant == "api-error");
            runner.Produce(end);
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var db = h.CreateObserverContext();
                return await db.TranscriptEntries.AnyAsync(t => t.Uuid == end.Uuid);
            });
            long boundary;
            await using (var observer = h.CreateObserverContext())
            {
                boundary = await observer.TranscriptEntries.Where(t => t.Uuid == end.Uuid).Select(t => t.Sequence).SingleAsync();
                (await TranscriptTurnWindow.FindOwningPromptAsync(observer, destination, boundary, default))
                    .ShouldNotBeNull("the consumer must reach a real persisted turn in the selected session");
            }
            // A fresh consumer has never seen the producer or an in-memory Track call.
            var restarted = new ReviewReplyDispatcher(h.Provider.GetRequiredService<IServiceScopeFactory>(), h.Clock,
                NullLogger<ReviewReplyDispatcher>.Instance);
            if (variant == "atomic-comment")
            {
                fault.Armed = true;
                await Should.ThrowAsync<IOException>(() => restarted.OnTranscriptActionAsync(destination, boundary, default));
                fault.Hits.ShouldBe(1);
                await AssertUnconsumedAsync();
            }
            if (variant == "forged-target")
            {
                await using (var db = h.CreateObserverContext())
                    await db.SessionQueuedMessages.Where(m => m.Id == source.Id).ExecuteUpdateAsync(u =>
                        u.SetProperty(m => m.ConversationKey, $"review:{decoy.Id}"));
                await restarted.OnTranscriptActionAsync(destination, boundary, default);
                await AssertUnconsumedAsync();
                await using var restore = h.CreateObserverContext();
                await restore.SessionQueuedMessages.Where(m => m.Id == source.Id).ExecuteUpdateAsync(u =>
                    u.SetProperty(m => m.ConversationKey, source.ConversationKey));
            }
            await restarted.OnTranscriptActionAsync(destination, boundary, default);
            await restarted.OnTranscriptActionAsync(destination, boundary, default);
            if (variant is "wrong-session" or "wrong-origin" or "incomplete-body" or "ttl" or "api-error")
            {
                await AssertUnconsumedAsync();
                if (variant != "api-error") return;
                // Model the provider resuming the same already delivered request after
                // an API failure. The original durable correlation must still be owed.
                runner.Produce(Entry(TranscriptKinds.UserPrompt, source.Body));
                runner.Produce(Entry(TranscriptKinds.AssistantText, answer));
                end = Entry(TranscriptKinds.TurnEnd, null);
                runner.Produce(end);
                await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
                {
                    await using var observer = h.CreateObserverContext();
                    return await observer.TranscriptEntries.AnyAsync(t => t.Uuid == end.Uuid);
                });
                await restarted.OnTranscriptActionAsync(h.SessionId, end.Sequence, default);
                await restarted.OnTranscriptActionAsync(h.SessionId, end.Sequence, default);
            }
            await using (var verify = h.CreateObserverContext())
            {
                var comments = await verify.ReviewComments.AsNoTracking().Where(c => c.Author == ReviewCommentAuthor.Agent).ToListAsync();
                comments.ShouldHaveSingleItem().Body.ShouldBe(answer);
                comments[0].ThreadId.ShouldBe(thread.Id);
                (await verify.ReviewThreads.SingleAsync(t => t.Id == thread.Id)).Status.ShouldBe(ReviewThreadStatus.AwaitingHuman);
                (await verify.ReviewThreads.SingleAsync(t => t.Id == decoy.Id)).Status.ShouldBe(ReviewThreadStatus.Open);
                (await verify.SessionQueuedMessages.CountAsync(m => m.ReviewThreadId == thread.Id && m.ReviewReplyConsumedAt != null)).ShouldBe(1);
                (await verify.SessionQueuedMessages.Where(m => m.ReviewThreadId == thread.Id).ToListAsync())
                    .ShouldAllBe(m => m.ChannelReplySettledAt == null, "review consumption cannot claim channel ownership");
            }

            async Task AssertUnconsumedAsync()
            {
                await using var db = h.CreateObserverContext();
                (await db.ReviewComments.CountAsync(c => c.Author == ReviewCommentAuthor.Agent)).ShouldBe(0);
                (await db.SessionQueuedMessages.SingleAsync(m => m.Id == source.Id)).ReviewReplyConsumedAt.ShouldBeNull();
                (await db.ReviewThreads.SingleAsync(t => t.Id == thread.Id)).Status.ShouldBe(ReviewThreadStatus.AwaitingAgent);
            }
        }
        finally { await pump.StopAsync(default); }
    }

    private sealed class CommentCommitFault : SaveChangesInterceptor
    {
        public bool Armed;
        public bool QueueArmed;
        public int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (QueueArmed && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added))
            { QueueArmed = false; Hits++; throw new IOException("deterministic review admission failure"); }
            if (Armed && data.Context!.ChangeTracker.Entries<ReviewComment>().Any(e => e.State == EntityState.Added))
            { Armed = false; Hits++; throw new IOException("deterministic comment transaction failure"); }
            return ValueTask.FromResult(result);
        }
    }
}
