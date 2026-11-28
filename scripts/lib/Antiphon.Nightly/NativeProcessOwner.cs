// CARD-1039 S1n-a. Windows native owner, compiled into the nightly core since W2-nightly S04 (it was
// scripts/lib/nightly-owned-process.cs, added with Add-Type). OwnedLauncher (NightlyLaunch.cs) starts it; its PowerShell starter,
// nightly-owned-process.ps1, went with the live lane's run (W2 slice S21). NightlyNativeOwnershipTests drives it in-process. Windows only:
// Start refuses another host before any native call. ASCII only.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        public bool FailJobTermination { get; set; }
        public int CleanupTimeoutMilliseconds { get; set; } = NativeProcessOwner.DefaultCleanupTimeoutMilliseconds;
        public bool ZeroCountAtRootWait { get; set; }
        public string TrxPath { get; set; } = "";
        public string DiagnosticDirectory { get; set; } = "";
        public int ExecutionGraceMilliseconds { get; set; } = NightlyCompletionGrace.DefaultMilliseconds;
        public string HangCapturePath { get; set; } = "";
        public NightlyChunkIdleMonitor IdleMonitor { get; set; }
        public string ActiveHeartbeatPath { get; set; } = "";
        // Fixture launches can supply input and clean their job as soon as the root exits.
        public string StandardInputPath { get; set; } = "";
        public bool TerminateDescendantsOnRootExit { get; set; }
        internal void Pause(string phase, CancellationToken cancellation = default(CancellationToken), string evidence = "entered")
        {
            if (!Barriers.Contains(phase)) return;
            File.WriteAllText(Path.Combine(BarrierDirectory, phase + ".entered"), evidence);
            while (!File.Exists(Path.Combine(BarrierDirectory, phase + ".release")))
            { cancellation.ThrowIfCancellationRequested(); Thread.Sleep(10); }
        }
        internal bool HoldWait(string phase)
        {
            if (!Barriers.Contains(phase)) return false;
            File.WriteAllText(Path.Combine(BarrierDirectory, phase + ".entered"), "entered");
            return !File.Exists(Path.Combine(BarrierDirectory, phase + ".release"));
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
        // The merged log writes the stdout bytes before any stderr bytes.
        public long StandardOutputBytes { get; set; }
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
        public string IdleHangReason { get; set; } = "";
        public string HangCapturePath { get; set; } = "";
        public string HangCaptureError { get; set; } = "";
        public long ExecutionFinishedAtMilliseconds { get; set; } = -1;
        public double ExecutionStopToExitSeconds { get; set; } = -1;
    }

    public sealed class NativeProcessOwner
    {
        internal const int DefaultCleanupTimeoutMilliseconds = 15000;
        private const int OutputDrainTimeoutMilliseconds = 15000;
        private const int ParentProcessAttribute = 0x20000;
        private const int HandleListAttribute = 0x20002;
        private const uint CreateSuspended = 0x4;
        private const uint CreateUnicodeEnvironment = 0x400;
        private const uint ExtendedStartupInfoPresent = 0x80000;
        private const uint CreateNoWindow = 0x08000000;
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
            // WSL recognizes switches such as -e before parsing the remaining command line.
            if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
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
            IntPtr parentAttribute = IntPtr.Zero;
            ProcessInfo carrier = default;
            bool attributesInitialized = false;
            ReaderWork stdout = null, stderr = null;
            Task logWrite = null;
            Stopwatch drain = null;
            Stopwatch cleanup = null;
            int cleanupBudget = _hooks.CleanupTimeoutMilliseconds > 0
                ? _hooks.CleanupTimeoutMilliseconds : DefaultCleanupTimeoutMilliseconds;
            var drainCancellation = _drainCancellation.Token;
            var clock = Stopwatch.StartNew();
            bool resumed = false;
            string stdoutPath = logPath + ".stdout.tmp", stderrPath = logPath + ".stderr.tmp";
            void ClearActiveHeartbeat()
            {
                if (string.IsNullOrEmpty(_hooks.ActiveHeartbeatPath)) return;
                try { File.Delete(_hooks.ActiveHeartbeatPath); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            try
            {
                ClearActiveHeartbeat();
                job = CreateJobObjectW(IntPtr.Zero, null);
                Check(job != IntPtr.Zero, "CreateJobObject");
                JobHandle = job;
                var limits = new ExtendedLimits();
                limits.Basic.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE only; no breakaway.
                Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()), "SetInformationJobObject");
                bool fileInput = !string.IsNullOrEmpty(_hooks.StandardInputPath);
                Pipe(out outRead, out outWrite, true, inheritChildEnd: !fileInput);
                Pipe(out errRead, out errWrite, true, inheritChildEnd: !fileInput);
                using var inputFile = fileInput
                    ? new FileStream(_hooks.StandardInputPath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                if (!fileInput) Pipe(out inputRead, out inputWrite, false);
                IntPtr localInput = inputFile?.SafeFileHandle.DangerousGetHandle() ?? inputRead;
                var startup = new StartupInfoEx();
                startup.Startup.cb = Marshal.SizeOf<StartupInfoEx>();
                startup.Startup.flags = 0x100; // USESTDHANDLES
                startup.Startup.stdin = localInput;
                startup.Startup.stdout = outWrite;
                startup.Startup.stderr = errWrite;

                // HANDLE_LIST still requires inheritable entries. Put those entries in a suspended
                // process so a concurrent CreateProcess in this process cannot collect them.
                if (fileInput)
                {
                    StartHandleCarrier(job, out carrier);
                    startup.Startup.stdin = DuplicateForChild(carrier.Process, localInput);
                    startup.Startup.stdout = DuplicateForChild(carrier.Process, outWrite);
                    startup.Startup.stderr = DuplicateForChild(carrier.Process, errWrite);
                }
                int attributeCount = fileInput ? 2 : 1;
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, attributeCount, 0, ref size);
                attributes = Marshal.AllocHGlobal(size);
                Check(InitializeProcThreadAttributeList(attributes, attributeCount, 0, ref size), "InitializeProcThreadAttributeList");
                attributesInitialized = true;
                if (fileInput)
                {
                    parentAttribute = Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(parentAttribute, carrier.Process);
                    Check(UpdateProcThreadAttribute(attributes, 0, (IntPtr)ParentProcessAttribute, parentAttribute, (IntPtr)IntPtr.Size,
                        IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute parent process");
                }
                handleList = Marshal.AllocHGlobal(IntPtr.Size * 3);
                Marshal.WriteIntPtr(handleList, 0, startup.Startup.stdin);
                Marshal.WriteIntPtr(handleList, IntPtr.Size, startup.Startup.stdout);
                Marshal.WriteIntPtr(handleList, IntPtr.Size * 2, startup.Startup.stderr);
                Check(UpdateProcThreadAttribute(attributes, 0, (IntPtr)HandleListAttribute, handleList, (IntPtr)(IntPtr.Size * 3),
                    IntPtr.Zero, IntPtr.Zero), "UpdateProcThreadAttribute standard handles");
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
                _hooks.Pause("create-process", evidence: localInput.ToInt64().ToString(CultureInfo.InvariantCulture));
                Check(CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true,
                    CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfoPresent | CreateNoWindow,
                    envBlock, directory, ref startup, out info), "CreateProcess suspended");
                root = info.Process; thread = info.Thread; Observation.Pid = (int)info.Pid;
                inputFile?.Dispose();
                Close(ref outWrite); Close(ref errWrite); Close(ref inputRead); Close(ref inputWrite);
                if (fileInput)
                {
                    CloseCarrierHandle(carrier.Process, startup.Startup.stdin);
                    CloseCarrierHandle(carrier.Process, startup.Startup.stdout);
                    CloseCarrierHandle(carrier.Process, startup.Startup.stderr);
                    StopHandleCarrier(ref carrier);
                }
                using (var identity = Process.GetProcessById(Observation.Pid)) Observation.RootStartedAtUtc = identity.StartTime.ToUniversalTime();
                // Desktop packaged apps can create unpackaged children outside this job,
                // even when this process itself was successfully assigned to it.
                uint packageLength = 0;
                int packageStatus = GetPackageFullName(root, ref packageLength, null);
                if (packageStatus != 15700) // APPMODEL_ERROR_NO_PACKAGE
                    throw new InvalidOperationException("Native job custody refuses packaged process images; use an unpackaged executable. Package query=" + packageStatus);
                _hooks.Pause("assignment");
                if (fileInput)
                {
                    // Windows inherits the chosen parent's job. Verify custody before resume.
                    Check(IsProcessInJob(root, job, out bool contained), "IsProcessInJob");
                    if (_hooks.FailAssignment || !contained)
                        throw new InvalidOperationException("AssignProcessToJobObject: inherited private job membership not established.");
                }
                else
                    Check(!_hooks.FailAssignment && AssignProcessToJobObject(job, root), "AssignProcessToJobObject");
                // Readers start before resume, and own only their respective read handles.
                stdout = Drain(outRead, stdoutPath, "stdout"); outRead = IntPtr.Zero;
                stderr = Drain(errRead, stderrPath, "stderr"); errRead = IntPtr.Zero;
                Check(ResumeThread(thread) != uint.MaxValue, "ResumeThread"); resumed = true;
                Close(ref thread);
                Observation.LaunchMilliseconds = clock.Elapsed.TotalMilliseconds;
                int grace = _hooks.ExecutionGraceMilliseconds > 0 ? _hooks.ExecutionGraceMilliseconds : NightlyCompletionGrace.DefaultMilliseconds;
                bool watchEvidence = !string.IsNullOrEmpty(_hooks.TrxPath) && !string.IsNullOrEmpty(_hooks.DiagnosticDirectory);
                DateTime notBefore = Observation.RootStartedAtUtc;
                QualificationHeartbeatRecord SampleIdle()
                {
                    double? cpuSeconds = null;
                    if (GetProcessTimes(root, out _, out var exit, out var kernel, out var user) && exit == 0)
                        cpuSeconds = (kernel + user) / (double)TimeSpan.TicksPerSecond;
                    var beat = _hooks.IdleMonitor.Sample(Observation.Pid, cpuSeconds);
                    if (beat != null && !string.IsNullOrEmpty(_hooks.ActiveHeartbeatPath))
                    {
                        try { QualificationHeartbeat.Write(beat, _hooks.ActiveHeartbeatPath); }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                    }
                    return beat;
                }
                bool stopOwnedChildren = false;
                CompletionGraceResult graceResult = NightlyCompletionGrace.Wait(
                    () =>
                    {
                        int active = _hooks.ZeroCountAtRootWait ? 0 : Count(job);
                        CaptureDescendants(job);
                        Observation.RemainingProcessCount = active;
                        bool rootExited = !_hooks.HoldWait("root-wait") && RootExited(root);
                        Observation.ChildrenExited = rootExited && active == 0;
                        stopOwnedChildren = _hooks.TerminateDescendantsOnRootExit && rootExited && active > 0;
                        return Observation.ChildrenExited || stopOwnedChildren;
                    },
                    watchEvidence ? (Func<bool>)(() => ExecutionEvidenceFinished(_hooks.TrxPath, _hooks.DiagnosticDirectory, notBefore)) : null,
                    grace,
                    timeout,
                    10,
                    _hooks.IdleMonitor != null ? SampleIdle : null);
                // A host that exits inside the probe interval still has the markers on disk.
                if (graceResult.ExecutionFinishedAtMilliseconds < 0 && watchEvidence && Observation.ChildrenExited
                    && ExecutionEvidenceFinished(_hooks.TrxPath, _hooks.DiagnosticDirectory, notBefore))
                {
                    graceResult.ExecutionFinishedAtMilliseconds = graceResult.ElapsedMilliseconds;
                }
                Observation.ExecutionFinishedAtMilliseconds = graceResult.ExecutionFinishedAtMilliseconds;
                Observation.IdleHangReason = graceResult.IdleHangReason;
                if (graceResult.CompletedButHung)
                {
                    Observation.TimedOut = true;
                    Observation.CompletedButHung = true;
                }
                else if (graceResult.TimedOut)
                {
                    Observation.TimedOut = true;
                }
                Observation.ExecutionMilliseconds = graceResult.ElapsedMilliseconds;
                if (Observation.TimedOut)
                {
                    try { WriteHangCapture(job, root, logPath); }
                    catch (Exception captureEx) { Observation.HangCaptureError = captureEx.Message; }
                }
                bool terminate = Observation.TimedOut || stopOwnedChildren;
                if (terminate) _hooks.Pause("job-termination");
                cleanup = Stopwatch.StartNew();
                if (terminate)
                {
                    Observation.TerminationRequested = true;
                    Observation.TerminationSucceeded = TryTerminateJob(job);
                    Check(Observation.TerminationSucceeded, "TerminateJobObject");
                }
                while (!RootExited(root) || Count(job) != 0)
                {
                    if (TimeLeft(cleanup, cleanupBudget) == 0)
                        throw new TimeoutException("Native job cleanup not observed within " + cleanupBudget + "ms.");
                    Thread.Sleep(10);
                }
                Observation.RemainingProcessCount = Count(job);
                Observation.ChildrenExited = RootExited(root) && Observation.RemainingProcessCount == 0;
                Observation.CleanupMilliseconds = cleanup.Elapsed.TotalMilliseconds;
                uint exit;
                Check(GetExitCodeProcess(root, out exit), "GetExitCodeProcess");
                Observation.ExitCode = Observation.TimedOut ? 1 : unchecked((int)exit);
                if (Observation.ExecutionFinishedAtMilliseconds >= 0)
                {
                    string diag = LatestDiagnosticFile(_hooks.DiagnosticDirectory);
                    DateTime exitUtc = DateTime.UtcNow;
                    long createdTicks, exitTicks, kernelTicks, userTicks;
                    if (root != IntPtr.Zero && GetProcessTimes(root, out createdTicks, out exitTicks, out kernelTicks, out userTicks) && exitTicks > 0)
                        exitUtc = DateTime.FromFileTimeUtc(exitTicks);
                    Observation.ExecutionStopToExitSeconds = NightlyCompletionGrace.MeasureFileStopToExitSeconds(diag, exitUtc);
                }
                drain = Stopwatch.StartNew();
                logWrite = Task.Run(() =>
                {
                    // Physical EOF and flushed temporary files permit the final write;
                    // reader completion is independently observed by the owner below.
                    Task.WhenAll(stdout.ContentReady.Task, stderr.ContentReady.Task).GetAwaiter().GetResult();
                    _hooks.Pause("log-write", drainCancellation);
                    using (var output = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        using (var input = File.OpenRead(stdoutPath)) input.CopyTo(output);
                        Observation.StandardOutputBytes = output.Position;
                        using (var input = File.OpenRead(stderrPath)) input.CopyTo(output);
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
                if (Observation.ExecutionStopToExitSeconds >= 0)
                    NightlyCompletionGrace.AppendStopToExitLine(logPath, Observation.ExecutionStopToExitSeconds);
                File.Delete(stdoutPath); File.Delete(stderrPath);
            }
            catch (Exception ex)
            {
                Observation.Error = ex.Message; Observation.ExitCode = 1;
                Observation.OutputDrained = false;
                cleanup ??= Stopwatch.StartNew();
                _drainCancellation.Cancel();
                try
                {
                    if (root != IntPtr.Zero) FinishFailedCleanup(root, ref job, resumed, cleanup, cleanupBudget);
                }
                catch (Exception cleanupError)
                {
                    Observation.Error += "; Native job cleanup observation failed: " + cleanupError.Message;
                }
                Observation.CleanupMilliseconds = cleanup.Elapsed.TotalMilliseconds;
                var pendingOutput = new[] { stdout?.Completion, stderr?.Completion, logWrite }.Where(work => work != null);
                try
                {
                    if (!Task.WhenAll(pendingOutput).Wait(TimeLeft(cleanup, cleanupBudget)))
                    {
                        Observation.TimedOut = true;
                        Observation.OutputDrainTimedOut = true;
                        Observation.Error += "; Native error-path output drain exceeded the remaining cleanup budget.";
                    }
                }
                catch (AggregateException) { /* retain partial logs and original failure */ }
                // No clean output verdict after any exception; retain temporary evidence.
            }
            finally
            {
                // Retain the idle verdict for the observer's next poll or until the next native chunk starts.
                if (string.IsNullOrEmpty(Observation.IdleHangReason)) ClearActiveHeartbeat();
                if (drain != null) Observation.DrainMilliseconds = drain.Elapsed.TotalMilliseconds;
                _drainCancellation.Dispose();
                StampOwnerObservation(root);
                Observation.DescendantIdentities = _descendants.Values.Select(p => p.Id + ":" + p.StartTime.ToUniversalTime().ToString("O")).ToArray();
                foreach (var p in _descendants.Values) p.Dispose();
                try { StopHandleCarrier(ref carrier); }
                catch (Exception e) { Observation.Error += "; " + e.Message; }
                Close(ref job); Close(ref root); Close(ref thread);
                JobHandle = IntPtr.Zero;
                Close(ref outRead); Close(ref outWrite); Close(ref errRead); Close(ref errWrite); Close(ref inputRead); Close(ref inputWrite);
                if (attributesInitialized) DeleteProcThreadAttributeList(attributes);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                if (parentAttribute != IntPtr.Zero) Marshal.FreeHGlobal(parentAttribute);
                if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
                if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
            }
            return Observation;
        }

        private bool TryTerminateJob(IntPtr job) => !_hooks.FailJobTermination && TerminateJobObject(job, 1);

        private void FinishFailedCleanup(IntPtr root, ref IntPtr job, bool resumed, Stopwatch cleanup, int budget)
        {
            Observation.ChildrenExited = false;
            Observation.RemainingProcessCount = -1;
            Observation.TerminationRequested = true;
            // An unadmitted suspended root may still be outside our job.
            Observation.TerminationSucceeded = resumed ? TryTerminateJob(job) : TerminateProcess(root, 1);
            if (!Observation.TerminationSucceeded)
            {
                // Closing the last private job handle kills the job even if the root already exited.
                JobHandle = IntPtr.Zero;
                Close(ref job);
            }

            while (true)
            {
                Observation.RemainingProcessCount = job == IntPtr.Zero ? -1 : Count(job);
                if (!_hooks.HoldWait("failure-cleanup") && Observation.RemainingProcessCount <= 0
                    && RootExited(root) && _descendants.Values.All(process => process.HasExited))
                {
                    Observation.ChildrenExited = Observation.RemainingProcessCount == 0;
                    return;
                }
                int remaining = TimeLeft(cleanup, budget);
                if (remaining == 0)
                {
                    Observation.TimedOut = true;
                    string message = "Native job cleanup not observed within " + budget + "ms.";
                    if (!Observation.Error.Contains(message, StringComparison.Ordinal)) Observation.Error += "; " + message;
                    return;
                }
                Thread.Sleep(Math.Min(remaining, 10));
            }
        }

        private static int TimeLeft(Stopwatch clock, int budget) => (int)Math.Max(0, budget - clock.ElapsedMilliseconds);

        private sealed class ReaderWork
        {
            public Task Completion;
            public TaskCompletionSource<bool> ContentReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private ReaderWork Drain(IntPtr handle, string path, string phase)
        {
            var reader = new ReaderWork();
            var cancellation = _drainCancellation.Token;
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
                            cancellation.ThrowIfCancellationRequested();
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
                    _hooks.Pause(phase, cancellation);
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
            if (string.IsNullOrEmpty(Observation.IdleHangReason)) text.AppendLine("completed-hang-capture");
            else text.AppendLine("idle-hang-capture").AppendLine(Observation.IdleHangReason);
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
            return ExecutionEvidenceFinished(trxPath, diagnosticDirectory, DateTime.MinValue);
        }

        private static bool ExecutionEvidenceFinished(string trxPath, string diagnosticDirectory, DateTime notBeforeUtc)
        {
            if (!EvidenceIsCurrent(trxPath, notBeforeUtc)) return false;
            string diagnostic = ResolveDiagnostic(diagnosticDirectory);
            if (!EvidenceIsCurrent(diagnostic, notBeforeUtc)) return false;
            int total, executed, notExecuted, failed;
            string outcome;
            if (!TryReadTrxSummary(trxPath, out outcome, out total, out executed, out notExecuted, out failed)) return false;
            if (!string.Equals(outcome, "Completed", StringComparison.Ordinal)) return false;
            if (failed > 0) return false;
            if (executed != total || notExecuted != 0) return false;
            string tail = ReadSharedTail(diagnostic, 16384);
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
            if (j == i || !int.TryParse(tail.Substring(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out ran)) return false;
            return ran == total;
        }

        // A rerun into the same log root leaves the previous TRX and diagnostic in place.
        // Evidence written at or before the new process start is not this run.
        private static bool EvidenceIsCurrent(string path, DateTime notBeforeUtc)
        {
            if (notBeforeUtc == DateTime.MinValue) return true;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                return File.GetLastWriteTimeUtc(path) > notBeforeUtc;
            }
            catch (Exception) { return false; }
        }

        private static bool TryReadTrxSummary(string path, out string outcome, out int total, out int executed, out int notExecuted, out int failed)
        {
            outcome = "";
            total = -1;
            executed = -1;
            notExecuted = -1;
            failed = -1;
            try
            {
                string text = ReadSharedTail(path, 2 << 20);
                if (string.IsNullOrEmpty(text)) return false;
                // The closing tag is </ResultSummary>. LastIndexOf("ResultSummary") lands there
                // and reads no outcome. Match the opening tag, including a namespace prefix.
                int summary = LastIndexOfOpeningTag(text, "ResultSummary");
                if (summary < 0) return false;
                int summaryEnd = text.IndexOf('>', summary);
                if (summaryEnd < 0) return false;
                outcome = XmlAttr(text.Substring(summary, summaryEnd - summary), "outcome");
                int window = summaryEnd + 4096;
                if (window > text.Length) window = text.Length;
                int counters = IndexOfOpeningTag(text, "Counters", summary, window);
                if (counters < 0) return false;
                int countersEnd = text.IndexOf('>', counters);
                if (countersEnd < 0) return false;
                string open = text.Substring(counters, countersEnd - counters);
                string failedText = XmlAttr(open, "failed");
                if (failedText.Length == 0) failed = 0;
                else if (!int.TryParse(failedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out failed)) return false;
                return int.TryParse(XmlAttr(open, "total"), NumberStyles.Integer, CultureInfo.InvariantCulture, out total)
                    && int.TryParse(XmlAttr(open, "executed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out executed)
                    && int.TryParse(XmlAttr(open, "notExecuted"), NumberStyles.Integer, CultureInfo.InvariantCulture, out notExecuted);
            }
            catch (Exception) { return false; }
        }

        private static int LastIndexOfOpeningTag(string text, string localName)
        {
            int found = -1;
            int from = 0;
            while (from < text.Length)
            {
                int at = IndexOfOpeningTag(text, localName, from, text.Length);
                if (at < 0) break;
                found = at;
                from = at + 1;
            }
            return found;
        }

        private static int IndexOfOpeningTag(string text, string localName, int start, int end)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(localName)) return -1;
            if (start < 0) start = 0;
            if (end > text.Length) end = text.Length;
            int from = start;
            while (from < end)
            {
                int at = text.IndexOf(localName, from, end - from, StringComparison.Ordinal);
                if (at < 0) return -1;
                int after = at + localName.Length;
                if (after < text.Length && !IsTagBoundary(text[after])) { from = at + 1; continue; }
                int i = at - 1;
                if (i >= start && text[i] == ':')
                {
                    i--;
                    while (i >= start && IsNameChar(text[i])) i--;
                }
                if (i >= start && text[i] == '<' && i + 1 < text.Length && text[i + 1] != '/') return i;
                from = at + 1;
            }
            return -1;
        }

        private static bool IsTagBoundary(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '>' || c == '/';
        }

        private static bool IsNameChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
        }

        private static string XmlAttr(string open, string name)
        {
            if (string.IsNullOrEmpty(open) || string.IsNullOrEmpty(name)) return "";
            int at = 0;
            while (at < open.Length)
            {
                int nameAt = open.IndexOf(name, at, StringComparison.Ordinal);
                if (nameAt < 0) return "";
                if (nameAt > 0 && !IsXmlSpace(open[nameAt - 1]) && open[nameAt - 1] != '<')
                {
                    at = nameAt + name.Length;
                    continue;
                }
                int i = nameAt + name.Length;
                while (i < open.Length && IsXmlSpace(open[i])) i++;
                if (i >= open.Length || open[i] != '=') { at = nameAt + name.Length; continue; }
                i++;
                while (i < open.Length && IsXmlSpace(open[i])) i++;
                if (i >= open.Length) return "";
                char quote = open[i];
                if (quote != '"' && quote != '\'') { at = nameAt + name.Length; continue; }
                int value = i + 1;
                int end = open.IndexOf(quote, value);
                if (end < 0) return "";
                return open.Substring(value, end - value);
            }
            return "";
        }

        private static bool IsXmlSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        private static string ResolveDiagnostic(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "";
                if (File.Exists(path)) return path;
                if (Directory.Exists(path)) return LatestDiagnosticFile(path);
                return "";
            }
            catch (Exception) { return ""; }
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
        private static void Pipe(out IntPtr read, out IntPtr write, bool childWrites, bool inheritChildEnd = true)
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = inheritChildEnd ? 1 : 0 };
            Check(CreatePipe(out read, out write, ref security, 0), "CreatePipe");
            if (inheritChildEnd) Check(SetHandleInformation(childWrites ? read : write, 1, 0), "SetHandleInformation");
        }

        private static void StartHandleCarrier(IntPtr job, out ProcessInfo carrier)
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            Check(CreateProcessW(Path.Combine(Environment.SystemDirectory, "cmd.exe"), null, IntPtr.Zero, IntPtr.Zero,
                false, CreateSuspended | CreateNoWindow, IntPtr.Zero, null, ref startup, out carrier), "CreateProcess suspended handle carrier");
            Check(AssignProcessToJobObject(job, carrier.Process), "AssignProcessToJobObject handle carrier");
        }

        private static IntPtr DuplicateForChild(IntPtr carrier, IntPtr handle)
        {
            Check(DuplicateHandle(GetCurrentProcess(), handle, carrier, out var duplicate, 0, true, 2), "DuplicateHandle for launch");
            return duplicate;
        }

        private static void CloseCarrierHandle(IntPtr carrier, IntPtr handle)
        {
            const uint duplicateCloseSource = 1;
            Check(DuplicateHandle(carrier, handle, IntPtr.Zero, IntPtr.Zero, 0, false, duplicateCloseSource),
                "DuplicateHandle close launch copy");
        }

        private void StopHandleCarrier(ref ProcessInfo carrier)
        {
            if (carrier.Process == IntPtr.Zero) return;
            try
            {
                // The carrier never runs. Its exit also releases any copies left by a failed launch.
                Check(TerminateProcess(carrier.Process, 1) || RootExited(carrier.Process), "TerminateProcess handle carrier");
                var wait = WaitForCarrierExit(carrier.Process, DefaultCleanupTimeoutMilliseconds);
                if (wait == 258) throw new TimeoutException("Suspended handle carrier did not exit within the cleanup budget.");
                Check(wait == 0, "WaitForSingleObject handle carrier");
            }
            finally
            {
                Close(ref carrier.Thread);
                Close(ref carrier.Process);
            }
        }

        private uint WaitForCarrierExit(IntPtr carrier, int milliseconds)
        {
            if (!_hooks.HoldWait("carrier-exit")) return WaitForSingleObject(carrier, (uint)milliseconds);
            // Hold the exit observation for this wait, after the real carrier termination request.
            Thread.Sleep(milliseconds);
            return 258;
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
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool contained);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr target, uint access, bool inherit, uint options);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, IntPtr target, uint access, bool inherit, uint options);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);
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

    public sealed class CompletionGraceResult
    {
        public bool TimedOut { get; set; }
        public bool CompletedButHung { get; set; }
        public string IdleHangReason { get; set; } = "";
        public long ElapsedMilliseconds { get; set; }
        public long ExecutionFinishedAtMilliseconds { get; set; } = -1;
    }

    public static class NightlyCompletionGrace
    {
        // Unmeasured placeholder. Chunk logs record execution-stop-to-exit-seconds
        // for a normal exit and for this kill. Set this near 2x that p95
        // (docs/test-performance-standards.md).
        public const int DefaultMilliseconds = 60000;
        public const int ProbeIntervalMilliseconds = 500;

        public static CompletionGraceResult Wait(Func<bool> childrenExited, Func<bool> evidenceFinished, int graceMilliseconds, int watchdogMilliseconds,
            int pollMilliseconds, Func<QualificationHeartbeatRecord> sampleIdle = null)
        {
            if (pollMilliseconds < 1) pollMilliseconds = 1;
            var clock = Stopwatch.StartNew();
            long finishedAt = -1;
            long lastProbe = -500;
            long lastIdleProbe = -NightlyChunkIdleMonitor.PollMilliseconds;
            bool timedOut = false;
            bool hung = false;
            var idleHangReason = "";
            while (true)
            {
                bool gone = false;
                if (childrenExited != null) gone = childrenExited();
                if (gone) break;
                long now = clock.ElapsedMilliseconds;
                // Same markers as Test-NightlyExecutionFinished: total ran plus request execute stop.
                if (evidenceFinished != null && finishedAt < 0 && now - lastProbe >= ProbeIntervalMilliseconds)
                {
                    lastProbe = now;
                    if (evidenceFinished()) finishedAt = now;
                }
                if (finishedAt >= 0 && now - finishedAt >= graceMilliseconds)
                {
                    timedOut = true;
                    hung = true;
                    break;
                }
                if (now >= watchdogMilliseconds)
                {
                    timedOut = true;
                    break;
                }
                if (sampleIdle != null && finishedAt < 0 && now - lastIdleProbe >= NightlyChunkIdleMonitor.PollMilliseconds)
                {
                    lastIdleProbe = now;
                    var beat = sampleIdle();
                    if (beat?.HangSuspected == true)
                    {
                        if (childrenExited?.Invoke() == true) break;
                        idleHangReason = beat.HangReason;
                        timedOut = true;
                        break;
                    }
                }
                Thread.Sleep(pollMilliseconds);
            }
            return new CompletionGraceResult
            {
                TimedOut = timedOut,
                CompletedButHung = hung,
                IdleHangReason = idleHangReason,
                ElapsedMilliseconds = clock.ElapsedMilliseconds,
                ExecutionFinishedAtMilliseconds = finishedAt
            };
        }

        public static double MeasureFileStopToExitSeconds(string diagnosticFile, DateTime exitUtc)
        {
            if (string.IsNullOrEmpty(diagnosticFile) || exitUtc == DateTime.MinValue) return -1;
            try
            {
                if (!File.Exists(diagnosticFile)) return -1;
                DateTime stop = File.GetLastWriteTimeUtc(diagnosticFile);
                if (stop == DateTime.MinValue) return -1;
                double seconds = (exitUtc - stop).TotalSeconds;
                if (seconds < 0) return 0;
                return seconds;
            }
            catch (Exception) { return -1; }
        }

        public static void AppendStopToExitLine(string logPath, double seconds)
        {
            if (string.IsNullOrEmpty(logPath) || seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            string line = "execution-stop-to-exit-seconds=" + seconds.ToString("0.000", CultureInfo.InvariantCulture);
            File.AppendAllText(logPath, line + "\n", Encoding.ASCII);
        }
    }
}
