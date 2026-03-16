using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Implements IGitService using git CLI commands via Process.Start.
/// Supports the two-tier branching/tagging strategy for workflow artifacts (FR28-FR34).
/// </summary>
public class GitService : IGitService
{
    private readonly ILogger<GitService> _logger;

    /// <summary>
    /// Timeout for git operations — must complete within 5 seconds for repos under 1GB (NFR5).
    /// </summary>
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    public GitService(ILogger<GitService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the workflow master branch name: antiphon/workflow-{id}/master
    /// </summary>
    public static string GetWorkflowMasterBranch(Guid workflowId) =>
        $"antiphon/workflow-{workflowId}/master";

    /// <summary>
    /// Gets the stage branch name: antiphon/workflow-{id}/stage-{name}
    /// </summary>
    public static string GetStageBranch(Guid workflowId, string stageName) =>
        $"antiphon/workflow-{workflowId}/stage-{stageName}";

    /// <summary>
    /// Gets the stage tag name: antiphon/workflow-{id}/{stage}-v{version}
    /// </summary>
    public static string GetStageTag(Guid workflowId, string stageName, int version) =>
        $"antiphon/workflow-{workflowId}/{stageName}-v{version}";

    /// <summary>
    /// Gets the artifact directory path: _antiphon/artifacts/workflow-{id}/
    /// </summary>
    public static string GetArtifactDirectory(Guid workflowId) =>
        $"_antiphon/artifacts/workflow-{workflowId}";

    public async Task InitializeWorkflowBranchesAsync(Guid workflowId, string repoPath, CancellationToken ct)
    {
        var branchName = GetWorkflowMasterBranch(workflowId);
        _logger.LogInformation("Creating workflow master branch {Branch} in {RepoPath}", branchName, repoPath);

        // Create the workflow master branch from the current HEAD
        await RunGitAsync(repoPath, $"checkout -b {branchName}", ct);

        // Create the artifact directory and add a .gitkeep so the directory is tracked
        var artifactDir = Path.Combine(repoPath, GetArtifactDirectory(workflowId));
        Directory.CreateDirectory(artifactDir);
        var gitkeepPath = Path.Combine(artifactDir, ".gitkeep");
        await File.WriteAllTextAsync(gitkeepPath, string.Empty, ct);

        await RunGitAsync(repoPath, $"add {GetArtifactDirectory(workflowId)}", ct);
        await RunGitAsync(repoPath, BuildCommitArgs("Initialize workflow artifact directory"), ct);
    }

    public async Task CreateStageBranchAsync(Guid workflowId, string stageName, string repoPath, CancellationToken ct)
    {
        var workflowMaster = GetWorkflowMasterBranch(workflowId);
        var stageBranch = GetStageBranch(workflowId, stageName);
        _logger.LogInformation("Creating stage branch {Branch} from {Base} in {RepoPath}", stageBranch, workflowMaster, repoPath);

        // Create the stage branch from the workflow master
        await RunGitAsync(repoPath, $"checkout -b {stageBranch} {workflowMaster}", ct);
    }

    public async Task CommitArtifactAsync(Guid workflowId, string stageName, string content, string artifactPath, string repoPath, CancellationToken ct)
    {
        var stageBranch = GetStageBranch(workflowId, stageName);
        _logger.LogInformation("Committing artifact {Path} on branch {Branch}", artifactPath, stageBranch);

        // Ensure we're on the stage branch
        await RunGitAsync(repoPath, $"checkout {stageBranch}", ct);

        // Write artifact content to the proper path
        var artifactDir = GetArtifactDirectory(workflowId);
        var fullArtifactDir = Path.Combine(repoPath, artifactDir);
        Directory.CreateDirectory(fullArtifactDir);

        var fullPath = Path.Combine(repoPath, artifactDir, artifactPath);
        var parentDir = Path.GetDirectoryName(fullPath);
        if (parentDir is not null)
            Directory.CreateDirectory(parentDir);

        await File.WriteAllTextAsync(fullPath, content, ct);

        // Stage and commit with [antiphon] trailer
        await RunGitAsync(repoPath, $"add {artifactDir}/{artifactPath}", ct);
        var message = $"Add artifact {artifactPath} for stage {stageName}";
        await RunGitAsync(repoPath, BuildCommitArgs(message), ct);
    }

    public async Task<string> TagStageAsync(Guid workflowId, string stageName, int version, string repoPath, CancellationToken ct)
    {
        var stageBranch = GetStageBranch(workflowId, stageName);
        var tagName = GetStageTag(workflowId, stageName, version);
        _logger.LogInformation("Tagging stage branch {Branch} as {Tag}", stageBranch, tagName);

        // Ensure we're on the stage branch
        await RunGitAsync(repoPath, $"checkout {stageBranch}", ct);

        // Create the tag at the current HEAD of the stage branch
        await RunGitAsync(repoPath, $"tag {tagName}", ct);

        return tagName;
    }

    public async Task MergeStageBranchAsync(Guid workflowId, string stageName, string repoPath, CancellationToken ct)
    {
        var workflowMaster = GetWorkflowMasterBranch(workflowId);
        var stageBranch = GetStageBranch(workflowId, stageName);
        _logger.LogInformation("Merging stage branch {Stage} into {Master}", stageBranch, workflowMaster);

        // Checkout workflow master
        await RunGitAsync(repoPath, $"checkout {workflowMaster}", ct);

        // Merge stage branch with a merge commit
        await RunGitAsync(repoPath, $"merge --no-ff {stageBranch} -m \"Merge stage {stageName} into workflow master\n\nSigned-off-by: Antiphon\n[antiphon]\"", ct);
    }

    public async Task<string> GetDiffBetweenTagsAsync(string tag1, string tag2, string repoPath, CancellationToken ct)
    {
        _logger.LogInformation("Computing diff between {Tag1} and {Tag2}", tag1, tag2);

        var result = await RunGitAsync(repoPath, $"diff {tag1}..{tag2}", ct);
        return result;
    }

    /// <summary>
    /// Builds git commit arguments with the [antiphon] trailer (FR30).
    /// Uses --trailer to add [antiphon] as a proper git trailer.
    /// </summary>
    private static string BuildCommitArgs(string subject)
    {
        // Git trailers are added as key-value pairs in the commit message body.
        // The [antiphon] trailer identifies system-generated commits (FR30, FR66).
        return $"commit -m \"{subject}\" --trailer \"antiphon=true\"";
    }

    /// <summary>
    /// Runs a git command in the specified repo directory and returns stdout.
    /// </summary>
    private async Task<string> RunGitAsync(string workingDirectory, string arguments, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(GitTimeout);

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
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        var stdout = stdoutBuilder.ToString();
        var stderr = stderrBuilder.ToString();

        if (process.ExitCode != 0)
        {
            _logger.LogError("git {Arguments} failed (exit {ExitCode}): {StdErr}", arguments, process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"git {arguments} failed with exit code {process.ExitCode}: {stderr}");
        }

        _logger.LogDebug("git {Arguments} completed successfully", arguments);
        return stdout;
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
}
