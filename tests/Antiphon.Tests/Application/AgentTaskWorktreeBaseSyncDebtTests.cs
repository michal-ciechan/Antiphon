using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1480: a same-card sibling that settled with an unpaid desktop sync debt owes a
/// fast-forward of its local branch to the pushed source S. The resolver must examine S
/// (when the local object store proves it fast-forwards the local tip) or refuse by name,
/// never silently use the stale local tip.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<GitProcessSpawnLimit>]
public class AgentTaskWorktreeBaseSyncDebtTests
{
    private const string AbsentSha = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task C1480_auto_inherits_settled_source_over_merged_local_tip()
    {
        using var repo = new ScratchGitRepo("c1480-auto");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var world = await SeedAsync(db, repo);
        AddDebt(db, world.Owner, world.B, world.S, AgentTaskSyncDebtState.Pending);
        await db.SaveChangesAsync();

        var selected = await ResolveAsync(db, NewRequest(repo, world.Card.Id));

        selected.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        selected.SourceTaskId.ShouldBe(world.Owner.Id);
        selected.SourceBranch.ShouldBe(world.Owner.WorktreeBranch);
        selected.SourceSha.ShouldBe(world.S);
        selected.CandidateWarnings.ShouldContain(w => w.Contains(world.B, StringComparison.Ordinal)
            && w.Contains(world.S, StringComparison.Ordinal) && w.Contains("Pending", StringComparison.Ordinal));
    }

    [Test]
    public async Task C1480_explicit_task_uses_settled_source()
    {
        using var repo = new ScratchGitRepo("c1480-task");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var world = await SeedAsync(db, repo);
        AddDebt(db, world.Owner, world.B, world.S, AgentTaskSyncDebtState.Pending);
        await db.SaveChangesAsync();
        var request = NewRequest(repo, world.Card.Id);
        request.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        request.RequestedWorktreeBaseTaskId = world.Owner.Id;

        var selected = await ResolveAsync(db, request);

        selected.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        selected.SourceTaskId.ShouldBe(world.Owner.Id);
        selected.SourceSha.ShouldBe(world.S);
    }

    [Test]
    [Arguments("absent", RequestedWorktreeBaseMode.Auto, AgentTaskSyncDebtState.Pending)]
    [Arguments("absent", RequestedWorktreeBaseMode.Task, AgentTaskSyncDebtState.Held)]
    [Arguments("diverged", RequestedWorktreeBaseMode.Auto, AgentTaskSyncDebtState.Superseded)]
    public async Task C1480_unverifiable_settled_source_refuses_by_name(string shape,
        RequestedWorktreeBaseMode mode, AgentTaskSyncDebtState state)
    {
        using var repo = new ScratchGitRepo("c1480-unverified");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var world = await SeedAsync(db, repo);
        var settled = shape == "absent" ? AbsentSha : world.Diverged;
        AddDebt(db, world.Owner, world.B, settled, state);
        await db.SaveChangesAsync();
        var request = NewRequest(repo, world.Card.Id);
        request.RequestedWorktreeBaseMode = mode;
        if (mode == RequestedWorktreeBaseMode.Task) request.RequestedWorktreeBaseTaskId = world.Owner.Id;

        var selected = await ResolveAsync(db, request);

        selected.Decision.ShouldBe(CardWorktreeBaseDecision.Unknown);
        selected.Reason.ShouldBe("source_sync_unverified");
        selected.SourceTaskId.ShouldBe(world.Owner.Id);
        selected.SourceSha.ShouldBe(settled);
        selected.CandidateWarnings.ShouldContain(w => w.Contains("-StartRef " + settled, StringComparison.Ordinal)
            && w.Contains(world.B, StringComparison.Ordinal) && w.Contains(state.ToString(), StringComparison.Ordinal));
    }

    [Test]
    public async Task C1480_pending_land_row_holds_on_settled_source()
    {
        using var repo = new ScratchGitRepo("c1480-pending");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var world = await SeedAsync(db, repo);
        AddDebt(db, world.Owner, world.B, world.S, AgentTaskSyncDebtState.Pending);
        var requestedAt = DateTime.UtcNow;
        world.Owner.LandRequestedAt = requestedAt;
        var land = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = world.Owner.Id, RequestedAt = requestedAt,
            LastEvaluatedAt = requestedAt, LastProgressAt = requestedAt,
            State = LandRequestState.Queued, IsPending = true, ExpectedSourceSha = world.S,
        };
        db.AgentTaskLandRequests.Add(land);
        world.Owner.CurrentLandRequestId = land.Id;
        await db.SaveChangesAsync();

        var selected = await ResolveAsync(db, NewRequest(repo, world.Card.Id));

