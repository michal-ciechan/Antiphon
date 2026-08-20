using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerClaudeAdapter : IAgentProtocolAdapter
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);
    private const string IdleTitleSignal = "\x1b]0;✳";

    private readonly RunnerTerminalSession _terminal;
    private readonly AgentRegistrySettings _settings;
    private readonly DeliveryVerificationSettings _verification;
    private readonly ILogger? _logger;
    private long _promptStartSequence;
    private bool _started;

    public RunnerClaudeAdapter(
        ISessionRunnerClient client,
        IOptions<AgentRegistrySettings> options,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        ILogger? logger = null)
    {
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
            throw new InvalidOperationException("RunnerClaudeAdapter already started.");
        _started = true;
        await _terminal.StartAsync(spec, ct);
    }

    public async Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) =>
        await _terminal.KillAsync(ct);

    public async Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        EnsureStarted();
        await _terminal.ClearLiveBufferAsync(ct);
        _promptStartSequence = await _terminal.GetLastSequenceAsync(ct);

        if (!_verification.Enabled)
        {
            await _terminal.SendLineAsync(prompt, ct);
            return;
        }

        // Verified delivery, same contract as the queue's DeliverAsync: composer evidence before
        // the Enter, output advance after it, swallowed Enters re-pressed. Boot prompts used to go
        // blind here — on 2026-08-08 a relaunched agent's card prompt sat unsubmitted in the
        // composer for half an hour with nothing logged. A verification failure now throws, which
        // fails the launch loudly and lets the supervisor retry it.
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
            TimeSpan.FromMilliseconds(_settings.ClaudeReadyQuietPeriodMs),
            TimeSpan.FromMilliseconds(_settings.ClaudeReadyMaxWaitMs),
            ct);
        if (!quiet)
            return false;

        // Quiet is not the same as usable. A first launch into a directory Claude has never seen
        // parks on the trust dialog and makes no output at all, so it reads as the quietest possible
        // session — CARD-0047's check interpreter was killed and relaunched seven times on that lie
        // (2026-08-16). Checked AFTER the quiet wait, so the modal has finished rendering.
        var blocking = await ResolveBlockingStartupPromptAsync(ct);
        if (blocking == ClaudeStartupBlockOutcome.TrustNotCleared)
            return false;

        var remaining = TimeSpan.FromMilliseconds(_settings.ClaudeReadyMinTotalWaitMs)
            - (DateTime.UtcNow - _terminal.StartedAt);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, ct); }
            catch (OperationCanceledException) { return false; }
        }

        // CARD-0103, and the ONLY step that proves the TUI is reading rather than painted — so it
        // goes last and its verdict is final. Everything above it is an output-side signal, and an
        // input-deaf TUI satisfies every one of them: it is quiet, it has a rendered composer, no
        // modal is up and 9s is long past. The two steps above are load-bearing PREDECESSORS: the
        // trust gate must run first because typing into an unanswered modal is exactly the keystroke
        // CARD-0047 refused to send, and the MinTotalWait floor must run first because before it the
        // composer accepts and silently DROPS writes — a probe typed inside that window would be
        // genuinely lost and time out on a healthy session.
        if (blocking == ClaudeStartupBlockOutcome.NotAnswerable)
        {
            // Probe SKIPPED, deliberately. That arm already announces "delivery to it is likely to
            // fail"; what it must not do is type into a modal nobody has authorised us to answer.
            _logger?.LogWarning(
                "Session {SessionId}: skipping the input-responsiveness probe because an "
                + "un-auto-answerable modal is standing — typing into it is not ours to do.",
                _terminal.SessionId);
            return true;
        }

        return await ProbeComposerInputAsync(ct);
    }

    /// <summary>
    /// The CARD-0103 gate: a full round trip through the composer (write a token, see it render,
    /// clear it) before "ready" is allowed to mean "safe to type". Every caller of
    /// <c>WaitForReadyOrThrowAsync</c> types something immediately afterwards — <c>/remote-control</c>,
    /// a card's work prompt, or a queue unblocked by the row flipping to Running — so every caller
    /// wants this. A false verdict fails the launch loudly through the existing path
    /// (<c>KillAndDisposeAsync</c>, CARD-0056), which is strictly better than the silent park inside
    /// a session everyone believes is healthy that this replaces.
    /// </summary>
    private async Task<bool> ProbeComposerInputAsync(CancellationToken ct)
    {
        if (_settings.ClaudeInputProbeTimeoutMs <= 0)
            return true;

        var result = await ComposerInputProbe.RunAsync(
            ComposerInputProbe.TokenFor(_terminal.SessionId),
            _terminal.SnapshotScreenAsync,
            _terminal.WriteAsync,
            ComposerProbeOptions.FromMilliseconds(
                _settings.ClaudeInputProbeTimeoutMs,
                _settings.ClaudeInputProbePollIntervalMs,
                _settings.ClaudeInputProbeRetypeIntervalMs,
                _settings.ClaudeInputProbeClearTimeoutMs,
                _settings.ClaudeInputProbeMaxWrites),
            message => _logger?.LogWarning(
                "Session {SessionId} input probe: {Message}", _terminal.SessionId, message),
            ct);

        if (result.Responsive)
        {
            if (result.Writes > 1 || result.Elapsed > TimeSpan.FromSeconds(5))
                _logger?.LogWarning(
                    "Session {SessionId} took {Elapsed:F1}s and {Writes} write(s) to answer the input "
                    + "probe. It IS reading now, but the TUI was deaf for most of that — anything typed "
                    + "in that window would have looked like a wedged composer.",
                    _terminal.SessionId, result.Elapsed.TotalSeconds, result.Writes);
            return true;
        }

        _logger?.LogError(
            "Session {SessionId} is NOT reading input: the probe token '{Token}' {Failure} after "
            + "{Elapsed:F1}s and {Writes} write(s). The TUI is painted but deaf; reporting the launch "
            + "as not ready rather than typing a boot prompt into it. Screen:\n{Screen}",
            _terminal.SessionId,
            result.Token,
            result.Outcome == ComposerProbeOutcome.NeverAppeared
                ? "never rendered"
                : "rendered but could not be cleared",
            result.Elapsed.TotalSeconds,
            result.Writes,
            await _terminal.SnapshotScreenAsync(ct));
        return false;
    }

    /// <summary>
    /// Answer a startup trust dialog if one is up. Only <c>TrustNotCleared</c> fails the launch — a
    /// modal we deliberately do not auto-answer is logged and allowed through, because the
    /// alternative is failing launches on a screen-shape match, and a false positive there types
    /// nothing but breaks everything. The OUTCOME is returned rather than a bool because the input
    /// probe needs to know about that arm too (CARD-0103: it must not type into it).
    /// </summary>
    private async Task<ClaudeStartupBlockOutcome> ResolveBlockingStartupPromptAsync(CancellationToken ct)
    {
        var resolution = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            _terminal.SnapshotScreenAsync,
            _terminal.WriteAsync,
            TimeSpan.FromMilliseconds(_settings.ClaudeTrustPromptSettleMs),
            ct);

        switch (resolution.Outcome)
        {
            case ClaudeStartupBlockOutcome.TrustCleared:
                _logger?.LogInformation(
                    "Session {SessionId} opened on Claude's trust dialog for an unseen working "
                    + "directory and it was answered; the session is usable. Prompt: {Title}",
                    _terminal.SessionId, resolution.Prompt?.Title);
                break;

            case ClaudeStartupBlockOutcome.TrustNotCleared:
                _logger?.LogError(
                    "Session {SessionId} is still blocked on Claude's trust dialog after answering "
                    + "it. Nothing can be delivered to this session. Prompt: {Title}",
                    _terminal.SessionId, resolution.Prompt?.Title);
                break;

            case ClaudeStartupBlockOutcome.NotAnswerable:
                // Not answered, and not treated as fatal: keying "1" into a permission modal would
                // approve a tool call nobody authorised, and refusing the launch outright would hang
                // every session the shape-match happened to hit.
                _logger?.LogWarning(
                    "Session {SessionId} appears blocked on a modal that will not be auto-answered "
                    + "({Kind}); continuing, but delivery to it is likely to fail. Prompt: {Title}",
                    _terminal.SessionId, resolution.Prompt?.Kind, resolution.Prompt?.Title);
                break;
        }

        return resolution.Outcome;
    }

    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var done = await _terminal.WaitForOutputAsync(
            text => text.Contains(IdleTitleSignal, StringComparison.Ordinal) || DonePattern.IsMatch(text),
            TimeSpan.FromMilliseconds(_settings.ClaudeDoneMaxWaitMs),
            ct);
        var raw = await _terminal.SnapshotTextAsync(ct);
        return new AgentTurnResult(
            TurnCompleted: done,
            ResponseText: ClaudeResponseAnalyzer.ExtractResponse(raw),
            IsAskingQuestion: ClaudeResponseAnalyzer.IsAskingQuestion(raw),
            RawSnapshot: raw);
    }

    public string SnapshotRawOutput()
    {
        EnsureStarted();
        return _terminal.SnapshotTextAsync(CancellationToken.None).GetAwaiter().GetResult();
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
            throw new InvalidOperationException("RunnerClaudeAdapter not started.");
    }
}
