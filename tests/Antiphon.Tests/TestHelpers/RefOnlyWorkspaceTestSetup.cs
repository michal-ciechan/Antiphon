using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Seeds recorded mode directly. No S5b-1 production dispatcher selects this representation.</summary>
internal static class RefOnlyWorkspaceTestSetup
{
    public static async Task RecordAsync(AppDbContext db, AgentTask task, ITaskProgressGit git, string root)
    {
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!;
        var binding = new TaskRefWorkspaceBinding { Id = Guid.NewGuid(), TaskId = task.Id,
            RepositoryPath = task.RepoPath!, CommonDirectory = await git.CommonDirectoryAsync(task.RepoPath!, default),
            IdentityPath = Path.Combine(root, ".ref-only", task.Id.ToString("N")), Branch = task.WorktreeBranch!,
            FullRef = baseline.Primary.FullRef, SelectedBaseSha = task.WorktreeBaseSha!,
            SelectedBaseRef = "refs/heads/master", SelectedBaseSource = WorktreeBaseSource.Explicit,
            PreparationState = TaskRefWorkspacePreparationState.Published, Revision = 1 };
        db.TaskRefWorkspaceBindings.Add(binding);
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly; task.TaskRefWorkspaceBindingId = binding.Id;
        task.WorktreePath = binding.IdentityPath; task.WorktreeBaseRef = binding.SelectedBaseRef;
        task.WorktreeBaseSource = binding.SelectedBaseSource;
        task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline with { SchemaVersion = 2,
            Primary = baseline.Primary with { RegisteredCheckout = null, DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly,
                TaskRefWorkspaceBindingId = binding.Id } });
        await db.SaveChangesAsync();
    }

    internal sealed record Binding(Guid Id, string Identity, string Common, string SourceSha);

    public static async Task<Binding> BindAsync(LandingSafetyHarness host, bool removeAuthoring = true)
    {
        var fixture = host.Fixture;
        var sha = (await fixture.RequiredAsync(fixture.Repository, "rev-parse", fixture.SourceRef)).Trim();
        await fixture.RequiredAsync(fixture.Repository, "push", "origin", fixture.SourceRef);
        if (removeAuthoring)
            await fixture.RequiredAsync(fixture.Repository, "worktree", "remove", fixture.Source);
        var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
        var identity = Path.Combine(fixture.Root, "trees", ".ref-only", fixture.TaskId.ToString("N"));
        await using var db = host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == fixture.TaskId);
        var binding = new TaskRefWorkspaceBinding
        {
            Id = Guid.NewGuid(), TaskId = task.Id, RepositoryPath = fixture.Repository, CommonDirectory = common,
            IdentityPath = identity, Branch = fixture.SourceRef[11..], FullRef = fixture.SourceRef,
            SelectedBaseSha = fixture.SeedSha, SelectedBaseRef = "refs/heads/master", SelectedBaseSource = WorktreeBaseSource.Explicit,
            PreparationState = TaskRefWorkspacePreparationState.Published, Revision = 1,
        };
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly;
        task.TaskRefWorkspaceBindingId = binding.Id;
        task.WorktreePath = identity;
        task.WorktreeBaseSha = binding.SelectedBaseSha;
        task.WorktreeBaseRef = binding.SelectedBaseRef;
        task.WorktreeBaseSource = binding.SelectedBaseSource;
        task.RunnerId = "fixture-runner";
        task.RemoteWorktreePath = "/fixture/runner/task";
        db.TaskRefWorkspaceBindings.Add(binding);
        await db.SaveChangesAsync();
        return new(binding.Id, identity, common, sha);
    }
}
