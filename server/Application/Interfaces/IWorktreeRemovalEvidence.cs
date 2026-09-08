using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeRemovalEvidence
{
    Task<AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct);
}
