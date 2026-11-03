using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.PtyHost.Client;

/// <summary>
/// CARD-1653 S2: the kernel's account of the process behind a connected pipe. Linux reads
/// <c>SO_PEERCRED</c> (the listener's pid and uid at <c>listen(2)</c>); Windows reads
/// <c>GetNamedPipeServerProcessId</c> and has no uid.
/// </summary>
public sealed record PtyHostPeerCredentials(int Pid, uint? Uid)
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    /// <summary>The peer of a connected client handle, or null when the kernel does not say. Never throws.</summary>
    public static PtyHostPeerCredentials? Read(SafePipeHandle handle)
    {
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            if (OperatingSystem.IsLinux())
            {
                var credentials = new LinuxUCred();
                var length = (uint)Marshal.SizeOf<LinuxUCred>();
                var fd = handle.DangerousGetHandle().ToInt32();
                return GetPeerCred(fd, SolSocket, SoPeerCred, ref credentials, ref length) == 0
                    && length == Marshal.SizeOf<LinuxUCred>() && credentials.Pid > 0
                    ? new PtyHostPeerCredentials(credentials.Pid, credentials.Uid) : null;
            }
            if (OperatingSystem.IsWindows())
                return GetNamedPipeServerProcessId(handle, out var pid) && pid is > 0 and <= int.MaxValue
                    ? new PtyHostPeerCredentials((int)pid, null) : null;
            return null;
        }
        catch (Exception)
        {
            // Unreadable credentials are unproven, never accepted.
            return null;
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxUCred { public int Pid; public uint Uid; public uint Gid; }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetPeerCred(int socket, int level, int option, ref LinuxUCred value, ref uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
