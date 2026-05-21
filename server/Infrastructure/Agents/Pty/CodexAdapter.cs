using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

/// <summary>
/// Adapter for the Codex TUI. Codex is driven like Claude through a PTY, but
/// uses quiet-period turn boundaries because the TUI does not expose a stable
/// positive done marker through raw terminal output.
/// </summary>
public sealed class CodexAdapter : IAgentProtocolAdapter
{
    private readonly PtyAgentRunner _runner = new();
    private readonly CodexReadyDetector _readyDetector;
    private readonly CodexDoneDetector _doneDetector;
    private TaskCompletionSource? _firstPromptOutput;
    private string? _lastPrompt;
    private bool _acceptedTrustPrompt;
    private bool _started;

    public CodexAdapter(IOptions<AgentRegistrySettings> options)
    {
        var settings = options.Value;
        _readyDetector = new CodexReadyDetector
        {
            QuietPeriod = TimeSpan.FromMilliseconds(settings.CodexReadyQuietPeriodMs),
            MaxWait = TimeSpan.FromMilliseconds(settings.CodexReadyMaxWaitMs),
        };
        _doneDetector = new CodexDoneDetector
        {
            QuietPeriod = TimeSpan.FromMilliseconds(settings.CodexDoneQuietPeriodMs),
            MaxWait = TimeSpan.FromMilliseconds(settings.CodexDoneMaxWaitMs),
        };
    }

    public Task<int> Exited => _runner.Exited;
    public int? Pid => _runner.Pid;
    public AgentExitReason ExitReason => MapExitReason(_runner.ExitReason);
    public string? AuditDirectory => _runner.AuditDirectory;

    public event Action<string>? OnTextDelta;

    public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (_started) throw new InvalidOperationException("CodexAdapter already started.");
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
        _lastPrompt = prompt;
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
        return _readyDetector.WaitAsync(_runner, AcceptTrustPromptIfVisibleAsync, ct);
    }

    public async Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        EnsureStarted();
        var done = await _doneDetector.WaitAsync(_runner, ct);
        var raw = _runner.SnapshotText();
        return new AgentTurnResult(
            TurnCompleted: done,
            ResponseText: CodexResponseAnalyzer.ExtractResponse(raw, _lastPrompt),
            IsAskingQuestion: CodexResponseAnalyzer.IsAskingQuestion(raw, _lastPrompt),
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
        if (!_started) throw new InvalidOperationException("CodexAdapter not started.");
    }

    private void ForwardData(string chunk)
    {
        _firstPromptOutput?.TrySetResult();
        OnTextDelta?.Invoke(chunk);
    }

    private async Task AcceptTrustPromptIfVisibleAsync(CancellationToken ct)
    {
        if (_acceptedTrustPrompt)
            return;

        if (!CodexTrustPromptDetector.IsVisible(_runner.SnapshotText(), _runner.SnapshotScreen()))
            return;

        _acceptedTrustPrompt = true;
        await _runner.WriteAsync("\r", ct);
    }

    private static AgentExitReason MapExitReason(PtyExitReason reason) => reason switch
    {
        PtyExitReason.ProcessExited => AgentExitReason.ProcessExited,
        PtyExitReason.KilledByRequest => AgentExitReason.KilledByRequest,
        PtyExitReason.MemoryKilled => AgentExitReason.MemoryKilled,
        _ => AgentExitReason.Unknown
    };
}
