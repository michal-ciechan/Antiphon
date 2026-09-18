using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0527 S3 (D-3): a gated commit that SUCCEEDED but whose receipt could not be read throws
/// <see cref="Antiphon.Server.Application.Exceptions.CommitInspectionPendingException"/>. The work
/// is on the branch, so reporting "Committing the delegate's work failed" and stranding the branch
/// is simply false: the merge proceeds and the outcome says the receipt is pending.
/// </summary>
public partial class DelegationWorktreeTests
{
    // A-13: with a merge target the merge lands and the detail names the pending operation.
    [Test]
    public async Task C527_a_pending_gated_receipt_still_merges_and_names_the_operation()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var spy = new RecordingGitWorkspaceService();
        var (service, _) = CreateService(repo, workspaceGit: spy);
        var task = NewTask(repo.Path, mergeTarget: "feat/parent");
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        // The receipt search fails only AFTER the gate has committed, which is the exact window
        // the exception exists for.
        spy.OverrideRun = args => spy.Verbs.Contains("commit") && args.Contains("--all")
            ? (-1, "", "timeout") : null;

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Merged);
        outcome.Detail.ShouldNotBeNull();
        outcome.Detail.ShouldContain("gated commit receipt pending (operation ");
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldBe("the work\n");
        Directory.Exists(task.WorktreePath).ShouldBeFalse("a merged worktree is removed");
    }

    // A-15: with no merge target the branch is kept for a human, receipt note appended.
    [Test]
    public async Task C527_a_pending_gated_receipt_with_no_merge_target_is_left_for_a_human_with_the_receipt()
    {
        using var repo = new ScratchGitRepo();
        await repo.CommitFileAsync("README.md", "base\n");

        var spy = new RecordingGitWorkspaceService();
        var (service, _) = CreateService(repo, workspaceGit: spy);
        var task = NewTask(repo.Path, mergeTarget: null);
        await service.CreateForTaskAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        spy.OverrideRun = args => spy.Verbs.Contains("commit") && args.Contains("--all")
            ? (-1, "", "timeout") : null;

        var outcome = await service.TryMergeBackAsync(task, CancellationToken.None);

        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.LeftForHuman);
        outcome.Detail.ShouldNotBeNull();
        outcome.Detail.ShouldEndWith("gated commit receipt pending (operation " + PendingOperationId(outcome.Detail) + ")");
        Directory.Exists(task.WorktreePath).ShouldBeTrue("a branch left for a human keeps its worktree");
    }

    private static string PendingOperationId(string detail)
    {
        var marker = "gated commit receipt pending (operation ";
        var start = detail.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return detail[start..detail.IndexOf(')', start)];
    }
}
