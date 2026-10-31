using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Kills an owned git root and the descendants whose parent chain still leads to it.
/// <see cref="Process.Kill(bool)"/> on Windows terminates the root first and only then
/// asks for children. An intermediate that dies with that root (a job object with
/// KILL_ON_JOB_CLOSE, the same shape as Linux PR_SET_PDEATHSIG) is already gone, so
/// its child is never walked and keeps the redirected pipes open. The child journal
/// then stays after the call returns. The descendant set is captured before the first
/// fatal signal. A process that has already been reparented is not in the set.
/// </summary>
internal static class OwnedProcessTree
{
    internal static HashSet<int> Descendants(IReadOnlyDictionary<int, int> parentByPid, int root)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in parentByPid)
        {
            if (pid <= 1 || pid == parent) continue;
            if (!children.TryGetValue(parent, out var list))
                children[parent] = list = new List<int>();
            list.Add(pid);
        }

        var found = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!children.TryGetValue(current, out var list)) continue;
            foreach (var child in list)
            {
                if (child == root || child == Environment.ProcessId || !found.Add(child)) continue;
                pending.Push(child);
            }
        }
        return found;
    }

    internal static void Kill(Process root)
    {
        int rootId;
        try { rootId = root.Id; }
        catch (InvalidOperationException) { return; }

        // Start ticks guard the later kill against pid reuse. The set is taken before any
        // fatal signal, so an intermediate that dies with the root cannot hide its child.
        var seen = new Dictionary<int, long>();
        for (var pass = 0; pass < 4; pass++)
        {
            var added = 0;
            foreach (var pid in Descendants(ParentByPid(), rootId))
            {
                if (seen.ContainsKey(pid)) continue;
                if (StartTicks(pid) is not long ticks) continue;
                seen[pid] = ticks;
                added++;
            }
            if (added == 0) break;
        }

        Console.Error.WriteLine($"repository_child_tree_kill root={rootId} descendants={seen.Count}");
        foreach (var (pid, ticks) in seen)
        {
            if (StartTicks(pid) == ticks) Terminate(pid);
        }
        try
        {
            if (!root.HasExited) root.Kill(entireProcessTree: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { }
    }

    private static long? StartTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return null;
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static void Terminate(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { }
    }

    private static Dictionary<int, int> ParentByPid()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsParents();
            if (OperatingSystem.IsLinux()) return LinuxParents();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"repository_child_tree_kill_snapshot_failed error={ex.GetType().Name}");
        }
        return new Dictionary<int, int>();
    }

    private static Dictionary<int, int> LinuxParents()
    {
        var parents = new Dictionary<int, int>();
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(entry);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            try
            {
                var stat = File.ReadAllText(Path.Combine(entry, "stat"));
                var end = stat.LastIndexOf(')');
                if (end < 0 || end + 2 >= stat.Length) continue;
                var fields = stat.AsSpan(end + 2);
                var stateEnd = fields.IndexOf(' ');
                if (stateEnd < 0 || stateEnd + 1 >= fields.Length) continue;
                var rest = fields[(stateEnd + 1)..];
                var ppidEnd = rest.IndexOf(' ');
                var ppidText = ppidEnd < 0 ? rest : rest[..ppidEnd];
                if (int.TryParse(ppidText, NumberStyles.None, CultureInfo.InvariantCulture, out var ppid))
                    parents[pid] = ppid;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { }
        }
        return parents;
    }

    private static Dictionary<int, int> WindowsParents()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == InvalidHandle) return parents;
        try
        {
            var entry = new ProcessEntry { dwSize = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) return parents;
            do
            {
                var pid = unchecked((int)entry.th32ProcessID);
                var parent = unchecked((int)entry.th32ParentProcessID);
                if (pid > 1) parents[pid] = parent;
                entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally { _ = CloseHandle(snapshot); }
        return parents;
    }

    private const uint SnapshotProcess = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
