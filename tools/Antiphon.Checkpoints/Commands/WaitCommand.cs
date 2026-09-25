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
    private readonly RunStateStore _store;
    private readonly IProcessLiveness _liveness;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public WaitCommand(
        RunStateStore? store = null,
        IProcessLiveness? liveness = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _store = store ?? new RunStateStore();
        _liveness = liveness ?? new ProcessLiveness();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public async Task<int> WaitAsync(
        string runDirectory,
        TimeSpan? maxWait,
        TimeSpan heartbeat,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(runDirectory, "state.json");
        var reportPath = Path.Combine(runDirectory, "report.md");
        var logPath = Path.Combine(runDirectory, "executor.log");
        var started = _clock();
        var nextBeat = started;
        while (true)
        {
            var now = _clock();
            var state = _store.TryRead(statePath);
            if (state?.Phase == "done")
            {
                if (File.Exists(reportPath))
                    output.Write(File.ReadAllText(reportPath));
                else
                    output.WriteLine(state.Heartbeat(now));
                return state.ExitCode ?? ExitCodes.Green;
            }

            if (state is not null && state.Phase != "done" && state.ExecutorPid > 0 && !_liveness.IsAlive(state.ExecutorPid))
            {
                output.WriteLine("executor died without phase=done");
                output.WriteLine(Tail(logPath));
                return ExitCodes.ExecutorCrashed;
            }

            if (now >= nextBeat && state is not null)
            {
                output.WriteLine(state.Heartbeat(now));
                nextBeat = now + heartbeat;
            }

            if (maxWait is TimeSpan limit && now - started >= limit)
            {
                output.WriteLine(state?.Heartbeat(now) ?? "HEARTBEAT run=? elapsed=0m00s |");
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

    private static string Tail(string path)
    {
        if (!File.Exists(path))
            return "";
        var lines = File.ReadAllLines(path);
        return string.Join('\n', lines.TakeLast(40));
    }
}
