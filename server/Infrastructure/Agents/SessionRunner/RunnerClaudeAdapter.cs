using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerClaudeAdapter : IAgentProtocolAdapter
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);
    private const string IdleTitleSignal = "\x1b]0;✳";

    private readonly RunnerTerminalSession _terminal;
    private readonly AgentRegistrySettings _settings;
    private long _promptStartSequence;
    private bool _started;

    public RunnerClaudeAdapter(ISessionRunnerClient client, IOptions<AgentRegistrySettings> options)
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
        var quiet = await _terminal.WaitForQuietAsync(
            TimeSpan.FromMilliseconds(_settings.ClaudeReadyQuietPeriodMs),
            TimeSpan.FromMilliseconds(_settings.ClaudeReadyMaxWaitMs),
            ct);
        if (!quiet)
            return false;

        var remaining = TimeSpan.FromSeconds(9) - (DateTime.UtcNow - _terminal.StartedAt);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return true;
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
