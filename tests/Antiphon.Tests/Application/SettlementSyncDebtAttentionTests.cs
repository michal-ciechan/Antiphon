using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1446 S2 (V-5). The pure attention projection names the attempt count and the exhausted recovery.</summary>
[Category("Unit")]
public sealed class SettlementSyncDebtAttentionTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    /// A Held <c>settlement_sync_attempts_exhausted</c> debt warns with <c>attempts=&lt;n&gt;</c> in its evidence and
    /// the recovery sentence that names the count; a stale Pending item carries <c>attempts=</c> too.
    /// </summary>
    [Test]
    public void C1446_ExhaustedHeldDebtNamesAttemptsAndRecovery()
    {
        var now = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        var taskId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var task = new AgentTask { Id = taskId, RootTaskId = taskId, Attempt = 4 };
        var settings = new DelegationSettings();

        var exhausted = Debt(taskId, AgentTaskSyncDebtState.Held, "settlement_sync_attempts_exhausted", 2080, now.AddDays(-8));
        var items = SettlementSyncDebtAttention.Build([exhausted], [task], now, settings);
        items.Count.ShouldBe(1);
        var item = items[0];
        item.Headline.ShouldBe("Desktop sync debt held: settlement_sync_attempts_exhausted");
        item.Evidence.ShouldContain("reason=settlement_sync_attempts_exhausted");
        item.Evidence.ShouldContain("attempts=2080");
        item.Evidence.ShouldContain(
            "Retried 2080 times without a fast-forward. Run git pull --ff-only in the desktop worktree, or retire it.");
        item.Evidence.ShouldNotContain("The sweep left this debt Held.");
        item.ConditionKey.ShouldBe("settlement-sync-debt:" + exhausted.Id.ToString("D"));

        var pending = Debt(taskId, AgentTaskSyncDebtState.Pending, RemoteSettlementSyncReasons.LeaseBusy, 3,
            now.AddMinutes(-31));
        var pendingItems = SettlementSyncDebtAttention.Build([pending], [task], now, settings);
        pendingItems.Count.ShouldBe(1);
        pendingItems[0].Headline.ShouldBe("desktop sync still pending (lease contention)");
        pendingItems[0].Evidence.ShouldContain("attempts=3");
        pendingItems[0].Evidence.ShouldNotContain("Retried");
    }

    private static AgentTaskSyncDebt Debt(Guid taskId, AgentTaskSyncDebtState state, string reason, int attempts,
        DateTime created) => new()
    {
        Id = Guid.Parse("22222222-3333-4444-5555-666666666666"),
        TaskId = taskId,
        Attempt = 4,
        State = state,
        SourceSha = Sha,
        ReasonCode = reason,
        Attempts = attempts,
        CreatedAt = created,
        UpdatedAt = created,
    };
}
