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
        if (!await ClearBlockingStartupPromptAsync(ct))
            return false;

        var remaining = TimeSpan.FromMilliseconds(_settings.ClaudeReadyMinTotalWaitMs)
            - (DateTime.UtcNow - _terminal.StartedAt);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return true;
    }

    /// <summary>
    /// Answer a startup trust dialog if one is up. Returns false only when the session is verifiably
    /// still blocked on one after being answered — a modal we deliberately do not auto-answer is
    /// logged and allowed through, because the alternative is failing launches on a screen-shape
    /// match, and a false positive there types nothing but breaks everything.
    /// </summary>
    private async Task<bool> ClearBlockingStartupPromptAsync(CancellationToken ct)
    {
        var resolution = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            _terminal.SnapshotScreenAsync,
            _terminal.WriteAsync,
            TimeSpan.FromMilliseconds(_settings.ClaudeTrustPromptSettleMs),
            ct);

        switch (resolution.Outcome)
        {
            case ClaudeStartupBlockOutcome.None:
                return true;

            case ClaudeStartupBlockOutcome.TrustCleared:
                _logger?.LogInformation(
                    "Session {SessionId} opened on Claude's trust dialog for an unseen working "
                    + "directory and it was answered; the session is usable. Prompt: {Title}",
                    _terminal.SessionId, resolution.Prompt?.Title);
                return true;

            case ClaudeStartupBlockOutcome.TrustNotCleared:
                _logger?.LogError(
                    "Session {SessionId} is still blocked on Claude's trust dialog after answering "
                    + "it. Nothing can be delivered to this session. Prompt: {Title}",
                    _terminal.SessionId, resolution.Prompt?.Title);
                return false;

            default:
                // Not answered, and not treated as fatal: keying "1" into a permission modal would
                // approve a tool call nobody authorised, and refusing the launch outright would hang
                // every session the shape-match happened to hit.
                _logger?.LogWarning(
                    "Session {SessionId} appears blocked on a modal that will not be auto-answered "
                    + "({Kind}); continuing, but delivery to it is likely to fail. Prompt: {Title}",
                    _terminal.SessionId, resolution.Prompt?.Kind, resolution.Prompt?.Title);
                return true;
        }
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
