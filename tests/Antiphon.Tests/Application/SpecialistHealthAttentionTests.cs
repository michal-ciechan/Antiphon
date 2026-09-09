using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class StandingSpecialistHealthPolicyTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Card0415_V17_older_completion_cannot_resolve_or_reopen_a_newer_episode(bool olderSuccess)
    {
        var now = DateTime.UtcNow;
        var health = new StandingSpecialistHealth();
        var winner = new SpecialistAttempt { Id = Guid.NewGuid(), CandidateId = Guid.NewGuid(),
            Outcome = SpecialistAttemptOutcome.ValidReading, CompletedAt = now };
        SpecialistRequest Request(bool success, DateTime started) => new() { Id = Guid.NewGuid(),
            Purpose = SpecialistRequestPurpose.Check, StartedAt = started, DeadlineAt = now.AddMinutes(1), CompletedAt = now,
            Status = success ? SpecialistRequestStatus.Succeeded : SpecialistRequestStatus.Failed,
            Outcome = success ? SpecialistAttemptOutcome.ValidReading : SpecialistAttemptOutcome.TimedOutAfterDispatch,
            WinnerAttemptId = success ? winner.Id : null, Reason = success ? null : "newer provider silence" };
        var newer = Request(!olderSuccess, now.AddSeconds(-2));
        StandingSpecialistHealthPolicy.ApplyRealCheck(health, newer, olderSuccess ? null : winner, now);
        if (olderSuccess) health.UnavailableSince = now;
        var originalCount = health.ConsecutiveFailedRequests;
        var originalFailure = health.FirstFailureAt;
        var originalUnavailable = health.UnavailableSince;
        var older = Request(olderSuccess, now.AddSeconds(-5));
        StandingSpecialistHealthPolicy.ApplyRealCheck(health, older, olderSuccess ? winner : null, now.AddSeconds(1));
        older.HealthAppliedAt.ShouldNotBeNull();
        health.LastRequestId.ShouldBe(newer.Id);
        health.ConsecutiveFailedRequests.ShouldBe(originalCount);
        health.FirstFailureAt.ShouldBe(originalFailure);
        health.UnavailableSince.ShouldBe(originalUnavailable);
    }

    [Test]
    public void Card0415_V16_failure_clock_and_precedence_are_fixed_and_qualification_cannot_clear_outage()
    {
        var now = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
        var h = new StandingSpecialistHealth { FirstFailureAt = now, ConsecutiveFailedRequests = 1 };
        StandingSpecialistHealthPolicy.Project(h, false, false, true, true, now.AddMinutes(5).AddTicks(-1)).ShouldBe(StandingSpecialistHealthStatus.Suspect);
        StandingSpecialistHealthPolicy.Project(h, false, false, true, true, now.AddMinutes(5)).ShouldBe(StandingSpecialistHealthStatus.Unavailable);
        h.UnavailableSince = now;
        h.FirstFailureAt = null;
        h.ConsecutiveFailedRequests = 0;
        StandingSpecialistHealthPolicy.Project(h, false, false, false, false, now).ShouldBe(StandingSpecialistHealthStatus.Unavailable);
        StandingSpecialistHealthPolicy.Project(h, true, true, true, true, now).ShouldBe(StandingSpecialistHealthStatus.Disabled);
    }

    [Test]
    public void Card0415_V17_only_once_per_real_winning_request_resets_service_failure()
    {
        var now = DateTime.UtcNow;
        var h = new StandingSpecialistHealth { FirstFailureAt = now.AddMinutes(-6), UnavailableSince = now.AddMinutes(-1), ConsecutiveFailedRequests = 3 };
        var winner = new SpecialistAttempt { Id = Guid.NewGuid(), CandidateId = Guid.NewGuid(), TaskId = Guid.NewGuid(), Outcome = SpecialistAttemptOutcome.ValidReading, CompletedAt = now };
        var r = new SpecialistRequest { Id = Guid.NewGuid(), Purpose = SpecialistRequestPurpose.Qualification, Status = SpecialistRequestStatus.Succeeded,
            WinnerAttemptId = winner.Id, CompletedAt = now, DeadlineAt = now.AddSeconds(1) };
        StandingSpecialistHealthPolicy.ApplyRealCheck(h, r, winner, now);
        h.ConsecutiveFailedRequests.ShouldBe(3);
        r.HealthAppliedAt.ShouldBeNull();
        r.Purpose = SpecialistRequestPurpose.Check;
        StandingSpecialistHealthPolicy.ApplyRealCheck(h, r, winner, now);
        h.ConsecutiveFailedRequests.ShouldBe(0);
        h.UnavailableSince.ShouldBeNull();
        h.LastValidCheckAt.ShouldBe(now);
        h.ActiveCandidateId.ShouldBe(winner.CandidateId);
        var residence = h.ActiveSince;
        StandingSpecialistHealthPolicy.ApplyRealCheck(h, r, winner, now.AddMinutes(1));
        h.ActiveSince.ShouldBe(residence);
    }

    [Test]
    public void Card0415_V16_busy_does_not_count_as_failed_interpretation()
    {
        var h = new StandingSpecialistHealth();
        var r = new SpecialistRequest { Purpose = SpecialistRequestPurpose.Check, Status = SpecialistRequestStatus.Failed,
            Outcome = SpecialistAttemptOutcome.Busy, CompletedAt = DateTime.UtcNow };
        StandingSpecialistHealthPolicy.ApplyRealCheck(h, r, null, DateTime.UtcNow);
        h.FirstFailureAt.ShouldBeNull();
        h.ConsecutiveFailedRequests.ShouldBe(0);
        r.HealthAppliedAt.ShouldNotBeNull();
    }
}

