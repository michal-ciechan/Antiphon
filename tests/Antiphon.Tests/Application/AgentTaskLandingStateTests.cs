using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class AgentTaskLandingStateTests
{
    [Test]
    [Arguments("valid-base-unchanged", true)]
    [Arguments("valid-exact-containment", true)]
    [Arguments("valid-verified-rebase", true)]
    [Arguments("base-changed", false)]
    [Arguments("selected-filter-skipped", false)]
    [Arguments("prepared-unpinned", false)]
    [Arguments("source-unpinned", false)]
    [Arguments("target-unpinned", false)]
    [Arguments("containment-after-rebase", false)]
    [Arguments("unknown-skip", false)]
    [Arguments("no-verification", false)]
    public void C448_V31_VerificationEvidenceMustDescribeTheExactCommit(string variant, bool accepted)
    {
        var op = new AgentTaskLanding { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(),
            SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            RepositoryPath = "repo", CommonDirectory = "common", GitDirectory = "git", WorktreePath = "tree",
            OriginalSourceSha = new string('a', 40), RebasedSourceSha = new string('a', 40), VerifiedSourceSha = new string('a', 40),
            TargetBeforeSha = new string('b', 40), ObservedRemoteTargetSha = new string('a', 40),
            RemoteFingerprint = new string('c', 64), RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
            Publication = LandPublicationOutcome.Landed, VerificationSkipReason = "base_unchanged",
            SourcePinned = true, TargetPinned = true, PreparedPinned = true,
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry", Phase = LandPhase.Prepared };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
        switch (variant)
        {
            case "valid-exact-containment": op.RebasedSourceSha = null; op.VerificationSkipReason = "exact_remote_containment"; op.Phase = LandPhase.RecoveryPinned; break;
            case "valid-verified-rebase": op.RebasedSourceSha = op.VerifiedSourceSha = new string('d', 40); op.VerificationSkipReason = null; op.VerificationPassed = true; break;
            case "base-changed": op.RebasedSourceSha = op.VerifiedSourceSha = new string('d', 40); break;
            case "selected-filter-skipped": op.VerificationFilter = "/*/*/Required/*"; break;
            case "prepared-unpinned": op.PreparedPinned = false; break;
            case "source-unpinned": op.SourcePinned = false; break;
            case "target-unpinned": op.TargetPinned = false; break;
            case "containment-after-rebase": op.VerificationSkipReason = "exact_remote_containment"; break;
            case "unknown-skip": op.VerificationSkipReason = "report says passed"; break;
            case "no-verification": op.VerificationSkipReason = null; break;
        }
        var policy = new AgentTaskLandingState();
        policy.HasPublication(op).ShouldBe(accepted, "a skip label alone cannot establish verification of changed/unpinned commits or an omitted selected filter");
        if (accepted) policy.Transition(op, LandPhase.Verified, DateTime.UtcNow);
        else Should.Throw<InvalidOperationException>(() => policy.Transition(op, LandPhase.Verified, DateTime.UtcNow));
    }

    [Test]
    public void C448_V22_StageDurationsUseTheirOwnAcknowledgedIntervals()
    {
        var start = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var op = new AgentTaskLanding { RebaseStartedAt = start, PreparedAt = start.AddSeconds(7),
            VerificationStartedAt = start.AddSeconds(10), VerifiedAt = start.AddSeconds(31),
            CleanupStartedAt = start.AddHours(1), CleanupCompletedAt = start.AddHours(1).AddSeconds(4),
            UpdatedAt = start.AddHours(2) };
        AgentTaskLandService.DurationSeconds(op, OrchestrationStage.Rebase).ShouldBe(7);
        AgentTaskLandService.DurationSeconds(op, OrchestrationStage.Verify).ShouldBe(21);
        AgentTaskLandService.DurationSeconds(op, OrchestrationStage.Cleanup).ShouldBe(4);
        AgentTaskLandService.DurationSeconds(new AgentTaskLanding(), OrchestrationStage.Verify).ShouldBe(0);
    }

    [Test]
    public void C448_V31_TransitionsRequireEvidence()
    {
        var policy = new AgentTaskLandingState();
        var operation = new AgentTaskLanding();
        Should.Throw<InvalidOperationException>(() => policy.Transition(operation, LandPhase.RecoveryPinned, DateTime.UtcNow));
        operation.SourcePinned = operation.TargetPinned = true;
        Should.Throw<InvalidOperationException>(() => policy.Transition(operation, LandPhase.RecoveryPinned, DateTime.UtcNow));
        operation.Id = Guid.NewGuid();
        operation.TaskId = Guid.NewGuid();
        operation.OriginalSourceSha = operation.TargetBeforeSha = new string('b', 40);
        operation.SourceFullRef = "refs/heads/source";
        operation.TargetFullRef = operation.DestinationFullRef = "refs/heads/master";
        operation.RepositoryPath = operation.CommonDirectory = operation.WorktreePath = operation.GitDirectory = "fixture";
        operation.RemoteFingerprint = new string('a', 64);
        operation.RecoveryRefPrefix = $"refs/antiphon/land/{operation.TaskId:N}/{operation.Id:N}";
        policy.Transition(operation, LandPhase.RecoveryPinned, DateTime.UtcNow);
        policy.Transition(operation, LandPhase.RebaseStarted, DateTime.UtcNow);
        Should.Throw<InvalidOperationException>(() => policy.Transition(operation, LandPhase.Verified, DateTime.UtcNow));
        operation.RebasedSourceSha = new string('a', 40);
        operation.PreparedPinned = true;
        policy.Transition(operation, LandPhase.Prepared, DateTime.UtcNow);
        Should.Throw<InvalidOperationException>(() => policy.Transition(operation, LandPhase.Verified, DateTime.UtcNow));
        operation.VerifiedSourceSha = operation.RebasedSourceSha;
        operation.VerifiedAt = DateTime.UtcNow;
        operation.VerificationPassed = true;
        policy.Transition(operation, LandPhase.Verified, DateTime.UtcNow);
        Should.Throw<InvalidOperationException>(() => policy.Transition(operation, LandPhase.PublicationConfirmed, DateTime.UtcNow));
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public void C448_V17_ResolvedInterruptedRebaseCanOpenNewOperation(bool explicitRequest, bool leaseHeld)
    {
        var policy = new AgentTaskLandingState();
        var previous = new AgentTaskLanding
        {
            TaskId = Guid.NewGuid(), Phase = LandPhase.Refused, LastReason = "interrupted_rebase_requires_inspection",
            SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master",
            OriginalSourceSha = new string('a', 40), CommonDirectory = "common", WorktreePath = "tree",
        };
        var inspection = new LandSourceInspection(new(new(previous.TaskId, "repo", "tree", previous.SourceFullRef,
                previous.TargetFullRef), "common", "tree", "git", previous.SourceFullRef,
            previous.OriginalSourceSha, previous.OriginalSourceSha, "", []), null);
        policy.CanReplaceRefused(previous, inspection, explicitRequest, leaseHeld).ShouldBe(explicitRequest && leaseHeld);
        policy.CanReplaceRefused(previous, new(null, "active_sequencer"), explicitRequest, leaseHeld).ShouldBeFalse();
        previous.Phase.ShouldBe(LandPhase.Refused);
        previous.OriginalSourceSha.ShouldBe(new string('a', 40));
    }
}
