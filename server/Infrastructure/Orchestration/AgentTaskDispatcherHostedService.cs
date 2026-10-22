using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Orchestration;

/// <summary>Owns independent serial admission and maintenance loops.</summary>
public sealed class AgentTaskDispatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly ILogger<AgentTaskDispatcherHostedService> _logger;
    private readonly RepositoryLeaseWaiters? _waiters;
    private readonly TimeProvider _clock;
    private readonly SweepInFlightState? _sweeps;

    public AgentTaskDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        ILogger<AgentTaskDispatcherHostedService> logger, RepositoryLeaseWaiters? waiters = null,
        TimeProvider? timeProvider = null, SweepInFlightState? sweepInFlight = null)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
        _waiters = waiters;
        _clock = timeProvider ?? TimeProvider.System;
        _sweeps = sweepInFlight;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Delegation is disabled; the task dispatcher will not run.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollIntervalSeconds));
        try
        {
            await Task.WhenAll(AdmissionLoopAsync(interval, stoppingToken), MaintenanceLoopAsync(interval, stoppingToken));
        }
        finally
        {
            // Both loops have joined: maintenance can no longer register an owned sweep.
            // An expired StopAsync deadline does not make its still-running scope disposable.
            if (_sweeps is not null) await _sweeps.WhenAllReleasedAsync();
        }
    }

    private async Task AdmissionLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        var lastFull = _clock.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var remaining = interval - _clock.GetElapsedTime(lastFull);
                if (remaining > TimeSpan.Zero)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var timer = Task.Delay(remaining, _clock, wait.Token);
                    var ready = _waiters?.WaitForReadyAsync(wait.Token) ?? timer;
                    await Task.WhenAny(timer, ready);
                    await wait.CancelAsync();
                    try { await Task.WhenAll(timer, ready); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                }
                ct.ThrowIfCancellationRequested();
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                // Ready signals coalesce in RepositoryLeaseWaiters. They never reset this deadline,
                // and a due full pass wins even when timer and signal become ready together.
                if (_clock.GetElapsedTime(lastFull) >= interval)
                {
                    lastFull = _clock.GetTimestamp();
                    LogAdmission(await dispatcher.DispatchQueuedAsync(ct));
                }
                else
                {
                    await dispatcher.DispatchReadyWaitersAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Delegation admission pass failed"); }
        }
    }

    private async Task MaintenanceLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _clock, ct);
                await using var scope = _scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().RunMaintenanceAsync(ct);
                if (result.SweepFailures > 0)
                    _logger.LogWarning("Delegation maintenance ran DEGRADED: {SweepFailures} sweeps failed (see the errors above)",
                        result.SweepFailures);
                if (result.ResumedRoutingBlocked > 0)
                    _logger.LogDebug("Delegation maintenance resumed {ResumedRoutingBlocked} routing-blocked tasks",
                        result.ResumedRoutingBlocked);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Delegation maintenance pass failed"); }
        }
    }

    private void LogAdmission(AgentTaskDispatcher.TickResult result)
    {
        if (result.Dispatched > 0 || result.Failures > 0 || result.SkippedCapacityWait > 0
            || result.HeldOnLease > 0 || result.HeldOnRunner > 0)
            _logger.LogDebug(
                "Delegation admission: {Dispatched} dispatched, {Concurrency} held on concurrency, "
                + "{Scope} held on scope, {CapacityWait} held on capacity wait, {Lease} held on lease, "
                + "{Runner} held on runner, {Failures} failed",
                result.Dispatched, result.SkippedConcurrency, result.SkippedScope, result.SkippedCapacityWait,
                result.HeldOnLease, result.HeldOnRunner, result.Failures);
    }
}
