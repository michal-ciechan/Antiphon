using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>Settles linked reviewers even when there is no executor walk or the process restarted.</summary>
public sealed class ReviewReviewerReconciler(
    IServiceScopeFactory scopes, TimeProvider time, IOptions<ReviewAgentSettings> options,
    ILogger<ReviewReviewerReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                await ReconcileAsync(null, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Review task reconciliation failed. The next pass retries.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ReconcileAsync(Guid? runId, CancellationToken ct)
    {
        await using var read = scopes.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var timeout = Math.Max(1, options.Value.TimeoutMinutes);
        var legacyCutoff = now.AddMinutes(-timeout);
        var tasks = await (from task in db.AgentTasks.AsNoTracking()
                           join run in db.ReviewRuns.AsNoTracking() on task.ReviewerRunId equals (Guid?)run.Id
                           where task.ReviewerRunId != null && (runId == null || run.Id == runId)
                               && task.Status != AgentTaskStatus.Succeeded && task.Status != AgentTaskStatus.Failed
                               && task.Status != AgentTaskStatus.Canceled
                               && (run.Status == ReviewRunStatus.Closed || task.ExecutionDeadlineAt <= now
                                   || (task.ExecutionDeadlineAt == null && task.CreatedAt <= legacyCutoff))
                           orderby task.CreatedAt, task.Id
                           select new { TaskId = task.Id, RunId = run.Id, Closed = run.Status == ReviewRunStatus.Closed })
            .ToListAsync(ct);
        foreach (var task in tasks)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reason = task.Closed ? $"Review run {task.RunId:D} was closed."
                    : "The review engine timed this reviewer out (Review:Agents:TimeoutMinutes).";
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(task.TaskId, ct, reason);
            }
            catch (Exception ex) when (ex is ConflictException or NotFoundException or DbUpdateConcurrencyException)
            {
                // A changing attempt stays on the next pass until settled. A terminal task is
                // already omitted by the query and may also have settled since we read it.
                logger.LogDebug(ex, "Review task {TaskId} was not cancelled on this pass.", task.TaskId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Review task {TaskId} cancellation failed. The next pass retries.", task.TaskId);
            }
        }
    }
}
