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
