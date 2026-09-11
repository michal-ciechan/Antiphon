using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

internal sealed record HerdrDisposalProcessSnapshot(HerdrPaneDisposalProcess? Shell,
    IReadOnlyList<HerdrPaneDisposalProcess>? Foreground, IReadOnlyList<HerdrPaneDisposalProcess> Affected, bool Complete);

internal interface IHerdrDisposalProcessInspector
{
    HerdrDisposalProcessSnapshot Inspect(HerdrPaneProcessInfo processes);
    bool? IsSameProcessAlive(HerdrPaneDisposalProcess process);
}

/// <summary>Best-effort OS tree snapshot, exact creation times; no PID-reuse tolerance or kill API.</summary>
internal sealed class HerdrDisposalProcessInspector : IHerdrDisposalProcessInspector
{
    public HerdrDisposalProcessSnapshot Inspect(HerdrPaneProcessInfo processes)
    {
        if (!OperatingSystem.IsWindows() || processes.ShellPid is not > 0 || processes.ForegroundProcesses is null)
            return new(null, null, [], false);
        try
        {
            var parents = Parents();
            var ids = new HashSet<int> { processes.ShellPid.Value };
            bool added;
            do
            {
                added = false;
                foreach (var (pid, parent) in parents)
                    if (ids.Contains(parent) && ids.Add(pid)) added = true;
            } while (added);
            var facts = ids.Select(pid => Read(pid, parents.GetValueOrDefault(pid),
                processes.ForegroundProcesses.FirstOrDefault(p => p.Pid == pid)?.Argv)).ToArray();
            var foreground = processes.ForegroundProcesses.Select(p => facts.FirstOrDefault(f => f.Pid == p.Pid)
                ?? new HerdrPaneDisposalProcess(p.Pid, null)).ToArray();
            return new(facts.FirstOrDefault(p => p.Pid == processes.ShellPid), foreground, facts,
                facts.All(p => p.StartedAtUtc is not null) && foreground.All(p => p.StartedAtUtc is not null));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        { return new(null, null, [], false); }
    }

    public bool? IsSameProcessAlive(HerdrPaneDisposalProcess process)
    {
        if (process.Pid <= 0 || process.StartedAtUtc is null) return null;
        try
        {
            using var p = Process.GetProcessById(process.Pid);
            return !p.HasExited && p.StartTime.ToUniversalTime() == process.StartedAtUtc;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return null; }
    }

    private static HerdrPaneDisposalProcess Read(int pid, int parent, IReadOnlyList<string>? argv)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var started = process.StartTime.ToUniversalTime();
            return process.HasExited ? new(pid, null) : new(pid, process.ProcessName, started, parent, HerdrDisposalIdentity.NativeIds(argv));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        { return new(pid, null); }
    }

    private static Dictionary<int, int> Parents()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new Dictionary<int, int>();
        do { result[(int)entry.Pid] = (int)entry.ParentPid; } while (Process32NextW(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public UIntPtr Heap;
        public uint Module, Threads, ParentPid;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);
}
