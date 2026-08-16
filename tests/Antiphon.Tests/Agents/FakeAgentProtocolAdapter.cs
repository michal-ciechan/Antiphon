using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Tests.Agents;

internal sealed class FakeAgentProtocolAdapter : IAgentProtocolAdapter
{
    // When set, StartAsync registers this adapter in the runtime under spec.SessionId — launch-path
    // tests need the adapter reachable (queue delivery, snapshots) BEFORE the launch code delivers
    // launch notes, which happens inside the launch itself.
    public AgentSessionRuntime? RegisterOnStart { get; set; }

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

    // ---- CARD-0055: what happens to a submitted prompt AFTER the Enter --------------------------
    //
    // Real Claude writes the submitted prompt into its JSONL, the tailer ingests it and the server
    // persists a UserPrompt row — that record is the ground truth a delivery is now confirmed
    // against. Harnesses wire this to insert the row so the fake models a whole delivery, not just
    // the composer half. Awaited inside SendInputAsync, so the row is committed by the time the
    // confirm loop's first poll runs.
    public Func<string, Task>? OnSubmitted { get; set; }

    // The measured ea2feb92 composer state: the Enter is SWALLOWED — the screen still redraws (so
    // "output advanced" is satisfied, which is exactly why the old verdict was wrong) but nothing
    // is submitted and the composer keeps holding the body. A later Enter pushes it in.
    public int SwallowSubmits { get; set; }

    // The measured 15c9150e shape: the first Enter submits the PREVIOUS delivery's stale body. A
    // new UserPrompt record really does appear — with the wrong text — while our body stays in the
    // composer waiting for an Enter that the old code never sent.
    public string? StaleSubmitBody { get; set; }
    private bool _staleSubmitUsed;
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

    // ---- CARD-0056: teardown ORDER, not just teardown ------------------------------------------
    //
    // A failed launch must KILL the process before disposing: DisposeAsync on the production
    // RunnerClaudeAdapter is `=> ValueTask.CompletedTask` (the agent lives in a detached pty-host),
    // so a catch that only disposed left a live agent behind a Failed row. "Kill"/"Dispose" in call
    // order; KillCount also counts the harmless second kill of an already-dead process.
    private readonly List<string> _lifecycle = [];
    public IReadOnlyList<string> Lifecycle => _lifecycle;
    public int KillCount { get; private set; }

    // Returns the exception a given prompt should fail with (null = it lands normally). Models
    // VerifiedPromptSubmitter throwing PromptDeliveryException when no composer evidence ever
    // appears for the body it typed.
    public Func<string, Exception?>? PromptFailure { get; set; }
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
        if (RegisterOnStart is not null && spec.SessionId is Guid sessionId)
            RegisterOnStart.Register(sessionId, this);
        Emit(StartupOutput);
        return Task.CompletedTask;
    }

    public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct)
    {
        Killed = true;
        KillCount++;
        _lifecycle.Add("Kill");
        if (KillResult)
            _exit.TrySetResult(ExitCode);

        return Task.FromResult(KillResult);
    }

    public Task SendPromptAsync(string prompt, CancellationToken ct)
    {
        // Injected delivery failure. Recorded in Prompts BEFORE throwing, because that is what
        // really happens: VerifiedPromptSubmitter types the body and only then fails to find
        // composer evidence for it (CARD-0056: a 15-char "/remote-control" that never surfaced).
        if (PromptFailure?.Invoke(prompt) is { } failure)
        {
            SentPrompt = prompt;
            _prompts.Add(prompt);
            throw failure;
        }

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

    // Every body actually SUBMITTED (composer content at the moment a lone "\r" landed), in order.
    // Batching tests assert the delivered body's shape here — Inputs can't, because a batched
    // delivery is one multi-line write whose composer content is what matters.
    // Bracketed-paste markers (\e[200~..\e[201~) are stripped from the composer: real TUIs treat
    // them as control sequences, not typed text. Inputs/SentInput still record the raw wire bytes
    // so Multi-line delivery-wrap tests can pin the markers.
    private readonly List<string> _submittedBodies = [];
    public IReadOnlyList<string> SubmittedBodies => _submittedBodies;

    public async Task SendInputAsync(string input, CancellationToken ct)
    {
        SentInput += input;
        _inputs.Add(input);
        if (input == "\r")
        {
            // Ordered so the two can compose: StaleSubmitBody claims the FIRST Enter (the measured
            // 15c9150e shape), SwallowSubmits then eats the re-presses behind it.
            if (StaleSubmitBody is { } stale && !_staleSubmitUsed)
            {
                // Someone else's body goes in; ours stays in the composer.
                _staleSubmitUsed = true;
                _submittedBodies.Add(stale);
                if (SubmitAck.Length > 0)
                    Emit(SubmitAck);
                if (OnSubmitted is { } recordStale)
                    await recordStale(stale);
                return;
            }

            if (SwallowSubmits > 0)
            {
                // Swallowed: the screen redraws, the composer keeps the body, nothing is submitted.
                SwallowSubmits--;
                if (SubmitAck.Length > 0)
                    Emit(SubmitAck);
                return;
            }

            var submitted = _composer.ToString();
            _composer.Clear();
            if (SubmitAck.Length > 0)
                Emit(SubmitAck);
            // Enter on an EMPTY composer is a no-op — the contract the whole Enter-only re-press
            // design leans on (slice 4 pins it against real Claude). Nothing submitted, no record.
            if (submitted.Length == 0)
                return;

            _submittedBodies.Add(submitted);
            if (OnSubmitted is { } record)
                await record(submitted);
        }
        else if (EchoTypedInputToScreen)
        {
            // DeliverAsync wraps multi-line bodies in bracketed paste; the markers never appear
            // as composer text on a real TUI, so strip them before echoing.
            _composer.Append(StripBracketedPasteMarkers(input));
        }
    }

    private static string StripBracketedPasteMarkers(string input) =>
        input.Replace("\x1b[200~", string.Empty, StringComparison.Ordinal)
             .Replace("\x1b[201~", string.Empty, StringComparison.Ordinal);

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
        _lifecycle.Add("Dispose");
        return ValueTask.CompletedTask;
    }
}
