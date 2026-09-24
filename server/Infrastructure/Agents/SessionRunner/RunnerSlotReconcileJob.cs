using Hangfire;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0653 Hangfire entry point: finish pending slot-release intents by auditing only. Triggered
/// once at startup and then on <c>PhoneHomeRunner:SlotReconcileCron</c>, so an intent whose
/// in-request reconcile also failed is not left pending until the next failed release.
/// </summary>
public sealed class RunnerSlotReconcileJob
{
    public const string RecurringJobId = "antiphon:runner-slot-reconcile";

    private readonly PhoneHomeRunnerDirectory _directory;
    private readonly AppDbContext _db;
    private readonly ILogger<RunnerSlotReconcileJob> _logger;

    public RunnerSlotReconcileJob(
        PhoneHomeRunnerDirectory directory, AppDbContext db, ILogger<RunnerSlotReconcileJob> logger)
    {
        _directory = directory;
        _db = db;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var finished = await RunnerSlotService.ReconcilePendingReleasesAsync(_directory, _db, cancellationToken);
        if (finished.Count > 0)
            _logger.LogInformation(
                "Runner slot reconcile audited {Count} pending release(s): {SessionIds}",
                finished.Count,
                string.Join(",", finished));
        return finished.Count;
    }
}
