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
    Task<TaskWorktreeRetirement?> ReadRetirementAsync(Guid retirementId, CancellationToken ct)
        => Task.FromResult<TaskWorktreeRetirement?>(null);
    /// <summary>CARD-0665 D-4: the task row whose artifact pointers the ignored-content gate checks; null refuses.</summary>
    Task<AgentTask?> ReadTaskAsync(Guid taskId, CancellationToken ct)
        => Task.FromResult<AgentTask?>(null);
}
