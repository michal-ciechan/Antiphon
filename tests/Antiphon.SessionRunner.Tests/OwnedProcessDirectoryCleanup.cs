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
                _ = handle.SafeHandle;
                if (handle.StartTime.ToUniversalTime() != record.Process.StartedUtc)
                    throw new IOException($"owned process generation changed pid={record.Process.Pid}");
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
        try
        {
            try { await body(); }
            finally
            {
                handles = capture();
                await stop();
            }
            foreach (var handle in handles)
                await handle.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
            delete();
        }
    }
}
