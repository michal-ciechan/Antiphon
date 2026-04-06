using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var workflows = app.MapGroup("/api/workflows")
            .WithTags("Workflows");

        workflows.MapGet("/", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var items = await db.Workflows
                .Include(w => w.Template)
                .Include(w => w.Project)
                .Include(w => w.CurrentStage)
                .Include(w => w.Stages)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync(cancellationToken);

            var dtos = items.Select(w => new WorkflowDto(
                w.Id,
                w.Name,
                w.Description,
                w.Status,
                w.CurrentStage?.Name,
                w.TemplateId,
                w.Template.Name,
                w.ProjectId,
                w.Project.Name,
                w.Stages.Count,
                w.Stages.Count(s => s.Status == StageStatus.Completed),
                WorkflowStateMachine.GetAvailableTransitions(w.Status),
                w.FeatureName,
                w.CreatedAt,
                w.UpdatedAt)).ToList();

            return Results.Ok(dtos);
        });

        workflows.MapGet("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var w = await db.Workflows
                .Include(w => w.Template)
                .Include(w => w.Project)
                .Include(w => w.CurrentStage)
                .Include(w => w.Stages.OrderBy(s => s.StageOrder))
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

            if (w is null)
            {
                return Results.NotFound();
            }

            var dto = new WorkflowDetailDto(
                w.Id,
                w.Name,
                w.Description,
                w.Status,
                w.CurrentStage?.Name,
                w.TemplateId,
                w.Template.Name,
                w.ProjectId,
                w.Project.Name,
                w.Stages.Count,
                w.Stages.Count(s => s.Status == StageStatus.Completed),
                WorkflowStateMachine.GetAvailableTransitions(w.Status),
                w.Stages.Select(s => new StageDto(
                    s.Id,
                    s.Name,
                    s.StageOrder,
                    s.Status,
                    s.GateRequired,
                    s.CurrentVersion)).ToList(),
                w.FeatureName,
                w.CreatedAt,
                w.UpdatedAt);

            return Results.Ok(dto);
        });

        workflows.MapPost("/", async (
            CreateWorkflowRequest request,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            var workflow = await engine.CreateWorkflowAsync(request, cancellationToken);
            return Results.Created($"/api/workflows/{workflow.Id}", new { id = workflow.Id });
        });

        workflows.MapPost("/{id:guid}/pause", async (
            Guid id,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            await engine.PauseWorkflowAsync(id, cancellationToken);
            return Results.NoContent();
        });

        workflows.MapPost("/{id:guid}/resume", async (
            Guid id,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            await engine.ResumeWorkflowAsync(id, cancellationToken);
            return Results.NoContent();
        });

        workflows.MapPost("/{id:guid}/abandon", async (
            Guid id,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            await engine.AbandonWorkflowAsync(id, cancellationToken);
            return Results.NoContent();
        });

        workflows.MapDelete("/{id:guid}", async (
            Guid id,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            await engine.DeleteWorkflowAsync(id, cancellationToken);
            return Results.NoContent();
        });

        // Workflow visit — records when a user opens the workflow detail page
        workflows.MapPost("/{id:guid}/visit", async (
            Guid id,
            HttpContext httpContext,
            AuditService auditService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var exists = await db.Workflows.AnyAsync(w => w.Id == id, cancellationToken);
            if (!exists) return Results.NotFound();

            var clientIp = httpContext.Items["ClientIp"] as string;
            await auditService.RecordEventAsync(
                AuditEventType.WorkflowOpened,
                workflowId: id,
                stageId: null,
                stageExecutionId: null,
                summary: "Workflow opened in browser",
                clientIp: clientIp,
                userId: null,
                gitTagName: null,
                fullContentJson: null,
                cancellationToken
            );
            return Results.NoContent();
        });

        // Workflow close — records when a user navigates away from the workflow detail page
        workflows.MapPost("/{id:guid}/close", async (
            Guid id,
            HttpContext httpContext,
            AuditService auditService,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var exists = await db.Workflows.AnyAsync(w => w.Id == id, cancellationToken);
            if (!exists) return Results.NotFound();

            var clientIp = httpContext.Items["ClientIp"] as string;
            await auditService.RecordEventAsync(
                AuditEventType.WorkflowClosed,
                workflowId: id,
                stageId: null,
                stageExecutionId: null,
                summary: "Workflow closed in browser",
                clientIp: clientIp,
                userId: null,
                gitTagName: null,
                fullContentJson: null,
                cancellationToken
            );
            return Results.NoContent();
        });

        // Branch diff endpoint — compares the project base branch against the workflow's git branch
        workflows.MapGet("/{id:guid}/branch-diff", async (
            Guid id,
            AppDbContext db,
            IGitService gitService,
            IGitHubService gitHubService,
            IOptions<GitSettings> gitSettingsOptions,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .Include(w => w.Project)
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

            if (workflow is null) return Results.NotFound();

            var project = workflow.Project;

            // Resolve local repo path: explicit path or auto-clone under workspace
            var repoPath = project.LocalRepositoryPath;
            if (string.IsNullOrEmpty(repoPath))
            {
                var settings = gitSettingsOptions.Value;
                if (string.IsNullOrEmpty(settings.WorkspacePath))
                    return Results.Problem(
                        "No workspace path is configured. Set Git:WorkspacePath in appsettings.",
                        statusCode: 503
                    );

                var workspacePath = Path.IsPathRooted(settings.WorkspacePath)
                    ? settings.WorkspacePath
                    : Path.Combine(AppContext.BaseDirectory, settings.WorkspacePath);

                var slug = SanitizeName(project.Name);
                repoPath = Path.Combine(workspacePath, slug);

                // Clone or fetch to ensure the repo is present and up-to-date
                try
                {
                    await gitService.EnsureCloneAsync(project.GitRepositoryUrl, repoPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        $"Failed to clone/fetch repository: {ex.Message}",
                        statusCode: 422
                    );
                }
            }

            // Use the branch name the workflow engine told the agent to use.
            // If the agent didn't follow it, we fall back to scanning for the most recent Antiphon commit.
            var headBranch = workflow.GitBranchName;

            // Look up GitHub PR for this branch — use PR's base branch if found, else project default
            PullRequestInfo? pr = null;
            if (project.GitHubIntegrationEnabled)
            {
                var ownerRepo = ParseOwnerRepo(project.GitRepositoryUrl);
                if (ownerRepo is var (owner, repo))
                    pr = await gitHubService.FindPullRequestForBranchAsync(owner, repo, headBranch, cancellationToken);
            }

            var resolvedBase = pr?.BaseBranch ?? project.BaseBranch;
            var baseBranch = $"origin/{resolvedBase}";

            string rawDiff;
            try
            {
                rawDiff = await gitService.GetBranchDiffAsync(baseBranch, headBranch, repoPath, cancellationToken);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("unknown revision") || ex.Message.Contains("ambiguous argument"))
            {
                // Branch doesn't exist — maybe the agent used a different name. Try finding it.
                var agentBranch = await gitService.FindAgentBranchAsync(repoPath, cancellationToken);
                if (agentBranch is null)
                    return Results.Problem(
                        "Workflow branch not yet initialized. The branch will be created when the first stage runs.",
                        statusCode: 404
                    );

                headBranch = agentBranch;

                // Re-resolve PR for the discovered branch
                if (project.GitHubIntegrationEnabled && pr is null)
                {
                    var ownerRepo = ParseOwnerRepo(project.GitRepositoryUrl);
                    if (ownerRepo is var (owner2, repo2))
                        pr = await gitHubService.FindPullRequestForBranchAsync(
                            owner2, repo2, headBranch.Replace("origin/", ""), cancellationToken);
                }

                resolvedBase = pr?.BaseBranch ?? project.BaseBranch;
                baseBranch = $"origin/{resolvedBase}";

                try
                {
                    rawDiff = await gitService.GetBranchDiffAsync(baseBranch, headBranch, repoPath, cancellationToken);
                }
                catch (InvalidOperationException ex2)
                {
                    return Results.Problem(ex2.Message, statusCode: 422);
                }
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 422);
            }

            var files = ParseUnifiedDiff(rawDiff);
            var result = new BranchDiffDto(
                baseBranch,
                headBranch,
                files,
                pr?.Number,
                pr?.HtmlUrl,
                pr?.Title,
                pr?.State);
            return Results.Ok(result);
        });

        // File content endpoint — reads a specific file from the workflow's git branch
        workflows.MapGet("/{id:guid}/file-content", async (
            Guid id,
            string path,
            AppDbContext db,
            IGitService gitService,
            IOptions<GitSettings> gitSettingsOptions,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(path))
                return Results.BadRequest("path query parameter is required");

            var workflow = await db.Workflows
                .Include(w => w.Project)
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

            if (workflow is null) return Results.NotFound();

            var project = workflow.Project;

            // Resolve local repo path (same logic as branch-diff)
            var repoPath = project.LocalRepositoryPath;
            if (string.IsNullOrEmpty(repoPath))
            {
                var settings = gitSettingsOptions.Value;
                if (string.IsNullOrEmpty(settings.WorkspacePath))
                    return Results.Problem("No workspace path configured.", statusCode: 503);

                var workspacePath = Path.IsPathRooted(settings.WorkspacePath)
                    ? settings.WorkspacePath
                    : Path.Combine(AppContext.BaseDirectory, settings.WorkspacePath);

                var slug = SanitizeName(project.Name);
                repoPath = Path.Combine(workspacePath, slug);

                try
                {
                    await gitService.EnsureCloneAsync(project.GitRepositoryUrl, repoPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to clone/fetch repository: {ex.Message}", statusCode: 422);
                }
            }

            // Resolve head branch with fallback (same as branch-diff)
            var headBranch = workflow.GitBranchName;

            string content;
            try
            {
                content = await gitService.GetFileContentAsync(headBranch, path, repoPath, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Branch may not exist — try fallback discovery
                var agentBranch = await gitService.FindAgentBranchAsync(repoPath, cancellationToken);
                if (agentBranch is null)
                    return Results.NotFound();

                headBranch = agentBranch;
                try
                {
                    content = await gitService.GetFileContentAsync(headBranch, path, repoPath, cancellationToken);
                }
                catch (InvalidOperationException ex2)
                {
                    return Results.Problem(ex2.Message, statusCode: 404);
                }
            }

            return Results.Ok(new { path, content });
        });

        // Feature status endpoint — aggregates completed stages across all workflows sharing
        // the same (projectId, featureName) pair.
        var projects = app.MapGroup("/api/projects")
            .WithTags("Projects");

        projects.MapGet("/{projectId:guid}/feature-status/{featureName}", async (
            Guid projectId,
            string featureName,
            FeatureStatusService featureStatusService,
            HttpContext httpContext) =>
        {
            var result = await featureStatusService.GetFeatureStatusAsync(
                projectId, featureName, httpContext.RequestAborted);
            return Results.Ok(result);
        });
    }

    private static (string Owner, string Repo)? ParseOwnerRepo(string gitUrl)
    {
        try
        {
            var uri = new Uri(gitUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2)
            {
                var owner = segments[^2];
                var repo = segments[^1].Replace(".git", "", StringComparison.OrdinalIgnoreCase);
                return (owner, repo);
            }
        }
        catch
        {
            // ignore malformed URLs
        }
        return null;
    }

    private static string SanitizeName(string name)
    {
        var sanitized = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        return sanitized.Trim('-');
    }

    /// <summary>
    /// Parses a unified diff string into per-file entries.
    /// </summary>
    private static List<BranchDiffFileDto> ParseUnifiedDiff(string diff)
    {
        var files = new List<BranchDiffFileDto>();
        if (string.IsNullOrWhiteSpace(diff)) return files;

        var lines = diff.Split('\n');
        string? currentFile = null;
        var patchLines = new List<string>();
        var additions = 0;
        var deletions = 0;

        void FlushFile()
        {
            if (currentFile is null) return;
            files.Add(new BranchDiffFileDto(currentFile, additions, deletions, string.Join('\n', patchLines)));
            currentFile = null;
            patchLines.Clear();
            additions = 0;
            deletions = 0;
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushFile();
                // Extract file name: "diff --git a/path b/path" → "path"
                var match = Regex.Match(line, @"diff --git a/(.+) b/(.+)");
                currentFile = match.Success ? match.Groups[2].Value : line;
                patchLines.Add(line);
            }
            else if (currentFile is not null)
            {
                patchLines.Add(line);
                if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
                    additions++;
                else if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
                    deletions++;
            }
        }

        FlushFile();
        return files;
    }
}
