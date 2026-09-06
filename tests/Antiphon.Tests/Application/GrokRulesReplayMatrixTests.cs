using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class GrokRulesReplayMatrixTests
{
    [Test]
    [Arguments(false, 0)] [Arguments(false, 1)] [Arguments(false, 2)]
    [Arguments(false, 3)] [Arguments(false, 4)] [Arguments(false, 5)]
    [Arguments(true, 0)] [Arguments(true, 1)] [Arguments(true, 2)]
    [Arguments(true, 3)] [Arguments(true, 4)] [Arguments(true, 5)]
    public async Task Persisted_refresh_evidence_reconciles_after_each_commit_without_new_logical_delivery(bool compact, int stage)
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await using var db = f.Db();
        if (compact)
        {
            await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
            var initialization = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
            initialization.RulesAcknowledgedAt = DateTime.UtcNow;
            initialization.Status = QueuedMessageStatus.Sent;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == f.Id);
            session.GrokRulesReadyAt = DateTime.UtcNow;
            session.GrokRulesState = GrokRulesState.Ready;
            // This suite starts at the persisted-entry seam; the separate native tailer
            // live/sync/startup matrix supplies parser and ingestion evidence.
            db.TranscriptEntries.Add(Entry(10, TranscriptKinds.CompactBoundary, "persisted automatic boundary"));
            await db.SaveChangesAsync();
        }
        if (stage > 0) await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        SessionQueuedMessage? row = stage == 0 ? null : await db.SessionQueuedMessages.SingleAsync(m =>
            m.AgentSessionId == f.Id && (compact ? m.RulesBoundarySequence != null : m.RulesBoundarySequence == null));
        if (stage >= 2)
        {
            row!.Status = QueuedMessageStatus.Sent;
            row.DeliveryAttempts = 1;
            row.DeliveryVerdict = null;
            row.LastDeliveryBaselineSequence = 10;
            row.RulesDeadlineAt = DateTime.UtcNow.AddMinutes(1);
            if (stage >= 3) db.TranscriptEntries.Add(Entry(11, TranscriptKinds.UserPrompt, row.Body));
            if (stage >= 4)
            {
                db.TranscriptEntries.Add(Entry(12, TranscriptKinds.AssistantText,
                    $"ANTIPHON_RULES_ACK id={row.Id:N} generation={f.Receipt.Generation:N} sha256={f.Receipt.Sha256}"));
                db.TranscriptEntries.Add(Entry(13, TranscriptKinds.TurnEnd, null));
            }
            await db.SaveChangesAsync();
        }
        if (stage == 5) await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        var previousId = row?.Id;
        var recovered = new GrokRulesRefreshService(f.Provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, Options.Create(new GrokRulesSettings()));
        for (var replay = 0; replay < 3; replay++) await recovered.ReconcileAsync(f.Id, CancellationToken.None);
        await using var fresh = f.Db();
        var recoveredRows = await fresh.SessionQueuedMessages.AsNoTracking().Where(m =>
            m.AgentSessionId == f.Id && (compact ? m.RulesBoundarySequence != null : m.RulesBoundarySequence == null)).ToListAsync();
        var same = recoveredRows.ShouldHaveSingleItem();
        if (previousId is not null) same.Id.ShouldBe(previousId.Value);
        same.DeliveryAttempts.ShouldBe(stage >= 2 ? 1 : 0);
        (same.RulesAcknowledgedAt is not null).ShouldBe(stage >= 4);
        if (stage >= 3) same.RulesPromptSequence.ShouldBe(11);
        if (stage == 2) same.DeliveryVerdict.ShouldBeNull("Sent without evidence is still in flight, not permission to resend");
        var final = await fresh.AgentSessions.SingleAsync(s => s.Id == f.Id);
        final.GrokRulesState.ShouldBe(stage >= 4 ? GrokRulesState.Ready : GrokRulesState.Pending);

        TranscriptEntry Entry(long sequence, string kind, string? text) => new() {
            Id = Guid.NewGuid(), AgentSessionId = f.Id, Sequence = sequence, Kind = kind,
            Uuid = $"replay-{sequence}", Text = text, StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
            Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
    }

    [Test]
    public async Task Concurrent_recovery_coalesces_busy_boundaries_and_new_boundary_after_ack_creates_new_read()
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var initial = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
        initial.RulesAcknowledgedAt = DateTime.UtcNow;
        initial.Status = QueuedMessageStatus.Sent;
        for (var n = 1; n <= 2; n++) db.TranscriptEntries.Add(Boundary(n));
        await db.SaveChangesAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Rules.ReconcileAsync(f.Id, CancellationToken.None)));
        var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == f.Id && m.RulesBoundarySequence != null).OrderBy(m => m.Sequence).ToListAsync();
        rows.Count.ShouldBe(2);
        rows.Count(m => m.RulesCoveredByMessageId == null).ShouldBe(1, "busy boundaries must produce one logical read");
        rows[1].RulesCoveredByMessageId.ShouldBe(rows[0].Id);
        await db.SessionQueuedMessages.Where(m => m.Id == rows[0].Id).ExecuteUpdateAsync(u =>
            u.SetProperty(m => m.RulesAcknowledgedAt, DateTime.UtcNow).SetProperty(m => m.Status, QueuedMessageStatus.Sent));
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        db.TranscriptEntries.Add(Boundary(3));
        await db.SaveChangesAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Rules.ReconcileAsync(f.Id, CancellationToken.None)));
        var next = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == f.Id && m.RulesBoundarySequence != null).OrderBy(m => m.Sequence).ToListAsync();
        next.Count.ShouldBe(3);
        next.Count(m => m.RulesAcknowledgedAt == null && m.RulesCoveredByMessageId == null).ShouldBe(1);
        next[^1].RulesCoveredByMessageId.ShouldBeNull("a read completed before a new boundary cannot cover it");
        next[1].RulesAcknowledgedAt.ShouldNotBeNull();

        TranscriptEntry Boundary(int n) => new() { Id = Guid.NewGuid(), AgentSessionId = f.Id,
            Sequence = n, Kind = TranscriptKinds.CompactBoundary, Uuid = $"busy-{n}",
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow };
    }
}
