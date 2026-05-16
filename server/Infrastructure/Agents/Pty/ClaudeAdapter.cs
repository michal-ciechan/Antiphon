using Microsoft.Extensions.Options;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

/// <summary>
/// Adapter for the Claude Code TUI. Wraps <see cref="ClaudeReadyDetector"/> and
/// <see cref="ClaudeCrunchedDetector"/> from the PTY library; falls back to
/// <see cref="ClaudeDoneDetector"/> behaviour when the crunched signal does not
/// land in the configured budget. Response text is post-processed by
/// <see cref="ClaudeResponseAnalyzer"/>.
/// </summary>
public sealed class ClaudeAdapter : IAgentProtocolAdapter
{
    private readonly PtyAgentRunner _runner = new();
    private readonly AgentRegistrySettings _settings;
    private readonly ClaudeReadyDetector _readyDetector;
    private readonly ClaudeCrunchedDetector _crunchedDetector;
    private TaskCompletionSource? _firstPromptOutput;
    private bool _started;

    public ClaudeAdapter(IOptions<AgentRegistrySettings> options)
    {
        _settings = options.Value;
        _readyDetector = new ClaudeReadyDetector
        {
            QuietPeriod = TimeSpan.FromMilliseconds(_settings.ClaudeReadyQuietPeriodMs),
            MaxWait = TimeSpan.FromMilliseconds(_settings.ClaudeReadyMaxWaitMs),
        };
        _crunchedDetector = new ClaudeCrunchedDetector
        {
            MaxWait = TimeSpan.FromMilliseconds(_settings.ClaudeDoneMaxWaitMs),
        };
    }

    public Task<int> Exited => _runner.Exited;
    public int? Pid => _runner.Pid;
    public AgentExitReason ExitReason => MapExitReason(_runner.ExitReason);
    public string? AuditDirectory => _runner.AuditDirectory;

    public event Action<string>? OnTextDelta;

    public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (_started) throw new InvalidOperationException("ClaudeAdapter already started.");
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
        // Mandatory per E01 findings: CrunchedDetector matches on prior-turn signals
        // if the buffer isn't reset between sends.
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

    public Task<bool> WaitForReadyAsync(CancellationToken ct)
    {
        EnsureStarted();
        return _readyDetector.WaitAsync(_runner, ct);
    }

    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var done = await _crunchedDetector.WaitAsync(_runner, ct);
        var raw = _runner.SnapshotText();
        return new AgentTurnResult(
            TurnCompleted: done,
            ResponseText: ClaudeResponseAnalyzer.ExtractResponse(raw),
            IsAskingQuestion: ClaudeResponseAnalyzer.IsAskingQuestion(raw),
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
        if (!_started) throw new InvalidOperationException("ClaudeAdapter not started.");
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
