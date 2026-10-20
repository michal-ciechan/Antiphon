using Antiphon.HostCleanup.Checkpoints;
namespace Antiphon.Checkpoints;

public interface IProcessLiveness
{
    bool IsAlive(int pid);
}

public sealed class ProcessLiveness : IProcessLiveness
{
    public bool IsAlive(int pid)
    {
        if (pid <= 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class WaitCommand
{
    private readonly CheckpointCleanupContext _context;
    private readonly RunStateStore _store;
    private readonly IProcessLiveness _liveness;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ProcessIdentityProbe _probe;

    public WaitCommand(
        RunStateStore? store = null,
        IProcessLiveness? liveness = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ProcessIdentityProbe? probe = null, CheckpointCleanupContext? context = null)
    {
        _context = context ?? CheckpointCleanupDefaults.Context;
        _store = store ?? new RunStateStore(_context);
        _liveness = liveness ?? new ProcessLiveness();
        _clock = clock ?? (() => _context.Time.GetUtcNow());
        _delay = delay ?? ((span, token) => Task.Delay(span, _context.Time, token));
        _probe = probe ?? new ProcessIdentityProbe(_context);
    }

    public async Task<int> WaitAsync(
        string runDirectory,
        TimeSpan? maxWait,
        TimeSpan heartbeat,
        TextWriter output,
        CancellationToken cancellationToken,
        string? expectedSourceSha = null)
    {
        var statePath = Path.Combine(runDirectory, "state.json");
        var reportPath = Path.Combine(runDirectory, "report.md");
        var logPath = Path.Combine(runDirectory, "executor.log");
        var started = _clock();
        var nextBeat = started;
        var cleanup = new ToolCopyCleanup(_probe, context: _context);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _clock();
            var state = _store.TryRead(statePath, out var stateRefusal, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (stateRefusal is not null && (stateRefusal.Contains("not regular", StringComparison.Ordinal) || stateRefusal.Contains("nonregular", StringComparison.Ordinal)
                || stateRefusal.Contains("receipt-identity:", StringComparison.Ordinal)))
            { output.WriteLine("refused: " + stateRefusal); return ExitCodes.Invalid; }
            var observed = cleanup.ObserveExecutor(runDirectory, cancellationToken);
            // Finish publishes phase=done after the "done exit=" log line, and the
            // process can exit in that window. Re-read once death is observed.
            if (observed.Verdict == ProcessVerdict.Dead && state?.Phase != "done")
                state = _store.TryRead(statePath, out _, cancellationToken);
            if (state?.Phase == "done" && observed.Verdict == ProcessVerdict.Dead)
            {
                var required = expectedSourceSha is not null && state.Source?.Start?.Commit != expectedSourceSha
                    ? "receipt-source-SHA-mismatching" : ReportWriter.RequiredReceiptRefusal(_context, runDirectory, state, cancellationToken);
                if (required is not null)
                {
                    output.WriteLine("refused: " + required);
                    return ExitCodes.Invalid;
                }
                var receipt = cleanup.Remove(runDirectory, cancellationToken);
                output.WriteLine(receipt);
                if (receipt.Outcome == "Refused")
                    output.WriteLine("refused: " + receipt.Reason);
                cancellationToken.ThrowIfCancellationRequested();
                if (_context.Io.FileExists(reportPath))
                    output.Write(ReceiptFile.ReadText(_context, reportPath, cancellationToken));
                else
                    output.WriteLine(state.Heartbeat(now));
                var exit = state.ExitCode!.Value;
                if (!receipt.Removed && exit == ExitCodes.Green)
                    return ExitCodes.Invalid;
                if (exit == ExitCodes.Green && _context.Io.FileExists(reportPath) && ReportRefused(reportPath, cancellationToken))
                    return ExitCodes.Invalid;
                return exit;
            }

            if (state?.Phase != "done" && observed.Verdict == ProcessVerdict.Dead)
            {
                var diedReceipt = cleanup.Remove(runDirectory, cancellationToken);
                output.WriteLine(diedReceipt);
                if (diedReceipt.Outcome == "Refused")
                    output.WriteLine("refused: " + diedReceipt.Reason);
                cancellationToken.ThrowIfCancellationRequested();
                output.WriteLine("executor died without phase=done");
                output.WriteLine(Tail(logPath, cancellationToken));
                return ExitCodes.ExecutorCrashed;
            }

            if (now >= nextBeat && state is not null)
            {
                WriteHeartbeat(output, state.Heartbeat(now));
                nextBeat = now + heartbeat;
            }

            if (maxWait is TimeSpan limit && now - started >= limit)
            {
                var beat = state?.Heartbeat(now);
                if (string.IsNullOrWhiteSpace(beat))
                {
                    var elapsed = state is null
                        ? "0m00s"
                        : RunState.FormatDuration((now - state.StartedAt).TotalSeconds);
                    var runId = string.IsNullOrWhiteSpace(state?.RunId) ? "?" : state!.RunId;
                    beat = $"HEARTBEAT run={runId} elapsed={elapsed}";
                }

                WriteHeartbeat(output, beat);
                output.WriteLine("STILL RUNNING exit=75");
                return ExitCodes.StillRunning;
            }

            var slice = TimeSpan.FromMilliseconds(200);
            if (maxWait is TimeSpan remainingLimit)
            {
                var left = remainingLimit - (now - started);
                if (left < slice)
                    slice = left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1);
            }

            await _delay(slice, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ReportRefused(string reportPath, CancellationToken cancellationToken)
    {
        foreach (var line in ReceiptFile.ReadText(_context, reportPath, cancellationToken).Split('\n'))
        {
            if (line.StartsWith("refused:", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void WriteHeartbeat(TextWriter output, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        output.WriteLine(line.Trim());
    }

    private string Tail(string path, CancellationToken cancellationToken)
    {
        if (!_context.Io.FileExists(path))
            return "";
        using var reader = new StringReader(ReceiptFile.ReadText(_context, path, cancellationToken));
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return string.Join('\n', lines.TakeLast(40));
    }
}
