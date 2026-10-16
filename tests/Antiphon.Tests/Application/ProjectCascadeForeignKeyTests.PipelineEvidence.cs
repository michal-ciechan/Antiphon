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

public partial class ProjectCascadeForeignKeyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Pipeline_evidence_is_purged_leaf_first_with_its_board_or_project(bool projectDelete)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var graph = await SeedGraphAsync(db);
        var evidence = graph.Scopes.Select(scope => AddPipelineEvidence(db, scope)).ToArray();
        var projectOnly = AddProjectEvidence(db, graph.Doomed.ProjectId, graph.Doomed.TaskId);
        var boardOnly = AddCorrection(db, graph.Doomed.ProjectId, graph.Doomed.BoardId, null, graph.Doomed.TaskId);
        await db.SaveChangesAsync();

        await DeleteAsync(db, graph.Doomed, projectDelete);

        await using var verify = CreateContext(schema);
        await AssertPipelineEvidenceAsync(verify, evidence[0], false);
        await AssertPipelineEvidenceAsync(verify, evidence[1], !projectDelete);
        await AssertPipelineEvidenceAsync(verify, evidence[2], true);
        (await verify.ReviewEvidenceCorrections.AnyAsync(c => c.Id == boardOnly.Id)).ShouldBeFalse();
        await AssertProjectEvidenceAsync(verify, projectOnly, !projectDelete);
        await AssertSurvivorsAsync(verify, graph, projectDelete);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Pipeline_evidence_is_restored_when_the_parent_delete_fails(bool projectDelete)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema, new FailBoardDelete());
        var graph = await SeedGraphAsync(db);
        var evidence = graph.Scopes.Select(scope => AddPipelineEvidence(db, scope)).ToArray();
        await db.SaveChangesAsync();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => DeleteAsync(db, graph.Doomed, projectDelete));
        error.Message.ShouldBe("CARD-1253: failure after cards and their dependents were deleted");

        await using var verify = CreateContext(schema);
        foreach (var row in evidence)
            await AssertPipelineEvidenceAsync(verify, row, true);
        foreach (var scope in graph.Scopes)
        {
            (await verify.Cards.AnyAsync(c => c.Id == scope.CardId)).ShouldBeTrue();
            (await verify.Boards.AnyAsync(b => b.Id == scope.BoardId)).ShouldBeTrue();
            (await verify.Projects.AnyAsync(p => p.Id == scope.ProjectId)).ShouldBeTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Pipeline_project_only_evidence_is_purged_with_an_empty_project_or_its_last_board(bool lastBoard)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var project = NewProject();
        var other = NewProject();
        db.Projects.AddRange(project, other);
        var task = NewTask(Guid.NewGuid(), project.Id);
        task.CardId = null;
        db.AgentTasks.Add(task);
        var doomed = AddProjectEvidence(db, project.Id, task.Id);
        var surviving = AddProjectEvidence(db, other.Id, task.Id);
        await db.SaveChangesAsync();

        if (lastBoard)
        {
            var boards = new BoardService(db, new MockEventBus(), TimeProvider.System);
            var board = await boards.CreateAsync(new CreateBoardRequest(project.Id, "Last"), CancellationToken.None);
            (await boards.DeleteAsync(board.Id, CancellationToken.None)).ProjectDeleted.ShouldBeTrue();
        }
        else
        {
            var scope = new Scope(project.Id, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, task.Id, Guid.Empty, Guid.Empty);
            await DeleteAsync(db, scope, projectDelete: true);
        }

        await using var verify = CreateContext(schema);
        await AssertProjectEvidenceAsync(verify, doomed, false);
        await AssertProjectEvidenceAsync(verify, surviving, true);
        (await verify.Projects.AnyAsync(p => p.Id == project.Id)).ShouldBeFalse();
        (await verify.Projects.AnyAsync(p => p.Id == other.Id)).ShouldBeTrue();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).ProjectId.ShouldBeNull();
    }

    private static PipelineEvidence AddPipelineEvidence(AppDbContext db, Scope scope)
    {
        var action = AddAction(db, scope.ProjectId, scope.CardId, scope.TaskId);
        var observation = AddObservation(db, action.Id);
        var correction = AddCorrection(db, scope.ProjectId, scope.BoardId, scope.CardId, scope.TaskId);
        var acceptance = new CardCompletionAcceptance
        {
            Id = Guid.NewGuid(), CardId = scope.CardId, BoardId = scope.BoardId, ProjectId = scope.ProjectId,
            Revision = 1, ImplementationTaskId = scope.TaskId, RecordedAt = DateTime.UtcNow
        };
        db.CardCompletionAcceptances.Add(acceptance);
        var receipt = new CardCompletionReceipt
        {
            Id = Guid.NewGuid(), CardId = scope.CardId, AcceptanceId = acceptance.Id, AcceptanceRevision = 1,
            OperationId = Guid.NewGuid(), ImplementationTaskId = scope.TaskId, RecordedAt = DateTime.UtcNow
        };
        db.CardCompletionReceipts.Add(receipt);
        return new PipelineEvidence(action.Id, observation.Id, correction.Id, acceptance.Id, receipt.Id);
    }

    private static ProjectEvidence AddProjectEvidence(AppDbContext db, Guid projectId, Guid taskId)
    {
        var action = AddAction(db, projectId, null, taskId);
        var observation = AddObservation(db, action.Id);
        var correction = AddCorrection(db, projectId, null, null, taskId);
        return new ProjectEvidence(action.Id, observation.Id, correction.Id);
    }

    private static PipelineHandoffAction AddAction(AppDbContext db, Guid projectId, Guid? cardId, Guid taskId)
    {
        var action = new PipelineHandoffAction
        {
            Id = Guid.NewGuid(), PredecessorTaskId = taskId, SettlementEventId = Guid.NewGuid(), Attempt = 1,
            NextStage = AgentTaskRole.Review, ProjectId = projectId, CardId = cardId, CreatedAt = DateTime.UtcNow
        };
        db.PipelineHandoffActions.Add(action);
        return action;
    }

    private static PipelineHandoffObservation AddObservation(AppDbContext db, Guid actionId)
    {
        var observation = new PipelineHandoffObservation
        {
            Id = Guid.NewGuid(), ActionId = actionId, Sequence = 1, ReasonCode = "eligible",
            Detail = "Retain the unrelated observation", ObservedAt = DateTime.UtcNow
        };
        db.PipelineHandoffObservations.Add(observation);
        return observation;
    }

    private static ReviewEvidenceCorrection AddCorrection(AppDbContext db, Guid projectId, Guid? boardId, Guid? cardId, Guid taskId)
    {
        var correction = new ReviewEvidenceCorrection
        {
            Id = Guid.NewGuid(), ReviewTaskId = taskId, SubjectTaskId = taskId, PredecessorOutcomeId = Guid.NewGuid(),
            ProjectId = projectId, BoardId = boardId, CardId = cardId, CanonicalBundleDigest = new string('a', 64),
            RecordedAt = DateTime.UtcNow
        };
        db.ReviewEvidenceCorrections.Add(correction);
        return correction;
    }

    private static async Task AssertPipelineEvidenceAsync(AppDbContext db, PipelineEvidence evidence, bool exists)
    {
        await AssertProjectEvidenceAsync(db, new ProjectEvidence(evidence.ActionId, evidence.ObservationId, evidence.CorrectionId), exists);
        (await db.CardCompletionAcceptances.AnyAsync(a => a.Id == evidence.AcceptanceId)).ShouldBe(exists);
        (await db.CardCompletionReceipts.AnyAsync(r => r.Id == evidence.ReceiptId)).ShouldBe(exists);
    }

    private static async Task AssertProjectEvidenceAsync(AppDbContext db, ProjectEvidence evidence, bool exists)
    {
        (await db.PipelineHandoffActions.AnyAsync(a => a.Id == evidence.ActionId)).ShouldBe(exists);
        (await db.PipelineHandoffObservations.AnyAsync(o => o.Id == evidence.ObservationId)).ShouldBe(exists);
        (await db.ReviewEvidenceCorrections.AnyAsync(c => c.Id == evidence.CorrectionId)).ShouldBe(exists);
    }

    private sealed record PipelineEvidence(Guid ActionId, Guid ObservationId, Guid CorrectionId, Guid AcceptanceId, Guid ReceiptId);
    private sealed record ProjectEvidence(Guid ActionId, Guid ObservationId, Guid CorrectionId);
}
