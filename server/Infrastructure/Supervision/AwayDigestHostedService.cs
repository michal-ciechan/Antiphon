using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Runs the blocked-task wake sweep and the scheduled digest check in fresh scopes.</summary>
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
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<BlockedTaskNotifier>().SweepAsync(stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<DecisionCardNotifier>().SweepAsync(stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<AwayDigestNotifier>().SendDueAsync(null, null, false, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _logger.LogWarning(ex, "Away digest sweep failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
