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
        public string TrxPath { get; set; } = "";
        public string DiagnosticDirectory { get; set; } = "";
        public int ExecutionGraceMilliseconds { get; set; } = 60000;
        public string HangCapturePath { get; set; } = "";
        internal void Pause(string phase, CancellationToken cancellation = default(CancellationToken))
        {
            if (!Barriers.Contains(phase)) return;
            File.WriteAllText(Path.Combine(BarrierDirectory, phase + ".entered"), "entered");
            while (!File.Exists(Path.Combine(BarrierDirectory, phase + ".release")))
            { cancellation.ThrowIfCancellationRequested(); Thread.Sleep(10); }
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
        public bool OutputDrainTimedOut { get; set; }
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
        public string RootExitedAtUtc { get; set; } = "";
        public string[] DescendantImages { get; set; } = Array.Empty<string>();
        public string[] DescendantExitedAtUtc { get; set; } = Array.Empty<string>();
        public int[] MissedDescendantPids { get; set; } = Array.Empty<int>();
        public bool JobEmptyObserved { get; set; }
        public bool CompletedButHung { get; set; }
        public string HangCapturePath { get; set; } = "";
        public string HangCaptureError { get; set; } = "";
        public long ExecutionFinishedAtMilliseconds { get; set; } = -1;
    }

    public sealed class NativeProcessOwner
    {
        private const int OutputDrainTimeoutMilliseconds = 15000;
        private readonly CancellationTokenSource _drainCancellation = new CancellationTokenSource();
        private readonly NativeProcessHooks _hooks;
        private readonly Dictionary<int, Process> _descendants = new Dictionary<int, Process>();
        private readonly List<int> _missedDescendants = new List<int>();
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
            Stopwatch drain = null;
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
                // Desktop packaged apps can create unpackaged children outside this job,
                // even when this process itself was successfully assigned to it.
                uint packageLength = 0;
                int packageStatus = GetPackageFullName(root, ref packageLength, null);
                if (packageStatus != 15700) // APPMODEL_ERROR_NO_PACKAGE
                    throw new InvalidOperationException("Native job custody refuses packaged process images; use an unpackaged executable. Package query=" + packageStatus);
                _hooks.Pause("assignment");
                Check(!_hooks.FailAssignment && AssignProcessToJobObject(job, root), "AssignProcessToJobObject");
                // Readers start before resume, and own only their respective read handles.
                stdout = Drain(outRead, stdoutPath, "stdout"); outRead = IntPtr.Zero;
                stderr = Drain(errRead, stderrPath, "stderr"); errRead = IntPtr.Zero;
                Check(ResumeThread(thread) != uint.MaxValue, "ResumeThread"); resumed = true;
                Close(ref thread);
                Observation.LaunchMilliseconds = clock.Elapsed.TotalMilliseconds;
                var execution = Stopwatch.StartNew();
                long finishedAt = -1;
                long lastProbe = -500;
                int grace = _hooks.ExecutionGraceMilliseconds > 0 ? _hooks.ExecutionGraceMilliseconds : 60000;
                bool watchEvidence = !string.IsNullOrEmpty(_hooks.TrxPath) && !string.IsNullOrEmpty(_hooks.DiagnosticDirectory);
                while (true)
                {
                    int active = _hooks.ZeroCountAtRootWait ? 0 : Count(job);
                    CaptureDescendants(job);
                    Observation.RemainingProcessCount = active;
                    bool rootExited = !_hooks.HoldRootWait() && RootExited(root);
                    Observation.ChildrenExited = rootExited && active == 0;
                    if (Observation.ChildrenExited) break;
                    long now = execution.ElapsedMilliseconds;
                    // Same markers as Test-NightlyExecutionFinished: total ran plus request execute stop.
                    if (watchEvidence && finishedAt < 0 && now - lastProbe >= 500)
                    {
                        lastProbe = now;
                        if (ExecutionEvidenceFinished(_hooks.TrxPath, _hooks.DiagnosticDirectory))
                        {
                            finishedAt = now;
                            Observation.ExecutionFinishedAtMilliseconds = now;
                        }
                    }
                    if (finishedAt >= 0 && now - finishedAt >= grace)
                    {
                        Observation.TimedOut = true;
                        Observation.CompletedButHung = true;
                        break;
                    }
                    if (now >= timeout) { Observation.TimedOut = true; break; }
                    Thread.Sleep(10);
                }
                Observation.ExecutionMilliseconds = execution.Elapsed.TotalMilliseconds;
                var cleanup = Stopwatch.StartNew();
                if (Observation.TimedOut)
                {
                    try { WriteHangCapture(job, root, logPath); }
                    catch (Exception captureEx) { Observation.HangCaptureError = captureEx.Message; }
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
                drain = Stopwatch.StartNew();
                logWrite = Task.Run(() =>
                {
                    // Physical EOF and flushed temporary files permit the final write;
                    // reader completion is independently observed by the owner below.
                    Task.WhenAll(stdout.ContentReady.Task, stderr.ContentReady.Task).GetAwaiter().GetResult();
                    _hooks.Pause("log-write", _drainCancellation.Token);
                    using (var output = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        foreach (string path in new[] { stdoutPath, stderrPath })
                            using (var input = File.OpenRead(path)) input.CopyTo(output);
                        output.Flush(true);
                    }
                });
                while (true)
                {
                    foreach (var work in new[] { stdout.Completion, stderr.Completion, logWrite })
                        if (work.IsFaulted) work.GetAwaiter().GetResult();
                    Observation.OutputDrained = stdout.Completion.IsCompletedSuccessfully && stderr.Completion.IsCompletedSuccessfully && logWrite.IsCompletedSuccessfully;
                    if (Observation.OutputDrained) break;
                    if (drain.ElapsedMilliseconds >= OutputDrainTimeoutMilliseconds)
                    {
                        Observation.TimedOut = true;
                        Observation.OutputDrainTimedOut = true;
                        throw new TimeoutException("Native output drain not observed within " + OutputDrainTimeoutMilliseconds + "ms.");
                    }
                    Thread.Sleep(10);
                }
                Observation.DrainMilliseconds = drain.Elapsed.TotalMilliseconds;
                File.Delete(stdoutPath); File.Delete(stderrPath);
            }
            catch (Exception ex)
            {
                Observation.Error = ex.Message; Observation.ExitCode = 1;
                _drainCancellation.Cancel();
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
                if (drain != null) Observation.DrainMilliseconds = drain.Elapsed.TotalMilliseconds;
                _drainCancellation.Dispose();
                StampOwnerObservation(root);
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
                    using (var pipe = new SafeFileHandle(handle, true))
                    using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        // Anonymous pipes do not support cancellable overlapped reads.
                        // Poll availability and read only available bytes, so an external
                        // duplicated writer cannot strand a synchronous ReadFile forever.
                        var buffer = new byte[81920];
                        while (true)
                        {
                            _drainCancellation.Token.ThrowIfCancellationRequested();
                            uint available;
                            if (!PeekNamedPipe(pipe, IntPtr.Zero, 0, IntPtr.Zero, out available, IntPtr.Zero))
                            {
                                int error = Marshal.GetLastWin32Error();
                                if (error == 109 || error == 233) break; // broken/disconnected pipe: physical EOF
                                throw new Win32Exception(error, "PeekNamedPipe");
                            }
                            if (available == 0) { Thread.Sleep(10); continue; }
                            uint read;
                            Check(ReadFile(pipe, buffer, Math.Min((uint)buffer.Length, available), out read, IntPtr.Zero), "ReadFile");
                            if (read == 0) break;
                            output.Write(buffer, 0, (int)read);
                        }
                        output.Flush(true);
                    }
                    reader.ContentReady.SetResult(true);
                    _hooks.Pause(phase, _drainCancellation.Token);
                }
                catch (Exception ex) { reader.ContentReady.TrySetException(ex); throw; }
            });
            return reader;
        }
        private void RecordMissed(int pid)
        {
            if (!_missedDescendants.Contains(pid)) _missedDescendants.Add(pid);
        }

        private void StampOwnerObservation(IntPtr root)
        {
            Observation.MissedDescendantPids = _missedDescendants.ToArray();
            Observation.JobEmptyObserved = Observation.RemainingProcessCount == 0;
            if (root != IntPtr.Zero)
            {
                try
                {
                    if (RootExited(root))
                    {
                        long creation, exitFile, kernel, user;
                        if (GetProcessTimes(root, out creation, out exitFile, out kernel, out user) && exitFile > 0)
                            Observation.RootExitedAtUtc = DateTime.FromFileTimeUtc(exitFile).ToString("o");
                    }
                }
                catch (Exception) { }
            }
            var images = new List<string>();
            var exitStamps = new List<string>();
            foreach (var process in _descendants.Values)
            {
                string image = "";
                string exitStamp = "";
                try
                {
                    try { image = process.ProcessName; } catch (Exception) { image = ""; }
                    if (process.HasExited)
                    {
                        try { exitStamp = process.ExitTime.ToUniversalTime().ToString("o"); } catch (Exception) { exitStamp = ""; }
                    }
                }
                catch (Exception) { }
                if (image == null) image = "";
                if (exitStamp == null) exitStamp = "";
                images.Add(image);
                exitStamps.Add(exitStamp);
            }
            Observation.DescendantImages = images.ToArray();
            Observation.DescendantExitedAtUtc = exitStamps.ToArray();
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
                        try
                        {
                            var p = Process.GetProcessById(pid);
                            _ = p.Handle; _ = p.StartTime;
                            _descendants.Add(pid, p);
                            // Windows cannot query ProcessName after exit. Cache it while the
                            // retained generation is live; a missed name does not affect custody.
                            try { _ = p.ProcessName; } catch (Exception) { }
                        }
                        catch (ArgumentException) { RecordMissed(pid); }
                        catch (InvalidOperationException) { RecordMissed(pid); }
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        private void WriteHangCapture(IntPtr job, IntPtr root, string logPath)
        {
            string path = _hooks.HangCapturePath ?? "";
            if (path.Length == 0)
            {
                // Callers that did not ask for a hang file keep the timeout fixture quiet.
                if (logPath == null) Observation.HangCaptureError = "";
                return;
            }
            var text = new StringBuilder();
            text.AppendLine("completed-hang-capture");
            bool rootExited = false;
            try { rootExited = root != IntPtr.Zero && RootExited(root); } catch (Exception) { rootExited = false; }
            int active = -1;
            try { if (job != IntPtr.Zero) active = Count(job); } catch (Exception) { active = -1; }
            text.Append("root pid=").Append(Observation.Pid).Append(" exited=").Append(rootExited ? "true" : "false").Append(" active=").Append(active).AppendLine();
            var pids = new HashSet<int>();
            if (Observation.Pid > 0) pids.Add(Observation.Pid);
            try { if (job != IntPtr.Zero) CaptureDescendants(job); } catch (Exception) { }
            foreach (var process in _descendants.Values)
            {
                string image = "";
                bool exited = false;
                int handles = -1;
                try { image = process.ProcessName ?? ""; } catch (Exception) { image = ""; }
                try { exited = process.HasExited; } catch (Exception) { exited = false; }
                try { if (!exited) handles = process.HandleCount; } catch (Exception) { handles = -1; }
                pids.Add(process.Id);
                text.Append("descendant pid=").Append(process.Id).Append(" image=").Append(image)
                    .Append(" exited=").Append(exited ? "true" : "false")
                    .Append(" handles=").Append(handles).AppendLine();
            }
            AppendOpenHandles(text, pids);
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, text.ToString());
                Observation.HangCapturePath = path;
            }
            catch (Exception ex) { Observation.HangCaptureError = ex.Message; }
        }

        // SystemExtendedHandleInformation only. Resolving a handle name can block on a pipe, so names stay unlisted.
        private static void AppendOpenHandles(StringBuilder text, HashSet<int> pids)
        {
            const int SystemExtendedHandleInformation = 64;
            const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
            const int cap = 400;
            int bytes = 1 << 20;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                while (true)
                {
                    if (bytes <= 0 || bytes > (64 << 20))
                    {
                        text.AppendLine("handles=unavailable reason=buffer-cap");
                        return;
                    }
                    buffer = Marshal.AllocHGlobal(bytes);
                    int returned;
                    int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bytes, out returned);
                    if (status == StatusInfoLengthMismatch)
                    {
                        Marshal.FreeHGlobal(buffer);
                        buffer = IntPtr.Zero;
                        bytes *= 2;
                        continue;
                    }
                    if (status != 0)
                    {
                        text.Append("handles=unavailable reason=status-").Append(status.ToString("X8")).AppendLine();
                        return;
                    }
                    break;
                }
                long count = Marshal.ReadIntPtr(buffer).ToInt64();
                long stride = Marshal.SizeOf<HandleEntry>();
                long header = IntPtr.Size * 2L;
                if (count < 0 || stride <= 0 || header + count * stride > bytes)
                {
                    text.AppendLine("handles=unavailable reason=truncated");
                    return;
                }
                var seen = new Dictionary<int, int>();
                int listed = 0;
                for (long i = 0; i < count; i++)
                {
                    IntPtr slot = IntPtr.Add(buffer, (int)(header + i * stride));
                    HandleEntry row = Marshal.PtrToStructure<HandleEntry>(slot);
                    int pid = unchecked((int)row.UniqueProcessId.ToInt64());
                    if (!pids.Contains(pid)) continue;
                    int already;
                    seen.TryGetValue(pid, out already);
                    seen[pid] = already + 1;
                    if (already >= cap) continue;
                    long handle = row.HandleValue.ToInt64();
                    text.Append("handle pid=").Append(pid)
                        .Append(" value=0x").Append(handle.ToString("X"))
                        .Append(" type=").Append(row.ObjectTypeIndex).AppendLine();
                    listed++;
                }
                foreach (var pair in seen)
                {
                    if (pair.Value > cap)
                        text.Append("handles-truncated pid=").Append(pair.Key).Append(" omitted=").Append(pair.Value - cap).AppendLine();
                }
                if (listed == 0) text.AppendLine("handles=none");
            }
            catch (Exception ex)
            {
                text.Append("handles=unavailable reason=").Append(ex.GetType().Name).AppendLine();
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool ExecutionEvidenceFinished(string trxPath, string diagnosticDirectory)
        {
            int total, executed, notExecuted;
            string outcome;
            if (!TryReadTrxSummary(trxPath, out outcome, out total, out executed, out notExecuted)) return false;
            if (!string.Equals(outcome, "Completed", StringComparison.Ordinal)) return false;
            if (executed != total || notExecuted != 0) return false;
            string tail = ReadSharedTail(LatestDiagnosticFile(diagnosticDirectory), 16384);
            if (string.IsNullOrEmpty(tail)) return false;
            if (tail.IndexOf("request execute stop", StringComparison.Ordinal) < 0) return false;
            const string marker = "total ran:";
            int at = tail.LastIndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return false;
            int i = at + marker.Length;
            while (i < tail.Length && (tail[i] == ' ' || tail[i] == '\t')) i++;
            int j = i;
            while (j < tail.Length && tail[j] >= '0' && tail[j] <= '9') j++;
            int ran;
            if (j == i || !int.TryParse(tail.Substring(i, j - i), out ran)) return false;
            return ran == total;
        }

        private static bool TryReadTrxSummary(string path, out string outcome, out int total, out int executed, out int notExecuted)
        {
            outcome = "";
            total = -1;
            executed = -1;
            notExecuted = -1;
            try
            {
                string text = ReadSharedTail(path, 2 << 20);
                if (string.IsNullOrEmpty(text)) return false;
                int summary = text.LastIndexOf("ResultSummary", StringComparison.Ordinal);
                if (summary < 0) return false;
                int summaryEnd = text.IndexOf('>', summary);
                if (summaryEnd < 0) return false;
                outcome = XmlAttr(text.Substring(summary, summaryEnd - summary), "outcome");
                int counters = text.IndexOf("Counters", summary, StringComparison.Ordinal);
                if (counters < 0 || counters > summaryEnd + 4096) return false;
                int countersEnd = text.IndexOf('>', counters);
                if (countersEnd < 0) return false;
                string open = text.Substring(counters, countersEnd - counters);
                return int.TryParse(XmlAttr(open, "total"), out total)
                    && int.TryParse(XmlAttr(open, "executed"), out executed)
                    && int.TryParse(XmlAttr(open, "notExecuted"), out notExecuted);
            }
            catch (Exception) { return false; }
        }

        private static string XmlAttr(string open, string name)
        {
            string key = name + "=\"";
            int start = open.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return "";
            start += key.Length;
            int end = open.IndexOf('"', start);
            if (end < 0) return "";
            return open.Substring(start, end - start);
        }

        private static string LatestDiagnosticFile(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return "";
                string best = "";
                DateTime bestTime = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (!file.EndsWith(".diag", StringComparison.OrdinalIgnoreCase)) continue;
                    DateTime stamp = File.GetLastWriteTimeUtc(file);
                    if (best.Length == 0 || stamp >= bestTime) { best = file; bestTime = stamp; }
                }
                return best;
            }
            catch (Exception) { return ""; }
        }

        private static string ReadSharedTail(string path, int byteCount)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || byteCount <= 0 || !File.Exists(path)) return "";
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = stream.Length;
                    if (length <= 0) return "";
                    int take = byteCount;
                    if (take > length) take = (int)length;
                    stream.Seek(length - take, SeekOrigin.Begin);
                    var buffer = new byte[take];
                    int read = stream.Read(buffer, 0, take);
                    if (read <= 0) return "";
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch (Exception) { return ""; }
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
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool PeekNamedPipe(SafeFileHandle pipe, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadFile(SafeFileHandle pipe, byte[] buffer, uint size, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
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
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int returned);
        [StructLayout(LayoutKind.Sequential)] private struct HandleEntry { public IntPtr Object; public IntPtr UniqueProcessId; public IntPtr HandleValue; public uint GrantedAccess; public ushort CreatorBackTraceIndex; public ushort ObjectTypeIndex; public uint HandleAttributes; public uint Reserved; }
    }
}
