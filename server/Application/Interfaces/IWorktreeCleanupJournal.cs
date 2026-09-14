using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeCleanupJournal
{
    Task<WorktreeCleanupAttempt> GetOrCreateAsync(WorktreeCleanupIdentity identity, CancellationToken ct);
    Task<WorktreeCleanupAttempt> ReadAsync(WorktreeCleanupContext context, CancellationToken ct);
    Task<bool> ConsumeSlotAsync(WorktreeCleanupContext context, Guid commandId, bool retry, CancellationToken ct);
    Task RecordOutcomeAsync(WorktreeCleanupContext context, WorktreeGitOutcome outcome, bool retry, bool failure, CancellationToken ct);
    Task CaptureAsync(WorktreeCleanupContext context, WorktreeCleanupCapture capture, CancellationToken ct);
    Task InterruptAsync(WorktreeCleanupContext context, string reason, CancellationToken ct);
    Task DecideAsync(WorktreeCleanupContext context, string reason, CancellationToken ct);
}
