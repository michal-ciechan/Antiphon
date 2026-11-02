using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Proof that a live process descends from one mutating git child after the parent-pid walk
/// has lost it. The walk misses a reparented grandchild: a POSIX double fork, or an msys
/// <c>sleep</c> whose Windows parent was the exited fork stub. The child and everything it
/// starts inherit <see cref="Variable"/>. On Windows they also join one job object, assigned
/// as soon as the child starts. The budget kill terminates that job, which reaches a reparented
/// holder. A process that holds the redirected pipe without either proof is a stranger. It
/// does not keep the journal fence.
/// </summary>
internal sealed class OwnedChildMarker : IDisposable
{
    internal const string Variable = "ANTIPHON_REPOSITORY_CHILD";

    private IntPtr _job;

    private OwnedChildMarker(string value, IntPtr job)
    {
        Value = value;
        _job = job;
    }

    public string Value { get; }

    /// <summary>Test seam: reads one <c>/proc/&lt;pid&gt;/environ</c> path.</summary>
    internal Func<string, byte[]> ReadEnviron { get; set; } = File.ReadAllBytes;

    /// <summary>Test seam: a stand-in job's active process count, read in place of the Windows job.</summary>
    internal Func<uint?>? JobProbe { get; set; }

    /// <summary>The job held the child before it could run or spawn. False when the child joined
    /// after <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
    internal bool AssignedBeforeSpawn { get; set; }

    internal static OwnedChildMarker Create(Action<string> log)
    {
        var job = IntPtr.Zero;
        if (OperatingSystem.IsWindows())
        {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                log("repository_child_job unavailable reason=create_win32_" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
        }
        return new OwnedChildMarker(Guid.NewGuid().ToString("N"), job);
    }

    internal void Apply(ProcessStartInfo start) => start.Environment[Variable] = Value;

    /// <summary>Windows: put the started child in the job. A child it started before this
    /// call is outside the job; the parent-pid walk still sees it while its parent lives.</summary>
    internal void Attach(Process child, Action<string> log)
    {
        if (_job == IntPtr.Zero) return;
        try
        {
            if (AssignProcessToJobObject(_job, child.Handle)) return;
            log("repository_child_job unavailable reason=assign_win32_" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log("repository_child_job unavailable reason=" + ex.GetType().Name);
        }
        CloseJob();
    }

    /// <summary>Windows: terminate every job member, including a reparented holder the tree
    /// walk could not see. Members are all descendants of the owned child.</summary>
    internal void TerminateMembers(int rootPid, Action<string> log)
    {
        if (_job == IntPtr.Zero) return;
        var active = ActiveJobProcesses();
        var terminated = TerminateJobObject(_job, 1);
        log($"repository_child_job_terminate root={rootPid} active={Format(active)} terminated={terminated}");
    }

    /// <summary>True when a live process carries this marker, false when none does, null when
    /// that cannot be established. Windows reads the job; Linux reads every readable environ.</summary>
    internal bool? AnyAlive()
    {
        if (JobProbe is not null) return JobVerdict(AssignedBeforeSpawn, JobProbe());
        if (OperatingSystem.IsWindows())
            return _job == IntPtr.Zero ? null : JobVerdict(AssignedBeforeSpawn, ActiveJobProcesses());
        if (OperatingSystem.IsLinux()) return AnyEnvironCarries(Value, ReadEnviron);
        return null;
    }

    internal static bool? JobVerdict(bool assignedBeforeSpawn, uint? active) =>
        active is uint count ? count > 0 : null;

    /// <summary>Linux: a live process whose initial environment carries the marker. A zombie has
    /// an empty environ and is not a writer. Another user's environ is unreadable and is not ours.</summary>
    internal static bool? AnyEnvironCarries(string value, Func<string, byte[]> readEnviron)
    {
        var needle = Encoding.UTF8.GetBytes(Variable + "=" + value + "\0");
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateDirectories("/proc"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        try
        {
            foreach (var entry in entries)
            {
                if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                if (pid == Environment.ProcessId) continue;
                byte[] environ;
                try { environ = readEnviron(Path.Combine(entry, "environ")); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (Carries(environ, needle)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        return false;
    }

    private static bool Carries(ReadOnlySpan<byte> environ, ReadOnlySpan<byte> needle)
    {
        var offset = 0;
        while (offset < environ.Length)
        {
            var found = environ[offset..].IndexOf(needle);
            if (found < 0) return false;
            var at = offset + found;
            if (at == 0 || environ[at - 1] == 0) return true;
            offset = at + 1;
        }
        return false;
    }

    private uint? ActiveJobProcesses()
    {
        var info = new JobBasicAccounting();
        return QueryInformationJobObject(_job, JobObjectBasicAccountingInformation, ref info,
            (uint)Marshal.SizeOf<JobBasicAccounting>(), IntPtr.Zero)
            ? info.ActiveProcesses
            : null;
    }

    private static string Format(uint? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private void CloseJob()
    {
        if (_job == IntPtr.Zero) return;
        _ = CloseHandle(_job);
        _job = IntPtr.Zero;
    }

    /// <summary>No KILL_ON_JOB_CLOSE: closing the handle leaves the members running, so a
    /// server death still leaves the journal and its live child for recovery.</summary>
    public void Dispose() => CloseJob();

    private const int JobObjectBasicAccountingInformation = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicAccounting
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, ref JobBasicAccounting info,
        uint length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
