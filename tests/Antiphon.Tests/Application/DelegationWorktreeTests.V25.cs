using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DelegationWorktreeTests
{
    [Test]
    public async Task T0442_V25_crash_before_coordinates_adopts_without_invented_base()
    {
        using var repo = new ScratchGitRepo("c442-adopt-unknown-base");
        await repo.CommitFileAsync("README.md", "M\n");
        var (service, _) = CreateV25Service(repo,
            new GitSettings { DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot });
        var first = NewV25Task(repo.Path);
        await service.CreateForTaskAsync(first, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(first.WorktreePath!, "task.txt"), "C\n");
        await ScratchGitRepo.GitInAsync(first.WorktreePath!, "add", "task.txt");
        await ScratchGitRepo.GitInAsync(first.WorktreePath!, "commit", "-m", "task C");
        var taskHead = (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();

        var retry = NewV25Task(repo.Path);
        retry.Id = first.Id;
        await service.CreateForTaskAsync(retry, CancellationToken.None);

        retry.WorktreePath.ShouldBe(first.WorktreePath);
        retry.WorktreeBranch.ShouldBe(first.WorktreeBranch);
        (await ScratchGitRepo.GitInAsync(retry.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe(taskHead);
        retry.WorktreeBaseRef.ShouldBeNull();
        retry.WorktreeBaseSha.ShouldBeNull();
        retry.WorktreeBaseTaskId.ShouldBeNull();
    }

    [Test]
    [Arguments("persisted_coordinates")]
    [Arguments("registered_missing_directory")]
    public async Task T0442_V25_recorded_base_survives_adoption_and_registration_heal(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-adopt-recorded");
        await repo.CommitFileAsync("README.md", "M\n");
        await repo.GitAsync("checkout", "-b", "source-A");
        await repo.CommitFileAsync("source.txt", "A\n");
        var sourceSha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "master");
        var (service, _) = CreateV25Service(repo,
            new GitSettings { DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot });
        var first = NewV25Task(repo.Path);
        first.MergeTargetRef = "source-A";
        await service.CreateForTaskAsync(first, CancellationToken.None);
        first.WorktreeBaseSha.ShouldBe(sourceSha);
        await File.WriteAllTextAsync(Path.Combine(first.WorktreePath!, "task.txt"), "C\n");
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "add", "task.txt")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "commit", "-m", "task C")).Ok.ShouldBeTrue();
        var taskHead = (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
        await repo.GitAsync("checkout", "-b", "sibling-X", "master");
        await repo.CommitFileAsync("sibling.txt", "X\n");
        await repo.GitAsync("checkout", "master");
        if (scenario == "registered_missing_directory")
            Directory.Delete(first.WorktreePath!, recursive: true);

        var retry = NewV25Task(repo.Path);
        retry.Id = first.Id;
        retry.MergeTargetRef = first.MergeTargetRef;
        retry.WorktreeBaseRef = first.WorktreeBaseRef;
        retry.WorktreeBaseSha = first.WorktreeBaseSha;
        retry.WorktreeBaseSource = first.WorktreeBaseSource;
        retry.WorktreeBaseTaskId = first.WorktreeBaseTaskId;
        retry.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        retry.RequestedWorktreeBaseTaskId = Guid.NewGuid(); // stale selection cannot replace owned C.
        await service.CreateForTaskAsync(retry, CancellationToken.None);

        retry.WorktreePath.ShouldBe(first.WorktreePath);
        retry.WorktreeBranch.ShouldBe(first.WorktreeBranch);
        (await ScratchGitRepo.GitInAsync(retry.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe(taskHead);
        retry.WorktreeBaseRef.ShouldBe("source-A");
        retry.WorktreeBaseSha.ShouldBe(sourceSha);
        retry.WorktreeBaseSource.ShouldBe(first.WorktreeBaseSource);
        retry.WorktreeBaseTaskId.ShouldBe(first.WorktreeBaseTaskId);
    }
    private static (DelegationWorktreeService Service, Antiphon.Server.Infrastructure.Git.WorktreeManager Manager)
        CreateV25Service(ScratchGitRepo repo, GitSettings settings)
    {
        var graph = DelegationTestServices.CreateGitGraph(settings);
        return (graph.Worktrees, graph.Manager);
    }

    private static AgentTask NewV25Task(string repoPath) => new()
    {
        Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "c442 adopt",
        Goal = "test", Workspace = WorkspaceMode.Worktree,
        WorkingDirectory = repoPath, RepoPath = repoPath, CreatedAt = DateTime.UtcNow,
    };
}
