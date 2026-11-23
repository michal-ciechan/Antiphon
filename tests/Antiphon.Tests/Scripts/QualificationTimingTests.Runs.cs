using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// W2-nightly S16: the rest of the qualification timing driver, in-process with its row names. A run is the dispatch the
// nightly-tests executor runs (NightlyDispatch.CoordinateOrFailure over a NightlySuiteRunner, the run state filled as
// Invoke-AntiphonNightlyTests fills it) on a RecordingLauncher whose behaviour is the driver's StartProcess seam, and its
// timing table is QualificationTiming.Report, which the executor copies into summary.json (timingChunks, suiteTimeline).
// The release-cut legs run the release cut in process (ReleaseCut, unit 15 of the 2026-11-22 plan; scripts/release-cut.ps1 is
// its thin launcher). The timing-root resolver is compiled (NightlyPaths.TimingRoot; W2-nightly S27 deleted
// scripts/lib/test-phase-paths.ps1).
public sealed partial class QualificationTimingTests
{
    private const string RunSha = "9f00008facfc48e75555c08df59887019b3f8dfe";
    private const string PhasePrefix = "ANTIPHON_TEST_PHASE_";

    // ---- the phase environment ------------------------------------------------------------------------------------

    // Enable-TestPhaseExecutionEnvironment and Remove-TestPhaseVariables (TestPhaseEnvironment): the nightly profile
    // enables the resolved root, a build child loses it, and one explicit timing root overrides an inherited phase root
    // (the inherited root is this process's ANTIPHON_TEST_PHASE_ROOT in production, an argument here).
    [Test]
    public void Phase_environment_follows_the_profile_and_the_timing_root()
    {
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        try
        {
            var resolved = Path.Combine(root, "timing-run-1");
            var map = TestPhaseEnvironment.Enable(new Hashtable(StringComparer.OrdinalIgnoreCase), root, "run-1", "g1", "1", "antiphon", "nightly", resolved, null!);
            rows.Check(PsValue.Str(map[TestPhaseEnvironment.RootVariable]) == resolved, "nightly-profile-enables-phase-root");
            var build = new Hashtable(StringComparer.OrdinalIgnoreCase) { [TestPhaseEnvironment.RootVariable] = resolved, ["ANTIPHON_TEST_PHASE_RUN_ID"] = "run-1" };
            TestPhaseEnvironment.Remove(build);
            rows.Check(!build.ContainsKey(TestPhaseEnvironment.RootVariable), "build-child-strips-phase-root");

            var envRoot = Path.Combine(root, "env");
            var explicitRoot = Path.Combine(root, "explicit");
            var forced = TestPhaseEnvironment.Enable(new Hashtable(StringComparer.OrdinalIgnoreCase), envRoot, "run-env", "g", "1", "antiphon", "nightly", explicitRoot, envRoot);
            rows.Check(PsValue.Str(forced[TestPhaseEnvironment.RootVariable]) == explicitRoot, "explicit-timing-root-overrides-environment");
            var blank = TestPhaseEnvironment.Enable(new Hashtable(StringComparer.OrdinalIgnoreCase), envRoot, "run-env", "g", "1", "antiphon", "nightly", "", envRoot);
            rows.Check(PsValue.Str(blank[TestPhaseEnvironment.RootVariable]) == envRoot, "blank-timing-root-uses-environment");
            var absentLog = Path.Combine(root, "absent");
            var absent = TestPhaseEnvironment.Enable(new Hashtable(StringComparer.OrdinalIgnoreCase), absentLog, "run-env", "g", "1", "antiphon", "nightly", "", "   ");
            rows.Check(PsValue.Str(absent[TestPhaseEnvironment.RootVariable]) == Path.Combine(absentLog, "timing"), "absent-timing-root-uses-log");
        }
        finally { Cleanup(root); }
        rows.ShouldAllPass(5);
    }

    // ---- runs -----------------------------------------------------------------------------------------------------

