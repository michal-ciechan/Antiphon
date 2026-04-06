namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Abstraction for git operations supporting the two-tier branching, tagging,
/// and artifact storage strategy (FR28-FR34).
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Creates the workflow master branch: antiphon/workflow-{id}/master (FR28).
    /// </summary>
    Task InitializeWorkflowBranchesAsync(Guid workflowId, string repoPath, CancellationToken ct);

    /// <summary>
    /// Creates an ephemeral stage branch: antiphon/workflow-{id}/stage-{name} (FR29).
    /// </summary>
    Task CreateStageBranchAsync(Guid workflowId, string stageName, string repoPath, CancellationToken ct);

    /// <summary>
    /// Commits an artifact to the stage branch with [antiphon] trailer (FR30, FR33).
    /// Artifact is stored at _antiphon/artifacts/workflow-{id}/{artifactPath}.
    /// </summary>
    Task CommitArtifactAsync(Guid workflowId, string stageName, string content, string artifactPath, string repoPath, CancellationToken ct);

    /// <summary>
    /// Tags the current stage branch head: antiphon/workflow-{id}/{stage}-v{version} (FR31).
    /// Returns the created tag name.
    /// </summary>
    Task<string> TagStageAsync(Guid workflowId, string stageName, int version, string repoPath, CancellationToken ct);

    /// <summary>
    /// Merges stage branch into workflow master on gate approval (FR32).
    /// </summary>
    Task MergeStageBranchAsync(Guid workflowId, string stageName, string repoPath, CancellationToken ct);

    /// <summary>
    /// Returns the diff between two tags for cascade update context (FR34).
    /// </summary>
    Task<string> GetDiffBetweenTagsAsync(string tag1, string tag2, string repoPath, CancellationToken ct);

    /// <summary>
    /// Returns the diff between two tags, filtered to only show changes under the specified path (FR34).
    /// Used for cascade update context where only _antiphon/artifacts/ changes matter.
    /// </summary>
    Task<string> GetDiffBetweenTagsAsync(string tag1, string tag2, string repoPath, string pathFilter, CancellationToken ct);

    /// <summary>
    /// Ensures a git repository exists at the given path. Clones from gitUrl if the directory
    /// does not exist; otherwise fetches all remotes to get latest branches.
    /// Returns the absolute path to the repository.
    /// </summary>
    Task<string> EnsureCloneAsync(string gitUrl, string targetPath, CancellationToken ct);

    /// <summary>
    /// Returns the three-dot diff between baseBranch and headBranch in the given repo.
    /// Uses "git diff {baseBranch}...{headBranch}" which shows changes since the branches diverged.
    /// </summary>
    Task<string> GetBranchDiffAsync(string baseBranch, string headBranch, string repoPath, CancellationToken ct);

    /// <summary>
    /// Finds the remote branch containing the most recent commit authored by the Antiphon agent.
    /// Returns the branch name (e.g. "origin/feature/antiphon-documentation") or null if not found.
    /// Used as a fallback when workflow.GitBranchName doesn't exist in the remote.
    /// </summary>
    Task<string?> FindAgentBranchAsync(string repoPath, CancellationToken ct);

    /// <summary>
    /// Returns the content of a file at the specified branch using "git show {branch}:{filePath}".
    /// Throws InvalidOperationException if the file or branch doesn't exist.
    /// </summary>
    Task<string> GetFileContentAsync(string branch, string filePath, string repoPath, CancellationToken ct);
}
