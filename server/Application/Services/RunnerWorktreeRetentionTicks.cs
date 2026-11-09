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
///
/// CARD-1804 (review 437ceb6c A-1, A-2). The turn is also held across server processes: every tick takes a database-wide
/// advisory lock after the process turn (<see cref="RunnerWorktreeRetentionService.TryLockAcrossProcessesAsync"/>), so a
/// second server on the same database refuses or skips instead of censusing the same population. The execute route saves
/// its run before it answers; a background tick that fails, is cancelled by host stop or finds retention switched off
/// finishes that run with a Deferred row, and host stop (this hosted service) waits for the running tick.
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

    /// <summary>The last background execute tick; it never faults (a failure is logged and finishes its run).</summary>
    public Task Background { get; private set; } = Task.CompletedTask;

    internal Task EnterAsync(CancellationToken ct) => _turn.WaitAsync(ct);

    internal bool TryEnter() => _turn.Wait(0);

    internal void Exit() => _turn.Release();

    internal static ConflictException TickInProgress() =>
        new("A runner worktree retention tick is already running; read its run, or retry once it has finished.", TickInProgressCode);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// CARD-1804 (A-2). Host stop: <c>ApplicationStopping</c> has already cancelled the background tick, which finishes its
    /// run and releases the turn and the lock; wait for that, within the host's shutdown bound.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Background.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Runner worktree retention: host stop did not wait for the running execute tick to finish");
        }
    }

    /// <summary>
    /// CARD-1794 (4), CARD-1796. Starts the operator's one-shot Execute tick for one allow-listed runner and returns
    /// its run id without waiting for it. The kill switch and the allow-list are checked here (409
    /// <c>retention_disabled</c> / <c>runner_not_listed</c>) and again by the tick itself. CARD-1804: another server
    /// process's tick refuses it too (409 <c>tick_in_progress</c>), and its run is saved before it returns.
    /// </summary>
    public async Task<ExecuteStarted> StartExecuteAsync(string runnerId, CancellationToken ct)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            throw new ConflictException("Runner worktree retention is switched off (RunnerWorktreeRetention:Enabled).", "retention_disabled");
        if (string.IsNullOrWhiteSpace(runnerId) || !settings.Runners.Contains(runnerId, StringComparer.Ordinal))
            throw new ConflictException("RunnerWorktreeRetention:Runners does not name this runner.", "runner_not_listed");
        if (!TryEnter())
            throw TickInProgress();

        // The scope (its connection holds the tick lock) passes to the background tick once that starts.
        AsyncServiceScope? scope = _scopes.CreateAsyncScope();
        RunnerWorktreeRetentionService? service = null;
        try
        {
            service = scope.Value.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>();
            if (!await service.TryLockAcrossProcessesAsync())
                throw TickInProgress();
            var runId = await service.StartExecuteRunAsync(runnerId, settings, ct);
            var owned = scope.Value;
            // The request's execution context (its HttpContext and logging scopes) stays with the request.
            using (ExecutionContext.SuppressFlow())
                Background = Task.Run(() => RunExecuteAsync(owned, service, runnerId, runId));
            scope = null;
            return new ExecuteStarted(runnerId, runId);
        }
        finally
        {
            if (scope is { } unstarted)
            {
                if (service is not null)
                    await service.UnlockAcrossProcessesAsync();
                await unstarted.DisposeAsync();
                Exit();
            }
        }
    }

    private async Task RunExecuteAsync(AsyncServiceScope scope, RunnerWorktreeRetentionService service, string runnerId, Guid runId)
    {
        string? failure = null;
        try
        {
            foreach (var result in await service.RunEnteredAsync(_stopping, executeRunner: runnerId, executeRunId: runId))
                _logger.LogInformation(
                    "Runner worktree retention one-shot execute runner={RunnerId} run={RunId} examined={Examined} eligible={Eligible} sent={Sent} absentSent={AbsentSent} queued={Queued} stopped={Stopped}",
                    result.RunnerId, result.RunId, result.Examined, result.Eligible, result.Sent, result.AbsentSent, result.Queued,
                    result.Stopped ?? "-");
        }
        catch (Exception ex)
        {
            failure = ex is OperationCanceledException && _stopping.IsCancellationRequested ? "tick_cancelled" : "tick_failed";
            _logger.LogError(ex, "Runner worktree retention one-shot execute runner={RunnerId} run={RunId} failed", runnerId, runId);
        }
        finally
        {
            // CARD-1804 (A-2): the run never stays unfinished; a fresh scope, since the tick's context may be what failed.
            try
            {
                await using var stamp = _scopes.CreateAsyncScope();
                await stamp.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>().FinishUnfinishedRunAsync(runId, runnerId, failure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Runner worktree retention one-shot execute runner={RunnerId} run={RunId} could not be finished", runnerId, runId);
            }
            try
            {
                await service.UnlockAcrossProcessesAsync();
                await scope.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Runner worktree retention one-shot execute runner={RunnerId} run={RunId} scope release failed: {Reason}",
                    runnerId, runId, ex.Message);
            }
            Exit();
        }
    }
}
