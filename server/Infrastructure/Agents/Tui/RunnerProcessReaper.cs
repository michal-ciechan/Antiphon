using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Antiphon.Server.Infrastructure.Agents.Tui;

public sealed class RunnerProcessReaper : IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private readonly ConcurrentDictionary<Guid, RunnerProcessStartGuard> _tracked = new();
    private readonly Func<Process, CancellationToken, Task<bool>> _stopTreeAsync;
    private readonly object _lifecycleGate = new();
    private bool _stopping;
    private int _activeAdmissions;
    private TaskCompletionSource? _admissionsDrained;
    private Task? _shutdownTask;

    public RunnerProcessReaper()
        : this(RunnerProcessCleanup.StopTreeAsync)
    {
    }

    internal RunnerProcessReaper(
        Func<Process, CancellationToken, Task<bool>> stopTreeAsync)
    {
        _stopTreeAsync = stopTreeAsync;
    }

    internal int TrackedProcessCount => _tracked.Count;

    internal IDisposable? TryAdmitProbe()
    {
        lock (_lifecycleGate)
        {
            if (_stopping)
                return null;
            _activeAdmissions++;
            return new ProbeAdmission(this);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task shutdownTask;
        lock (_lifecycleGate)
        {
            _stopping = true;
            if (_shutdownTask is not null)
            {
                shutdownTask = _shutdownTask;
            }
            else
            {
                Task admissionsDrained;
                if (_activeAdmissions == 0)
                {
                    admissionsDrained = Task.CompletedTask;
                }
                else
                {
                    _admissionsDrained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    admissionsDrained = _admissionsDrained.Task;
                }
                shutdownTask = StopAdmittedProcessesAsync(admissionsDrained);
                _shutdownTask = shutdownTask;
            }
        }

        await shutdownTask.WaitAsync(cancellationToken);
    }

    private async Task StopAdmittedProcessesAsync(Task admissionsDrained)
    {
        await admissionsDrained;
        var tracked = _tracked.ToArray();
        foreach (var entry in tracked)
            entry.Value.StopOrPreventStart();

        while (tracked.Any(entry =>
                   _tracked.TryGetValue(entry.Key, out var current)
                   && ReferenceEquals(current, entry.Value)))
        {
            foreach (var entry in tracked)
                entry.Value.StopOrPreventStart();
            await Task.Delay(RetryDelay);
        }
    }

    private void ReleaseProbeAdmission()
    {
        TaskCompletionSource? drained = null;
        lock (_lifecycleGate)
        {
            _activeAdmissions--;
            if (_stopping && _activeAdmissions == 0)
                drained = _admissionsDrained;
        }
        drained?.TrySetResult();
    }

    internal Guid Register(RunnerProcessStartGuard process)
    {
        var trackingId = Guid.NewGuid();
        if (!_tracked.TryAdd(trackingId, process))
            throw new InvalidOperationException("The probe process could not be tracked for cleanup.");
        return trackingId;
    }

    internal void Release(Guid trackingId)
    {
        if (_tracked.TryRemove(trackingId, out var process))
            process.Dispose();
    }

    internal void AdoptStarted(Guid trackingId)
    {
        if (_tracked.TryGetValue(trackingId, out var process))
            _ = ReapAsync(trackingId, process);
    }

    internal void AdoptPendingStart(Guid trackingId, Task<bool> startTask)
    {
        if (_tracked.TryGetValue(trackingId, out var process))
            _ = ReapPendingStartAsync(trackingId, process, startTask);
    }

    internal async Task WaitForEmptyAsync(TimeSpan timeout)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!_tracked.IsEmpty)
        {
            if (Stopwatch.GetElapsedTime(startedAt) >= timeout)
                throw new TimeoutException("Timed out while waiting for runner process cleanup.");
            await Task.Delay(RetryDelay);
        }
    }

    private async Task ReapAsync(Guid trackingId, RunnerProcessStartGuard process)
    {
        try
        {
            while (!process.IsSettled)
            {
                var cleanupConfirmed = false;
                try
                {
                    cleanupConfirmed = await _stopTreeAsync(process.Process, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Retain ownership and retry after a bounded delay.
                }

                if (cleanupConfirmed || process.IsSettled)
                    break;
                await Task.Delay(RetryDelay);
            }
        }
        finally
        {
            _tracked.TryRemove(trackingId, out _);
            process.Dispose();
        }
    }

    private async Task ReapPendingStartAsync(
        Guid trackingId,
        RunnerProcessStartGuard process,
        Task<bool> startTask)
    {
        try
        {
            var started = false;
            try
            {
                started = await startTask;
            }
            catch (Exception)
            {
                started = process.HasStarted;
            }

            if (!started)
                return;
            while (!process.IsSettled)
            {
                var cleanupConfirmed = false;
                try
                {
                    cleanupConfirmed = await _stopTreeAsync(process.Process, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Retain ownership and retry after a bounded delay.
                }

                if (cleanupConfirmed || process.IsSettled)
                    break;
                await Task.Delay(RetryDelay);
            }
        }
        finally
        {
            _tracked.TryRemove(trackingId, out _);
            process.Dispose();
        }
    }

    private sealed class ProbeAdmission(RunnerProcessReaper owner) : IDisposable
    {
        private RunnerProcessReaper? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseProbeAdmission();
        }
    }
}

