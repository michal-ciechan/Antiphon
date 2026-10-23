using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Antiphon.HostCleanup.Diagnostics;

// Query-only RM declarations promoted from the existing WindowsFileLockOwners helper.
// https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist
[SupportedOSPlatform("windows")]
internal sealed class NativeWindowsFileUseSource : WindowsFileUseSource
{
    public override int Start(out uint session) => RmStartSession(out session, 0, new StringBuilder(33));
    public override int Register(uint session, string[] files) =>
        RmRegisterResources(session, (uint)files.Length, files.Select(Path.GetFullPath).ToArray(), 0, IntPtr.Zero, 0, IntPtr.Zero);
    public override WindowsFileUseList List(uint session, uint capacity)
    {
        if (capacity > 4096) throw new IOException("owner bound");
        var buffer = capacity == 0 ? null : new ProcessInfo[capacity];
        var count = capacity;
        var status = RmGetList(session, out var needed, ref count, buffer, out _);
        if (status != 0) return new(status, needed, []);
        if (count > capacity) return new(234, count, []);
        return new(0, needed, buffer is null ? [] : buffer.Take((int)count)
            .Select(row => new WindowsFileUseProcess(row.Process.Pid,
                ((long)(uint)row.Process.Start.dwHighDateTime << 32) | (uint)row.Process.Start.dwLowDateTime,
                row.Application ?? "[unavailable]")).ToArray());
    }
    public override void End(uint session) => _ = RmEndSession(session);
    public override IEnumerable<int> Processes()
    {
        // No handle is retained for a census entry. Only matching identities are enriched.
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes.Take(4097)) yield return process.Id;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public override long Generation(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.StartTime.ToUniversalTime().ToFileTimeUtc();
    }
    public override WindowsFileUseMetadata Metadata(int pid)
    {
        var generation = Generation(pid);
        using var search = new ManagementObjectSearcher(
            "SELECT Name,ExecutablePath,CommandLine,ParentProcessId,CreationDate FROM Win32_Process WHERE ProcessId=" +
            pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        search.Options.Timeout = TimeSpan.FromSeconds(1);
        search.Options.ReturnImmediately = false;
        using var collection = Query(search);
        foreach (ManagementObject row in collection)
        {
            using (row)
            {
                // CIM timestamps have microsecond precision; native generation keeps all FILETIME ticks.
                var creation = row["CreationDate"] as string;
                if (creation is null) throw new UnauthorizedAccessException();
                var cimGeneration = ManagementDateTimeConverter.ToDateTime(creation).ToUniversalTime().ToFileTimeUtc();
                if (cimGeneration / 10 != generation / 10 || Generation(pid) != generation)
                    return new(pid, -1, "[identity changed]", null, null, null);
                var name = row["Name"] as string ?? "[unavailable]";
                var executable = row["ExecutablePath"] as string;
                var raw = row["CommandLine"] as string;
                var parent = row["ParentProcessId"] is uint value && value <= int.MaxValue ? (int?)value : null;
                return new(pid, generation, name, executable, Arguments(raw), parent);
            }
        }
        throw new ArgumentException("process exited");
    }
    public override IEnumerable<string> Modules(int pid)
    {
        using var process = Process.GetProcessById(pid);
        foreach (ProcessModule module in process.Modules) yield return module.FileName;
    }

    private static ManagementObjectCollection Query(ManagementObjectSearcher search)
    {
        try { return search.Get(); }
        catch (ManagementException error) when (error.ErrorCode == ManagementStatus.AccessDenied)
        { throw new UnauthorizedAccessException("process metadata unavailable"); }
    }

    private static string? Arguments(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 65536) return null;
        // Omit an uncertain tokenization completely. Never try a shell or preserve raw text.
        var quoted = false;
        var slashes = 0;
        foreach (var ch in command)
        {
            if (ch == '"' && slashes % 2 == 0) quoted = !quoted;
            slashes = ch == '\\' ? slashes + 1 : 0;
        }
        if (quoted) return null;
        var argv = CommandLineToArgvW(command, out var count);
        if (argv == IntPtr.Zero) return null;
        try
        {
            if (count is <= 0 or > 256) return null;
            var values = new string[count];
            for (var index = 0; index < count; index++)
            {
                var value = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size));
                if (value is null || value.Length > 8192) return null;
                values[index] = value;
            }
            return string.Join('\0', values);
        }
        finally { _ = LocalFree(argv); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { internal int Pid; internal FILETIME Start; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        internal UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Application;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Service;
        internal int Type;
        internal uint Status, SessionId;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint applications, IntPtr processes, uint services, IntPtr serviceNames);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? info, out uint reasons);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
