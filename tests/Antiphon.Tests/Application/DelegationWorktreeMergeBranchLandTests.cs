using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1637 (1)/(2): a plain -Land rebases and flattens merge commits. When that rebase conflicts
/// on a branch that carries merges, the land refuses with the Merge-role path instead of leaving
/// a NeedsResolution request and spawning a rebase resolver.
/// </summary>
public partial class DelegationWorktreeTests
{
    [Test]
    public async Task C1637_merge_branch_containing_target_refuses_with_merge_role_path_and_spawns_no_resolver()
    {
        using var repo = new ScratchGitRepo("antiphon-land-merge-branch");
        using var remote = new TemporaryDirectory("antiphon-land-merge-branch-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");
        var targetSha = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        // The branch joins the target once and resolves the conflict in its merge commit.
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "merge", "--no-edit", "master")).Ok.ShouldBeFalse();
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "merged version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "--no-edit");
        await RequiredGitAsync(task.WorktreePath!, "merge-base", "--is-ancestor", targetSha, "HEAD");
        var (request, sourceSha, sourcePath, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("merge_branch_needs_merge_role");
        operation.Phase.ShouldBe(LandPhase.Refused);
        operation.PushStartedAt.ShouldBeNull();
        var terminal = await AssertTerminalAsync(db, task.Id, request.RequestId, operation.Id, AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldContain("merge_branch_needs_merge_role");
        terminal.Detail.ShouldContain("shared.md");
        terminal.Detail.ShouldContain($"already contains master {targetSha}");
        terminal.Detail.ShouldContain($"git push origin {sourceSha}:refs/heads/master");
        terminal.Detail.ShouldContain("Merge-role");
        terminal.Detail.ShouldContain("new Review");
        (await db.AgentTasks.AsNoTracking().CountAsync(t => t.ParentTaskId == task.Id))
            .ShouldBe(0, "a branch with merge commits must never get a rebase resolver");
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Conflicted)).ShouldBe(0);
        await AssertUnchangedSourceAsync(sourcePath, sourceFullRef, sourceSha, "shared.md", "merged version\n");
        (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim().ShouldBe(targetSha);
        (await RebaseStateDirectoryAsync(operation.LandWorktreePath!)).ShouldBeNull();
    }

    [Test]
    public async Task C1637_merge_branch_behind_target_refuses_with_merge_role_path_and_spawns_no_resolver()
    {
        using var repo = new ScratchGitRepo("antiphon-land-merge-behind");
        using var remote = new TemporaryDirectory("antiphon-land-merge-behind-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var (land, service) = CreateLand(db, repo);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        task.Status = AgentTaskStatus.Succeeded;
        task.CompletedAt = DateTime.UtcNow;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        // The branch merges a side line once, then the target moves past it with a conflicting edit.
        await RequiredGitAsync(task.WorktreePath!, "checkout", "-b", "side");
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "side.md"), "side\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "side.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "side edit");
        await RequiredGitAsync(task.WorktreePath!, "checkout", task.WorktreeBranch!);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await RequiredGitAsync(task.WorktreePath!, "add", "shared.md");
        await RequiredGitAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await RequiredGitAsync(task.WorktreePath!, "merge", "--no-ff", "--no-edit", "side");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");
        var targetSha = (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim();
        var (request, sourceSha, sourcePath, _, sourceFullRef) = await PublishAndRequestAsync(land, task);

        (await land.RunAsync(task.Id, null, CancellationToken.None)).ShouldBe(LandRunResult.Complete);

        var operation = await AssertBoundOperationAsync(db, task.Id, request.RequestId, sourceSha, sourceFullRef);
        operation.LastReason.ShouldBe("merge_branch_needs_merge_role");
        operation.Phase.ShouldBe(LandPhase.Refused);
        var terminal = await AssertTerminalAsync(db, task.Id, request.RequestId, operation.Id, AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldContain("shared.md");
        terminal.Detail.ShouldContain($"-StartRef {targetSha}");
        terminal.Detail.ShouldNotContain("already contains");
        terminal.Detail.ShouldContain("new Review");
        (await db.AgentTasks.AsNoTracking().CountAsync(t => t.ParentTaskId == task.Id)).ShouldBe(0);
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Conflicted)).ShouldBe(0);
        await AssertUnchangedSourceAsync(sourcePath, sourceFullRef, sourceSha, "shared.md", "task version\n");
        (await RequiredGitAsync(remote.Path, "rev-parse", "master")).Trim().ShouldBe(targetSha);
    }
}
