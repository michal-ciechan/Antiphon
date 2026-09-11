using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskReviewEvidenceTests
{
    [Test]
    public async Task C488_ManualFindingFieldsRestricted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await SeedAsync(db, AgentTaskRole.Review);
        var svc = new StageOutcomeService(db);
        var error = await Should.ThrowAsync<ValidationException>(() => svc.RecordFindingAsync(task.Id,
            new RecordStageFindingRequest("Verify", Found: false, ReviewedSourceSha: new string('a', 40)), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_fields_restricted");
        error = await Should.ThrowAsync<ValidationException>(() => svc.RecordFindingAsync(task.Id,
            new RecordStageFindingRequest("Review", Found: true, ReviewedSourceSha: new string('a', 40)), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_fields_restricted");
        (await db.StageOutcomes.CountAsync(o => o.ReviewedSourceSha != null)).ShouldBe(0);
    }

    [Test]
    public async Task C488_OverrideDoesNotCopyApproval()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await SeedAsync(db, AgentTaskRole.Review);
        db.StageOutcomes.Add(new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = task.Id, StageTaskId = task.Id,
            ReviewedSourceSha = new string('a', 40), ReviewedSourceRef = "refs/heads/feat/x",
            ReviewedRepositoryPath = task.RepoPath, RecordedAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        var written = await new StageOutcomeService(db).RecordFindingAsync(task.Id,
            new RecordStageFindingRequest("Review", Found: false, Detail: "override without sha"), CancellationToken.None);
        written.ReviewedSourceSha.ShouldBeNull();
        var old = await db.StageOutcomes.SingleAsync(o => o.ReviewedSourceSha != null);
        old.ReviewedSourceSha.ShouldBe(new string('a', 40));
        old.Id.ShouldNotBe(written.Id);
    }

    [Test]
    public async Task C488_ExplicitFindingEvidenceMatrix()
    {
        await C488_ManualFindingFieldsRestricted();
        await C488_OverrideDoesNotCopyApproval();
    }

    [Test]
    public async Task C488_ReviewEvidencePreservesStageRouting()
    {
        var report = """
            reviewed
            --- review evidence ---
            subjectTaskId: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
            reviewedSourceSha: 0123456789abcdef0123456789abcdef01234567
            --- next stage ---
            next: land
            handoff: bind SHA
            [antiphon-report:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee done]
            """;
        var parsed = PipelineHandoff.TryParse(report);
        parsed.Kind.ShouldBe(PipelineHandoffKind.Land);
        PipelineHandoff.HeaderBit(AgentTaskRole.Review, parsed).ShouldBe("land");
        ReviewEvidence.TryParse(report).Usable.ShouldBeTrue();
        var code = PipelineHandoff.TryParse("""
            done
            --- next stage ---
            next: mutation
            handoff: PCs pending
            [antiphon-report:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee done]
            """);
        PipelineHandoff.HeaderBit(AgentTaskRole.Code, code).ShouldBe("mutation");
    }

    private static async Task<AgentTask> SeedAsync(AppDbContext db, AgentTaskRole role)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "review", Goal = "review", Role = role,
            Kind = AgentTaskKind.Worker, Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
            WorkingDirectory = "C:/tmp/review", RepoPath = "C:/tmp/review", WorktreePath = "C:/tmp/review-tree",
            WorktreeBranch = "feat/review", CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
