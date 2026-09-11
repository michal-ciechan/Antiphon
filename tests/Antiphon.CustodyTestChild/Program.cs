using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Owned, unpaid native peer. Named events keep ancestry/exit cuts deterministic.
if (!OperatingSystem.IsWindows() || args.Length != 3) return 2;
var role = args[0];
var prefix = args[1];
var flags = uint.Parse(args[2]);
if (!Native.IsProcessInJob(Native.GetCurrentProcess(), IntPtr.Zero, out var contained) || !contained)
    return 3;
using var ready = EventWaitHandle.OpenExisting(prefix + "-" + role);
using var release = EventWaitHandle.OpenExisting(prefix + "-release-" + role);
Console.WriteLine($"{role}:{Environment.ProcessId}:contained");
if (role != "leaf")
{
    var next = role == "root" ? "middle" : "leaf";
    var startup = new Native.StartupInfo
    {
        Size = Marshal.SizeOf<Native.StartupInfo>(), Flags = 1, ShowWindow = 0,
    };
    var command = new StringBuilder($"\"{Path.GetFileName(Environment.ProcessPath)}\" {next} {prefix} {flags}");
    if (!Native.CreateProcess(null, command, IntPtr.Zero, IntPtr.Zero, false,
            role == "middle" ? flags : 0, IntPtr.Zero, AppContext.BaseDirectory, ref startup, out var child))
    {
        var error = Marshal.GetLastWin32Error();
        if (flags == 0x01000000 && error == 5)
        {
            using var denied = EventWaitHandle.OpenExisting(prefix + "-denied");
            denied.Set();
        }
        return error;
    }
    Native.CloseHandle(child.Thread);
    Native.CloseHandle(child.Process);
}
ready.Set();
if (!release.WaitOne(TimeSpan.FromSeconds(60))) return 4;
Console.WriteLine($"{role}:final-output");
return 0;

internal static class Native
{
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(string? app, StringBuilder command,
        IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags,
        IntPtr environment, string cwd, ref StartupInfo startup, out ProcessInfo process);
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        public short ShowWindow, ReservedBytes;
        public IntPtr Reserved2, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }
}
