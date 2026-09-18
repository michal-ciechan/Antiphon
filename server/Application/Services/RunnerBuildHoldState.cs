using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0511 D-3/D-4. The durable runner-build hold, modelled on
/// <see cref="StandingContinuityState"/> — with one deliberate difference: this hold needs no
/// operator decision, because it has a precise machine-readable release signal (a different runner
/// identity). While held the agent costs nothing: no attempts, no incidents, no ladder. The trail
/// is exactly two rows — one Critical <c>RunnerBuildStale</c> on entry, one Info
/// <c>RunnerBuildReplaced</c> on release.
/// </summary>
public sealed class RunnerBuildHoldState(AppDbContext db, TimeProvider clock)
{
    public async Task HoldAsync(
        Guid agentId, Guid? sessionId, string identity, string evidence, CancellationToken ct)
    {
        var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = agentId };
            db.AgentSupervisionStates.Add(state);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        state.RunnerBuildHeldAt ??= now;
        state.RunnerBuildHeldIdentity = Trim(identity, 200);
        state.RunnerBuildHoldEvidence = Trim(evidence, 1000);
        // A hold is not a schedule. Leaving a due NextRestartAt would have the sweep attempt the
        // same stale runner on the very next tick.
        state.NextRestartAt = null;
        state.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The runner answered with a different identity than the hold was taken against: arm exactly
    /// one fresh attempt. Under D-1 that attempt either launches or re-holds on the NEW identity
    /// with its own Kind-29 row, so this can never become an unpaced retry loop.
    /// </summary>
    public async Task ReleaseAsync(
        AgentSupervisionState state, RunnerBuildDto? observed, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        Clear(state);
        state.NextRestartAt = now;
        state.UpdatedAt = now;
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = state.AgentId,
            SessionId = null,
            Kind = AgentIncidentKind.RunnerBuildReplaced,
            Severity = AlertSeverity.Info,
            Message = $"Runner replaced: built from {DescribeBuild(observed)} "
                + $"(running since {observed?.ProcessStartUtc:u}); retrying the standing session now.",
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
    }

    public void Clear(AgentSupervisionState state)
    {
        state.RunnerBuildHeldAt = null;
        state.RunnerBuildHeldIdentity = null;
        state.RunnerBuildHoldEvidence = null;
    }

    private static string DescribeBuild(RunnerBuildDto? build) =>
        build?.CommitSha is { Length: > 0 } sha
            ? sha[..Math.Min(7, sha.Length)]
            : build?.InformationalVersion ?? "an unknown commit";

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
