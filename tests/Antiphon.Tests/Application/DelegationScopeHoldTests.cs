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
/// The advisory scope lease as the dispatcher tick actually applies it (CARD-0063 S1).
///
/// <para>Three behaviours the old rule got wrong and one it never had: ReadOnly is outside the
/// lease in both directions, the key is the REPO rather than the declared working directory, a
/// hold writes exactly one <see cref="AgentTaskEventType.Held"/> event no matter how many ticks it
/// spans, and a hold that resolves dispatches rather than leaving a second event behind.</para>
///
/// <para>Every assertion is scoped to the rows this class creates — the fixture database is shared
/// and other suites' queued/running tasks are writing throughout.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class DelegationScopeHoldTests
{
    [Test]
    public async Task a_held_task_carries_exactly_one_held_event_across_three_ticks_then_dispatches()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        var holder = await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "delivery,docs/notes.md", title: "the holder");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var waiting = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "delivery", pinnedAgentId: agentId);

        for (var i = 0; i < 3; i++)
            await dispatcher.TickAsync(CancellationToken.None);

        await using (var held = CreateContext())
        {
            (await held.AgentTasks.SingleAsync(t => t.Id == waiting.Id)).Status
                .ShouldBe(AgentTaskStatus.Queued, "the holder is still running");
            var events = await held.AgentTaskEvents
                .Where(e => e.AgentTaskId == waiting.Id && e.Type == AgentTaskEventType.Held)
                .ToListAsync();
            events.Count.ShouldBe(1, "one event per hold, not one per tick");
            events[0].Detail.ShouldContain(DelegationReportFormatter.Short(holder.Id));
            events[0].Detail.ShouldContain("the holder");
        }

        await SettleAsync(holder.Id);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == waiting.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched, "the hold resolved when the holder settled");
        (await verify.AgentTaskEvents
                .CountAsync(e => e.AgentTaskId == waiting.Id && e.Type == AgentTaskEventType.Held))
            .ShouldBe(1, "a hold that resolves is not a second event — the dispatch is");
    }

    [Test]
    public async Task a_read_only_task_is_never_held_and_never_holds()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        var (readerAgent, _) = await SeedWarmAgentAsync(workspace.Path);
        var writerHolder = await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "delivery", title: "a running writer");
        var reader = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.ReadOnly, "delivery", pinnedAgentId: readerAgent);

        await dispatcher.TickAsync(CancellationToken.None);

        await using (var first = CreateContext())
        {
            (await first.AgentTasks.SingleAsync(t => t.Id == reader.Id)).Status
                .ShouldBe(AgentTaskStatus.Dispatched, "ReadOnly writes nothing, so nothing holds it");
            (await first.AgentTaskEvents
                    .AnyAsync(e => e.AgentTaskId == reader.Id && e.Type == AgentTaskEventType.Held))
                .ShouldBeFalse();
        }

        // The other direction: a running ReadOnly task must not hold a queued writer.
        await SettleAsync(writerHolder.Id);
        await SetStatusAsync(reader.Id, AgentTaskStatus.Working);
        var (writerAgent, _) = await SeedWarmAgentAsync(workspace.Path);
        var writer = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "delivery", pinnedAgentId: writerAgent);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == writer.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched, "a running ReadOnly task holds nobody");
    }

    [Test]
    public async Task two_tasks_in_one_repo_hold_each_other_from_different_working_directories()
    {
        using var workspace = new TempWorkspace();
        var subdirectory = Directory.CreateDirectory(Path.Combine(workspace.Path, "client")).FullName;
        var dispatcher = CreateDispatcher();
        await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "client/src/App.tsx",
            title: "root-directory writer", repoPath: workspace.Path);
        var (agentId, _) = await SeedWarmAgentAsync(subdirectory);
        var waiting = await SeedQueuedTaskAsync(
            subdirectory, WorkspaceMode.Shared, "client/**",
            pinnedAgentId: agentId, repoPath: workspace.Path);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == waiting.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued, "same RepoPath, intersecting scopes — the key is the repo");
        (await verify.AgentTaskEvents
                .CountAsync(e => e.AgentTaskId == waiting.Id && e.Type == AgentTaskEventType.Held))
            .ShouldBe(1);
    }

    [Test]
    public async Task labels_that_merely_share_a_prefix_do_not_hold_each_other()
    {
        // The only hold in 623 live tasks, and it was wrong: CARD-0054 slice 3 waited 579 seconds
        // behind slice 2 because "card-reopen-client".StartsWith("card-reopen-cli").
        //
        // SerialiseSharedWriters off: D3 would hold this pair anyway, for the honest reason that
        // they share a checkout, and that would hide whether the LABEL rule still matches by prefix.
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher(serialiseSharedWriters: false);
        await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "card-reopen-client", title: "slice 2");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var slice3 = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "card-reopen-cli", pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == slice3.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched, "two different labels are two different areas");
    }

    // ---- CARD-0063 S3: the pair decides, not the area ---------------------------------------

    [Test]
    public async Task a_worktree_task_dispatches_over_a_shared_holder_with_one_warning()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        var holder = await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, "delivery", title: "the shared writer",
            branch: "feat/holder");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var isolated = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Worktree, "delivery", pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == isolated.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched, "a worktree collides at merge, not at dispatch");
        (await verify.AgentTaskEvents
                .AnyAsync(e => e.AgentTaskId == isolated.Id && e.Type == AgentTaskEventType.Held))
            .ShouldBeFalse();
        var warnings = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == isolated.Id && e.Type == AgentTaskEventType.Warning)
            .ToListAsync();
        warnings.Count.ShouldBe(1);
        warnings[0].Detail.ShouldContain(DelegationReportFormatter.Short(holder.Id));
        warnings[0].Detail.ShouldContain("the shared writer");
        warnings[0].Detail.ShouldContain("feat/holder");
        warnings[0].Detail.ShouldContain("Worktree");
    }

    [Test]
    public async Task two_worktree_tasks_in_one_area_both_run()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Worktree, "delivery", title: "worktree one",
            branch: "feat/one");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var second = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Worktree, "delivery", pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == second.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
        (await verify.AgentTaskEvents
                .CountAsync(e => e.AgentTaskId == second.Id && e.Type == AgentTaskEventType.Warning))
            .ShouldBe(1);
    }

    [Test]
    public async Task an_undeclared_shared_writer_waits_behind_another_shared_writer()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        var holder = await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, scope: null, title: "the first writer");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var second = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Shared, scope: null, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == second.Id)).Status
            .ShouldBe(AgentTaskStatus.Queued, "two shared writers share one working TREE");
        var held = await verify.AgentTaskEvents
            .SingleAsync(e => e.AgentTaskId == second.Id && e.Type == AgentTaskEventType.Held);
        held.Detail.ShouldContain(DelegationReportFormatter.Short(holder.Id));
        held.Detail.ShouldContain("no intersecting scope");
    }

    [Test]
    public async Task undeclared_shared_writers_run_concurrently_with_the_setting_off()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher(serialiseSharedWriters: false);
        await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, scope: null, title: "the first writer");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var second = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.Shared, scope: null, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == second.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched, "the operator turned the rule off deliberately");
    }

    [Test]
    public async Task a_read_only_task_is_outside_the_undeclared_shared_writer_rule_too()
    {
        using var workspace = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        await SeedRunningTaskAsync(
            workspace.Path, WorkspaceMode.Shared, scope: null, title: "a running writer");
        var (agentId, _) = await SeedWarmAgentAsync(workspace.Path);
        var reader = await SeedQueuedTaskAsync(
            workspace.Path, WorkspaceMode.ReadOnly, scope: null, pinnedAgentId: agentId);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == reader.Id)).Status
            .ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task a_role_recommendation_does_not_prevent_a_second_dispatch()
    {
        // CARD-0304: RecommendedInFlight is advisory. Two Shared writers in different checkouts
        // still dispatch when MaxConcurrentTasks allows it, even though each role recommends 1.
        using var first = new TempWorkspace();
        using var second = new TempWorkspace();
        var dispatcher = CreateDispatcher();
        var (firstAgent, _) = await SeedWarmAgentAsync(first.Path);
        var (secondAgent, _) = await SeedWarmAgentAsync(second.Path);
        var a = await SeedQueuedTaskAsync(
            first.Path, WorkspaceMode.Shared, scope: null, pinnedAgentId: firstAgent, title: "first code");
        var b = await SeedQueuedTaskAsync(
            second.Path, WorkspaceMode.Shared, scope: null, pinnedAgentId: secondAgent, title: "second code");

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == a.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await verify.AgentTasks.SingleAsync(t => t.Id == b.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static AgentTaskDispatcher CreateDispatcher(bool serialiseSharedWriters = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            // The fixture database is shared; leftover Dispatched/Working rows from other suites
            // must never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
            SerialiseSharedWriters = serialiseSharedWriters,
        }));
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-scope-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(
        string directory, WorkspaceMode workspace, string? scope,
        Guid? pinnedAgentId = null, string? repoPath = null, string title = "queued work")
    {
        var task = NewTask(directory, workspace, scope, repoPath, title);
        task.Status = AgentTaskStatus.Queued;
        task.AgentId = pinnedAgentId;
        task.Ephemeral = pinnedAgentId is null;
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedRunningTaskAsync(
        string directory, WorkspaceMode workspace, string? scope,
        string title, string? repoPath = null, string? branch = null)
    {
        var task = NewTask(directory, workspace, scope, repoPath, title);
        task.Status = AgentTaskStatus.Working;
        task.WorktreeBranch = branch ?? task.WorktreeBranch;
        task.DispatchedAt = DateTime.UtcNow.AddMinutes(-1);
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTask NewTask(
        string directory, WorkspaceMode workspace, string? scope, string? repoPath, string title)
    {
        var id = Guid.NewGuid();
        var shortId = id.ToString("N")[..8];
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = workspace,
            WorkingDirectory = directory,
            RepoPath = repoPath,
            Scope = scope,
            // Pre-filled for a Worktree task so the dispatch does not try a real `git worktree add`
            // in a temp directory that is not a repo — the lease is what is under test, not git.
            WorktreePath = workspace == WorkspaceMode.Worktree
                ? Path.Combine(directory, "worktrees", $"task-{shortId}")
                : null,
            WorktreeBranch = workspace == WorkspaceMode.Worktree ? $"feat/task-{shortId}" : null,
            Ephemeral = true,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static async Task SettleAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task SetStatusAsync(Guid taskId, AgentTaskStatus status)
    {
        await using var db = CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).Status = status;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A settled delegate the tick can take over, so a dispatch in these tests reuses a live
    /// session instead of trying to spawn a real Claude.
    /// </summary>
    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-3),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-scope-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
