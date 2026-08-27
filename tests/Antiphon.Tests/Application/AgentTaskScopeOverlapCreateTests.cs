using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// What a caller is told at CREATE time about what its <c>-Scope</c> just cost (CARD-0063 S3).
///
/// <para>This is the ergonomic centre of the card. The dispatcher's own verdict arrives five
/// seconds and one queue away, on a board nobody is watching; the moment the caller can still
/// change its mind — pick <c>-Worktree</c>, wait, pick a different slice — is the moment it
/// pressed enter.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskScopeOverlapCreateTests
{
    [Test]
    public async Task a_shared_task_is_told_it_will_wait_behind_the_running_one()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();
        var running = await SeedRunningAsync(workspace.Path, WorkspaceMode.Shared, "delivery", "the holder");

        var created = await CreateService(db).CreateAsync(
            Request("write the queue tests", scope: "delivery"),
            new AgentTaskService.Caller(null, null, workspace.Path),
            CancellationToken.None);

        var overlap = created.ScopeOverlaps.ShouldHaveSingleItem();
        overlap.TaskId.ShouldBe(running.Id);
        overlap.Policy.ShouldBe("serialise");
        overlap.Areas.ShouldBe("delivery");
        overlap.Title.ShouldBe("the holder");
    }

    [Test]
    public async Task a_worktree_task_is_told_a_rebase_is_coming_and_against_which_branch()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace(git: true);
        var repoPath = await RepoPathOfAsync(workspace.Path);
        var running = await SeedRunningAsync(
            workspace.Path, WorkspaceMode.Worktree, "delivery", "the other worktree",
            branch: "feat/other", repoPath: repoPath);

        var created = await CreateService(db).CreateAsync(
            Request("write the queue tests", scope: "delivery", workspace: WorkspaceMode.Worktree),
            new AgentTaskService.Caller(null, null, workspace.Path),
            CancellationToken.None);

        var overlap = created.ScopeOverlaps.ShouldHaveSingleItem();
        overlap.TaskId.ShouldBe(running.Id);
        overlap.Policy.ShouldBe("warn");
        overlap.Branch.ShouldBe("feat/other");
    }

    [Test]
    public async Task a_disjoint_scope_is_told_nothing()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace(git: true);
        await SeedRunningAsync(
            workspace.Path, WorkspaceMode.Worktree, "delivery", "elsewhere",
            repoPath: await RepoPathOfAsync(workspace.Path));

        var created = await CreateService(db).CreateAsync(
            Request("rewrite the board", scope: "client/src/features/board/**",
                workspace: WorkspaceMode.Worktree),
            new AgentTaskService.Caller(null, null, workspace.Path),
            CancellationToken.None);

        created.ScopeOverlaps.ShouldBeNull();
    }

    [Test]
    public async Task a_read_only_task_is_told_nothing_because_it_can_collide_with_nothing()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();
        await SeedRunningAsync(workspace.Path, WorkspaceMode.Shared, "delivery", "the holder");

        var created = await CreateService(db).CreateAsync(
            Request("read the queue", scope: "delivery", workspace: WorkspaceMode.ReadOnly),
            new AgentTaskService.Caller(null, null, workspace.Path),
            CancellationToken.None);

        created.ScopeOverlaps.ShouldBeNull();
    }

    [Test]
    public async Task an_undeclared_shared_writer_is_told_it_will_wait()
    {
        await using var db = CreateContext();
        using var workspace = new TempWorkspace();
        var running = await SeedRunningAsync(workspace.Path, WorkspaceMode.Shared, null, "the holder");

        var created = await CreateService(db).CreateAsync(
            Request("do a thing"),
            new AgentTaskService.Caller(null, null, workspace.Path),
            CancellationToken.None);

        var overlap = created.ScopeOverlaps.ShouldHaveSingleItem();
        overlap.TaskId.ShouldBe(running.Id);
        overlap.Policy.ShouldBe("serialise");
        overlap.Areas.ShouldBeNull("nothing was declared — the checkout is the collision");
    }

    [Test]
    public async Task a_running_task_in_another_repo_is_told_nothing()
    {
        await using var db = CreateContext();
        using var mine = new TempWorkspace();
        using var theirs = new TempWorkspace();
        await SeedRunningAsync(theirs.Path, WorkspaceMode.Shared, "delivery", "another repo");

        var created = await CreateService(db).CreateAsync(
            Request("write the queue tests", scope: "delivery"),
            new AgentTaskService.Caller(null, null, mine.Path),
            CancellationToken.None);

        created.ScopeOverlaps.ShouldBeNull("cross-repo tasks must run concurrently");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static CreateAgentTaskRequest Request(
        string goal, string? scope = null, WorkspaceMode? workspace = null) =>
        new(Goal: goal, Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Docs,
            Scope: scope, Workspace: workspace);

    /// <summary>
    /// The repo toplevel exactly as the service will derive it — the temp root and git's own
    /// answer for it differ on Windows (8.3 shortening, drive case), and the lease key must match.
    /// </summary>
    private static async Task<string?> RepoPathOfAsync(string directory) =>
        await new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance)
            .GetRepoToplevelAsync(directory, CancellationToken.None);

    private static async Task<AgentTask> SeedRunningAsync(
        string directory, WorkspaceMode workspace, string? scope, string title,
        string? branch = null, string? repoPath = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
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
            WorktreeBranch = branch,
            Status = AgentTaskStatus.Working,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            DispatchedAt = DateTime.UtcNow.AddMinutes(-1),
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
        }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance,
        quotaGate: null,
        areas: new AreaMapLoader(
            Options.Create(new DelegationSettings()), NullLogger<AreaMapLoader>.Instance));

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace(bool git = false)
        {
            Path = Directory.CreateTempSubdirectory("antiphon-overlap-test").FullName;
            // A Worktree create is refused outright when the directory is not a repo (there is
            // nothing to branch), so the worktree arms need a real one.
            if (!git)
                return;

            using var init = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "init --quiet",
                WorkingDirectory = Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            init?.WaitForExit(30_000);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
