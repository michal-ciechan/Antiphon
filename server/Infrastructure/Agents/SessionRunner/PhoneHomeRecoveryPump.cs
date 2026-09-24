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
            return false;

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
    internal int EventFailures(PhoneHomeLiveConnection live) => 0;

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
            try
            {
                if (frame.EventName is null || frame.Payload is null)
                    continue;
                var parsed = RunnerContractMapper.ParseEvent(frame.EventName, frame.Payload.Value.GetRawText());
                if (parsed is null)
                    continue;
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
            finally
            {
                live.ReleaseEvent(size);
            }
        }
    }

    internal async Task<bool> OwnerMatchesAsync(PhoneHomeLiveConnection live, Guid sessionId, CancellationToken ct)
    {
        var binding = await _directory.GetBindingAsync(sessionId, ct);
        return binding is SessionRunnerBinding.Remote remote
            && string.Equals(remote.Owner.RunnerId, live.RunnerId, StringComparison.Ordinal)
            && remote.Owner.RunnerStoreId == live.RunnerStoreId;
    }
}
