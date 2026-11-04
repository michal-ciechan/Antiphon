using Hangfire;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-0824 S4a Hangfire entry point (S-2). One tick: at most <c>MaxReleasesPerTick</c> system releases of
/// terminal desktop <c>card-task-*</c> worktrees, each retired through the guarded removal. Never overlaps itself;
/// automatic retry is off so a failure is one Failed dashboard entry. It ships on; the service does nothing while
/// <c>WorktreeLegacyRelease:Enabled</c> is false or the settings are invalid (re-read before every row), and
/// files nothing while <c>Execute</c> is false.
/// </summary>
public sealed class DesktopLegacyReleasePolicyJob(DesktopLegacyReleasePolicyService service, ILogger<DesktopLegacyReleasePolicyJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct)
    {
        var result = await service.RunTickAsync(ct);
        logger.LogInformation(
            "Desktop legacy release tick examined={Examined} eligible={Eligible} backedOff={BackedOff} filed={Filed} retired={Retired} stopped={Stopped} run={RunId}",
            result.Examined, result.Eligible, result.BackedOff, result.Filed, result.Retired, result.Stopped ?? "-", result.RunId);
    }
}