        selected.Decision.ShouldBe(CardWorktreeBaseDecision.WaitForLand);
        selected.Reason.ShouldBe("pending_land");
        selected.SourceTaskId.ShouldBe(world.Owner.Id);
        selected.SourceSha.ShouldBe(world.S);
    }

    [Test]
    public async Task C1480_start_ref_bypasses_debt_guard()
    {
        using var repo = new ScratchGitRepo("c1480-start-ref");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var world = await SeedAsync(db, repo);
        AddDebt(db, world.Owner, world.B, AbsentSha, AgentTaskSyncDebtState.Pending);
        await db.SaveChangesAsync();
        var request = NewRequest(repo, world.Card.Id);
        request.WorktreeBaseRequestedRef = world.B;
        var trace = new TraceGit();

        var selected = await ResolveAsync(db, request, trace);

        selected.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        selected.Reason.ShouldBeNull();
        selected.GitCommands.ShouldBe(0);
        trace.Commands.ShouldBeEmpty();
    }

    [Test]
    [Arguments("ready")]
    [Arguments("same-tip")]
    [Arguments("prior-attempt")]
    public async Task C1480_paid_or_foreign_debts_do_not_change_selection(string shape)
    {
        using var repo = new ScratchGitRepo("c1480-paid");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var world = await SeedAsync(db, repo);
        await db.SaveChangesAsync();
        var baseline = await ResolveAsync(db, NewRequest(repo, world.Card.Id));
        baseline.Decision.ShouldBe(CardWorktreeBaseDecision.Target);

        if (shape == "prior-attempt") world.Owner.Attempt = 2;
        AddDebt(db, world.Owner, world.B, shape == "same-tip" ? world.B : world.S,
            shape == "ready" ? AgentTaskSyncDebtState.Ready : AgentTaskSyncDebtState.Pending,
            attempt: 1);
        await db.SaveChangesAsync();
        var actual = await ResolveAsync(db, NewRequest(repo, world.Card.Id));

        actual.Decision.ShouldBe(baseline.Decision);
        actual.SourceTaskId.ShouldBe(baseline.SourceTaskId);
        actual.SourceSha.ShouldBe(baseline.SourceSha);
        actual.Reason.ShouldBe(baseline.Reason);
        actual.CandidateWarnings.SequenceEqual(baseline.CandidateWarnings).ShouldBeTrue();
    }

    private sealed record World(Card Card, AgentTask Owner, string B, string S, string Diverged);

    /// <summary>
    /// The card's red shape: owner O on branch b at B, master fast-forwarded to B (O has no
    /// Landed event and no pending land), the settled source S on top of B held only under a
    /// progress ref, and a diverged commit on master's parent that does not descend B.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db, ScratchGitRepo repo)
    {
        await repo.CommitFileAsync("seed.txt", "M\n");
        var m = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        var card = await CardAsync(db);
        var o = await SourceAsync(db, repo, card.Id, "owner", "master");
        o.Attempt = 1;
        await repo.GitAsync("merge", "--ff-only", o.WorktreeBranch!);
        var b = (await repo.GitReadAsync("rev-parse", o.WorktreeBranch!)).Trim();
        var s = await DetachedCommitAsync(repo, b, "settled", $"refs/antiphon/progress/{o.Id:N}/1/observed");
        var diverged = await DetachedCommitAsync(repo, m, "diverged", $"refs/antiphon/progress/{o.Id:N}/1/diverged");
        (await repo.GitReadAsync("rev-parse", o.WorktreeBranch!)).Trim().ShouldBe(b);
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(b);
        return new World(card, o, b, s, diverged);
    }

    internal static void AddDebt(AppDbContext db, AgentTask owner, string baseline, string source,
        AgentTaskSyncDebtState state, int? attempt = null)
    {
        var now = DateTime.UtcNow;
        db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(), TaskId = owner.Id, Attempt = attempt ?? owner.Attempt,
            SettlementEventId = Guid.NewGuid(), RunnerId = "server2",
            FullRef = "refs/heads/" + owner.WorktreeBranch, BaselineSha = baseline, SourceSha = source,
            State = state, ConfirmedSha = state == AgentTaskSyncDebtState.Ready ? source : null,
            CreatedAt = now, UpdatedAt = now,
        });
    }

    internal static async Task<string> DetachedCommitAsync(ScratchGitRepo repo, string parent, string label, string reference)
    {
        var temp = "c1480-" + label + "-" + Guid.NewGuid().ToString("N")[..8];
        await repo.GitAsync("checkout", "-b", temp, parent);
        await repo.CommitFileAsync(label + ".txt", label + "\n");
        var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "master");
        await repo.GitAsync("branch", "-D", temp);
        await repo.GitAsync("update-ref", reference, sha);
        return sha;
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<Card> CardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c1480-" + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c1480.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c1480",
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
            Identifier = "CARD-1480", Title = "Debt-aware same-card base",
            Description = "Fixture", CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<AgentTask> SourceAsync(AppDbContext db, ScratchGitRepo repo,
        Guid cardId, string label, string startRef)
    {
        var id = Guid.NewGuid();
        var branch = "feat/card-task-" + id.ToString("N")[..8];
        await repo.GitAsync("checkout", "-b", branch, startRef);
        await repo.CommitFileAsync(label + ".txt", label + "\n");
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

    private sealed class TraceGit : LandingGit
    {
        public List<string> Commands { get; } = [];
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args,
            CancellationToken ct)
        {
            Commands.Add(repository + " " + string.Join(' ', args));
            return await base.RunAsync(repository, args, ct);
        }
    }
}