[Category("Integration")]
public class SpecialistHealthAttentionTests
{
    [Test]
    public async Task Card0415_V16_exhaustion_is_immediate_and_Attention_survives_pruned_incidents()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString, Delegation = settings });
        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        owner.StandingSpecialistOwnerId = owner.Id;
        owner.StandingSpecialistRole = AgentTaskRole.Check;
        db.StandingSpecialistCandidateStates.Add(new() { Id = Guid.NewGuid(), AgentId = owner.Id, PhysicalAgentId = owner.Id,
            AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Low, ModelAlias = "haiku", Enabled = true,
            Status = StandingSpecialistCandidateStatus.Unqualified, Reason = "awaiting real qualification" });
        await db.SaveChangesAsync();
        var service = new StandingSpecialistHealthService(db, Options.Create(settings), TimeProvider.System, h.EventBus, NullLogger<StandingSpecialistHealthService>.Instance);
        await service.ReconcileOwnerAsync(owner.Id, CancellationToken.None);
        var health = await db.StandingSpecialistHealths.SingleAsync();
        health.Status.ShouldBe(StandingSpecialistHealthStatus.Unavailable);
        health.ConsecutiveFailedRequests.ShouldBe(0);
        var first = health.UnavailableSince;
        await service.ReconcileOwnerAsync(owner.Id, CancellationToken.None);
        health.UnavailableSince.ShouldBe(first);
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.StandingSpecialistHealth)).ShouldBe(1);
        await db.AgentIncidents.ExecuteDeleteAsync();
        health.UnavailableSince = DateTime.UtcNow.AddDays(-2);
        await db.SaveChangesAsync();
        var attention = new AttentionService(db, h.Runner, Options.Create(new SupervisionSettings { Enabled = false }), Options.Create(settings),
            TimeProvider.System, NullLogger<AttentionService>.Instance);
        var result = await attention.GetAsync(CancellationToken.None, includeProgressProbe: false);
        var row = result.Items.Single(i => i.Kind == AttentionKind.StandingSpecialistHealth);
        row.Severity.ShouldBe(AlertSeverity.Error);
        row.AgentId.ShouldBe(owner.Id);
        row.Actions.ShouldContain(AttentionAction.OpenAgent);
        settings.CheckInterpreterEnabled = false;
        (await attention.GetAsync(CancellationToken.None, false)).Items.ShouldNotContain(i => i.Kind == AttentionKind.StandingSpecialistHealth);
        (await db.StandingSpecialistHealths.CountAsync()).ShouldBe(1);
        ((int)AttentionKind.CapacityRecoveryExhausted).ShouldBe(32);
        ((int)AttentionKind.StandingSpecialistHealth).ShouldBe(33);
    }
}
