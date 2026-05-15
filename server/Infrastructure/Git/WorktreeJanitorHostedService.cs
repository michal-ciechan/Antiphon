using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class WorktreeJanitorHostedService : BackgroundService
{
    private readonly IWorktreeManager _worktreeManager;
    private readonly GitSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorktreeJanitorHostedService> _logger;

    public WorktreeJanitorHostedService(
        IWorktreeManager worktreeManager,
        IOptions<GitSettings> settings,
        TimeProvider timeProvider,
        ILogger<WorktreeJanitorHostedService> logger)
    {
        _worktreeManager = worktreeManager;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorktreeJanitorHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pruned = await _worktreeManager.PruneStaleAsync(stoppingToken);
                if (pruned > 0)
                    _logger.LogInformation("Pruned {Count} stale worktrees", pruned);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stale worktree pruning cycle");
            }

            var interval = TimeSpan.FromHours(Math.Max(1, _settings.WorktreeJanitorIntervalHours));
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }

        _logger.LogInformation("WorktreeJanitorHostedService stopped");
    }
}
