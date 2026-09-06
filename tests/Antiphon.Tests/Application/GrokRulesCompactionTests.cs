using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Persisted-boundary bookkeeping tests. These do not substitute for native auto-compaction acceptance.</summary>
[Category("Integration")]
public sealed class GrokRulesCompactionTests
{
    [Test]
    public async Task Concurrent_replay_keeps_one_trigger_per_boundary_and_coalesces_untyped_reads()
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var launch = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
        launch.RulesAcknowledgedAt = DateTime.UtcNow;
        launch.Status = QueuedMessageStatus.Sent;
        var first = Add(db, f.Id, 1, TranscriptKinds.CompactBoundary);
        var second = Add(db, f.Id, 2, TranscriptKinds.CompactBoundary);
        await db.SaveChangesAsync();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Rules.ReconcileAsync(f.Id, CancellationToken.None)));
        var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == f.Id && m.RulesBoundarySequence != null)
            .OrderBy(m => m.Sequence).ToListAsync();
        rows.Count.ShouldBe(2);
        rows[0].RulesRefreshKey.ShouldBe($"compact:{first.Id:N}");
        rows[1].RulesRefreshKey.ShouldBe($"compact:{second.Id:N}");
        rows[1].RulesCoveredByMessageId.ShouldBe(rows[0].Id);
        rows.ShouldAllBe(m => m.RulesAcknowledgedAt == null && m.DeliveryAttempts == 0);
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == f.Id)).GrokRulesState.ShouldBe(GrokRulesState.Pending);
    }

    [Test]
    public async Task Refresh_caused_compaction_permits_one_follow_on_then_fails_with_ownership_retained()
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var launch = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
        launch.DeliveryAttempts = 1;
        launch.RulesDeadlineAt = DateTime.UtcNow.AddMinutes(1);
        Add(db, f.Id, 1, TranscriptKinds.UserPrompt, launch.Body);
        Add(db, f.Id, 2, TranscriptKinds.CompactBoundary);
        Add(db, f.Id, 3, TranscriptKinds.AssistantText, Ack(f, launch));
        Add(db, f.Id, 4, TranscriptKinds.TurnEnd);
        await db.SaveChangesAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        var follow = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id && m.RulesFollowOnCount == 1);
        follow.RulesChainId.ShouldBe(launch.Id);
        follow.DeliveryAttempts = 1;
        follow.RulesDeadlineAt = DateTime.UtcNow.AddMinutes(1);
        Add(db, f.Id, 5, TranscriptKinds.UserPrompt, follow.Body);
        Add(db, f.Id, 6, TranscriptKinds.CompactBoundary);
        await db.SaveChangesAsync();
        // Recreated service uses persisted chain metadata, not a process-local counter.
        var recreated = new GrokRulesRefreshService(f.Provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new Antiphon.SessionRunner.Contracts.GrokRulesSettings()));
        await recreated.ReconcileAsync(f.Id, CancellationToken.None);
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == f.Id);
        session.GrokRulesFailure.ShouldBe("grok_rules_refresh_failed: refresh_loop");
        session.Status.ShouldBe(SessionStatus.Running);
        session.EndedAt.ShouldBeNull();
        (await db.AgentIncidents.CountAsync(i => i.SessionId == f.Id && i.Severity == AlertSeverity.Error)).ShouldBe(1);
        await recreated.ReconcileAsync(f.Id, CancellationToken.None);
        (await db.AgentIncidents.CountAsync(i => i.SessionId == f.Id)).ShouldBe(1);
    }

    private static string Ack(GrokRulesInitializationTests.Fixture f, SessionQueuedMessage row) =>
        $"ANTIPHON_RULES_ACK id={row.Id:N} generation={f.Receipt.Generation:N} sha256={f.Receipt.Sha256}";

    private static TranscriptEntry Add(AppDbContext db, Guid session, long sequence, string kind, string? text = null)
    {
        var row = new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = session, Sequence = sequence,
            Kind = kind, Text = text, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null };
        db.TranscriptEntries.Add(row);
        return row;
    }
}
