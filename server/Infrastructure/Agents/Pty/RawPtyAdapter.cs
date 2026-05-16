using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

/// <summary>
/// Passthrough adapter for raw consoles (cmd, pwsh, bash). No protocol-aware
/// detection: ready = first chunk arrives or 500 ms grace; turn complete = 2 s
/// of quiet output (max 60 s). Response text is the verbatim live snapshot.
/// </summary>
public sealed class RawPtyAdapter : IAgentProtocolAdapter
{
    private static readonly TimeSpan ReadyGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TurnQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TurnMaxWait = TimeSpan.FromSeconds(60);

    private readonly PtyAgentRunner _runner = new();
    private TaskCompletionSource? _firstPromptOutput;
    private bool _started;

    public Task<int> Exited => _runner.Exited;
    public int? Pid => _runner.Pid;
    public AgentExitReason ExitReason => MapExitReason(_runner.ExitReason);
    public string? AuditDirectory => _runner.AuditDirectory;

    public event Action<string>? OnTextDelta;

    public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (_started) throw new InvalidOperationException("RawPtyAdapter already started.");
        _started = true;

        _runner.OnData += ForwardData;

        await _runner.StartAsync(
            app: spec.Exe,
            commandLine: spec.Args.ToArray(),
            cwd: spec.Cwd,
            env: spec.Env.ToDictionary(kv => kv.Key, kv => kv.Value),
            cols: spec.Cols,
            rows: spec.Rows,
            memoryLimitMb: spec.MemoryLimitMb,
            ct: ct);
    }

    public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct)
        => _runner.KillAsync(timeout);

    public async Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        EnsureStarted();
        _runner.ClearLiveBuffer();
        _firstPromptOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _runner.SendLineAsync(prompt, ct);
    }

    public async Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct)
    {
        EnsureStarted();
        var firstPromptOutput = _firstPromptOutput
            ?? throw new InvalidOperationException("No prompt has been sent.");
        var delay = Task.Delay(timeout, ct);
        var completed = await Task.WhenAny(firstPromptOutput.Task, delay);
        if (completed == delay)
            ct.ThrowIfCancellationRequested();

        return completed == firstPromptOutput.Task;
    }

    public async Task SendInputAsync(string input, CancellationToken ct)
    {
        EnsureStarted();
        await _runner.WriteAsync(input, ct);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken ct)
    {
        EnsureStarted();
        ct.ThrowIfCancellationRequested();
        _runner.Resize(cols, rows);
        return Task.CompletedTask;
    }

    public async Task<bool> WaitForReadyAsync(CancellationToken ct)
    {
        EnsureStarted();
        // First chunk arrives -> ready immediately. Otherwise grace period
        // elapses -> still ready (raw shells may have nothing to say).
        await _runner.WaitForOutputAsync(_ => true, ReadyGrace, ct);
        return true;
    }

    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var quiet = await _runner.WaitForQuietAsync(TurnQuietPeriod, TurnMaxWait, ct);
        var raw = _runner.SnapshotText();
        return new AgentTurnResult(
            TurnCompleted: quiet,
            ResponseText: raw,
            IsAskingQuestion: false,
            RawSnapshot: raw);
    }

    public string SnapshotRawOutput()
    {
        EnsureStarted();
        return _runner.SnapshotText();
    }

    public string SnapshotRenderedScreen()
    {
        EnsureStarted();
        return _runner.SnapshotScreen();
    }

    public async ValueTask DisposeAsync()
    {
        _runner.OnData -= ForwardData;
        await _runner.DisposeAsync();
    }

    private void EnsureStarted()
    {
        if (!_started) throw new InvalidOperationException("RawPtyAdapter not started.");
    }

    private void ForwardData(string chunk)
    {
        _firstPromptOutput?.TrySetResult();
        OnTextDelta?.Invoke(chunk);
    }

    private static AgentExitReason MapExitReason(PtyExitReason reason) => reason switch
    {
        PtyExitReason.ProcessExited => AgentExitReason.ProcessExited,
        PtyExitReason.KilledByRequest => AgentExitReason.KilledByRequest,
        PtyExitReason.MemoryKilled => AgentExitReason.MemoryKilled,
        _ => AgentExitReason.Unknown
    };
}
