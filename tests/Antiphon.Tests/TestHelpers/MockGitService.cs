using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// In-memory mock IGitService that records calls for test assertions without performing git operations.
/// </summary>
public class MockGitService : IGitService
{
    private readonly List<GitOperation> _operations = [];

    public IReadOnlyList<GitOperation> Operations => _operations;

    public Task InitializeWorkflowBranchesAsync(Guid workflowId, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("InitializeWorkflowBranches", workflowId, null, null, repoPath));
        return Task.CompletedTask;
    }

    public Task CreateStageBranchAsync(Guid workflowId, string stageName, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("CreateStageBranch", workflowId, stageName, null, repoPath));
        return Task.CompletedTask;
    }

    public Task CommitArtifactAsync(Guid workflowId, string stageName, string content, string artifactPath, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("CommitArtifact", workflowId, stageName, artifactPath, repoPath));
        return Task.CompletedTask;
    }

    public Task<string> TagStageAsync(Guid workflowId, string stageName, int version, string repoPath, CancellationToken ct)
    {
        var tagName = $"antiphon/workflow-{workflowId}/{stageName}-v{version}";
        _operations.Add(new GitOperation("TagStage", workflowId, stageName, tagName, repoPath));
        return Task.FromResult(tagName);
    }

    public Task MergeStageBranchAsync(Guid workflowId, string stageName, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("MergeStageBranch", workflowId, stageName, null, repoPath));
        return Task.CompletedTask;
    }

    public Task<string> GetDiffBetweenTagsAsync(string tag1, string tag2, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("GetDiffBetweenTags", Guid.Empty, null, $"{tag1}..{tag2}", repoPath));
        return Task.FromResult($"diff between {tag1} and {tag2}");
    }

    public Task<string> GetDiffBetweenTagsAsync(string tag1, string tag2, string repoPath, string pathFilter, CancellationToken ct)
    {
        _operations.Add(new GitOperation("GetDiffBetweenTagsFiltered", Guid.Empty, null, $"{tag1}..{tag2} -- {pathFilter}", repoPath));
        return Task.FromResult($"filtered diff between {tag1} and {tag2} at {pathFilter}");
    }

    public Task<string> EnsureCloneAsync(string gitUrl, string targetPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("EnsureClone", Guid.Empty, null, gitUrl, targetPath));
        return Task.FromResult(targetPath);
    }

    public Task<string> GetBranchDiffAsync(string baseBranch, string headBranch, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("GetBranchDiff", Guid.Empty, null, $"{baseBranch}...{headBranch}", repoPath));
        return Task.FromResult($"diff {baseBranch}...{headBranch}");
    }

    public Task<string> GetWorktreeDiffAsync(string baseRef, string worktreePath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("GetWorktreeDiff", Guid.Empty, null, baseRef, worktreePath));
        return Task.FromResult($"diff --git a/README.md b/README.md\n+changed from {baseRef}");
    }

    public Task<bool> CommitAllChangesAsync(string worktreePath, string message, CancellationToken ct)
    {
        _operations.Add(new GitOperation("CommitAllChanges", Guid.Empty, null, message, worktreePath));
        return Task.FromResult(true);
    }

    public Task<string?> FindAgentBranchAsync(string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("FindAgentBranch", Guid.Empty, null, null, repoPath));
        return Task.FromResult<string?>(null);
    }

    public Task<string> GetFileContentAsync(string branch, string filePath, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("GetFileContent", Guid.Empty, null, $"{branch}:{filePath}", repoPath));
        return Task.FromResult(string.Empty);
    }

    public Task DeleteBranchAsync(string branchName, string repoPath, CancellationToken ct)
    {
        _operations.Add(new GitOperation("DeleteBranch", Guid.Empty, branchName, null, repoPath));
        return Task.CompletedTask;
    }

    public void Clear() => _operations.Clear();

    public record GitOperation(string Method, Guid WorkflowId, string? StageName, string? Detail, string RepoPath);
}
