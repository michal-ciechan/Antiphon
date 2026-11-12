using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Hang task d268c50c (run ae84478d, antiphon all-group-118): every process a test-started root
/// spawns, including one that the parent-pid walk of <c>Kill(entireProcessTree: true)</c> cannot
/// reach. On Windows that is an msys <c>sleep</c> from a git hook: bash forks, the fork execs
/// <c>sleep.exe</c>, and the exec stub exits, so the sleeper's Windows parent pid names a dead
/// process. It then keeps the job of the chunk harness alive with inherited handles.
/// Every process started from <see cref="Apply"/> inherits <see cref="Variable"/>; Linux finds
/// the members by their initial environ. On Windows, <see cref="Adopt"/> puts the root in a job
/// that every later descendant joins, with no breakaway. Adopt runs after
/// <see cref="Process.Start(ProcessStartInfo)"/>. A grandchild created in that window misses the
/// job, which is a false negative only. Closing it needs <c>CREATE_SUSPENDED</c> or
/// <c>PROC_THREAD_ATTRIBUTE_JOB_LIST</c> on <c>CreateProcess</c>. Callers need the redirected
/// <see cref="Process"/> that <c>Process.Start</c> builds, and that API exposes neither flag.
/// A private CreateProcess stack would be a new native launch path, and
/// <c>DescendantLeakScopeTests</c> still starts with <c>Process.Start</c> then Adopt, so
/// CARD-1853 leaves the race optional. The pwsh worker and the witness's <c>ping -n 2</c>
/// delay both outlast the window.
/// The job is KILL_ON_JOB_CLOSE: a test host that dies still takes the members with it.
/// </summary>
internal sealed class DescendantLeakScope : IDisposable
{
    internal const string Variable = "ANTIPHON_TEST_DESCENDANT_SCOPE";

    private IntPtr _job;

    private DescendantLeakScope(string token, IntPtr job)
    {
        Token = token;
        _job = job;
    }

    public string Token { get; }

    public static DescendantLeakScope Create()
    {
        var job = IntPtr.Zero;
        if (OperatingSystem.IsWindows())
        {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                throw new InvalidOperationException("CreateJobObject failed: " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
            {
                var error = Marshal.GetLastWin32Error();
                CloseHandle(job);
                throw new InvalidOperationException("SetInformationJobObject failed: " + error.ToString(CultureInfo.InvariantCulture));
            }
        }
        return new DescendantLeakScope(Guid.NewGuid().ToString("N"), job);
    }

    public void Apply(ProcessStartInfo start) => start.Environment[Variable] = Token;

    /// <summary>Windows: put a started root in the job. Linux needs nothing past <see cref="Apply"/>.</summary>
    public void Adopt(Process root)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!AssignProcessToJobObject(_job, root.Handle))
            throw new InvalidOperationException("AssignProcessToJobObject failed: " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <see cref="Adopt"/>, or <paramref name="adopt"/> when a test injects a failure.
    /// A throw kills <paramref name="root"/> before it escapes, so a caller whose try has not
    /// started cannot leave the worker running (CARD-1853 A-4).
    /// </summary>
    public void AdoptOrKill(Process root, Action<Process>? adopt = null)
    {
        try
        {
            if (adopt is null) Adopt(root);
            else adopt(root);
        }
        catch
        {
            try
            {
                if (!root.HasExited) root.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The root exited between the check and the kill.
            }
            throw;
        }
    }

    /// <summary>Live members of the scope, never the test host itself.</summary>
    public IReadOnlyList<int> Alive()
    {
        if (OperatingSystem.IsWindows()) return JobMembers();
        if (OperatingSystem.IsLinux()) return EnvironCarriers(Token);
        return [];
    }

    /// <summary>Kill every live member. Windows terminates the job, which reaches a member whose
    /// parent has exited. Linux kills each pid whose environ still carries the token.</summary>
    public void KillAll()
    {
        if (OperatingSystem.IsWindows())
        {
            if (_job != IntPtr.Zero) TerminateJobObject(_job, 1);
            return;
        }
        foreach (var pid in Alive())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (EnvironCarries(pid, Token)) process.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone between the scan and the kill.
            }
        }
    }

    /// <summary>The leak check. Wait up to <paramref name="bound"/> for every member to exit. Any
    /// survivor fails the test, after this kills it so the failure itself leaks nothing.</summary>
    public async Task AssertNoneAliveAsync(TimeSpan bound, string because)
    {
        var clock = Stopwatch.StartNew();
        var alive = Alive();
        while (alive.Count > 0 && clock.Elapsed < bound)
        {
            await Task.Delay(50);
            alive = Alive();
        }
        if (alive.Count == 0) return;
        var survivors = string.Join(", ", alive.Select(Describe));
        KillAll();
        throw new ShouldAssertException(
            $"{alive.Count} descendant(s) survived the test's cleanup for {bound.TotalSeconds:0.#}s: {survivors}. {because}");
    }

    public void Dispose()
    {
        if (_job != IntPtr.Zero)
        {
            CloseHandle(_job); // KILL_ON_JOB_CLOSE ends any member still running.
            _job = IntPtr.Zero;
        }
        else
        {
            KillAll();
        }
    }

    private static string Describe(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return $"pid={pid}/image={process.ProcessName}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"pid={pid}/image=?";
        }
    }

    private IReadOnlyList<int> JobMembers()
    {
        if (_job == IntPtr.Zero) return [];
        const int capacity = 1024;
        var size = 8 + capacity * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(_job, JobObjectBasicProcessIdList, buffer, (uint)size, out _))
                throw new InvalidOperationException("QueryInformationJobObject failed: " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
            var listed = Marshal.ReadInt32(buffer, 4);
            var pids = new List<int>(listed);
            for (var i = 0; i < listed; i++)
            {
                var pid = (int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size);
                if (pid != Environment.ProcessId) pids.Add(pid);
            }
            return pids;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Linux: a live process whose initial environ carries the token. A zombie has an
    /// empty environ and holds no handle, so it is not a survivor.</summary>
    private static IReadOnlyList<int> EnvironCarriers(string token)
    {
        var pids = new List<int>();
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            if (pid == Environment.ProcessId) continue;
            if (EnvironCarries(pid, token)) pids.Add(pid);
        }
        return pids;
    }

    private static bool EnvironCarries(int pid, string token)
    {
        byte[] environ;
        try { environ = File.ReadAllBytes("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/environ"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        var needle = Encoding.UTF8.GetBytes(Variable + "=" + token + "\0");
        for (var offset = 0; offset < environ.Length;)
        {
            var found = environ.AsSpan(offset).IndexOf(needle);
            if (found < 0) return false;
            var at = offset + found;
            if (at == 0 || environ[at - 1] == 0) return true;
            offset = at + 1;
        }
        return false;
    }

    private const int JobObjectBasicProcessIdList = 3;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
