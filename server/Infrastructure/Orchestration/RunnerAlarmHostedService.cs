using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

public sealed class RunnerAlarmHostedService(
    IServiceScopeFactory scopes,
    AlarmWakeQueue wake,
    RunnerAlarmState state,
    IOptions<AlarmSettings> settings,
    TimeProvider clock,
    ILogger<RunnerAlarmHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = (scopes, wake, state, clock, logger);
        if (!settings.Value.Enabled)
            return;
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
