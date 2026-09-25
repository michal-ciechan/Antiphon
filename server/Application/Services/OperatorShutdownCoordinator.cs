using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0716 D-2. Drains launches, records what a shutdown interrupts, then stops the host.</summary>
public sealed class OperatorShutdownCoordinator
{
    public OperatorShutdownCoordinator(
        ILaunchDrain drain,
        IHostApplicationLifetime lifetime,
        IOptions<OperatorSettings> settings,
        ILogger<OperatorShutdownCoordinator> logger,
        IServiceScopeFactory scopes,
        TimeProvider clock)
    {
        _ = drain;
        _ = lifetime;
        _ = settings;
        _ = logger;
        _ = scopes;
        _ = clock;
    }

    public Task StopAsync(string? reason, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> RecordInterruptedLaunchesAsync(
        AppDbContext db, string? reason, CancellationToken ct) =>
        throw new NotImplementedException();
}
