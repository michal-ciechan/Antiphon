using System.Security.Cryptography;
using System.Text;

namespace Antiphon.SessionRunner;

internal static class HerdrSnapshotFile
{
    // Concurrent MoveFileEx(overwrite) calls can fail with access denied on Windows even
    // with distinct, closed source files. Serialize only replacement, after each temp is
    // complete. The OS mutex also covers independent sidecar objects for the same path.
    internal static void Replace(string temp, string destination)
    {
        using var lease = Acquire(destination);
        File.Move(temp, destination, overwrite: true);
    }

    // Synchronous metadata transactions only: never hold this thread-affine lease across await.
    internal static IDisposable Acquire(string destination) =>
        TryAcquire(destination, Timeout.Infinite)!;

    internal static IDisposable? TryAcquire(string destination, int millisecondsTimeout)
    {
        var path = Path.GetFullPath(destination);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        var name = "Antiphon-HerdrSnapshot-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        var mutex = new Mutex(false, name);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(millisecondsTimeout);
        }
        catch (AbandonedMutexException)
        {
            acquired = true; // The previous owner exited; this caller owns the mutex.
        }
        finally
        {
            if (!acquired) mutex.Dispose();
        }
        return acquired ? new Lease(mutex) : null;
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    { public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); } }
}
