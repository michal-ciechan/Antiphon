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
    private readonly ConditionalWeakTable<PhoneHomeLiveConnection, ConnectionState> _states = new();

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

    /// <summary>How many owned sessions' transcripts failed in the last catch-up that listed.</summary>
    internal int LastCatchUpTranscriptFailures =>
        _transcriptFailures.Count == 0 ? 0 : _transcriptFailures.Values.Sum();

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
        if (live is null || !live.SocketOpen)
        {
            cycle.Recovered = null;
            cycle.NextCatchUp = null;
            return false;
        }

        if (ReferenceEquals(cycle.Recovered, live))
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

            RefreshInventoryIfDue(live, cycle, ct);
            return false;
        }

        // CARD-0633 D-2: a failed catch-up is retried after CatchUpRetrySeconds on the connection's
        // own clock, not on the next 50 ms turn of the loop.
        if (cycle.NextCatchUp is { } next && ReferenceEquals(next.Live, live) && live.Clock.GetUtcNow() < next.At)
            return false;

        // Dispatch-eligible only after the owner inventory was actually read. A List that failed or
        // timed out used to be swallowed here and the runner marked recovered anyway (CARD-0629).
        if (!await CatchUpAsync(live, ct))
        {
            cycle.NextCatchUp = (live, live.Clock.GetUtcNow() + TimeSpan.FromSeconds(_settings.CatchUpRetrySeconds));
            return false;
        }

        cycle.NextCatchUp = null;
        cycle.NextRefresh = live.Clock.GetUtcNow() + TimeSpan.FromSeconds(_settings.InventoryRefreshSeconds);
        _directory.MarkRecovered(live);
        cycle.Recovered = live;
        StartPump(live, cycle, ct);
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
        try
        {
            await ReadInventoryAsync(live, ct);
            var ended = Interlocked.Exchange(ref state.RefreshFailures, 0);
            if (ended >= StaleAfterRefreshes)
                _logger.LogInformation(
                    "Phone-home inventory refresh for runner {RunnerId} epoch {Epoch} succeeded after "
                    + "{ConsecutiveFailures} failed refreshes", live.RunnerId, live.Epoch, ended);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The last inventory stands, but only until its entries age past InventoryMaxAge; a
            // closed connection vouches for nothing anyway, and the next refresh or the next
            // connection's catch-up reads it again.
            var failures = Interlocked.Increment(ref state.RefreshFailures);
            _logger.LogWarning(
                ex, "Phone-home inventory refresh List for runner {RunnerId} epoch {Epoch} failed ({Code})",
                live.RunnerId, live.Epoch, ProblemCode(ex));
            if (failures == StaleAfterRefreshes)
                _logger.LogWarning(
                    "Phone-home inventory refreshes for runner {RunnerId} epoch {Epoch} failed {ConsecutiveFailures} "
                    + "times in a row while the connection stays up; cached sessions no List, launch ack or event "
                    + "confirmed within {MaxAgeSeconds}s no longer count as live",
                    live.RunnerId, live.Epoch, failures, _settings.InventoryMaxAge?.TotalSeconds);
        }
    }

    // Review 57fa2e6a: the Warning threshold is the stale bound; with the bound off, three in a row.
    private int StaleAfterRefreshes =>
        _settings.InventoryStaleAfterRefreshes > 0 ? _settings.InventoryStaleAfterRefreshes : 3;

    /// <summary>
    /// CARD-0679 D-10: one List, which replaces <paramref name="live"/>'s cached inventory and
    /// records its latency as <see cref="PhoneHomeLiveConnection.LastCatchUpMs"/> (D-1).
    /// </summary>
    private static async Task<IReadOnlyList<SessionRunnerSessionDto>> ReadInventoryAsync(
        PhoneHomeLiveConnection live, CancellationToken ct)
    {
        var stamp = live.BeginInventoryRead();
        var started = live.Clock.GetTimestamp();
        var sessions = await new PhoneHomeRunnerClient(live).ListAsync(ct);
        live.LastCatchUpMs = (long)live.Clock.GetElapsedTime(started).TotalMilliseconds;
        live.ReplaceKnownLiveSessions(
            sessions.Where(s => s.Status is "Running" or "Starting").Select(s => (s.SessionId, s.AcceptedStartedAt)),
            stamp);
        return sessions;
    }

    private void StartPump(PhoneHomeLiveConnection live, Cycle cycle, CancellationToken ct)
    {
        cycle.PumpStop?.Cancel();
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cycle.PumpStop = stop;
        cycle.Pump = RunPumpAsync(live, stop);
    }

    private async Task RunPumpAsync(PhoneHomeLiveConnection live, CancellationTokenSource stop)
    {
        try
        {
            await PumpEventsAsync(live, stop.Token);
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

    internal async Task<bool> CatchUpAsync(PhoneHomeLiveConnection live, CancellationToken ct)
    {
        var hold = _catchUpHolds.TryGetValue(live.RunnerId, out var specific) ? specific : CatchUpHold;
        if (hold is not null)
            await hold.Task.WaitAsync(ct);

        var client = new PhoneHomeRunnerClient(live);
        IReadOnlyList<SessionRunnerSessionDto> sessions;
        try
        {
            sessions = await ReadInventoryAsync(live, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Phone-home catch-up List for runner {RunnerId} failed ({Code}); the runner stays "
                + "dispatch-ineligible and catch-up retries in {RetrySeconds}s",
                live.RunnerId, ProblemCode(ex), _settings.CatchUpRetrySeconds);
            return false;
        }

        _transcriptFailures[live.RunnerId] = 0;
        if (sessions.Count == 0)
            return true;

        // One unreadable session is a Warning, not a fence: List is the owner inventory, and
        // without it owner matching is blind, but a single dead transcript must not keep the whole
        // runner ineligible forever.
        var failures = 0;
        await using var scope = _scopes.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();
        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();
            if (!await OwnerMatchesAsync(live, session.SessionId, ct))
                continue;
            try
            {
                var transcript = await client.GetTranscriptAsync(session.SessionId, ct);
                await runtime.PersistTranscriptAsync(session.SessionId, transcript.Entries);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(
                    ex, "Phone-home catch-up transcript for session {SessionId} on runner {RunnerId} failed ({Code})",
                    session.SessionId, live.RunnerId, ProblemCode(ex));
            }
        }

        _transcriptFailures[live.RunnerId] = failures;
        return true;
    }

    private static string ProblemCode(Exception ex) => ex switch
    {
        PhoneHomeTransportException transport => transport.Code,
        HttpException { Code: { } code } => code,
        _ => ex.GetType().Name,
    };

    internal async Task PumpEventsAsync(PhoneHomeLiveConnection live, CancellationToken ct)
    {
        await foreach (var frame in live.Events.ReadAllAsync(ct))
        {
            var size = frame.Payload?.GetRawText().Length ?? 0;
            Guid? sessionId = null;
            try
            {
                if (frame.EventName is null || frame.Payload is null)
                    continue;
                var parsed = RunnerContractMapper.ParseEvent(frame.EventName, frame.Payload.Value.GetRawText());
                if (parsed is null)
                    continue;
                sessionId = parsed.SessionId;
                // CARD-0679 D-10: an exited session leaves the inventory whoever owns it; the List
                // the inventory came from is not owner-filtered either. Any other event from a
                // session is the runner confirming it is still there.
                if (parsed.Exited is not null)
                    live.NoteSessionGone(parsed.SessionId, parsed.Exited.AcceptedStartedAt);
                else
                    live.NoteSessionConfirmed(parsed.SessionId);
                if (!await OwnerMatchesAsync(live, parsed.SessionId, ct))
                    continue;

                await using var scope = _scopes.CreateAsyncScope();
                var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();
                if (parsed.Output is not null)
                    await runtime.ObserveOutputAsync(parsed.Output.SessionId, parsed.Output.Sequence, parsed.Output.Text, ct);
                else if (parsed.Exited is not null)
                    await runtime.ObserveExitAsync(parsed.Exited, ct);
                else if (parsed.Transcript is not null)
                    await runtime.ObserveTranscriptAsync(parsed.Transcript, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // CARD-0679 D-2: one event that cannot be observed must not end the pump, or every
                // later event on the connection stays pending until the cap closes the socket. The
                // runner's transcript still holds it, and the next connection's catch-up re-reads it.
                var failures = Interlocked.Increment(ref StateFor(live).EventFailures);
                _logger.LogWarning(
                    ex, "Phone-home event {EventName} for session {SessionId} on runner {RunnerId} epoch {Epoch} "
                    + "failed; the pump continues ({EventFailures} failed events on this connection)",
                    frame.EventName, sessionId, live.RunnerId, live.Epoch, failures);
            }
            finally
            {
                live.ReleaseEvent(size);
            }
        }
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

    private ConnectionState StateFor(PhoneHomeLiveConnection live) => _states.GetValue(live, _ => new ConnectionState());

    private sealed class Cycle
    {
        public PhoneHomeLiveConnection? Recovered;
        public (PhoneHomeLiveConnection Live, DateTimeOffset At)? NextCatchUp;
        public DateTimeOffset NextRefresh;
        public Task? Refresh;
        public Task? Pump;
        public CancellationTokenSource? PumpStop;
    }

    /// <summary>What the pump keeps for one connection; it goes with the connection.</summary>
    private sealed class ConnectionState
    {
        public ConcurrentDictionary<Guid, (bool Owned, DateTimeOffset At)> Owners { get; } = new();
        public int EventFailures;
        public int RefreshFailures;
    }
}
