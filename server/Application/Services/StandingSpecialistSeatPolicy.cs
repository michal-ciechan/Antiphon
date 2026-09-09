using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public static class StandingSpecialistSeatPolicy
{
    public static bool IsCheck(Agent agent) => agent.StandingSpecialistRole == AgentTaskRole.Check;
    public static bool IsAlternate(Agent agent) => IsCheck(agent)
        && agent.StandingSpecialistOwnerId is { } owner && owner != agent.Id;

    // The configured name is a compatibility discovery path only. Once adopted, rename cannot
    // erase the typed relation or turn an alternate into an ordinary AlwaysOn agent.
    public static bool IsCheck(Agent agent, DelegationSettings settings) => IsCheck(agent)
        || string.Equals(agent.Slug, CheckInterpreterProvisioner.Slug(settings), StringComparison.OrdinalIgnoreCase);

    public static async Task<string?> StartRefusalAsync(
        AppDbContext db, Agent seat, DelegationSettings settings, bool automatic, CancellationToken ct,
        bool requireCapability = true)
    {
        if (!IsCheck(seat, settings)) return null;
        if (!settings.Enabled || !settings.CheckEnabled || !settings.CheckInterpreterEnabled)
            return "Check interpretation is disabled.";
        var ownerId = seat.StandingSpecialistOwnerId ?? seat.Id;
        var owner = ownerId == seat.Id ? seat : await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == ownerId, ct);
        if (owner is null || !owner.AlwaysOn || owner.IsPoolDelegate)
            return "The logical Check owner no longer authorizes this seat.";
        var states = await db.AgentSupervisionStates.AsNoTracking()
            .Where(s => s.AgentId == ownerId || s.AgentId == seat.Id).ToListAsync(ct);
        if (states.Any(s => (automatic || s.AgentId != seat.Id)
            && (s.Suspended || s.LivenessLatchedAt != null || s.HerdrFailureHeldAt != null)))
            return "The Check owner or physical seat requires human continuation.";
        var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == ownerId, ct);
        if (routing is { Enabled: false }) return "Specialist routing is disabled.";
        if (ownerId != seat.Id)
        {
            if (routing is null) return "This alternate is no longer declared.";
            var pair = new RoutingCandidate(seat.Kind, seat.ModelLevel);
            if (!RoutingCandidate.Parse(routing.CandidatesJson).Skip(1).Contains(pair))
                return "This alternate is no longer declared.";
            var candidate = await db.StandingSpecialistCandidateStates.AsNoTracking()
                .SingleOrDefaultAsync(c => c.AgentId == ownerId && c.PhysicalAgentId == seat.Id && c.Enabled, ct);
            if (candidate is null) return "This physical seat has no active candidate relation.";
            if (requireCapability && (string.IsNullOrWhiteSpace(candidate.CapabilityFingerprint) || candidate.MaxInputUtf8Bytes is not > 0))
                return "The alternate has no certified CLI/input/tool capability; standing start is deferred.";
            if (candidate.Status == StandingSpecialistCandidateStatus.PendingDependency
                || (requireCapability && candidate.Status == StandingSpecialistCandidateStatus.Unsupported))
                return candidate.Reason ?? "The candidate has no certified capability.";
            if (requireCapability && candidate.Status == StandingSpecialistCandidateStatus.Quarantined
                && candidate.QualificationAuthorization == candidate.ClaimedQualificationAuthorization)
                return "The candidate is quarantined; explicit revalidate is required.";
            if (seat.TuiProfileId is not null || seat.ModelId is not null || seat.SessionBackend != SessionBackend.PtyHost)
                return "V1 alternates require the declared registry definition and an isolated PtyHost seat.";
        }
        return null;
    }
}
