using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerClaudeAdapter : IAgentProtocolAdapter, IAttachableProtocolAdapter
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);
    private const string IdleTitleSignal = "\x1b]0;✳";

    private readonly RunnerTerminalSession _terminal;
    private readonly AgentRegistrySettings _settings;
    private readonly DeliveryVerificationSettings _verification;
    private readonly ILogger? _logger;
    private long _promptStartSequence;
    private bool _started;
    private string? _cwd;
    private AgentLaunchBlock? _launchBlock;
    private ClaudeEffortIntent _effortIntent = new();

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
    public AgentLaunchBlock? LaunchBlock => _launchBlock;
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
        _cwd = spec.Cwd;
        _effortIntent = ClaudeEffortIntent.Read(spec.Args);
        await _terminal.StartAsync(spec, ct);
    }

    public async Task AttachAsync(Guid sessionId, CancellationToken ct)
    {
        if (_started)
            throw new InvalidOperationException("RunnerClaudeAdapter already started.");
        _started = true;
        await _terminal.AttachAsync(sessionId, ct);
    }

    public async Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) =>
        await _terminal.KillAsync(ct);

    public Task<bool> KillGenerationAsync(DateTime expectedAcceptedStartedAt, TimeSpan timeout, CancellationToken ct) =>
        _terminal.KillGenerationAsync(expectedAcceptedStartedAt, ct);

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

        // Preserve the early trust gate; effort is resolved inside the bounded post-floor phase.
        var early = ClaudeBlockingPromptDetector.Detect(await _terminal.SnapshotScreenAsync(ct));
        if (early?.Kind == ClaudeBlockingPromptKind.TrustFolder)
        {
            var trust = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
                _terminal.SnapshotScreenAsync, _terminal.WriteAsync,
                TimeSpan.FromMilliseconds(_settings.ClaudeTrustPromptSettleMs), ct);
            if (trust.Outcome is ClaudeStartupBlockOutcome.TrustNotCleared or ClaudeStartupBlockOutcome.TrustUnanswerable)
            {
                _launchBlock = new(AgentLaunchBlockKind.TrustDialogNotCleared,
                    ClaudeBlockingPromptDetector.FormatTrustDialogNotClearedReason(_cwd, early.Layout, trust.Detail));
                return false;
            }
        }
        var remaining = TimeSpan.FromMilliseconds(_settings.ClaudeReadyMinTotalWaitMs) - (DateTime.UtcNow - _terminal.StartedAt);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, ct); }
            catch (OperationCanceledException) { return false; }
        }

        var probeToken = ComposerInputProbe.TokenFor(_terminal.SessionId);
        var probeClock = System.Diagnostics.Stopwatch.StartNew();
        var result = await ClaudeStartupReadiness.RunAsync(
            probeToken,
            _terminal.SnapshotScreenAsync, _terminal.WriteAsync, _effortIntent,
            new ClaudeReadinessOptions(
                ComposerProbeOptions.FromMilliseconds(_settings.ClaudeInputProbeTimeoutMs,
                    _settings.ClaudeInputProbePollIntervalMs, _settings.ClaudeInputProbeRetypeIntervalMs,
                    _settings.ClaudeInputProbeClearTimeoutMs, _settings.ClaudeInputProbeMaxWrites),
                TimeSpan.FromMilliseconds(_settings.ClaudeTrustPromptSettleMs),
                TimeSpan.FromMilliseconds(_settings.ClaudeEffortPromptSettleMs),
                TimeSpan.FromMilliseconds(_settings.ClaudeReadyMaxWaitMs)),
            message => _logger?.LogInformation("Claude startup: {Detail}", message), Exited, ct);
        if (result.Outcome == ClaudeReadinessOutcome.EffortFailed)
            _launchBlock = new(AgentLaunchBlockKind.EffortDialogNotCleared, result.Detail);
        else if (result.Outcome == ClaudeReadinessOutcome.TrustFailed)
            _launchBlock = new(AgentLaunchBlockKind.TrustDialogNotCleared,
                ClaudeBlockingPromptDetector.FormatTrustDialogNotClearedReason(_cwd, result.TrustLayout, result.Detail));
        if (result.Outcome == ClaudeReadinessOutcome.ProbeFailed)
            _logger?.LogError(
                "Session {SessionId} is NOT reading input: the probe token '{Token}' failed ({Detail}) after "
                + "{Elapsed:F1}s and {Writes} write(s). The TUI is painted but deaf; reporting the launch "
                + "as not ready rather than typing a boot prompt into it. Screen:\n{Screen}",
                _terminal.SessionId, probeToken, result.Detail, probeClock.Elapsed.TotalSeconds, result.Writes,
                await _terminal.SnapshotScreenAsync(ct));
        else if (!result.Ready) _logger?.LogError("Claude startup failed: {Outcome}; {Detail}", result.Outcome, result.Detail);
        return result.Ready;
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
            throw new InvalidOperationException("RunnerClaudeAdapter not started.");
    }
}
