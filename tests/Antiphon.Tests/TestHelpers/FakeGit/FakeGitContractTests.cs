using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers.FakeGit;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class FakeGitContractTests
{
    private readonly TestGitBackendHost _host = new(() => Environment.GetEnvironmentVariable("ANTIPHON_TEST_REAL_GIT"));
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static string Master => GitService.GetWorkflowMasterBranch(Id);
    private static string Stage => GitService.GetStageBranch(Id, "architecture");
    private static string Tag => GitService.GetStageTag(Id, "architecture", 1);

    private async Task<TestGitBackend> RepoAsync()
    {
        var git = _host.Create();
        await git.InitializeAsync();
        return git;
    }

    private static async Task<string> Rev(TestGitBackend git, string name) =>
        (await git.RequiredAsync("rev-parse", name)).Trim();

    [Test]
    public async Task CommitUsesIndexSnapshotAndPreservesUnstagedBytes()
    {
        await using var git = await RepoAsync();
        var staged = Path.Combine(git.RepoPath, "staged.txt");
        await git.Files.File.WriteAllTextAsync(staged, "A");
        await git.RequiredAsync("add", "staged.txt");
        await git.Files.File.WriteAllTextAsync(staged, "B");
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "untracked.txt"), "U");
        await git.RequiredAsync("commit", "-m", "snapshot");
        (await git.RequiredAsync("show", "HEAD:staged.txt")).ShouldBe("A");
        (await git.RequiredAsync("show", ":staged.txt")).ShouldBe("A");
        git.Files.File.ReadAllText(staged).ShouldBe("B");
        (await git.RunAsync("show", "HEAD:untracked.txt")).ExitCode.ShouldBe(128);
    }

    [Test]
    public async Task BranchCreationUsesNamedBase()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        var baseOid = await Rev(git, Master);
        await git.RequiredAsync("checkout", "-b", "unrelated", Master);
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "other.txt"), "other");
        await git.RequiredAsync("add", "other.txt");
        await git.RequiredAsync("commit", "-m", "other");
        (await Rev(git, "HEAD")).ShouldNotBe(baseOid);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        (await Rev(git, Stage)).ShouldBe(baseOid);
        (await git.RunAsync("show", "HEAD:other.txt")).ExitCode.ShouldBe(128);
    }

    [Test]
    public async Task StageTagAtSameHeadIsIdempotent()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        var first = await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None);
        var target = await Rev(git, Tag);
        var second = await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None);
        second.ShouldBe(first);
        (await Rev(git, Tag)).ShouldBe(target);
        (await git.RequiredAsync("tag", "--list")).Split('\n').Count(x => x == Tag).ShouldBe(1);
    }

    [Test]
    public async Task StageTagAtDifferentHeadRefuses()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None);
        var target = await Rev(git, Tag);
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "later.txt"), "later");
        await git.RequiredAsync("add", "later.txt");
        await git.RequiredAsync("commit", "-m", "later");
        await Should.ThrowAsync<InvalidOperationException>(() =>
            git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None));
        (await Rev(git, Tag)).ShouldBe(target);
    }

    [Test]
    public async Task MissingRevisionKeepsDiagnosticAndAllowsTagCreation()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        var missing = await git.RunAsync("rev-list", "-n", "1", Tag);
        missing.ExitCode.ShouldBe(128);
        (missing.Stderr.Contains("unknown revision") || missing.Stderr.Contains("ambiguous argument")).ShouldBeTrue();
        await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None);
        (await Rev(git, Tag)).ShouldBe(await Rev(git, "HEAD"));
        var later = GitService.GetStageTag(Id, "architecture", 2);
        git.InjectServiceResult(["rev-list", "-n", "1", later],
            new Antiphon.Server.Application.Interfaces.GitCommandResult(1, "", "permission denied"));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            git.Service.TagStageAsync(Id, "architecture", 2, git.RepoPath, CancellationToken.None));
        (await git.RunAsync("rev-list", "-n", "1", later)).ExitCode.ShouldBe(128);
    }

    [Test]
    public async Task NoFastForwardMergeHasOrderedParentsAndCombinedTree()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "stage.txt"), "stage");
        await git.RequiredAsync("add", "stage.txt");
        await git.RequiredAsync("commit", "-m", "stage");
        var stage = await Rev(git, Stage);
        await git.RequiredAsync("checkout", Master);
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "master.txt"), "master");
        await git.RequiredAsync("add", "master.txt");
        await git.RequiredAsync("commit", "-m", "master");
        var master = await Rev(git, Master);
        await git.Service.MergeStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        var merge = await Rev(git, "HEAD");
        (await git.RequiredAsync("rev-list", "--parents", "-n", "1", "HEAD")).Trim().Split(' ')
            .ShouldBe([merge, master, stage]);
        (await git.RequiredAsync("show", "HEAD:master.txt")).ShouldBe("master");
        (await git.RequiredAsync("show", "HEAD:stage.txt")).ShouldBe("stage");
    }

    [Test]
    public async Task TagDiffPreservesDirectionAndContent()
    {
        await using var git = await RepoAsync();
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "doc.txt"), "old\n");
        await git.RequiredAsync("add", "doc.txt");
        await git.RequiredAsync("commit", "-m", "old");
        await git.RequiredAsync("tag", "old");
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "doc.txt"), "new\n");
        await git.RequiredAsync("add", "doc.txt");
        await git.RequiredAsync("commit", "-m", "new");
        await git.RequiredAsync("tag", "new");
        var forward = await git.RequiredAsync("diff", "old..new");
        var reverse = await git.RequiredAsync("diff", "new..old");
        forward.ShouldContain("-old"); forward.ShouldContain("+new");
        reverse.ShouldContain("-new"); reverse.ShouldContain("+old");
        forward.ShouldNotContain("README.md");
    }

    [Test]
    public async Task RejectedMutationPreservesRefs()
    {
        await using var git = await RepoAsync();
        await git.RequiredAsync("branch", "existing");
        await git.RequiredAsync("tag", "existing-tag");
        var branch = await Rev(git, "existing");
        var tag = await Rev(git, "existing-tag");
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "later.txt"), "later");
        await git.RequiredAsync("add", "later.txt");
        await git.RequiredAsync("commit", "-m", "later");
        (await git.RunAsync("branch", "existing")).ExitCode.ShouldBe(128);
        (await git.RunAsync("tag", "existing-tag")).ExitCode.ShouldBe(128);
        (await Rev(git, "existing")).ShouldBe(branch);
        (await Rev(git, "existing-tag")).ShouldBe(tag);
    }
}
