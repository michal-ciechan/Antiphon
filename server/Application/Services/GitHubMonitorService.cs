using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.GitHub;

/// <summary>
/// Background service that polls GitHub PRs for comments, review feedback,
/// and build status (FR61, FR62). Feeds PR feedback to the AI agent via
/// WorkflowEngine re-execution. Polling interval from GitSettings.
/// </summary>
public class GitHubMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GitHubMonitorService> _logger;
    private readonly GithubSettings _githubSettings;
    private readonly GitSettings _gitSettings;

    /// <summary>
    /// Tracks last-seen comment IDs per PR to avoid re-processing.
    /// Key: "owner/repo#prNumber", Value: set of seen comment IDs.
    /// </summary>
    private readonly Dictionary<string, HashSet<long>> _seenComments = new();

    public GitHubMonitorService(
        IServiceScopeFactory scopeFactory,
        IOptions<GithubSettings> githubSettings,
        IOptions<GitSettings> gitSettings,
        ILogger<GitHubMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _githubSettings = githubSettings.Value;
        _gitSettings = gitSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_githubSettings.Enabled)
        {
            _logger.LogInformation("GitHub integration is disabled globally. GitHubMonitorService will not run");
            return;
        }

        _logger.LogInformation("GitHubMonitorService started with {Interval}s poll interval", _gitSettings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllActivePullRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during GitHub PR polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_gitSettings.PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("GitHubMonitorService stopped");
    }

    private async Task PollAllActivePullRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gitHubService = scope.ServiceProvider.GetRequiredService<IGitHubService>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        // Find workflows that are active (Running or GateWaiting) and belong to projects with GitHub enabled
        var activeWorkflows = await db.Workflows
            .Include(w => w.Project)
            .Where(w => w.Project.GitHubIntegrationEnabled
                && (w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.GateWaiting))
            .ToListAsync(ct);

        foreach (var workflow in activeWorkflows)
        {
            var ownerRepo = ParseOwnerRepo(workflow.Project.GitRepositoryUrl);
            if (ownerRepo is null)
                continue;

            var (owner, repo) = ownerRepo.Value;

            // Check for PRs associated with this workflow's branches
            // Convention: PRs are created from stage branches or workflow master
            await MonitorWorkflowPullRequestsAsync(
                gitHubService, eventBus, db, workflow.Id, owner, repo, ct);
        }
    }

    private async Task MonitorWorkflowPullRequestsAsync(
        IGitHubService gitHubService,
        IEventBus eventBus,
        AppDbContext db,
        Guid workflowId,
        string owner,
        string repo,
        CancellationToken ct)
    {
        // Look for stage executions with associated PR numbers (stored as git tags with PR info)
        // For now, we monitor known PRs by checking recent stage executions
        var recentExecutions = await db.StageExecutions
            .Where(se => se.WorkflowId == workflowId && se.Status == StageStatus.Completed)
            .OrderByDescending(se => se.CompletedAt)
            .Take(5)
            .ToListAsync(ct);

        foreach (var execution in recentExecutions)
        {
            if (string.IsNullOrEmpty(execution.GitTagName))
                continue;

            // Extract PR number from the tag name if it follows our convention
            // This is a best-effort approach — real PR tracking would store the PR number on the execution
            try
            {
                await CheckForNewFeedbackAsync(
                    gitHubService, eventBus, workflowId, owner, repo, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check PR feedback for workflow {WorkflowId}", workflowId);
            }
        }
    }

    private async Task CheckForNewFeedbackAsync(
        IGitHubService gitHubService,
        IEventBus eventBus,
        Guid workflowId,
        string owner,
        string repo,
        CancellationToken ct)
    {
        // Note: In a full implementation, PR numbers would be stored on the workflow/stage.
        // For now, we search for open PRs whose head branch matches our workflow branch pattern.
        // This is a placeholder for the full PR-tracking integration.

        _logger.LogDebug("Checking for PR feedback for workflow {WorkflowId} in {Owner}/{Repo}", workflowId, owner, repo);

        // The actual PR number would be stored when the PR is created.
        // This monitoring loop processes any new comments found and pushes them via IEventBus.
        // The WorkflowEngine would then pick these up for agent re-execution (FR62).

        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes new comments on a PR and publishes events for agent feedback (FR62).
    /// </summary>
    internal async Task ProcessPullRequestFeedbackAsync(
        IGitHubService gitHubService,
        IEventBus eventBus,
        Guid workflowId,
        string owner,
        string repo,
        int prNumber,
        CancellationToken ct)
    {
        var prKey = $"{owner}/{repo}#{prNumber}";

        // Get all comments
        var comments = await gitHubService.GetPullRequestCommentsAsync(owner, repo, prNumber, ct);

        // Get build status
        var status = await gitHubService.GetPullRequestStatusAsync(owner, repo, prNumber, ct);

        // Track which comments we've already seen
        if (!_seenComments.TryGetValue(prKey, out var seen))
        {
            seen = new HashSet<long>();
            _seenComments[prKey] = seen;
        }

        var newComments = comments.Where(c => !seen.Contains(c.Id)).ToList();
        foreach (var comment in newComments)
        {
            seen.Add(comment.Id);

            _logger.LogInformation(
                "New PR comment on {PrKey} by {Author}: {Body}",
                prKey, comment.Author, comment.Body.Length > 100 ? comment.Body[..100] + "..." : comment.Body);

            // Publish PR feedback event for WorkflowEngine to pick up (FR62)
            await eventBus.PublishToGroupAsync(
                $"workflow-{workflowId}",
                "PullRequestFeedback",
                new
                {
                    workflowId,
                    prNumber,
                    commentId = comment.Id,
                    author = comment.Author,
                    body = comment.Body,
                    isReviewComment = comment.IsReviewComment,
                    createdAt = comment.CreatedAt
                },
                ct);
        }

        // Publish build status event if there are active check runs
        if (status.CheckRuns.Count > 0)
        {
            await eventBus.PublishToGroupAsync(
                $"workflow-{workflowId}",
                "PullRequestBuildStatus",
                new
                {
                    workflowId,
                    prNumber,
                    state = status.State,
                    checkRuns = status.CheckRuns.Select(cr => new
                    {
                        name = cr.Name,
                        status = cr.Status,
                        conclusion = cr.Conclusion
                    })
                },
                ct);
        }
    }

    /// <summary>
    /// Parses "owner/repo" from a GitHub repository URL.
    /// Supports https://github.com/owner/repo and git@github.com:owner/repo.git formats.
    /// </summary>
    internal static (string Owner, string Repo)? ParseOwnerRepo(string repoUrl)
    {
        if (string.IsNullOrEmpty(repoUrl))
            return null;

        // https://github.com/owner/repo.git or https://github.com/owner/repo
        if (repoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = repoUrl
                .Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
                .Replace("git@github.com:", "", StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/')
                .Replace(".git", "", StringComparison.OrdinalIgnoreCase)
                .Split('/');

            if (parts.Length >= 2)
                return (parts[0], parts[1]);
        }

        return null;
    }
}
