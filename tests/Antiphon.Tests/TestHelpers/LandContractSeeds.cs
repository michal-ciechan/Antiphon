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

    /// <summary>
    /// CARD-0552 M-5. A project, a board with Backlog and Done columns and a Done original
    /// <c>CARD-0001</c>: the board state a companion needs somewhere to be created.
    /// </summary>
    public static async Task<(Guid BoardId, Guid OriginalCardId, Guid BacklogColumnId)>
        SeedBoardWithOriginalAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"c552-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/c552.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C552 contract",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var columns = new[] { CardStatus.Backlog, CardStatus.Done }
            .Select((status, order) => new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = board.Id, StateKey = status.ToString().ToLowerInvariant(),
                Name = status.ToString(), ColumnOrder = order, CardStatus = status,
                CreatedAt = now, UpdatedAt = now,
            }).ToArray();
        var original = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id,
            BoardColumnId = columns.Single(c => c.CardStatus == CardStatus.Done).Id,
            Identifier = "CARD-0001", Title = "CARD-0001 title", Description = "The original.",
            Status = CardStatus.Done, CompletedAt = now, TerminalReason = "closed by fixture",
            CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, original);
        db.AddRange(columns);
        await db.SaveChangesAsync();
        return (board.Id, original.Id, columns.Single(c => c.CardStatus == CardStatus.Backlog).Id);
    }

    /// <summary>
    /// CARD-0552 M-5. The C448_V33 confirmed-publication shape for an owner task. Pass
    /// <paramref name="remoteConfirmedAt"/> null for a landing that never confirmed.
    /// </summary>
    public static async Task<AgentTaskLanding> SeedConfirmedLandingAsync(
        AppDbContext db, AgentTask task, DateTime? remoteConfirmedAt, bool active = true)
    {
        var now = DateTime.UtcNow;
        var repo = task.RepoPath ?? task.WorkingDirectory;
        var sha = new string('b', 40);
        var op = new AgentTaskLanding
        {
            Id = Guid.NewGuid(), TaskId = task.Id, Active = active,
            Phase = remoteConfirmedAt is null ? LandPhase.PushStarted : LandPhase.PublicationConfirmed,
            Publication = remoteConfirmedAt is null
                ? LandPublicationOutcome.Unconfirmed : LandPublicationOutcome.Landed,
            Cleanup = LandCleanupStatus.Complete, Mode = LandOperationMode.Fresh,
            OriginalSourceSha = new string('c', 40), RebasedSourceSha = sha, VerifiedSourceSha = sha,
            ObservedRemoteTargetSha = remoteConfirmedAt is null ? null : sha,
            TargetBeforeSha = new string('a', 40),
            TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
            SourceFullRef = "refs/heads/source", RepositoryPath = repo, CommonDirectory = repo,
            WorktreePath = repo, GitDirectory = repo,
            SourcePinned = true, TargetPinned = true, PreparedPinned = true, VerificationPassed = true,
            VerifiedAt = now, RemoteFingerprint = new string('a', 64),
            RemoteConfirmedAt = remoteConfirmedAt,
            ConfirmationMethod = remoteConfirmedAt is null ? null : "push-endpoint-read-fetch-ancestry",
            CreatedAt = now, UpdatedAt = now,
        };
        op.RecoveryRefPrefix = $"refs/antiphon/land/{task.Id:N}/{op.Id:N}";
        db.AgentTaskLandings.Add(op);
        if (active) task.ActiveLandingId = op.Id;
        await db.SaveChangesAsync();
        return op;
    }

    private static string FullRef(string? branch) =>
        branch is null
            ? "refs/heads/missing"
            : branch.StartsWith("refs/", StringComparison.Ordinal)
                ? branch
                : "refs/heads/" + branch;
}
