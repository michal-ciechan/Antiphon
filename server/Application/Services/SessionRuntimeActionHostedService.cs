using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class SessionRuntimeActionHostedService(SessionRuntimeActionCoordinator coordinator) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => coordinator.RunAsync(stoppingToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (ExecuteTask is not { } execution) return;

        // Keep execution ownership until any in-flight effect finishes, even past the host deadline.
        try
        {
            await execution;
        }
        catch (OperationCanceledException) when (execution.IsCanceled)
        {
            // A queued worker can be canceled before the coordinator starts.
        }
    }
}
