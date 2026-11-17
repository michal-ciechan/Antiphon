using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// After a phone-home socket connects, catch up transcripts for that owner, then mark the
/// connection dispatch-eligible and ingest live events with owner/epoch provenance.
/// </summary>
public sealed class PhoneHomeRecoveryPump : BackgroundService
{
    private readonly PhoneHomeRunnerDirectory _directory;
    private readonly PhoneHomeRunnerSettings _settings;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PhoneHomeRecoveryPump> _logger;
    private readonly IBackgroundJobClient? _jobs;
    private readonly ConcurrentDictionary<string, Cycle> _cycles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _catchUpHolds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _transcriptFailures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _unverifiedSessions = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<PhoneHomeLiveConnection, ConnectionState> _states = new();

    // CARD-1219 D-2: one replay guard per runner id (Replayed_frames_across_a_reconnect_record_once).
    private readonly ConcurrentDictionary<string, TranscriptIncidentReplayGuard> _replayGuards = new(StringComparer.Ordinal);

    public PhoneHomeRecoveryPump(
        PhoneHomeRunnerDirectory directory,
        IOptions<PhoneHomeRunnerSettings> settings,
        IServiceScopeFactory scopes,
        ILogger<PhoneHomeRecoveryPump> logger,
        // CARD-0679 D-7: absent (tests, a host without Hangfire), deferred kills wait for the cron.
        IBackgroundJobClient? jobs = null)
    {
        _jobs = jobs;
        _directory = directory;
        _settings = settings.Value;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Test seam: when set, catch-up for every runner waits until the TCS completes.</summary>
    internal TaskCompletionSource? CatchUpHold { get; set; }

    /// <summary>Hold catch-up for one runner. Another runner's cycle is not waited on.</summary>
    internal void HoldCatchUp(string runnerId, TaskCompletionSource hold) => _catchUpHolds[runnerId] = hold;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
            return;

        // CARD-1964: either setting at zero or less leaves the watchdog loop stopped; the pump runs on.
        var watchdog = _settings.RecoveryWatchdogIntervals > 0 && _settings.RecoveryWatchdogPollSeconds > 0
            ? WatchdogLoopAsync(stoppingToken)
            : null;
        try
        {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Phone-home recovery pump cycle failed");
            }

            await Task.Delay(50, stoppingToken);
        }
        }
        finally
        {
            foreach (var cycle in _cycles.Values)
                try { cycle.PumpStop?.Cancel(); } catch (ObjectDisposedException) { }
            foreach (var cycle in _cycles.Values)
            {
                await JoinPumpAsync(cycle.Pump);
                await JoinPumpAsync(cycle.Refresh);
                await JoinPumpAsync(cycle.Teardown);
            }
            await JoinPumpAsync(watchdog);
        }
    }

    private async Task JoinPumpAsync(Task? task)
    {
        if (task is null) return;
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Phone-home owned observation task ended with a fault"); }
    }

    /// <summary>How many owned sessions' transcripts failed in the last catch-up that listed.</summary>
    internal int LastCatchUpTranscriptFailures =>
        _transcriptFailures.Count == 0 ? 0 : _transcriptFailures.Values.Sum();

    /// <summary>CARD-1964 D-3: sessions the last catch-up that listed skipped on a budget (counted unverified).</summary>
    internal int LastCatchUpUnverifiedSessions =>
        _unverifiedSessions.Count == 0 ? 0 : _unverifiedSessions.Values.Sum();

    /// <summary>
    /// One recovery step for the current live connection. Returns true when this cycle marked it
    /// dispatch-eligible.
    /// </summary>
    internal async Task<bool> RunCycleAsync(CancellationToken ct)
    {
        var ids = _directory.RemoteRunnerIds;
        if (ids.Count == 0)
            return false;
        if (ids.Count == 1)
            return await RunOneAsync(ids[0], ct);
        var results = await Task.WhenAll(ids.Select(id => RunOneAsync(id, ct)));
        return results.Any(static ready => ready);
    }

    private async Task<bool> RunOneAsync(string runnerId, CancellationToken ct)
    {
        var cycle = _cycles.GetOrAdd(runnerId, static _ => new Cycle());
        var live = _directory.SnapshotLive(runnerId);
        if (cycle.Teardown is { } teardown)
        {
            if (!teardown.IsCompleted) return false;
            await teardown;
            cycle.Teardown = null;
            cycle.Recovered = null;
        }
        if (cycle.Recovered is { } previous && (!ReferenceEquals(previous, live) || !previous.SocketOpen))
        {
            try { cycle.PumpStop?.Cancel(); } catch (ObjectDisposedException) { }
            cycle.Teardown = JoinPumpAsync(cycle.Pump);
            cycle.NextCatchUp = null;
            if (!cycle.Teardown.IsCompleted)
                _logger.LogWarning("Phone-home observer teardown is still running for {RunnerId} epoch {Epoch}; replacement waits", runnerId, previous.Epoch);
            return false;
        }
        if (live is null || !live.SocketOpen)
        {
            cycle.Recovered = null;
            cycle.NextCatchUp = null;
            return false;
        }

        // CARD-1964 watchdog: a pending restart is applied here, before the lease branch, so an
        // eligible runner's stuck refresh is detached too. The replacement attempt starts this turn.
        var state = StateFor(live);
        ApplyRestartIfRequested(live, state, cycle);

        var recoveringLease = ReferenceEquals(cycle.Recovered, live);
        if (recoveringLease)
        {
            // CARD-0679 D-2: nothing else would ever release this connection's later events. A
            // pump that ended because the connection was disposed drained a completed Events
            // channel; that ending is not an error, and the next cycle sees the closed socket.
            if (cycle.Pump is { IsCompleted: true } ended
                && !ct.IsCancellationRequested
                && !live.Events.Completion.IsCompleted)
            {
                _logger.LogError(
                    ended.Exception?.GetBaseException(),
                    "Phone-home event pump ended while the connection is live (runner {RunnerId} epoch {Epoch}, "
                    + "{PumpStatus}); restarting it with {PendingEvents} events pending",
                    live.RunnerId, live.Epoch, ended.Status, live.PendingEvents);
                StartPump(live, cycle, ct);
            }

            if (live.Diagnostics.TakeSlowWarning() is { } slow)
                _logger.LogWarning(
                    "Phone-home slow observer runner={RunnerId} epoch={Epoch} worker={Worker} session={SessionId} event={EventName} phase={Phase} elapsedMs={ElapsedMs} oldestMs={OldestPendingMs} diagnostics={Diagnostics}",
                    runnerId, live.Epoch, slow.Worker, slow.SessionId, slow.EventName, slow.Phase, slow.ElapsedMs,
                    live.AccountingSnapshot().OldestPendingMs,
                    System.Text.Json.JsonSerializer.Serialize(live.Diagnostics.Snapshot(), PhoneHomeFraming.Json));
            if (live.DispatchEligible)
            {
                RefreshInventoryIfDue(live, cycle, ct);
                return false;
            }

            // A lease expiry clears directory eligibility without replacing the connection.
            // Renewed heartbeats must go through catch-up again, while its existing observer
            // group continues draining events. Never start a second group for that connection.
            lock (state.WatchLock)
                state.LeaseLostAt ??= live.Clock.GetUtcNow();
            if (_directory.LeaseRecoveryRefusal(live) is { } refusal)
            {
                NoteRefusal(live, state, "before_catch_up", refusal);
                return false;
            }
        }

        // CARD-0633 D-2: a failed catch-up is retried after CatchUpRetrySeconds on the connection's
        // own clock, not on the next 50 ms turn of the loop.
        if (cycle.NextCatchUp is { } next && ReferenceEquals(next.Live, live) && live.Clock.GetUtcNow() < next.At)
            return false;

        // Dispatch-eligible only after the owner inventory was actually read. A List that failed or
        // timed out used to be swallowed here and the runner marked recovered anyway (CARD-0629).
        // CARD-1964 D-2: a lease re-recovery needs only that List; the observers never stopped.
        bool caughtUp;
        var restart = BeginAttempt(live, state);
        using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, restart.Token))
        {
            try
            {
                caughtUp = await CatchUpAsync(live, state, ct, attempt.Token, listOnly: recoveringLease);
            }
            finally
            {
                EndAttempt(state, restart);
            }
        }

        if (!caughtUp)
        {
            cycle.NextCatchUp = (live, live.Clock.GetUtcNow() + TimeSpan.FromSeconds(_settings.CatchUpRetrySeconds));
            return false;
        }

        if (recoveringLease)
        {
            // CARD-1964 D-9: a refusal after the List keeps its next-turn retry; it is only logged.
            if (!_directory.TryRecoverLease(live, out var refusal))
            {
                NoteRefusal(live, state, "after_catch_up", refusal ?? "replaced");
                return false;
            }
            LogLeaseRecovered(live, state);
        }

        cycle.NextCatchUp = null;
        cycle.NextRefresh = live.Clock.GetUtcNow() + TimeSpan.FromSeconds(_settings.InventoryRefreshSeconds);
        if (!recoveringLease)
        {
            _directory.MarkRecovered(live);
            cycle.Recovered = live;
            StartPump(live, cycle, ct);
        }
        EnqueueSlotReconcile(live);
        return true;
    }

    /// <summary>
    /// CARD-0679 D-7: a deferred generation kill lands seconds after the runner is back, not at the
    /// next <c>SlotReconcileCron</c> tick. Best effort: the cron still carries the intent.
    /// </summary>
    private void EnqueueSlotReconcile(PhoneHomeLiveConnection live)
    {
        if (_jobs is null)
            return;
        try
        {
            _jobs.Enqueue<RunnerSlotReconcileJob>(job => job.ExecuteAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Enqueueing the runner slot reconcile after phone-home recovery failed (runner {RunnerId} epoch {Epoch})",
                live.RunnerId, live.Epoch);
        }
    }

    /// <summary>
    /// CARD-0679 D-10: re-read the runner's List every <see cref="PhoneHomeRunnerSettings.InventoryRefreshSeconds"/>
    /// while <paramref name="live"/> stays recovered, so the cached inventory also learns what no
    /// ack or event told it. Off the cycle: a silent runner must not hold up pump supervision.
    /// </summary>
    private void RefreshInventoryIfDue(PhoneHomeLiveConnection live, Cycle cycle, CancellationToken ct)
    {
        if (_settings.InventoryRefreshSeconds <= 0
            || cycle.Refresh is { IsCompleted: false }
            || live.Clock.GetUtcNow() < cycle.NextRefresh)
            return;
        cycle.NextRefresh = live.Clock.GetUtcNow() + TimeSpan.FromSeconds(_settings.InventoryRefreshSeconds);
        cycle.Refresh = RefreshInventoryAsync(live, ct);
    }

    private async Task RefreshInventoryAsync(PhoneHomeLiveConnection live, CancellationToken ct)
    {
        var state = StateFor(live);
        lock (state.WatchLock)
        {
            if (state.AttemptStartedAt is null)
                state.AttemptStep = Steps.Refresh;
        }

        // CARD-1964 D-1: the refresh List has the same budget as the catch-up List.
        using var step = new Budget(ct, _settings.CatchUpListTimeoutSeconds, live.Clock);
        try
        {
            await BoundedAsync(ReadInventoryAsync(live, state, step.Token), step.Token);
            var ended = Interlocked.Exchange(ref state.RefreshFailures, 0);
            if (ended >= StaleAfterRefreshes)
                _logger.LogInformation(
                    "Phone-home inventory refresh for runner {RunnerId} epoch {Epoch} succeeded after "
                    + "{ConsecutiveFailures} failed refreshes", live.RunnerId, live.Epoch, ended);
        }
        // CARD-1964 (review 5f96c990 A3): the filter reads the stopping token, not the step token,
        // so a budget cancel reaches the Warning below as refresh_list_budget.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The last inventory stands, but only until its entries age past InventoryMaxAge; a
            // closed connection vouches for nothing anyway, and the next refresh or the next
            // connection's catch-up reads it again.
            var failures = Interlocked.Increment(ref state.RefreshFailures);
            _logger.LogWarning(
                ex, "Phone-home inventory refresh List for runner {RunnerId} epoch {Epoch} failed ({Code})",
                live.RunnerId, live.Epoch,
                ex is OperationCanceledException && step.Expired ? "refresh_list_budget" : ProblemCode(ex));
            if (failures == StaleAfterRefreshes)
                _logger.LogWarning(
                    "Phone-home inventory refreshes for runner {RunnerId} epoch {Epoch} failed {ConsecutiveFailures} "
                    + "times in a row while the connection stays up; cached sessions no List, launch ack or event "
                    + "confirmed within {MaxAgeSeconds}s no longer count as live",
                    live.RunnerId, live.Epoch, failures, _settings.InventoryMaxAge?.TotalSeconds);
        }
        finally
        {
            lock (state.WatchLock)
            {
                if (state.AttemptStep == Steps.Refresh)
                    state.AttemptStep = Steps.None;
            }
        }
    }

    // Review 57fa2e6a: the Warning threshold is the stale bound; with the bound off, three in a row.
    private int StaleAfterRefreshes =>
        _settings.InventoryStaleAfterRefreshes > 0 ? _settings.InventoryStaleAfterRefreshes : 3;

    /// <summary>
    /// CARD-0679 D-10: one List, which replaces <paramref name="live"/>'s cached inventory and
    /// records its latency as <see cref="PhoneHomeLiveConnection.LastCatchUpMs"/> (D-1).
    /// CARD-1964 (review 5f96c990 A2): the completion is published under the watchdog's lock
    /// before this returns, so a poll can never fire on a window start it read before it.
    /// </summary>
    private static async Task<IReadOnlyList<SessionRunnerSessionDto>> ReadInventoryAsync(
        PhoneHomeLiveConnection live, ConnectionState state, CancellationToken ct)
    {
        var stamp = live.BeginInventoryRead();
        var started = live.Clock.GetTimestamp();
        var sessions = await new PhoneHomeRunnerClient(live).ListAsync(ct);
        live.LastCatchUpMs = (long)live.Clock.GetElapsedTime(started).TotalMilliseconds;
        live.ReplaceKnownLiveSessions(
            sessions.Where(s => s.Status is "Running" or "Starting").Select(s => (s.SessionId, s.AcceptedStartedAt)),
            stamp);
        lock (state.WatchLock)
            state.LastListAt = live.Clock.GetUtcNow();
        return sessions;
    }

    private void StartPump(PhoneHomeLiveConnection live, Cycle cycle, CancellationToken ct)
    {
        if (cycle.Pump is { IsCompleted: false })
            throw new InvalidOperationException("The previous phone-home observer group has not joined.");

        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cycle.PumpStop = stop;
        cycle.Pump = RunPumpAsync(live, stop);
    }

    private async Task RunPumpAsync(PhoneHomeLiveConnection live, CancellationTokenSource stop)
    {
        try
        {
            await Task.Run(() => PumpEventsAsync(live, stop.Token), CancellationToken.None);
        }
        finally
        {
            stop.Dispose();
        }
    }

    /// <summary>Test seam (CARD-0679 V-9): end the current pump task as if it had died.</summary>
    internal async Task EndPumpForTest()
    {
        foreach (var cycle in _cycles.Values)
        {
            if (cycle.Pump is not { } pump)
                continue;
            try
            {
                cycle.PumpStop?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already ended
            }

            try
            {
                await pump;
            }
            catch (OperationCanceledException)
            {
                // the ending this seam asked for
            }
        }
    }

    /// <summary>CARD-0679 D-2: events on <paramref name="live"/> whose processing threw.</summary>
    internal int EventFailures(PhoneHomeLiveConnection live) => Volatile.Read(ref StateFor(live).EventFailures);

    /// <summary>
    /// CARD-1964 D-1/D-3: the List, then (unless <paramref name="listOnly"/>) each owned session's
    /// transcript. <paramref name="attempt"/> is the stopping token plus the watchdog's cancel;
    /// every step await below returns at its budget even when the awaited code ignores its token.
    /// True only after the List completed within this attempt.
    /// </summary>
    private async Task<bool> CatchUpAsync(PhoneHomeLiveConnection live, ConnectionState state,
        CancellationToken stopping, CancellationToken attempt, bool listOnly)
    {
        var hold = _catchUpHolds.TryGetValue(live.RunnerId, out var specific) ? specific : CatchUpHold;
        if (hold is not null)
        {
            // D-10: a test hold waits on the attempt token only, outside every budget.
            SetStep(state, Steps.Hold);
            try
            {
                await hold.Task.WaitAsync(attempt);
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                return false;
            }
        }

        using var budget = new Budget(attempt, _settings.CatchUpTimeoutSeconds, live.Clock);
        var client = new PhoneHomeRunnerClient(live);
        IReadOnlyList<SessionRunnerSessionDto> sessions;
        SetStep(state, Steps.List);
        using (var step = new Budget(budget.Token, _settings.CatchUpListTimeoutSeconds, live.Clock))
        {
            try
            {
                sessions = await BoundedAsync(ReadInventoryAsync(live, state, step.Token), step.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stopping.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex, "Phone-home catch-up List for runner {RunnerId} failed ({Code}); the runner stays "
                    + "dispatch-ineligible and catch-up retries in {RetrySeconds}s",
                    live.RunnerId, ListFailureCode(ex, attempt, budget, step), _settings.CatchUpRetrySeconds);
                return false;
            }
        }

        _transcriptFailures[live.RunnerId] = 0;
        _unverifiedSessions[live.RunnerId] = 0;
        if (listOnly || sessions.Count == 0)
            return true;

        // One unreadable session is a Warning, not a fence: List is the owner inventory, and
        // without it owner matching is blind, but a single dead transcript must not keep the whole
        // runner ineligible forever. CARD-1964 D-3: neither does a session that runs out of its budget.
        var failures = 0;
        var unverified = 0;
        var completed = 0;
        await using var scope = _scopes.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();
        for (var index = 0; index < sessions.Count; index++)
        {
            stopping.ThrowIfCancellationRequested();
            if (budget.Token.IsCancellationRequested)
            {
                LogBudgetExhausted(live, budget, completed, sessions.Count, unverified + sessions.Count - index);
                unverified += sessions.Count - index;
                break;
            }

            var session = sessions[index];
            using var step = new Budget(budget.Token, _settings.CatchUpSessionTimeoutSeconds, live.Clock);
            var stepName = Steps.Owner;
            try
            {
                SetStep(state, stepName);
                if (!await BoundedAsync(OwnerMatchesAsync(live, session.SessionId, step.Token), step.Token))
                {
                    completed++;
                    continue;
                }

                try
                {
                    stepName = Steps.Fetch;
                    SetStep(state, stepName);
                    var transcript = await BoundedAsync(client.GetTranscriptAsync(session.SessionId, step.Token), step.Token);
                    stepName = Steps.Persist;
                    SetStep(state, stepName);
                    await BoundedAsync(runtime.PersistTranscriptAsync(session.SessionId, transcript.Entries, step.Token), step.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    _logger.LogWarning(
                        ex, "Phone-home catch-up transcript for session {SessionId} on runner {RunnerId} failed ({Code})",
                        session.SessionId, live.RunnerId, ProblemCode(ex));
                }

                completed++;
            }
            catch (OperationCanceledException) when (step.Expired && !budget.Token.IsCancellationRequested)
            {
                // This session's own budget ran out; the attempt and the whole budget are both
                // inside budget.Token, so neither is the cancel here.
                completed++;
                unverified++;
                _logger.LogWarning(
                    "Phone-home catch-up step {Step} for session {SessionId} on runner {RunnerId} epoch {Epoch} "
                    + "exceeded its {BudgetSeconds}s budget; the session is skipped and counted unverified "
                    + "({Unverified} so far in this catch-up)",
                    stepName, session.SessionId, live.RunnerId, live.Epoch, step.Seconds, unverified);
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                // The whole budget ran out (or the watchdog cancelled) inside this session: it and
                // every later session are skipped. The List completed, so eligibility rests on it.
                completed++;
                unverified += sessions.Count - index;
                LogBudgetExhausted(live, budget, completed, sessions.Count, unverified);
                break;
            }
        }

        _transcriptFailures[live.RunnerId] = failures;
        _unverifiedSessions[live.RunnerId] = unverified;
        return true;
    }

    private void LogBudgetExhausted(PhoneHomeLiveConnection live, Budget budget, int completed, int listed, int unverified) =>
        _logger.LogWarning(
            "Phone-home catch-up for runner {RunnerId} epoch {Epoch} exhausted its {BudgetSeconds}s budget after "
            + "{Completed} of {Listed} listed sessions; {Unverified} sessions counted unverified; eligibility rests "
            + "on the completed List",
            live.RunnerId, live.Epoch, budget.Expired ? budget.Seconds : (int)OverrunLimitSeconds, completed, listed, unverified);

    // CARD-1964: which bound ended a catch-up List. The stopping token never reaches here.
    private static string ListFailureCode(Exception ex, CancellationToken attempt, Budget budget, Budget step) =>
        ex is not OperationCanceledException ? ProblemCode(ex)
        : budget.Expired ? "catch_up_budget"
        : step.Expired ? "catch_up_list_budget"
        : attempt.IsCancellationRequested ? "watchdog_restart"
        : ProblemCode(ex);

    private static string ProblemCode(Exception ex) => ex switch
    {
        PhoneHomeTransportException transport => transport.Code,
        HttpException { Code: { } code } => code,
        _ => ex.GetType().Name,
    };

    /// <summary>
    /// CARD-1964 D-1: wait for <paramref name="task"/> until <paramref name="token"/> cancels, even
    /// when the awaited code ignores it. An abandoned task's fault is observed, never rethrown.
    /// </summary>
    private static async Task<T> BoundedAsync<T>(Task<T> task, CancellationToken token)
    {
        try
        {
            return await task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (!task.IsCompleted)
        {
            Abandon(task);
            throw;
        }
    }

    private static async Task BoundedAsync(Task task, CancellationToken token)
    {
        try
        {
            await task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (!task.IsCompleted)
        {
            Abandon(task);
            throw;
        }
    }

    private static void Abandon(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    // ---- CARD-1964 watchdog -------------------------------------------------------------------

    private double RefreshBasisSeconds => _settings.InventoryRefreshSeconds > 0 ? _settings.InventoryRefreshSeconds : 30;

    private double ListBoundSeconds => _settings.CatchUpListTimeoutSeconds > 0
        ? _settings.CatchUpListTimeoutSeconds
        : PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.List).TotalSeconds;

    private double CatchUpBoundSeconds => _settings.CatchUpTimeoutSeconds > 0 ? _settings.CatchUpTimeoutSeconds : 300;

    /// <summary>
    /// The no-List window. Its floor keeps a small interval count from opening a window shorter
    /// than one List attempt plus its retry, so a replacement attempt keeps its List budget (P-1).
    /// </summary>
    internal double WatchdogWindowSeconds => Math.Max(
        _settings.RecoveryWatchdogIntervals * RefreshBasisSeconds,
        ListBoundSeconds + _settings.CatchUpRetrySeconds + Math.Max(0, _settings.RecoveryWatchdogPollSeconds) + 30);

    /// <summary>An attempt running longer than this is cancelled once. Always above the List bound.</summary>
    internal double OverrunLimitSeconds => CatchUpBoundSeconds + ListBoundSeconds;

    private async Task WatchdogLoopAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(_settings.RecoveryWatchdogPollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                RunWatchdogOnce();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Phone-home recovery watchdog poll failed");
            }
        }
    }

    /// <summary>
    /// CARD-1964: one watchdog poll. Per runner it fires at most one trigger: <c>attempt_overrun</c>
    /// once per attempt, or <c>no_list</c> once per window. A fire logs one Error, cancels the
    /// attempt in flight and asks the loop for a restart; while that restart is pending the runner
    /// is left alone. It never touches eligibility, the socket or the observers.
    /// </summary>
    internal void RunWatchdogOnce()
    {
        if (_settings.RecoveryWatchdogIntervals <= 0)
            return;
        foreach (var runnerId in _directory.RemoteRunnerIds)
        {
            var live = _directory.SnapshotLive(runnerId);
            if (live is null || !live.SocketOpen)
                continue;
            var state = StateFor(live);
            // Review 5f96c990 A2: the decision and the cancel are one critical section with the
            // List completion's write of LastListAt.
            lock (state.WatchLock)
            {
                if (state.RestartRequested)
                    continue;
                if (state.AttemptStartedAt is null && live.DispatchEligible && _settings.InventoryRefreshSeconds <= 0)
                    continue;

                var now = live.Clock.GetUtcNow();
                string trigger;
                double limit;
                if (state.AttemptStartedAt is { } started
                    && (now - started).TotalSeconds > OverrunLimitSeconds
                    && state.OverrunFiredAttemptId != state.AttemptId)
                {
                    trigger = "attempt_overrun";
                    limit = OverrunLimitSeconds;
                    state.OverrunFiredAttemptId = state.AttemptId;
                }
                else
                {
                    var window = WindowStart(state);
                    // An attempt whose List already completed is bounded by its catch-up budget and
                    // by attempt_overrun; it has not gone without a List.
                    var listedInAttempt = state.AttemptStartedAt is { } attemptStart
                        && state.LastListAt is { } listed && listed >= attemptStart;
                    if (listedInAttempt
                        || (now - window).TotalSeconds < WatchdogWindowSeconds
                        || state.FiredWindowStart == window)
                        continue;
                    trigger = "no_list";
                    limit = WatchdogWindowSeconds;
                    state.FiredWindowStart = window;
                }

                var lastList = state.LastListAt is { } list && list > state.FirstSeenAt ? list : state.FirstSeenAt;
                _logger.LogError(
                    "Phone-home recovery watchdog: runner {RunnerId} epoch {Epoch} {Trigger} (no List for "
                    + "{SinceListSeconds}s, attempt step {AttemptStep} running {AttemptSeconds}s, limit {LimitSeconds}s, "
                    + "eligible {DispatchEligible}, last refusal {LastRefusal}, pendingEvents {PendingEvents}); "
                    + "restarting its recovery cycle on the same connection",
                    runnerId, live.Epoch, trigger, (long)(now - lastList).TotalSeconds, state.AttemptStep,
                    state.AttemptStartedAt is { } running ? (long)(now - running).TotalSeconds : 0L, (long)limit,
                    live.DispatchEligible, state.LastRefusal ?? "none", live.PendingEvents);
                // Callbacks run off this lock: the cancelled attempt resumes on the pool.
                _ = state.AttemptCts?.CancelAsync();
                state.RestartRequested = true;
            }
        }
    }

    private static DateTimeOffset WindowStart(ConnectionState state)
    {
        var start = state.FirstSeenAt;
        if (state.LastListAt is { } listed && listed > start)
            start = listed;
        if (state.LastRestartAt is { } restarted && restarted > start)
            start = restarted;
        return start;
    }

    /// <summary>Apply: Pending back to Watching. The restart opens a new window at now.</summary>
    private void ApplyRestartIfRequested(PhoneHomeLiveConnection live, ConnectionState state, Cycle cycle)
    {
        lock (state.WatchLock)
        {
            if (!state.RestartRequested)
                return;
            state.RestartRequested = false;
            state.LastRestartAt = live.Clock.GetUtcNow();
            state.RefusalLoggedAt = null;
        }

        cycle.NextCatchUp = null;
        if (cycle.Refresh is { IsCompleted: false } stuck)
        {
            cycle.Refresh = null;
            _ = stuck.ContinueWith(
                t => _logger.LogDebug(t.Exception?.GetBaseException(),
                    "Detached phone-home inventory refresh for runner {RunnerId} epoch {Epoch} ended {Status}",
                    live.RunnerId, live.Epoch, t.Status),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private static CancellationTokenSource BeginAttempt(PhoneHomeLiveConnection live, ConnectionState state)
    {
        var restart = new CancellationTokenSource();
        lock (state.WatchLock)
        {
            state.AttemptId++;
            state.AttemptStartedAt = live.Clock.GetUtcNow();
            state.AttemptStep = Steps.None;
            state.AttemptCts = restart;
        }
        return restart;
    }

    private static void EndAttempt(ConnectionState state, CancellationTokenSource restart)
    {
        lock (state.WatchLock)
        {
            state.AttemptStartedAt = null;
            state.AttemptStep = Steps.None;
            if (ReferenceEquals(state.AttemptCts, restart))
                state.AttemptCts = null;
        }

        // A cancelled source may still be running its callbacks on the pool; it owns no timer
        // or parent registration, so it is left to the collector.
        if (!restart.IsCancellationRequested)
            restart.Dispose();
    }

    private static void SetStep(ConnectionState state, string step)
    {
        lock (state.WatchLock)
            state.AttemptStep = step;
    }

    /// <summary>
    /// CARD-1964 D-5 (L4): the first refusal of a streak logs, so does a change of stage or reason,
    /// then at most one line per <see cref="PhoneHomeRunnerSettings.RecoveryRefusalLogSeconds"/>.
    /// Every refusal is counted.
    /// </summary>
    private void NoteRefusal(PhoneHomeLiveConnection live, ConnectionState state, string stage, string reason)
    {
        var now = live.Clock.GetUtcNow();
        bool log;
        int refusals;
        DateTimeOffset first;
        lock (state.WatchLock)
        {
            if (state.Refusals == 0)
                state.FirstRefusedAt = now;
            state.Refusals++;
            log = state.RefusalLoggedAt is not { } last
                || !string.Equals(state.RefusalStage, stage, StringComparison.Ordinal)
                || !string.Equals(state.LastRefusal, reason, StringComparison.Ordinal)
                || (now - last).TotalSeconds >= _settings.RecoveryRefusalLogSeconds;
            state.RefusalStage = stage;
            state.LastRefusal = reason;
            if (log)
                state.RefusalLoggedAt = now;
            refusals = state.Refusals;
            first = state.FirstRefusedAt;
        }

        if (log)
            _logger.LogWarning(
                "Phone-home lease re-recovery for runner {RunnerId} epoch {Epoch} refused {Stage}: {Reason} "
                + "(pendingEvents {PendingEvents}; {Refusals} refusals since {FirstRefusedAt:O})",
                live.RunnerId, live.Epoch, stage, reason, live.PendingEvents, refusals, first);
    }

    /// <summary>CARD-1964 L5: every successful lease re-recovery; it ends the refusal streak.</summary>
    private void LogLeaseRecovered(PhoneHomeLiveConnection live, ConnectionState state)
    {
        var now = live.Clock.GetUtcNow();
        int refusals;
        DateTimeOffset lost;
        lock (state.WatchLock)
        {
            refusals = state.Refusals;
            lost = state.LeaseLostAt ?? now;
            state.Refusals = 0;
            state.RefusalLoggedAt = null;
            state.RefusalStage = null;
            state.LeaseLostAt = null;
        }

        _logger.LogInformation(
            "Phone-home runner {RunnerId} epoch {Epoch} re-recovered its lease after {IneligibleSeconds}s "
            + "(List {ListMs} ms, {Refusals} refusals); dispatch-eligible again",
            live.RunnerId, live.Epoch, (long)(now - lost).TotalSeconds, live.LastCatchUpMs, refusals);
    }

    internal Task PumpEventsAsync(PhoneHomeLiveConnection live, CancellationToken ct) =>
        new PhoneHomeEventScheduler(live, _settings.EventConsumerConcurrency,
            (worker, item, parsed, token) => ObserveEventAsync(live, worker, item, parsed, token),
            (item, error) => RecordEventFailure(live, item, null, error)).RunAsync(ct);

    private async Task ObserveEventAsync(PhoneHomeLiveConnection live, int worker, PhoneHomeQueuedEvent item,
        SessionRunnerEvent parsed, CancellationToken ct)
    {
        try
        {
                // CARD-0679 D-10: an exited session leaves the inventory whoever owns it; the List
                // the inventory came from is not owner-filtered either. Any other event from a
                // session is the runner confirming it is still there.
                if (parsed.Exited is not null)
                    live.NoteSessionGone(parsed.SessionId, parsed.Exited.AcceptedStartedAt);
                else
                    live.NoteSessionConfirmed(parsed.SessionId);
                live.Diagnostics.Phase(worker, parsed.SessionId, parsed.EventName, "owner");
                if (!await OwnerMatchesAsync(live, parsed.SessionId, ct))
                    return;

                await using var scope = _scopes.CreateAsyncScope();
                var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();
                if (parsed.Output is not null)
                    await runtime.ObserveOutputAsync(parsed.Output.SessionId, parsed.Output.Sequence, parsed.Output.Text,
                        phase => live.Diagnostics.Phase(worker, parsed.SessionId, parsed.EventName, phase switch
                        {
                            SessionObservationPhase.OutputMention => "output.mention",
                            SessionObservationPhase.OutputPublish => "output.publish",
                            _ => "output.activity"
                        }), ct);
                else if (parsed.Exited is not null)
                {
                    live.Diagnostics.Phase(worker, parsed.SessionId, parsed.EventName, "exit.observe");
                    await runtime.ObserveExitAsync(parsed.Exited, ct);
                }
                else if (parsed.Transcript is not null)
                {
                    live.Diagnostics.Phase(worker, parsed.SessionId, parsed.EventName, "transcript.persist");
                    await runtime.ObserveTranscriptAsync(parsed.Transcript, ct);
                }
                // CARD-1219 D-4: transcript fault/bound frames (PhoneHomeTranscriptIncidentTests).
                else if (parsed.TranscriptFault is { } fault)
                {
                    live.Diagnostics.Phase(worker, parsed.SessionId, parsed.EventName, "transcript.incident");
                    if (ReplayGuardFor(live).Admit(fault))
                        await scope.ServiceProvider.GetRequiredService<TranscriptBindingIncidentService>()
                            .OnTranscriptFaultAsync(fault, ct);
                    else
                        LogReplay(live, parsed.EventName, fault.SessionId);
                }
                else if (parsed.TranscriptBound is { } bound)
                {
                    live.Diagnostics.Phase(worker, parsed.SessionId, parsed.EventName, "transcript.incident");
                    if (ReplayGuardFor(live).Admit(bound))
                        await scope.ServiceProvider.GetRequiredService<TranscriptBindingIncidentService>()
                            .OnHeuristicBindAsync(bound, ct);
                    else
                        LogReplay(live, parsed.EventName, bound.SessionId);
                }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            RecordEventFailure(live, item, parsed.SessionId, ex);
        }
    }

    private void RecordEventFailure(PhoneHomeLiveConnection live, PhoneHomeQueuedEvent item, Guid? sessionId, Exception ex)
    {
        var failures = Interlocked.Increment(ref StateFor(live).EventFailures);
        _logger.LogWarning(ex,
            "Phone-home event {EventName} for session {SessionId} on runner {RunnerId} epoch {Epoch} failed; the pump continues ({EventFailures} failed events on this connection)",
            item.Frame.EventName, sessionId, live.RunnerId, live.Epoch, failures);
    }

    /// <summary>
    /// CARD-0679 D-3: a session's runner binding is persisted before its Launch and never changes,
    /// so a match holds for the connection's life. A miss is re-read after
    /// <see cref="PhoneHomeRunnerSettings.OwnerCacheNegativeSeconds"/>, so a row committed just
    /// after its first event is still picked up.
    /// </summary>
    internal async Task<bool> OwnerMatchesAsync(PhoneHomeLiveConnection live, Guid sessionId, CancellationToken ct)
    {
        var owners = StateFor(live).Owners;
        var now = live.Clock.GetUtcNow();
        if (owners.TryGetValue(sessionId, out var cached)
            && (cached.Owned || now - cached.At < TimeSpan.FromSeconds(_settings.OwnerCacheNegativeSeconds)))
            return cached.Owned;

        var binding = await _directory.GetBindingAsync(sessionId, ct);
        var owned = binding is SessionRunnerBinding.Remote remote
            && string.Equals(remote.Owner.RunnerId, live.RunnerId, StringComparison.Ordinal)
            && remote.Owner.RunnerStoreId == live.RunnerStoreId;
        owners[sessionId] = (owned, now);
        return owned;
    }

    private TranscriptIncidentReplayGuard ReplayGuardFor(PhoneHomeLiveConnection live) =>
        _replayGuards.GetOrAdd(live.RunnerId, _ => new TranscriptIncidentReplayGuard(
            live.Clock, TimeSpan.FromSeconds(_settings.TranscriptIncidentReplayWindowSeconds)));

    private void LogReplay(PhoneHomeLiveConnection live, string eventName, Guid sessionId) =>
        _logger.LogDebug(
            "Phone-home {EventName} for session {SessionId} on runner {RunnerId} epoch {Epoch} is a replay "
            + "inside {WindowSeconds}s; not recorded again",
            eventName, sessionId, live.RunnerId, live.Epoch, _settings.TranscriptIncidentReplayWindowSeconds);

    private ConnectionState StateFor(PhoneHomeLiveConnection live) =>
        _states.GetValue(live, static connection => new ConnectionState(connection.Clock.GetUtcNow()));

    private sealed class Cycle
    {
        public PhoneHomeLiveConnection? Recovered;
        public (PhoneHomeLiveConnection Live, DateTimeOffset At)? NextCatchUp;
        public DateTimeOffset NextRefresh;
        public Task? Refresh;
        public Task? Pump;
        public Task? Teardown;
        public CancellationTokenSource? PumpStop;
    }

    /// <summary>What the pump keeps for one connection; it goes with the connection.</summary>
    private sealed class ConnectionState(DateTimeOffset firstSeenAt)
    {
        public ConcurrentDictionary<Guid, (bool Owned, DateTimeOffset At)> Owners { get; } = new();
        public int EventFailures;
        public int RefreshFailures;

        // CARD-1964 watchdog. The loop and the watchdog share these; every one is read and
        // written under WatchLock.
        public object WatchLock { get; } = new();
        public DateTimeOffset FirstSeenAt = firstSeenAt;
        public DateTimeOffset? LastListAt;
        public DateTimeOffset? LastRestartAt;
        public DateTimeOffset? FiredWindowStart;
        public bool RestartRequested;
        public long AttemptId;
        public DateTimeOffset? AttemptStartedAt;
        public string AttemptStep = Steps.None;
        public CancellationTokenSource? AttemptCts;
        public long OverrunFiredAttemptId;

        // CARD-1964 D-5: the lease re-recovery refusal streak (L4/L5), also under WatchLock.
        public int Refusals;
        public DateTimeOffset FirstRefusedAt;
        public DateTimeOffset? RefusalLoggedAt;
        public string? RefusalStage;
        public string? LastRefusal;
        public DateTimeOffset? LeaseLostAt;
    }

    /// <summary>CARD-1964: the step an attempt is in, as L6 and the tests read it.</summary>
    private static class Steps
    {
        public const string None = "none";
        public const string Hold = "hold";
        public const string List = "list";
        public const string Owner = "owner";
        public const string Fetch = "transcript.fetch";
        public const string Persist = "transcript.persist";
        public const string Refresh = "refresh";
    }

    /// <summary>
    /// CARD-1964 D-1: a token cancelled by its parent, or after <see cref="Seconds"/> on the
    /// connection's clock. Zero or less is no timer: the bound is off.
    /// </summary>
    private sealed class Budget : IDisposable
    {
        private readonly CancellationTokenSource? _timer;
        private readonly CancellationTokenSource _linked;

        public Budget(CancellationToken parent, int seconds, TimeProvider clock)
        {
            Seconds = seconds;
            if (seconds > 0)
            {
                _timer = new CancellationTokenSource(TimeSpan.FromSeconds(seconds), clock);
                _linked = CancellationTokenSource.CreateLinkedTokenSource(parent, _timer.Token);
            }
            else
            {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(parent);
            }
        }

        public int Seconds { get; }
        public CancellationToken Token => _linked.Token;

        /// <summary>This budget's own timer fired (not its parent).</summary>
        public bool Expired => _timer?.IsCancellationRequested == true;

        public void Dispose()
        {
            _linked.Dispose();
            _timer?.Dispose();
        }
    }
}
