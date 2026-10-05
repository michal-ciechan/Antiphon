using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    internal void WriteProbeInput(SafeFileHandle job, SafeFileHandle? unrelatedEvent = null) =>
        File.WriteAllText(Path.Combine(DirectoryPath, "probe-input"),
            $"{Nonce} {job.DangerousGetHandle().ToInt64()} {unrelatedEvent?.DangerousGetHandle().ToInt64() ?? 0}");

    internal async Task<string[]> WaitProbeAsync(Task root)
    {
        var clock = Stopwatch.StartNew();
        var path = Path.Combine(DirectoryPath, "probe-receipt");
        while (!File.Exists(path))
        {
            if (root.IsCompleted) throw new InvalidOperationException("Root exited before handle probe.", await ScriptHarnessProcessFixture.CaptureAsync(root));
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Handle probe readiness deadline.");
            await Task.Delay(20);
        }
        var fields = File.ReadAllText(path).Split(' ');
        fields.Length.ShouldBe(7);
        fields[0].ShouldBe(Nonce);
        Root!.Executing().ShouldBeTrue();
        return fields;
    }

    internal static uint ReadHandleFlags(SafeFileHandle handle)
    {
        if (!GetHandleInformation(handle, out var flags)) throw NativeError("Independent handle flags");
        return flags;
    }

    internal static SafeFileHandle CreateInheritableEvent()
    {
        var attributes = new ProbeSecurityAttributes { Length = Marshal.SizeOf<ProbeSecurityAttributes>(), Inherit = true };
        var handle = CreateEventW(ref attributes, true, false, null);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        throw NativeError("Create unrelated inheritable event");
    }

    internal static bool EventIsSignaled(SafeFileHandle handle) => WaitForSingleObject(handle, 0) switch
    {
        0 => true, 258 => false, _ => throw NativeError("Independent event wait")
    };

    internal static async Task WithOwnerAsync(string caseName, WindowsScriptHarnessHooks hooks,
        Func<DirectOwner, Task> body, Action<ScriptHarnessWindowsProcessFixture>? configure = null, bool drainStreams = true)
    {
        DirectOwner? owned = null;
        Exception? primary = null;
        try
        {
            await Task.Run(async () =>
            {
                owned = new DirectOwner(caseName, hooks, configure, drainStreams);
                await body(owned);
            }).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (owned is not null)
                try { await owned.DisposeAsync(); }
                catch (Exception cleanup) when (primary is not null)
                { throw new AggregateException("Native assertion and direct-owner cleanup both failed.", primary, cleanup); }
        }
    }

    internal sealed class DirectOwner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _execution = new(TimeSpan.FromSeconds(5));
        private bool _disposed;
        internal ScriptHarnessWindowsProcessFixture Windows { get; }
        internal WindowsScriptHarnessProcess Owner { get; }
        internal ScriptProcessRequest Request { get; }
        internal Task<int> RootExit { get; }
        internal Task StdoutDrain { get; }
        internal Task StderrDrain { get; }

        internal DirectOwner(string caseName, WindowsScriptHarnessHooks hooks,
            Action<ScriptHarnessWindowsProcessFixture>? configure, bool drainStreams)
        {
            Windows = new ScriptHarnessWindowsProcessFixture(hooks);
            configure?.Invoke(Windows);
            Request = new ScriptProcessRequest(ScriptHarnessProcessFixture.ResolveInstalledPowerShell(),
                ScriptHarnessProcessFixture.ScriptPath, caseName,
                Path.Combine(Windows.DirectoryPath, "results"), Path.Combine(Windows.DirectoryPath, "control"),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), Windows.Arguments());
            try { Owner = new WindowsScriptHarnessProcess(Request, hooks); }
            catch { Windows.Dispose(); _execution.Dispose(); throw; }
            // This independent retained handle remains valid when W4 disposes
            // the adapter's process handle to exercise kill-on-close alone.
            RootExit = Windows.Root!.WaitForExitAsync(_execution.Token);
            StdoutDrain = drainStreams ? DrainAsync(Owner.Stdout) : Task.CompletedTask;
            StderrDrain = drainStreams ? DrainAsync(Owner.Stderr) : Task.CompletedTask;
        }

        internal Task<ScriptHarnessProcessFixture.ObservedTree> WaitReadyAsync() => Windows.WaitReadyAsync(Request, RootExit);

        internal void DisposeOwner()
        {
            _disposed = true;
            Owner.Dispose();
        }

        internal void DeletePathsAfterConfirmedDeath()
        {
            foreach (var process in Windows._processes)
                process.Executing().ShouldBeFalse("Direct fixture paths cannot be removed before independent death confirmation.");
            // Direct W4 bypasses the coordinator intentionally. Its fixture owns
            // these scratch paths and performs the coordinator's deletion only
            // after the retained root/child/grandchild handles have signaled.
            if (Directory.Exists(Request.ResultsDirectory)) Directory.Delete(Request.ResultsDirectory, true);
            if (Directory.Exists(Request.ControlDirectory)) Directory.Delete(Request.ControlDirectory, true);
        }

        public async ValueTask DisposeAsync()
        {
            var clock = Stopwatch.StartNew();
            _execution.Cancel();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                if (!_disposed)
                {
                    try
                    {
                        await Owner.TerminateAsync(cleanup.Token);
                        await Owner.ConfirmDeadAsync(cleanup.Token);
                        await Task.WhenAll(StdoutDrain, StderrDrain).WaitAsync(cleanup.Token);
                    }
                    finally { DisposeOwner(); }
                }
                await Task.WhenAll(StdoutDrain, StderrDrain).WaitAsync(cleanup.Token);
                try { await RootExit; }
                catch (OperationCanceledException) when (_execution.IsCancellationRequested) { }
                Windows.AssertStoppedBeforeDispose(requireExplicitStop: false);
            }
            finally
            {
                try { Windows.Dispose(TimeSpan.FromSeconds(5) - clock.Elapsed); }
                finally { _execution.Dispose(); }
            }
        }

        private static async Task DrainAsync(StreamReader reader)
        {
            var buffer = new char[4096];
            while (await reader.ReadAsync(buffer.AsMemory()) != 0) { }
        }
    }

    // Never assign the test host. Only the suspended target and this separately
    // started sentinel join the outer job, which has its own fixture custody.
    internal sealed class OuterJob : IDisposable
    {
        internal SafeFileHandle Handle { get; }
        internal ObservedProcess Sentinel { get; }

        internal OuterJob()
        {
            Handle = CreateJobObjectW(IntPtr.Zero, null);
            if (Handle.IsInvalid) { Handle.Dispose(); throw NativeError("Create outer job"); }
            SafeFileHandle? process = null, thread = null;
            try
            {
                var buffer = Marshal.AllocHGlobal(144);
                try
                {
                    Marshal.Copy(new byte[144], 0, buffer, 144);
                    Marshal.WriteInt32(buffer, 16, 0x2000);
                    if (!SetInformationJobObject(Handle, 9, buffer, 144)) throw NativeError("Set outer job limits");
                }
                finally { Marshal.FreeHGlobal(buffer); }
                var startup = new ProbeStartupInfo { Size = Marshal.SizeOf<ProbeStartupInfo>() };
                // Paths are quoted as whole Windows arguments; neither contains a quote.
                var command = new StringBuilder($"dotnet \"{ScriptHarnessProcessFixture.HelperPath}\" windows-sentinel");
                if (!CreateProcessW(null, command, IntPtr.Zero, IntPtr.Zero, false, 4,
                        IntPtr.Zero, Directory.GetCurrentDirectory(), ref startup, out var information))
                    throw NativeError("Create suspended sentinel");
                process = new SafeFileHandle(information.Process, true);
                thread = new SafeFileHandle(information.Thread, true);
                Assign(process);
                Sentinel = ObservedProcess.Retain(process);
                if (ResumeThread(thread) != 1) throw NativeError("Resume sentinel");
            }
            catch
            {
                if (process is not null)
                {
                    TerminateProcess(process, 99);
                    if (WaitForSingleObject(process, 2000) != 0) throw new TimeoutException("Sentinel setup unwind deadline.");
                }
                Sentinel?.Dispose();
                Handle.Dispose();
                throw;
            }
            finally { thread?.Dispose(); process?.Dispose(); }
        }

        internal void Assign(SafeFileHandle process)
        {
            if (!AssignProcessToJobObject(Handle, process)) throw NativeError("Assign outer job");
        }

        internal bool Contains(ObservedProcess process) => process.IsInJob(Handle);

        public void Dispose()
        {
            try
            {
                if (Sentinel.Executing()) Sentinel.Terminate();
                Sentinel.Join(TimeSpan.FromSeconds(5));
            }
            finally { Sentinel.Dispose(); Handle.Dispose(); }
        }
    }

    internal void AssertStoppedBeforeDispose(bool requireExplicitStop = true)
    {
        var calls = Hooks.Calls;
        if (requireExplicitStop)
        {
            Array.IndexOf(calls, "terminate-job").ShouldBeGreaterThanOrEqualTo(0);
            Array.IndexOf(calls, "dispose").ShouldBeGreaterThan(Array.IndexOf(calls, "terminate-job"));
        }
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
        internal bool IsInJob(SafeFileHandle job) => ScriptHarnessWindowsProcessFixture.IsInJob(_handle, job);
        internal async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            while (Executing()) await Task.Delay(20, cancellationToken);
            if (!GetExitCodeProcess(_handle, out var exitCode)) throw NativeError("Independent root exit code");
            return unchecked((int)exitCode);
        }
        internal void Terminate()
        {
            if (TerminateProcess(_handle, 99)) return;
            var code = Marshal.GetLastWin32Error();
            if (Executing()) throw new Win32Exception(code,
                $"Emergency TerminateProcess failed ({code}): {new Win32Exception(code).Message}");
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
    [StructLayout(LayoutKind.Sequential)] private struct ProbeSecurityAttributes
    { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProbeStartupInfo
    {
        public int Size; public string? Reserved; public string? Desktop; public string? Title;
        public int X; public int Y; public int XSize; public int YSize; public int XCount; public int YCount;
        public int Fill; public uint Flags; public short Show; public short ReservedLength; public IntPtr ReservedBytes;
        public IntPtr Stdin; public IntPtr Stdout; public IntPtr Stderr;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProbeProcessInformation
    { public IntPtr Process; public IntPtr Thread; public int Pid; public int ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetHandleInformation(SafeFileHandle handle, out uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateEventW(ref ProbeSecurityAttributes attributes, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, IntPtr buffer, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string? application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref ProbeStartupInfo startup, out ProbeProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, IntPtr buffer, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(SafeFileHandle process, SafeFileHandle job, out bool result);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetProcessId(SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeFileHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
}
