using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeRemovalEvidence
{
    Task<AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct);
    Task<VerificationRemovalAuthority?> ReadVerificationAsync(WorktreeRemovalRequest request, CancellationToken ct)
        => Task.FromResult<VerificationRemovalAuthority?>(null);
    Task<bool> RecordVerificationRemovalStartAsync(WorktreeRemovalRequest request, CancellationToken ct)
        => Task.FromResult(false);
}
