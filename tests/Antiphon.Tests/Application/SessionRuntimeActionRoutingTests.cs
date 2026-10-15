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
public class SessionRuntimeActionRoutingTests
{
    [Test]
    [Arguments("map-free-restart")]
    [Arguments("forged-target")]
    [Arguments("wrong-session")]
    [Arguments("wrong-origin")]
    [Arguments("incomplete-body")]
    [Arguments("atomic-comment")]
    [Arguments("ttl")]
    [Arguments("api-error")]
    public async Task C1259_review_recovery_uses_authorized_queue_identity_once(string variant)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var runner = new ScriptedSessionRunnerClient();
        var fault = new CommentCommitFault();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
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
        SessionRunnerTranscriptEvent Entry(string kind, string? text, Guid? session = null, bool error = false) =>
            new(session ?? h.SessionId, Interlocked.Increment(ref sequence), kind, Guid.NewGuid().ToString(), null,
                h.Clock.GetUtcNow(), kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text,
                null, null, null, null, kind == TranscriptKinds.TurnEnd ? "end_turn" : null, IsApiError: error);
        h.Adapter.OnSubmitted = body =>
        {
            body.ShouldBe(source.Body);
            runner.Produce(Entry(TranscriptKinds.UserPrompt,
                variant == "incomplete-body" ? body[..Math.Min(100, body.Length - 1)] : body));
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
                receipt.Text.ShouldBe(variant == "incomplete-body" ? source.Body[..Math.Min(100, source.Body.Length - 1)] : source.Body);
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
            // A fresh consumer has never seen the producer or an in-memory Track call.
            var restarted = new ReviewReplyDispatcher(h.Provider.GetRequiredService<IServiceScopeFactory>(), h.Clock,
                NullLogger<ReviewReplyDispatcher>.Instance);
            if (variant == "atomic-comment")
            {
                fault.Armed = true;
                await Should.ThrowAsync<IOException>(() => restarted.OnTranscriptActionAsync(destination, end.Sequence, default));
                fault.Hits.ShouldBe(1);
                await AssertUnconsumedAsync();
            }
            if (variant == "forged-target")
            {
                await using (var db = h.CreateObserverContext())
                    await db.SessionQueuedMessages.Where(m => m.Id == source.Id).ExecuteUpdateAsync(u =>
                        u.SetProperty(m => m.ConversationKey, $"review:{decoy.Id}"));
                await restarted.OnTranscriptActionAsync(destination, end.Sequence, default);
                await AssertUnconsumedAsync();
                await using var restore = h.CreateObserverContext();
                await restore.SessionQueuedMessages.Where(m => m.Id == source.Id).ExecuteUpdateAsync(u =>
                    u.SetProperty(m => m.ConversationKey, source.ConversationKey));
            }
            await restarted.OnTranscriptActionAsync(destination, end.Sequence, default);
            await restarted.OnTranscriptActionAsync(destination, end.Sequence, default);
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
        public int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<ReviewComment>().Any(e => e.State == EntityState.Added))
            { Armed = false; Hits++; throw new IOException("deterministic comment transaction failure"); }
            return ValueTask.FromResult(result);
        }
    }
}
