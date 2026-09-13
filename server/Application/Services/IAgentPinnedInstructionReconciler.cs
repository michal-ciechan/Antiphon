namespace Antiphon.Server.Application.Services;

/// <summary>
/// Post-commit projection/notification work. S1 persists intent only; S3/S4 own file I/O and queue.
/// Never called inside the pin mutation transaction.
/// </summary>
public interface IAgentPinnedInstructionReconciler
{
    Task ReconcileAfterCommitAsync(Guid agentId, int revision, CancellationToken ct);
}
