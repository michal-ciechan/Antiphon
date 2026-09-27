using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskWorktreeBaseResolverTests
{
    [Test]
    [Arguments("newer_review_at_ancestor")]
    [Arguments("equal_tip_completion")]
    [Arguments("equal_tip_id_tie")]
    public async Task T0442_V02_tip_order_is_ancestry_then_equal_tip_label(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v02");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var a = await SourceAsync(db, repo, card.Id, "A", "master");
        AgentTask expected;
        if (scenario == "newer_review_at_ancestor")
        {
            var b = await SourceAsync(db, repo, card.Id, "B", a.WorktreeBranch!);
            var review = await SourceAsync(db, repo, card.Id, "review", a.WorktreeBranch!, commit: false);
            review.Role = AgentTaskRole.Review;
            review.CompletedAt = DateTime.UtcNow;
            b.CompletedAt = DateTime.UtcNow.AddMinutes(-10);
            expected = b;
        }
        else
        {
            var alias = await SourceAsync(db, repo, card.Id, "alias", a.WorktreeBranch!, commit: false);
            if (scenario == "equal_tip_completion")
            {
                alias.CompletedAt = DateTime.UtcNow;
                a.CompletedAt = DateTime.UtcNow.AddMinutes(-10);
                expected = alias;
            }
            else
            {
                alias.CompletedAt = a.CompletedAt;
                expected = string.CompareOrdinal(a.Id.ToString("D"), alias.Id.ToString("D")) < 0 ? a : alias;
            }
        }
        await db.SaveChangesAsync();
        var sha = (await repo.GitReadAsync("rev-parse", expected.WorktreeBranch!)).Trim();
        var request = NewRequest(repo, card.Id);
        var first = await ResolveAsync(db, request);
        var second = await ResolveAsync(db, request);
        foreach (var selection in new[] { first, second })
        {
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selection.SourceTaskId.ShouldBe(expected.Id);
            selection.SourceSha.ShouldBe(sha);
            selection.CandidateWarnings.ShouldNotContain(w => w.Contains("competing tip", StringComparison.Ordinal));
        }
    }

    [Test]
    [Arguments(AgentTaskStatus.Blocked)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task T0442_V05_quiescent_non_success_requires_explicit_selection(AgentTaskStatus status)
    {
        using var repo = new ScratchGitRepo("c442-v05");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "A", "master");
        source.Status = status;
        await db.SaveChangesAsync();
        var auto = await ResolveAsync(db, NewRequest(repo, card.Id));
        auto.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        auto.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal)
            && w.Contains(status.ToString(), StringComparison.Ordinal));
        var request = NewRequest(repo, card.Id);
        request.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        request.RequestedWorktreeBaseTaskId = source.Id;
        var explicitSelection = await ResolveAsync(db, request);
        explicitSelection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        explicitSelection.SourceTaskId.ShouldBe(source.Id);
        explicitSelection.SourceSha.ShouldBe((await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim());
    }

    [Test]
    [Arguments("excluded_contained")]
    [Arguments("excluded_divergent")]
    public async Task T0442_V09_eligible_tip_keeps_excluded_history_visible(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v09");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var a = await SourceAsync(db, repo, card.Id, "A", "master");
        var b = await SourceAsync(db, repo, card.Id, "B", a.WorktreeBranch!);
        var excluded = scenario == "excluded_contained"
            ? await SourceAsync(db, repo, card.Id, "alias", a.WorktreeBranch!, commit: false)
            : await SourceAsync(db, repo, card.Id, "X", "master");
        excluded.Status = AgentTaskStatus.Failed;
        await db.SaveChangesAsync();
        var selection = await ResolveAsync(db, NewRequest(repo, card.Id));
        selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        selection.SourceTaskId.ShouldBe(b.Id);
        if (scenario == "excluded_divergent")
            selection.CandidateWarnings.ShouldContain(w => w.Contains(excluded.WorktreeBranch!, StringComparison.Ordinal));
        else
            selection.CandidateWarnings.ShouldNotContain(w => w.Contains(excluded.WorktreeBranch!, StringComparison.Ordinal)
                && w.Contains("not inherited", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("no_card_auto")]
    [Arguments("bound_no_candidates")]
    [Arguments("fresh_target")]
    public async Task T0442_V10_fallback_uses_configured_default_or_explicit_destination(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v10");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await repo.GitAsync("branch", "release");
        await repo.GitAsync("checkout", "-b", "topic");
        await repo.CommitFileAsync("topic.txt", "topic\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = scenario == "no_card_auto" ? null : await CardAsync(db);
        AgentTask? source = null;
        if (scenario == "fresh_target")
        {
            source = await SourceAsync(db, repo, card!.Id, "A", "master");
            await db.SaveChangesAsync();
        }
        foreach (var destination in new string?[] { null, "release" })
        {
            var request = NewRequest(repo, card?.Id);
            request.MergeTargetRef = destination;
            if (scenario == "fresh_target")
                request.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target;
            var selection = await ResolveAsync(db, request);
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
            selection.FallbackRef.ShouldBe(destination ?? "master"); // CARD-0508 configured default.
            selection.LandingTarget.ShouldBe(destination ?? "master");
            selection.SourceTaskId.ShouldBeNull();
            if (source is not null && destination is null)
                selection.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal));
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

    private static Task<CardWorktreeBaseSelection> ResolveAsync(AppDbContext db, AgentTask request) =>
        new AgentTaskWorktreeBaseResolver(db, new LandingGit(),
            Options.Create(new GitSettings { DefaultBranch = "master" }))
            .ResolveAsync(request, CancellationToken.None);
}
