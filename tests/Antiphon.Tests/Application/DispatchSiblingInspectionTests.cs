using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class DispatchSiblingInspectionTests
{
    [Test]
    [Arguments("64 ancestors")]
    public async Task C1349_ContainedInventory(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-contained");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        for (var i = 0; i < 64; i++)
        {
            var id = Guid.NewGuid();
            var branch = "feat/card-task-" + id.ToString("N")[..8];
            await repo.GitAsync("branch", branch, "master");
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "contained", Goal = "contained",
                Workspace = WorkspaceMode.Worktree, CardId = card.Id,
                RepoPath = repo.Path, WorkingDirectory = repo.Path, WorktreeBranch = branch,
                Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        var trace = new RecordingGit();
        var selected = await ResolveAsync(db, NewRequest(repo, card.Id), trace);
        selected.Decision.ShouldBe(CardWorktreeBaseDecision.Target, scenario);
        selected.Incomplete.ShouldBeFalse();
        trace.Commands.Count(c => c[0] == "for-each-ref").ShouldBe(2);
        trace.Commands.ShouldNotContain(c => c[0] is "cherry" or "log");
        selected.GitCommands.ShouldBeLessThanOrEqualTo(4);
    }

    private sealed class RecordingGit : LandingGit
    {
        public List<string[]> Commands { get; } = [];
        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            Commands.Add(args.ToArray());
            return base.RunAsync(repository, args, ct);
        }
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<Card> CardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c442-" + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c442.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c442",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0442", Title = "Worktree continuation",
            Description = "Fixture", CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<AgentTask> SourceAsync(AppDbContext db, ScratchGitRepo repo,
        Guid cardId, string label, string startRef, bool commit = true)
    {
        var id = Guid.NewGuid();
        var branch = "feat/card-task-" + id.ToString("N")[..8];
        await repo.GitAsync("checkout", "-b", branch, startRef);
        if (commit) await repo.CommitFileAsync(label + ".txt", label + "\n");
        await repo.GitAsync("checkout", "master");
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = label, Goal = label,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path, RepoPath = repo.Path, CardId = cardId,
            WorktreeBranch = branch, Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static AgentTask NewRequest(ScratchGitRepo repo, Guid? cardId) => new()
    {
        Id = Guid.NewGuid(), CardId = cardId, RepoPath = repo.Path,
        Workspace = WorkspaceMode.Worktree,
    };

    private static Task<CardWorktreeBaseSelection> ResolveAsync(AppDbContext db, AgentTask request,
        ILandingGit? git = null) =>
        new AgentTaskWorktreeBaseResolver(db, git ?? new LandingGit(),
            Options.Create(new GitSettings { DefaultBranch = "master" }))
            .ResolveAsync(request, CancellationToken.None);

}
