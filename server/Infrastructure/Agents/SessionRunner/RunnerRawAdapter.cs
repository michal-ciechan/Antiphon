using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerRawAdapter : IAgentProtocolAdapter
{
    private static readonly TimeSpan ReadyGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TurnQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TurnMaxWait = TimeSpan.FromSeconds(60);

    private readonly RunnerTerminalSession _terminal;
    private long _promptStartSequence;
    private bool _started;

    public RunnerRawAdapter(ISessionRunnerClient client)
    {
        _terminal = new RunnerTerminalSession(client);
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
            throw new InvalidOperationException("RunnerRawAdapter already started.");
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
        await _terminal.WaitForOutputAsync(_ => true, ReadyGrace, ct);
        return true;
    }

    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var quiet = await _terminal.WaitForQuietAsync(TurnQuietPeriod, TurnMaxWait, ct);
        var raw = await _terminal.SnapshotTextAsync(ct);
        return new AgentTurnResult(
            TurnCompleted: quiet,
            ResponseText: raw,
            IsAskingQuestion: false,
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
            throw new InvalidOperationException("RunnerRawAdapter not started.");
    }
}
