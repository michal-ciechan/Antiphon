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
    [Arguments("wrong-verified-sha", false)]
    [Arguments("no-verified-time", false)]
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
            case "wrong-verified-sha": op.VerifiedSourceSha = new string('d', 40); break;
            case "no-verified-time": op.VerifiedAt = null; break;
        }
        var policy = new AgentTaskLandingState();
        policy.HasPublication(op).ShouldBe(accepted, "a skip label alone cannot establish verification of changed/unpinned commits or an omitted selected filter");
        if (accepted) policy.Transition(op, LandPhase.Verified, DateTime.UtcNow);
        else Should.Throw<InvalidOperationException>(() => policy.Transition(op, LandPhase.Verified, DateTime.UtcNow));
    }

    [Test]
    public void C488_UnknownVersionRefuses()
    {
        var op = ValidV2();
        op.SchemaVersion = 3;
        var policy = new AgentTaskLandingState();
        policy.HasPublication(op).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => policy.Transition(op, LandPhase.Verified, DateTime.UtcNow))
            .Message.ShouldBe("landing_schema_unsupported");
    }

    [Test]
    public void C488_V2ReceiptNeedsRequestBinding()
    {
        var op = ValidV2();
        op.ApprovalLandRequestId = Guid.Empty;
        new AgentTaskLandingState().HasPublication(op).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => new AgentTaskLandingState().Transition(op, LandPhase.Verified, DateTime.UtcNow));
    }

    [Test]
    public void C488_V2ReceiptNeedsApprovedOriginal()
    {
        var op = ValidV2();
        op.ReviewedSourceSha = new string('e', 40);
        new AgentTaskLandingState().HasPublication(op).ShouldBeFalse();
    }

    [Test]
    public void C488_V2ReceiptNeedsLineage()
    {
        var op = ValidV2();
        op.PreparationInputSha = new string('f', 40);
        op.PreviousPreparationOperationId = null;
        new AgentTaskLandingState().HasPublication(op).ShouldBeFalse();
        new AgentTaskLandingState().HasLineage(op).ShouldBeFalse();
    }

    [Test]
    public void C488_VerifiedCommitExact()
    {
        var op = ValidV2();
        op.VerifiedSourceSha = new string('e', 40);
        new AgentTaskLandingState().HasPublication(op).ShouldBeFalse();
    }

    [Test]
    public void C488_ContainedInputReceiptHasLineage()
    {
        var predecessor = Guid.NewGuid();
        var op = ValidV2();
        op.PreparationInputSha = new string('f', 40);
        op.PreviousPreparationOperationId = predecessor;
        op.RebasedSourceSha = null;
        op.VerifiedSourceSha = new string('f', 40);
        op.ObservedRemoteTargetSha = new string('f', 40);
        op.VerificationSkipReason = "exact_remote_containment";
        op.VerificationPassed = false;
        op.PreparedPinned = false;
        op.Phase = LandPhase.RecoveryPinned;
        var policy = new AgentTaskLandingState();
        policy.HasLineage(op).ShouldBeTrue();
        policy.HasPublication(op).ShouldBeTrue();
        policy.Transition(op, LandPhase.Verified, DateTime.UtcNow);
    }

    [Test]
    public void C488_VersionedReceiptMatrix()
    {
        var v1 = ValidV2();
        v1.SchemaVersion = 1;
        v1.ApprovalLandRequestId = null;
        v1.ReviewedSourceSha = null;
        v1.PreparationInputSha = null;
        new AgentTaskLandingState().HasPublication(v1).ShouldBeTrue();
        new AgentTaskLandingState().HasPublication(ValidV2()).ShouldBeTrue();
        var unknown = ValidV2();
        unknown.SchemaVersion = 9;
        new AgentTaskLandingState().HasPublication(unknown).ShouldBeFalse();
    }

    private static AgentTaskLanding ValidV2()
    {
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), SchemaVersion = 2,
            ApprovalLandRequestId = Guid.NewGuid(),
            SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            RepositoryPath = "repo", CommonDirectory = "common", GitDirectory = "git", WorktreePath = "tree",
            OriginalSourceSha = new string('a', 40), ReviewedSourceSha = new string('a', 40),
            PreparationInputSha = new string('a', 40),
            RebasedSourceSha = new string('a', 40), VerifiedSourceSha = new string('a', 40),
            TargetBeforeSha = new string('b', 40), ObservedRemoteTargetSha = new string('a', 40),
            RemoteFingerprint = new string('c', 64), RemoteConfirmedAt = DateTime.UtcNow, VerifiedAt = DateTime.UtcNow,
            Publication = LandPublicationOutcome.Landed, VerificationSkipReason = "base_unchanged",
            SourcePinned = true, TargetPinned = true, PreparedPinned = true,
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry", Phase = LandPhase.Prepared,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
        return op;
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

    [Test]
    [Arguments("reason:target_dirty_or_unknown", true)]
    [Arguments("reason:remote_read_failed", true)]
    [Arguments("reason:verification_failed", true)]
    [Arguments("reason:interrupted_rebase_requires_inspection", true)]
    [Arguments("reason:unknown", true)]
    [Arguments("reason:null", true)]
    [Arguments("source-ref", true)]
    [Arguments("source-sha", true)]
    [Arguments("target-ref", true)]
    [Arguments("common", true)]
    [Arguments("path", true)]
    [Arguments("automatic", false)]
    [Arguments("no-lease", false)]
    [Arguments("schema", false)]
    [Arguments("inspection", false)]
    [Arguments("wrong-task", false)]
    [Arguments("automatic-changed", false)]
    [Arguments("no-lease-changed", false)]
    [Arguments("phase:Inspected", false)]
    [Arguments("phase:RecoveryPinned", false)]
    [Arguments("phase:RebaseStarted", false)]
    [Arguments("phase:Prepared", false)]
    [Arguments("phase:Verified", false)]
    [Arguments("phase:TargetAdvanceStarted", false)]
    [Arguments("phase:LocalTargetAdvanced", false)]
    [Arguments("phase:PushStarted", false)]
    [Arguments("phase:PublicationConfirmed", false)]
    [Arguments("phase:CleanupStarted", false)]
    [Arguments("phase:Complete", false)]
    [Arguments("receipt:Landed", false)]
    [Arguments("receipt:AlreadyPresent", false)]
    [Arguments("refused-status", true)]
    public void RR_V7_RefusedReplacementEligibilityDependsOnlyOnAdmission(string row, bool expected)
    {
        var previous = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), Phase = LandPhase.Refused,
            SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            RepositoryPath = "repo", CommonDirectory = "common", GitDirectory = "git", WorktreePath = "tree",
            OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40), RemoteFingerprint = new string('c', 64),
        };
        previous.RecoveryRefPrefix = $"refs/antiphon/land/{previous.TaskId:N}/{previous.Id:N}";
        var snapshot = new LandSourceSnapshot(new(previous.TaskId, "repo", "tree", previous.SourceFullRef, previous.TargetFullRef),
            "common", "tree", "git", previous.SourceFullRef, previous.OriginalSourceSha, previous.OriginalSourceSha, "", []);
        var explicitRequest = true;
        var leaseHeld = true;
        if (row.StartsWith("reason:")) previous.LastReason = row == "reason:null" ? null : row[7..];
        if (row.StartsWith("phase:")) previous.Phase = Enum.Parse<LandPhase>(row[6..]);
        if (row.Contains("changed") || row == "source-sha") snapshot = snapshot with { HeadSha = new string('d', 40), BranchSha = new string('d', 40) };
        switch (row)
        {
            case "source-ref": snapshot = snapshot with { Coordinates = snapshot.Coordinates with { SourceFullRef = "refs/heads/other" }, SymbolicHead = "refs/heads/other" }; break;
            case "target-ref": snapshot = snapshot with { Coordinates = snapshot.Coordinates with { TargetFullRef = "refs/heads/other-target" } }; break;
            case "common": snapshot = snapshot with { CommonDirectory = "other-common" }; break;
            case "path": snapshot = snapshot with { RegisteredPath = "other-tree", Coordinates = snapshot.Coordinates with { WorktreePath = "other-tree" } }; break;
            case "automatic": case "automatic-changed": explicitRequest = false; break;
            case "no-lease": case "no-lease-changed": leaseHeld = false; break;
            case "schema": previous.SchemaVersion = 999; break;
            case "wrong-task": snapshot = snapshot with { Coordinates = snapshot.Coordinates with { TaskId = Guid.NewGuid() } }; break;
            case "refused-status": previous.Publication = LandPublicationOutcome.Refused; break;
        }
        var policy = new AgentTaskLandingState();
        if (row.StartsWith("receipt:"))
        {
            previous.Publication = Enum.Parse<LandPublicationOutcome>(row[8..]);
            previous.VerifiedSourceSha = previous.OriginalSourceSha;
            previous.VerifiedAt = previous.RemoteConfirmedAt = DateTime.UtcNow;
            previous.SourcePinned = previous.TargetPinned = previous.VerificationPassed = true;
            previous.ObservedRemoteTargetSha = previous.OriginalSourceSha;
            previous.ConfirmationMethod = "push-endpoint-read-fetch-ancestry";
            policy.HasPublication(previous).ShouldBeTrue();
        }
        var inspection = row == "inspection" ? new LandSourceInspection(null, "active_sequencer") : new(snapshot, null);
        var oldEvidence = System.Text.Json.JsonSerializer.Serialize(previous);
        var oldInspection = System.Text.Json.JsonSerializer.Serialize(inspection);
        policy.CanReplaceRefused(previous, inspection, explicitRequest, leaseHeld).ShouldBe(expected, row);
        System.Text.Json.JsonSerializer.Serialize(previous).ShouldBe(oldEvidence);
        System.Text.Json.JsonSerializer.Serialize(inspection).ShouldBe(oldInspection);
    }
}
