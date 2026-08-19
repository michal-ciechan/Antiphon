using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>
/// Drives <see cref="DataRetentionService"/> on a fixed interval. One hosted service covers
/// every table the retention settings name plus the audit FullContent archive; slices 1-3
/// delete sessions, transcripts, queued messages, and whole AgentTask trees, and slice 4
/// nulls stale audit FullContent.
/// </summary>
public sealed class DataRetentionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionSettings _settings;
    private readonly ILogger<DataRetentionHostedService> _logger;

    public DataRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionSettings> settings,
        ILogger<DataRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.SweepHours <= 0)
        {
            _logger.LogInformation("Data retention sweep disabled (SweepHours <= 0)");
            return;
        }

        try
        {
            // First pass on start so a deploy does not wait SweepHours to take effect.
            await SweepAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.SweepHours));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var retention = scope.ServiceProvider.GetRequiredService<DataRetentionService>();
            var result = await retention.RunOnceAsync(stoppingToken);
            _logger.LogInformation(
                "Retention sweep deleted {Sessions} session row(s), {Transcripts} transcript row(s), {QueuedMessages} queued message(s), {Tasks} task row(s), {AuditRecords} audit record(s)",
                result.Sessions, result.Transcripts, result.QueuedMessages, result.Tasks, result.AuditRecords);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Data retention sweep failed");
        }
    }
}
