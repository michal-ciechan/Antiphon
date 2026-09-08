using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Retired by CARD-0448 F2. Historical landing prose is not publication or verification
/// evidence. Landing writes its own stage rows; no historical event, including AlreadyPresent,
/// may synthesize replacement green rows here. Existing history remains for audit.
/// </summary>
public sealed class StageOutcomeBackfillService : BackgroundService
{
    public StageOutcomeBackfillService(IServiceScopeFactory scopeFactory, ILogger<StageOutcomeBackfillService> logger) { }

    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;

    internal static Task<int> RunAsync(AppDbContext db, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }
}
