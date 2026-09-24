using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0657 D-1. Prepares a runner-bound Worktree task's canonical desktop checkout before any
/// Git-dependent completion decision. Implemented by the remote workspace service; progress
/// evaluation depends on this seam rather than on the runner transport.
/// </summary>
public interface IRemoteSettlementSync
{
    /// <param name="reportedTips">Bind-refusal recovery only: the commits its correlated evidence
    /// names. When given, origin's tip must be one of them, checked before any checkout mutation.</param>
    Task<RemoteSettlementSyncResult> SyncAsync(
        AgentTask task, CancellationToken ct, IReadOnlyCollection<string>? reportedTips = null);
}
