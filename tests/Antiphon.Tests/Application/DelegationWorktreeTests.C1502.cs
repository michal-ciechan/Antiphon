using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DelegationWorktreeTests
{
    [Test]
    public async Task C1502_complete_checkout_that_died_before_its_final_save_is_adopted()
    {
        // CARD-1502 (A): `worktree add` returned, the final save never ran. The next dispatch heals the
        // flag and adopts the checkout with its work instead of refusing it forever.
        using var repo = new ScratchGitRepo("c1502-heal");
        await repo.CommitFileAsync("README.md", "base\n");
        var (service, _) = CreateService(repo);
        var first = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(first, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(first.WorktreePath!, "work.md"), "work\n");
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "add", "work.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(first.WorktreePath!, "commit", "-m", "work")).Ok.ShouldBeTrue();
        InterruptedWorktreeCreation.DiedBeforeFinalSave(repo.WorktreeRoot, first.WorktreePath!);

        var retry = NewTask(repo.Path, mergeTarget: null);
        retry.Id = first.Id;
        await service.CreateForTaskAsync(retry, CancellationToken.None);

        retry.WorktreePath.ShouldBe(first.WorktreePath);
        File.Exists(Path.Combine(retry.WorktreePath!, "work.md")).ShouldBeTrue("healed-work-kept");
    }
}
