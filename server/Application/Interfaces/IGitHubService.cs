namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Abstraction for GitHub operations: PR creation, commit pushing, and PR monitoring (FR59-FR64).
/// Feature-flagged per project via GitHubIntegrationEnabled.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Creates a pull request from a source branch to a target branch (FR59, FR60).
    /// Returns the PR number.
    /// </summary>
    Task<int> CreatePullRequestAsync(string owner, string repo, string sourceBranch, string targetBranch, string title, string body, CancellationToken ct);

    /// <summary>
    /// Pushes local commits on a branch to the remote origin (FR63).
    /// </summary>
    Task PushBranchAsync(string repoPath, string branchName, CancellationToken ct);

    /// <summary>
    /// Gets comments and review feedback on a pull request (FR61).
    /// </summary>
    Task<IReadOnlyList<PullRequestComment>> GetPullRequestCommentsAsync(string owner, string repo, int prNumber, CancellationToken ct);

    /// <summary>
    /// Gets the combined status/check-run result for a pull request (FR61).
    /// </summary>
    Task<PullRequestStatus> GetPullRequestStatusAsync(string owner, string repo, int prNumber, CancellationToken ct);

    /// <summary>
    /// Gets basic pull request details including state and mergeable status.
    /// </summary>
    Task<PullRequestDetail> GetPullRequestDetailAsync(string owner, string repo, int prNumber, CancellationToken ct);

    /// <summary>
    /// Lists all repositories accessible to the authenticated user (for repo picker in project config).
    /// </summary>
    Task<IReadOnlyList<GitHubRepoDto>> GetRepositoriesAsync(CancellationToken ct);

    /// <summary>
    /// Lists branches for the given repository (for branch picker in NewWorkflowDialog).
    /// </summary>
    Task<IReadOnlyList<GitHubBranchDto>> GetBranchesAsync(string owner, string repo, CancellationToken ct);

    /// <summary>
    /// Finds an open or merged pull request whose head branch matches the given branch name.
    /// Returns null if no PR is found or if GitHub integration is unavailable.
    /// </summary>
    Task<PullRequestInfo?> FindPullRequestForBranchAsync(string owner, string repo, string headBranch, CancellationToken ct);

    /// <summary>
    /// Checks API connectivity by calling /user. Returns the authenticated username on success.
    /// </summary>
    Task<(bool Success, string? Login, string? Error)> CheckConnectivityAsync(CancellationToken ct);
}

/// <summary>
/// A comment or review on a GitHub pull request.
/// </summary>
public sealed record PullRequestComment(
    long Id,
    string Author,
    string Body,
    DateTime CreatedAt,
    bool IsReviewComment);

/// <summary>
/// Combined CI/check status for a pull request.
/// </summary>
public sealed record PullRequestStatus(
    string State,
    IReadOnlyList<CheckRunInfo> CheckRuns);

/// <summary>
/// A single CI check run result.
/// </summary>
public sealed record CheckRunInfo(
    string Name,
    string Status,
    string? Conclusion);

/// <summary>
/// Basic pull request detail.
/// </summary>
public sealed record PullRequestDetail(
    int Number,
    string State,
    bool IsMerged,
    string HeadSha,
    string BaseBranch,
    string HeadBranch);

/// <summary>
/// A GitHub repository entry returned by the repo listing API.
/// </summary>
public sealed record GitHubRepoDto(
    string FullName,
    string CloneUrl,
    string HtmlUrl,
    bool IsPrivate);

/// <summary>
/// A GitHub branch entry.
/// </summary>
public sealed record GitHubBranchDto(
    string Name,
    string Sha,
    bool IsProtected);

/// <summary>
/// Summary info for a pull request found by head branch.
/// </summary>
public sealed record PullRequestInfo(
    int Number,
    string Title,
    string State,
    string BaseBranch,
    string HtmlUrl);
