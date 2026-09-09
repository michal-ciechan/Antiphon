using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class StandingSpecialistHealthService(
    AppDbContext db, IOptions<DelegationSettings> settings, TimeProvider time,
    IEventBus events, ILogger<StandingSpecialistHealthService> logger)
{
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var owners = await db.Agents.AsNoTracking().Where(StandingSpecialistSeatPolicy.Owner)
            .Select(a => a.Id).ToListAsync(ct);
        foreach (var id in owners) await ReconcileOwnerAsync(id, ct);
    }

    public async Task ReconcileOwnerAsync(Guid ownerId, CancellationToken ct)
    {
        var changed = false;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var owner = await db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {ownerId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            var now = time.GetUtcNow().UtcDateTime;
            var health = await db.StandingSpecialistHealths.SingleOrDefaultAsync(h => h.AgentId == ownerId, ct);
            if (health is null)
            {
                health = new() { AgentId = ownerId, UpdatedAt = now };
                db.StandingSpecialistHealths.Add(health);
            }
            var before = health.Status;
            var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == ownerId, ct);
            var suspended = await db.AgentSupervisionStates.AsNoTracking().AnyAsync(s => s.AgentId == ownerId
                && (s.Suspended || s.LivenessLatchedAt != null || s.HerdrFailureHeldAt != null), ct);
            var disabled = owner is null || !owner.AlwaysOn || suspended || routing?.Enabled == false
                || !settings.Value.Enabled || !settings.Value.CheckEnabled || !settings.Value.CheckInterpreterEnabled;
            var completed = await db.SpecialistRequests.Where(r => r.AgentId == ownerId
                && r.Purpose == SpecialistRequestPurpose.Check && r.CompletedAt != null && r.HealthAppliedAt == null)
                .OrderBy(r => r.CompletedAt).ThenBy(r => r.Id).ToListAsync(ct);
            foreach (var request in completed)
            {
                if (disabled) { request.HealthAppliedAt = now; continue; }
                var winner = request.WinnerAttemptId is { } winnerId
                    ? await db.SpecialistAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == winnerId, ct) : null;
                StandingSpecialistHealthPolicy.ApplyRealCheck(health, request, winner, now);
            }
            var candidates = await db.StandingSpecialistCandidateStates.AsNoTracking().Where(c => c.AgentId == ownerId && c.Enabled).ToListAsync(ct);
            var sessionIds = candidates.Where(c => c.SessionId != null).Select(c => c.SessionId!.Value).ToArray();
            var sessions = await db.AgentSessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id)).ToListAsync(ct);
            var held = await db.ModelAvailabilityHolds.AsNoTracking().Where(h => h.ClearedAt == null
                && (h.DisabledUntil == null || h.DisabledUntil > now)).ToListAsync(ct);
            bool WarmQualified(StandingSpecialistCandidateState c) => c.Status == StandingSpecialistCandidateStatus.Qualified
                && c.QualifiedAt is not null && c.Fingerprint is not null
                && sessions.Any(s => s.Id == c.SessionId && s.StartedAt == c.SessionStartedAt && s.Status == SessionStatus.Running)
                && !held.Any(h => h.Kind == c.AgentKind && (h.ModelAlias == "*" || h.ModelAlias == c.ModelAlias));
            var ready = candidates.Where(WarmQualified).ToList();
            var preferred = owner is null ? null : candidates.SingleOrDefault(c => c.PhysicalAgentId == owner.Id);
            var fallback = health.ActiveCandidateId is { } active && active != preferred?.Id;
            var readiness = candidates.Any(c => c.Id != preferred?.Id && !WarmQualified(c));
            var physical = candidates.Where(c => c.PhysicalAgentId != null).Select(c => c.PhysicalAgentId!.Value).ToArray();
            var oldest = await db.AgentTasks.AsNoTracking().Where(StandingSpecialistSeatPolicy.SeatWork)
                .Where(t => t.AgentId != null && physical.Contains(t.AgentId.Value)
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
                .MinAsync(t => (DateTime?)t.CreatedAt, ct);
            if (oldest is { } blocked && now - blocked >= TimeSpan.FromMinutes(5)) health.StarvedSince ??= blocked;
            health.Status = StandingSpecialistHealthPolicy.Project(health, disabled, ready.Count == 0, fallback, readiness, now);
            if (health.Status == StandingSpecialistHealthStatus.Unavailable) health.UnavailableSince ??= now;
            var lines = candidates.Select(c => $"{c.AgentKind}/{c.ModelLevel}/{c.ModelAlias}: {c.Status}; {c.Reason ?? "no readiness evidence"}; transient {c.TransientFailures}/{settings.Value.CheckInterpreterTransientFailureThreshold}; next {c.NextEligibleAt?.ToString("O") ?? "unknown"}");
            health.CandidateSummary = Bound(string.Join("\n", lines), 4000);
            health.Reason = Bound(health.Status switch
            {
                StandingSpecialistHealthStatus.Disabled => "Check interpretation is disabled or intentionally suspended.",
                StandingSpecialistHealthStatus.Unavailable when ready.Count > 0 => "A candidate is ready; awaiting a successful real Check to resolve the outage.",
                StandingSpecialistHealthStatus.Unavailable => health.Reason ?? "No current warm qualified Check route remains.",
                StandingSpecialistHealthStatus.UsingFallback => "The Check interpreter is using a declared alternate.",
                StandingSpecialistHealthStatus.DegradedReadiness => "A declared standby is not ready for Check interpretation.",
                StandingSpecialistHealthStatus.Suspect => health.Reason ?? "A Check request failed; awaiting real service recovery.",
                _ => "The preferred interpreter last completed a valid Check.",
            }, 800);
            health.UpdatedAt = now;
            changed = health.Status != before || completed.Count > 0;
            if (health.Status != before)
                db.AgentIncidents.Add(new AgentIncident
                {
                    Id = Guid.NewGuid(), AgentId = ownerId, Kind = AgentIncidentKind.StandingSpecialistHealth,
                    Severity = health.Status == StandingSpecialistHealthStatus.Unavailable ? AlertSeverity.Error : AlertSeverity.Info,
                    Message = $"Check interpreter: {before} -> {health.Status}. {health.Reason}", CreatedAt = now,
                });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (changed)
        {
            try { await events.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(ownerId), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Check health committed; notification failed for {OwnerId}", ownerId); }
        }
    }

    private static string Bound(string value, int max) => value[..Math.Min(value.Length, max)];
}
