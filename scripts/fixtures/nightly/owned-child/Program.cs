using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var modes = new[] { "child", "breakaway-child", "descendant", "breakaway", "root-held", "plain" };
var controlArguments = args.Length >= 2 && modes.Contains(args[1]);
var directory = controlArguments ? args[0] : Environment.GetEnvironmentVariable("C1039_FIXTURE_DIRECTORY")!;
var mode = controlArguments ? args[1] : Environment.GetEnvironmentVariable("C1039_FIXTURE_MODE")!;
File.WriteAllText(Path.Combine(directory, mode + ".pid"), Environment.ProcessId.ToString());
if (mode == "child" || mode == "breakaway-child")
{
    while (!File.Exists(Path.Combine(directory, "child.release"))) Thread.Sleep(10);
    Console.WriteLine("CHILD-STDOUT-END"); Console.Error.WriteLine("CHILD-STDERR-END");
    return;
}
File.WriteAllText(Path.Combine(directory, "started"), "started");
File.WriteAllText(Path.Combine(directory, "argv.json"), JsonSerializer.Serialize(controlArguments ? args.Skip(2) : args));
if (mode == "descendant")
{
    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false,
        ArgumentList = { directory, "child" }
    })!;
    while (!File.Exists(Path.Combine(directory, "child.pid"))) Thread.Sleep(10);
}
if (mode == "breakaway")
{
    var startup = new Native.Startup { Size = Marshal.SizeOf<Native.Startup>() };
    var command = new StringBuilder('"' + Environment.ProcessPath! + "\" \"" + directory + "\" breakaway-child");
    var started = Native.CreateProcessW(Environment.ProcessPath!, command, IntPtr.Zero, IntPtr.Zero,
        false, 0x01000000 | 0x08000000, IntPtr.Zero, directory, ref startup, out var child);
    File.WriteAllText(Path.Combine(directory, "breakaway-result"), started ? "escaped" : "refused");
    if (started)
    {
        File.WriteAllText(Path.Combine(directory, "breakaway-child.pid"), child.Pid.ToString());
        Native.CloseHandle(child.Process); Native.CloseHandle(child.Thread);
    }
}
if (mode == "root-held")
    while (!File.Exists(Path.Combine(directory, "root.release"))) Thread.Sleep(10);
Console.WriteLine("ROOT-STDOUT-END"); Console.Error.WriteLine("ROOT-STDERR-END");

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Startup
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public int X,Y,Cx,Cy,CharsX,CharsY,Fill,Flags;
        public short Show, ReservedBytes; public IntPtr Reserved2, Stdin, Stdout, Stderr;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Info { public IntPtr Process, Thread; public uint Pid, Tid; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    internal static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out Info info);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
}

// Separate compiled fixture observer: read the live private job, not the adapter's
// configured constant. An enclosing checkpoint job may independently deny escape.
public static class NativeJobObserver
{
    public static uint ReadLimitFlags(IntPtr job)
    {
        // The API requires the exact JOBOBJECT_EXTENDED_LIMIT_INFORMATION size.
        var size = IntPtr.Size == 8 ? 144 : 112;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, 9, buffer, (uint)size, out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((uint)Marshal.ReadInt32(buffer, 16));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int kind, IntPtr buffer, uint size, out uint returned);
}
