using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Review;

/// <summary>A delegate task as the review engine reads it back: its status, its final report and what it cost.</summary>
public sealed record ReviewTaskState(
    Guid Id,
    AgentTaskStatus Status,
    string? Result,
    string? ResultFilePath,
    string? FailureReason,
    AgentKind Kind,
    AgentModelLevel Level,
    string? Model,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    DateTime CreatedAt,
    DateTime? CompletedAt)
{
    public bool IsFinished => Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;

    /// <summary>The final report: <see cref="Result"/>, or the spilled full report when the stored one has no ABF document.</summary>
    public string? ReportText()
    {
        if (Result is { Length: > 0 } text && text.Contains("@abf", StringComparison.OrdinalIgnoreCase))
            return text;
        if (ResultFilePath is { Length: > 0 } path)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return Result;
    }
}

/// <summary>
/// The review engine's door into delegation: make a commit reachable for a worktree, create a task as
/// a server-originated caller with no parent and no reply routing, read it back, cancel it. One seam so
/// the reviewer runner and the delegation completion model can be tested without launching agents.
/// </summary>
public interface IReviewTaskGateway
{
    /// <summary>
    /// Makes <paramref name="headSha"/> resolvable in <paramref name="repoPath"/>: a GitHub PR head is
    /// fetched from origin's <c>refs/pull/&lt;n&gt;/head</c>; a local subject is already there. Throws
    /// <see cref="ReviewAgentRunnerException"/> when it cannot.
    /// </summary>
    Task EnsureHeadAsync(string repoPath, ReviewHost host, int? prNumber, string headSha, CancellationToken ct);

    /// <summary>Creates the task. <paramref name="callerDirectory"/> is the caller's own tree, which the workspace rules allow.</summary>
    Task<Guid> CreateAsync(CreateAgentTaskRequest request, Guid? projectId, string callerDirectory, CancellationToken ct);

    /// <summary>The task, or null when it no longer exists.</summary>
    Task<ReviewTaskState?> ReadAsync(Guid taskId, CancellationToken ct);

    /// <summary>Cancels the task; a task already finished or gone is left alone.</summary>
    Task CancelAsync(Guid taskId, string reason, CancellationToken ct);
}

/// <summary>
/// <see cref="IReviewTaskGateway"/> over <see cref="AgentTaskService"/>. Each call runs in its own DI
/// scope, so task creation never shares the executor walk's <see cref="AppDbContext"/> and its tracked rows.
/// </summary>
public sealed class AgentTaskReviewGateway(IServiceScopeFactory scopes, StartRefAvailability startRefs, ILogger<AgentTaskReviewGateway> logger)
    : IReviewTaskGateway
{
    public async Task EnsureHeadAsync(string repoPath, ReviewHost host, int? prNumber, string headSha, CancellationToken ct)
    {
        if (host != ReviewHost.GitHub || prNumber is not { } number)
            return;
        var problem = await startRefs.TryFetchPullRequestHeadAsync(repoPath, number, headSha, ct);
        if (problem is not null)
            throw new ReviewAgentRunnerException($"The PR head {headSha} is not reachable in {repoPath}: {problem}.");
    }

    public async Task<Guid> CreateAsync(CreateAgentTaskRequest request, Guid? projectId, string callerDirectory, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var created = await tasks.CreateAsync(request, new AgentTaskService.Caller(null, null, callerDirectory, ProjectId: projectId), ct);
        if (created.Warning is { Length: > 0 } warning)
            logger.LogInformation("Review delegate task {TaskId} created with a warning: {Warning}", created.Id, warning);
        return created.Id;
    }

    public async Task<ReviewTaskState?> ReadAsync(Guid taskId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId)
            .Select(t => new ReviewTaskState(t.Id, t.Status, t.Result, t.ResultFilePath, t.FailureReason, t.AgentKind, t.ModelLevel,
                t.RequestedModelAlias, t.TokensIn + t.CacheReadTokens + t.CacheCreationTokens, t.TokensOut, t.CostUsd, t.CreatedAt, t.CompletedAt))
            .SingleOrDefaultAsync(ct);
    }

    public async Task CancelAsync(Guid taskId, string reason, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(taskId, ct, reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Review delegate task {TaskId} could not be cancelled ({Reason})", taskId, reason);
        }
    }
}
