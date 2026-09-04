using Hangfire;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-0328 Hangfire entry point. Report-only until <c>WorktreeResidue:Execute</c> is flipped.
/// Automatic retry is off so a git/database outage produces one Failed dashboard entry.
/// </summary>
public sealed class WorktreeResidueJob
{
    private readonly WorktreeResidueSweepService _service;
    private readonly ILogger<WorktreeResidueJob> _logger;

    public WorktreeResidueJob(WorktreeResidueSweepService service, ILogger<WorktreeResidueJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task<WorktreeResidueResult> ExecuteAsync(CancellationToken cancellationToken)
    {
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
}
