using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0215: a card-bound Worktree task must not branch from master while a same-card
/// kept sibling is still off to the side. Hold while that sibling's land is in flight;
/// warn (and still dispatch) when the branch is simply not landed; stay silent when the
/// branch is already gone.
/// </summary>
[Category("Integration")]
public class AgentTaskDispatchBaseGuardTests
{
    [Test]
    [Timeout(30_000)]
    public async Task a_sibling_land_in_flight_holds_until_the_base_contains_it(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-hold");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldContain(sibling.WorktreeBranch!);
        heldEvents[0].Detail.ShouldContain(DelegationReportFormatter.Short(sibling.Id));
        heldEvents[0].Detail.ShouldContain("is landing");

        var heldSibling = await db.AgentTasks.SingleAsync(t => t.Id == sibling.Id, ct);
        heldSibling.LandRequestedAt = null;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("merge", "--ff-only", sibling.WorktreeBranch!);

        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreePath.ShouldNotBeNull();
        Directory.Exists(dispatched.WorktreePath).ShouldBeTrue();
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(1);
        (await ScratchGitRepo.GitInAsync(
            dispatched.WorktreePath!, "merge-base", "--is-ancestor", sibling.WorktreeBranch!, "HEAD"))
            .Ok.ShouldBeTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_kept_sibling_with_no_land_dispatches_with_a_warning_and_whenidle_note(
        CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-warn");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreePath.ShouldNotBeNull();

        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
        var tip = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "--short", sibling.WorktreeBranch!))
            .StdOut.Trim();
        warning.Detail.ShouldContain(tip);
        warning.Detail.ShouldContain("Land " + DelegationReportFormatter.Short(sibling.Id));

        var notes = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync(ct);
        notes.ShouldHaveSingleItem();
        notes[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        notes[0].Body.ShouldContain(sibling.WorktreeBranch!);
        notes[0].Body.ShouldContain(tip);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_sibling_whose_branch_was_deleted_is_silent(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-gone");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        await repo.GitAsync("branch", "-D", sibling.WorktreeBranch!);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId: null);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_stranded_request_row_with_a_null_column_only_warns(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-stranded");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = sibling.Id,
            Type = AgentTaskEventType.LandRequested,
            Detail = "Land requested",
            At = DateTime.UtcNow.AddMinutes(-1),
        });
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
    }

    private static async Task<AgentTask> SeedKeptSiblingAsync(
        AppDbContext db, ScratchGitRepo repo, Guid cardId, string commitMessage)
    {
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        await repo.GitAsync("checkout", "-b", branch);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "plan.md"), "the plan\n");
        await repo.GitAsync("add", "plan.md");
        await repo.GitAsync("commit", "-m", commitMessage);
        await repo.GitAsync("checkout", "master");

        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0215 plan",
            Goal = "Write the plan.",
            Role = AgentTaskRole.Plan,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            CardId = cardId,
            WorktreeBranch = branch,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task<AgentTask> SeedQueuedWorktreeTaskAsync(
        AppDbContext db, string repoPath, Guid cardId, Guid? parentSessionId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0215 execute",
            Goal = "Build the plan.",
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repoPath,
            RepoPath = repoPath,
            CardId = cardId,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task SeedParentSessionAsync(AppDbContext db, Guid parentSessionId)
    {
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId,
            DefinitionName = "card0215-parent",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
        });
        await Task.CompletedTask;
    }

    private static async Task<Card> SeedCardAsync(AppDbContext db, string identifier)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"card0215-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/card0215.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"CARD-0215 {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} ancestry",
            Description = "CARD-0215.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static ServiceProvider CreateProvider(string connectionString, string worktreeBase)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = worktreeBase,
            WorktreeAddTimeoutSeconds = 180,
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
