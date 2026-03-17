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
}
