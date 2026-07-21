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

    // Composer simulation for delivery verification (ComposerDeliveryEvidence): typed input is
    // echoed into the rendered screen like a real TUI composer, and a lone "\r" clears it and
    // emits SubmitAck (advancing the output sequence, like a real submit redraw). Turn either off
    // to simulate a WEDGED terminal: EchoTypedInputToScreen=false → typed text never appears;
    // SubmitAck="" → the submitting Enter produces no output.
    public bool EchoTypedInputToScreen { get; set; } = true;
    public string SubmitAck { get; set; } = "\n";
    private readonly StringBuilder _composer = new();
    public int PromptOutputDelayMs { get; set; } = 10;
    public bool ReadyResult { get; set; } = true;
    public bool TurnCompleted { get; set; } = true;
    public bool ThrowOnRenderedSnapshot { get; set; }
    public string SentInput { get; private set; } = string.Empty;
    private readonly List<string> _inputs = [];
    // Every SendInputAsync call, in order — lets tests assert the SHAPE of delivery, not just the
    // concatenated bytes. Critical for the queue's two-write submit (body, then a separate "\r"):
    // SentInput alone can't distinguish one write of "body\r" from two writes "body" + "\r".
    public IReadOnlyList<string> Inputs => _inputs;
    public string SentPrompt { get; private set; } = string.Empty;
    private readonly List<string> _prompts = [];
    // Every prompt sent, in order — lets tests assert the /rename + /remote-control sequence
    // that precedes the work prompt. SentPrompt still holds the last prompt for older tests.
    public IReadOnlyList<string> Prompts => _prompts;
    public IReadOnlyList<string> StartedArgs { get; private set; } = [];
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int MemoryLimitMb { get; private set; }
    public bool Started { get; private set; }
    public bool Killed { get; private set; }
    public bool Disposed { get; private set; }
    public bool KillResult { get; set; } = true;
    // When set, StartAsync throws this — simulates a spawn failure (missing exe, runner 500) so
    // tests can assert the launch-failure paths (session Failed + agent rolled back from Working).
    public Exception? ThrowOnStart { get; set; }

    public event Action<string>? OnTextDelta;

    public Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (ThrowOnStart is not null)
            throw ThrowOnStart;

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
        _prompts.Add(prompt);
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
        _inputs.Add(input);
        if (input == "\r")
        {
            _composer.Clear();
            if (SubmitAck.Length > 0)
                Emit(SubmitAck);
        }
        else if (EchoTypedInputToScreen)
        {
            _composer.Append(input);
        }
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

        var screen = RenderedScreenOverride ?? _rawOutput.ToString();
        return _composer.Length > 0 ? screen + "\n> " + _composer : screen;
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
