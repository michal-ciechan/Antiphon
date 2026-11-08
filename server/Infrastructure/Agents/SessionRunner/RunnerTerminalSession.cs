using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

internal sealed class RunnerTerminalSession
{
    private readonly ISessionRunnerClient _client;
    // CARD-0679 R5 repair 2: a remote client follows the runner's current connection, so an exit
    // watcher left running after its adapter was released would poll the replacement connection.
    private readonly CancellationTokenSource _watcher = new();
    private Guid _sessionId;
    private bool _started;
    private DateTime? _acceptedGeneration;
    private string? _backend;
    private ClaudeEffortIntent _effortIntent = new();

    public RunnerTerminalSession(ISessionRunnerClient client)
    {
        _client = client;
    }

    public Guid SessionId => _sessionId;
    public DateTime StartedAt { get; private set; }
    public DateTime? AcceptedStartedAt => _acceptedGeneration;
    public int? Pid { get; private set; }
    public AgentExitReason ExitReason { get; private set; } = AgentExitReason.Unknown;

    /// <summary>
    /// Cause observed by the last <see cref="KillGenerationAsync"/>, not by the exit watcher.
    /// Null when that call did not report one (including <see cref="AgentExitReason.Unknown"/>).
    /// </summary>
    public AgentExitReason? GenerationKillCause { get; private set; }
    public Task<int> Exited { get; private set; } = Task.FromResult(-1);
    private readonly TaskCompletionSource<AgentExitObservation> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<AgentExitObservation> Completion => _completion.Task;

    public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (_started)
            throw new InvalidOperationException("Runner terminal session already started.");
        if (spec.SessionId is not Guid sessionId || sessionId == Guid.Empty)
            throw new InvalidOperationException("AgentLaunchSpec.SessionId is required for session runner launches.");

