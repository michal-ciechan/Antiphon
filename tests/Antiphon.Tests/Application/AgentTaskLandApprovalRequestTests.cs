using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class AgentTaskLandApprovalRequestTests
{
    private static readonly string ShaA = new('a', 40);
    private static readonly string ShaB = new('b', 40);
    private static readonly string ShaC = new('c', 40);

    [Test]
    public async Task C488_FreshApprovalRequired()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var error = await Should.ThrowAsync<ValidationException>(
            () => land.RequestAsync(task.Id, new LandAgentTaskRequest(), CancellationToken.None));
        error.Code.ShouldBe("expected_source_sha_required");
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == task.Id)).ShouldBe(0);
    }

    [Test]
    public async Task C488_FullOidRequired()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var error = await Should.ThrowAsync<ValidationException>(
            () => land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: "deadbee"), CancellationToken.None));
        error.Code.ShouldBe("expected_source_sha_invalid");
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C488_ApprovalAdmissionMatrix()
    {
        await C488_FreshApprovalRequired();
        await C488_FullOidRequired();
        await C488_CallerShaWithoutReviewIsValid();
    }

    [Test]
    public async Task C488_CallerShaWithoutReviewIsValid()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var result = await land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: ShaB), CancellationToken.None);
        result.Status.ShouldBe("queued");
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == result.RequestId)).ExpectedSourceSha.ShouldBe(ShaB);
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == result.RequestId)).ReviewEvidenceId.ShouldBeNull();
    }

    [Test]
    public async Task C488_EvidenceShaMatches()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var evidence = await SeedReviewAsync(db, task, ShaB);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaC, ReviewEvidenceId: evidence.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_sha_mismatch");
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C488_EvidenceSubjectMatches()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var owner = await SeedSucceededWorktreeAsync(db);
        var other = await SeedSucceededWorktreeAsync(db, cardId: owner.CardId);
        var evidence = await SeedReviewAsync(db, other, ShaB);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(owner.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB, ReviewEvidenceId: evidence.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_subject_mismatch");
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C488_EvidenceRefMatches()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var evidence = await SeedReviewAsync(db, task, ShaB, sourceRef: "refs/heads/other");
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB, ReviewEvidenceId: evidence.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_ref_mismatch");
    }

    [Test]
    public async Task C488_EvidenceRepositoryMatches()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var evidence = await SeedReviewAsync(db, task, ShaB, repository: "C:/other/repo");
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB, ReviewEvidenceId: evidence.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_repository_mismatch");
    }

    [Test]
    public async Task C488_EvidenceVerdictEligible()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var found = await SeedReviewAsync(db, task, ShaB, outcome: StageOutcomeKind.Found);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB, ReviewEvidenceId: found.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_ineligible");
    }

    [Test]
    public async Task C488_SupersededEvidenceRefuses()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var first = await SeedReviewAsync(db, task, ShaB);
        db.StageOutcomes.Add(new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Orchestrator, SubjectTaskId = task.Id, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = ShaC, ReviewedSourceRef = FullRef(task.WorktreeBranch),
            ReviewedRepositoryPath = task.RepoPath, SupersedesId = first.Id, RecordedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB, ReviewEvidenceId: first.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_superseded");
    }

    [Test]
    public async Task C488_PendingExpectedShaImmutable()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue, Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var first = await land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: ShaA), CancellationToken.None);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB), CancellationToken.None));
        error.Code.ShouldBe("land_request_identity_conflict");
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId)).ExpectedSourceSha.ShouldBe(ShaA);
    }

    [Test]
    public async Task C488_PendingEvidenceImmutable()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var evidence = await SeedReviewAsync(db, task, ShaA);
        var other = await SeedReviewAsync(db, task, ShaA);
        var first = await land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaA, ReviewEvidenceId: evidence.Id), CancellationToken.None);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaA, ReviewEvidenceId: other.Id), CancellationToken.None));
        error.Code.ShouldBe("land_request_identity_conflict");
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId)).ReviewEvidenceId.ShouldBe(evidence.Id);
    }

    [Test]
    public async Task C488_PendingFilterImmutable()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var first = await land.RequestAsync(task.Id, new LandAgentTaskRequest("keep", ShaA), CancellationToken.None);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest("changed", ShaA), CancellationToken.None));
        error.Code.ShouldBe("land_request_identity_conflict");
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId)).VerifyFilter.ShouldBe("keep");
    }

    [Test]
    public async Task C488_PendingAgeAndIdentityPreserved()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var clock = Frozen(DateTime.UtcNow);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue, clock);
        var task = await SeedSucceededWorktreeAsync(db);
        var first = await land.RequestAsync(task.Id, new LandAgentTaskRequest("keep", ShaA), CancellationToken.None);
        queue.Release(task.Id);
        clock.Advance(TimeSpan.FromHours(1));
        var again = await land.RequestAsync(task.Id, new LandAgentTaskRequest("keep", ShaA), CancellationToken.None);
        again.RequestId.ShouldBe(first.RequestId);
        var stored = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId);
        stored.RequestedAt.ShouldBe(clock.GetUtcNow().UtcDateTime.AddHours(-1));
        stored.Attempt.ShouldBe(0);
    }

    [Test]
    public async Task C488_PendingIdentityMatrix()
    {
        await C488_PendingExpectedShaImmutable();
        await C488_PendingEvidenceImmutable();
        await C488_PendingFilterImmutable();
        await C488_PendingAgeAndIdentityPreserved();
    }

    [Test]
    public async Task C488_ActiveRequestConflicts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var clock = Frozen(DateTime.UtcNow);
        var land = CreateLand(db, queue, clock);
        var task = await SeedSucceededWorktreeAsync(db);
        await land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: ShaA), CancellationToken.None);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaA), CancellationToken.None));
        error.Code.ShouldBe("land_running");
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == task.Id && r.IsPending)).ShouldBe(1);
    }

    [Test]
    public async Task C488_StaleWakeupDoesNotRun()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var land = CreateLand(db, new AgentTaskLandQueue(), Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var accepted = await land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: ShaA), CancellationToken.None);
        await land.RunRequestAsync(task.Id, Guid.NewGuid(), null, CancellationToken.None);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == task.Id)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id && e.IsLandTerminal)).ShouldBe(0);
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == accepted.RequestId)).IsPending.ShouldBeTrue();
    }

    [Test]
    public async Task C488_ConflictSupersessionEligibility()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var queue = new AgentTaskLandQueue();
        var land = CreateLand(db, queue, Frozen(DateTime.UtcNow));
        var task = await SeedSucceededWorktreeAsync(db);
        var first = await land.RequestAsync(task.Id, new LandAgentTaskRequest(ExpectedSourceSha: ShaA), CancellationToken.None);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB), CancellationToken.None));
        error.Code.ShouldBe("land_running");
        queue.Release(task.Id);
        error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(task.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: ShaB), CancellationToken.None));
        error.Code.ShouldBe("land_request_identity_conflict");
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId)).ExpectedSourceSha.ShouldBe(ShaA);
    }

    private static string FullRef(string? branch) =>
        branch is null ? "refs/heads/missing" : branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;

    private static async Task<StageOutcome> SeedReviewAsync(AppDbContext db, AgentTask subject, string sha,
        StageOutcomeKind outcome = StageOutcomeKind.Clean, string? sourceRef = null, string? repository = null)
    {
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = outcome,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = subject.Id, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha, ReviewedSourceRef = sourceRef ?? FullRef(subject.WorktreeBranch),
            ReviewedRepositoryPath = repository ?? subject.RepoPath, RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static async Task<AgentTask> SeedSucceededWorktreeAsync(AppDbContext db, Guid? cardId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "approval", Goal = "Land me.",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "C:/tmp/land-approval", RepoPath = "C:/tmp/land-approval",
            WorktreePath = "C:/tmp/land-approval-tree",
            WorktreeBranch = $"feat/card-task-{DelegationReportFormatter.Short(id)}",
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, CardId = cardId,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskLandService CreateLand(AppDbContext db, AgentTaskLandQueue queue, TimeProvider clock)
    {
        var manager = new WorktreeManager(Options.Create(new GitSettings { WorktreeBasePath = Path.GetTempPath() }),
            clock, NullLogger<WorktreeManager>.Instance);
        var worktrees = new DelegationWorktreeService(manager, new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance, new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }),
            new MockEventBus(), new RecordingSessionStopper(), clock, NullLogger<AgentTaskService>.Instance);
        return new AgentTaskLandService(db, worktrees, tasks, queue, null!, new MockEventBus(), clock,
            Options.Create(new DelegationSettings()), NullLogger<AgentTaskLandService>.Instance);
    }

    private static FakeTimeProvider Frozen(DateTime utc) =>
        new(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
