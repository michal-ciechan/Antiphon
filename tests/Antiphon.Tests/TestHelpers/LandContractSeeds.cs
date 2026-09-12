using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Tests.TestHelpers;

/// <summary>HTTP-contract seeds copied from <c>AgentTaskLandApprovalRequestTests</c> (CARD-0495).</summary>
internal static class LandContractSeeds
{
    public static async Task<StageOutcome> SeedReviewAsync(
        AppDbContext db,
        AgentTask subject,
        string sha,
        StageOutcomeKind outcome = StageOutcomeKind.Clean,
        string? sourceRef = null,
        string? repository = null)
    {
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(),
            Stage = OrchestrationStage.Review,
            Outcome = outcome,
            Source = StageOutcomeSource.Delegate,
            SubjectTaskId = subject.Id,
            StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha,
            ReviewedSourceRef = sourceRef ?? FullRef(subject.WorktreeBranch),
            ReviewedRepositoryPath = repository ?? subject.RepoPath,
            RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    public static async Task<AgentTask> SeedSucceededWorktreeAsync(AppDbContext db, Guid? cardId = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "approval",
            Goal = "Land me.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "C:/tmp/land-approval",
            RepoPath = "C:/tmp/land-approval",
            WorktreePath = "C:/tmp/land-approval-tree",
            WorktreeBranch = $"feat/card-task-{DelegationReportFormatter.Short(id)}",
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            CardId = cardId,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    public static async Task<AgentTask> SeedWorktreeAsync(AppDbContext db, AgentTaskStatus status)
    {
        var task = await SeedSucceededWorktreeAsync(db);
        task.Status = status;
        await db.SaveChangesAsync();
        return task;
    }

    private static string FullRef(string? branch) =>
        branch is null
            ? "refs/heads/missing"
            : branch.StartsWith("refs/", StringComparison.Ordinal)
                ? branch
                : "refs/heads/" + branch;
}
