using Antiphon.Server.Infrastructure.Git;
using Shouldly;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// Unit tests for GitService branch/tag name generation and integration tests for git operations.
/// </summary>
public class GitServiceTests
{
    private static readonly Guid TestWorkflowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // --- Branch/Tag name generation tests ---

    [Test]
    public void GetWorkflowMasterBranch_ReturnsCorrectFormat()
    {
        var branch = GitService.GetWorkflowMasterBranch(TestWorkflowId);

        branch.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/master");
    }

    [Test]
    public void GetStageBranch_ReturnsCorrectFormat()
    {
        var branch = GitService.GetStageBranch(TestWorkflowId, "architecture");

        branch.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/stage-architecture");
    }

    [Test]
    public void GetStageTag_ReturnsCorrectFormat()
    {
        var tag = GitService.GetStageTag(TestWorkflowId, "architecture", 1);

        tag.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/architecture-v1");
    }

    [Test]
    public void GetStageTag_VersionIncrements()
    {
        var tag1 = GitService.GetStageTag(TestWorkflowId, "design", 1);
        var tag2 = GitService.GetStageTag(TestWorkflowId, "design", 2);

        tag1.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/design-v1");
        tag2.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/design-v2");
    }

    [Test]
    public void GetArtifactDirectory_ReturnsCorrectFormat()
    {
        var dir = GitService.GetArtifactDirectory(TestWorkflowId);

        dir.ShouldBe("_antiphon/artifacts/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    }

    [Test]
    public void GetStageBranch_WithSpacesInName_IncludesSpaces()
    {
        // Stage names with spaces are used as-is in branch names
        var branch = GitService.GetStageBranch(TestWorkflowId, "stage-one");

        branch.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/stage-stage-one");
    }

    // --- Integration tests for actual git operations ---

    [Test]
    public async Task InitializeWorkflowBranchesAsync_CreatesWorkflowMasterBranch()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);

            // Verify the branch was created
            var branches = await RunGit(repoPath, "branch --list");
            branches.ShouldContain("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/master");

            // Verify artifact directory exists
            var artifactDir = Path.Combine(repoPath, "_antiphon", "artifacts", $"workflow-{TestWorkflowId}");
            Directory.Exists(artifactDir).ShouldBeTrue();
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    [Test]
    public async Task CreateStageBranchAsync_CreatesBranchFromWorkflowMaster()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);

            var branches = await RunGit(repoPath, "branch --list");
            branches.ShouldContain("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/stage-architecture");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    [Test]
    public async Task CommitArtifactAsync_CreatesFileAndCommitsWithAntiphonTrailer()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);

            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture", "# Architecture\nDesign doc content",
                "architecture.md", repoPath, CancellationToken.None);

            // Verify file exists
            var artifactPath = Path.Combine(repoPath, "_antiphon", "artifacts",
                $"workflow-{TestWorkflowId}", "architecture.md");
            File.Exists(artifactPath).ShouldBeTrue();
            (await File.ReadAllTextAsync(artifactPath)).ShouldContain("Design doc content");

            // Verify commit message has [antiphon] trailer
            var log = await RunGit(repoPath, "log -1 --format=%B");
            log.ShouldContain("antiphon: true");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    [Test]
    public async Task TagStageAsync_CreatesVersionedTag()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);
            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture", "content", "doc.md", repoPath, CancellationToken.None);

            var tagName = await service.TagStageAsync(TestWorkflowId, "architecture", 1, repoPath, CancellationToken.None);

            tagName.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/architecture-v1");

            var tags = await RunGit(repoPath, "tag --list");
            tags.ShouldContain(tagName);
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    [Test]
    public async Task MergeStageBranchAsync_MergesIntoWorkflowMaster()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);
            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture", "content", "doc.md", repoPath, CancellationToken.None);
            await service.TagStageAsync(TestWorkflowId, "architecture", 1, repoPath, CancellationToken.None);

            await service.MergeStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);

            // We should be on the workflow master branch after merge
            var currentBranch = (await RunGit(repoPath, "branch --show-current")).Trim();
            currentBranch.ShouldBe("antiphon/workflow-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/master");

            // The artifact file should exist on workflow master
            var artifactPath = Path.Combine(repoPath, "_antiphon", "artifacts",
                $"workflow-{TestWorkflowId}", "doc.md");
            File.Exists(artifactPath).ShouldBeTrue();
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    [Test]
    public async Task GetDiffBetweenTagsAsync_ReturnsDiff()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);

            // Create stage and commit v1
            await service.CreateStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);
            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture", "version 1 content", "doc.md", repoPath, CancellationToken.None);
            await service.TagStageAsync(TestWorkflowId, "architecture", 1, repoPath, CancellationToken.None);

            // Commit v2 on same branch
            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture", "version 2 content", "doc.md", repoPath, CancellationToken.None);
            await service.TagStageAsync(TestWorkflowId, "architecture", 2, repoPath, CancellationToken.None);

            var diff = await service.GetDiffBetweenTagsAsync(
                GitService.GetStageTag(TestWorkflowId, "architecture", 1),
                GitService.GetStageTag(TestWorkflowId, "architecture", 2),
                repoPath, CancellationToken.None);

            diff.ShouldContain("version 1 content");
            diff.ShouldContain("version 2 content");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    // --- Helper methods ---

    private static async Task<(GitService Service, string RepoPath)> CreateTestRepo()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), $"antiphon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoPath);

        // Initialize a git repo with an initial commit
        await RunGit(repoPath, "init");
        await RunGit(repoPath, "config user.email \"test@antiphon.dev\"");
        await RunGit(repoPath, "config user.name \"Antiphon Test\"");

        // Create an initial commit so branches can be created
        var readmePath = Path.Combine(repoPath, "README.md");
        await File.WriteAllTextAsync(readmePath, "# Test Repo");
        await RunGit(repoPath, "add .");
        await RunGit(repoPath, "commit -m \"Initial commit\"");

        var service = new GitService(NullLogger<GitService>.Instance);
        return (service, repoPath);
    }

    private static async Task<string> RunGit(string workingDirectory, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private static void CleanupTestRepo(string repoPath)
    {
        try
        {
            // On Windows, git files may be read-only
            foreach (var file in Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(repoPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
