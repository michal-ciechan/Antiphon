using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1817. Idle cadence for <see cref="RunnerRepositoryMaintenance"/>. The first pass waits
/// five minutes so boot adoption is not sharing the repository with gc, then the check interval
/// decides whether the week or the loose-object threshold is due. Disabled when phone-home is off
/// or <c>PhoneHome:RepositoryMaintenance:Enabled</c> is false.
/// </summary>
public sealed class RunnerRepositoryMaintenanceService(
    RunnerRepositoryMaintenance maintenance,
    IOptions<PhoneHomeSettings> settings,
    ILogger<RunnerRepositoryMaintenanceService> logger) : BackgroundService
{
    /// <summary>CARD-1818. Production waits five minutes. A test sets a shorter delay.</summary>
    internal TimeSpan InitialDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>CARD-1818. Production uses CheckIntervalMinutes. A test sets a short interval.</summary>
    internal TimeSpan? Interval { get; init; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var phoneHome = settings.Value;
        if (!phoneHome.Enabled || !phoneHome.RepositoryMaintenance.Enabled)
            return;
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        { return; }

        using var timer = new PeriodicTimer(Interval ?? TimeSpan.FromMinutes(phoneHome.RepositoryMaintenance.CheckIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await maintenance.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Repository maintenance pass failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            { return; }
        }
    }
}
