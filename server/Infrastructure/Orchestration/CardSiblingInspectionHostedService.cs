using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Hosting;

namespace Antiphon.Server.Infrastructure.Orchestration;

public sealed class CardSiblingInspectionHostedService(CardSiblingInspectionQueue queue) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        queue.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => queue.StopAsync(ct);
}
