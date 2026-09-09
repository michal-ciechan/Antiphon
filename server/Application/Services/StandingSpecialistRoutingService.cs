using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Declared specialist routing and bounded qualification authorization. Configuration never starts
/// a process or manufactures capability/qualification evidence. Writes take only the consumer-owner
/// lock; no provider/global admission lock or external operation is acquired underneath it.
/// </summary>
public sealed class StandingSpecialistRoutingService(
    AppDbContext db, IOptions<DelegationSettings> settings, TimeProvider time,
    IEventBus events, ILogger<StandingSpecialistRoutingService> logger)
{
    public async Task<StandingSpecialistRoutingDto> GetAsync(Guid agentId, CancellationToken ct)
    {
        var owner = await RequireOwnerAsync(agentId, false, ct);
        return await DescribeAsync(owner, ct);
    }

    public async Task<StandingSpecialistRoutingDto> PutAsync(
        Guid agentId, PutStandingSpecialistRoutingRequest request, CancellationToken ct)
    {
        StandingSpecialistRoutingDto result;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var owner = await RequireOwnerAsync(agentId, true, ct);
            var pairs = StandingSpecialistRoutingPolicy.Validate(owner, request.Candidates);
            var existing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == agentId, ct);
            if (existing?.ConcurrencyToken != request.ConcurrencyToken)
                throw new ConflictException("Specialist routing changed; reload it before saving.", "specialist_routing_stale");
            var now = time.GetUtcNow().UtcDateTime;
            var token = Guid.NewGuid();
            if (existing is null)
            {
                var row = new StandingSpecialistRouting
                {
                    Id = Guid.NewGuid(), AgentId = agentId, CandidatesJson = RoutingCandidate.Serialize(pairs),
                    Enabled = request.Enabled, ConcurrencyToken = token, CreatedAt = now, UpdatedAt = now,
                };
                db.StandingSpecialistRoutings.Add(row);
                await db.SaveChangesAsync(ct);
                db.Entry(row).State = EntityState.Detached;
            }
            else
            {
                var json = RoutingCandidate.Serialize(pairs);
                await db.StandingSpecialistRoutings.Where(r => r.Id == existing.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.CandidatesJson, json)
                        .SetProperty(r => r.Enabled, request.Enabled).SetProperty(r => r.ConcurrencyToken, token)
                        .SetProperty(r => r.UpdatedAt, now), ct);
            }

            var states = await db.StandingSpecialistCandidateStates.AsNoTracking().Where(c => c.AgentId == agentId).ToListAsync(ct);
            // A primary identity edit can move an existing alternate pair to the head. The old
            // primary pair must not keep claiming the owner's physical process as an alternate.
            // Retain its old session/fingerprint evidence, but require a distinct new seat.
            var detachedPrimaryIds = new HashSet<Guid>();
            foreach (var state in states.Where(c => c.PhysicalAgentId == owner.Id
                && new RoutingCandidate(c.AgentKind, c.ModelLevel) != pairs[0]))
            {
                await db.StandingSpecialistCandidateStates.Where(c => c.Id == state.Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.PhysicalAgentId, (Guid?)null)
                        .SetProperty(c => c.QualifiedAt, (DateTime?)null), ct);
                state.PhysicalAgentId = null;
                detachedPrimaryIds.Add(state.Id);
            }
            // Retire admission first, releasing the active-physical-seat uniqueness claim. Retain
            // all evidence and ownership; actual active work drains through the execution owner.
            foreach (var state in states.Where(c => !request.Enabled || !pairs.Contains(new(c.AgentKind, c.ModelLevel))))
                await db.StandingSpecialistCandidateStates.Where(c => c.Id == state.Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.Enabled, false).SetProperty(c => c.Status, StandingSpecialistCandidateStatus.Disabled)
                        .SetProperty(c => c.UpdatedAt, now), ct);

            for (var index = 0; index < pairs.Count; index++)
            {
                var pair = pairs[index];
                var alias = DispatchModelAlias.Resolve(pair.AgentKind!.Value, pair.ModelLevel!.Value, index == 0 ? owner.ModelId : null);
                var state = states.SingleOrDefault(c => c.AgentKind == pair.AgentKind && c.ModelLevel == pair.ModelLevel);
                var status = request.Enabled
                    ? (index == 0 ? StandingSpecialistCandidateStatus.Unqualified : StandingSpecialistCandidateStatus.DeclaredButUnprovisioned)
                    : StandingSpecialistCandidateStatus.Disabled;
                var reason = request.Enabled
                    ? (index == 0 ? "Awaiting qualification of the current primary." : "Awaiting standing-start admission; next eligibility is unknown.")
                    : "Routing is disabled.";
                if (request.Enabled && pair.AgentKind == AgentKind.Codex)
                {
                    status = StandingSpecialistCandidateStatus.PendingDependency;
                    reason = "Pending CARD-0167 agent-path injection and CARD-0415 capability certification.";
                }
                if (state is null)
                {
                    state = new StandingSpecialistCandidateState
                    {
                        Id = Guid.NewGuid(), AgentId = agentId, AgentKind = pair.AgentKind.Value,
                        ModelLevel = pair.ModelLevel.Value, PhysicalAgentId = index == 0 ? owner.Id : null,
                        ModelAlias = alias, Enabled = request.Enabled, Status = status, Reason = reason,
                        DeclaredAt = now, UnprovisionedAt = index == 0 ? null : now,
                        QualificationAuthorization = Guid.NewGuid(), UpdatedAt = now,
                    };
                    db.StandingSpecialistCandidateStates.Add(state);
                    await db.SaveChangesAsync(ct);
                    db.Entry(state).State = EntityState.Detached;
                }
                else if (request.Enabled && (!state.Enabled || state.ModelAlias != alias
                    || detachedPrimaryIds.Contains(state.Id) || (index == 0 && state.PhysicalAgentId != owner.Id)))
                {
                    var authorization = Guid.NewGuid();
                    var physical = index == 0 ? owner.Id : state.PhysicalAgentId;
                    if (physical is not null && status != StandingSpecialistCandidateStatus.PendingDependency)
                        status = StandingSpecialistCandidateStatus.Unqualified;
                    await db.StandingSpecialistCandidateStates.Where(c => c.Id == state.Id).ExecuteUpdateAsync(s =>
                        s.SetProperty(c => c.Enabled, true).SetProperty(c => c.ModelAlias, alias)
                            .SetProperty(c => c.PhysicalAgentId, physical).SetProperty(c => c.Status, status)
                            .SetProperty(c => c.Reason, reason).SetProperty(c => c.QualificationAuthorization, authorization)
                            .SetProperty(c => c.UnprovisionedAt, physical == null ? state.UnprovisionedAt ?? now : null)
                            .SetProperty(c => c.QualifiedAt, (DateTime?)null).SetProperty(c => c.UpdatedAt, now), ct);
                }
                // Reordering an enabled unchanged pair preserves its seat, certificate and streak.
            }
            result = await DescribeAsync(owner, ct);
            await transaction.CommitAsync(ct);
        }
        await PublishAsync(agentId, ct);
        return result;
    }

    public async Task<StandingSpecialistRoutingDto> RevalidateAsync(
        Guid agentId, RevalidateStandingSpecialistRequest request, CancellationToken ct)
    {
        StandingSpecialistRoutingDto result;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var owner = await RequireOwnerAsync(agentId, true, ct);
            var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == agentId, ct);
            if (routing is null || !routing.Enabled || !settings.Value.CheckInterpreterEnabled)
                throw new ConflictException("Check interpreter routing is disabled.", "specialist_routing_disabled");
            if (routing.ConcurrencyToken != request.ConcurrencyToken)
                throw new ConflictException("Specialist routing changed; reload it before revalidating.", "specialist_routing_stale");
            var now = time.GetUtcNow().UtcDateTime;
            var token = Guid.NewGuid();
            await db.StandingSpecialistRoutings.Where(r => r.Id == routing.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.ConcurrencyToken, token).SetProperty(r => r.UpdatedAt, now), ct);
            var candidates = await db.StandingSpecialistCandidateStates.AsNoTracking()
                .Where(c => c.AgentId == agentId && c.Enabled).ToListAsync(ct);
            foreach (var candidate in candidates)
            {
                var authorization = Guid.NewGuid();
                await db.StandingSpecialistCandidateStates.Where(c => c.Id == candidate.Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.QualificationAuthorization, authorization)
                        .SetProperty(c => c.UpdatedAt, now), ct);
            }
            // Authorization neither clears a streak/quarantine nor confers qualification. The
            // reconciliation owner must still observe idle/hold/human/capability barriers.
            result = await DescribeAsync(owner, ct);
            await transaction.CommitAsync(ct);
        }
        await PublishAsync(agentId, ct);
        return result;
    }

    private async Task<Agent> RequireOwnerAsync(Guid agentId, bool locked, CancellationToken ct)
    {
        var owner = locked
            ? await db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {agentId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct)
            : await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == agentId, ct);
        if (owner is null) throw new NotFoundException("Agent", agentId);
        if (!StandingSpecialistSeatPolicy.IsCheck(owner, settings.Value) || StandingSpecialistSeatPolicy.IsAlternate(owner)
            || owner.IsPoolDelegate || !owner.AlwaysOn)
            throw new ValidationException("agentId", "Only the standing Check interpreter owns specialist routing.");
        return owner;
    }

    private async Task<StandingSpecialistRoutingDto> DescribeAsync(Agent owner, CancellationToken ct)
    {
        var health = await db.StandingSpecialistHealths.AsNoTracking().SingleOrDefaultAsync(h => h.AgentId == owner.Id, ct);
        var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == owner.Id, ct);
        var states = await db.StandingSpecialistCandidateStates.AsNoTracking().Where(c => c.AgentId == owner.Id)
            .OrderBy(c => c.DeclaredAt).ThenBy(c => c.Id).ToListAsync(ct);
        return new(owner.Id, routing?.ConcurrencyToken, routing?.Enabled, owner.Kind, owner.ModelLevel,
            DispatchModelAlias.Resolve(owner.Kind, owner.ModelLevel, owner.ModelId),
            RoutingCandidate.Parse(routing?.CandidatesJson), states.Select(c => new StandingSpecialistCandidateDto(
                c.Id, c.AgentKind, c.ModelLevel, c.ModelAlias, c.PhysicalAgentId, c.Enabled, c.Status,
                c.Reason, c.DeclaredAt, c.UnprovisionedAt, c.LastAdmissionRefusedAt, c.NextEligibleAt, c.TransientFailures)).ToArray(),
            health is null ? null : new(health.Status, health.Reason, health.LastValidCheckAt, health.ConsecutiveFailedRequests,
                health.UnavailableSince, health.LastAttemptTaskId));
    }

    private async Task PublishAsync(Guid agentId, CancellationToken ct)
    {
        try { await events.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Specialist routing for {AgentId} committed; change notification failed", agentId);
        }
    }
}
