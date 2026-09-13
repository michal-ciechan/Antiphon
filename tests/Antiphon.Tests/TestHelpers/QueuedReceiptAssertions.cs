using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0499: promoted from PostLandMutationDeliveryTests.ConfirmQueuedReceiptAsync.
/// </summary>
internal static class QueuedReceiptAssertions
{
    public static async Task ConfirmQueuedReceiptAsync(
        string connection,
        BridgeQueueHarness h,
        SessionQueuedMessage queued,
        Guid sessionId,
        bool busy,
        string cut = "after-receipt")
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        queued = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued.Id);
        if (busy) await SetWorkingAsync(connection, sessionId, true);

        if (queued.Status == QueuedMessageStatus.Pending && queued.DeliveryAttempts == 0)
        {
            await h.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
            if (busy)
            {
                h.Adapter.Inputs.ShouldBeEmpty();
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id))
                    .Status.ShouldBe(QueuedMessageStatus.Pending);
                await SetWorkingAsync(connection, sessionId, false);
                await h.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
            }
            queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
        }

        var typedBody = h.Adapter.SubmittedBodies.LastOrDefault() ?? queued.Body;
        if (!await db.TranscriptEntries.AnyAsync(e =>
                e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt && e.Text == typedBody))
        {
            var seq = (await db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = seq,
                Kind = TranscriptKinds.UserPrompt,
                Text = typedBody,
                CreatedAt = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var prompts = await db.TranscriptEntries
            .Where(p => p.AgentSessionId == sessionId && p.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        prompts.Count.ShouldBe(1);
        prompts[0].Text.ShouldBe(typedBody);
        if (cut is "queue-inserted" or "lost-wakeup")
            queued.Status.ShouldBeOneOf(QueuedMessageStatus.Pending, QueuedMessageStatus.Sent);
    }

    private static async Task SetWorkingAsync(string connection, Guid sessionId, bool working)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var seq = ((await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = working ? TranscriptKinds.AssistantText : TranscriptKinds.TurnEnd,
            Text = working ? "busy" : null,
            StopReason = working ? null : TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
