using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Proof that a live process descends from one mutating git child after the parent-pid walk
/// has lost it. The walk misses a reparented grandchild: a POSIX double fork, or an msys
/// <c>sleep</c> whose Windows parent was the exited fork stub. The child and everything it
/// starts inherit <see cref="Variable"/>. On Windows they also join one job object, assigned
/// as soon as the child starts. The budget kill terminates that job, which reaches a reparented
/// holder. A process that holds the redirected pipe without either proof is a stranger. It
/// does not keep the journal fence. Every "cannot tell" keeps it: an environ that cannot be read,
/// a hidden process table, and a job the child joined only after it could already spawn.
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
    internal Func<string, byte[]> ReadEnviron { get; set; } = path => new LinuxProcOperations().ReadEnvironmentAsync(
        int.Parse(Path.GetFileName(Path.GetDirectoryName(path))!, CultureInfo.InvariantCulture),
        CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>Test seam: a stand-in job's active process count, read in place of the Windows job.</summary>
    internal Func<uint?>? JobProbe { get; set; }

    /// <summary>The job held the child before it could run or spawn. Only then does an empty job
    /// prove that no owned process lives. <see cref="Attach"/> runs after
    /// <see cref="Process.Start(ProcessStartInfo)"/>, so production leaves it false: a grandchild
    /// started before the assignment and then reparented is outside the job.</summary>
    internal bool AssignedBeforeSpawn { get; set; }

    /// <summary>Linux: the root's <c>/proc/&lt;pid&gt;/stat</c> start time in clock ticks since boot.
    /// No descendant can start before it.</summary>
    internal long? RootStartTime { get; private set; }

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
        if (OperatingSystem.IsLinux()) RootStartTime = StartTime("/proc/" + child.Id.ToString(CultureInfo.InvariantCulture));
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
        var assigned = AssignedBeforeSpawn ? "before_spawn" : "after_start";
        log($"repository_child_job_terminate root={rootPid} active={Format(active)} terminated={terminated} assigned={assigned}");
    }

    /// <summary>True when a live process carries this marker, false when none does, null when
    /// that cannot be established. Windows reads the job; Linux reads every readable environ.</summary>
    internal bool? AnyAlive()
    {
        if (JobProbe is not null) return JobVerdict(AssignedBeforeSpawn, JobProbe());
        if (OperatingSystem.IsWindows())
            return _job == IntPtr.Zero ? null : JobVerdict(AssignedBeforeSpawn, ActiveJobProcesses());
        if (OperatingSystem.IsLinux()) return AnyEnvironCarries(Value, RootStartTime, ReadEnviron);
        return null;
    }

    /// <summary>A live member is ours. An empty job proves nothing unless the child was inside it
    /// before it could spawn (review bd90f6b5 D-2).</summary>
    internal static bool? JobVerdict(bool assignedBeforeSpawn, uint? active) =>
        active is not uint count ? null
        : count > 0 ? true
        : assignedBeforeSpawn ? false
        : null;

    /// <summary>Linux: a live process whose initial environment carries the marker. A zombie has
    /// an empty environ and is not a writer. An environ that cannot be read (another uid, an
    /// undumpable or setuid descendant) is "cannot tell" unless that process started before the
    /// root, which no descendant can (review bd90f6b5 D-1). A process that is gone by the read is skipped.</summary>
    internal static bool? AnyEnvironCarries(string value, long? rootStartTime, Func<string, byte[]> readEnviron)
    {
        using var members = new LinuxOwnedGitMembers(new LinuxProcOperations(readEnviron));
        members.SetMarker(value);
        var result = members.ProbeAsync(new OwnedGitRoot(0, null, rootStartTime),
            new OwnedGitDeadline(TimeProvider.System, TimeSpan.FromSeconds(6)), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result.Survivors > 0 ? true : result.State == OwnedGitMembershipState.Empty ? false : null;
    }

    private static long? StartTime(string entry)
    {
        try { return LinuxProcOperations.ReadIdentityFile(int.Parse(Path.GetFileName(entry), CultureInfo.InvariantCulture))?.StartTicks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
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
