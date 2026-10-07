using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1082 D-1/D-2. Pure rule for desktop sync debt. Settlement classifies once in
/// PrepareRemoteAsync, before progress, the block decision, and the debt row.
/// With <see cref="DelegationSettings.RunnerSyncDebtOnSettlement"/> off, every
/// method that classifies returns the sync result unchanged.
/// </summary>
public static class SettlementSyncDebtPolicy
{
    /// <summary>
    /// Returns <paramref name="result"/> with state <see cref="RemoteSettlementSyncState.Pending"/>
    /// when the only failure is a spent desktop lease and the server observed a full tip on the
    /// task's own owned ref. Otherwise the same instance. Role does not decide eligibility.
    /// A Code report with no progress evidence, and an indeterminate Code read, still block
    /// through <see cref="BlockReason"/>.
    /// </summary>
    public static RemoteSettlementSyncResult Classify(
        AgentTask task, RemoteSettlementSyncResult result, DelegationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(settings);
        if (!Eligible(task, result, settings))
            return result;
        return result with { State = RemoteSettlementSyncState.Pending };
    }

    /// <summary>
    /// Today's runner-sync block, plus the Pending arm. Pending blocks only a Code task:
    /// no progress evidence blocks with the lease reason, and an indeterminate read blocks
    /// with that evidence's reason, or the lease reason when the reason is absent. Every
    /// other Pending result may keep its own verdict. The non-Pending arms are unchanged.
    /// </summary>
    public static string? BlockReason(
        AgentTask task, RemoteSettlementSyncResult? prepared, CompletionProgressEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (prepared is null || prepared.State == RemoteSettlementSyncState.NotApplicable)
            return null;
        if (prepared.State == RemoteSettlementSyncState.Pending)
        {
            if (task.Role != AgentTaskRole.Code)
                return null;
            if (evidence is null)
                return RemoteSettlementSyncReasons.LeaseBusy;
            if (evidence.Assessment == CompletionProgressAssessment.Indeterminate)
                return evidence.Reason ?? RemoteSettlementSyncReasons.LeaseBusy;
            return null;
        }

        if (!prepared.Confirmed)
            return prepared.Reason ?? RemoteSettlementSyncReasons.InspectionUnavailable;
        if (task.Role == AgentTaskRole.Code
            && evidence is { Assessment: CompletionProgressAssessment.Indeterminate } uncertain)
            return uncertain.Reason ?? RemoteSettlementSyncReasons.InspectionUnavailable;
        return null;
    }

    /// <summary>
    /// Caller warning for a Pending settlement. Names the observed tip and says the desktop
    /// checkout will be fast-forwarded later. The text does not ask for a reply.
    /// </summary>
    public static string PendingWarning(AgentTask task, RemoteSettlementSyncResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        RequirePending(result);
        var sha = result.RemoteSha;
        return "Runner sync pending: " + RemoteSettlementSyncReasons.LeaseBusy
            + ". Origin " + result.FullRef + " is at " + sha
            + "; the desktop checkout " + task.WorktreePath
            + " will be fast-forwarded by the settlement sync sweep (synced later). "
            + "No reply is needed; Review and -Land use the pushed branch at " + sha + ".";
    }

    /// <summary>Workspace header bit: the branch is left for review against the observed tip.</summary>
    public static string WorkspaceNote(AgentTask task, RemoteSettlementSyncResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        RequirePending(result);
        return "branch " + task.WorktreeBranch + " left for review; source " + result.RemoteSha
            + " (desktop-sync=pending)";
    }

    private static void RequirePending(RemoteSettlementSyncResult result)
    {
        if (result.State != RemoteSettlementSyncState.Pending)
            throw new ArgumentException(
                "Pending text requires a Pending result; got " + result.State + ".", nameof(result));
    }

    private static bool Eligible(AgentTask task, RemoteSettlementSyncResult result, DelegationSettings settings) =>
        settings.RunnerSyncDebtOnSettlement
        && result.State == RemoteSettlementSyncState.Unavailable
        && result.Reason == RemoteSettlementSyncReasons.LeaseBusy
        && GitObjectId.IsFull(result.RemoteSha)
        && string.Equals(
            result.FullRef,
            "refs/heads/" + RemoteWorkspaceService.OwnedBranch(task.Id),
            StringComparison.Ordinal);
}