    // Wrapper path: the nightly profile, one native chunk, the seam instead of Windows job custody. The chunk leaves its
    // sidecar beside the TRX and a timing row with queue, build, run wall, p50 and slowest; the native child gets the
    // phase root and the executor's build child does not.
    [Test]
    [Timeout(120_000)]
    public void Wrapper_run_leaves_a_sidecar_and_a_timing_table(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        using var run = TimingRun.Start("nightly", "timing-wrap", WrapperSeam);
        try
        {
            // The executor's dotnet build child (Invoke-AntiphonNightlyTests): the policy's safe environment without the
            // phase variables, whatever this process inherited.
            var inherited = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables()) inherited[(string)pair.Key] = pair.Value;
            foreach (var name in TestPhaseEnvironment.VariableNames) inherited[name] = "inherited-" + name;
            var buildEnvironment = NightlyPolicy.SafeChildEnvironment(run.State.Policy, "antiphon", inherited);
            TestPhaseEnvironment.Remove(buildEnvironment);
            run.Host.Launch(NightlyChunkRunner.OwnedLaunch("dotnet", ["build", "Antiphon.sln", "-c", "Debug", "--nologo"], run.State.RepoRoot, 1200000,
                buildEnvironment, Path.Combine(run.LogRoot, "dotnet-build.log")));
            run.Dispatch();

            var report = run.Report;
            rows.Check(report != null, "wrapper-summary-exists");
            rows.Check(!string.IsNullOrWhiteSpace(report?.TimingRoot), "wrapper-summary-timing-root", report?.TimingRoot ?? "");
            var chunks = report?.Chunks ?? [];
            rows.Check(chunks.Length >= 1, "wrapper-chunk-table");
            var native = chunks.Where(c => c.Suite == "antiphon" && c.Chunk != "unstarted").ToArray();
            rows.Check(native.Length >= 1, "wrapper-native-row", string.Join(",", chunks.Select(c => c.Suite + "/" + c.Chunk)));
            if (native.Length >= 1)
            {
                rows.Check(native[0].QueueWaitSeconds != null, "wrapper-queue");
                rows.Check(native[0].BuildSeconds != null, "wrapper-build");
                rows.Check(native[0].RunWallSeconds != null, "wrapper-run-wall");
                rows.Check(native[0].P50Seconds != null, "wrapper-p50");
                rows.Check(native[0].Slowest.Length >= 1, "wrapper-slowest");
                rows.Check(File.Exists(Path.Combine(run.LogRoot, native[0].Suite + "-" + native[0].Chunk + ".timing.json")), "wrapper-sidecar-beside-trx");
            }
            var timeline = report?.Timeline ?? [];
            rows.Check(timeline.Length >= 1 && !string.IsNullOrWhiteSpace(timeline[0].StartedAt), "wrapper-suite-timeline");
            var launches = run.Launcher.Launches;
            rows.Check(launches.Count >= 1, "wrapper-recorded-launches");
            var enabled = launches.Where(l => Has(l, "--report-trx") && !Has(l, "--list-tests") && !string.IsNullOrWhiteSpace(Env(l, TestPhaseEnvironment.RootVariable))).ToArray();
            rows.Check(enabled.Length >= 1, "wrapper-native-child-has-phase-root");
            var builds = launches.Where(l => l.CommandText.Contains("build Antiphon.sln", StringComparison.Ordinal)).ToArray();
            rows.Check(builds.Length >= 1 && builds.All(l => string.IsNullOrWhiteSpace(Env(l, TestPhaseEnvironment.RootVariable))), "wrapper-build-has-no-phase-root");
            rows.Check(run.Dispatched, "wrapper-returned", run.Failure?.Message ?? "");
        }
        finally { QualificationTiming.Reset(); }
        rows.ShouldAllPass(15);
    }

    // F3: discovery and launch failures still leave a terminal timing row, and the run fails with the failure's message
    // (the executor's preflight.unhandledError).
    [Test]
    [Timeout(120_000)]
    [Arguments("discovery", "rc", "discovery", "discovery-failed")]
    [Arguments("discovery-timeout", "rc", "discovery", "discovery-timeout")]
    [Arguments("launch", "nightly", "launch", "launch-failed")]
    [Arguments("missing-trx", "nightly", "complete", "missing-trx")]
    public void Failure_paths_leave_a_terminal_timing_row(string mode, string profile, string lastPhase, string why, CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        using var run = TimingRun.Start(profile, "fail-" + mode, launch => FailureSeam(mode, launch));
        try
        {
            run.Dispatch();
            var match = (run.Report?.Chunks ?? []).Where(c => c.LastPhase == lastPhase && c.Why == why).ToArray();
            var rowOk = match.Length >= 1;
            if (mode == "launch") rowOk = rowOk && (run.Failure?.Message ?? "").Contains("REVIEW_LAUNCH_FAILURE", StringComparison.Ordinal);
            if (mode == "discovery") rowOk = rowOk && (run.Failure?.Message ?? "").Contains("REVIEW_DISCOVERY_FAILURE", StringComparison.Ordinal);
            rows.Check(run.ExitCode == 1 && rowOk, "failure-row-" + mode,
                "exit=" + run.ExitCode + " failure=" + run.Failure?.Message + " rows=" + string.Join(",", (run.Report?.Chunks ?? []).Select(c => c.Chunk + ":" + c.LastPhase + ":" + c.Why)));
        }
        finally { QualificationTiming.Reset(); }
        rows.ShouldAllPass(1);
    }

    // The real nightly heartbeat (the pre-launch QualificationTiming.Update) stays non-terminal: a readable TRX already at
    // the chunk path is not parsed into the sidecar before the owned process starts.
    [Test]
    [Timeout(120_000)]
    public void Nightly_heartbeat_stays_non_terminal(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        JsonElement? seen = null;
        using var run = TimingRun.Start("nightly", "heart-beat", launch =>
        {
            var trx = Arg(launch, "--report-trx-filename");
            var results = Arg(launch, "--results-directory");
            if (trx.Length > 0 && results.Length > 0 && !Has(launch, "--list-tests"))
            {
                var side = Path.Combine(results, Path.GetFileNameWithoutExtension(trx) + ".timing.json");
                if (File.Exists(side))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(side));
                    seen = doc.RootElement.Clone();
                }
                else seen = default(JsonElement);
            }
            return SeamPass();
        }, timingRoot: "timing");
        try
        {
            File.WriteAllText(Path.Combine(run.LogRoot, "antiphon-all.trx"), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun><Results>\n"
                + "<UnitTestResult testId=\"probe\" testName=\"HEARTBEAT-PROBE\" duration=\"00:00:09\" outcome=\"Passed\" />\n</Results></TestRun>\n");
            run.Dispatch();
            var heartOk = false;
            var detail = "no launch saw the sidecar";
            if (seen is { ValueKind: JsonValueKind.Object } side)
            {
                var names = side.TryGetProperty("slowest", out var slowest) && slowest.ValueKind == JsonValueKind.Array
                    ? string.Join(",", slowest.EnumerateArray().Select(s => s.GetProperty("name").GetString())) : "";
                var p50Null = !side.TryGetProperty("p50Seconds", out var p50) || p50.ValueKind == JsonValueKind.Null;
                heartOk = Text(side, "lastPhase") == "run" && Text(side, "why") == "" && side.GetProperty("testCount").GetInt32() == 0 && p50Null
                    && !names.Contains("HEARTBEAT-PROBE", StringComparison.Ordinal);
                detail = side.GetRawText();
            }
            rows.Check(run.Dispatched && heartOk, "nightly-heartbeat-stays-non-terminal", detail);
        }
        finally { QualificationTiming.Reset(); }
        rows.ShouldAllPass(1);
    }

    // A FIFO at the chunk's sidecar path delays nothing: the launch is reached once, with no sidecar read and no open of
    // the FIFO before it. A FIFO is a POSIX property; a blocked open would hang the run, so the run has a deadline.
    [Test]
    [Timeout(120_000)]
    public void Fifo_sidecar_does_not_delay_launch(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFOs are POSIX properties");
        var rows = new NightlyCaseRows();
        var count = 0;
        var watch = new Stopwatch();
        (double Seconds, int SidecarReads, int FifoOpens)? marker = null;
        string sidecar = "";
        using var run = TimingRun.Start("nightly", "fifo-launch", launch =>
        {
            if (Has(launch, "--report-trx") && !Has(launch, "--list-tests"))
            {
                count++;
                var reads = QualificationTiming.ReadLog;
                marker = (watch.Elapsed.TotalSeconds, reads?.SidecarReads ?? -1, reads?.Opens.Count(p => p == sidecar) ?? -1);
            }
            return SeamPass();
        }, timingRoot: "timing");
        try
        {
            sidecar = Path.Combine(run.LogRoot, "antiphon-all.timing.json");
            var created = Mkfifo(sidecar) == 0;
            QualificationTiming.StartReadLog();
            watch.Start();
            var body = Task.Run(run.Dispatch);
            var finished = body.Wait(TimeSpan.FromSeconds(26));
            watch.Stop();
            if (!finished)
            {
                // Release a reader blocked on the FIFO so the worker can end.
                try { using var writer = new FileStream(sidecar, FileMode.Open, FileAccess.Write); } catch (IOException) { }
            }
            Console.WriteLine("MEASURED launch-body=" + watch.Elapsed.TotalSeconds + " count=" + count + " marker=" + marker);
            rows.Check(finished, "fifo-sidecar-launch-child-exited");
            rows.Check(finished && body.Exception == null, "fifo-sidecar-launch-child-exit-code", body.Exception?.ToString() ?? "");
            rows.Check(watch.Elapsed.TotalSeconds < 15, "fifo-sidecar-launch-body-budget", watch.Elapsed.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            rows.Check(finished && run.Dispatched && count == 1, "fifo-sidecar-launch-result-and-count", "count=" + count);
            rows.Check(marker is { SidecarReads: 0, FifoOpens: 0 }, "fifo-sidecar-no-reads-before-launch", marker?.ToString() ?? "no launch");
            rows.Check(created && finished && marker != null, "fifo-sidecar-does-not-delay-launch");
        }
        finally
        {
            QualificationTiming.ReadLog = null;
            QualificationTiming.Reset();
        }
        rows.ShouldAllPass(6);
    }

    // ---- F6: malformed telemetry cannot stall a heartbeat or a terminal read ---------------------------------------

    // A 100 MB single-line journal: the bounded reader stops at its byte cap inside the wall budget, allocates far less
    // than the file and returns no record. The driver ran this read in a pwsh child to bound its peak working set; in
    // process the bound is the bytes this thread allocated. It is plain I/O, so it runs on every platform.
    [Test]
    [Timeout(120_000)]
    public void Oversized_journal_line_stays_bounded(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        try
        {
            var huge = Path.Combine(root, "logs", "timing", "cases-80-huge.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(huge)!);
            using (var stream = File.Create(huge))
            {
                var block = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024));
                for (var n = 0; n < 100; n++) stream.Write(block);
            }
            TestPhaseReads.JsonlBounded(Path.Combine(root, "absent.jsonl"), 262144, 4000, 200, Stopwatch.StartNew());
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var read = TestPhaseReads.JsonlBounded(huge, 262144, 4000, 200, Stopwatch.StartNew());
            watch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("MEASURED oversized seconds=" + watch.Elapsed.TotalSeconds + " allocated=" + allocated + " complete=" + read.Complete + " count=" + read.Records.Length);
            rows.Check(watch.Elapsed.TotalSeconds < 0.2, "oversized-line-reader-wall-budget", watch.Elapsed.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            rows.Check(allocated < 256L * 1024 * 1024, "oversized-line-peak-memory", allocated.ToString(System.Globalization.CultureInfo.InvariantCulture));
            rows.Check(!read.Complete, "oversized-line-incomplete");
            rows.Check(read.Records.Length == 0, "oversized-line-no-records");
        }
        finally { Cleanup(root); }
        rows.ShouldAllPass(4);
    }

    // A FIFO journal and a mode-000 journal are refused by the bounded reader and the sidecar reader before any stream
    // opens, inside the budget. POSIX properties; a blocked open would hang, so each read has a deadline.
    [Test]
    [Timeout(120_000)]
    public void Unsafe_journals_are_refused_by_the_bounded_reader(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFOs and unix mode 000 are POSIX properties");
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        var denied = "";
        try
        {
            var timing = Path.Combine(root, "logs", "timing");
            var fifoDir = Path.Combine(timing, "fifo-dir");
            var fifoChunk = ChunkDir(fifoDir, "g1", "run-fifo", "1", "antiphon");
            var fifo = Path.Combine(fifoChunk, "cases-81-fifo.jsonl");
            var created = Mkfifo(fifo) == 0;
            var fifoRead = Bounded(() =>
            {
                var watch = Stopwatch.StartNew();
                var read = TestPhaseReads.JsonlBounded(fifo, 262144, 4000, 200, Stopwatch.StartNew());
                var readSeconds = watch.Elapsed.TotalSeconds;
                var sums = QualificationTiming.PhaseSums(fifoDir, "g1", "run-fifo", "antiphon", "1");
                return (read.Complete, read.Records.Length, WaitNull: sums.ProcessWaitSeconds == null, readSeconds, Seconds: watch.Elapsed.TotalSeconds);
            });
            rows.Check(created && fifoRead.Done && !fifoRead.Value.Complete && fifoRead.Value.Length == 0 && fifoRead.Value.WaitNull, "fifo-cases-file-refuses-unsafe-input",
                fifoRead.Value.ToString());

            denied = Path.Combine(timing, "cases-82-denied.jsonl");
            File.WriteAllText(denied, "{\"recordKind\":\"header\"}\n");
            Chmod(denied, 0).ShouldBe(0, "mode-000");
            var deniedRead = Bounded(() =>
            {
                var watch = Stopwatch.StartNew();
                var read = TestPhaseReads.JsonlBounded(denied, 262144, 4000, 200, Stopwatch.StartNew());
                var side = QualificationTiming.ReadSidecar(denied);
                return (read.Complete, read.Records.Length, SideNull: side == null, Seconds: watch.Elapsed.TotalSeconds);
            });
            rows.Check(deniedRead.Done && deniedRead.Value.Seconds < 0.2, "unreadable-timing-file-in-process-budget", deniedRead.Value.ToString());
            rows.Check(deniedRead.Done && !deniedRead.Value.Complete && deniedRead.Value.Length == 0 && deniedRead.Value.SideNull, "unreadable-timing-file-refuses-unsafe-input",
                deniedRead.Value.ToString());
        }
        finally
        {
            if (denied.Length > 0 && File.Exists(denied)) Chmod(denied, Convert.ToUInt32("644", 8));
            Cleanup(root);
        }
        rows.ShouldAllPass(3);
    }

    // The launch heartbeat opens no timing file: with a FIFO TRX, a FIFO sidecar, a 100 MB journal, a mode-000 journal, a
    // linked journal and a FIFO journal in place, the pre-launch Update (the call the chunk runner makes before the owned
    // process starts) attempts no open of any of them. The driver traced openat with strace in a pwsh child; in process
    // the witness is the timing writer's own read log, which records every open it attempts.
    [Test]
    [Timeout(120_000)]
    public void Launch_path_opens_no_timing_file(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFOs and unix mode 000 are POSIX properties");
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        var denied = "";
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            Directory.CreateDirectory(timing);
            var fifoTrx = Path.Combine(log, "antiphon-g1.trx");
            var side = Path.Combine(log, "antiphon-g1.timing.json");
            var huge = Path.Combine(timing, "cases-80-huge.jsonl");
            denied = Path.Combine(timing, "cases-82-denied.jsonl");
            var link = Path.Combine(timing, "cases-83-link.jsonl");
            var fifo = Path.Combine(timing, "cases-81-fifo.jsonl");
            Mkfifo(fifoTrx).ShouldBe(0, "fifo-created");
            Mkfifo(side).ShouldBe(0, "fifo-created");
            Mkfifo(fifo).ShouldBe(0, "fifo-created");
            using (var stream = File.Create(huge))
            {
                var block = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024));
                for (var n = 0; n < 100; n++) stream.Write(block);
            }
            File.WriteAllText(denied, "{\"recordKind\":\"header\"}\n");
            Chmod(denied, 0).ShouldBe(0, "mode-000");
            File.CreateSymbolicLink(link, huge);
            string[] hostile = [fifoTrx, side, huge, denied, link, fifo];
            var reads = QualificationTiming.StartReadLog();
            var done = Bounded(() =>
            {
                QualificationTiming.Update(new ChunkTimingUpdate
                {
                    LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g1", LastPhase = "run", StartedAt = Run,
                    QueueWaitSeconds = 1, BuildSeconds = 2, DiscoverySeconds = 0, TrxPath = fifoTrx, RunId = "open", Attempt = "1",
                }, DateTime.UtcNow);
                return true;
            });
            QualificationTiming.ReadLog = null;
            var touched = reads.Opens.Concat(reads.Streams).Where(p => hostile.Contains(p)).Distinct().ToArray();
            rows.Check(done.Done && touched.Length == 0, "launch-path-opens-no-timing-file", "opens=" + string.Join(",", reads.Opens));
        }
        finally
        {
            if (denied.Length > 0 && File.Exists(denied)) Chmod(denied, Convert.ToUInt32("644", 8));
            Cleanup(root);
        }
        rows.ShouldAllPass(1);
    }

    // Linux supervisor-death custody is a known gap: an unseamed Linux launch goes to the Windows Job Object owner and
    // refuses.
    [Test]
    public void Linux_owned_launch_is_a_known_custody_gap()
    {
        if (OperatingSystem.IsWindows()) Skip.Test("the native Job Object owner is the Windows custody");
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        try
        {
            var refusal = "";
            try
            {
                new OwnedLauncher().Run(NightlyChunkRunner.OwnedLaunch("/bin/sh", ["-c", "echo refused"], root, 2000, null!, Path.Combine(root, "refused.log")));
            }
            catch (Exception ex) { refusal = ex.Message; }
            rows.Check(refusal == "Nightly native custody requires Windows.", "linux-supervisor-death-custody-is-a-known-gap", "linux-refusal=[" + refusal + "]");
        }
        finally { Cleanup(root); }
        rows.ShouldAllPass(1);
    }

    // ---- the capture shell and the capture identity ---------------------------------------------------------------

    // The capture shell resolver (the driver's own helper, here as CaptureShell.Resolve): Git's sh.exe beside git or on
    // PATH, never a literal /bin/sh on a Windows-shaped PATH, nothing when there is none. On Linux the capture launcher
    // then starts the resolved (planted) shell, and with the phase environment stripped keeps every other variable.
    [Test]
    [Category("Integration")]
    [ParallelLimiter<ProcessSpawnLimit>]
    [Timeout(60_000)]
    public void Capture_shell_resolver_takes_git_sh_never_bin_sh(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        try
        {
            var gitCmd = Path.Combine(root, "Program Files", "Git", "cmd");
            var gitUsr = Path.Combine(root, "Program Files", "Git", "usr", "bin");
            var direct = Path.Combine(root, "direct");
            var empty = Path.Combine(root, "empty");
            var probeRoot = Path.Combine(root, "probe");
            var probeDir = Path.Combine(probeRoot, "cap");
            foreach (var dir in new[] { gitCmd, gitUsr, direct, empty, probeDir }) Directory.CreateDirectory(dir);
            var plant = Path.Combine(gitUsr, "sh.exe");
            File.WriteAllText(plant, "#!/bin/sh\nprintf '%s\\n' \"$0\" > \"$QTIMING_CAPTURE_SHELL_MARKER\"\nexec /bin/sh \"$@\"\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(plant, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.WriteAllText(Path.Combine(gitCmd, "git.exe"), "git");
            File.WriteAllText(Path.Combine(direct, "sh.exe"), "direct");
            var marker = Path.Combine(root, "marker");

            var beside = CaptureShell.Resolve("windows", gitCmd) ?? "";
            var besideOk = beside.Length > 0 && beside != "/bin/sh" && Slashed(beside) == Slashed(Path.GetFullPath(plant));
            rows.Check(besideOk, "capture-shell-resolver-windows-shape", beside);
            rows.Check(Slashed(CaptureShell.Resolve("windows", direct) ?? "") == Slashed(Path.GetFullPath(Path.Combine(direct, "sh.exe"))), "capture-shell-resolver-path-sh-exe");
            var binRejected = string.IsNullOrWhiteSpace(CaptureShell.Resolve("windows", "/bin"));
            if (!OperatingSystem.IsWindows()) binRejected = binRejected && File.Exists("/bin/sh");
            rows.Check(binRejected, "capture-shell-resolver-skips-bin-sh");
            rows.Check(string.IsNullOrWhiteSpace(CaptureShell.Resolve("windows", empty)), "capture-shell-absent-skips");
            if (OperatingSystem.IsWindows() || !besideOk)
            {
                // The planted shell is a POSIX script (a shebang); Windows cannot start it.
                rows.ShouldAllPass(4);
                return;
            }

            var capture = new CaptureShell(beside, probeDir) { ChildExit = 0, Forward = true };
            rows.Check(capture.Shell == beside, "capture-shell-seam-embeds-resolved-shell");
            capture.Run(NightlyChunkRunner.OwnedLaunch("dotnet", ["--report-trx"], probeRoot, 10000,
                new Hashtable { [TestPhaseEnvironment.RootVariable] = probeRoot, ["QTIMING_CAPTURE_SHELL_MARKER"] = marker }, Path.Combine(probeRoot, "a.log")));
            var markerText = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
            var started = markerText == beside && ReadOr(Path.Combine(probeDir, "stdout-0")) == "QUAL-OUT" && ReadOr(Path.Combine(probeDir, "stderr-0")) == "QUAL-ERR"
                && ReadOr(Path.Combine(probeDir, "exit-0")).Trim() == "0" && Directory.GetFiles(probeDir, "seen-*").Length >= 1;
            rows.Check(started, "capture-shell-seam-starts-resolved-shell", "marker=" + markerText);

            var inherited = TestPhaseEnvironment.VariableNames.ToDictionary(name => name, name => "inherited-" + name);
            var off = new CaptureShell(beside, probeDir) { ChildExit = 0, Forward = false, EnvWitness = true, Inherited = inherited };
            off.Run(NightlyChunkRunner.OwnedLaunch("dotnet", ["--report-trx"], probeRoot, 10000,
                new Hashtable { [TestPhaseEnvironment.RootVariable] = "forwarded-phase", ["QTIMING_KEEP"] = "kept-value", ["QTIMING_CAPTURE_SHELL_MARKER"] = marker },
                Path.Combine(probeRoot, "b.log")));
            var dump = Directory.GetFiles(probeDir, "env-*").Order(StringComparer.Ordinal).LastOrDefault();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in (dump == null ? "" : File.ReadAllText(dump)).Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                var eq = trimmed.IndexOf('=');
                if (eq > 0) values[trimmed[..eq]] = trimmed[(eq + 1)..];
            }
            var phaseHits = values.Keys.Count(k => k.StartsWith(PhasePrefix, StringComparison.Ordinal));
            var pathValue = values.TryGetValue("PATH", out var p) ? p : values.TryGetValue("Path", out var q) ? q : "";
            var kept = pathValue.Length > 0 && values.GetValueOrDefault("QTIMING_KEEP") == "kept-value" && phaseHits == 0;
            rows.Check(kept, "capture-seam-off-keeps-non-phase", "count=" + values.Count + " path=" + (pathValue.Length > 0 ? 1 : 0) + " phase=" + phaseHits);
        }
        finally { Cleanup(root); }
        rows.ShouldAllPass(7);
    }

    // Stdout, stderr and the chunk exit stay the same with the phase environment forwarded to the child or stripped:
    // two runs per child exit (0 and 1) through the capture launcher, which starts the resolved shell for the chunk.
    [Test]
    [Category("Integration")]
    [ParallelLimiter<ProcessSpawnLimit>]
    [Timeout(180_000)]
    public void Capture_identity_is_the_same_with_the_phase_environment_forwarded_or_stripped(CancellationToken cancellationToken)
    {
        var shell = CaptureShell.Resolve();
        if (string.IsNullOrWhiteSpace(shell))
            Skip.Test("capture-identity shell-absent: no sh.exe on PATH and no Git for Windows sh.exe beside git");
        Console.WriteLine("DIAG capture-identity shell=" + shell);
        var rows = new NightlyCaseRows();
        var root = NewRoot();
        try
        {
            foreach (var childExit in new[] { 0, 1 })
            {
                var runs = new List<CaptureRun>();
                foreach (var forward in new[] { true, false })
                {
                    var capDir = Path.Combine(root, "e" + childExit + "-f" + (forward ? 1 : 0), "cap");
                    Directory.CreateDirectory(capDir);
                    var capture = new CaptureShell(shell!, capDir) { ChildExit = childExit, Forward = forward };
                    using var run = TimingRun.Start("nightly", "cap-" + childExit + "-" + (forward ? 1 : 0), capture.Run);
                    run.Dispatch();
                    string[] Files(string pattern) => Directory.GetFiles(capDir, pattern).Order(StringComparer.Ordinal).ToArray();
                    var suites = run.State.SuiteResults.Where(r => Field(r, "id") == "antiphon" && !PsValue.IsTrue(FieldObject(r, "skipped")))
                        .Select(r => Field(r, "exitCode")).ToArray();
                    runs.Add(new CaptureRun(
                        run.ExitCode,
                        string.Join(",", suites),
                        string.Join("|", Files("stdout-*").Select(File.ReadAllText)),
                        string.Join("|", Files("stderr-*").Select(File.ReadAllText)),
                        string.Join(",", Files("exit-*").Select(f => File.ReadAllText(f).Trim())),
                        Files("seen-*").Length,
                        string.Join("|", Payloads(run.LogRoot))));
                    QualificationTiming.Reset();
                }
                var (on, offRun) = (runs[0], runs[1]);
                var outParts = on.StdOut.Split('|');
                var errParts = on.StdErr.Split('|');
                var exitParts = on.Exits.Split(',');
                var suiteParts = on.Suites.Split(',');
                var bytesOk = outParts.Length >= 1 && outParts.All(x => x == "QUAL-OUT") && errParts.All(x => x == "QUAL-ERR")
                    && exitParts.All(x => x == childExit.ToString(System.Globalization.CultureInfo.InvariantCulture)) && suiteParts.Length == exitParts.Length;
                var same = on.Wrapper == offRun.Wrapper && on.Wrapper == 1 && on.Suites == offRun.Suites && on.StdOut == offRun.StdOut && on.StdErr == offRun.StdErr
                    && on.Exits == offRun.Exits && on.Payload == offRun.Payload && on.Seen >= 1 && offRun.Seen == 0 && bytesOk;
                rows.Check(same, "capture-identity-exit-" + childExit, "on=" + on + " off=" + offRun + " bytesOk=" + bytesOk);
            }
        }
        finally
        {
            QualificationTiming.Reset();
            Cleanup(root);
        }
        rows.ShouldAllPass(2);
    }

    private sealed record CaptureRun(int Wrapper, string Suites, string StdOut, string StdErr, string Exits, int Seen, string Payload);

    // Get-QualificationCapturePayload: every *-tests.log under the run, without its C487_INVOCATION first line.
    private static string[] Payloads(string logRoot) =>
        Directory.GetFiles(logRoot, "*-tests.log", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path =>
        {
            var lines = File.ReadAllText(path).Split('\n');
            return lines.Length > 0 && lines[0].StartsWith("C487_INVOCATION=", StringComparison.Ordinal) ? string.Join("\n", lines.Skip(1)) : string.Join("\n", lines);
        }).ToArray();

    // ---- the timing-root resolver ----------------------------------------------------------------------------------

    // Resolve-QualificationTimingRoot is compiled (NightlyPaths.TimingRoot); W2-nightly S27 deleted
    // scripts/lib/test-phase-paths.ps1, and the release cut (ReleaseCut, unit 15 of the 2026-11-22 plan) calls the compiled
    // rule for a blank timing root. A blank root resolves to LogRoot/timing-<run> (LogRoot/timing without a run id, blank
    // without a log root); an explicit one is kept. The PowerShell 5.1 load of the old library (and its Add-Type of the
    // Linux identity) has no subject left.
    [Test]
    public void Timing_root_resolver_is_the_compiled_rule()
    {
        var rows = new NightlyCaseRows();
        var log = Path.Combine(Path.GetTempPath(), "qtiming-resolver", "log");
        var resolved = NightlyPaths.TimingRoot("", log, "run-1");
        rows.Check(!string.IsNullOrWhiteSpace(resolved), "blank-root-resolved", resolved);
        rows.Check(resolved == Path.Combine(log, "timing-run-1"), "blank-root-retained", resolved);
        var kept = NightlyPaths.TimingRoot(Path.Combine(log, "explicit"), log, "run-1");
        rows.Check(kept == Path.Combine(log, "explicit"), "explicit-root-kept", kept);
        var unnamed = NightlyPaths.TimingRoot("", log, "");
        var none = NightlyPaths.TimingRoot("", "", "run-1");
        rows.Check(unnamed == Path.Combine(log, "timing") && none == "", "no-run-id-and-no-log-root", unnamed + " | " + none);
        var cut = File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "scripts", "lib", "Antiphon.Nightly", "ReleaseCut.cs"));
        rows.Check(cut.Contains("NightlyPaths.TimingRoot(\"\", logRoot, runId)", StringComparison.Ordinal)
            && !File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "scripts", "release-cut.ps1")).Contains("TimingRoot(", StringComparison.Ordinal),
            "release-cut-calls-the-compiled-resolver");
        rows.ShouldAllPass(5);
    }

    // ---- F2: release-cut seals the timing root --------------------------------------------------------------------

    // Replaying a sealed blank timingRoot through the release cut keeps the seal; an explicit root is sealed; an absent one
    // defaults for a new attempt; another explicit root against a sealed attempt conflicts. Subject: ReleaseCut.Run (the
    // body of scripts/release-cut.ps1 until unit 15 of the 2026-11-22 plan) and the selection attempt it seals
    // (ReleaseSelectionAttempt.Create), in process on a real candidate checkout with its bare origin (ReleaseCutWorld.Real);
    // the rc run is a recorded green stand-in and nothing publishes (SkipPublish).
    [Test]
    [Category("Integration")]
    [ParallelLimiter<ProcessSpawnLimit>]
    [Timeout(600_000)]
    public void Release_cut_seals_the_timing_root(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        using var world = ReleaseCutWorld.Real("qtiming-cut");
        var candidate = ReleaseGateCredit.Candidate(ReleaseCutWorld.Ref, world.Sha);
        var seed = ReleaseSelectionAttempt.Create(new ReleaseSelectionRequest
        {
            RepositoryRoot = world.Checkout, Repository = "michal-ciechan/Antiphon", ReleaseRoot = world.Release, CandidateSha = world.Sha,
            CandidateId = candidate.CandidateId, CandidateRef = candidate.Ref, Profile = "rc", RunId = "blank-replay", SelectionMode = "Full", Attempt = "1",
            BaselineSha = "", BaselineReceiptPath = "", QualificationPath = "", TimingRoot = "", TrainManifestPath = "", ReleaseIntentPath = "",
        });
        var blank = world.Cut(o =>
        {
            o.RunId = "blank-replay";
            o.TimingRoot = "";
            o.SkipPublish = true;
        });
        var sealedText = File.ReadAllText(seed.SelectionAttemptPath);
        rows.Check(NightlyPaths.FileSha256(seed.SelectionAttemptPath) == seed.SelectionAttemptDigest && sealedText.Contains("\"timingRoot\":\"\"", StringComparison.Ordinal)
            && blank.ExitCode == 0 && world.Runs[^1].TimingRoot == "", "blank-sealed-replay-preserves-attempt", "refusal=" + blank.Refusal + " timing=" + world.Runs.LastOrDefault()?.TimingRoot);

        var attempts = Path.Combine(world.CandidateRoot, "attempts");
        var explicitTiming = Path.Combine(world.Release, "explicit-timing");
        var explicitCut = world.Cut(o =>
        {
            o.RunId = "explicit-run";
            o.TimingRoot = explicitTiming;
            o.SkipPublish = true;
        });
        var explicitSealed = Sealed(ReleaseGateFixture.ReadObject(Path.Combine(attempts, "explicit-run", "selection-attempt.json")), "timingRoot");
        rows.Check(explicitCut.ExitCode == 0 && explicitSealed == explicitTiming && world.Runs[^1].TimingRoot == explicitTiming, "explicit-timing-root-is-sealed",
            "refusal=" + explicitCut.Refusal + " sealed=" + explicitSealed);

        var absentCut = world.Cut(o =>
        {
            o.RunId = "absent-run";
            o.TimingRoot = "";
            o.SkipPublish = true;
        });
        var absentExpected = NightlyPaths.TimingRoot("", Path.Combine(world.CandidateRoot, "logs"), "absent-run");
        var absentSealed = Sealed(ReleaseGateFixture.ReadObject(Path.Combine(attempts, "absent-run", "selection-attempt.json")), "timingRoot");
        rows.Check(absentCut.ExitCode == 0 && absentSealed == absentExpected && world.Runs[^1].TimingRoot == absentExpected, "absent-timing-root-defaults-for-new-attempt",
            "refusal=" + absentCut.Refusal + " sealed=" + absentSealed);

        var runs = world.Runs.Count;
        var conflict = world.Cut(o =>
        {
            o.RunId = "explicit-run";
            o.TimingRoot = Path.Combine(world.Release, "another-timing");
            o.SkipPublish = true;
        });
        rows.Check(conflict.ExitCode == 3 && conflict.Refusal == "selection-attempt-conflict" && world.Runs.Count == runs
            && Sealed(ReleaseGateFixture.ReadObject(Path.Combine(attempts, "explicit-run", "selection-attempt.json")), "timingRoot") == explicitTiming,
            "another-explicit-root-conflicts-with-the-seal", "refusal=" + conflict.Refusal);
        rows.ShouldAllPass(4);

        static string Sealed(System.Text.Json.Nodes.JsonObject document, string name) => ReleaseGateFixture.Str(document[name]);
    }

    // ---- run and seam helpers --------------------------------------------------------------------------------------

    // One run of the antiphon suite through the dispatch, its state filled as Invoke-AntiphonNightlyTests fills it: the
    // repository, the shipped policy and cost table, the profile's required suites, the platform this host observes, a
    // fresh log root and the resolved timing root (Resolve-QualificationTimingRoot: blank is LogRoot/timing-<run>; a
    // relative one is under the log root), a measured build, and the discovery producer's steps on the host.
    private sealed class TimingRun : IDisposable
    {
        private TimingRun(string profile, string runId, Func<NightlyLaunch, NightlyLaunchResult> behaviour, string timingRoot)
        {
            Fixture = NightlyFixture.Create("qtiming-run");
            Launcher = new RecordingLauncher(behaviour);
            Host = Fixture.Host(launcher: Launcher, clock: TimeProvider.System);
            LogRoot = Fixture.PathOf("run");
            Directory.CreateDirectory(LogRoot);
            var timing = string.IsNullOrWhiteSpace(timingRoot) ? Path.Combine(LogRoot, "timing-" + runId)
                : Path.IsPathRooted(timingRoot) ? timingRoot : Path.Combine(LogRoot, timingRoot);
            var policy = NightlyPlanCases.Policy();
            var host = Host;
            State = new NightlyRunState
            {
                Host = host,
                RepoRoot = NightlyPlanCases.Repo,
                LogRoot = LogRoot,
                TimingRoot = timing,
                QualificationTimingRoot = timing,
                InvocationId = runId,
                Attempt = "1",
                Profile = profile,
                Sha = RunSha,
                GitRef = "origin/master",
                Policy = policy.Object,
                PolicyHash = policy.Hash,
                Platform = NightlyTestsPlan.ExecutionPlatform(new NightlyHostProbe().Platform()),
                StartedAt = DateTime.UtcNow,
                BuildSeconds = 0.25,
                ProfileAware = profile != "nightly",
                RequiredSuites = NightlyPolicy.ProfileRequiredSuites(policy.Object, profile).Cast<object>().ToArray(),
                ChunksBySuite = NightlyPlanCases.Table(),
                ClassCostPath = NightlyPlanCases.CostPath,
                ExplicitPhaseRoot = "",
                ProduceDiscovery = request => FailedFirstDispatchCases.ProduceDiscovery(host, request),
                PublicationScope = () => JournalFixtures.Scope(timing),
                Warning = _ => { },
            };
        }

        public static TimingRun Start(string profile, string runId, Func<NightlyLaunch, NightlyLaunchResult> behaviour, string timingRoot = "") =>
            new(profile, runId, behaviour, timingRoot);

        public NightlyFixture Fixture { get; }
        public RecordingLauncher Launcher { get; }
        public NightlyHost Host { get; }
        public NightlyRunState State { get; }
        public string LogRoot { get; }
        public Exception? Failure { get; private set; }
        public TimingReport? Report { get; private set; }
        public bool Dispatched { get; private set; }

        /// <summary>The executor's exit: 1 when the dispatch failed (its catch) or the run failed.</summary>
        public int ExitCode => Failure != null || State.OverallFailed ? 1 : 0;

        /// <summary>The dispatch of the antiphon suite, then the timing report the summary copies.</summary>
        public void Dispatch()
        {
            Failure = NightlyDispatch.CoordinateOrFailure(State, new NightlySuiteRunner(State).Steps(), ["antiphon"], null!, null!, "", null!, null!, "");
            Dispatched = true;
            Report = QualificationTiming.Report(LogRoot, State.TimingRoot);
        }

        public void Dispose() => Fixture.Dispose();
    }

    // The StartProcess seam's answer: exit 0, children exited, cleanup complete, pid 1, no log.
    private static NightlyLaunchResult SeamPass() =>
        new() { ExitCode = 0, TimedOut = false, ChildrenExited = true, CleanupComplete = true, Pid = 1, LogText = "" };

    // The wrapper seam: a native run writes a three-row TRX (3 s, 2 s, 1 s) where its argv names it.
    private static NightlyLaunchResult WrapperSeam(NightlyLaunch launch)
    {
        var trx = Arg(launch, "--report-trx-filename");
        var results = Arg(launch, "--results-directory");
        if (trx.Length > 0 && results.Length > 0 && !Has(launch, "--list-tests"))
            File.WriteAllText(Path.Combine(results, trx), TrxText(("Slow", "00:00:03"), ("Mid", "00:00:02"), ("Fast", "00:00:01")));
        return SeamPass();
    }

    // The failure seam: discovery throws or times out, the launch throws, or the launch writes no TRX.
    private static NightlyLaunchResult FailureSeam(string mode, NightlyLaunch launch)
    {
        var list = Has(launch, "--list-tests");
        var report = Has(launch, "--report-trx");
        if (list && mode == "discovery-timeout")
            return new NightlyLaunchResult { ExitCode = 1, TimedOut = true, ChildrenExited = true, CleanupComplete = true, Pid = 1, LogText = "" };
        if (list && mode == "discovery") throw new InvalidOperationException("REVIEW_DISCOVERY_FAILURE");
        if (report && mode == "launch") throw new InvalidOperationException("REVIEW_LAUNCH_FAILURE");
        return SeamPass();
    }

    private static bool Has(NightlyLaunch launch, string flag) => (launch.Arguments ?? []).Contains(flag);

    private static string Arg(NightlyLaunch launch, string flag)
    {
        var args = launch.Arguments ?? [];
        for (var i = 0; i + 1 < args.Count; i++)
            if (args[i] == flag) return args[i + 1] ?? "";
        return "";
    }

    private static string Env(NightlyLaunch launch, string name) =>
        launch.Environment != null && launch.Environment.TryGetValue(name, out var value) ? value ?? "" : "";

    private static object? FieldObject(object row, string name) => row is IDictionary map ? (map.Contains(name) ? map[name] : null) : PsValue.Prop(row, name);

    private static string Field(object row, string name) => PsValue.Str(FieldObject(row, name));

    private static string Text(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static bool Bool(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Slashed(string path) => (path ?? "").Replace('\\', '/');

    private static string ReadOr(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    // A read that must not block: run on a worker with a deadline (a blocked FIFO open never returns).
    private static (bool Done, T Value) Bounded<T>(Func<T> read)
    {
        var task = Task.Run(read);
        return task.Wait(TimeSpan.FromSeconds(15)) ? (true, task.Result) : (false, default!);
    }

    /// <summary>
    /// The capture launcher (the driver's New-QualificationCaptureSeam): a native run (--report-trx, not --list-tests)
    /// starts Shell -c with a command that prints QUAL-OUT and QUAL-ERR, marks seen-&lt;n&gt; when it sees
    /// ANTIPHON_TEST_PHASE_ROOT, optionally dumps its environment to env-&lt;n&gt;, and exits ChildExit; its stdout, stderr
    /// and exit land in stdout-&lt;n&gt;, stderr-&lt;n&gt; and exit-&lt;n&gt;. The child gets this process's environment, then Inherited,
    /// then the launch's; without Forward every ANTIPHON_TEST_PHASE_ variable is stripped. Every other launch passes.
    /// </summary>
    internal sealed class CaptureShell(string shell, string directory)
    {
        public string Shell { get; } = shell;
        public int ChildExit { get; init; }
        public bool Forward { get; init; } = true;
        public bool EnvWitness { get; init; }
        public IReadOnlyDictionary<string, string>? Inherited { get; init; }

        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (!Has(launch, "--report-trx") || Has(launch, "--list-tests")) return SeamPass();
            var seqPath = Path.Combine(directory, "seq");
            var seq = File.Exists(seqPath) ? int.Parse(File.ReadAllText(seqPath), System.Globalization.CultureInfo.InvariantCulture) : 0;
            File.WriteAllText(seqPath, (seq + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var start = new ProcessStartInfo(Shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-c");
            // The dump path is embedded in the command: a variable would be removed by the strip under test, and
            // /usr/bin/env does not need PATH.
            var command = "printf QUAL-OUT; printf QUAL-ERR 1>&2; if [ -n \"$ANTIPHON_TEST_PHASE_ROOT\" ]; then printf yes > \"$QTIMING_SEEN\"; fi;";
            if (EnvWitness) command += " /usr/bin/env > '" + Msys(Path.Combine(directory, "env-" + seq)) + "';";
            command += " exit " + ChildExit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.ArgumentList.Add(command);
            if (Inherited != null)
                foreach (var pair in Inherited) start.Environment[pair.Key] = pair.Value;
            if (launch.Environment != null)
                foreach (var pair in launch.Environment)
                {
                    if (pair.Value == null || (!Forward && pair.Key.StartsWith(PhasePrefix, StringComparison.Ordinal))) continue;
                    start.Environment[pair.Key] = pair.Value;
                }
            if (!Forward)
                foreach (var name in start.Environment.Keys.Where(k => k.StartsWith(PhasePrefix, StringComparison.Ordinal)).ToList())
                    start.Environment.Remove(name);
            start.Environment["QTIMING_SEEN"] = Msys(Path.Combine(directory, "seen-" + seq));
            using var process = Process.Start(start)!;
            var stdoutRead = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            var stdout = stdoutRead.GetAwaiter().GetResult();
            process.WaitForExit();
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(directory, "stdout-" + seq), stdout, utf8);
            File.WriteAllText(Path.Combine(directory, "stderr-" + seq), stderr, utf8);
            File.WriteAllText(Path.Combine(directory, "exit-" + seq), process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new NightlyLaunchResult { ExitCode = process.ExitCode, TimedOut = false, ChildrenExited = true, CleanupComplete = true, Pid = process.Id, LogText = stdout + stderr };
        }

        // A Windows path as the MSYS shell names it (C:\x\y is /c/x/y).
        private static string Msys(string path)
        {
            if (!Regex.IsMatch(path, @"^[A-Za-z]:\\")) return path;
            return "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/');
        }

        /// <summary>The shell this host would resolve (its platform, its PATH).</summary>
        public static string? Resolve() => Resolve(OperatingSystem.IsWindows() ? "windows" : "linux", Environment.GetEnvironmentVariable("PATH") ?? "");

        /// <summary>
        /// Resolve-QualificationCaptureShell: on Linux the literal /bin/sh; otherwise sh.exe on the PATH, then sh.exe under
        /// usr/bin or bin of the installation two levels above a git or sh found on the PATH (through its link), then (not
        /// on Windows) a plain sh on the PATH. A Windows-shaped resolution never returns /bin/sh. Null when there is none.
        /// </summary>
        public static string? Resolve(string platform, string pathValue)
        {
            platform = (platform ?? "").ToLowerInvariant();
            if (platform == "linux" && File.Exists("/bin/sh")) return "/bin/sh";
            var dirs = (pathValue ?? "").Split(Path.PathSeparator).Where(d => !string.IsNullOrWhiteSpace(d)).ToArray();
            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, "sh.exe");
                if (!File.Exists(candidate)) continue;
                var full = Path.GetFullPath(candidate);
                if (platform == "windows" && full == "/bin/sh") continue;
                return full;
            }
            foreach (var dir in dirs)
                foreach (var name in new[] { "git.exe", "git", "sh.exe", "sh" })
                {
                    var candidate = Path.Combine(dir, name);
                    if (!File.Exists(candidate)) continue;
                    var full = Path.GetFullPath(candidate);
                    if (platform == "windows" && full == "/bin/sh") continue;
                    var installation = Path.GetDirectoryName(Path.GetDirectoryName(Linked(full)) ?? "");
                    if (string.IsNullOrWhiteSpace(installation)) continue;
                    foreach (var relative in new[] { Path.Combine("usr", "bin", "sh.exe"), Path.Combine("bin", "sh.exe") })
                    {
                        var shell = Path.Combine(installation, relative);
                        if (!File.Exists(shell)) continue;
                        var shellFull = Path.GetFullPath(shell);
                        if (platform == "windows" && shellFull == "/bin/sh") continue;
                        return shellFull;
                    }
                }
            if (platform != "windows")
                foreach (var dir in dirs)
                {
                    var candidate = Path.Combine(dir, "sh");
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            return null;
        }

        // Resolve-QualificationCaptureLink: a link's existing target, else the path.
        private static string Linked(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget != null && info.ResolveLinkTarget(true) is { Exists: true } target) return target.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return path;
        }
    }
}
