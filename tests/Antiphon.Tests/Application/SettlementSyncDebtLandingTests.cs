using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1082 V-29 / G-12. A Succeeded owner whose stored sync is Pending still lands on the
/// pushed branch when Clean review evidence names that SHA. Pending is not an approval and
/// not a refusal; the land leaves the stored sync unconfirmed.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SettlementSyncDebtLandingTests
{
    [Test]
    public async Task C1082_PendingSyncOwnerLandsOnPushedBranch()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var reviewed = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);

        Guid evidence;
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            owner.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(new(
                1, CompletionProgressAssessment.ProgressObserved,
                RemoteSync: new RemoteSyncEvidence(
                    1, RemoteSettlementSyncState.Pending,
                    "refs/heads/" + owner.WorktreeBranch, reviewed, null,
                    RemoteSettlementSyncReasons.LeaseBusy)));
            evidence = await AddReviewAsync(db, owner, reviewed);
        }

        var queued = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence);
        queued.Status.ShouldBe("queued");
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);

        var operation = (await h.OperationAsync()).ShouldNotBeNull();
        operation.Publication.ShouldBe(LandPublicationOutcome.Landed);
        operation.ReviewEvidenceId.ShouldBe(evidence);
        operation.OriginalSourceSha.ShouldBe(reviewed);

        await using var check = h.CreateContext();
        var terminal = await check.AgentTaskEvents.AsNoTracking().SingleAsync(e =>
            e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal);
        terminal.Type.ShouldBe(AgentTaskEventType.Landed);
        var stored = TaskProgressJson.TryReadEvidence(
            (await check.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId))
            .CompletionProgressEvidenceJson)!.RemoteSync!;
        stored.State.ShouldBe(RemoteSettlementSyncState.Pending);
        stored.ConfirmedSha.ShouldBeNull();
    }

    private static async Task<Guid> AddReviewAsync(AppDbContext db, AgentTask subject, string sha)
    {
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = subject.Id, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha, ReviewedSourceClean = true,
            ReviewedSourceRef = "refs/heads/" + subject.WorktreeBranch,
            ReviewedRepositoryPath = subject.RepoPath, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }
}
