using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

internal static class ProviderSignInIncident
{
    public static async Task RecordAsync(
        AppDbContext db,
        AgentSupervisorService? supervisor,
        Guid? agentId,
        Guid? sessionId,
        string episodeKey,
        string reason,
        CancellationToken ct)
    {
        if (supervisor is not null)
        {
            await supervisor.RecordIncidentAsync(
                agentId, sessionId, AgentIncidentKind.ProviderSignInRequired,
                AlertSeverity.Critical, reason, failureReason: episodeKey, ct: ct);
            return;
        }

        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = AgentIncidentKind.ProviderSignInRequired,
            Severity = AlertSeverity.Critical,
            Message = ColumnText.Clip(reason, AgentIncident.MessageMaxLength),
            FailureReason = ColumnText.ClipOrNull(episodeKey, AgentIncident.FailureReasonMaxLength),
            CreatedAt = DateTime.UtcNow,
        });
    }
}
