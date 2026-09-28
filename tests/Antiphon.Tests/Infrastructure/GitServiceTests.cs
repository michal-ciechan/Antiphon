using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.TestHelpers.FakeGit;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class GitServiceTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly TestGitBackendHost _host = new(() => Environment.GetEnvironmentVariable("ANTIPHON_TEST_REAL_GIT"));
    private static string Master => GitService.GetWorkflowMasterBranch(Id);
    private static string Stage => GitService.GetStageBranch(Id, "architecture");
    private static string Tag(int n) => GitService.GetStageTag(Id, "architecture", n);
    private static string Artifact(string name) => $"{GitService.GetArtifactDirectory(Id)}/{name}";

    [Test] public void GetWorkflowMasterBranch_ReturnsCorrectFormat()
    {
        Master.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/master");
        Pure(nameof(GetWorkflowMasterBranch_ReturnsCorrectFormat), Master);
    }
    [Test] public void GetStageBranch_ReturnsCorrectFormat()
    {
        Stage.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/stage-architecture");
        Pure(nameof(GetStageBranch_ReturnsCorrectFormat), Stage);
    }
    [Test] public void GetStageTag_ReturnsCorrectFormat()
    {
        Tag(1).ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/architecture-v1");
        Pure(nameof(GetStageTag_ReturnsCorrectFormat), Tag(1));
    }
    [Test] public void GetStageTag_VersionIncrements()
    {
        GitService.GetStageTag(Id, "design", 1).ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/design-v1");
        GitService.GetStageTag(Id, "design", 2).ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/design-v2");
        Pure(nameof(GetStageTag_VersionIncrements),
            GitService.GetStageTag(Id, "design", 1) + "|" + GitService.GetStageTag(Id, "design", 2));
    }
    [Test] public void GetArtifactDirectory_ReturnsCorrectFormat()
    {
        GitService.GetArtifactDirectory(Id).ShouldBe("_antiphon/artifacts/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Pure(nameof(GetArtifactDirectory_ReturnsCorrectFormat), GitService.GetArtifactDirectory(Id));
    }
    [Test] public void GetStageBranch_WithSpacesInName_IncludesSpaces()
    {
        GitService.GetStageBranch(Id, "stage-one").ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/stage-stage-one");
        Pure(nameof(GetStageBranch_WithSpacesInName_IncludesSpaces), GitService.GetStageBranch(Id, "stage-one"));
    }

    [Test]
    public async Task InitializeWorkflowBranchesAsync_CreatesWorkflowMasterBranch()
    {
        await using var git = await RepoAsync();
        var seed = await Rev(git, "HEAD");
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        (await git.RequiredAsync("branch", "--show-current")).Trim().ShouldBe(Master);
        (await git.RequiredAsync("rev-list", "--parents", "-n", "1", Master)).Trim().Split(' ')[1].ShouldBe(seed);
        git.Files.File.Exists(Path.Combine(git.RepoPath, Artifact(".gitkeep"))).ShouldBeTrue();
        (await git.RequiredAsync("show", $"{Master}:{Artifact(".gitkeep")}")).ShouldBe("");
        await git.SaveReceiptAsync(nameof(InitializeWorkflowBranchesAsync_CreatesWorkflowMasterBranch), [Master], [], [Artifact(".gitkeep")]);
    }

    [Test]
    public async Task CreateStageBranchAsync_CreatesBranchFromWorkflowMaster()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        var master = await Rev(git, Master);
        await git.RequiredAsync("checkout", "-b", "unrelated", Master);
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "unrelated.txt"), "other");
        await git.RequiredAsync("add", "unrelated.txt");
        await git.RequiredAsync("commit", "-m", "unrelated");
        (await Rev(git, "HEAD")).ShouldNotBe(master);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        (await Rev(git, Stage)).ShouldBe(master);
        (await git.RequiredAsync("branch", "--show-current")).Trim().ShouldBe(Stage);
        (await git.RunAsync("show", $"{Stage}:unrelated.txt")).ExitCode.ShouldBe(128);
        await git.SaveReceiptAsync(nameof(CreateStageBranchAsync_CreatesBranchFromWorkflowMaster), [Master, Stage], [], [Artifact(".gitkeep"), "unrelated.txt"]);
    }

    [Test]
    public async Task CommitArtifactAsync_CreatesFileAndCommitsWithAntiphonTrailer()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        var before = await Rev(git, "HEAD");
        await git.Files.File.WriteAllTextAsync(Path.Combine(git.RepoPath, "unrelated.txt"), "unstaged sentinel");
        const string content = "# Architecture\nDesign doc content";
        await git.Service.CommitArtifactAsync(Id, "architecture", content, "architecture.md", git.RepoPath, CancellationToken.None);
        git.Files.File.ReadAllText(Path.Combine(git.RepoPath, Artifact("architecture.md"))).ShouldBe(content);
        (await git.RequiredAsync("show", $"{Stage}:{Artifact("architecture.md")}")).ShouldBe(content);
        (await git.RunAsync("show", $"{Stage}:unrelated.txt")).ExitCode.ShouldBe(128);
        git.Files.File.ReadAllText(Path.Combine(git.RepoPath, "unrelated.txt")).ShouldBe("unstaged sentinel");
        (await Rev(git, "HEAD")).ShouldNotBe(before);
        (await git.RequiredAsync("log", "-1", "--format=%B")).TrimEnd().ShouldEndWith("antiphon: true");
        await git.SaveReceiptAsync(nameof(CommitArtifactAsync_CreatesFileAndCommitsWithAntiphonTrailer), [Master, Stage], [], [Artifact("architecture.md"), "unrelated.txt"]);
    }

    [Test]
    public async Task TagStageAsync_CreatesVersionedTag()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        await git.Service.CommitArtifactAsync(Id, "architecture", "content", "doc.md", git.RepoPath, CancellationToken.None);
        var stage = await Rev(git, Stage);
        await git.RequiredAsync("checkout", Master);
        (await Rev(git, "HEAD")).ShouldNotBe(stage);
        (await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None)).ShouldBe(Tag(1));
        (await git.RequiredAsync("rev-list", "-n", "1", Tag(1))).Trim().ShouldBe(stage);
        (await git.RequiredAsync("tag", "--list")).ShouldContain(Tag(1));
        await git.SaveReceiptAsync(nameof(TagStageAsync_CreatesVersionedTag), [Master, Stage], [Tag(1)], [Artifact("doc.md")]);
    }

    [Test]
    public async Task MergeStageBranchAsync_MergesIntoWorkflowMaster()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        await git.Service.CommitArtifactAsync(Id, "architecture", "content", "doc.md", git.RepoPath, CancellationToken.None);
        await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None);
        var stage = await Rev(git, Stage);
        var master = await Rev(git, Master);
        await git.Service.MergeStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        (await git.RequiredAsync("branch", "--show-current")).Trim().ShouldBe(Master);
        var merged = await Rev(git, "HEAD");
        merged.ShouldNotBe(stage);
        merged.ShouldNotBe(master);
        (await git.RequiredAsync("rev-list", "--parents", "-n", "1", "HEAD")).Trim().Split(' ')
            .ShouldBe([merged, master, stage]);
        (await git.RequiredAsync("show", $"{Master}:{Artifact("doc.md")}")).ShouldBe("content");
        (await Rev(git, Tag(1))).ShouldBe(stage);
        await git.SaveReceiptAsync(nameof(MergeStageBranchAsync_MergesIntoWorkflowMaster), [Master, Stage], [Tag(1)], [Artifact("doc.md")]);
    }

    [Test]
    public async Task GetDiffBetweenTagsAsync_ReturnsDiff()
    {
        await using var git = await RepoAsync();
        await git.Service.InitializeWorkflowBranchesAsync(Id, git.RepoPath, CancellationToken.None);
        await git.Service.CreateStageBranchAsync(Id, "architecture", git.RepoPath, CancellationToken.None);
        await git.Service.CommitArtifactAsync(Id, "architecture", "version 1 content", "doc.md", git.RepoPath, CancellationToken.None);
        await git.Service.TagStageAsync(Id, "architecture", 1, git.RepoPath, CancellationToken.None);
        await git.Service.CommitArtifactAsync(Id, "architecture", "version 2 content", "doc.md", git.RepoPath, CancellationToken.None);
        await git.Service.TagStageAsync(Id, "architecture", 2, git.RepoPath, CancellationToken.None);
        (await Rev(git, Tag(1))).ShouldNotBe(await Rev(git, Tag(2)));
        var diff = await git.Service.GetDiffBetweenTagsAsync(Tag(1), Tag(2), git.RepoPath, CancellationToken.None);
        diff.ShouldContain("-version 1 content");
        diff.ShouldContain("+version 2 content");
        diff.ShouldNotContain("README.md");
        await git.SaveReceiptAsync(nameof(GetDiffBetweenTagsAsync_ReturnsDiff), [Master, Stage], [Tag(1), Tag(2)], [Artifact("doc.md")]);
    }

    private async Task<TestGitBackend> RepoAsync()
    {
        var git = _host.Create();
        try { await git.InitializeAsync(); return git; }
        catch { await git.DisposeAsync(); throw; }
    }

    private static async Task<string> Rev(TestGitBackend git, string name) =>
        (await git.RequiredAsync("rev-parse", name)).Trim();

    private void Pure(string method, string value) =>
        TestGitBackend.SavePureReceipt(method, value, _host.UseRealGit);
}
