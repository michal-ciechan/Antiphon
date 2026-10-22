using Antiphon.Server.Application.Exceptions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DelegationWorktreeTests
{
    // CARD-1448: an interrupted `git worktree add` (locked initializing, partial checkout,
    // CreationComplete=false) is never adopted as the task's checkout.

    [Test]
    public async Task C1448_incomplete_creation_is_not_adopted_when_recovery_refuses()
    {
        using var repo = new ScratchGitRepo("c1448-refused");
        await repo.CommitFileAsync("README.md", "base\n");
        var (service, _) = CreateService(repo);
        var first = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(first, CancellationToken.None);
        InterruptedWorktreeCreation.Simulate(repo.WorktreeRoot, first.WorktreePath!, "operator hold");

        var retry = NewTask(repo.Path, mergeTarget: null);
        retry.Id = first.Id;
        await Should.ThrowAsync<ConflictException>(() => service.CreateForTaskAsync(retry, CancellationToken.None));

        retry.WorktreePath.ShouldBeNull("incomplete-creation-not-adopted");
        Directory.Exists(first.WorktreePath!).ShouldBeTrue("refusal-removes-nothing");
    }

    [Test]
    public async Task C1448_interrupted_creation_is_recreated_and_its_base_recorded()
    {
        using var repo = new ScratchGitRepo("c1448-recreated");
        await repo.CommitFileAsync("README.md", "base\n");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var (service, _) = CreateService(repo);
        var first = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(first, CancellationToken.None);
        InterruptedWorktreeCreation.Simulate(repo.WorktreeRoot, first.WorktreePath!);

        var retry = NewTask(repo.Path, mergeTarget: null);
        retry.Id = first.Id;
        await service.CreateForTaskAsync(retry, CancellationToken.None);

        retry.WorktreePath.ShouldBe(first.WorktreePath);
        File.Exists(Path.Combine(retry.WorktreePath!, "README.md")).ShouldBeTrue("full-checkout");
        retry.WorktreeBaseSha.ShouldBe(head, "fresh-creation-records-its-base");
    }

    [Test]
    public async Task C1448_complete_creation_is_still_adopted()
    {
        using var repo = new ScratchGitRepo("c1448-complete");
        await repo.CommitFileAsync("README.md", "base\n");
        var (service, _) = CreateService(repo);
        var first = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(first, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(first.WorktreePath!, "work.md"), "work\n");
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "add", "work.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "commit", "-m", "work")).Ok.ShouldBeTrue();

        var retry = NewTask(repo.Path, mergeTarget: null);
        retry.Id = first.Id;
        await service.CreateForTaskAsync(retry, CancellationToken.None);

        retry.WorktreePath.ShouldBe(first.WorktreePath);
        File.Exists(Path.Combine(retry.WorktreePath!, "work.md")).ShouldBeTrue("adopted-work-kept");
        retry.WorktreeBaseSha.ShouldBeNull("adopt-keeps-base-unknown");
    }
}
