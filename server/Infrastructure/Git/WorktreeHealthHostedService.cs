using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-0147 S3: periodic detection-only sweep of <c>feat/card-task-*</c> worktrees.
/// Never prune, heal, cancel, or re-dispatch.
/// </summary>
public sealed class WorktreeHealthHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GitSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorktreeHealthHostedService> _logger;

    public WorktreeHealthHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<GitSettings> settings,
        TimeProvider timeProvider,
        ILogger<WorktreeHealthHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorktreeHealthHostedService started");

        // Short delay so create/dispatch is not competing with a boot scan.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var health = scope.ServiceProvider.GetRequiredService<WorktreeHealthService>();
                var report = await health.SweepAsync(stoppingToken);
                if (report.FindingCount > 0)
                {
                    _logger.LogWarning(
                        "Worktree health sweep found {Count} uncleared feat/card-task-* mismatches (detection only; nothing pruned)",
                        report.FindingCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Worktree health sweep failed");
            }

            var seconds = Math.Max(5, _settings.WorktreeHealthIntervalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("WorktreeHealthHostedService stopped");
    }
}
