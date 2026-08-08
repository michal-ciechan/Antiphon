using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// The agent Files review surface: workspace file listing (git ∪ agent activity), content/diff
/// reads, viewed/reviewed marks, and inline comment threads with agent dispatch.
/// </summary>
public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        var agents = app.MapGroup("/api/agents/{agentId:guid}")
            .WithTags("Review");

        agents.MapGet("/files", async (
            Guid agentId, string? since, AgentFilesService files, CancellationToken ct) =>
        {
            var result = await files.GetFilesAsync(agentId, since, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        agents.MapGet("/files/commits", async (
            Guid agentId, int? limit, AgentFilesService files, CancellationToken ct) =>
        {
            var result = await files.GetCommitsAsync(agentId, limit ?? 30, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        agents.MapGet("/files/tree", async (
            Guid agentId, AgentFilesService files, CancellationToken ct) =>
        {
            var result = await files.GetTreeAsync(agentId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        agents.MapGet("/files/content", async (
            Guid agentId, string path, string? rev, string? since, AgentFilesService files, CancellationToken ct) =>
        {
            var result = await files.GetContentAsync(agentId, path, rev ?? "work", since, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        agents.MapGet("/files/diff", async (
            Guid agentId, string path, string? since, AgentFilesService files, CancellationToken ct) =>
        {
            var diff = await files.GetDiffAsync(agentId, path, since, ct);
            return Results.Ok(new { path, diff });
        });

        agents.MapPost("/files/review", async (
            Guid agentId, MarkFilesReviewRequest request, AgentFilesService files, CancellationToken ct) =>
        {
            FileReviewLevel? level = request.Level?.ToLowerInvariant() switch
            {
                "viewed" => FileReviewLevel.Viewed,
                "reviewed" => FileReviewLevel.Reviewed,
                _ => null,
            };
            var count = await files.MarkAsync(agentId, request.Paths, request.Prefix, level, request.Since, ct);
            return Results.Ok(new { marked = count });
        });

        agents.MapGet("/review/sections", async (
            Guid agentId, string path, AgentFilesService files, CancellationToken ct) =>
        {
            var result = await files.GetSectionReviewsAsync(agentId, path, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        agents.MapPost("/review/sections", async (
            Guid agentId, MarkSectionReviewsRequest request, AgentFilesService files, CancellationToken ct) =>
        {
            var count = await files.MarkSectionsAsync(agentId, request, ct);
            return Results.Ok(new { marked = count });
        });

        agents.MapPost("/review/checkpoint", async (
            Guid agentId, SetReviewCheckpointRequest? request,
            AgentReviewCheckpointService checkpoints, CancellationToken ct) =>
        {
            var commit = string.IsNullOrWhiteSpace(request?.CommitSha) ? null : request!.CommitSha!.Trim();
            var reason = string.IsNullOrWhiteSpace(request?.Reason)
                ? commit is null ? "Manual baseline" : $"Manual baseline at {commit[..Math.Min(7, commit.Length)]}"
                : request!.Reason!.Trim();
            var checkpoint = await checkpoints.CaptureAsync(agentId, reason, commit, ct);
            if (checkpoint is null)
            {
                return commit is null
                    ? Results.NotFound()
                    : Results.BadRequest(new { message = $"Commit '{commit}' not found in the workspace history." });
            }
            return Results.Ok(new { checkpoint.Id, checkpoint.CommitSha, checkpoint.CreatedAt, checkpoint.Reason });
        });

        agents.MapGet("/review/threads", async (
            Guid agentId, string? path, ReviewThreadService threads, CancellationToken ct) =>
            Results.Ok(await threads.GetThreadsAsync(agentId, path, ct)));

        agents.MapPost("/review/threads", async (
            Guid agentId, CreateReviewThreadRequest request, ReviewThreadService threads, CancellationToken ct) =>
        {
            var thread = await threads.CreateAsync(agentId, request, ct);
            return thread is null ? Results.NotFound() : Results.Ok(thread);
        });

        var thread = app.MapGroup("/api/review/threads/{threadId:guid}")
            .WithTags("Review");

        thread.MapPost("/comments", async (
            Guid threadId, AddReviewCommentRequest request, ReviewThreadService threads, CancellationToken ct) =>
        {
            var result = await threads.AddCommentAsync(threadId, request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        thread.MapPost("/dispatch", async (
            Guid threadId, ReviewThreadService threads, CancellationToken ct) =>
        {
            var ok = await threads.DispatchAsync(threadId, ct);
            return ok
                ? Results.Ok()
                : Results.Conflict(new { message = "Thread not dispatched — agent session is not running." });
        });

        thread.MapPost("/resolve", async (
            Guid threadId, ReviewThreadService threads, CancellationToken ct) =>
        {
            var result = await threads.ResolveAsync(threadId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }
}
