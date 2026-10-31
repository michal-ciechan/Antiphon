using System.Diagnostics;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Keep native process handles alive until the test's executable directory is removable.</summary>
internal static class OwnedProcessDirectoryCleanup
{
    internal static IReadOnlyList<Process> Capture(IEnumerable<TestOwnedPtyHost.Owned> owned)
    {
        var handles = new List<Process>();
        try
        {
            foreach (var record in owned)
            {
                // Pin the already-attributed generation before the runtime can shut it down.
                // These handles only witness exit; control remains with TestOwnedPtyHost.
                if (record.Process.Handle!.HasExited) continue;
                Process handle;
                try { handle = Process.GetProcessById(record.Process.Pid); }
                catch (ArgumentException) { continue; }
                handles.Add(handle);
                try { _ = handle.SafeHandle; }
                catch (InvalidOperationException) when (record.Process.Handle.HasExited) { }
                // The original pinned handle must still be alive after opening this one.
                // That proves the PID was not recycled, without comparing converted wall clocks.
                if (record.Process.Handle.HasExited)
                {
                    handles.Remove(handle);
                    handle.Dispose();
                }
            }
            return handles;
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    internal static async Task RunAsync(Func<Task> body, Func<Task> stop,
        Func<IReadOnlyList<Process>> capture, Action delete)
    {
        IReadOnlyList<Process> handles = [];
        var captured = false;
        var stopped = false;
        try
        {
            await TestOwnedPtyHost.ScopeAsync(body, async () =>
            {
                await TestOwnedPtyHost.ScopeAsync(
                    () => TestOwnedPtyHost.ScopeAsync(() =>
                    {
                        handles = capture();
                        captured = true;
                        return Task.CompletedTask;
                    }, async () =>
                    {
                        await stop();
                        stopped = true;
                    }),
                    async () =>
                    {
                        if (!captured) return; // Unknown ownership retains the directory.
                        var started = Stopwatch.StartNew();
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var failures = new List<Exception>();
                        foreach (var handle in handles)
                        {
                            try { await handle.WaitForExitAsync(deadline.Token); }
                            catch (Exception ex)
                            {
                                failures.Add(new IOException(
                                    $"owned process did not exit pid={handle.Id} name={handle.ProcessName} elapsed={started.Elapsed} budget=00:00:10; directory retained", ex));
                            }
                        }
                        if (failures.Count > 0) throw new AggregateException(failures);
                        if (stopped) delete(); // A cleanup ownership error also retains the directory.
                    });
            });
        }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
        }
    }
}
