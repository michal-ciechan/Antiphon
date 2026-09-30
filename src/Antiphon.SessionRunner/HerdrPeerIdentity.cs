using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

/// <summary>Obtains the kernel-reported peer process, then binds it to its observed start time.</summary>
internal static class HerdrPeerIdentity
{
    internal static AsyncLocal<Func<Socket, int?>?> NativePidOverride { get; } = new();
    internal static AsyncLocal<bool> ForceUnavailable { get; } = new();

    internal static string? FromPipe(SafePipeHandle pipe)
    {
        if (ForceUnavailable.Value) return null;
        if (!OperatingSystem.IsWindows() || !GetNamedPipeServerProcessId(pipe, out var pid) || pid > int.MaxValue)
            return null;
        return Format((int)pid);
    }

    internal static string? FromSocket(Socket socket)
    {
        if (ForceUnavailable.Value) return null;
        int? pid;
        try
        {
            pid = NativePidOverride.Value is { } read ? read(socket) : ReadNativePid(socket);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or ObjectDisposedException)
        {
            return null;
        }
        return pid is > 0 ? Format(pid.Value) : null;
    }

    private static int? ReadNativePid(Socket socket)
    {
        var handle = socket.SafeHandle;
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            var fd = handle.DangerousGetHandle().ToInt32();
            if (OperatingSystem.IsLinux())
            {
                var credentials = new LinuxUCred();
                uint length = (uint)Marshal.SizeOf<LinuxUCred>();
                return GetLinuxPeerCred(fd, 1, 17, ref credentials, ref length) == 0
                    && length == Marshal.SizeOf<LinuxUCred>() && credentials.Pid > 0
                    ? credentials.Pid : null;
            }
            if (OperatingSystem.IsMacOS())
            {
                var pid = 0;
                uint length = sizeof(int);
                return GetMacPeerPid(fd, 0, 2, ref pid, ref length) == 0
                    && length == sizeof(int) && pid > 0 ? pid : null;
            }
            return null;
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    private static string? Format(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return $"{pid}:{process.StartTime.ToUniversalTime().Ticks}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxUCred { public int Pid; public int Uid; public int Gid; }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetLinuxPeerCred(int socket, int level, int option, ref LinuxUCred value, ref uint length);

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetMacPeerPid(int socket, int level, int option, ref int value, ref uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
