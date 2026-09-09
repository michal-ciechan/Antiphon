using System.Linq.Expressions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public static class StandingSpecialistSeatPolicy
{
    /// <summary>
    /// CARD-0352 S1 allowlist entry (<c>SpecialistRoleContractTests</c>). V1's standing-specialist
    /// subsystem — routing, candidate states, health, physical seats and the task rows admitted to
    /// them — is Check-only by schema, and this file is the single place that says so. Every other
    /// service asks the members below instead of comparing the role itself, so declaring a second
    /// standing specialist is an edit here, not a hunt through eight services.
    /// </summary>
    public const AgentTaskRole Role = AgentTaskRole.Check;

    /// <summary>Any physical seat: the logical owner or one of its declared alternates.</summary>
    public static readonly Expression<Func<Agent, bool>> Seat = a => a.StandingSpecialistRole == Role;

    /// <summary>The logical owner — the seat that owns itself.</summary>
    public static readonly Expression<Func<Agent, bool>> Owner =
        a => a.StandingSpecialistRole == Role && a.StandingSpecialistOwnerId == a.Id;

    /// <summary>A declared alternate — a physical seat whose owner is somebody else.</summary>
    public static readonly Expression<Func<Agent, bool>> Alternate =
        a => a.StandingSpecialistRole == Role && a.StandingSpecialistOwnerId != a.Id;

    /// <summary>Work admitted to a standing seat.</summary>
    public static readonly Expression<Func<AgentTask, bool>> SeatWork = t => t.Role == Role;

    /// <summary>EF twin of <see cref="IsCheck(Agent, DelegationSettings)"/>, compatibility slug included.</summary>
    public static Expression<Func<Agent, bool>> SeatOrSlug(DelegationSettings settings)
    {
        var slug = CheckInterpreterProvisioner.Slug(settings);
        return a => a.StandingSpecialistRole == Role || a.Slug == slug;
    }

    /// <summary>EF twin of the logical-owner lookup, compatibility slug included.</summary>
    public static Expression<Func<Agent, bool>> OwnerOrSlug(DelegationSettings settings)
    {
        var slug = CheckInterpreterProvisioner.Slug(settings);
        return a => a.Slug == slug
            || (a.StandingSpecialistRole == Role && a.StandingSpecialistOwnerId == a.Id);
    }

    public static bool IsCheck(Agent agent) => agent.StandingSpecialistRole == Role;
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
