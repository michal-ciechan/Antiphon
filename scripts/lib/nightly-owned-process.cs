// CARD-1039 S1n-a. Windows native owner; loaded by PowerShell 7 with Add-Type.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Nightly
{
    // Tests may pause individual OS operations; the launcher and custody loop remain real.
    public sealed class NativeProcessHooks
    {
        public string BarrierDirectory { get; set; } = "";
        public string[] Barriers { get; set; } = Array.Empty<string>();
        public bool FailAssignment { get; set; }
        public bool ZeroCountAtRootWait { get; set; }
        internal void Pause(string phase)
        {
            if (!Barriers.Contains(phase)) return;
            File.WriteAllText(Path.Combine(BarrierDirectory, phase + ".entered"), "entered");
            while (!File.Exists(Path.Combine(BarrierDirectory, phase + ".release"))) Thread.Sleep(10);
        }
        internal bool HoldRootWait()
        {
            if (!Barriers.Contains("root-wait")) return false;
            File.WriteAllText(Path.Combine(BarrierDirectory, "root-wait.entered"), "entered");
            return !File.Exists(Path.Combine(BarrierDirectory, "root-wait.release"));
        }
    }

    public sealed class NativeProcessResult
    {
        public int ExitCode { get; set; } = 1;
        public bool TimedOut { get; set; }
        public int Pid { get; set; }
        public DateTime RootStartedAtUtc { get; set; }
        public string[] DescendantIdentities { get; set; } = Array.Empty<string>();
        public bool ChildrenExited { get; set; }
        public bool OutputDrained { get; set; }
        public bool CleanupComplete { get { return ChildrenExited && OutputDrained; } }
        public bool TerminationRequested { get; set; }
        public bool TerminationSucceeded { get; set; }
        public int RemainingProcessCount { get; set; } = -1;
        public double LaunchMilliseconds { get; set; }
        public double ExecutionMilliseconds { get; set; }
        public double CleanupMilliseconds { get; set; }
        public double DrainMilliseconds { get; set; }
        public string Error { get; set; } = "";
    }

    public sealed class NativeProcessOwner
    {
        private readonly NativeProcessHooks _hooks;
        private readonly Dictionary<int, Process> _descendants = new Dictionary<int, Process>();
        public NativeProcessResult Observation { get; } = new NativeProcessResult();
        // Live OS handle for an independent fixture observer, never part of a receipt.
        public IntPtr JobHandle { get; private set; }
        public Task<NativeProcessResult> Completion { get; private set; }
        private NativeProcessOwner(NativeProcessHooks hooks) { _hooks = hooks; }

        public static NativeProcessOwner Start(string executable, string[] arguments, string directory,
            int timeoutMilliseconds, string logPath, string[] environment, NativeProcessHooks hooks)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("Nightly native custody requires Windows.");
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            var owner = new NativeProcessOwner(hooks ?? new NativeProcessHooks());
            owner.Completion = Task.Run(() => owner.Run(executable, arguments, directory, timeoutMilliseconds, logPath, environment));
            return owner;
        }

        // Windows CommandLineToArgvW/CRT encoding, including empty tokens and trailing slashes.
        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { text.Append('\\', slashes * 2 + 1); text.Append(c); }
                else { text.Append('\\', slashes); text.Append(c); }
                slashes = 0;
            }
            text.Append('\\', slashes * 2); text.Append('"');
            return text.ToString();
        }

        private NativeProcessResult Run(string executable, string[] arguments, string directory,
            int timeout, string logPath, string[] environment)
        {
            IntPtr job = IntPtr.Zero, root = IntPtr.Zero, thread = IntPtr.Zero;
            IntPtr outRead = IntPtr.Zero, outWrite = IntPtr.Zero, errRead = IntPtr.Zero, errWrite = IntPtr.Zero;
            IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, attributes = IntPtr.Zero, handleList = IntPtr.Zero, envBlock = IntPtr.Zero;
            ReaderWork stdout = null, stderr = null;
            Task logWrite = null;
            var clock = Stopwatch.StartNew();
            bool resumed = false;
            string stdoutPath = logPath + ".stdout.tmp", stderrPath = logPath + ".stderr.tmp";
            try
            {
                job = CreateJobObjectW(IntPtr.Zero, null);
                Check(job != IntPtr.Zero, "CreateJobObject");
                JobHandle = job;
                var limits = new ExtendedLimits();
                limits.Basic.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE only; no breakaway.
                Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()), "SetInformationJobObject");
                Pipe(out outRead, out outWrite, true);
                Pipe(out errRead, out errWrite, true);
                Pipe(out inputRead, out inputWrite, false);
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attributes = Marshal.AllocHGlobal(size);
                Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size), "InitializeProcThreadAttributeList");
                handleList = Marshal.AllocHGlobal(IntPtr.Size * 3);
                Marshal.WriteIntPtr(handleList, 0, inputRead);
                Marshal.WriteIntPtr(handleList, IntPtr.Size, outWrite);
                Marshal.WriteIntPtr(handleList, IntPtr.Size * 2, errWrite);
                Check(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20002, handleList, (IntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute");
                var startup = new StartupInfoEx();
                startup.Startup.cb = Marshal.SizeOf<StartupInfoEx>();
                startup.Startup.flags = 0x100; // USESTDHANDLES
                startup.Startup.stdin = inputRead; startup.Startup.stdout = outWrite; startup.Startup.stderr = errWrite;
                startup.Attributes = attributes;
                // Null inherits via CreateProcess; even an empty explicit map replaces.
                if (environment != null)
                {
                    var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string item in environment)
                    {
                        int eq = item.IndexOf('=');
                        if (eq <= 0) throw new ArgumentException("Invalid native environment entry.");
                        string name = item.Substring(0, eq), value = item.Substring(eq + 1);
                        if (value.Length == 0) env.Remove(name);
                        else env[name] = value;
                    }
                    envBlock = Marshal.StringToHGlobalUni(string.Join("\0", env.Select(x => x.Key + "=" + x.Value)) + "\0\0");
                }
                var command = new StringBuilder(Quote(executable) + " " + string.Join(" ", (arguments ?? Array.Empty<string>()).Select(Quote)));
                ProcessInfo info;
                Check(CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                    0x4 | 0x400 | 0x80000 | 0x08000000, envBlock, directory, ref startup, out info), "CreateProcess suspended");
                root = info.Process; thread = info.Thread; Observation.Pid = (int)info.Pid;
                using (var identity = Process.GetProcessById(Observation.Pid)) Observation.RootStartedAtUtc = identity.StartTime.ToUniversalTime();
                Close(ref outWrite); Close(ref errWrite); Close(ref inputRead); Close(ref inputWrite);
                _hooks.Pause("assignment");
                Check(!_hooks.FailAssignment && AssignProcessToJobObject(job, root), "AssignProcessToJobObject");
                // Readers start before resume, and own only their respective read handles.
                stdout = Drain(outRead, stdoutPath, "stdout"); outRead = IntPtr.Zero;
                stderr = Drain(errRead, stderrPath, "stderr"); errRead = IntPtr.Zero;
                Check(ResumeThread(thread) != uint.MaxValue, "ResumeThread"); resumed = true;
                Close(ref thread);
                Observation.LaunchMilliseconds = clock.Elapsed.TotalMilliseconds;
                var execution = Stopwatch.StartNew();
                while (true)
                {
                    int active = _hooks.ZeroCountAtRootWait ? 0 : Count(job);
                    CaptureDescendants(job);
                    Observation.RemainingProcessCount = active;
                    bool rootExited = !_hooks.HoldRootWait() && RootExited(root);
                    Observation.ChildrenExited = rootExited && active == 0;
                    if (Observation.ChildrenExited) break;
                    if (execution.ElapsedMilliseconds >= timeout) { Observation.TimedOut = true; break; }
                    Thread.Sleep(10);
                }
                Observation.ExecutionMilliseconds = execution.Elapsed.TotalMilliseconds;
                var cleanup = Stopwatch.StartNew();
                if (Observation.TimedOut)
                {
                    Observation.TerminationRequested = true;
                    Observation.TerminationSucceeded = TerminateJobObject(job, 1);
                    Check(Observation.TerminationSucceeded, "TerminateJobObject");
                }
                while (!RootExited(root) || Count(job) != 0)
                {
                    if (cleanup.ElapsedMilliseconds >= 15000) throw new TimeoutException("Native job cleanup not observed within 15000ms.");
                    Thread.Sleep(10);
                }
                Observation.RemainingProcessCount = Count(job);
                Observation.ChildrenExited = RootExited(root) && Observation.RemainingProcessCount == 0;
                Observation.CleanupMilliseconds = cleanup.Elapsed.TotalMilliseconds;
                uint exit;
                Check(GetExitCodeProcess(root, out exit), "GetExitCodeProcess");
                Observation.ExitCode = Observation.TimedOut ? 1 : unchecked((int)exit);
                var drain = Stopwatch.StartNew();
                logWrite = Task.Run(() =>
                {
                    // Physical EOF and flushed temporary files permit the final write;
                    // reader completion is independently observed by the owner below.
                    Task.WhenAll(stdout.ContentReady.Task, stderr.ContentReady.Task).GetAwaiter().GetResult();
                    _hooks.Pause("log-write");
                    using (var output = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        foreach (string path in new[] { stdoutPath, stderrPath })
                            using (var input = File.OpenRead(path)) input.CopyTo(output);
                        output.Flush(true);
                    }
                });
                while (true)
                {
                    if (stdout.Completion.IsFaulted || stderr.Completion.IsFaulted || logWrite.IsFaulted)
                        Task.WhenAll(stdout.Completion, stderr.Completion, logWrite).GetAwaiter().GetResult();
                    Observation.OutputDrained = stdout.Completion.IsCompletedSuccessfully && stderr.Completion.IsCompletedSuccessfully && logWrite.IsCompletedSuccessfully;
                    if (Observation.OutputDrained) break;
                    Thread.Sleep(10);
                }
                Observation.DrainMilliseconds = drain.Elapsed.TotalMilliseconds;
                File.Delete(stdoutPath); File.Delete(stderrPath);
            }
            catch (Exception ex)
            {
                Observation.Error = ex.Message; Observation.ExitCode = 1;
                // Failed admission must kill the suspended root, which is not yet in our job.
                // A running root is contained: kill only our job, never a process-name sweep.
                if (root != IntPtr.Zero)
                {
                    Observation.TerminationRequested = true;
                    Observation.TerminationSucceeded = resumed ? TerminateJobObject(job, 1) : TerminateProcess(root, 1);
                    if (!Observation.TerminationSucceeded && !RootExited(root)) Close(ref job); // kill-on-close fallback
                    // Keep this foreground owner until the retained exact handles signal.
                    while (!RootExited(root) || _descendants.Values.Any(p => !p.HasExited)) Thread.Sleep(100);
                    if (job != IntPtr.Zero)
                    {
                        Observation.RemainingProcessCount = Count(job);
                        while (Observation.RemainingProcessCount != 0)
                        { Thread.Sleep(100); Observation.RemainingProcessCount = Count(job); }
                        Observation.ChildrenExited = RootExited(root) && Observation.RemainingProcessCount == 0;
                    }
                }
                if (stdout != null && stderr != null)
                {
                    try { Task.WhenAll(stdout.Completion, stderr.Completion).GetAwaiter().GetResult(); } catch { /* retain partial logs */ }
                }
                // No clean output verdict after any exception; retain temporary evidence.
            }
            finally
            {
                // Join test-operation barriers too, including when a broken verdict is
                // deliberately exercised. The observer can see that broken verdict.
                if (stdout != null && stderr != null)
                    try { Task.WhenAll(stdout.Completion, stderr.Completion).GetAwaiter().GetResult(); } catch { }
                if (logWrite != null) try { logWrite.GetAwaiter().GetResult(); } catch { }
                Observation.DescendantIdentities = _descendants.Values.Select(p => p.Id + ":" + p.StartTime.ToUniversalTime().ToString("O")).ToArray();
                foreach (var p in _descendants.Values) p.Dispose();
                Close(ref job); Close(ref root); Close(ref thread);
                JobHandle = IntPtr.Zero;
                Close(ref outRead); Close(ref outWrite); Close(ref errRead); Close(ref errWrite); Close(ref inputRead); Close(ref inputWrite);
                if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
                if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
                if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
            }
            return Observation;
        }

        private sealed class ReaderWork
        {
            public Task Completion;
            public TaskCompletionSource<bool> ContentReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private ReaderWork Drain(IntPtr handle, string path, string phase)
        {
            var reader = new ReaderWork();
            reader.Completion = Task.Run(() =>
            {
                try
                {
                    using (var pipe = new FileStream(new SafeFileHandle(handle, true), FileAccess.Read, 4096, false))
                    using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    { pipe.CopyTo(output); output.Flush(true); }
                    reader.ContentReady.SetResult(true);
                    _hooks.Pause(phase);
                }
                catch (Exception ex) { reader.ContentReady.TrySetException(ex); throw; }
            });
            return reader;
        }
        private void CaptureDescendants(IntPtr job)
        {
            // Job process-list query: grow rather than truncate the observed roster.
            int bytes = 256;
            while (true)
            {
                IntPtr buffer = Marshal.AllocHGlobal(bytes);
                try
                {
                    uint returned;
                    if (!QueryInformationJobObject(job, 3, buffer, (uint)bytes, out returned))
                    { if (Marshal.GetLastWin32Error() == 234) { bytes *= 2; continue; } Check(false, "Job process list"); }
                    int count = Marshal.ReadInt32(buffer, 4);
                    for (int i = 0; i < count; i++)
                    {
                        int pid = (int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size);
                        if (pid == Observation.Pid || _descendants.ContainsKey(pid)) continue;
                        try { var p = Process.GetProcessById(pid); _ = p.Handle; _ = p.StartTime; _descendants.Add(pid, p); }
                        catch (ArgumentException) { /* exited between the two independent OS reads */ }
                        catch (InvalidOperationException) { /* exited */ }
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        private static int Count(IntPtr job)
        {
            int size = Marshal.SizeOf<Accounting>(); IntPtr buffer = Marshal.AllocHGlobal(size);
            try { uint returned; Check(QueryInformationJobObject(job, 1, buffer, (uint)size, out returned), "Job active process count"); return (int)Marshal.PtrToStructure<Accounting>(buffer).Active; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        private static bool RootExited(IntPtr root) { uint value = WaitForSingleObject(root, 0); Check(value != uint.MaxValue, "Root wait"); return value == 0; }
        private static void Pipe(out IntPtr read, out IntPtr write, bool childWrites)
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
            Check(CreatePipe(out read, out write, ref security, 0), "CreatePipe");
            Check(SetHandleInformation(childWrites ? read : write, 1, 0), "SetHandleInformation");
        }
        private static void Close(ref IntPtr handle) { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
        private static void Check(bool ok, string operation) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }

        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int cb; public IntPtr reserved, desktop, title; public int x,y,cx,cy,charsX,charsY,fill,flags; public short show, reservedBytes; public IntPtr reserved2, stdin, stdout, stderr; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorking, MaxWorking; public uint ActiveLimit; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential)] private struct Accounting { public long User, Kernel, PeriodUser, PeriodKernel; public uint PageFaults, Total, Active, Terminated; }
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, uint size);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool QueryInformationJobObject(IntPtr job, int kind, IntPtr buffer, uint size, out uint returned);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
