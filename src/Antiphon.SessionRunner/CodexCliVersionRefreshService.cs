using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>Initial refresh is awaited before adoption releases registration; this owns later refreshes.</summary>
public sealed class CodexCliVersionRefreshService(
    CodexCliVersionProbe probe, TimeProvider clock, IOptions<CodexCliVersionSettings> settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.Value.RefreshIntervalMinutes), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await probe.ReapAsync();
                await probe.RefreshDefaultAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
