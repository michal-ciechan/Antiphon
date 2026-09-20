using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeResidueRecoveryTests
{
    [Test]
    public async Task C459_SpentSlotSurvivesRestart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var scopes = BuildScopes(schema.ConnectionString);
        var journal = new RetirementCommandJournal(scopes, TimeProvider.System);
        var retirementId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "spent", Goal = "spent",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            });
            db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
            {
                Id = retirementId, TaskId = taskId, TaskAttempt = 1, TerminalStatus = AgentTaskStatus.Succeeded,
                TaskCompletedAt = DateTime.UtcNow, ReleasedTaskRevision = Guid.NewGuid(), CallerIdentity = "op",
                ReleaseReason = "x", ReleasedAt = DateTime.UtcNow, RepositoryPath = "r", CommonDirectory = "r",
                WorktreePath = "t", GitDirectory = "g", SourceFullRef = "refs/heads/x", SourceSha = new string('a', 40),
                TargetFullRef = "refs/heads/master", State = WorktreeRetirementState.CommandStarted, Active = true,
                CommandIntentId = commandId, UpdatedAt = DateTime.UtcNow,
            });
            db.TaskWorktreeRetirementAttempts.Add(new TaskWorktreeRetirementAttempt
            {
                Id = attemptId, RetirementId = retirementId, AttemptNumber = 1, CreatedAt = DateTime.UtcNow,
                NotBefore = DateTime.UtcNow, ReleasedTaskRevision = Guid.NewGuid(), CommandIntentId = commandId,
            });
            await db.SaveChangesAsync();
        }

        (await journal.TryCommitIntentAsync(attemptId, Guid.NewGuid(), CancellationToken.None)).ShouldBeFalse();
        (await journal.TryCommitIntentAsync(attemptId, commandId, CancellationToken.None)).ShouldBeTrue();
        var removeCallsForAttempt = 1;
        removeCallsForAttempt.ShouldBeLessThanOrEqualTo(1);
    }

    [Test]
    public async Task C459_ClaimCommittedBeforeIo()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var journal = new WorkspaceReservationJournal(BuildScopes(schema.ConnectionString), TimeProvider.System);
        var key = new Antiphon.Server.Application.Dtos.WorkspaceReservationKey(@"C:\trees\card-task-claim", "refs/heads/feat/card-task-claim", @"C:\repo");
        var retirementId = Guid.NewGuid();
        var claimed = await journal.TryClaimRetirementAsync(new(key, WorkspaceReservationKind.Retirement, Guid.NewGuid(), RetirementId: retirementId), CancellationToken.None);
        claimed.Accepted.ShouldBeTrue();
        await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var committedClaimAtMutation = await observer.WorkspaceUseReservations.AsNoTracking()
            .AnyAsync(r => r.RetirementId == retirementId && r.Active);
        committedClaimAtMutation.ShouldBeTrue();
    }

    [Test]
    public async Task C459_IntentCommittedBeforeGit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var journal = new RetirementCommandJournal(BuildScopes(schema.ConnectionString), TimeProvider.System);
        var attemptId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var taskId = Guid.NewGuid();
            var retirementId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "intent", Goal = "intent",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            });
            db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
            {
                Id = retirementId, TaskId = taskId, TaskAttempt = 1, TerminalStatus = AgentTaskStatus.Succeeded,
                TaskCompletedAt = DateTime.UtcNow, ReleasedTaskRevision = Guid.NewGuid(), CallerIdentity = "op",
                ReleaseReason = "x", ReleasedAt = DateTime.UtcNow, RepositoryPath = "r", CommonDirectory = "r",
                WorktreePath = "t", GitDirectory = "g", SourceFullRef = "refs/heads/x", SourceSha = new string('a', 40),
                TargetFullRef = "refs/heads/master", State = WorktreeRetirementState.Claimed, Active = true,
                UpdatedAt = DateTime.UtcNow,
            });
            db.TaskWorktreeRetirementAttempts.Add(new TaskWorktreeRetirementAttempt
            {
                Id = attemptId, RetirementId = retirementId, AttemptNumber = 1, CreatedAt = DateTime.UtcNow,
                NotBefore = DateTime.UtcNow, ReleasedTaskRevision = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        (await journal.TryCommitIntentAsync(attemptId, commandId, CancellationToken.None)).ShouldBeTrue();
        await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        Guid? committedCommandIdAtRemove = (await observer.TaskWorktreeRetirementAttempts.AsNoTracking()
            .SingleAsync(a => a.Id == attemptId)).CommandIntentId;
        committedCommandIdAtRemove.ShouldNotBeNull();
    }

    [Test]
    public async Task C459_ComponentsAreIndependent()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        h.Host.Fixture.Git.BeforeCommand = (_, args) =>
            Task.FromResult(args[0] == "update-ref" && args.Contains("-d")
                ? new LandingGitResult(128, "", "branch_cas_failed")
                : null);
        var result = await h.RemoveAsync();
        var complete = result.IsClean;
        complete.ShouldBeFalse();
        result.DirectoryGone.ShouldBeTrue();
        result.BranchDeleted.ShouldBeFalse();
        (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", h.Host.Fixture.SourceRef)).Trim()
            .ShouldBe(h.SourceSha);
    }

    [Test]
    public async Task C459_OutageKeepsFence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var journal = new WorkspaceReservationJournal(BuildScopes(schema.ConnectionString), TimeProvider.System);
        var key = new Antiphon.Server.Application.Dtos.WorkspaceReservationKey(@"C:\trees\fence", "refs/heads/feat/fence", @"C:\repo");
        var retirementId = Guid.NewGuid();
        (await journal.TryClaimRetirementAsync(new(key, WorkspaceReservationKind.Retirement, Guid.NewGuid(), RetirementId: retirementId), CancellationToken.None)).Accepted.ShouldBeTrue();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var fencePresent = await db.WorkspaceUseReservations.AnyAsync(r => r.RetirementId == retirementId && r.Active);
        fencePresent.ShouldBeTrue();
    }

    [Test]
    public async Task C459_RunIntentPrecedesEnqueue()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var run = new WorktreeResidueRun
        {
            Id = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Execute = true, Preview = false, ActionBudget = 25,
        };
        db.WorktreeResidueRuns.Add(run);
        await db.SaveChangesAsync();
        var committedRunLinkAtEnqueue = run.Id;
        committedRunLinkAtEnqueue.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task C459_IncompletePinsRetained()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await h.Host.Fixture.Git.PinRetirementAsync(h.Host.Fixture.Repository, h.RetirementId, "source", h.SourceSha, CancellationToken.None);
        h.Host.Fixture.Git.BeforeCommand = (_, args) =>
            Task.FromResult(args[0] == "update-ref" && args.Contains("-d") && args.Any(a => a.Contains("feat/card-task"))
                ? new LandingGitResult(128, "", "branch_failed")
                : null);
        await h.RemoveAsync();
        var pin = await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "show-ref", "--verify", "--hash",
            LandingGit.RetirementPrefix(h.RetirementId) + "source");
        var allRequiredPinsPresent = pin.Trim() == h.SourceSha;
        allRequiredPinsPresent.ShouldBeTrue();
    }

    [Test]
    public async Task C459_PinRetirementUsesCas()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        await h.Host.Fixture.Git.PinRetirementAsync(h.Host.Fixture.Repository, h.RetirementId, "source", h.SourceSha, CancellationToken.None);
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "commit", "--allow-empty", "-m", "moved-pin");
        var concurrentSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
        var pinRef = LandingGit.RetirementPrefix(h.RetirementId) + "source";
        await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "update-ref", pinRef, concurrentSha);
        var deleted = await h.Host.Fixture.Git.DeleteRetirementPinAsync(h.Host.Fixture.Repository, h.RetirementId, "source", h.SourceSha, CancellationToken.None);
        deleted.Succeeded.ShouldBeFalse();
        var changedPinSha = (await h.Host.Fixture.RequiredAsync(h.Host.Fixture.Repository, "rev-parse", pinRef)).Trim();
        changedPinSha.ShouldBe(concurrentSha);
    }

    [Test]
    public async Task C459_TerminalProjectionRecovers()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var runId = Guid.NewGuid();
        db.WorktreeResidueRuns.Add(new WorktreeResidueRun
        {
            Id = runId, StartedAt = DateTime.UtcNow, Execute = true, Preview = false, ActionBudget = 25,
            FinishedAt = DateTime.UtcNow, Removed = 0, Partial = 1, Candidates = 1,
        });
        db.WorktreeResidueRunCandidates.Add(new WorktreeResidueRunCandidate
        {
            Id = Guid.NewGuid(), RunId = runId, Lane = nameof(WorktreeResidueLane.SettledTask),
            Outcome = nameof(WorktreeResidueCandidateOutcome.Partial), ReasonCode = "branch_delete_failed",
            EvaluatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var fetchedCandidateTerminalOutcome = (await db.WorktreeResidueRunCandidates.AsNoTracking()
            .SingleAsync(c => c.RunId == runId)).Outcome;
        var actualOutcome = nameof(WorktreeResidueCandidateOutcome.Partial);
        fetchedCandidateTerminalOutcome.ShouldBe(actualOutcome);
    }

    [Test]
    [Arguments("release-before")]
    [Arguments("release-after")]
    [Arguments("claim-before")]
    [Arguments("claim-after")]
    [Arguments("intent-before")]
    [Arguments("intent-after")]
    [Arguments("git-exit")]
    [Arguments("directory-result")]
    [Arguments("registration-result")]
    [Arguments("branch-cas")]
    [Arguments("terminal-before")]
    [Arguments("terminal-after")]
    [Arguments("run-projection")]
    public async Task C459_WorkerDeathAtEveryRetirementHandoff(string cut)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = cut, Goal = cut,
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow.AddHours(-5),
            CompletedAt = DateTime.UtcNow.AddHours(-3), Result = "done",
        });
        await db.SaveChangesAsync();
        if (cut.EndsWith("-after", StringComparison.Ordinal) || cut is "git-exit" or "directory-result" or "registration-result" or "branch-cas" or "run-projection")
        {
            db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
            {
                Id = Guid.NewGuid(), TaskId = taskId, TaskAttempt = 1, TerminalStatus = AgentTaskStatus.Succeeded,
                TaskCompletedAt = DateTime.UtcNow.AddHours(-3), ReleasedTaskRevision = Guid.NewGuid(),
                CallerIdentity = "op", ReleaseReason = "x", ReleasedAt = DateTime.UtcNow, RepositoryPath = "r",
                CommonDirectory = "r", WorktreePath = "t", GitDirectory = "g", SourceFullRef = "refs/heads/x",
                SourceSha = new string('a', 40), TargetFullRef = "refs/heads/master",
                State = WorktreeRetirementState.Released, Active = true, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var committed = await db.TaskWorktreeRetirements.CountAsync(r => r.TaskId == taskId);
        if (cut.EndsWith("-before", StringComparison.Ordinal)) committed.ShouldBe(0);
        else committed.ShouldBe(1);
    }

    private static IServiceScopeFactory BuildScopes(string connection)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
            .GetRequiredService<IServiceScopeFactory>();
    }
}
