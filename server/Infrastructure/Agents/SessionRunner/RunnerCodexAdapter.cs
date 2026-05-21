using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerCodexAdapter : IAgentProtocolAdapter
{
    private readonly RunnerTerminalSession _terminal;
    private readonly AgentRegistrySettings _settings;
    private long _promptStartSequence;
    private string? _lastPrompt;
    private bool _acceptedTrustPrompt;
    private bool _started;

    public RunnerCodexAdapter(ISessionRunnerClient client, IOptions<AgentRegistrySettings> options)
    {
        _terminal = new RunnerTerminalSession(client);
        _settings = options.Value;
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

    public async Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        EnsureStarted();
        await _terminal.ClearLiveBufferAsync(ct);
        _lastPrompt = prompt;
        _promptStartSequence = await _terminal.GetLastSequenceAsync(ct);
        await _terminal.SendLineAsync(prompt, ct);
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
        var quietPeriod = TimeSpan.FromMilliseconds(_settings.CodexReadyQuietPeriodMs);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(_settings.CodexReadyMaxWaitMs);
        var lastSequence = await _terminal.GetLastSequenceAsync(ct);
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            await AcceptTrustPromptIfVisibleAsync(ct);

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }

            var currentSequence = await _terminal.GetLastSequenceAsync(ct);
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

    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var done = await _terminal.WaitForQuietAsync(
            TimeSpan.FromMilliseconds(_settings.CodexDoneQuietPeriodMs),
            TimeSpan.FromMilliseconds(_settings.CodexDoneMaxWaitMs),
            ct);
        var raw = await _terminal.SnapshotTextAsync(ct);
        return new AgentTurnResult(
            TurnCompleted: done,
            ResponseText: CodexResponseAnalyzer.ExtractResponse(raw, _lastPrompt),
            IsAskingQuestion: CodexResponseAnalyzer.IsAskingQuestion(raw, _lastPrompt),
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
