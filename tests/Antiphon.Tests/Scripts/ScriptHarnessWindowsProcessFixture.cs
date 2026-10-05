using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Independent observations stay outside the paths the harness is allowed to delete.
// Every process is pinned before the parent-exit barrier is released.
internal sealed class ScriptHarnessWindowsProcessFixture : IDisposable
{
    private readonly List<ObservedProcess> _processes = [];
    internal string Nonce { get; } = Guid.NewGuid().ToString("N");
    internal string DirectoryPath { get; }
    internal WindowsScriptHarnessHooks Hooks { get; }
    internal ObservedProcess? Root { get; private set; }

    internal ScriptHarnessWindowsProcessFixture(WindowsScriptHarnessHooks? hooks = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native Windows qualification requires Windows.");
        if (!File.Exists(ScriptHarnessProcessFixture.HelperPath)) throw new FileNotFoundException("ScriptHarness helper was not staged.");
        DirectoryPath = Path.Combine(Path.GetTempPath(), "c1047-observer-" + Nonce);
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "nonce"), Nonce);
        Hooks = hooks ?? new WindowsScriptHarnessHooks();
        Hooks.Created = handle => { Root = ObservedProcess.Retain(handle); _processes.Add(Root); };
    }

    internal IReadOnlyList<string> Arguments(string? payload = null)
    {
        var args = new List<string> { "-HelperPath", ScriptHarnessProcessFixture.HelperPath,
            "-ObservationDirectory", DirectoryPath, "-Nonce", Nonce };
        if (payload is not null) args.AddRange(["-Payload", payload]);
        return args;
    }

    internal async Task<ScriptHarnessProcessFixture.ObservedTree> WaitReadyAsync(ScriptProcessRequest request, Task run)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(DirectoryPath, "ready")))
        {
            if (run.IsCompleted) throw new InvalidOperationException("Harness finished before native readiness.", await ScriptHarnessProcessFixture.CaptureAsync(run));
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Native tree readiness deadline.");
            await Task.Delay(20);
        }
        File.ReadAllText(Path.Combine(DirectoryPath, "ready")).ShouldBe(Nonce + " " + request.CaseName);
        var root = Read("root", Root ?? throw new InvalidOperationException("Root observation not retained."));
        var child = Read("child");
        var grandchild = Read("grandchild");
        var tree = new ScriptHarnessProcessFixture.ObservedTree(root, child, grandchild, request.ResultsDirectory);
        tree.AllExecuting().ShouldBeTrue("Ready requires three independently observed live processes.");
        File.WriteAllText(Path.Combine(DirectoryPath, "observed"), Nonce);
        return tree;
    }

    private ScriptHarnessProcessFixture.ProcessIdentity Read(string name, ObservedProcess? retained = null)
    {
        var fields = File.ReadAllText(Path.Combine(DirectoryPath, name)).Split(' ');
        fields.Length.ShouldBe(4);
        fields[0].ShouldBe(Nonce);
        var pid = int.Parse(fields[1]);
        var ticks = long.Parse(fields[2]);
        var observed = retained ?? ObservedProcess.Open(pid, ticks);
        if (retained is null) _processes.Add(observed);
        observed.Pid.ShouldBe(pid);
        observed.StartTicks.ShouldBe(ticks);
        return new ScriptHarnessProcessFixture.ProcessIdentity(pid, ticks, 0, observed);
    }

    internal void ReleaseRace() => File.WriteAllText(Path.Combine(DirectoryPath, "release"), Nonce);

    internal void AssertStoppedBeforeDispose()
    {
        var calls = Hooks.Calls;
        Array.IndexOf(calls, "terminate-job").ShouldBeGreaterThanOrEqualTo(0);
        Array.IndexOf(calls, "dispose").ShouldBeGreaterThan(Array.IndexOf(calls, "terminate-job"));
        foreach (var observed in _processes) observed.Executing().ShouldBeFalse("Owned process remained live before fixture sweep.");
        foreach (var (_, handle) in Hooks.Handles) handle.IsClosed.ShouldBeTrue("An adapter-owned handle remained open.");
    }

    public void Dispose() => Dispose(TimeSpan.FromSeconds(5));

    internal void Dispose(TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        var rescued = false;
        try
        {
            // Release every bounded fixture barrier even when an assertion failed.
            File.WriteAllText(Path.Combine(DirectoryPath, "observed"), Nonce);
            ReleaseRace();
            foreach (var name in new[] { "child", "grandchild" })
                if (File.Exists(Path.Combine(DirectoryPath, name)))
                {
                    var fields = File.ReadAllText(Path.Combine(DirectoryPath, name)).Split(' ');
                    if (fields[0] != Nonce) throw new InvalidDataException("Emergency observation nonce mismatch.");
                    var pid = int.Parse(fields[1]);
                    if (_processes.All(p => p.Pid != pid)) Read(name);
                }
            foreach (var process in _processes)
                if (process.Executing()) { rescued = true; process.Terminate(); }
            foreach (var process in _processes) process.Join(budget - clock.Elapsed);
        }
        finally
        {
            foreach (var process in _processes) process.Dispose();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
        rescued.ShouldBeFalse("Emergency sweep rescued a live process; ordinary custody proof failed.");
    }

    // This query deliberately has its own P/Invoke and buffer layout, independent
    // of the adapter's configured structure (ported from NativeJobObserver).
    internal static uint ReadJobLimitFlags(SafeFileHandle job)
    {
        var size = IntPtr.Size == 8 ? 144 : 112;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, 9, buffer, (uint)size, out _)) throw NativeError("Independent job limits");
            return unchecked((uint)Marshal.ReadInt32(buffer, 16));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static bool IsInJob(SafeFileHandle process, SafeFileHandle job)
    {
        if (!IsProcessInJob(process, job, out var member)) throw NativeError("Independent membership");
        return member;
    }

    internal sealed class ObservedProcess : IDisposable
    {
        private readonly SafeFileHandle _handle;
        internal int Pid { get; }
        internal long StartTicks { get; }
        private ObservedProcess(SafeFileHandle handle)
        {
            _handle = handle;
            Pid = unchecked((int)GetProcessId(handle));
            if (Pid == 0) throw NativeError("GetProcessId");
            if (!GetProcessTimes(handle, out var created, out _, out _, out _)) throw NativeError("GetProcessTimes");
            StartTicks = DateTime.FromFileTimeUtc(created).Ticks;
        }
        internal static ObservedProcess Retain(SafeFileHandle source)
        {
            var current = GetCurrentProcess();
            if (!DuplicateHandle(current, source, current, out var copy, 0, false, 2)) throw NativeError("Duplicate observer handle");
            try { return new ObservedProcess(copy); }
            catch { copy.Dispose(); throw; }
        }
        internal static ObservedProcess Open(int pid, long ticks)
        {
            var handle = OpenProcess(0x100000 | 0x1000 | 1, false, pid);
            if (handle.IsInvalid) { handle.Dispose(); throw NativeError("Open observer process"); }
            try
            {
                var process = new ObservedProcess(handle);
                if (process.StartTicks != ticks) throw new InvalidDataException("Observer process identity changed.");
                return process;
            }
            catch { handle.Dispose(); throw; }
        }
        internal bool Executing()
        {
            var wait = WaitForSingleObject(_handle, 0);
            return wait switch { 0 => false, 258 => true, _ => throw NativeError("Observer wait") };
        }
        internal void Terminate()
        {
            if (!TerminateProcess(_handle, 99) && Executing()) throw NativeError("Emergency TerminateProcess");
        }
        internal void Join(TimeSpan remaining)
        {
            if (!Executing()) return;
            if (remaining <= TimeSpan.Zero || WaitForSingleObject(_handle, (uint)Math.Ceiling(remaining.TotalMilliseconds)) != 0)
                throw new TimeoutException("Emergency process join exceeded the total five-second budget.");
        }
        public void Dispose() => _handle.Dispose();
    }

    private static Win32Exception NativeError(string operation) => new(Marshal.GetLastWin32Error(), operation);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, IntPtr buffer, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(SafeFileHandle process, SafeFileHandle job, out bool result);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetProcessId(SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeFileHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
}
