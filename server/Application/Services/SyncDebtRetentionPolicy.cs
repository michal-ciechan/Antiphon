using System.Linq.Expressions;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>The unpaid settlement syncs that keep a worktree in either retention policy.</summary>
internal static class SyncDebtRetentionPolicy
{
    // Keep this as an expression so both the batch census and pre-action rechecks filter in SQL.
    // Ready needs its confirmation; equal missing/blank evidence is not payment.
    public static Expression<Func<AgentTaskSyncDebt, bool>> Unpaid => debt =>
        debt.State == AgentTaskSyncDebtState.Pending
        || debt.State == AgentTaskSyncDebtState.Held
        || (debt.State == AgentTaskSyncDebtState.Ready
            && (string.IsNullOrWhiteSpace(debt.SourceSha) || debt.ConfirmedSha != debt.SourceSha));
}
