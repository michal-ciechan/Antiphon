using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerGrokAdapter : IAgentProtocolAdapter
{
    // The measured turn-end lines (grok 1.0.5, CARD-0080 S1): "Worked for 1.7s" — DECIMAL seconds,
    // which the old " for \d+s" integer regex never matched — and, for an Esc mid-turn,
    // "Turn cancelled by user in 1.7s." ("in", not "for"). Both are FALLBACK signals only: the
    // primary turn-end verdict is the tailed TurnEnd transcript row (CARD-0080 S2).
    private static readonly Regex DonePattern = new(
        @"Worked for \d+(?:\.\d+)?s|Turn cancelled by user", RegexOptions.Compiled);

    // Grok's idle OSC title is plain "grok" (measured; it NEVER sets Claude's ✳, which is what the
    // old check looked for). The BEL terminator keeps working titles ("Thinking - grok",
    // "⠹ - Waiting for response… - grok") from matching; note a session that has generated a
    // conversation title re-idles as "<title> - grok", which this exact form deliberately does not
    // match — the done lines above and the transcript row carry that case.
    private const string IdleTitleSignal = "\x1b]0;grok\x07";

    // How long after a screen-level done signal to keep polling for the trailing TurnEnd row —
    // measured ~90ms behind the done line, so this is generous.
    private static readonly TimeSpan TranscriptGraceAfterScreenDone = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TurnPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly RunnerTerminalSession _terminal;
    private readonly ISessionRunnerClient _client;
    private readonly AgentRegistrySettings _settings;
    private readonly DeliveryVerificationSettings _verification;
    private readonly ILogger? _logger;
    private long _promptStartSequence;
    private long _transcriptBaselineSequence;
    // CARD-0113: last successful GetTranscript LastSequence. Distinct from the per-turn floor
    // below — a failed capture must fall back here, not to 0, once this adapter has observed rows.
    private long? _lastKnownTranscriptSequence;
    private string? _lastPrompt;
    private bool _started;

    public RunnerGrokAdapter(
        ISessionRunnerClient client,
        IOptions<AgentRegistrySettings> options,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        ILogger? logger = null)
    {
        _client = client;
        _terminal = new RunnerTerminalSession(client);
        _settings = options.Value;
        _verification = (supervisionSettings?.Value ?? new SupervisionSettings()).DeliveryVerification;
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
            throw new InvalidOperationException("RunnerGrokAdapter already started.");
        _started = true;
        await _terminal.StartAsync(spec, ct);
    }

    public async Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) =>
        await _terminal.KillAsync(ct);

    public async Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        EnsureStarted();
        await _terminal.ClearLiveBufferAsync(ct);
        _lastPrompt = prompt;
        _promptStartSequence = await _terminal.GetLastSequenceAsync(ct);
        // The confirmation floor for this turn: rows ingested past it are THIS prompt's. Captured
        // before a byte is typed, same discipline as the queue's baseline (CARD-0055). A transient
        // miss preserves the last successful read rather than collapsing onto 0 (CARD-0113).
        CaptureTranscriptBaseline(await TryGetTranscriptAsync(ct));

        if (!_verification.Enabled)
        {
            await _terminal.SendLineAsync(prompt, ct);
            return;
        }

        try
        {
            await VerifiedPromptSubmitter.SubmitAsync(
                prompt,
                _terminal.SnapshotScreenAsync,
                _terminal.GetLastSequenceAsync,
                _terminal.WriteAsync,
                new VerifiedSubmitOptions(
                    TimeSpan.FromSeconds(_verification.EvidenceTimeoutSeconds),
                    TimeSpan.FromMilliseconds(_verification.PollIntervalMs),
                    TimeSpan.FromSeconds(_verification.PostSubmitAdvanceTimeoutSeconds)),
                message => _logger?.LogWarning(
                    "Session {SessionId} prompt delivery: {Message}", _terminal.SessionId, message),
                ct);
        }
        catch (PromptDeliveryException ex)
        {
            _logger?.LogWarning(
                "Session {SessionId} prompt delivery failed: {Message}", _terminal.SessionId, ex.Message);
            throw;
        }
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

    public async Task<bool> WaitForReadyAsync(CancellationToken ct)
    {
        EnsureStarted();
        var quiet = await _terminal.WaitForQuietAfterVisibleAsync(
            TimeSpan.FromMilliseconds(_settings.GrokReadyQuietPeriodMs),
            TimeSpan.FromMilliseconds(_settings.GrokReadyMaxWaitMs),
            ct);
        if (!quiet)
            return false;

        var remaining = TimeSpan.FromMilliseconds(_settings.GrokReadyMinTotalWaitMs)
            - (DateTime.UtcNow - _terminal.StartedAt);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return true;
    }

    /// <summary>
    /// Primary signal: the tailed <c>TurnEnd</c> transcript row past this prompt's baseline —
    /// Grok writes an explicit <c>turn_completed</c> for every turn, cancelled ones included
    /// (CARD-0080 S1/S2), and the reply text comes from the same window's <c>AssistantText</c>
    /// rows instead of screen-scraping. The screen heuristic (done line, idle title, quiet time)
    /// stays as the FALLBACK for a session with no transcript rows — and until S1 corrected the
    /// patterns it was entirely dead code, so quiet time is what production actually ran on.
    /// </summary>
    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(_settings.GrokDoneMaxWaitMs);
        var quietPeriod = TimeSpan.FromMilliseconds(_settings.GrokDoneQuietPeriodMs);
        var lastSequence = await _terminal.GetLastSequenceAsync(ct);
        var lastChange = DateTime.UtcNow;
        var screenDone = false;
        DateTime? screenDoneGraceDeadline = null;

        while (DateTime.UtcNow < deadline)
        {
            if (await TryBuildTranscriptVerdictAsync(ct) is { } fromTranscript)
                return fromTranscript;

            if (!screenDone)
            {
                var rawText = await _terminal.SnapshotTextAsync(ct);
                if (DonePattern.IsMatch(rawText) || rawText.Contains(IdleTitleSignal, StringComparison.Ordinal))
                {
                    screenDone = true;
                }
                else
                {
                    var sequence = await _terminal.GetLastSequenceAsync(ct);
                    if (sequence != lastSequence)
                    {
                        lastSequence = sequence;
                        lastChange = DateTime.UtcNow;
                    }
                    else if (DateTime.UtcNow - lastChange >= quietPeriod
                             && VisiblePtyOutput.HasVisibleOutput(rawText))
                    {
                        screenDone = true;
                    }
                }

                // The screen says done ~90ms BEFORE the turn_completed row lands; give the row a
                // short grace so the verdict (and the reply text) still come from the transcript
                // whenever one exists at all.
                if (screenDone)
                    screenDoneGraceDeadline = DateTime.UtcNow + TranscriptGraceAfterScreenDone;
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
    /// A completed verdict when the tailed transcript proves it: a TurnEnd row past this prompt's
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
        return new AgentTurnResult(
            TurnCompleted: true,
            ResponseText: reply.Length > 0 ? reply : CodexResponseAnalyzer.ExtractResponse(raw, _lastPrompt),
            IsAskingQuestion: CodexResponseAnalyzer.IsAskingQuestion(raw, _lastPrompt),
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
            // screen fallback carries the turn, exactly as it did before S2.
            _logger?.LogDebug(
                ex, "Session {SessionId}: transcript unavailable for turn-complete detection",
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
            throw new InvalidOperationException("RunnerGrokAdapter not started.");
    }
}
