using Hangfire;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-1677 S1 Hangfire entry point (D-4). One tick: per allow-listed runner, one inventory and at most
/// <c>MaxRemovalsPerTick</c> sequential retention requests. Never overlaps itself; automatic retry is off so a
/// failure is one Failed dashboard entry. The service does nothing while <c>RunnerWorktreeRetention:Enabled</c>
/// is false, and sends DryRun only while <c>Execute</c> is false.
/// </summary>
public sealed class RunnerWorktreeRetentionJob(RunnerWorktreeRetentionService service, ILogger<RunnerWorktreeRetentionJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task RunAsync(CancellationToken ct)
    {
        var results = await service.RunTickAsync(ct);
        foreach (var result in results)
            logger.LogInformation("Runner worktree retention tick runner={RunnerId} sent={Sent} run={RunId}",
                result.RunnerId, result.Sent, result.RunId);
    }
}
