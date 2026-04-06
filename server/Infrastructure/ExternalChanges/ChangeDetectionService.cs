using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.ExternalChanges;

/// <summary>
/// Background service that polls workflow branches via git fetch to detect
/// external commits (FR65-FR71). Distinguishes Antiphon commits (with [antiphon] trailer)
/// from external commits, auto-pulls on detection, and triggers cascade updates
/// when changes affect _antiphon/artifacts/ paths.
/// </summary>
public class ChangeDetectionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChangeDetectionService> _logger;
    private readonly GitSettings _gitSettings;

    /// <summary>
    /// The artifact directory prefix used to detect cascade-triggering changes (FR68).
    /// </summary>
    private const string ArtifactPathPrefix = "_antiphon/artifacts/";

    /// <summary>
    /// The trailer value that identifies Antiphon-generated commits (FR66).
    /// </summary>
    private const string AntiphonTrailer = "antiphon: true";

    /// <summary>
    /// Tracks the last known remote HEAD per branch to detect new commits.
    /// Key: branch name, Value: last known commit SHA.
    /// </summary>
    private readonly Dictionary<string, string> _lastKnownHeads = new();

    public ChangeDetectionService(
        IServiceScopeFactory scopeFactory,
        IOptions<GitSettings> gitSettings,
        ILogger<ChangeDetectionService> logger)
    {
        _scopeFactory = scopeFactory;
        _gitSettings = gitSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ChangeDetectionService started with {Interval}s poll interval",
            _gitSettings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllWorkflowBranchesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during external change detection polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_gitSettings.PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ChangeDetectionService stopped");
    }

    private async Task PollAllWorkflowBranchesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        // Find active workflows (Running or GateWaiting) with their projects
        var activeWorkflows = await db.Workflows
            .Include(w => w.Project)
            .Include(w => w.Stages.OrderBy(s => s.StageOrder))
            .Where(w => w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.GateWaiting)
            .ToListAsync(ct);

        foreach (var workflow in activeWorkflows)
        {
            var repoPath = workflow.Project.LocalRepositoryPath;
            if (string.IsNullOrEmpty(repoPath))
                continue;

            try
            {
                await DetectExternalChangesForWorkflowAsync(
                    db, eventBus, workflow.Id, workflow.GitBranchName, repoPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to detect external changes for workflow {WorkflowId} in {RepoPath}",
                    workflow.Id, repoPath);
            }
        }
    }

    /// <summary>
    /// Fetches the remote, compares local vs remote HEAD for the workflow branch,
    /// and processes any new external commits found (FR65, FR66, FR67).
    /// </summary>
    internal async Task DetectExternalChangesForWorkflowAsync(
        AppDbContext db,
        IEventBus eventBus,
        Guid workflowId,
        string branchName,
        string repoPath,
        CancellationToken ct)
    {
        // Fetch latest from remote (FR65)
        await RunGitAsync(repoPath, "fetch origin", ct);

        // Get local HEAD
        var localHead = (await RunGitAsync(repoPath, $"rev-parse {branchName}", ct)).Trim();

        // Get remote HEAD (may not exist if branch hasn't been pushed)
        string remoteHead;
        try
        {
            remoteHead = (await RunGitAsync(repoPath, $"rev-parse origin/{branchName}", ct)).Trim();
        }
        catch
        {
            // Remote branch doesn't exist yet — nothing to detect
            return;
        }

        if (localHead == remoteHead)
            return;

        // There are new commits on the remote. Check if they're external.
        var newCommits = await GetNewCommitsAsync(repoPath, localHead, remoteHead, ct);
        if (newCommits.Count == 0)
            return;

        var externalCommits = new List<CommitInfo>();
        var antiphonCommits = new List<CommitInfo>();

        foreach (var commit in newCommits)
        {
            if (IsAntiphonCommit(commit))
            {
                antiphonCommits.Add(commit);
            }
            else
            {
                externalCommits.Add(commit);
            }
        }

        if (externalCommits.Count == 0)
        {
            // All new commits are from Antiphon — just pull to stay in sync
            await RunGitAsync(repoPath, $"checkout {branchName}", ct);
            await RunGitAsync(repoPath, "pull origin", ct);
            _lastKnownHeads[branchName] = remoteHead;
            return;
        }

        _logger.LogInformation(
            "Detected {Count} external commit(s) on branch {Branch} for workflow {WorkflowId}",
            externalCommits.Count, branchName, workflowId);

        // Auto-pull on external commit detection (FR67)
        await RunGitAsync(repoPath, $"checkout {branchName}", ct);
        await RunGitAsync(repoPath, "pull origin", ct);
        _lastKnownHeads[branchName] = remoteHead;

        // Audit trail for external change events (FR71)
        await eventBus.PublishToGroupAsync(
            $"workflow-{workflowId}",
            "ExternalChangeDetected",
            new
            {
                workflowId,
                branchName,
                commitCount = externalCommits.Count,
                commits = externalCommits.Select(c => new
                {
                    sha = c.Sha,
                    author = c.Author,
                    message = c.Message
                }),
                detectedAt = DateTime.UtcNow
            },
            ct);

        // Check if any external commits affect _antiphon/artifacts/ paths (FR68)
        var artifactChanges = await DetectArtifactChangesAsync(
            repoPath, localHead, remoteHead, ct);

        if (artifactChanges.Count > 0)
        {
            _logger.LogInformation(
                "External changes affect {Count} artifact path(s) — triggering cascade detection for workflow {WorkflowId}",
                artifactChanges.Count, workflowId);

            // Determine affected stages from changed artifact paths (FR69)
            var affectedStages = DetermineAffectedStages(artifactChanges, workflowId);

            // Publish cascade trigger event (FR69)
            await eventBus.PublishToGroupAsync(
                $"workflow-{workflowId}",
                "CascadeTriggered",
                new
                {
                    workflowId,
                    trigger = "external_artifact_change",
                    changedPaths = artifactChanges,
                    affectedStages,
                    detectedAt = DateTime.UtcNow
                },
                ct);

            // Audit trail for cascade trigger (FR71)
            await eventBus.PublishToGroupAsync(
                $"workflow-{workflowId}",
                "AuditEvent",
                new
                {
                    workflowId,
                    eventType = "ExternalCascadeTrigger",
                    changedPaths = artifactChanges,
                    affectedStages,
                    externalCommits = externalCommits.Select(c => c.Sha),
                    timestamp = DateTime.UtcNow
                },
                ct);
        }
        else
        {
            // Code-only changes don't trigger cascade (FR70)
            _logger.LogInformation(
                "External changes are code-only (no artifact paths) — no cascade triggered for workflow {WorkflowId}",
                workflowId);
        }
    }

    /// <summary>
    /// Gets the list of commits between two SHAs.
    /// </summary>
    private async Task<IReadOnlyList<CommitInfo>> GetNewCommitsAsync(
        string repoPath, string fromSha, string toSha, CancellationToken ct)
    {
        // Use git log to list commits between the two SHAs
        // Format: SHA|Author|Subject|Trailers
        var output = await RunGitAsync(
            repoPath,
            $"log {fromSha}..{toSha} --format=\"%H|%an|%s|%(trailers:key=antiphon,valueonly)\"",
            ct);

        var commits = new List<CommitInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|', 4);
            if (parts.Length >= 3)
            {
                commits.Add(new CommitInfo(
                    Sha: parts[0].Trim(),
                    Author: parts[1].Trim(),
                    Message: parts[2].Trim(),
                    AntiphonTrailerValue: parts.Length > 3 ? parts[3].Trim() : string.Empty));
            }
        }

        return commits;
    }

    /// <summary>
    /// Checks if a commit was generated by Antiphon by looking for the [antiphon] trailer (FR66).
    /// </summary>
    private static bool IsAntiphonCommit(CommitInfo commit)
    {
        // A commit is from Antiphon if it has the antiphon trailer with value "true"
        return !string.IsNullOrEmpty(commit.AntiphonTrailerValue)
            && commit.AntiphonTrailerValue.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects which files under _antiphon/artifacts/ were changed between two SHAs (FR68).
    /// Returns only artifact paths, filtering out code-only changes (FR70).
    /// </summary>
    private async Task<IReadOnlyList<string>> DetectArtifactChangesAsync(
        string repoPath, string fromSha, string toSha, CancellationToken ct)
    {
        // Get list of changed files between the two commits
        var output = await RunGitAsync(
            repoPath,
            $"diff --name-only {fromSha}..{toSha}",
            ct);

        var artifactPaths = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.StartsWith(ArtifactPathPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Trim())
            .ToList();

        return artifactPaths;
    }

    /// <summary>
    /// Determines which downstream stages are affected by changes to artifact paths (FR69).
    /// Maps artifact paths back to their originating stage based on the path convention:
    /// _antiphon/artifacts/workflow-{id}/{artifactPath}
    /// </summary>
    private static IReadOnlyList<string> DetermineAffectedStages(
        IReadOnlyList<string> changedArtifactPaths, Guid workflowId)
    {
        // Extract unique stage-related paths from the changed artifacts
        // Convention: _antiphon/artifacts/workflow-{id}/{stage-artifact}
        var workflowPrefix = $"{ArtifactPathPrefix}workflow-{workflowId}/";
        var affectedParts = new HashSet<string>();

        foreach (var path in changedArtifactPaths)
        {
            if (path.StartsWith(workflowPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = path[workflowPrefix.Length..];
                // The first segment after the workflow ID is typically the artifact name
                // which maps to the stage that produced it
                var firstSegment = remainder.Split('/')[0];
                if (!string.IsNullOrEmpty(firstSegment))
                {
                    affectedParts.Add(firstSegment);
                }
            }
        }

        return affectedParts.ToList();
    }

    /// <summary>
    /// Runs a git command and returns stdout.
    /// </summary>
    private async Task<string> RunGitAsync(string workingDirectory, string arguments, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogDebug("Running git {Arguments} in {WorkingDirectory}", arguments, workingDirectory);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        var stdoutTask = ReadStreamAsync(process.StandardOutput, stdoutBuilder, cts.Token);
        var stderrTask = ReadStreamAsync(process.StandardError, stderrBuilder, cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        if (process.ExitCode != 0)
        {
            var stderr = stderrBuilder.ToString();
            _logger.LogError("git {Arguments} failed (exit {ExitCode}): {StdErr}", arguments, process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"git {arguments} failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdoutBuilder.ToString();
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder builder, CancellationToken ct)
    {
        var buffer = new char[4096];
        int bytesRead;
        while ((bytesRead = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            builder.Append(buffer, 0, bytesRead);
        }
    }

    /// <summary>
    /// Information about a single git commit used for classification.
    /// </summary>
    internal sealed record CommitInfo(
        string Sha,
        string Author,
        string Message,
        string AntiphonTrailerValue);
}
