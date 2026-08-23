using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0071 S8: an API-error stub must never be posted as a review-thread reply, and must never
/// consume the in-memory correlation. Mirrors ChannelReplyDurabilityTests' S2 guard, on the
/// remaining unguarded fan-out consumer.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ReviewReplyDispatcherTests
{
    // CARD-0071 (S8 of the usage-limit spec). A turn killed by the API writes its error string as
    // ordinary AssistantText, so before this guard "API Error: 529 Overloaded" was authored as the
    // Agent on the thread — and TakeAllMatching consumed the correlation, flipping the thread to
    // AwaitingHuman on garbage with nothing left to re-answer. The stub turn must post NOTHING and
    // leave the correlation pending for a resumed turn (or, failing that, the TTL eviction).
    [Test]
    public async Task A_turn_killed_by_an_api_error_posts_nothing_and_stays_pending()
    {
        await using var h = await Harness.CreateAsync();
        var prompt = h.TrackThread();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertApiErrorStubAsync();

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var thread = await h.ReloadThreadAsync();
        thread.Status.ShouldBe(ReviewThreadStatus.AwaitingAgent, "the thread must not flip on garbage");
        thread.Comments.Count.ShouldBe(1, "no ReviewComment is authored from an API-error stub");
        thread.Comments.ShouldAllBe(c => c.Author == ReviewCommentAuthor.Human);
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1, "the correlation stays pending for a resumed turn");
    }

    // The spec's decision is to withhold the TURN, not to strip the stub line: a multi-call turn can
    // produce real text before a later API call dies, and posting the fragment would settle the
    // correlation against half an answer.
    [Test]
    public async Task A_mixed_turn_with_real_text_beside_the_stub_is_withheld_whole()
    {
        await using var h = await Harness.CreateAsync();
        var prompt = h.TrackThread();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "Looking at the line now.");
        await h.InsertApiErrorStubAsync(
            errorText: "API Error: 529 Overloaded", apiErrorClass: "server_error", apiErrorStatus: 529);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var thread = await h.ReloadThreadAsync();
        thread.Status.ShouldBe(ReviewThreadStatus.AwaitingAgent);
        thread.Comments.Count.ShouldBe(1, "half an answer would settle the correlation against an interim fragment");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1);
    }

    // Withholding is only useful if the correlation is actually preserved: the genuine answer on a
    // later (resumed) turn must still land on the thread by the same [Review #id] tag match.
    [Test]
    public async Task The_genuine_answer_on_a_later_turn_still_lands()
    {
        await using var h = await Harness.CreateAsync();
        var prompt = h.TrackThread();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertApiErrorStubAsync();
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(1);

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "The number is correct — source linked.");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var thread = await h.ReloadThreadAsync();
        thread.Status.ShouldBe(ReviewThreadStatus.AwaitingHuman);
        thread.Comments.Count.ShouldBe(2);
        thread.Comments[^1].Author.ShouldBe(ReviewCommentAuthor.Agent);
        thread.Comments[^1].Body.ShouldBe("The number is correct — source linked.");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0);
    }

    [Test]
    public async Task A_normal_turn_is_unaffected()
    {
        await using var h = await Harness.CreateAsync();
        var prompt = h.TrackThread();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "Checked — the number is correct.");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var thread = await h.ReloadThreadAsync();
        thread.Status.ShouldBe(ReviewThreadStatus.AwaitingHuman);
        thread.Comments.Count.ShouldBe(2);
        thread.Comments[^1].Author.ShouldBe(ReviewCommentAuthor.Agent);
        thread.Comments[^1].Body.ShouldBe("Checked — the number is correct.");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0);
    }

    // CARD-0154: a review prompt queued into a busy composer is QueuedUserPrompt. Without the
    // kind-set widen the reply-match query misses it and the in-memory correlation ages out.
    [Test]
    public async Task A_queued_review_prompt_still_routes_the_reply()
    {
        await using var h = await Harness.CreateAsync();
        var prompt = h.TrackThread();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "Checked — the number is correct.");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var thread = await h.ReloadThreadAsync();
        thread.Status.ShouldBe(ReviewThreadStatus.AwaitingHuman);
        thread.Comments.Count.ShouldBe(2);
        thread.Comments[^1].Author.ShouldBe(ReviewCommentAuthor.Agent);
        thread.Comments[^1].Body.ShouldBe("Checked — the number is correct.");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0);
    }

    // CARD-0154 / CARD-0068: a queued prompt after the TurnEnd must cap the extraction window.
    // Without the cap kind matching the reply-match kind, the next turn's assistant text would
    // join into this reply.
    [Test]
    public async Task A_queued_prompt_caps_the_turn_window()
    {
        await using var h = await Harness.CreateAsync();
        var prompt = h.TrackThread();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "Checked — the number is correct.");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.QueuedUserPrompt, "a completion note that queued while the composer was busy");
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.AssistantText, "This belongs to the next turn and must not be posted.");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var thread = await h.ReloadThreadAsync();
        thread.Status.ShouldBe(ReviewThreadStatus.AwaitingHuman);
        thread.Comments.Count.ShouldBe(2);
        thread.Comments[^1].Author.ShouldBe(ReviewCommentAuthor.Agent);
        thread.Comments[^1].Body.ShouldBe("Checked — the number is correct.");
        h.Dispatcher.PendingCount(h.SessionId).ShouldBe(0);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required ReviewReplyDispatcher Dispatcher { get; init; }
        public required Guid AgentId { get; init; }
        public required Guid SessionId { get; init; }
        public required Guid ThreadId { get; init; }

        public static async Task<Harness> CreateAsync()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<ReviewReplyDispatcher>();
            services.AddLogging();
            var provider = services.BuildServiceProvider();

            var agentId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var threadId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                db.Agents.Add(new Agent
                {
                    Id = agentId,
                    Name = $"ReviewDisp-{agentId:N}"[..30],
                    Slug = $"rev-disp-{agentId:N}"[..20],
                    WorkingDirectory = Path.GetTempPath(),
                    Status = AgentStatus.Running,
                    PersistentSessionId = sessionId.ToString("D"),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, CardId = null, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running, Cwd = Path.GetTempPath(), Cols = 120, Rows = 30,
                    CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                db.ReviewThreads.Add(new ReviewThread
                {
                    Id = threadId,
                    AgentId = agentId,
                    Path = "notes/report.md",
                    Line = 12,
                    Snippet = "the questionable line",
                    Status = ReviewThreadStatus.AwaitingAgent,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Comments =
                    [
                        new ReviewComment
                        {
                            Id = Guid.NewGuid(),
                            ThreadId = threadId,
                            Author = ReviewCommentAuthor.Human,
                            Body = "Is this number right?",
                            CreatedAt = now,
                        },
                    ],
                });
                await db.SaveChangesAsync();
            }

            return new Harness
            {
                Provider = provider,
                Dispatcher = provider.GetRequiredService<ReviewReplyDispatcher>(),
                AgentId = agentId,
                SessionId = sessionId,
                ThreadId = threadId,
            };
        }

        /// <summary>Enqueue the in-memory correlation the dispatcher matches on the [Review #id] tag.</summary>
        public string TrackThread()
        {
            var prompt =
                $"{ReviewPromptFormat.EnvelopePrefix(ThreadId)} notes/report.md:12\nIs this number right?";
            Dispatcher.Track(SessionId, new ReviewReplyDispatcher.PendingThreadReply(
                ThreadId, prompt, DateTime.UtcNow));
            return prompt;
        }

        public async Task InsertTranscriptEntryAsync(
            string kind,
            string? text = null,
            string? stopReason = null,
            bool? isApiError = null,
            string? apiErrorClass = null,
            int? apiErrorStatus = null)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var seq = ((await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = seq,
                Kind = kind,
                Text = text,
                StopReason = stopReason,
                IsApiError = isApiError,
                ApiErrorClass = apiErrorClass,
                ApiErrorStatus = apiErrorStatus,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// The measured API-error stub shape (CARD-0072 S1): the error string as ordinary AssistantText
        /// plus a stop_sequence TurnEnd, both stamped IsApiError.
        /// </summary>
        public async Task InsertApiErrorStubAsync(
            string errorText = "API Error: 429 You've hit your usage limit. Your limit will reset at 6:10pm (Europe/London).",
            string apiErrorClass = "rate_limit",
            int? apiErrorStatus = 429)
        {
            await InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, errorText,
                isApiError: true, apiErrorClass: apiErrorClass, apiErrorStatus: apiErrorStatus);
            await InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "stop_sequence",
                isApiError: true, apiErrorClass: apiErrorClass, apiErrorStatus: apiErrorStatus);
        }

        public async Task<ReviewThread> ReloadThreadAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            return await db.ReviewThreads
                .AsNoTracking()
                .Include(t => t.Comments)
                .SingleAsync(t => t.Id == ThreadId);
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.ReviewComments.Where(c => c.ThreadId == ThreadId).ExecuteDeleteAsync();
                await db.ReviewThreads.Where(t => t.Id == ThreadId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
                await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
            }
            await Provider.DisposeAsync();
        }
    }
}
