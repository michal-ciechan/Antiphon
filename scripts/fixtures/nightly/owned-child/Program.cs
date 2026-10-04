using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var directory = args[0];
var mode = args[1];
File.WriteAllText(Path.Combine(directory, mode + ".pid"), Environment.ProcessId.ToString());
if (mode == "child" || mode == "breakaway-child")
{
    while (!File.Exists(Path.Combine(directory, "child.release"))) Thread.Sleep(10);
    Console.WriteLine("CHILD-STDOUT-END"); Console.Error.WriteLine("CHILD-STDERR-END");
    return;
}
File.WriteAllText(Path.Combine(directory, "started"), "started");
File.WriteAllText(Path.Combine(directory, "argv.json"), JsonSerializer.Serialize(args.Skip(2)));
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
