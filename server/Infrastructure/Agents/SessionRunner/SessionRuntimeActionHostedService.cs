using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class SessionRuntimeActionHostedService(SessionRuntimeActionCoordinator coordinator) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => coordinator.RunAsync(stoppingToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // A host deadline never releases execution ownership while an effect still runs.
        if (ExecuteTask is not null) await ExecuteTask;
    }
}
