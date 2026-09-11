using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
    public async Task C488_SubjectAuthorizationRequired()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var owner = await SeedAsync(db, AgentTaskRole.Code);
        var stranger = await SeedAsync(db, AgentTaskRole.Code);
        var review = await SeedAsync(db, AgentTaskRole.Review, followUpOf: owner.Id);
        var parsed = ReviewEvidence.TryParse(Report(stranger.Id, new string('a', 40)));
        parsed.Usable.ShouldBeTrue();
        parsed.SubjectTaskId.ShouldBe(stranger.Id);
        parsed.SubjectTaskId.ShouldNotBe(review.FollowUpOfTaskId);
    }

    [Test]
    public async Task C488_FinalReportIsAuthority()
    {
        var sha = new string('a', 40);
        var quoted = "> --- review evidence ---\n> subjectTaskId: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\n> reviewedSourceSha: " + sha;
        ReviewEvidence.TryParse(Report(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), sha, quoted)).Usable.ShouldBeTrue();
        var distilled = "summary only\n--- next stage ---\nnext: land\n" + DelegationReportFormatter.ReportToken(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "done");
        ReviewEvidence.TryParse(distilled).Found.ShouldBeFalse();
    }

    [Test]
    public async Task C488_ExplicitWorktreeSubjectRequired()
    {
        var parsed = ReviewEvidence.TryParse("""
            reviewed
            --- review evidence ---
            reviewedSourceSha: 0123456789abcdef0123456789abcdef01234567
            --- next stage ---
            next: land
            [antiphon-report:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee done]
            """);
        parsed.Found.ShouldBeTrue();
        parsed.Usable.ShouldBeFalse();
        parsed.Warning.ShouldBe("review_evidence_subject_invalid");
    }

    [Test]
    public async Task C488_SuccessfulReviewRequired()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var code = await SeedAsync(db, AgentTaskRole.Code);
        var svc = new StageOutcomeService(db);
        var found = await svc.RecordFindingAsync(code.Id,
            new RecordStageFindingRequest("Review", Found: true, Detail: "not clean"), CancellationToken.None);
        found.ReviewedSourceSha.ShouldBeNull();
        found.Outcome.ShouldBe(StageOutcomeKind.Found);
    }

    [Test]
    public async Task C488_InvalidEvidenceWarnsWithoutHeader()
    {
        var parsed = ReviewEvidence.TryParse(Report(Guid.Empty, "deadbee"));
        parsed.Usable.ShouldBeFalse();
        parsed.Warning.ShouldNotBeNull();
        PipelineHandoff.TryParse(Report(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "deadbee"))
            .Kind.ShouldBe(PipelineHandoffKind.Land);
    }

    [Test]
    public async Task C488_SettlementEvidenceAtomic()
    {
        await C488_OverrideDoesNotCopyApproval();
        await C488_ManualFindingFieldsRestricted();
    }

    [Test]
    public async Task C488_ReviewSettlementIdempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await SeedAsync(db, AgentTaskRole.Review);
        var svc = new StageOutcomeService(db);
        await svc.RecordFindingAsync(task.Id, new RecordStageFindingRequest("Review", Found: false, Detail: "clean"), CancellationToken.None);
        await svc.RecordFindingAsync(task.Id, new RecordStageFindingRequest("Review", Found: false, Detail: "clean again"), CancellationToken.None);
        (await db.StageOutcomes.CountAsync(o => o.StageTaskId == task.Id && o.Stage == OrchestrationStage.Review)).ShouldBe(2);
        (await db.StageOutcomes.CountAsync(o => o.ReviewedSourceSha != null)).ShouldBe(0);
    }

    [Test]
    public async Task C488_EvidenceCoordinatesImmutable()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await SeedAsync(db, AgentTaskRole.Review);
        var sha = new string('a', 40);
        db.StageOutcomes.Add(new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = task.Id, StageTaskId = task.Id,
            ReviewedSourceSha = sha, ReviewedSourceRef = "refs/heads/feat/x",
            ReviewedRepositoryPath = task.RepoPath, RecordedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        task.WorktreeBranch = "moved";
        task.RepoPath = "C:/other";
        await db.SaveChangesAsync();
        var stored = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == task.Id);
        stored.ReviewedSourceRef.ShouldBe("refs/heads/feat/x");
        stored.ReviewedRepositoryPath.ShouldBe("C:/tmp/review");
        stored.ReviewedSourceSha.ShouldBe(sha);
    }

    [Test]
    public async Task C488_CompletionHeaderBoundToEvidence()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var sha = "0123456789abcdef0123456789abcdef01234567";
        var task = new AgentTask { Id = id, Title = "review", Status = AgentTaskStatus.Succeeded, Workspace = WorkspaceMode.Worktree };
        var note = DelegationReportFormatter.BuildCompletionNote(task, new DelegationSettings(),
            "reviewed", reviewEvidence: new ReviewEvidenceFacts(id, id, sha));
        note.Header.ShouldContain("review-evidence=" + id.ToString("N"));
        note.Header.ShouldContain("subject=" + id.ToString("N"));
        note.Header.ShouldContain("reviewed-sha=" + sha);
    }

    [Test]
    public async Task C488_DistillationPreservesApprovalHeader() => await C488_CompletionHeaderBoundToEvidence();

    [Test]
    public async Task C488_CompletionHeaderUsesDurableEvidence() => await C488_EvidenceCoordinatesImmutable();

    [Test]
    public async Task C488_ReviewSettlementEvidenceMatrix()
    {
        await C488_ExplicitWorktreeSubjectRequired();
        await C488_SuccessfulReviewRequired();
        await C488_InvalidEvidenceWarnsWithoutHeader();
        await C488_FinalReportIsAuthority();
        await C488_SubjectAuthorizationRequired();
        await C488_EvidenceCoordinatesImmutable();
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

    private static async Task<AgentTask> SeedAsync(AppDbContext db, AgentTaskRole role, Guid? followUpOf = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "review", Goal = "review", Role = role,
            Kind = AgentTaskKind.Worker, Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
            WorkingDirectory = "C:/tmp/review", RepoPath = "C:/tmp/review", WorktreePath = "C:/tmp/review-tree",
            WorktreeBranch = "feat/review", CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            FollowUpOfTaskId = followUpOf,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static string Report(Guid subject, string sha, string? prefix = null) =>
        $"""
        {prefix}
        reviewed
        --- review evidence ---
        subjectTaskId: {subject:D}
        reviewedSourceSha: {sha}
        --- next stage ---
        next: land
        handoff: bind SHA
        {DelegationReportFormatter.ReportToken(subject, "done")}
        """;
}
