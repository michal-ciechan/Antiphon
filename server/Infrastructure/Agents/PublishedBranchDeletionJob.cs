using Hangfire;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-0824 S2 (Q-6). Hangfire entry point of the origin branch deletion sweep, registered only while
/// <c>Landing:DeleteSourceBranchAfterPublication</c> is true at startup. The service re-reads the flag before every
/// row, so switching it off stops a run in flight. No automatic retry and no overlap.
/// </summary>
public sealed class PublishedBranchDeletionJob(PublishedBranchDeletionService service, ILogger<PublishedBranchDeletionJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public async Task<PublishedBranchDeletionRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = await service.RunAsync(cancellationToken);
        logger.LogInformation(
            "Published branch deletion run enabled={Enabled} dryRun={DryRun} examined={Examined} actions={Actions} stopped={Stopped}",
            result.Enabled, result.DryRun, result.Examined, result.PushAttempts, result.Stopped);
        foreach (var row in result.Rows)
            logger.LogInformation("Published branch deletion {Outcome} {Reason} {Branch} task={TaskId} op={OperationId}",
                row.Outcome, row.Reason, row.SourceFullRef, row.TaskId, row.OperationId);
        return result;
    }
}
