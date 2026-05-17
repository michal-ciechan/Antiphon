using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.Agents;

internal sealed class FakeAgentProtocolAdapter : IAgentProtocolAdapter
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _rawOutput = new();
    private TaskCompletionSource _firstPromptOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<int> Exited => _exit.Task;
    public int? Pid => 1234;
    public AgentExitReason ExitReason { get; set; } = AgentExitReason.Unknown;
    public int ExitCode { get; set; }
    public string? AuditDirectory => null;
    public string StartupOutput { get; set; } = string.Empty;
    public string NoiseDuringSendPrompt { get; set; } = string.Empty;
    public string PromptOutput { get; set; } = string.Empty;
    public string? RenderedScreenOverride { get; set; }
    public int PromptOutputDelayMs { get; set; } = 10;
    public bool ReadyResult { get; set; } = true;
    public bool TurnCompleted { get; set; } = true;
    public bool ThrowOnRenderedSnapshot { get; set; }
    public string SentInput { get; private set; } = string.Empty;
    public string SentPrompt { get; private set; } = string.Empty;
    public IReadOnlyList<string> StartedArgs { get; private set; } = [];
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int MemoryLimitMb { get; private set; }
    public bool Started { get; private set; }
    public bool Killed { get; private set; }
    public bool Disposed { get; private set; }
    public bool KillResult { get; set; } = true;

    public event Action<string>? OnTextDelta;

    public Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        Started = true;
        StartedArgs = spec.Args.ToArray();
        Cols = spec.Cols;
        Rows = spec.Rows;
        MemoryLimitMb = spec.MemoryLimitMb;
        Emit(StartupOutput);
        return Task.CompletedTask;
    }

    public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct)
    {
        Killed = true;
        if (KillResult)
            _exit.TrySetResult(ExitCode);

        return Task.FromResult(KillResult);
    }

    public Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        _firstPromptOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Emit(NoiseDuringSendPrompt);
        SentPrompt = prompt;
        if (!string.IsNullOrEmpty(PromptOutput))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(0, PromptOutputDelayMs), ct);
                    Emit(PromptOutput);
                    _firstPromptOutput.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    _firstPromptOutput.TrySetCanceled(ct);
                }
            }, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public async Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct)
    {
        var delay = Task.Delay(timeout, ct);
        var completed = await Task.WhenAny(_firstPromptOutput.Task, delay);
        if (completed == delay)
            ct.ThrowIfCancellationRequested();

        return completed == _firstPromptOutput.Task && _firstPromptOutput.Task.IsCompletedSuccessfully;
    }

    public Task SendInputAsync(string input, CancellationToken ct)
    {
        SentInput += input;
        return Task.CompletedTask;
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken ct)
    {
        Cols = cols;
        Rows = rows;
        return Task.CompletedTask;
    }

    public Task<bool> WaitForReadyAsync(CancellationToken ct) => Task.FromResult(ReadyResult);

    public Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct)
    {
        if (ExitReason != AgentExitReason.Unknown)
            _exit.TrySetResult(ExitCode);

        return Task.FromResult(new AgentTurnResult(TurnCompleted, null, false, SnapshotRawOutput()));
    }

    public string SnapshotRawOutput() => _rawOutput.ToString();

    public string SnapshotRenderedScreen()
    {
        if (ThrowOnRenderedSnapshot)
            throw new InvalidOperationException("Rendered snapshot is not available.");

        return RenderedScreenOverride ?? _rawOutput.ToString();
    }

    public void Emit(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _rawOutput.Append(text);
        OnTextDelta?.Invoke(text);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
