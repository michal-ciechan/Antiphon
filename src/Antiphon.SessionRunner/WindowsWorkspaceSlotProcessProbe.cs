using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0824 S3 (D-7). Query-only Windows holder probe. A process-cwd census, then Restart Manager
/// <c>RmGetList</c> on at most <see cref="MaxLockPaths"/> paths that could not be opened. The query
/// session is closed afterwards. No process is stopped. Off Windows the default probe reports an
/// incomplete scan and does not call into Windows.
/// </summary>
public sealed class WindowsWorkspaceSlotProcessProbe : IWorkspaceSlotProcessProbe, IWorkspaceHolderScan
{
    public const int MaxLockPaths = 64;

    private readonly Func<string, HolderScan>? _backend;

    public WindowsWorkspaceSlotProcessProbe()
    {
    }

    /// <summary>Linux witness seam. Production construction uses the Windows query.</summary>
    internal WindowsWorkspaceSlotProcessProbe(Func<string, HolderScan> backend) => _backend = backend;

    public IReadOnlyList<WorkspaceSlotHolder> Holders(string path) => Scan(path).Holders;

    public HolderScan Scan(string path)
    {
        if (_backend is not null)
            return _backend(path);
        if (!OperatingSystem.IsWindows())
            return new HolderScan(false, []);
        return ScanWindows(path);
    }

    [SupportedOSPlatform("windows")]
    private static HolderScan ScanWindows(string path)
    {
        try { return WindowsSlotHolderQuery.Scan(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return new HolderScan(false, []);
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsSlotHolderQuery
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const int ProcessBasicInformationClass = 0;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorMoreData = 234;

    internal static HolderScan Scan(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var holders = new List<WorkspaceSlotHolder>();
        if (!CensusCwd(root, holders))
            return new HolderScan(false, holders);
        if (!CollectLocks(root, holders))
            return new HolderScan(false, holders);
        return new HolderScan(true, holders);
    }

    private static bool CensusCwd(string root, List<WorkspaceSlotHolder> holders)
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return false; }

        var sawSelf = false;
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        sawSelf = true;
                        if (Under(Directory.GetCurrentDirectory(), root))
                            holders.Add(new WorkspaceSlotHolder(process.Id, process.ProcessName, "cwd"));
                        continue;
                    }

                    if (!TryReadCwd(process.Id, out var cwd, out var skipped))
                        return false;
                    if (skipped)
                        continue;
                    if (Under(cwd, root))
                        holders.Add(new WorkspaceSlotHolder(process.Id, SafeName(process), "cwd"));
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the snapshot and the read.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return sawSelf;
    }

    /// <summary>
    /// A same-bitness cwd we cannot read fails the census. Access denied and a 32-bit process are
    /// skipped: this runner cannot see them, the same bound as an unreadable uid on Linux.
    /// </summary>
    private static bool TryReadCwd(int pid, out string? cwd, out bool skipped)
    {
        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
        {
            cwd = null;
            skipped = true;
            return true;
        }

        try
        {
            return TryReadOpenProcessCwd(handle, out cwd, out skipped);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    internal static bool TryReadOpenProcessCwd(IntPtr handle, out string? cwd, out bool skipped)
    {
        cwd = null;
        skipped = false;
        if (!IsWow64Process(handle, out var wow64))
            return false;
        if (wow64)
        {
            skipped = true;
            return true;
        }

        if (NtQueryInformationProcess(handle, ProcessBasicInformationClass, out var info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0
            || info.PebBaseAddress == IntPtr.Zero)
            return false;

        var parametersAddress = info.PebBaseAddress + 0x20;
        if (!ReadPointer(handle, parametersAddress, out var parameters) || parameters == IntPtr.Zero)
            return false;
        return ReadUnicode(handle, parameters + 0x38, out cwd);
    }

    private static bool CollectLocks(string root, List<WorkspaceSlotHolder> holders)
    {
        List<string> failed;
        try { failed = FailedOpens(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (failed.Count > WindowsWorkspaceSlotProcessProbe.MaxLockPaths)
            return false;
        if (failed.Count == 0)
            return true;
        return QueryRestartManager(failed, holders);
    }

    private static List<string> FailedOpens(string root)
    {
        var failed = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            if (!FailedOpen(file))
                continue;
            failed.Add(file);
            if (failed.Count > WindowsWorkspaceSlotProcessProbe.MaxLockPaths)
                break;
        }

        return failed;
    }

    private static bool FailedOpen(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Query only. The session is ended; nothing is shut down.</summary>
    private static bool QueryRestartManager(IReadOnlyList<string> paths, List<WorkspaceSlotHolder> holders)
    {
        var key = new StringBuilder(64);
        if (RmStartSession(out var session, 0, key) != 0)
            return false;
        try
        {
            if (RmRegisterResources(session, (uint)paths.Count, paths.ToArray(), 0, IntPtr.Zero, 0, IntPtr.Zero) != 0)
                return false;
            var count = 0u;
            var status = RmGetList(session, out var needed, ref count, null, out _);
            if (status == 0)
                return true;
            if (status != ErrorMoreData || needed is 0 or > 256)
                return false;
            var buffer = new ProcessInfo[needed];
            count = needed;
            status = RmGetList(session, out needed, ref count, buffer, out _);
            if (status != 0)
                return false;
            for (var i = 0; i < count && i < buffer.Length; i++)
            {
                var name = buffer[i].Application;
                if (string.IsNullOrWhiteSpace(name))
                    name = "unknown";
                holders.Add(new WorkspaceSlotHolder(buffer[i].Process.Pid, name, "rm"));
            }

            return true;
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    private static bool ReadPointer(IntPtr process, IntPtr address, out IntPtr value)
    {
        var buffer = new byte[IntPtr.Size];
        value = IntPtr.Zero;
        if (!ReadProcessMemory(process, address, buffer, buffer.Length, out var read) || read != buffer.Length)
            return false;
        value = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(buffer, 0) : (IntPtr)BitConverter.ToInt32(buffer, 0);
        return true;
    }

    private static bool ReadUnicode(IntPtr process, IntPtr address, out string? value)
    {
        value = null;
        var header = new byte[16];
        if (!ReadProcessMemory(process, address, header, header.Length, out var read) || read != header.Length)
            return false;
        var length = BitConverter.ToUInt16(header, 0);
        var buffer = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(header, 8) : (IntPtr)BitConverter.ToInt32(header, 4);
        if (length == 0 || buffer == IntPtr.Zero)
        {
            value = "";
            return true;
        }

        if (length > 32768)
            return false;
        var chars = new byte[length];
        if (!ReadProcessMemory(process, buffer, chars, chars.Length, out read) || read != chars.Length)
            return false;
        value = Encoding.Unicode.GetString(chars).TrimEnd('\0');
        return true;
    }

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return ""; }
    }

    private static bool Under(string? target, string root)
    {
        if (string.IsNullOrEmpty(target))
            return false;
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return false; }
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public int Pid;
        public System.Runtime.InteropServices.ComTypes.FILETIME Start;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Application;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public int Type;
        public uint Status;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr process, out bool wow64);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int @class, out ProcessBasicInformation info, int length, out int returned);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint applications, IntPtr processes, uint services, IntPtr serviceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? info, out uint reasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
