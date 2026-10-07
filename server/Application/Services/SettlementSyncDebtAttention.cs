using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1082 D-7. Read-time projection of settlement sync debt. Held always warns.
/// Pending warns only when it is older than <see cref="DelegationSettings.RunnerSyncDebtAttentionMinutes"/>.
/// Ready and Superseded warn nothing.
/// </summary>
public static class SettlementSyncDebtAttention
{
    public static IReadOnlyList<AttentionItemDto> Build(
        IReadOnlyList<AgentTaskSyncDebt> debts,
        IReadOnlyList<AgentTask> tasks,
        DateTime now,
        DelegationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(debts);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(settings);
        var byId = new Dictionary<Guid, AgentTask>();
        foreach (var task in tasks)
            byId[task.Id] = task;
        var threshold = TimeSpan.FromMinutes(settings.RunnerSyncDebtAttentionMinutes);
        var items = new List<AttentionItemDto>();
        foreach (var debt in debts.OrderBy(d => d.Id))
        {
            if (!Warns(debt, now, threshold))
                continue;
            byId.TryGetValue(debt.TaskId, out var task);
            var held = debt.State == AgentTaskSyncDebtState.Held;
            var headline = held
                ? "Desktop sync debt held: " + debt.ReasonCode
                : "desktop sync still pending (lease contention)";
            var evidence = "task=" + debt.TaskId.ToString("D")
                + "; attempt=" + debt.Attempt
                + "; source=" + debt.SourceSha
                + "; reason=" + debt.ReasonCode;
            if (held)
                evidence += "; " + Recovery(debt.ReasonCode);
            items.Add(new AttentionItemDto(
                AttentionKind.SessionDisagreement,
                AlertSeverity.Warning,
                debt.TaskId,
                null,
                null,
                null,
                "Desktop sync debt",
                headline,
                evidence,
                debt.CreatedAt,
                null,
                task is null ? [] : [AttentionAction.OpenDrawer],
                CardId: task?.CardId,
                ConditionKey: "settlement-sync-debt:" + debt.Id.ToString("D")));
        }

        return items;
    }

    private static bool Warns(AgentTaskSyncDebt debt, DateTime now, TimeSpan threshold)
    {
        if (debt.State == AgentTaskSyncDebtState.Held)
            return true;
        if (debt.State != AgentTaskSyncDebtState.Pending)
            return false;
        return now - debt.CreatedAt > threshold;
    }

    /// <summary>Held recovery, the same sentences as Runner sync outcomes.</summary>
    private static string Recovery(string reason) => reason switch
    {
        RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged =>
            "The task attempt, baseline or worktree changed; this debt will not be fast-forwarded.",
        RemoteSettlementSyncReasons.TipNotReported =>
            "Origin moved past the recorded source; the sweep does not follow it.",
        RemoteSettlementSyncReasons.Diverged =>
            "Review that exact pushed S in a Worktree Review with -StartRef",
        RemoteSettlementSyncReasons.Dirty =>
            "The desktop checkout is dirty. This debt stays Held.",
        _ => "The sweep left this debt Held.",
    };
}
