using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using SyncWorld = Antiphon.Tests.Application.RunnerSettlementSyncTests.SyncWorld;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real persisted binding, bare origin and independent author; no desktop source checkout during SUT.</summary>
internal sealed class RefOnlySyncFixture : IAsyncDisposable
{
    public SyncWorld Git { get; private init; } = null!;
    public IsolatedTestSchema Schema { get; private init; } = null!;
    public AppDbContext Db { get; private init; } = null!;
    public TaskRefWorkspaceBinding Binding { get; private init; } = null!;
    public AgentTask Task => Git.Task;
    public TaskWorkspaceContextResolver Contexts => new(Db);
    public string Identity => Task.WorktreePath!;

    public static async Task<RefOnlySyncFixture> CreateAsync(bool modern = false)
    {
        var git = await SyncWorld.CreateAsync(cardBranch: modern);
        IsolatedTestSchema? schema = null;
        AppDbContext? db = null;
        try
        {
            await git.EnsureRunnerAsync();
            await git.RunAsync(git.Desktop, "worktree", "remove", git.Worktree);
            schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var snapshot = TaskProgressJson.TryReadBaseline(git.Task.ProgressBaselineJson)!;
            var binding = new TaskRefWorkspaceBinding
            {
                Id = Guid.NewGuid(), TaskId = git.TaskId, RepositoryPath = git.Desktop,
                CommonDirectory = snapshot.Primary.CanonicalCommonDirectory,
                IdentityPath = Path.Combine(git.Root, ".ref-only", git.TaskId.ToString("N")),
                Branch = git.Branch, FullRef = git.FullRef, SelectedBaseSha = git.Baseline,
                SelectedBaseRef = "refs/heads/master", SelectedBaseSource = WorktreeBaseSource.Explicit,
                PreparationState = TaskRefWorkspacePreparationState.Published, Revision = 1,
            };
            git.Task.RootTaskId = git.TaskId;
            git.Task.WorktreePath = binding.IdentityPath;
            git.Task.WorktreeBaseRef = binding.SelectedBaseRef;
            git.Task.WorktreeBaseSource = binding.SelectedBaseSource;
            git.Task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly;
            git.Task.TaskRefWorkspaceBindingId = binding.Id;
            git.Task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot with
            {
                SchemaVersion = 2,
                Primary = snapshot.Primary with
                {
                    RegisteredCheckout = null, DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly,
                    TaskRefWorkspaceBindingId = binding.Id,
                },
            });
            db.AgentTasks.Add(git.Task);
            db.TaskRefWorkspaceBindings.Add(binding);
            await db.SaveChangesAsync();
            git.Git.Clear();
            return new() { Git = git, Schema = schema, Db = db, Binding = binding };
        }
        catch
        {
            if (db is not null) await db.DisposeAsync();
            if (schema is not null) await schema.DisposeAsync();
            await git.DisposeAsync();
            throw;
        }
    }

    public RemoteWorkspaceService Service(TimeProvider? clock = null, Action? busy = null) =>
        Git.Service(clock: clock, leaseBusy: busy, workspaceContexts: Contexts);

    public async Task<string> TipAsync() => await Git.RevParseAsync(Git.FullRef);

    public async Task<string> InventoryAsync() => await Git.RunAsync(Git.Desktop, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads/");

    public void AssertNoDesktopCommands()
    {
        Git.Git.Commands.ShouldNotContain(c => c.Contains("--ff-only", StringComparison.Ordinal)
            || c.StartsWith("checkout ", StringComparison.Ordinal) || c.StartsWith("reset ", StringComparison.Ordinal)
            || c.StartsWith("clean ", StringComparison.Ordinal) || c.StartsWith("worktree add ", StringComparison.Ordinal)
            || c.StartsWith("worktree remove ", StringComparison.Ordinal) || c.StartsWith("worktree prune", StringComparison.Ordinal)
            || c.Contains("--force", StringComparison.Ordinal) || c.Contains("refs/heads/*", StringComparison.Ordinal),
            "no-desktop-worktree");
        Git.Git.WorkingDirectories.ShouldNotContain(Identity, "identity-never-git-cwd");
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Schema.DisposeAsync();
        await Git.DisposeAsync();
    }
}
