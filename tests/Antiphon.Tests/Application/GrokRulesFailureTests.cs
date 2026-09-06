using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class GrokRulesFailureTests
{
    [Test]
    [Arguments("unreadable")]
    [Arguments("incomplete")]
    [Arguments("revision_mismatch")]
    [Arguments("missing_ack")]
    public async Task Failed_read_or_missing_ack_retains_the_original_work_and_fails_once(string reason)
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
        row.DeliveryAttempts = 1;
        row.Status = QueuedMessageStatus.Sent;
        row.RulesDeadlineAt = DateTime.UtcNow.AddMinutes(1);
        var goal = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.Id, Sequence = 2,
            Body = "unaltered original goal\r\ntail", Origin = QueuedMessageOrigin.Delegation, CreatedAt = DateTime.UtcNow };
        db.SessionQueuedMessages.Add(goal);
        Add(1, TranscriptKinds.UserPrompt, row.Body);
        Add(2, TranscriptKinds.AssistantText, reason == "missing_ack" ? "finished reading" :
            $"ANTIPHON_RULES_FAILED id={row.Id:N} generation={f.Receipt.Generation:N} reason={reason}");
        Add(3, TranscriptKinds.TurnEnd, null);
        await db.SaveChangesAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == f.Id);
        session.GrokRulesState.ShouldBe(GrokRulesState.Failed);
        session.GrokRulesFailure.ShouldBe("grok_rules_initialization_failed: " + reason);
        var retained = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == goal.Id);
        retained.Body.ShouldBe(goal.Body);
        retained.Status.ShouldBe(QueuedMessageStatus.Pending);
        retained.DeliveryAttempts.ShouldBe(0);
        (await db.AgentIncidents.CountAsync(i => i.SessionId == f.Id)).ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.Id)).ShouldBe(2);
        void Add(long sequence, string kind, string? text) => db.TranscriptEntries.Add(new() {
            Id = Guid.NewGuid(), AgentSessionId = f.Id, Sequence = sequence, Kind = kind, Text = text,
            CreatedAt = DateTime.UtcNow.AddMinutes(-3), Timestamp = DateTime.UtcNow.AddMinutes(-3),
            StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Persisted_deadline_is_inclusive_at_the_exact_instant(bool atDeadline)
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
        var deadline = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        row.RulesDeadlineAt = deadline.UtcDateTime;
        await db.SaveChangesAsync();
        var clock = new FakeTimeProvider(atDeadline ? deadline : deadline.AddTicks(-1));
        var rules = new GrokRulesRefreshService(f.Provider.GetRequiredService<IServiceScopeFactory>(), clock, Options.Create(new GrokRulesSettings()));
        await rules.ReconcileAsync(f.Id, CancellationToken.None);
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == f.Id)).GrokRulesState
            .ShouldBe(atDeadline ? GrokRulesState.Failed : GrokRulesState.Pending);
        (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == row.Id)).RulesDeadlineAt.ShouldBe(deadline.UtcDateTime);
    }
}
