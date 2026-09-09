using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class StandingContinuityState(AppDbContext db, TimeProvider clock)
{
    public const string HeldCode = "standing_continuity_held";

    public async Task HoldAsync(Guid agentId, Guid? sessionId, StandingContinuityReason reason, CancellationToken ct)
    {
        var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (state is null)
        {
            state = new AgentSupervisionState { AgentId = agentId };
            db.AgentSupervisionStates.Add(state);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = state.ContinuityHeldAt is null || state.ContinuitySessionId != sessionId || state.ContinuityReason != reason;
        state.ContinuityHeldAt ??= now;
        state.ContinuitySessionId = sessionId;
        state.ContinuityReason = reason;
        // Metadata only: exception text can contain credentials or provider transcript output.
        state.ContinuityEvidence = $"Standing conversation {sessionId?.ToString("D") ?? "unknown"}: {reason}. Inspect and repair, select an owned conversation, or explicitly start fresh.";
        state.NextRestartAt = null;
        state.UpdatedAt = now;
        if (changed) db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(), AgentId = agentId, SessionId = sessionId,
            Kind = AgentIncidentKind.StandingContinuityHeld, Severity = AlertSeverity.Info,
            Message = state.ContinuityEvidence, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
    }

    public void Clear(AgentSupervisionState state)
    {
        state.ContinuityHeldAt = null;
        state.ContinuitySessionId = null;
        state.ContinuityReason = null;
        state.ContinuityEvidence = null;
    }
}
