using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Runs each digest sweep call in its own scope and its own try, so one failure does not skip the rest.
/// </summary>
public sealed class AwayDigestHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DigestSettings _settings;
    private readonly ILogger<AwayDigestHostedService> _logger;
    public AwayDigestHostedService(IServiceScopeFactory scopes, IOptions<DigestSettings> settings, ILogger<AwayDigestHostedService> logger)
    { _scopes = scopes; _settings = settings.Value; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled) { _logger.LogInformation("Away digest disabled by configuration"); return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _settings.SweepSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepOneAsync<BlockedTaskNotifier>(n => n.SweepAsync(stoppingToken), stoppingToken);
                await SweepOneAsync<BlockedSeatEscalationNotifier>(n => n.SweepAsync(stoppingToken), stoppingToken);
                await SweepOneAsync<DecisionCardNotifier>(n => n.SweepAsync(stoppingToken), stoppingToken);
                await SweepOneAsync<IncidentPageNotifier>(n => n.SweepAsync(stoppingToken), stoppingToken);
                await SweepOneAsync<AwayDigestNotifier>(n => n.SendDueAsync(null, null, false, stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SweepOneAsync<T>(Func<T, Task> call, CancellationToken stoppingToken)
        where T : notnull
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await call(scope.ServiceProvider.GetRequiredService<T>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogWarning(ex, "Away digest sweep failed"); }
    }
}
