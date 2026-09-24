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
    Task<RemoteSettlementSyncResult> SyncAsync(AgentTask task, CancellationToken ct);
}
