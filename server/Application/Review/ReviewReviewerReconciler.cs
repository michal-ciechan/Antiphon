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
    private const int BatchSize = 16;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReleaseAttemptBudget = TimeSpan.FromSeconds(8);
    private (DateTime CreatedAt, Guid Id)? _cursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, time);
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
        using var passBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        passBudget.CancelAfter(SweepInterval);
        var pass = passBudget.Token;
        await using var read = scopes.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var timeout = Math.Max(1, options.Value.TimeoutMinutes);
        var legacyCutoff = now.AddMinutes(-timeout);
        var eligible = from task in db.AgentTasks.AsNoTracking()
                       join run in db.ReviewRuns.AsNoTracking() on task.ReviewerRunId equals (Guid?)run.Id
                       where task.ReviewerRunId != null && (runId == null || run.Id == runId)
                           && task.Status != AgentTaskStatus.Succeeded && task.Status != AgentTaskStatus.Failed
                           && task.Status != AgentTaskStatus.Canceled
                           && (run.Status == ReviewRunStatus.Closed || task.Status == AgentTaskStatus.Blocked
                               || task.ExecutionDeadlineAt <= now
                               || (task.ExecutionDeadlineAt == null && task.CreatedAt <= legacyCutoff))
                       select new
                       {
                           task.Id, task.CreatedAt, RunId = run.Id, Closed = run.Status == ReviewRunStatus.Closed,
                           Expired = task.ExecutionDeadlineAt <= now || (task.ExecutionDeadlineAt == null && task.CreatedAt <= legacyCutoff),
                       };
        if (runId is null && _cursor is { } after)
            eligible = eligible.Where(t => t.CreatedAt > after.CreatedAt
                || (t.CreatedAt == after.CreatedAt && t.Id.CompareTo(after.Id) > 0));

        var selection = eligible.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(BatchSize).ToListAsync(pass);
        try { await selection; }
        catch (OperationCanceledException) when (pass.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Review task selection reached its sweep deadline. A later pass retries it.");
            return;
        }
        var tasks = await selection;
        if (runId is null && tasks.Count == 0)
            _cursor = null;
        foreach (var task in tasks)
        {
            ct.ThrowIfCancellationRequested();
            if (pass.IsCancellationRequested)
                break;
            // Advance even on a transient failure so one unreachable runner cannot starve later tasks.
            if (runId is null)
                _cursor = (task.CreatedAt, task.Id);
            using var attemptBudget = CancellationTokenSource.CreateLinkedTokenSource(pass);
            attemptBudget.CancelAfter(ReleaseAttemptBudget);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reason = task.Closed ? $"Review run {task.RunId:D} was closed."
                    : task.Expired ? "The review engine timed this reviewer out (Review:Agents:TimeoutMinutes)."
                    : "The review engine takes no questions from a reviewer.";
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(task.Id, attemptBudget.Token, reason);
            }
            catch (Exception ex) when (ex is ConflictException or NotFoundException or DbUpdateConcurrencyException)
            {
                // A changing attempt stays on the next pass until settled. A terminal task is
                // already omitted by the query and may also have settled since we read it.
                logger.LogDebug(ex, "Review task {TaskId} was not cancelled on this pass.", task.Id);
            }
            catch (OperationCanceledException) when (attemptBudget.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Review task {TaskId} reached its release attempt deadline. A later pass retries it.", task.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Review task {TaskId} cancellation failed. The next pass retries.", task.Id);
            }
        }
    }
}