        _sessionId = sessionId;
        _acceptedGeneration = spec.AcceptedStartedAt;
        _effortIntent = ClaudeEffortIntent.Read(spec.Args);
        var session = await _client.StartAsync(sessionId, spec, ct);
        StartedAt = session.StartedAt;
        _backend = session.Backend;
        Pid = session.Pid;
        ExitReason = session.ExitReason;
        Exited = WaitForExitAsync(sessionId, _watcher.Token);
        _started = true;
    }

    /// <summary>
    /// Adopt a runner session this process did not start (CARD-0340). Requires the runner to
    /// report Running with no Pending herdr wait. <see cref="StartedAt"/> is copied from the
    /// runner so Claude/Grok <c>MinTotalWait</c> floors are already satisfied on a minutes-old
    /// process.
    /// </summary>
    public async Task AttachAsync(Guid sessionId, CancellationToken ct)
    {
        if (_started)
            throw new InvalidOperationException("Runner terminal session already started.");
        if (sessionId == Guid.Empty)
            throw new InvalidOperationException("sessionId is required to attach a runner session.");

        SessionRunnerSessionDto session;
        try
        {
            session = await _client.GetAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Cannot attach: session runner does not know session {sessionId}.", ex);
        }

        if (!string.Equals(session.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot attach: runner session {sessionId} is {session.Status}.");
        }

        if (!string.IsNullOrEmpty(session.Pending))
        {
            throw new InvalidOperationException(
                $"Cannot attach: runner session {sessionId} is pending ({session.Pending}).");
        }

        _sessionId = sessionId;
        StartedAt = session.StartedAt;
        _backend = session.Backend;
        _acceptedGeneration = session.AcceptedStartedAt;
        Pid = session.Pid;
        ExitReason = session.ExitReason;
        Exited = WaitForExitAsync(sessionId, _watcher.Token);
        _started = true;
    }

    public async Task WriteAsync(string input, CancellationToken ct)
    {
        EnsureStarted();
        await _client.SendInputAsync(_sessionId, input, ct);
    }

    internal async Task WriteGuardedAsync(string input, CancellationToken ct,
        ClaudeDialogInputIntent intent = ClaudeDialogInputIntent.None)
    {
        EnsureStarted();
        var observation = await new ProviderDialogObservationService(_client).ReadAsync(_sessionId, ct);
        if (_acceptedGeneration is not { } generation)
            throw new ProviderDialogInputRefusedException(ConditionalInputOutcomes.ObservationUnknown);
        var request = ProviderDialogObservationService.Request(observation, _sessionId, generation, input, intent);
        ProviderDialogObservationService.RequireWritten(await _client.SendConditionalInputAsync(_sessionId, request, ct));
    }

    /// <summary>
    /// CARD-1668: a startup keystroke refused as <c>stale-observation</c> (output arrived between
    /// the snapshot and the write) is re-observed and re-sent at most this many times in all.
    /// </summary>
    internal const int StartupStaleObservationAttempts = 3;

    private static readonly TimeSpan StartupStaleObservationRetryDelay = TimeSpan.FromMilliseconds(50);

    internal async Task WriteStartupAsync(string input, CancellationToken ct)
    {
        ClaudeDialogInputIntent? firstIntent = null;
        for (var attempt = 1; ; attempt++)
        {
            var snapshot = await GetSnapshotAsync(ct);
            var screen = snapshot.RenderedScreen;
            var kind = ClaudeBlockingPromptDetector.Detect(screen)?.Kind;
            var intent = kind == ClaudeBlockingPromptKind.TrustFolder ? ClaudeDialogInputIntent.StartupTrust
                : kind == ClaudeBlockingPromptKind.EffortChoice && ClaudeEffortPrompt.Parse(screen) is { } menu
                    ? menu.Select(_effortIntent) switch
                    {
                        ClaudeEffortOption.Keep => ClaudeDialogInputIntent.StartupEffortKeep,
                        ClaudeEffortOption.Switch => ClaudeDialogInputIntent.StartupEffortSwitch,
                        _ => ClaudeDialogInputIntent.None
                    } : ClaudeDialogInputIntent.None;
            // The caller chose this input for the screen it saw. A re-observed screen that now
            // calls for another intent (a trust dialog gone, an effort menu moved) is not the
            // screen the input answers, so it keeps the refusal instead of re-sending.
            firstIntent ??= intent;
            if (intent != firstIntent)
                throw new ProviderDialogInputRefusedException(ConditionalInputOutcomes.StaleObservation);
            if (_acceptedGeneration is not { } generation)
                throw new ProviderDialogInputRefusedException(ConditionalInputOutcomes.ObservationUnknown);
            var request = ProviderDialogObservationService.Request(snapshot.Dialog, _sessionId, generation, input, intent);
            var result = await _client.SendConditionalInputAsync(_sessionId, request, ct);
            // StaleObservation guarantees ZERO backend bytes, so the same input may be re-sent
            // against a fresh snapshot, within the caller's readiness deadline. Written/Unknown and
            // every other result are terminal: never replay a keystroke that may have arrived.
            if (result.Outcome != ConditionalInputOutcomes.StaleObservation || attempt >= StartupStaleObservationAttempts)
            {
                ProviderDialogObservationService.RequireWritten(result);
                return;
            }
            await Task.Delay(StartupStaleObservationRetryDelay, ct);
        }
    }

    internal async Task SendGuardedLineAsync(string line, CancellationToken ct)
    {
        await WriteGuardedAsync(PtyInputEncoding.EncodeBody(line), ct);
        await Task.Delay(20, ct);
        await WriteGuardedAsync("\r", ct);
    }

    public Task<RunnerConditionalInputResult> WriteConditionalAsync(
        RunnerConditionalInputRequest request, CancellationToken ct)
    {
        EnsureStarted();
        return _client.SendConditionalInputAsync(_sessionId, request, ct);
    }

    public async Task SendLineAsync(string line, CancellationToken ct)
    {
        EnsureStarted();
        // LF-normalized and bracket-wrapped when multi-line (PtyInputEncoding REQUIREMENT). This
        // path delivered the interactive/card boot prompts RAW: a CRLF prompt's trailing CR fell
        // inside the TUI's paste window, folded to a newline, and the whole prompt sat stranded
        // unsubmitted in the composer (live miss 2026-08-08 — "agent never came back" after a
        // supervised restart). The submitting CR stays a separate, delayed, unbracketed write.
        await _client.SendInputAsync(_sessionId, PtyInputEncoding.EncodeBody(line), ct);
        await Task.Delay(20, ct);
        await _client.SendInputAsync(_sessionId, "\r", ct);
    }

    public async Task ClearLiveBufferAsync(CancellationToken ct)
    {
        EnsureStarted();
        await _client.ClearLiveBufferAsync(_sessionId, ct);
    }

    public async Task<string> SnapshotTextAsync(CancellationToken ct)
    {
        EnsureStarted();
        return (await _client.GetSnapshotAsync(_sessionId, ct)).RawOutput;
    }

    public async Task<string> SnapshotScreenAsync(CancellationToken ct)
    {
        EnsureStarted();
        return (await GetSnapshotAsync(ct)).RenderedScreen;
    }

    /// <summary>
    /// CARD-0574 D-1: one runner snapshot per readiness decision so raw text and the
    /// rendered screen are the same frame.
    /// </summary>
    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(CancellationToken ct)
    {
        EnsureStarted();
        return _client.GetSnapshotAsync(_sessionId, ct);
    }

    public async Task<long> GetLastSequenceAsync(CancellationToken ct)
    {
        EnsureStarted();
        // CARD-1766: a pty session record is an in-memory sequence. Herdr's single-session
        // GET also runs pane.get, process-info and a status refresh, and an old runner
        // omits Backend. Both stay on the buffer route, which returns the runner's current
        // LastSequence on every call. The sequence is not cached in this process.
        if (SequencePollRoute.UsesSessionRecord(_backend))
            return (await _client.GetAsync(_sessionId, ct)).LastSequence;
        return (await _client.GetBufferAsync(_sessionId, ct)).LastSequence;
    }

    public async Task<bool> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        EnsureStarted();
        var deadline = DateTime.UtcNow + timeout;
        // A pty frame moves with LastSequence under one lock, so holding it until the
        // next session read is not a time cache. Herdr's screen is a pane.read, and an
        // old runner omits Backend: both keep a snapshot on every poll.
        var sequenceTracksFrame = string.Equals(_backend, SessionBackends.PtyHost, StringComparison.Ordinal);
        long? heldSequence = null;
        string? heldText = null;
        while (DateTime.UtcNow < deadline)
        {
            string text;
            if (sequenceTracksFrame)
            {
                var session = await _client.GetAsync(_sessionId, ct);
                if (heldText is null || session.LastSequence != heldSequence)
                {
                    var frame = await GetSnapshotAsync(ct);
                    heldText = frame.RawOutput;
                    heldSequence = frame.LastSequence;
                }

                text = heldText;
            }
            else
            {
                text = await SnapshotTextAsync(ct);
            }

            if (predicate(text))
                return true;

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return false;
    }

    public async Task<bool> WaitForQuietAsync(
        TimeSpan quietPeriod,
        TimeSpan maxWait,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + maxWait;
        var lastSequence = await GetLastSequenceAsync(ct);
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }

            var currentSequence = await GetLastSequenceAsync(ct);
            if (currentSequence != lastSequence)
            {
                lastSequence = currentSequence;
                lastChange = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastChange >= quietPeriod)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Quiet cannot count until the snapshot has visible child output (CARD-0052).
    /// Lockstep with <c>PtyAgentRunner.WaitForQuietAfterVisibleAsync</c>: poll until
    /// visible, then wait <paramref name="quietPeriod"/> of no further sequence
    /// change. Returns false if <paramref name="maxWait"/> expires at either stage.
    /// </summary>
    public async Task<bool> WaitForQuietAfterVisibleAsync(
        TimeSpan quietPeriod,
        TimeSpan maxWait,
        CancellationToken ct,
        Func<CancellationToken, Task>? observeAsync = null)
    {
        var deadline = DateTime.UtcNow + maxWait;

        while (DateTime.UtcNow < deadline)
        {
            if (observeAsync is not null)
                await observeAsync(ct);

            if (VisiblePtyOutput.HasVisibleOutput(await SnapshotTextAsync(ct)))
                break;

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }

        if (!VisiblePtyOutput.HasVisibleOutput(await SnapshotTextAsync(ct)))
            return false;

        var lastSequence = await GetLastSequenceAsync(ct);
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            if (observeAsync is not null)
                await observeAsync(ct);

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }

            var currentSequence = await GetLastSequenceAsync(ct);
            if (currentSequence != lastSequence)
            {
                lastSequence = currentSequence;
                lastChange = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastChange >= quietPeriod)
                return true;
        }

        return false;
    }

    public async Task ResizeAsync(int cols, int rows, CancellationToken ct)
    {
        EnsureStarted();
        await _client.ResizeAsync(_sessionId, cols, rows, ct);
    }

    public async Task<bool> KillAsync(CancellationToken ct)
    {
        EnsureStarted();
        var session = await _client.KillAsync(_sessionId, ct);
        ExitReason = session.ExitReason;
        return session.Status == "Exited" || session.ExitCode is not null || session.ExitSignal is > 0;
    }

    public async Task<bool> KillGenerationAsync(DateTime expectedAcceptedStartedAt, CancellationToken ct)
    {
        GenerationKillCause = null;
        if (_sessionId == Guid.Empty)
            return false;
        if (!SessionGeneration.Equal(_acceptedGeneration, expectedAcceptedStartedAt))
            return false;

        var result = await _client.KillGenerationAsync(_sessionId, expectedAcceptedStartedAt, ct);
        var cause = RunnerContractMapper.MapExitReason(result.ExitReason ?? "");
        if (cause != AgentExitReason.Unknown)
        {
            GenerationKillCause = cause;
            ExitReason = cause;
        }

        return result.Killed;
    }

    /// <summary>
    /// Ends the exit watcher: the adapter that owns this session was disposed, so nothing reads its
    /// exit any more and no further Get is sent. <see cref="Exited"/> then completes with -1, as it
    /// does when the watcher loses the runner.
    /// </summary>
    public void StopWatching() => _watcher.Cancel();

    private async Task<int> WaitForExitAsync(Guid sessionId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var session = await _client.GetAsync(sessionId, ct);
                if (_acceptedGeneration is { } mine
                    && session.AcceptedStartedAt is { } current
                    && !SessionGeneration.Equal(current, mine))
                {
                    _completion.TrySetResult(new(null));
                    return -1;
                }

                if (session.Status == "Exited")
                {
                    if (_acceptedGeneration is { } expected
                        && session.AcceptedStartedAt is { } exitedGen
                        && !SessionGeneration.Equal(exitedGen, expected))
                    {
                        _completion.TrySetResult(new(null));
                        return -1;
                    }

                    ExitReason = session.ExitReason;
                    _completion.TrySetResult(new(session.ExitCode, session.ExitSignal, session.ExitStopIntent));
                    return new AgentExitObservation(session.ExitCode, session.ExitSignal, session.ExitStopIntent).CompatibilityCode;
                }
            }
            catch
            {
                _completion.TrySetResult(new(null));
                return -1;
            }

            try
            {
                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException)
            {
                _completion.TrySetResult(new(null));
                return -1;
            }
        }

        _completion.TrySetResult(new(null));
        return -1;
    }

    private void EnsureStarted()
    {
        if (!_started)
            throw new InvalidOperationException("Runner terminal session not started.");
    }
}
