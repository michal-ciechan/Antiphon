using Hangfire;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-1677 S1 Hangfire entry point (D-4). One tick: per allow-listed runner, one inventory and at most
/// <c>MaxRemovalsPerTick</c> sequential retention requests. Never overlaps itself; automatic retry is off so a
/// failure is one Failed dashboard entry. It ships enabled for server2; the service does nothing while
/// <c>RunnerWorktreeRetention:Enabled</c> is false (re-read before every request, so it also stops a running
/// tick), sends DryRun only while <c>Execute</c> is false, and stops starting requests past <c>MaxTickMinutes</c>.
/// </summary>
public sealed class RunnerWorktreeRetentionJob(RunnerWorktreeRetentionService service, ILogger<RunnerWorktreeRetentionJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task RunAsync(CancellationToken ct)
    {
        var results = await service.RunTickAsync(ct);
        foreach (var result in results)
            logger.LogInformation(
                "Runner worktree retention tick runner={RunnerId} examined={Examined} eligible={Eligible} backedOff={BackedOff} sent={Sent} stopped={Stopped} run={RunId}",
                result.RunnerId, result.Examined, result.Eligible, result.BackedOff, result.Sent, result.Stopped ?? "-", result.RunId);
    }
}