internal sealed class RunnerProcessStartGuard : IDisposable
{
    private readonly object _gate = new();
    private readonly Action? _startCommitted;
    private StartState _state;
    private bool _stopRequested;

    internal RunnerProcessStartGuard(Process process, Action? startCommitted = null)
    {
        Process = process;
        _startCommitted = startCommitted;
    }

    internal Process Process { get; }

    internal bool HasStarted
    {
        get
        {
            lock (_gate)
                return _state == StartState.Started;
        }
    }

    internal bool IsSettled
    {
        get
        {
            lock (_gate)
            {
                return _state == StartState.Prevented
                       || (_state == StartState.Started
                           && RunnerProcessCleanup.HasExited(Process));
            }
        }
    }

    internal bool TryStart()
    {
        lock (_gate)
        {
            if (_state != StartState.Pending)
                return false;
            _state = StartState.Starting;
        }

        var started = false;
        try
        {
            _startCommitted?.Invoke();
            started = Process.Start();
            return started;
        }
        finally
        {
            var stop = false;
            lock (_gate)
            {
                _state = started ? StartState.Started : StartState.Prevented;
                stop = started && _stopRequested;
            }
            if (stop)
                RunnerProcessCleanup.KillTree(Process);
        }
    }

    internal void StopOrPreventStart()
    {
        var kill = false;
        var dispose = false;
        lock (_gate)
        {
            switch (_state)
            {
                case StartState.Pending:
                    _state = StartState.Prevented;
                    dispose = true;
                    break;
                case StartState.Starting:
                    _stopRequested = true;
                    break;
                case StartState.Started:
                    kill = true;
                    break;
            }
        }

        if (kill)
            RunnerProcessCleanup.KillTree(Process);
        if (dispose)
            Process.Dispose();
    }

    public void Dispose() => Process.Dispose();

    private enum StartState
    {
        Pending,
        Starting,
        Started,
        Prevented
    }
}

internal static class RunnerProcessCleanup
{
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMilliseconds(200);

    internal static async Task<bool> StopTreeAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        KillTree(process);
        return await AwaitExitAsync(process, cancellationToken);
    }

    internal static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The process may have exited between the state check and kill request.
        }
    }

    internal static async Task<bool> AwaitExitAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(ConfirmationTimeout, cancellationToken);
            return process.HasExited;
        }
        catch (Exception exception) when (exception is TimeoutException
                                          or OperationCanceledException
                                          or InvalidOperationException
                                          or Win32Exception)
        {
            KillTree(process);
            return HasExited(process);
        }
    }

    internal static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool HasStarted(Process process)
    {
        try
        {
            _ = process.Id;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
