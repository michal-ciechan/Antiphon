using Hangfire;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-0298 Hangfire entry point. Report-only: a candidate is a successful dry run, not a failed job.
/// Automatic retry is off so a prerequisite outage produces one Failed dashboard entry.
/// </summary>
public sealed class ZombieCensusJob
{
    public const int MaxLoggedCandidates = 20;

    private readonly ZombieCensusService _service;
    private readonly ILogger<ZombieCensusJob> _logger;

    public ZombieCensusJob(ZombieCensusService service, ILogger<ZombieCensusJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task<ZombieCensusResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RunAsync(cancellationToken);
            _logger.LogInformation(
                "Zombie census completed in {DurationMs}ms PoolExpired={PoolExpired} ReconcilerOwned={ReconcilerOwned} EndedButAlive={EndedButAlive} Unclaimed={Unclaimed} Ignored={Ignored} Unidentified={Unidentified} Candidates={Candidates}",
                (int)result.Duration.TotalMilliseconds,
                result.Counts.PoolExpired,
                result.Counts.ReconcilerOwned,
                result.Counts.EndedButAlive,
                result.Counts.Unclaimed,
                result.Counts.Ignored,
                result.Counts.Unidentified,
                result.Counts.Candidates);

            var logged = 0;
            foreach (var row in result.Candidates)
            {
                if (logged >= MaxLoggedCandidates)
                    break;
                _logger.LogWarning(
                    "Zombie census candidate pid={Pid} identity={IdentityMethod} session={SessionId} class={Class} failedRules={FailedRules} runnerClaimed={RunnerClaimed} agent={Agent}",
                    row.Pid,
                    row.IdentityMethod,
                    row.SessionId,
                    row.Class,
                    string.Join("; ", row.FailedRules),
                    row.RunnerClaimed,
                    row.AgentName);
                logged++;
            }

            if (result.Candidates.Count > MaxLoggedCandidates)
            {
                _logger.LogWarning(
                    "Zombie census omitted {Omitted} further candidate row(s) from the log (cap {Cap})",
                    result.Candidates.Count - MaxLoggedCandidates,
                    MaxLoggedCandidates);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Zombie census canceled because the host is shutting down");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zombie census failed");
            throw;
        }
    }
}
