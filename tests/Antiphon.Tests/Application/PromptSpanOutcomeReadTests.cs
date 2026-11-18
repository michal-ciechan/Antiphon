using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1987. The six read outcomes on a private cache: cold, hit, bypass, tail, and a full read
/// after the proof fails. Too-large is the database-free store test.
/// </summary>
[Category("Integration")]
public class PromptSpanOutcomeReadTests
{
    [Test]
    public async Task C1987_prompt_span_read_outcomes_match_the_path()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(sessionId, 1, "one", dispatchedAt.AddMinutes(1));
        await SeedEntryAsync(sessionId, 2, "two", dispatchedAt.AddMinutes(2));
        var cache = new TranscriptPromptSpan.PromptCache(
            8, 4_000_000, 32_000_000, TimeSpan.FromSeconds(120), () => Environment.TickCount64);

        var coldBefore = cache.ReadOutcomes();
        var cold = await LoadAsync(sessionId, dispatchedAt, cache);
        cold.RawPrompts.Count.ShouldBe(2);
        (cache.ReadOutcomes().Cold - coldBefore.Cold).ShouldBe(1);

        var hitBefore = cache.ReadOutcomes();
        var hit = await LoadAsync(sessionId, dispatchedAt, cache);
        hit.RawPrompts.Count.ShouldBe(2);
        (cache.ReadOutcomes().Hit - hitBefore.Hit).ShouldBe(1);

        var bypassBefore = cache.ReadOutcomes();
        await using (var open = CreateContext())
        {
            await using var tx = await open.Database.BeginTransactionAsync();
            var bypass = await TranscriptPromptSpan.LoadAsync(open, sessionId, dispatchedAt, cache, CancellationToken.None);
            bypass.RawPrompts.Count.ShouldBe(2);
        }
        (cache.ReadOutcomes().Bypass - bypassBefore.Bypass).ShouldBe(1);

        await SeedEntryAsync(sessionId, 3, "three", dispatchedAt.AddMinutes(3));
        var tailBefore = cache.ReadOutcomes();
        var tail = await LoadAsync(sessionId, dispatchedAt, cache);
        tail.RawPrompts.Count.ShouldBe(3);
        tail.RawPrompts.Select(r => r.Sequence).ShouldBe(new[] { 1L, 2L, 3L });
        (cache.ReadOutcomes().Tail - tailBefore.Tail).ShouldBe(1);

        await using (var db = CreateContext())
        {
            var gone = await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == sessionId && t.Sequence == 1);
            db.TranscriptEntries.Remove(gone);
            await db.SaveChangesAsync();
        }
        var fullBefore = cache.ReadOutcomes();
        var full = await LoadAsync(sessionId, dispatchedAt, cache);
        full.RawPrompts.Count.ShouldBe(2);
        full.RawPrompts.Select(r => r.Sequence).ShouldBe(new[] { 2L, 3L });
        (cache.ReadOutcomes().FullAfterProof - fullBefore.FullAfterProof).ShouldBe(1);
        cache.ReadOutcomes().TooLargeToStore.ShouldBe(0);
    }

    private static async Task<TranscriptPromptSpan.Result> LoadAsync(
        Guid sessionId, DateTime dispatchedAt, TranscriptPromptSpan.PromptCache cache)
    {
        await using var db = CreateContext();
        return await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, cache, CancellationToken.None);
    }

    private static async Task<(Guid SessionId, DateTime DispatchedAt)> SeedSessionAsync()
    {
        var sessionId = Guid.NewGuid();
        var at = DateTime.UtcNow.AddMinutes(-11);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = at,
            StartedAt = at,
            LastSeenAt = at,
        });
        await db.SaveChangesAsync();
        return (sessionId, at);
    }

    private static async Task SeedEntryAsync(Guid sessionId, long sequence, string text, DateTime timestamp)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = TranscriptKinds.UserPrompt,
            Uuid = "span-" + Guid.NewGuid().ToString("N"),
            Role = "user",
            Text = text,
            Timestamp = timestamp,
            CreatedAt = timestamp,
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
