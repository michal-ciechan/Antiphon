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
