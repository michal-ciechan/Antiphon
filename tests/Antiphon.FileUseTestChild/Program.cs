using System.Runtime.InteropServices;

using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var mode = args[0]; var path = args[1]; var nonce = args[2];
using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
IntPtr mapping = IntPtr.Zero;
try
{
    if (mode == "mapped-file")
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        mapping = Native.mmap(IntPtr.Zero, 4096, 1, 1, stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 0);
        if (mapping == new IntPtr(-1)) throw new IOException("mmap failed");
        stream.Dispose(); // maps-only witness: no fd to the resource remains.
    }
    Console.WriteLine("READY " + nonce); Console.Out.Flush();
    var release = await Console.In.ReadLineAsync(watchdog.Token);
    if (release != "RELEASE " + nonce) return 2;
    Console.WriteLine("RELEASED " + nonce);
    return 0;
}
catch (OperationCanceledException) { return 3; }
finally { if (mapping != IntPtr.Zero && mapping != new IntPtr(-1)) Native.munmap(mapping, 4096); }
internal static class Native
{
    [DllImport("libc", SetLastError = true)] internal static extern IntPtr mmap(IntPtr address, nuint length, int protection, int flags, int fd, long offset);
    [DllImport("libc", SetLastError = true)] internal static extern int munmap(IntPtr address, nuint length);
}
