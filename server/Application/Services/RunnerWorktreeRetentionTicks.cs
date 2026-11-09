using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1796 (review e4276bff A-1, A-2). One runner worktree retention tick at a time in this process: the scheduled
/// job, the preview route and the operator's one-shot execute all take this turn, so two ticks never census the same
/// population at once (their cursor inserts collided on <c>IX_WorktreeResidueCandidateCursors_CandidateKey</c> and
/// aborted one tick after its sends). The scheduled job waits for the turn; the routes refuse 409 <c>tick_in_progress</c>
/// while it is held. The execute route runs its tick in the background on the host's lifetime, not the request's, and
/// answers with the run id at once, so a client or proxy timeout no longer cancels a paced tick mid-way.
/// </summary>
public sealed class RunnerWorktreeRetentionTicks : IHostedService
{
    public const string TickInProgressCode = "tick_in_progress";

    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<RunnerWorktreeRetentionSettings> _settings;
    private readonly ILogger<RunnerWorktreeRetentionTicks> _logger;
    private readonly CancellationToken _stopping;

    public RunnerWorktreeRetentionTicks(IServiceScopeFactory scopes, IOptionsMonitor<RunnerWorktreeRetentionSettings> settings,
        ILogger<RunnerWorktreeRetentionTicks> logger, IHostApplicationLifetime? lifetime = null)
    {
        _scopes = scopes;
        _settings = settings;
        _logger = logger;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
    }

    /// <summary>The execute route's reply: the run to read (<c>GET /api/agent-tasks/worktree-residue/runs/{runId}</c>).</summary>
    public sealed record ExecuteStarted(string RunnerId, Guid RunId);

    /// <summary>The last background execute tick; it never faults (a failure is logged).</summary>
    public Task Background { get; private set; } = Task.CompletedTask;

    internal Task EnterAsync(CancellationToken ct) => _turn.WaitAsync(ct);

    internal bool TryEnter() => _turn.Wait(0);

    internal void Exit() => _turn.Release();

    internal static ConflictException TickInProgress() =>
        new("A runner worktree retention tick is already running; read its run, or retry once it has finished.", TickInProgressCode);

    /// <summary>
    /// CARD-1794 (4), CARD-1796. Starts the operator's one-shot Execute tick for one allow-listed runner and returns
    /// its run id without waiting for it. The kill switch and the allow-list are checked here (409
    /// <c>retention_disabled</c> / <c>runner_not_listed</c>) and again by the tick itself.
    /// </summary>
    // RED (CARD-1804): the async route surface over the old body; the fix commit replaces it.
    public Task<ExecuteStarted> StartExecuteAsync(string runnerId, CancellationToken ct = default) => Task.FromResult(StartExecute(runnerId));

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // RED (CARD-1804): host stop does not await the background tick yet.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ExecuteStarted StartExecute(string runnerId)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            throw new ConflictException("Runner worktree retention is switched off (RunnerWorktreeRetention:Enabled).", "retention_disabled");
        if (string.IsNullOrWhiteSpace(runnerId) || !settings.Runners.Contains(runnerId, StringComparer.Ordinal))
            throw new ConflictException("RunnerWorktreeRetention:Runners does not name this runner.", "runner_not_listed");
        if (!TryEnter())
            throw TickInProgress();

        var runId = Guid.NewGuid();
        try
        {
            // The request's execution context (its HttpContext and logging scopes) stays with the request.
            using (ExecutionContext.SuppressFlow())
                Background = Task.Run(() => RunExecuteAsync(runnerId, runId));
        }
        catch
        {
            Exit();
            throw;
        }
        return new ExecuteStarted(runnerId, runId);
    }

    private async Task RunExecuteAsync(string runnerId, Guid runId)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>();
            foreach (var result in await service.RunEnteredAsync(_stopping, executeRunner: runnerId, executeRunId: runId))
                _logger.LogInformation(
                    "Runner worktree retention one-shot execute runner={RunnerId} run={RunId} examined={Examined} eligible={Eligible} sent={Sent} absentSent={AbsentSent} queued={Queued} stopped={Stopped}",
                    result.RunnerId, result.RunId, result.Examined, result.Eligible, result.Sent, result.AbsentSent, result.Queued,
                    result.Stopped ?? "-");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runner worktree retention one-shot execute runner={RunnerId} run={RunId} failed", runnerId, runId);
        }
        finally
        {
            Exit();
        }
    }
}
