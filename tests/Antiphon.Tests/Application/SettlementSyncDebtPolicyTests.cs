using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1082 S1. The pure sync-debt policy, before settlement calls it.</summary>
[Category("Unit")]
public sealed class SettlementSyncDebtPolicyTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string LongSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Desktop = "fedcba9876543210fedcba9876543210fedcba98";
    private const string FullRef = "refs/heads/feat/card-task-abcd1234";
    private const string Branch = "feat/card-task-abcd1234";
    private const string Checkout = "/desktop/card-task-abcd1234";

    [Test]
    public void C1082_LeaseBusyWithObservedTipClassifiesPending()
    {
        var settings = new DelegationSettings();
        var classified = SettlementSyncDebtPolicy.Classify(Task(), LeaseBusy(), settings);
        AssertPending(classified, Sha);

        var longTip = SettlementSyncDebtPolicy.Classify(Task(), LeaseBusy() with { RemoteSha = LongSha }, settings);
        AssertPending(longTip, LongSha);
    }

    [Test]
    public void C1082_DisabledSettingLeavesLeaseBusyUnavailable()
    {
        var result = LeaseBusy();
        var classified = SettlementSyncDebtPolicy.Classify(
            Task(), result, new DelegationSettings { RunnerSyncDebtOnSettlement = false });
        classified.ShouldBeSameAs(result);
        classified.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        classified.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
    }

    [Test]
    [Arguments(AgentTaskRole.Review, CompletionProgressAssessment.Indeterminate, null)]
    [Arguments(AgentTaskRole.Plan, CompletionProgressAssessment.NoAttributedProgress, null)]
    [Arguments(AgentTaskRole.Code, CompletionProgressAssessment.ProgressObserved, null)]
    [Arguments(AgentTaskRole.Code, CompletionProgressAssessment.Indeterminate, RemoteSettlementSyncReasons.LeaseBusy)]
    public void C1082_BlockReasonForPendingDependsOnRoleAndProgress(
        AgentTaskRole role, CompletionProgressAssessment assessment, string? expected)
    {
        var evidence = new CompletionProgressEvidence(1, assessment,
            assessment == CompletionProgressAssessment.Indeterminate ? "progress_read_other" : null);
        var reason = SettlementSyncDebtPolicy.BlockReason(
            Task(role), LeaseBusy() with { State = RemoteSettlementSyncState.Pending }, evidence);
        reason.ShouldBe(expected);
    }

    [Test]
    public void C1082_PendingEvidenceRecordsObservedNotConfirmed()
    {
        var result = new RemoteSettlementSyncResult(
            RemoteSettlementSyncState.Pending,
            RemoteSettlementSyncReasons.LeaseBusy,
            FullRef,
            Desktop,
            Sha,
            DesktopBeforeSha: Desktop,
            DesktopAfterSha: Desktop,
            SourceDescends: true);
        result.Confirmed.ShouldBeFalse();
        var evidence = RemoteSyncEvidence.From(3, result);
        evidence.Attempt.ShouldBe(3);
        evidence.State.ShouldBe(RemoteSettlementSyncState.Pending);
        evidence.ObservedSha.ShouldBe(Sha);
        evidence.ConfirmedSha.ShouldBeNull();
        evidence.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
    }

    [Test]
    public void C1082_PendingWarningSaysSyncedLaterAndNoReply()
    {
        var result = LeaseBusy() with { State = RemoteSettlementSyncState.Pending };
        var task = Task();
        var warning = SettlementSyncDebtPolicy.PendingWarning(task, result);
        warning.ShouldBe(
            "Runner sync pending: " + RemoteSettlementSyncReasons.LeaseBusy
            + ". Origin " + FullRef + " is at " + Sha
            + "; the desktop checkout " + Checkout
            + " will be fast-forwarded by the settlement sync sweep (synced later). "
            + "No reply is needed; Review and -Land use the pushed branch at " + Sha + ".");
        warning.ShouldContain("synced later");
        warning.ShouldContain("No reply is needed");
        warning.ShouldNotContain("then reply");
        SettlementSyncDebtPolicy.WorkspaceNote(task, result).ShouldBe(
            "branch " + Branch + " left for review; source " + Sha + " (desktop-sync=pending)");
    }

    [Test]
    [Arguments(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.Timeout)]
    [Arguments(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.FetchUnavailable)]
    [Arguments(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.DependencyUnavailable)]
    [Arguments(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.LeaseWaiting)]
    [Arguments(RemoteSettlementSyncState.Unavailable, RemoteSettlementSyncReasons.InspectionUnavailable)]
    [Arguments(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Dirty)]
    [Arguments(RemoteSettlementSyncState.Refused, RemoteSettlementSyncReasons.Diverged)]
    public void C1082_OtherReasonsAndStatesAreNeverPending(RemoteSettlementSyncState state, string reason)
    {
        var result = LeaseBusy() with { State = state, Reason = reason };
        var classified = SettlementSyncDebtPolicy.Classify(Task(), result, new DelegationSettings());
        classified.ShouldBeSameAs(result);
        classified.State.ShouldBe(state);
        classified.State.ShouldNotBe(RemoteSettlementSyncState.Pending);
    }

    [Test]
    [Arguments(null)]
    [Arguments("abc1234")]
    public void C1082_MissingOrShortObservedTipIsNeverPending(string? remoteSha)
    {
        var result = LeaseBusy() with { RemoteSha = remoteSha };
        var classified = SettlementSyncDebtPolicy.Classify(Task(), result, new DelegationSettings());
        classified.ShouldBeSameAs(result);
        classified.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        classified.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
    }

    [Test]
    [Arguments("Held")]
    [Arguments("StalePending")]
    [Arguments("FreshPending")]
    [Arguments("Ready")]
    public void C1082_AttentionWarnsOnHeldAndStalePendingDebt(string shape)
    {
        var now = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var taskId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var task = Task();
        task.Id = taskId;
        task.Attempt = 4;
        var settings = new DelegationSettings();
        settings.RunnerSyncDebtAttentionMinutes.ShouldBe(30);

        AgentTaskSyncDebt Debt(AgentTaskSyncDebtState state, DateTime created, string reason) => new()
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TaskId = taskId,
            Attempt = 4,
            State = state,
            SourceSha = Sha,
            ReasonCode = reason,
            CreatedAt = created,
        };

        var (state, created, reason, warns) = shape switch
        {
            "Held" => (AgentTaskSyncDebtState.Held, now,
                RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged, true),
            "StalePending" => (AgentTaskSyncDebtState.Pending, now.AddMinutes(-30).AddSeconds(-1),
                RemoteSettlementSyncReasons.LeaseBusy, true),
            "FreshPending" => (AgentTaskSyncDebtState.Pending, now.AddMinutes(-30),
                RemoteSettlementSyncReasons.LeaseBusy, false),
            "Ready" => (AgentTaskSyncDebtState.Ready, now.AddHours(-5),
                RemoteSettlementSyncReasons.SettlementSyncReady, false),
            _ => throw new InvalidOperationException(shape),
        };

        var items = SettlementSyncDebtAttention.Build([Debt(state, created, reason)], [task], now, settings);
        if (!warns)
        {
            items.ShouldBeEmpty();
            if (shape == "Ready")
            {
                SettlementSyncDebtAttention.Build(
                    [Debt(AgentTaskSyncDebtState.Superseded, now.AddHours(-5),
                        RemoteSettlementSyncReasons.SettlementSyncSuperseded)],
                    [task], now, settings).ShouldBeEmpty();
            }

            return;
        }

        var item = items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(AttentionKind.SessionDisagreement);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.TaskId.ShouldBe(taskId);
        item.ConditionKey.ShouldBe("settlement-sync-debt:11111111-2222-3333-4444-555555555555");
        item.Evidence.ShouldContain("task=aaaaaaaa-0000-0000-0000-000000000001");
        item.Evidence.ShouldContain("attempt=4");
        item.Evidence.ShouldContain("source=" + Sha);
        item.Evidence.ShouldContain("reason=" + reason);
        if (shape == "Held")
        {
            item.Headline.ShouldBe(
                "Desktop sync debt held: " + RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged);
            item.Evidence.ShouldNotContain("then reply");
            foreach (var (code, sentence) in HeldRecoveries)
            {
                var held = SettlementSyncDebtAttention.Build(
                    [Debt(AgentTaskSyncDebtState.Held, now, code)], [task], now, settings)
                    .ShouldHaveSingleItem();
                held.Headline.ShouldBe("Desktop sync debt held: " + code);
                held.Evidence.ShouldContain(sentence);
            }
        }
        else
        {
            item.Headline.ShouldBe("desktop sync still pending (lease contention)");
        }
    }

    private static readonly (string Code, string Sentence)[] HeldRecoveries =
    [
        (RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged,
            "The task attempt, baseline or worktree changed; this debt will not be fast-forwarded."),
        (RemoteSettlementSyncReasons.TipNotReported,
            "Origin moved past the recorded source; the sweep does not follow it."),
        (RemoteSettlementSyncReasons.Diverged,
            "Review that exact pushed S in a Worktree Review with -StartRef"),
        (RemoteSettlementSyncReasons.Dirty,
            "The desktop checkout is dirty. This debt stays Held."),
    ];

    private static void AssertPending(RemoteSettlementSyncResult classified, string sha)
    {
        classified.State.ShouldBe(RemoteSettlementSyncState.Pending);
        classified.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        classified.RemoteSha.ShouldBe(sha);
        classified.FullRef.ShouldBe(FullRef);
        classified.DesktopBeforeSha.ShouldBe(Desktop);
        classified.SourceDescends.ShouldBe(true);
        classified.Confirmed.ShouldBeFalse();
    }

    private static AgentTask Task(AgentTaskRole role = AgentTaskRole.Review) => new()
    {
        Role = role,
        WorktreeBranch = Branch,
        WorktreePath = Checkout,
    };

    private static RemoteSettlementSyncResult LeaseBusy() => new(
        RemoteSettlementSyncState.Unavailable,
        RemoteSettlementSyncReasons.LeaseBusy,
        FullRef,
        Desktop,
        Sha,
        DesktopBeforeSha: Desktop,
        SourceDescends: true);
}
