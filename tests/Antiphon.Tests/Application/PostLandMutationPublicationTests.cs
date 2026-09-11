using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PostLandMutationPublicationTests
{
    [Test]
    [Arguments(LandPublicationOutcome.Landed, true)]
    [Arguments(LandPublicationOutcome.AlreadyPresent, true)]
    [Arguments(LandPublicationOutcome.Unconfirmed, false)]
    [Arguments(LandPublicationOutcome.Refused, false)]
    public void C478_V02_ConfirmedOutcomeMatrix(LandPublicationOutcome publication, bool accepted)
    {
        var op = Coherent();
        op.Publication = publication;
        if (publication == LandPublicationOutcome.Landed)
            op.Cleanup = LandCleanupStatus.Refused;
        new AgentTaskLandingState().HasPublication(op).ShouldBe(accepted);
    }

    [Test] public void C478_G027_Schema() { var op = Coherent(); op.SchemaVersion = 999; Reject(op); }
    [Test] public void C478_G028_OperationId() { var op = Coherent(); op.Id = Guid.Empty; Reject(op); }
    [Test] public void C478_G029_TaskId() { var op = Coherent(); op.TaskId = Guid.Empty; op.RecoveryRefPrefix = $"refs/antiphon/land/{Guid.Empty:N}/{op.Id:N}"; Reject(op); }
    [Test] public void C478_G030_OriginalOid() { var op = Coherent(); op.OriginalSourceSha = "not-an-oid"; Reject(op); }
    [Test] public void C478_G031_TargetOid() { var op = Coherent(); op.TargetBeforeSha = "not-an-oid"; Reject(op); }
    [Test] public void C478_G032_SourceRef() { var op = Coherent(); op.SourceFullRef = "refs/tags/source"; Reject(op); }
    [Test] public void C478_G033_TargetRef() { var op = Coherent(); op.TargetFullRef = op.DestinationFullRef = "refs/tags/target"; Reject(op); }
    [Test] public void C478_G034_DistinctRefs() { var op = Coherent(); op.SourceFullRef = op.TargetFullRef; Reject(op); }
    [Test] public void C478_G035_Destination() { var op = Coherent(); op.DestinationFullRef = "refs/heads/other"; Reject(op); }
    [Test] public void C478_G036_RepositoryIdentity() { var op = Coherent(); op.RepositoryPath = " "; Reject(op); }
    [Test] public void C478_G037_CommonIdentity() { var op = Coherent(); op.CommonDirectory = ""; Reject(op); }
    [Test] public void C478_G038_WorktreeIdentity() { var op = Coherent(); op.WorktreePath = ""; Reject(op); }
    [Test] public void C478_G039_GitIdentity() { var op = Coherent(); op.GitDirectory = ""; Reject(op); }
    [Test] public void C478_G040_Fingerprint() { var op = Coherent(); op.RemoteFingerprint = "short"; Reject(op); }
    [Test] public void C478_G041_Namespace() { var op = Coherent(); op.RecoveryRefPrefix = "refs/antiphon/land/other"; Reject(op); }
    [Test] public void C478_G042_ConfirmedTime() { var op = Coherent(); op.RemoteConfirmedAt = null; Reject(op); }
    [Test] public void C478_G043_PublicationKind() { var op = Coherent(); op.Publication = LandPublicationOutcome.Unconfirmed; Reject(op); }
    [Test] public void C478_G044_VerifiedOid() { var op = Coherent(); op.VerifiedSourceSha = "nope"; Reject(op); }
    [Test] public void C478_G045_ObservedOid() { var op = Coherent(); op.ObservedRemoteTargetSha = "nope"; Reject(op); }
    [Test] public void C478_G046_Method() { var op = Coherent(); op.ConfirmationMethod = "push-exit-code"; Reject(op); }
    [Test] public void C478_G047_VerifiedTime() { var op = Coherent(); op.VerifiedAt = null; Reject(op); }
    [Test] public void C478_G048_SourcePin() { var op = Coherent(); op.SourcePinned = false; Reject(op); }
    [Test] public void C478_G049_TargetPin() { var op = Coherent(); op.TargetPinned = false; Reject(op); }
    [Test] public void C478_G050_PreparedPin() { var op = Coherent(); op.RebasedSourceSha = op.VerifiedSourceSha = new string('e', 40); op.PreparedPinned = false; Reject(op); }
    [Test] public void C478_G051_VerifiedIdentity() { var op = Coherent(); op.VerifiedSourceSha = new string('e', 40); Reject(op); }
    [Test] public void C478_G052_VerificationVerdict() { var op = Coherent(); op.VerificationPassed = false; op.VerificationSkipReason = null; Reject(op); }
    [Test] public void C478_G053_UnchangedBase() { var op = Coherent(); op.VerificationPassed = false; op.VerificationSkipReason = "base_unchanged"; op.RebasedSourceSha = new string('e', 40); op.VerifiedSourceSha = new string('e', 40); op.PreparedPinned = true; Reject(op); }
    [Test] public void C478_G054_RequiredFilter() { var op = Coherent(); op.VerificationPassed = false; op.VerificationSkipReason = "base_unchanged"; op.VerificationFilter = "/*/*/Required/*"; Reject(op); }
    [Test] public void C478_G055_ContainmentSkip() { var op = Coherent(); op.VerificationPassed = false; op.VerificationSkipReason = "exact_remote_containment"; op.RebasedSourceSha = new string('e', 40); op.VerifiedSourceSha = new string('e', 40); op.PreparedPinned = true; Reject(op); }

    private static void Reject(AgentTaskLanding op) =>
        new AgentTaskLandingState().HasPublication(op).ShouldBeFalse();

    private static AgentTaskLanding Coherent()
    {
        var op = new AgentTaskLanding
        {
            SchemaVersion = 1,
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            SourceFullRef = "refs/heads/source",
            TargetFullRef = "refs/heads/master",
            DestinationFullRef = "refs/heads/master",
            RepositoryPath = "repo",
            CommonDirectory = "common",
            GitDirectory = "git",
            WorktreePath = "tree",
            OriginalSourceSha = new string('a', 40),
            VerifiedSourceSha = new string('a', 40),
            TargetBeforeSha = new string('b', 40),
            ObservedRemoteTargetSha = new string('c', 40),
            RemoteFingerprint = new string('d', 64),
            RemoteConfirmedAt = DateTime.UtcNow,
            VerifiedAt = DateTime.UtcNow,
            Publication = LandPublicationOutcome.Landed,
            VerificationPassed = true,
            SourcePinned = true,
            TargetPinned = true,
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry",
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{op.TaskId:N}/{op.Id:N}";
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        return op;
    }
}
