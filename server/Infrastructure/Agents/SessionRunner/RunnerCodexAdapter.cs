using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerCodexAdapter : IAgentProtocolAdapter
{
    // How long after a screen-level done signal to keep polling for the trailing TurnEnd row, so a
    // verdict the transcript could have made is not made from the screen instead. Codex flushes
    // task_complete within ~300ms of the answer rendering (CARD-0099 S1), so this is generous.
    private static readonly TimeSpan TranscriptGraceAfterScreenDone = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TurnPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly RunnerTerminalSession _terminal;
    private readonly ISessionRunnerClient _client;
    private readonly AgentRegistrySettings _settings;
    private readonly ILogger? _logger;
    private long _promptStartSequence;
    private long _transcriptBaselineSequence;
    // CARD-0113: last successful GetTranscript LastSequence. Distinct from the per-turn floor
    // below — a failed capture must fall back here, not to 0, once this adapter has observed rows.
    private long? _lastKnownTranscriptSequence;
    private string? _lastPrompt;
    private bool _acceptedTrustPrompt;
    private bool _started;

    public RunnerCodexAdapter(
        ISessionRunnerClient client,
        IOptions<AgentRegistrySettings> options,
        ILogger? logger = null)
    {
        _client = client;
        _terminal = new RunnerTerminalSession(client);
        _settings = options.Value;
        _logger = logger;
    }

    public Task<int> Exited => _terminal.Exited;
    public int? Pid => _terminal.Pid;
    public AgentExitReason ExitReason => _terminal.ExitReason;
    public string? AuditDirectory => null;
    public event Action<string>? OnTextDelta
    {
        add { }
        remove { }
    }

    public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (_started)
            throw new InvalidOperationException("RunnerCodexAdapter already started.");
        _started = true;
        await _terminal.StartAsync(spec, ct);
    }

    public async Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) =>
        await _terminal.KillAsync(ct);

    /// <summary>
    /// Types the prompt and then PROVES it submitted, because for Codex a typed body and a
    /// submitted body are routinely different things (CARD-0108 S1: the production body + delayed
    /// CR stranded the prompt 6/6 in a measured probe, with the CR folding inside the TUI's
    /// paste-detection window). The confirmation is a <c>UserPrompt</c> row past the baseline
    /// captured here; the retry is Enter only. See <see cref="CodexSubmitConfirmation"/> for the
    /// whole contract, including why an unconfirmable session degrades instead of failing.
    /// </summary>
    public async Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        EnsureStarted();
        await _terminal.ClearLiveBufferAsync(ct);
        _lastPrompt = prompt;
        _promptStartSequence = await _terminal.GetLastSequenceAsync(ct);
        // The confirmation floor for this turn: rows past it are THIS prompt's — for the submit
        // confirmation below AND for WaitForTurnCompleteAsync's TurnEnd. Captured before a byte is
        // typed, same discipline as the queue's baseline (CARD-0055). A transient miss preserves
        // the last successful read rather than collapsing onto 0 (CARD-0113).
        CaptureTranscriptBaseline(await TryGetTranscriptAsync(ct));

        await CodexSubmitConfirmation.SubmitAsync(
            prompt,
            _transcriptBaselineSequence,
            c => _terminal.SendLineAsync(prompt, c),
            c => _terminal.WriteAsync("\r", c),
            async c => (await TryGetTranscriptAsync(c))?.Entries,
            _terminal.SnapshotScreenAsync,
            new CodexSubmitOptions(
                TimeSpan.FromMilliseconds(_settings.CodexSubmitReEnterIntervalMs),
                _settings.CodexSubmitAttempts,
                TimeSpan.FromMilliseconds(_settings.CodexSubmitConfirmTimeoutMs),
                TurnPollInterval),
            message => _logger?.LogWarning(
                "Session {SessionId} Codex prompt delivery: {Message}", _terminal.SessionId, message),
            ct);
    }

    public async Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct)
    {
        EnsureStarted();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await _terminal.GetLastSequenceAsync(ct) > _promptStartSequence)
                return true;
            await Task.Delay(25, ct);
        }

        return false;
    }

    public async Task SendInputAsync(string input, CancellationToken ct)
    {
        EnsureStarted();
        await _terminal.WriteAsync(input, ct);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken ct)
    {
        EnsureStarted();
        return _terminal.ResizeAsync(cols, rows, ct);
    }

    public Task<bool> WaitForReadyAsync(CancellationToken ct)
    {
        EnsureStarted();
        return _terminal.WaitForQuietAfterVisibleAsync(
            TimeSpan.FromMilliseconds(_settings.CodexReadyQuietPeriodMs),
            TimeSpan.FromMilliseconds(_settings.CodexReadyMaxWaitMs),
            ct,
            AcceptTrustPromptIfVisibleAsync);
    }

    /// <summary>
    /// Primary signal: the tailed <c>TurnEnd</c> row past this prompt's baseline — Codex writes an
    /// explicit <c>event_msg/task_complete</c> per turn (CARD-0099 S1), observable by a 250 ms
    /// poller within ~2.8 s of the submitting Enter, i.e. the same instant the answer renders and
    /// FASTER than the 3 s quiet wait it replaces. The reply text comes from the same window's
    /// <c>AssistantText</c> rows, and <c>IsAskingQuestion</c> from that reply — not from a screen
    /// scrape, which over a real completed turn picked up the composer's ghost hint text and
    /// spinner fragments alongside the answer (measured, CARD-0108 §1).
    ///
    /// <para>The screen heuristic is the FALLBACK for a session with no transcript rows, and for
    /// Codex it is deliberately not bare quiet: it requires the measured Working-indicator
    /// lifecycle (see <see cref="CodexTurnScreenTracker"/>). A session where the indicator never
    /// appears runs to <c>CodexDoneMaxWaitMs</c> and returns <c>TurnCompleted: false</c> — the
    /// stranded-composer shape, for which false is the truth and the old bare-quiet rule returned
    /// true with the status bar as the answer.</para>
    /// </summary>
    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(_settings.CodexDoneMaxWaitMs);
        var tracker = new CodexTurnScreenTracker(
            TimeSpan.FromMilliseconds(_settings.CodexDoneQuietPeriodMs));
        var screenDone = false;
        DateTime? screenDoneGraceDeadline = null;

        while (DateTime.UtcNow < deadline)
        {
            if (await TryBuildTranscriptVerdictAsync(ct) is { } fromTranscript)
                return fromTranscript;

            if (!screenDone)
            {
                var rawText = await _terminal.SnapshotTextAsync(ct);
                var screen = await _terminal.SnapshotScreenAsync(ct);
                var sequence = await _terminal.GetLastSequenceAsync(ct);
                if (tracker.Observe(screen, rawText, sequence, DateTime.UtcNow))
                {
                    screenDone = true;
                    // The screen can beat the row; give it a short grace so the verdict (and the
                    // reply text) still come from the transcript whenever one exists at all.
                    screenDoneGraceDeadline = DateTime.UtcNow + TranscriptGraceAfterScreenDone;
                }
            }
            else if (DateTime.UtcNow >= screenDoneGraceDeadline)
            {
                break;
            }

            try { await Task.Delay(TurnPollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }

        var raw = await _terminal.SnapshotTextAsync(ct);
        return new AgentTurnResult(
            TurnCompleted: screenDone,
            ResponseText: CodexResponseAnalyzer.ExtractResponse(raw, _lastPrompt),
            IsAskingQuestion: CodexResponseAnalyzer.IsAskingQuestion(raw, _lastPrompt),
            RawSnapshot: raw);
    }

    /// <summary>
    /// A completed verdict when the tailed rollout proves it: a TurnEnd row past this prompt's
    /// baseline, with the reply assembled from the AssistantText rows of the same window. Null
    /// while the turn is still running or no transcript is available.
    /// </summary>
    private async Task<AgentTurnResult?> TryBuildTranscriptVerdictAsync(CancellationToken ct)
    {
        var transcript = await TryGetTranscriptAsync(ct);
        if (transcript is null)
            return null;

        var rows = transcript.Entries
            .Where(e => e.Sequence > _transcriptBaselineSequence)
            .ToList();
        if (!rows.Any(e => e.Kind == TranscriptKinds.TurnEnd))
            return null;

        var reply = string.Join(
            "\n\n",
            rows.Where(e => e.Kind == TranscriptKinds.AssistantText && !string.IsNullOrWhiteSpace(e.Text))
                .Select(e => e.Text));
        var raw = await _terminal.SnapshotTextAsync(ct);
        if (reply.Length == 0)
        {
            // A turn with no assistant text of its own (a pure tool turn, or rows we could not
            // read): the screen scrape is all there is, and it carries the scrape's known flaws.
            return new AgentTurnResult(
                TurnCompleted: true,
                ResponseText: CodexResponseAnalyzer.ExtractResponse(raw, _lastPrompt),
                IsAskingQuestion: CodexResponseAnalyzer.IsAskingQuestion(raw, _lastPrompt),
                RawSnapshot: raw);
        }

        return new AgentTurnResult(
            TurnCompleted: true,
            ResponseText: reply,
            // From the REPLY, never the screen: the status bar and the composer's ghost hint text
            // both live on the screen and both contain punctuation this would otherwise read.
            IsAskingQuestion: CodexResponseAnalyzer.IsAskingQuestion(reply),
            RawSnapshot: raw);
    }

    private void CaptureTranscriptBaseline(SessionRunnerTranscriptDto? snapshot)
    {
        long? fetched = snapshot?.LastSequence;
        _transcriptBaselineSequence = TranscriptTurnBaseline.Resolve(fetched, _lastKnownTranscriptSequence);
        if (TranscriptTurnBaseline.PreservedLastKnown(fetched, _lastKnownTranscriptSequence))
        {
            _logger?.LogWarning(
                "Session {SessionId}: transcript fetch failed while capturing the turn baseline; preserving last-known sequence {Sequence} instead of resetting to 0",
                _terminal.SessionId, _transcriptBaselineSequence);
        }
    }

    private async Task<SessionRunnerTranscriptDto?> TryGetTranscriptAsync(CancellationToken ct)
    {
        try
        {
            var transcript = await _client.GetTranscriptAsync(_terminal.SessionId, ct);
            _lastKnownTranscriptSequence = transcript.LastSequence;
            return transcript;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No transcript endpoint / session not found / transient transport failure — the
            // screen fallback carries the turn, and the submit confirmation degrades to blind.
            _logger?.LogDebug(
                ex, "Session {SessionId}: transcript unavailable for Codex turn detection",
                _terminal.SessionId);
            return null;
        }
    }

    public string SnapshotRawOutput()
    {
        EnsureStarted();
        return _terminal.SnapshotTextAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<string> SnapshotRawOutputAsync(CancellationToken ct)
    {
        EnsureStarted();
        return await _terminal.SnapshotTextAsync(ct);
    }

    public string SnapshotRenderedScreen()
    {
        EnsureStarted();
        return _terminal.SnapshotScreenAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void EnsureStarted()
    {
        if (!_started)
            throw new InvalidOperationException("RunnerCodexAdapter not started.");
    }

    private async Task AcceptTrustPromptIfVisibleAsync(CancellationToken ct)
    {
        if (_acceptedTrustPrompt)
            return;

        var raw = await _terminal.SnapshotTextAsync(ct);
        var screen = await _terminal.SnapshotScreenAsync(ct);
        if (!CodexTrustPromptDetector.IsVisible(raw, screen))
            return;

        _acceptedTrustPrompt = true;
        await _terminal.WriteAsync("\r", ct);
    }
}
