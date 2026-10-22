using Hangfire;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-0328 Hangfire entry point. Report-only until <c>WorktreeResidue:Execute</c> is flipped.
/// Automatic retry is off so a git/database outage produces one Failed dashboard entry.
/// CARD-1446 D-1: the abandoned-landing reconciler runs first on every daily run, whatever
/// <c>Execute</c> says (a state correction, not a deletion); its failure never fails the sweep.
/// </summary>
public sealed class WorktreeResidueJob
{
    private readonly WorktreeResidueSweepService _service;
    private readonly ILogger<WorktreeResidueJob> _logger;
    private readonly AbandonedLandingReconciler? _reconciler;

    public WorktreeResidueJob(WorktreeResidueSweepService service, ILogger<WorktreeResidueJob> logger,
        AbandonedLandingReconciler? reconciler = null)
    {
        _service = service;
        _logger = logger;
        _reconciler = reconciler;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task<WorktreeResidueResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        await ReconcileAbandonedLandingsAsync(cancellationToken);
        try
        {
            var result = await _service.RunAsync(cancellationToken);
            _logger.LogInformation(
                "Worktree residue sweep completed in {DurationMs}ms Execute={Execute} Unknown={Unknown} Live={Live} Settling={Settling} Unmerged={Unmerged} Dirty={Dirty} Eligible={Eligible} Removed={Removed} Kept={Kept}",
                (int)result.Duration.TotalMilliseconds,
                result.Execute,
                result.Counts.Unknown,
                result.Counts.Live,
                result.Counts.Settling,
                result.Counts.Unmerged,
                result.Counts.Dirty,
                result.Counts.Eligible,
                result.Counts.Removed,
                result.Counts.Kept);

            foreach (var row in result.Kept)
            {
                _logger.LogWarning(
                    "Worktree residue kept label={Label} path={Path} branch={Branch} task={TaskId} detail={Detail}",
                    row.DisplayLabel,
                    row.Path,
                    row.Branch,
                    row.TaskId,
                    row.Detail);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worktree residue sweep canceled because the host is shutting down");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worktree residue sweep failed");
            throw;
        }
    }

    private async Task ReconcileAbandonedLandingsAsync(CancellationToken cancellationToken)
    {
        if (_reconciler is null) return;
        try
        {
            var refused = await _reconciler.RunAsync(cancellationToken);
            _logger.LogInformation("Abandoned landing reconciler refused {Refused} operation(s)", refused);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Abandoned landing reconciler failed; the residue sweep still runs");
        }
    }
}
