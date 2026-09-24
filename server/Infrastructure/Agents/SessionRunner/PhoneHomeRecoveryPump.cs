using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
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
    private PhoneHomeLiveConnection? _recovered;
    private (PhoneHomeLiveConnection Live, DateTimeOffset At)? _nextCatchUp;
    private Task? _pump;
    private CancellationTokenSource? _pumpStop;
    private readonly ConditionalWeakTable<PhoneHomeLiveConnection, ConnectionState> _states = new();

    public PhoneHomeRecoveryPump(
        PhoneHomeRunnerDirectory directory,
        IOptions<PhoneHomeRunnerSettings> settings,
        IServiceScopeFactory scopes,
        ILogger<PhoneHomeRecoveryPump> logger)
    {
        _directory = directory;
        _settings = settings.Value;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Test seam: when set, catch-up waits until the TCS completes.</summary>
    internal TaskCompletionSource? CatchUpHold { get; set; }

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
    internal int LastCatchUpTranscriptFailures { get; private set; }

    /// <summary>
    /// One recovery step for the current live connection. Returns true when this cycle marked it
    /// dispatch-eligible.
    /// </summary>
    internal async Task<bool> RunCycleAsync(CancellationToken ct)
    {
        var live = _directory.SnapshotLive();
        if (live is null || !live.SocketOpen)
        {
            _recovered = null;
            _nextCatchUp = null;
            return false;
        }

        if (ReferenceEquals(_recovered, live))
        {
            // CARD-0679 D-2: nothing else would ever release this connection's later events. A
            // pump that ended because the connection was disposed drained a completed Events
            // channel; that ending is not an error, and the next cycle sees the closed socket.
            if (_pump is { IsCompleted: true } ended
                && !ct.IsCancellationRequested
                && !live.Events.Completion.IsCompleted)
            {
                _logger.LogError(
                    ended.Exception?.GetBaseException(),
                    "Phone-home event pump for runner {RunnerId} epoch {Epoch} ended while the connection is "
                    + "live ({PumpStatus}); restarting it with {PendingEvents} events pending",
                    live.RunnerId, live.Epoch, ended.Status, live.PendingEvents);
                StartPump(live, ct);
            }

            return false;
        }

        // CARD-0633 D-2: a failed catch-up is retried after CatchUpRetrySeconds on the connection's
        // own clock, not on the next 50 ms turn of the loop.
        if (_nextCatchUp is { } next && ReferenceEquals(next.Live, live) && live.Clock.GetUtcNow() < next.At)
            return false;

        // Dispatch-eligible only after the owner inventory was actually read. A List that failed or
        // timed out used to be swallowed here and the runner marked recovered anyway (CARD-0629).
        if (!await CatchUpAsync(live, ct))
        {
            _nextCatchUp = (live, live.Clock.GetUtcNow() + TimeSpan.FromSeconds(_settings.CatchUpRetrySeconds));
            return false;
        }

        _nextCatchUp = null;
        _directory.MarkRecovered(live);
        _recovered = live;
        StartPump(live, ct);
        return true;
    }

    private void StartPump(PhoneHomeLiveConnection live, CancellationToken ct)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pumpStop = stop;
        _pump = RunPumpAsync(live, stop);
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
        if (_pump is not { } pump)
            return;
        try
        {
            _pumpStop?.Cancel();
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

    /// <summary>CARD-0679 D-2: events on <paramref name="live"/> whose processing threw.</summary>
    internal int EventFailures(PhoneHomeLiveConnection live) => Volatile.Read(ref StateFor(live).EventFailures);

    internal async Task<bool> CatchUpAsync(PhoneHomeLiveConnection live, CancellationToken ct)
    {
        if (CatchUpHold is { } hold)
            await hold.Task.WaitAsync(ct);

        var client = new PhoneHomeRunnerClient(live);
        IReadOnlyList<SessionRunnerSessionDto> sessions;
        try
        {
            sessions = await client.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Phone-home catch-up List for runner {RunnerId} failed ({Code}); the runner stays "
                + "dispatch-ineligible and catch-up retries in {RetrySeconds}s",
                live.RunnerId, ProblemCode(ex), _settings.CatchUpRetrySeconds);
            return false;
        }

        LastCatchUpTranscriptFailures = 0;
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

        LastCatchUpTranscriptFailures = failures;
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

    /// <summary>What the pump keeps for one connection; it goes with the connection.</summary>
    private sealed class ConnectionState
    {
        public ConcurrentDictionary<Guid, (bool Owned, DateTimeOffset At)> Owners { get; } = new();
        public int EventFailures;
    }
}
