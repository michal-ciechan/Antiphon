// W2-nightly S12: the chunk runner (plan 2026-11-21-w2-nightly-port-slices.md, slice S12), moved from
// nightly-tests-impl.ps1: Invoke-NightlyScheduledChunk with the owned launch (Invoke-NightlyOwnedProcess), the cleanup
// and active-count reads (Wait-NightlyOwnedCleanup, Get-NightlyNativeActiveCount), the observation writer
// (Add-NightlyOwnerObservation, Publish-NightlyChunkObservation), the measured-phase writers (Write-NightlyMeasuredPhase,
// Write-NightlyPrerequisitePhases), the chunk directory, and the suite's first-discovery record
// (RcRunEvidenceRecorder.DiscoveryOnce, once Register-NightlyRcDiscoveryOnce).
//
// One chunk of a prepared suite runs on the run's NightlyHost: the launch, the cleanup wait and the active count go through
// its launcher, the clock, the elapsed times and the trace are its own. The run state a chunk reads and changes (the verdict
// flags, the reasons, the native counters, the outcome, the skip rows) travels in the context and comes back in the
// outcome; the suite state changes in place. A failure is the outcome's Error, after the state it reached, as the
// dot-sourced PowerShell left its caller's variables when it threw. Collaborators the caller binds are delegates a plain
// caller may leave null: the executed manifest (TestPhaseManifests.WriteExecuted, S09), the active-chunk identity
// (QualificationHeartbeat.ActiveChunkWriter) and the discovery producer (Invoke-NightlyProduceDiscovery).
// Plain C#: no PowerShell types; the records the core returns are read through PsValue. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One chunk's executed observation for the phase manifest (Write-TestPhaseExecutedManifest's arguments).</summary>
    public sealed class NightlyExecutedObservation
    {
        public NightlyExecutedObservation(object verdict, string logRoot, string trxPath, string suite, string chunk, string runId, string attempt, double hostWallSeconds)
        {
            Verdict = verdict;
            LogRoot = logRoot;
            TrxPath = trxPath;
            Suite = suite;
            Chunk = chunk;
            RunId = runId;
            Attempt = attempt;
            HostWallSeconds = hostWallSeconds;
        }

        /// <summary>{ ExitCode, coverageComplete, testsPassed } and the owner observation (NightlyChunkRunner.ObservationVerdict).</summary>
        public object Verdict { get; }
        public string LogRoot { get; }
        public string TrxPath { get; }
        public string Suite { get; }
        public string Chunk { get; }
        public string RunId { get; }
        public string Attempt { get; }
        public double HostWallSeconds { get; }
    }

    /// <summary>The active-chunk identity a native chunk records before its launch (Active) and after it (Idle).</summary>
    public sealed class NightlyActiveChunk
    {
        public NightlyActiveChunk(string directory, string suite, string chunk, string runId, string attempt, string state)
        {
            Directory = directory;
            Suite = suite;
            Chunk = chunk;
            RunId = runId;
            Attempt = attempt;
            State = state;
        }

        /// <summary>The timing root.</summary>
        public string Directory { get; }
        public string Suite { get; }
        public string Chunk { get; }
        public string RunId { get; }
        public string Attempt { get; }

        /// <summary>Active or Idle.</summary>
        public string State { get; }
    }

    /// <summary>A supplemental discovery a native chunk asks for (Invoke-NightlyProduceDiscovery's arguments).</summary>
    public sealed class NightlyDiscoveryRequest
    {
        public NightlyDiscoveryRequest(string exePath, string[] argumentPrefix, string assemblyPath, string outputPath, string diagnosticDirectory,
            string logPath, string workingDirectory, Hashtable environment)
        {
            ExePath = exePath;
            ArgumentPrefix = argumentPrefix ?? new string[0];
            AssemblyPath = assemblyPath;
            OutputPath = outputPath;
            DiagnosticDirectory = diagnosticDirectory;
            LogPath = logPath;
            WorkingDirectory = workingDirectory;
            Environment = environment;
        }

        public string ExePath { get; }
        public string[] ArgumentPrefix { get; }
        public string AssemblyPath { get; }
        public string OutputPath { get; }
        public string DiagnosticDirectory { get; }
        public string LogPath { get; }
        public string WorkingDirectory { get; }
        public Hashtable Environment { get; }
    }

    /// <summary>
    /// What a chunk runs with: the run's host and identity, the policy, the run state it reads (copied into the outcome
    /// before the chunk changes it), and the collaborators still outside C#. FromScope reads it from the executor's
    /// variables (ScopeNames); a C# caller sets the properties.
    /// </summary>
    public sealed class NightlyChunkContext
    {
        /// <summary>The executor's variables the context is read from, by name (nightly-tests-impl.ps1 Invoke-AntiphonNightlyTests).</summary>
        public static string[] ScopeNames { get; } =
        {
            "RepoRoot", "LogRoot", "TimingRoot", "invocationId", "Attempt", "Profile", "Sha", "GitRef", "policy", "policyHash", "platform",
            "RcEvidenceRoot", "startedAt", "qualificationBuildSeconds", "profileAware", "expectedSkipRoster", "rcSkipRoster",
            "activeNative", "nativeActiveMax", "coverageComplete", "testsPassed", "overallFailed", "reasons", "toleratedSkipRows",
            "expectedSkipWarnings", "harnessHangReason",
        };

        /// <summary>The run's host (required).</summary>
        public NightlyHost Host { get; set; }

        public string RepoRoot { get; set; } = "";
        public string LogRoot { get; set; } = "";

        /// <summary>
        /// The antiphon-scripts assembly a client chunk runs `test-client` from (dotnet exec); blank: this process's entry
        /// assembly, the CLI that runs nightly-tests (unit 32 of 2026-11-22-remaining-ps1-to-csharp.md).
        /// </summary>
        public string CliPath { get; set; } = "";

        /// <summary>The qualification timing root ($TimingRoot).</summary>
        public string TimingRoot { get; set; } = "";

        /// <summary>Get-TestPhaseTimingDirectory: the phase timing directory (blank: LogRoot/timing).</summary>
        public string PhaseTimingDirectory { get; set; } = "";

        public string InvocationId { get; set; } = "";
        public string Attempt { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Sha { get; set; } = "";
        public string GitRef { get; set; } = "";
        public ExecutionPolicy Policy { get; set; }
        public string PolicyHash { get; set; } = "";
        public string Platform { get; set; } = "";
        public string RcEvidenceRoot { get; set; } = "";

        /// <summary>The run's start: evidence older than a second before it is stale.</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>$qualificationBuildSeconds: the prerequisite build seconds, null when unmeasured.</summary>
        public double? BuildSeconds { get; set; }

        public bool ProfileAware { get; set; }

        /// <summary>The expected-platform-skip roster (NightlyChunkVerdict.SkipRoster's entries).</summary>
        public object ExpectedSkipRoster { get; set; }

        /// <summary>The same roster as the shadow chunk record reads it; null when there is none.</summary>
        public RcSkipEntry[] RcSkipRoster { get; set; }

        /// <summary>ANTIPHON_TEST_PHASE_ROOT of this process (Enable-TestPhaseExecutionEnvironment's explicit root).</summary>
        public string ExplicitPhaseRoot { get; set; } = "";

        /// <summary>
        /// The completion grace a native launch gets once total ran and request execute stop are on disk: a leftover tree is
        /// then killed after this, not after the full watchdog. Unmeasured placeholder (CARD-1410 follow-up): chunk logs record
        /// execution-stop-to-exit-seconds; set it near 2x that p95 (docs/test-performance-standards.md).
        /// </summary>
        public int CompletionGraceMilliseconds { get; set; } = NightlyCompletionGrace.DefaultMilliseconds;

        /// <summary>The run's shadow evidence recorder (Get-NightlyRcRunEvidence); null records nothing.</summary>
        public RcRunEvidenceRecorder RcEvidence { get; set; }

        /// <summary>
        /// CARD-1836 S1: 2 stamps each observed row with its execution sha, content digest and disposition (RcChunkReceipt);
        /// 0 (the default) writes the rows exactly as before. Diagnostic only: it never changes what runs.
        /// </summary>
        public int ChunkReceiptVersion { get; set; }

        // ---- the run state the chunk starts from -------------------------------------------------------------------

        public int ActiveNative { get; set; }
        public int NativeActiveMax { get; set; }
        public bool CoverageComplete { get; set; } = true;
        public bool TestsPassed { get; set; } = true;
        public bool OverallFailed { get; set; }
        public object[] Reasons { get; set; } = new object[0];

        /// <summary>$script:NightlyTestsOutcome.</summary>
        public string TestsOutcome { get; set; } = "green";

        public object[] ToleratedSkipRows { get; set; } = new object[0];
        public object[] ExpectedSkipWarnings { get; set; } = new object[0];
        public string HarnessHangReason { get; set; } = "";

        // ---- collaborators still in PowerShell -----------------------------------------------------------------------

        /// <summary>The executed manifest (TestPhaseManifests.WriteExecuted) for a native chunk's observation; null writes none.</summary>
        public Action<NightlyExecutedObservation> PublishObservation { get; set; }

        /// <summary>The active-chunk identity writer (QualificationHeartbeat.ActiveChunkWriter); null records none.</summary>
        public Action<NightlyActiveChunk> ActiveChunk { get; set; }

        /// <summary>Invoke-NightlyProduceDiscovery for a chunk that left a TRX but no discovery; null refuses (discovery-producer-unavailable).</summary>
        public Action<NightlyDiscoveryRequest> ProduceDiscovery { get; set; }

        /// <summary>
        /// The context of the executor's variables: each ScopeNames entry, plus NightlyTestsOutcome, PhaseTimingDirectory,
        /// RcEvidence and Host when present. A missing variable takes the value PowerShell read it as ($null: blank, 0,
        /// false or empty); the explicit phase root is this process's ANTIPHON_TEST_PHASE_ROOT.
        /// </summary>
        public static NightlyChunkContext FromScope(IDictionary scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            object Get(string name) => NightlyChunkRunner.Value(scope.Contains(name) ? scope[name] : null);
            string Text(string name) => PsValue.Str(Get(name));
            bool Flag(string name) => PsValue.IsTrue(Get(name));
            int Number(string name) { var v = Get(name); return v == null ? 0 : PsOps.ToInt(v); }
            object[] List(string name) { var v = Get(name); return v == null ? new object[0] : PsValue.AsArray(v); }
            var build = Get("qualificationBuildSeconds");
            var started = Get("startedAt");
            var outcome = Get("NightlyTestsOutcome");
            return new NightlyChunkContext
            {
                Host = PsValue.BaseObject(Get("Host")) as NightlyHost,
                RepoRoot = Text("RepoRoot"),
                LogRoot = Text("LogRoot"),
                TimingRoot = Text("TimingRoot"),
                PhaseTimingDirectory = Text("PhaseTimingDirectory"),
                InvocationId = Text("invocationId"),
                Attempt = Text("Attempt"),
                Profile = Text("Profile"),
                Sha = Text("Sha"),
                GitRef = Text("GitRef"),
                Policy = NightlyChunkRunner.PolicyOf(Get("policy")),
                PolicyHash = Text("policyHash"),
                Platform = Text("platform"),
                RcEvidenceRoot = Text("RcEvidenceRoot"),
                StartedAt = started is DateTime at ? at : started == null ? default(DateTime) : NightlyChunkRunner.ToDateTime(started),
                BuildSeconds = build == null ? (double?)null : PsValue.Double(build),
                ProfileAware = Flag("profileAware"),
                ExpectedSkipRoster = Get("expectedSkipRoster"),
                RcSkipRoster = Get("rcSkipRoster") == null ? null : List("rcSkipRoster").Select(e => PsValue.BaseObject(NightlyChunkRunner.Value(e))).OfType<RcSkipEntry>().ToArray(),
                ExplicitPhaseRoot = Environment.GetEnvironmentVariable("ANTIPHON_TEST_PHASE_ROOT") ?? "",
                RcEvidence = PsValue.BaseObject(Get("RcEvidence")) as RcRunEvidenceRecorder,
                ActiveNative = Number("activeNative"),
                NativeActiveMax = Number("nativeActiveMax"),
                CoverageComplete = Flag("coverageComplete"),
                TestsPassed = Flag("testsPassed"),
                OverallFailed = Flag("overallFailed"),
                Reasons = List("reasons"),
                TestsOutcome = outcome == null ? null : PsValue.Str(outcome),
                ToleratedSkipRows = List("toleratedSkipRows"),
                ExpectedSkipWarnings = List("expectedSkipWarnings"),
                HarnessHangReason = Text("harnessHangReason"),
            };
        }
    }

    /// <summary>
    /// A prepared suite's state its chunks share (Prepare-NightlySuiteExecution's Variables): what the chunks read, and
    /// the chunk count, the test host, the discovery nodes and the terminal nodes they change.
    /// </summary>
    public sealed class NightlySuiteState
    {
        public string SuiteId { get; set; } = "";

        /// <summary>The policy's suite row (its assembly).</summary>
        public object Suite { get; set; }

        public string Label { get; set; } = "";
        public bool IsNative { get; set; }

        /// <summary>The profile's exclusion rows for this suite.</summary>
        public object ProfileExclusions { get; set; }

        /// <summary>$suiteHost: the native test host, resolved by the first chunk when the suite left it unset.</summary>
        public NightlyTestHost Host { get; set; }

        /// <summary>$suiteDiscoveryNodes: the suite's discovery, or the first native verdict's when the suite had none.</summary>
        public object[] DiscoveryNodes { get; set; } = new object[0];

        /// <summary>The profile discovery document, or null.</summary>
        public object ProfileDiscovery { get; set; }

        public string ProfileDiscoveryPath { get; set; } = "";

        /// <summary>CARD-1419: the suite's one discovery index, shared by every launch verdict.</summary>
        public NightlyDiscoveryIndex DiscoveryIndex { get; set; }

        /// <summary>The suite's census, or null (each verdict's own then).</summary>
        public object Census { get; set; }

        public double DiscoverySeconds { get; set; }

        /// <summary>$suiteTerminalNodes: every chunk's terminal nodes so far.</summary>
        public object[] TerminalNodes { get; set; } = new object[0];

        public int ChunkIndex { get; set; }

        /// <summary>The state of Prepare-NightlySuiteExecution's Variables table.</summary>
        public static NightlySuiteState FromVariables(IDictionary variables)
        {
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            object Get(string name) => NightlyChunkRunner.Value(variables.Contains(name) ? variables[name] : null);
            // The array itself when it is one (19,000 discovery nodes in the main suite), not a copy per chunk.
            object[] List(string name)
            {
                var v = Get(name);
                if (v == null) return new object[0];
                return PsValue.BaseObject(v) as object[] ?? PsValue.AsArray(v);
            }
            var seconds = Get("suiteDiscoverySeconds");
            var index = Get("chunkIndex");
            return new NightlySuiteState
            {
                SuiteId = PsValue.Str(Get("suiteId")),
                Suite = Get("suite"),
                Label = PsValue.Str(Get("label")),
                IsNative = PsValue.IsTrue(Get("isNative")),
                ProfileExclusions = Get("profileExclusions"),
                Host = PsValue.BaseObject(Get("suiteHost")) as NightlyTestHost,
                DiscoveryNodes = List("suiteDiscoveryNodes"),
                ProfileDiscovery = Get("profileDiscovery"),
                ProfileDiscoveryPath = PsValue.Str(Get("profileDiscoveryPath")),
                DiscoveryIndex = PsValue.BaseObject(Get("suiteDiscoveryIndex")) as NightlyDiscoveryIndex,
                Census = Get("suiteCensus"),
                DiscoverySeconds = seconds == null ? 0.0 : PsValue.Double(seconds),
                TerminalNodes = List("suiteTerminalNodes"),
                ChunkIndex = index == null ? 0 : PsOps.ToInt(index),
            };
        }

        /// <summary>Writes back what a chunk changes: chunkIndex, suiteHost, suiteDiscoveryNodes and suiteTerminalNodes.</summary>
        public void WriteVariables(IDictionary variables)
        {
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            variables["chunkIndex"] = ChunkIndex;
            variables["suiteHost"] = Host;
            variables["suiteDiscoveryNodes"] = DiscoveryNodes;
            variables["suiteTerminalNodes"] = TerminalNodes;
        }
    }

    /// <summary>
    /// A chunk's outcome: the run state after it (the context's, changed), the suite row it adds (null when it failed
    /// first), and the failure, if any. Scope holds the executor's variables to set back.
    /// </summary>
    public sealed class NightlyChunkOutcome
    {
        internal NightlyChunkOutcome(NightlyChunkContext context)
        {
            ActiveNative = context.ActiveNative;
            NativeActiveMax = context.NativeActiveMax;
            CoverageComplete = context.CoverageComplete;
            TestsPassed = context.TestsPassed;
            OverallFailed = context.OverallFailed;
            ReasonList = new List<object>(context.Reasons ?? new object[0]);
            TestsOutcome = context.TestsOutcome;
            ToleratedSkipList = new List<object>(context.ToleratedSkipRows ?? new object[0]);
            WarningList = new List<object>(context.ExpectedSkipWarnings ?? new object[0]);
            HarnessHangReason = context.HarnessHangReason ?? "";
        }

        public int ActiveNative { get; internal set; }
        public int NativeActiveMax { get; internal set; }
        public bool CoverageComplete { get; internal set; }
        public bool TestsPassed { get; internal set; }
        public bool OverallFailed { get; internal set; }
        public string TestsOutcome { get; internal set; }
        public string HarnessHangReason { get; internal set; }

        internal List<object> ReasonList { get; set; }
        internal List<object> ToleratedSkipList { get; }
        internal List<object> WarningList { get; }

        public object[] Reasons => ReasonList.ToArray();
        public object[] ToleratedSkipRows => ToleratedSkipList.ToArray();
        public object[] ExpectedSkipWarnings => WarningList.ToArray();

        /// <summary>The suite row ($suiteResults += $row); null when the chunk failed before adding it.</summary>
        public OrderedDictionary Row { get; internal set; }

        /// <summary>The failure that ended the chunk (its message is the reason the executor records), or null.</summary>
        public Exception Error { get; internal set; }

        /// <summary>The executor's variables after the chunk, by name (the outcome is applied to that scope).</summary>
        public Hashtable Scope => new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            { "activeNative", ActiveNative }, { "nativeActiveMax", NativeActiveMax }, { "coverageComplete", CoverageComplete },
            { "testsPassed", TestsPassed }, { "overallFailed", OverallFailed }, { "reasons", Reasons },
            { "toleratedSkipRows", ToleratedSkipRows }, { "expectedSkipWarnings", ExpectedSkipWarnings }, { "harnessHangReason", HarnessHangReason },
        };
    }

    /// <summary>Invoke-NightlyScheduledChunk and the owned-process, observation and measured-phase helpers around it.</summary>
    public static class NightlyChunkRunner
    {
        private static readonly string[] PhaseVariableNames =
        {
            "ANTIPHON_TEST_PHASE_ROOT", "ANTIPHON_TEST_PHASE_RUN_ID", "ANTIPHON_TEST_PHASE_GROUP_ID", "ANTIPHON_TEST_PHASE_ATTEMPT", "ANTIPHON_TEST_PHASE_SUITE",
        };

        // Add-NightlyOwnerObservation: the owner observation's fields under their manifest names, both spellings, first wins.
        private static readonly string[][] OwnerFields =
        {
            new[] { "RootStartedAtUtc", "rootStartedAtUtc" }, new[] { "rootStartedAtUtc", "rootStartedAtUtc" },
            new[] { "RootExitedAtUtc", "rootExitedAtUtc" }, new[] { "rootExitedAtUtc", "rootExitedAtUtc" },
            new[] { "JobEmptyObserved", "jobEmpty" }, new[] { "jobEmpty", "jobEmpty" },
            new[] { "DrainMilliseconds", "drainMilliseconds" }, new[] { "drainMilliseconds", "drainMilliseconds" },
            new[] { "ExecutionMilliseconds", "executionMilliseconds" }, new[] { "executionMilliseconds", "executionMilliseconds" },
            new[] { "LaunchMilliseconds", "launchMilliseconds" }, new[] { "launchMilliseconds", "launchMilliseconds" },
            new[] { "CleanupMilliseconds", "cleanupMilliseconds" }, new[] { "cleanupMilliseconds", "cleanupMilliseconds" },
            new[] { "ChildrenExited", "childrenExited" }, new[] { "childrenExited", "childrenExited" },
            new[] { "OutputDrained", "outputDrained" }, new[] { "outputDrained", "outputDrained" },
            new[] { "RemainingProcessCount", "remainingProcessCount" }, new[] { "remainingProcessCount", "remainingProcessCount" },
            new[] { "MissedDescendantPids", "missedDescendantPids" }, new[] { "missedDescendantPids", "missedDescendantPids" },
            new[] { "DescendantIdentities", "descendantIdentities" }, new[] { "descendantIdentities", "descendantIdentities" },
            new[] { "DescendantImages", "descendantImages" }, new[] { "descendantImages", "descendantImages" },
            new[] { "DescendantExitedAtUtc", "descendantExitedAtUtc" }, new[] { "descendantExitedAtUtc", "descendantExitedAtUtc" },
            new[] { "Descendants", "descendants" }, new[] { "descendants", "descendants" },
        };

        private static readonly Regex PhaseName = new Regex("^[a-z0-9][a-z0-9-]{0,40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ProcessExitReason = new Regex("^process-exit-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Invoke-NightlyScheduledChunk: runs one chunk of a prepared suite (client: the CLI's test-client; scripts: each unattended
        /// census script; otherwise the native test host under the launch watchdog), judges its log, its native verdict and
        /// its timeout, records its timing, and returns the run state after it with its suite row. Never throws for the
        /// chunk's own failures: they are the outcome's Error.
        /// </summary>
        public static NightlyChunkOutcome Run(NightlyChunkContext context, NightlySuiteState suite, object chunk)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (context.Host == null) throw new ArgumentException("The chunk context needs its run's host.", nameof(context));
            var outcome = new NightlyChunkOutcome(context);
            var chunkId = PsValue.Str(PsValue.Prop(chunk, "id"));
            try
            {
                RunChunk(context, suite, chunk, chunkId, outcome);
            }
            catch (Exception ex)
            {
                outcome.Row = null;
                outcome.Error = ex;
            }
            finally
            {
                if (suite.IsNative)
                {
                    try { RecordActiveChunk(context, suite.SuiteId, chunkId, "Idle"); }
                    catch (Exception ex) { if (outcome.Error == null) outcome.Error = ex; }
                }
            }
            return outcome;
        }

        private static void RunChunk(NightlyChunkContext c, NightlySuiteState s, object chunk, string chunkId, NightlyChunkOutcome o)
        {
            var host = c.Host;
            var suiteId = s.SuiteId;
            var isClient = string.Equals(suiteId, "client", StringComparison.OrdinalIgnoreCase);
            var isScripts = string.Equals(suiteId, "scripts", StringComparison.OrdinalIgnoreCase);
            var chunkTestsPassed = true;
            var chunkCoverageComplete = true;
            var exitTolerated = false;
            var recordNativeChunk = false;
            s.ChunkIndex++;
            var chunkStartedAt = Stamp(host.UtcNow);
            var queueStart = host.Clock.GetTimestamp();
            var logPath = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + "-tests.log");
            var trxPath = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + ".trx");
            var jsonPath = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + ".json");
            var envMap = NightlyPolicy.SafeChildEnvironment(c.Policy, suiteId, null);
            RemovePhaseVariables(envMap);
            envMap["C487_INVOCATION"] = c.InvocationId;
            envMap["C487_RUN_DIR"] = c.LogRoot;

            if (s.IsNative)
            {
                if (o.ActiveNative > 0) throw new NightlyRefusal("native overlap");
                o.ActiveNative++;
                var current = host.Launcher.NativeActiveCount;
                if (current > o.NativeActiveMax) o.NativeActiveMax = current;
                if (o.ActiveNative > o.NativeActiveMax) o.NativeActiveMax = o.ActiveNative;
            }

            NightlyLaunchResult run;
            var idleHang = false;
            var execDiagDir = "";
            var queueWaitSeconds = Math.Round(host.Clock.GetElapsedTime(queueStart).TotalSeconds, 3);
            var chunkDiscoverySeconds = NightlyTestsPlan.ChunkDiscoverySeconds(s.DiscoverySeconds, null, false).ChunkDiscoverySeconds;
            var heartbeatTrx = s.IsNative ? trxPath : "";
            var running = Timing(c, suiteId, chunkId);
            running.LastPhase = "run";
            running.StartedAt = chunkStartedAt;
            running.QueueWaitSeconds = queueWaitSeconds;
            running.BuildSeconds = c.BuildSeconds;
            running.DiscoverySeconds = chunkDiscoverySeconds;
            running.TrxPath = heartbeatTrx;
            QualificationTiming.Update(running, host.UtcNow);
            // A launch that throws is recorded as launch-failed and the chunk ends with it.
            NightlyLaunchResult Launch(NightlyLaunch launch)
            {
                try { return host.Launch(launch); }
                catch (Exception)
                {
                    var failed = Timing(c, suiteId, chunkId);
                    failed.LastPhase = "launch";
                    failed.Why = "launch-failed";
                    failed.StartedAt = chunkStartedAt;
                    failed.QueueWaitSeconds = queueWaitSeconds;
                    failed.BuildSeconds = c.BuildSeconds;
                    failed.DiscoverySeconds = chunkDiscoverySeconds;
                    failed.TrxPath = heartbeatTrx;
                    QualificationTiming.Update(failed, host.UtcNow);
                    throw;
                }
            }

            var runStart = host.Clock.GetTimestamp();
            if (isClient)
            {
                // The client suite is the CLI's test-client command on the checkout under test (its gates, then Vitest).
                var cli = string.IsNullOrWhiteSpace(c.CliPath) ? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "" : c.CliPath;
                if (string.IsNullOrWhiteSpace(cli)) throw new NightlyRefusal("client-cli-unresolved");
                run = Launch(OwnedLaunch("dotnet", new[] { "exec", cli, "test-client", "--repo-root", c.RepoRoot, "--json-result-path", jsonPath }, c.RepoRoot, 1200000, envMap, logPath));
            }
            else if (isScripts)
            {
                var scriptFail = false;
                foreach (var row in NightlyPolicy.ScriptCensus(c.Policy))
                {
                    if (!string.Equals(row.Disposition ?? "", "unattended", StringComparison.OrdinalIgnoreCase)) continue;
                    if (row.Wildcard) throw new NightlyRefusal("wildcard script execution refused");
                    var scriptPath = Path.Combine(c.RepoRoot, (row.Path ?? "").Replace('/', Path.DirectorySeparatorChar));
                    var scriptArgs = new List<string> { "-NoProfile", "-NonInteractive", "-File", scriptPath };
                    if (PsValue.IsTrue(row.Arguments)) foreach (var argument in row.Arguments) scriptArgs.Add(argument ?? "");
                    var scriptRun = Launch(OwnedLaunch("pwsh", scriptArgs, c.RepoRoot, 600000, envMap, Path.Combine(c.LogRoot, "script-" + row.Id + ".log")));
                    if (scriptRun.ExitCode != 0) scriptFail = true;
                }
                run = new NightlyLaunchResult { ExitCode = scriptFail ? 1 : 0, TimedOut = false, ChildrenExited = true };
            }
            else
            {
                if (s.Host == null)
                {
                    var exeName = PsValue.Str(PsValue.Prop(s.Suite, "assembly"));
                    if (string.IsNullOrWhiteSpace(exeName)) exeName = s.Label;
                    s.Host = NightlyTestsPlan.TestHost(c.RepoRoot, exeName);
                }
                execDiagDir = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + "-execution");
                CreateChunkDirectory(host, execDiagDir);
                var chunkClasses = new string[0];
                var classesValue = PsValue.Prop(chunk, "classes");
                if (PsValue.IsTrue(classesValue) && PsValue.AsArray(classesValue).Length > 0) chunkClasses = PsValue.AsArray(classesValue).Select(PsValue.Str).ToArray();
                var groupFilter = Has(chunk, "groupFilter") ? PsValue.Str(PsValue.Prop(chunk, "groupFilter")) : "";
                var methodFilter = Has(chunk, "methodFilter") ? PsValue.Str(PsValue.Prop(chunk, "methodFilter")) : "";
                var nativeArgs = new List<string>();
                foreach (var prefixArg in s.Host.Prefix ?? new string[0])
                    if (!string.IsNullOrWhiteSpace(prefixArg)) nativeArgs.Add(prefixArg);
                nativeArgs.AddRange(NightlyTestsPlan.NativeExecutionArguments(trxPath, execDiagDir, chunkClasses, PsValue.Str(PsValue.Prop(chunk, "exactFilter")), groupFilter, methodFilter));
                var phaseEnv = new Hashtable(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry pair in envMap) phaseEnv[PsValue.Str(pair.Key)] = PsValue.Str(pair.Value);
                EnablePhaseEnvironment(phaseEnv, c.LogRoot, c.InvocationId, chunkId, c.Attempt, suiteId, c.Profile, c.TimingRoot, c.ExplicitPhaseRoot);
                object plannedSeconds = null;
                if (Has(chunk, "seconds") && PsValue.Prop(chunk, "seconds") != null) plannedSeconds = PsValue.Prop(chunk, "seconds");
                var costBasis = "default";
                if (Has(chunk, "costBasis") && !string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(chunk, "costBasis")))) costBasis = PsValue.Str(PsValue.Prop(chunk, "costBasis"));
                var admission = NightlyWatchdog.ResolveLaunch(suiteId, plannedSeconds, costBasis, "", null, null, c.Policy);
                if (!PsValue.IsTrue(PsValue.Prop(admission, "Admitted")))
                    throw new NightlyRefusal("watchdog-admission-refused id=" + chunkId + " reason=" + PsValue.Str(PsValue.Prop(admission, "Reason")));
                RecordActiveChunk(c, suiteId, chunkId, "Active");
                // S6b shadow: join the build seal before the timed launch. A refusal stays unproven and does not change the verdict.
                c.RcEvidence?.Launch(c.RcEvidenceRoot, c.LogRoot, suiteId, chunkId, Path.GetDirectoryName(s.Host.AssemblyPath ?? ""), nativeArgs.ToArray(), "", c.RepoRoot, c.Sha);
                var phaseStart = host.Clock.GetTimestamp();
                var nativeLaunch = OwnedLaunch(s.Host.FilePath, nativeArgs, c.RepoRoot, PsOps.ToInt(PsValue.Prop(admission, "WatchdogMilliseconds")), phaseEnv, logPath,
                    trxPath, execDiagDir, logPath + ".hang.txt", c.CompletionGraceMilliseconds);
                if (nativeLaunch.Hooks != null && c.Profile == "rc" && !string.IsNullOrEmpty(c.InvocationId)
                    && host.Variables(QualificationSupervise.RunIdVariable) == c.InvocationId)
                {
                    var minutes = 20.0;
                    if (double.TryParse(host.Variables(QualificationSupervise.HangMinutesVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out var configured)
                        && configured >= 1 && configured <= 600) minutes = configured;
                    var provenSeconds = string.IsNullOrEmpty(c.TimingRoot) ? null : QualificationHeartbeat.PlannedCost(
                        Path.Combine(c.TimingRoot, "groups-planned-" + suiteId + ".json"), chunkId, suiteId, c.InvocationId, c.Attempt).Seconds;
                    nativeLaunch.Hooks.IdleMonitor = new NightlyChunkIdleMonitor(host.Clock, trxPath, execDiagDir, minutes, provenSeconds);
                    nativeLaunch.Hooks.ActiveHeartbeatPath = Path.Combine(c.LogRoot, NightlyChunkIdleMonitor.HeartbeatFile);
                }
                run = Launch(nativeLaunch);
                idleHang = run.TimedOut && !string.IsNullOrEmpty(run.Owner?.IdleHangReason);
                var hostWallSeconds = host.Clock.GetElapsedTime(phaseStart).TotalSeconds;
                recordNativeChunk = true;
                try { Publish(c, run, trxPath, suiteId, chunkId, hostWallSeconds, o.CoverageComplete && !idleHang, o.TestsPassed && !idleHang); }
                catch (Exception) { WriteFailureMarker(c, "phase-summary-failed"); }
            }
            var runWallSeconds = Math.Round(host.Clock.GetElapsedTime(runStart).TotalSeconds, 3);

            var cleaned = host.Launcher.WaitCleanup(run);
            if (s.IsNative)
            {
                o.ActiveNative--;
                if (!cleaned) throw new NightlyRefusal("owned-child still running");
            }

            StampLog(host, logPath, c.InvocationId);
            var fresh = NightlyChunkVerdict.Freshness(logPath, c.LogRoot, c.StartedAt, c.InvocationId);
            var suiteRow = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "id", suiteId }, { "chunk", chunkId }, { "name", s.Label }, { "log", logPath }, { "exitCode", run.ExitCode }, { "timedOut", run.TimedOut },
                { "skipped", false }, { "result", run.TimedOut ? "TIMEOUT" : run.ExitCode != 0 ? "FAIL" : "pass" },
            };
            if (!fresh.Ok)
            {
                chunkTestsPassed = false;
                chunkCoverageComplete = false;
                o.CoverageComplete = false;
                o.TestsPassed = false;
                o.OverallFailed = true;
                suiteRow["detail"] = fresh.Reason;
                o.ReasonList.Add(fresh.Reason);
            }

            object nativeVerdict = null;
            if (s.IsNative)
            {
                var discPath = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + ".discovery.json");
                if (PsValue.IsTrue(s.ProfileDiscovery))
                {
                    discPath = s.ProfileDiscoveryPath;
                    if (PsValue.IsTrue(PsValue.Prop(chunk, "exactFilter")) || (Has(chunk, "methodFilter") && PsValue.IsTrue(PsValue.Prop(chunk, "methodFilter"))))
                    {
                        // The full discovery remains the suite union authority; the leaf verdict checks just this chunk's method expansions.
                        discPath = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + "-selection.discovery.json");
                        var leafNodes = PsValue.Prop(chunk, "nodes");
                        var leaf = NightlyDiagnosticLog.NewDiscoveryDocument(leafNodes == null ? null : PsValue.AsArray(leafNodes), PsValue.Str(PsValue.Prop(s.ProfileDiscovery, "assemblyHash")));
                        NightlyDiagnosticLog.WriteDiscoveryDocument(discPath, leaf);
                    }
                }
                if (Exists(trxPath) && !Exists(discPath))
                {
                    try
                    {
                        var diagDir = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + "-discovery");
                        var discLog = Path.Combine(c.LogRoot, suiteId + "-" + chunkId + "-discovery.log");
                        var discStart = host.Clock.GetTimestamp();
                        var produce = c.ProduceDiscovery ?? throw new NightlyRefusal("discovery-producer-unavailable");
                        produce(new NightlyDiscoveryRequest(s.Host.FilePath, s.Host.Prefix, s.Host.AssemblyPath, discPath, diagDir, discLog, c.RepoRoot, envMap));
                        var markerSeconds = host.Clock.GetElapsedTime(discStart).TotalSeconds;
                        chunkDiscoverySeconds = NightlyTestsPlan.ChunkDiscoverySeconds(s.DiscoverySeconds, markerSeconds, true).ChunkDiscoverySeconds;
                        c.RcEvidence?.DiscoveryOnce(c.RcEvidenceRoot, c.LogRoot, suiteId, discPath);
                    }
                    catch (Exception ex)
                    {
                        o.ReasonList.Add("discovery-produce-failed " + ex.Message);
                    }
                }
                var requiredClasses = PsValue.Prop(chunk, "classes");
                var required = PsValue.IsTrue(requiredClasses) ? PsValue.AsArray(requiredClasses).Select(PsValue.Str).ToArray() : new string[0];
                var execDiagPath = NightlyDiagnosticLog.Latest(execDiagDir);
                nativeVerdict = NightlyChunkVerdict.NativeSuiteVerdict(trxPath, discPath, execDiagPath, c.LogRoot, c.StartedAt, run.ExitCode, c.Sha, c.Sha, c.GitRef, c.GitRef,
                    c.PolicyHash, c.PolicyHash, required, s.ProfileExclusions, c.ProfileAware, s.DiscoveryIndex, c.Platform, c.ExpectedSkipRoster);
                if (Has(nativeVerdict, "processExitTolerated") && PsValue.IsTrue(PsValue.Prop(nativeVerdict, "processExitTolerated")))
                {
                    exitTolerated = true;
                    suiteRow["processExitTolerated"] = true;
                }
                if (Has(nativeVerdict, "toleratedSkips"))
                    foreach (var toleratedSkip in PsValue.AsArray(PsValue.Prop(nativeVerdict, "toleratedSkips")))
                        if (toleratedSkip != null) o.ToleratedSkipList.Add(toleratedSkip);
                if (Has(nativeVerdict, "warnings"))
                    foreach (var skipWarning in PsValue.AsArray(PsValue.Prop(nativeVerdict, "warnings")))
                    {
                        var text = PsValue.Str(skipWarning);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        if (!PsOps.Contains(o.WarningList.ToArray(), text, true)) o.WarningList.Add(text);
                    }
                var verdictNodes = PsValue.Prop(nativeVerdict, "discoveryNodes");
                if (PsValue.IsTrue(verdictNodes) && PsValue.AsArray(verdictNodes).Length > 0 && s.DiscoveryNodes.Length == 0) s.DiscoveryNodes = PsValue.AsArray(verdictNodes);
                var terminalNodes = PsValue.Prop(nativeVerdict, "terminalNodes");
                // One concatenation per chunk, not one array copy per terminal node (19,000 in the main suite).
                if (PsValue.IsTrue(terminalNodes)) s.TerminalNodes = s.TerminalNodes.Concat(PsValue.AsArray(terminalNodes)).ToArray();
                suiteRow["trx"] = trxPath;
                if (c.ChunkReceiptVersion == RcChunkReceipt.Version && File.Exists(trxPath) && File.Exists(execDiagPath))
                {
                    suiteRow["trxDigest"] = RcChunkReceipt.FileDigest(trxPath);
                    suiteRow["executionDiagnostic"] = execDiagPath;
                    suiteRow["executionDiagnosticDigest"] = RcChunkReceipt.FileDigest(execDiagPath);
                }
                suiteRow["evidenceReasons"] = PsValue.AsArray(PsValue.Prop(nativeVerdict, "reasons"));
                var census = PsValue.IsTrue(s.Census) ? s.Census : PsValue.Prop(nativeVerdict, "census");
                if (PsValue.IsTrue(census))
                {
                    // D-5: expanded-UID accounting is manifest evidence; record both counts and the digest, plus every exclusion with its reason.
                    suiteRow["requiredUidCount"] = PsOps.ToInt(PsValue.Prop(census, "RequiredCount"));
                    suiteRow["excludedUidCount"] = PsOps.ToInt(PsValue.Prop(census, "ExcludedCount"));
                    suiteRow["censusDigest"] = PsValue.Str(PsValue.Prop(census, "Digest"));
                    suiteRow["exclusions"] = PsValue.AsArray(PsValue.Prop(census, "Excluded"));
                }
                if (!PsValue.IsTrue(PsValue.Prop(nativeVerdict, "coverageComplete")))
                {
                    chunkCoverageComplete = false;
                    o.CoverageComplete = false;
                    suiteRow["coverageComplete"] = false;
                    foreach (var reason in PsValue.AsArray(PsValue.Prop(nativeVerdict, "reasons"))) o.ReasonList.Add(reason);
                }
                if (!PsValue.IsTrue(PsValue.Prop(nativeVerdict, "testsPassed")))
                {
                    chunkTestsPassed = false;
                    o.TestsPassed = false;
                    o.OverallFailed = true;
                    suiteRow["result"] = "FAIL";
                }
                else if (exitTolerated && PsValue.IsTrue(PsValue.Prop(nativeVerdict, "coverageComplete")))
                {
                    suiteRow["result"] = "pass";
                }
            }

            var observed = run.Observed;
            if (recordNativeChunk && c.RcEvidence != null && c.RcEvidence.Recording)
            {
                // The shadow parse of this launch, bound to the native verdict and the log freshness above.
                var chunkRecord = RcChunkOutcome.Read(run.ExitCode, PsValue.IsTrue(PsValue.Prop(observed, "OutputDrainTimedOut")), trxPath, c.RcSkipRoster, c.Platform);
                if (s.IsNative)
                    chunkRecord = RcChunkOutcome.Bind(chunkRecord, Nullable(PsValue.Prop(nativeVerdict, "testsPassed")), Nullable(PsValue.Prop(nativeVerdict, "coverageComplete")),
                        PsValue.AsArray(PsValue.Prop(nativeVerdict, "reasons")).Select(PsValue.Str).ToArray(), fresh.Ok, fresh.Reason ?? "");
                c.RcEvidence.ChunkRevision(c.RcEvidenceRoot, c.LogRoot, suiteId, chunkId, chunkRecord.Outcome, chunkRecord.Reason, false);
            }
            if (isClient && !Exists(jsonPath))
            {
                chunkTestsPassed = false;
                chunkCoverageComplete = false;
                o.CoverageComplete = false;
                o.TestsPassed = false;
                o.OverallFailed = true;
                o.ReasonList.Add("missing-client-json");
                suiteRow["coverageComplete"] = false;
            }

            var completedHang = false;
            if (run.TimedOut)
            {
                o.OverallFailed = true;
                var finished = false;
                var childText = "";
                var rootPid = 0;
                var pid = PsValue.Property(observed, "Pid", out var hasPid);
                if (hasPid && pid != null) rootPid = PsOps.ToInt(pid);
                if (s.IsNative && !idleHang)
                {
                    var hangDiag = NightlyDiagnosticLog.Latest(execDiagDir);
                    try { finished = NightlyWatchdog.ExecutionFinished(trxPath, hangDiag); } catch (Exception) { finished = false; }
                    object identities = new object[0];
                    object images = new object[0];
                    var observedIdentities = PsValue.Property(observed, "DescendantIdentities", out var hasIdentities);
                    if (hasIdentities) identities = PsValue.AsArray(observedIdentities);
                    var observedImages = PsValue.Property(observed, "DescendantImages", out var hasImages);
                    if (hasImages) images = PsValue.AsArray(observedImages);
                    childText = NightlyWatchdog.HangChildren(identities, images, rootPid);
                    var hung = PsValue.Property(observed, "CompletedButHung", out var hasHung);
                    if (hasHung && PsValue.IsTrue(hung)) finished = true;
                }
                var resolvedHang = NightlyWatchdog.CompletedHang(true, finished, suiteId, childText);
                completedHang = PsValue.IsTrue(PsValue.Prop(resolvedHang, "CompletedButHung"));
                var timeoutDisposition = NightlyWatchdog.TimeoutDisposition(Exists(trxPath), false, completedHang);
                o.CoverageComplete = PsValue.IsTrue(PsValue.Prop(timeoutDisposition, "CoverageComplete"));
                o.TestsPassed = PsValue.IsTrue(PsValue.Prop(timeoutDisposition, "TestsPassed"));
                if (completedHang)
                {
                    suiteRow["result"] = "FAIL";
                    // The kill's exit code is not a test reason: drop it from the row and its last copy from the run's reasons.
                    suiteRow["evidenceReasons"] = PsValue.AsArray(suiteRow["evidenceReasons"]).Where(r => !ProcessExitReason.IsMatch(PsValue.Str(r))).ToArray();
                    var killReason = "process-exit-" + run.ExitCode.ToString(CultureInfo.InvariantCulture);
                    var kept = new List<object>();
                    var droppedKill = false;
                    for (var i = o.ReasonList.Count - 1; i >= 0; i--)
                    {
                        var text = PsValue.Str(o.ReasonList[i]);
                        if (!droppedKill && string.Equals(text, killReason, StringComparison.OrdinalIgnoreCase)) { droppedKill = true; continue; }
                        kept.Insert(0, text);
                    }
                    o.ReasonList = kept;
                    o.ReasonList.Add(PsValue.Str(PsValue.Prop(resolvedHang, "Reason")));
                    var capturePath = "";
                    var observedCapture = PsValue.Property(observed, "HangCapturePath", out var hasCapture);
                    if (hasCapture && !string.IsNullOrWhiteSpace(PsValue.Str(observedCapture))) capturePath = PsValue.Str(observedCapture);
                    if (string.IsNullOrWhiteSpace(capturePath) || !Exists(capturePath))
                    {
                        capturePath = logPath + ".hang.txt";
                        if (!Exists(capturePath)) NightlyWatchdog.WriteHangCapture(capturePath, suiteId, childText, rootPid);
                    }
                    suiteRow["hangCapture"] = capturePath;
                    // TESTS, not TIMEOUT, so publicationTerminal is failed-run.
                    o.HarnessHangReason = "completed-but-hung";
                    o.TestsOutcome = NightlyTestsPlan.Outcome(o.TestsOutcome, "TESTS");
                }
                else
                {
                    o.TestsOutcome = NightlyTestsPlan.Outcome(o.TestsOutcome, "TIMEOUT");
                    o.ReasonList.Add(idleHang ? "idle-hang " + suiteId : "timeout " + suiteId);
                    if (idleHang)
                    {
                        suiteRow["result"] = "FAIL";
                        suiteRow["coverageComplete"] = false;
                        suiteRow["detail"] = "idle-hang";
                        if (!string.IsNullOrEmpty(run.Owner.HangCapturePath)) suiteRow["hangCapture"] = run.Owner.HangCapturePath;
                    }
                }
            }
            else if (run.ExitCode != 0 && !exitTolerated)
            {
                o.OverallFailed = true;
                o.TestsPassed = false;
                o.TestsOutcome = NightlyTestsPlan.Outcome(o.TestsOutcome, "TESTS");
            }
            else if (!chunkTestsPassed || !chunkCoverageComplete)
            {
                o.OverallFailed = true;
                o.TestsOutcome = NightlyTestsPlan.Outcome(o.TestsOutcome, "TESTS");
            }

            var timingPhase = "complete";
            var timingWhy = "";
            if (run.TimedOut) { timingPhase = "timeout"; timingWhy = "timeout"; }
            else if (s.IsNative && !Exists(trxPath)) timingWhy = "missing-trx";
            else if (isClient && !Exists(jsonPath)) timingWhy = "missing-client-json";
            else if (isClient) timingWhy = "no-trx:client";
            else if (isScripts && run.ExitCode != 0) timingWhy = "script-failed";
            else if (isScripts) timingWhy = "no-trx:scripts";
            if (completedHang) timingWhy = "completed-but-hung";
            if (idleHang) timingWhy = "idle-hang";
            var terminal = Timing(c, suiteId, chunkId);
            terminal.LastPhase = timingPhase;
            terminal.Why = timingWhy;
            terminal.RunWallSeconds = runWallSeconds;
            terminal.DiscoverySeconds = chunkDiscoverySeconds;
            terminal.BuildSeconds = c.BuildSeconds;
            terminal.QueueWaitSeconds = queueWaitSeconds;
            terminal.TrxPath = s.IsNative ? trxPath : "";
            terminal.Terminal = true;
            if (!run.TimedOut) terminal.CompletedAt = Stamp(host.UtcNow);
            QualificationTiming.Update(terminal, host.UtcNow);
            if (c.ChunkReceiptVersion == RcChunkReceipt.Version)
            {
                var receiptClasses = PsValue.IsTrue(PsValue.Prop(chunk, "classes")) ? PsValue.AsArray(PsValue.Prop(chunk, "classes")).Select(PsValue.Str).ToArray() : new string[0];
                var assembly = s.Host == null ? suiteId : Path.GetFileNameWithoutExtension(s.Host.AssemblyPath ?? suiteId);
                var roster = s.IsNative && nativeVerdict != null
                    ? RcChunkReceipt.RosterFromNodes(assembly, PsValue.AsArray(PsValue.Prop(nativeVerdict, "discoveryNodes"))
                        .Where(node => !NightlyCensus.Excluded(node, s.ProfileExclusions, c.ProfileAware)).ToArray(), receiptClasses)
                    : Array.Empty<RcCaseIdentity>();
                RcChunkReceipt.Stamp(suiteRow, c.Sha, RcChunkReceipt.ContentDigest(suiteId, s.IsNative ? "native" : "command", roster),
                    new RcReceiptScope { Platform = c.Platform ?? "", Architecture = RcReceiptScope.CurrentArchitecture(), Profile = c.Profile ?? "" });
                suiteRow["runId"] = c.InvocationId;
                suiteRow["attempt"] = c.Attempt;
            }
            if (NightlyTestsPlan.IsInvariantChunk(chunk))
            {
                var terminalNodes = PsValue.Prop(nativeVerdict, "terminalNodes");
                suiteRow["observedInvariantUids"] = terminalNodes == null ? Array.Empty<string>()
                    : PsValue.AsArray(terminalNodes).Select(node => PsValue.Str(PsValue.Prop(node, "Uid"))).ToArray();
                suiteRow["coverageComplete"] = chunkCoverageComplete;
            }
            o.Row = suiteRow;
        }

        // ---- the owned launch, the observation and the measured phases ---------------------------------------------

        /// <summary>
        /// Invoke-NightlyOwnedProcess's launch: Owned custody of fileName with the arguments (null: none) in the working
        /// directory under the timeout, the environment map (null: this process's) as strings, the log; on Windows the
        /// native owner's hooks: the TRX and diagnostic of the completion grace, the hang capture path, and a positive
        /// grace (0 or less: the owner's default).
        /// </summary>
        public static NightlyLaunch OwnedLaunch(string fileName, IEnumerable<string> arguments, string workingDirectory, int timeoutMilliseconds, IDictionary environment,
            string logPath, string trxPath = "", string diagnosticDirectory = "", string hangCapturePath = "", int executionGraceMilliseconds = 0)
        {
            NativeProcessHooks hooks = null;
            if (OperatingSystem.IsWindows())
            {
                hooks = new NativeProcessHooks();
                if (!string.IsNullOrWhiteSpace(trxPath)) hooks.TrxPath = trxPath;
                if (!string.IsNullOrWhiteSpace(diagnosticDirectory)) hooks.DiagnosticDirectory = diagnosticDirectory;
                if (!string.IsNullOrWhiteSpace(hangCapturePath)) hooks.HangCapturePath = hangCapturePath;
                if (executionGraceMilliseconds > 0) hooks.ExecutionGraceMilliseconds = executionGraceMilliseconds;
            }
            return new NightlyLaunch(fileName, (arguments ?? new string[0]).Select(a => a ?? "").ToArray(), workingDirectory, timeoutMilliseconds,
                StringMap(environment), logPath, NightlyCustody.Owned, hooks);
        }

        /// <summary>
        /// Add-NightlyOwnerObservation over Publish-NightlyChunkObservation's verdict: { ExitCode (the run's, 0 when it
        /// has none), coverageComplete, testsPassed } and each owner field the run has (a property, not a dictionary key)
        /// under its manifest name. run is a launch result (its Observed object is read) or any record.
        /// </summary>
        public static object ObservationVerdict(object run, object coverageComplete, object testsPassed)
        {
            var source = run is NightlyLaunchResult result ? result.Observed : run;
            var exitCode = 0;
            if (source != null)
            {
                var value = PsValue.Property(source, "ExitCode", out var found);
                if (found && value != null) exitCode = PsOps.ToInt(value);
            }
            var verdict = PsValue.Record(("ExitCode", exitCode), ("coverageComplete", coverageComplete != null && PsValue.IsTrue(coverageComplete)),
                ("testsPassed", testsPassed != null && PsValue.IsTrue(testsPassed)));
            if (source == null) return verdict;
            foreach (var pair in OwnerFields)
            {
                var value = PsValue.Property(source, pair[0], out var present);
                if (!present) continue;
                PsValue.Property(verdict, pair[1], out var taken);
                if (taken) continue;
                PsValue.AddNote(verdict, pair[1], value);
            }
            return verdict;
        }

        private static void Publish(NightlyChunkContext c, NightlyLaunchResult run, string trxPath, string suiteId, string chunkId, double hostWallSeconds,
            bool coverageComplete, bool testsPassed)
        {
            if (c.PublishObservation == null) return;
            var verdict = ObservationVerdict(run, coverageComplete, testsPassed);
            c.PublishObservation(new NightlyExecutedObservation(verdict, c.LogRoot, trxPath, suiteId, chunkId, c.InvocationId, c.Attempt, hostWallSeconds));
        }

        /// <summary>
        /// Write-NightlyMeasuredPhase: one measured run phase (QualificationTiming.WriteRunPhase) from its start and seconds,
        /// in the timing directory (blank: logRoot/timing). Nothing for a blank log root or name, a missing start or seconds,
        /// or a name that is not a phase name; a start or seconds that does not read, or a failed write, is ignored.
        /// </summary>
        public static void WriteMeasuredPhase(string logRoot, string timingDirectory, string name, object startedAt, object seconds)
        {
            if (string.IsNullOrWhiteSpace(logRoot) || string.IsNullOrWhiteSpace(name)) return;
            startedAt = Value(startedAt);
            seconds = Value(seconds);
            if (startedAt == null || seconds == null) return;
            if (!PhaseName.IsMatch(name)) return;
            try
            {
                var started = ToDateTime(startedAt);
                var span = PsValue.Double(seconds);
                var ended = started.AddSeconds(span);
                var directory = string.IsNullOrWhiteSpace(timingDirectory) ? Path.Combine(logRoot, "timing") : timingDirectory;
                QualificationTiming.WriteRunPhase(directory, name, Stamp(started), Stamp(ended), span);
            }
            catch (Exception) { }
        }

        /// <summary>Write-NightlyPrerequisitePhases: the npm-ci, npm-build, lint and dotnet-build phases.</summary>
        public static void WritePrerequisitePhases(string logRoot, string timingDirectory, object npmCiStarted, object npmCiSeconds, object npmBuildStarted, object npmBuildSeconds,
            object lintStarted, object lintSeconds, object dotnetStarted, object dotnetSeconds)
        {
            WriteMeasuredPhase(logRoot, timingDirectory, "npm-ci", npmCiStarted, npmCiSeconds);
            WriteMeasuredPhase(logRoot, timingDirectory, "npm-build", npmBuildStarted, npmBuildSeconds);
            WriteMeasuredPhase(logRoot, timingDirectory, "lint", lintStarted, lintSeconds);
            WriteMeasuredPhase(logRoot, timingDirectory, "dotnet-build", dotnetStarted, dotnetSeconds);
        }

        // ---- the phase environment (test-phase-evidence.ps1's, for this runner) --------------------------------------

        /// <summary>Remove-TestPhaseVariables: the phase variables out of the map.</summary>
        public static void RemovePhaseVariables(IDictionary environment)
        {
            if (environment == null) return;
            foreach (var name in PhaseVariableNames) environment.Remove(name);
        }

        /// <summary>
        /// Enable-TestPhaseExecutionEnvironment: a qualification profile (rc, nightly), an explicit phase root or a timing
        /// root enables the phase variables (the root: the timing root, else the explicit root, else logRoot/timing);
        /// otherwise they are removed.
        /// </summary>
        public static IDictionary EnablePhaseEnvironment(IDictionary environment, string logRoot, string runId, string groupId, string attempt, string suite,
            string profile, string timingRoot, string explicitRoot)
        {
            environment = environment ?? new Hashtable(StringComparer.OrdinalIgnoreCase);
            var qualification = string.Equals(profile, "rc", StringComparison.OrdinalIgnoreCase) || string.Equals(profile, "nightly", StringComparison.OrdinalIgnoreCase);
            if (!qualification && string.IsNullOrWhiteSpace(explicitRoot) && string.IsNullOrWhiteSpace(timingRoot))
            {
                RemovePhaseVariables(environment);
                return environment;
            }
            var root = timingRoot;
            if (string.IsNullOrWhiteSpace(root)) root = explicitRoot;
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(logRoot ?? "", "timing");
            environment["ANTIPHON_TEST_PHASE_ROOT"] = root ?? "";
            environment["ANTIPHON_TEST_PHASE_RUN_ID"] = runId ?? "";
            environment["ANTIPHON_TEST_PHASE_GROUP_ID"] = groupId ?? "";
            environment["ANTIPHON_TEST_PHASE_ATTEMPT"] = attempt ?? "";
            environment["ANTIPHON_TEST_PHASE_SUITE"] = suite ?? "";
            return environment;
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private static void RecordActiveChunk(NightlyChunkContext c, string suiteId, string chunkId, string state) =>
            c.ActiveChunk?.Invoke(new NightlyActiveChunk(c.TimingRoot, suiteId, chunkId, c.InvocationId, c.Attempt, state));

        private static void WriteFailureMarker(NightlyChunkContext c, string code) => WriteFailureMarker(c.LogRoot, c.PhaseTimingDirectory, code);

        /// <summary>
        /// Write-TestPhaseFailureMarker: the code in &lt;phase timing directory&gt;/phase-summary-failed (blank directory:
        /// logRoot/timing); an unwritable directory never changes the caller's verdict.
        /// </summary>
        internal static void WriteFailureMarker(string logRoot, string timingDirectory, string code)
        {
            try
            {
                var directory = string.IsNullOrWhiteSpace(timingDirectory) ? Path.Combine(logRoot ?? "", "timing") : timingDirectory;
                if (!Exists(directory)) Directory.CreateDirectory(directory);
                EvidenceFiles.WriteAllText(Path.Combine(directory, "phase-summary-failed"), code);
            }
            catch (Exception) { }
        }

        /// <summary>New-NightlyChunkDirectory: the directory and its parents; quiet when a file is in the way, any other failure reported, never thrown.</summary>
        private static void CreateChunkDirectory(NightlyHost host, string path)
        {
            try { Directory.CreateDirectory(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                var blocked = path;
                try
                {
                    while (!string.IsNullOrEmpty(blocked) && !File.Exists(blocked)) blocked = Path.GetDirectoryName(blocked);
                }
                catch (Exception) { blocked = ""; }
                if (string.IsNullOrEmpty(blocked)) host.Error.WriteLine("ERROR: " + ex.Message);
            }
        }

        /// <summary>The chunk log names its invocation: an existing log without the id gets the stamp line first; a missing one is the stamp line.</summary>
        private static void StampLog(NightlyHost host, string logPath, string invocationId)
        {
            var stamp = "C487_INVOCATION=" + invocationId;
            if (Exists(logPath))
            {
                var raw = File.ReadAllText(logPath);
                if (!Regex.IsMatch(raw, Regex.Escape(invocationId ?? ""), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    File.WriteAllText(logPath, stamp + Environment.NewLine + raw);
                return;
            }
            try { File.WriteAllText(logPath, stamp + Environment.NewLine); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                host.Error.WriteLine("ERROR: " + ex.Message);
            }
        }

        private static ChunkTimingUpdate Timing(NightlyChunkContext c, string suiteId, string chunkId) => new ChunkTimingUpdate
        {
            LogRoot = c.LogRoot,
            TimingRoot = c.TimingRoot,
            Suite = suiteId,
            Chunk = chunkId,
            RunId = c.InvocationId,
            Attempt = c.Attempt,
        };

        // $o.PSObject.Properties.Name -contains name.
        private static bool Has(object o, string name)
        {
            if (o == null) return false;
            PsValue.Property(o, name, out var found);
            return found;
        }

        // Test-NightlyPathExists / Test-Path -LiteralPath: a file or a directory.
        private static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private static string Stamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

        private static bool? Nullable(object value) => value == null ? (bool?)null : PsValue.IsTrue(value);

        /// <summary>A variable's value: PowerShell's empty-pipeline marker reads as null.</summary>
        internal static object Value(object value)
        {
            if (value == null) return null;
            if (PsRuntime.Loaded && PsFields.IsAutomationNull(value)) return null;
            return value;
        }

        /// <summary>$policy.Object: a PolicyFile's policy, or the policy itself.</summary>
        internal static ExecutionPolicy PolicyOf(object value)
        {
            var bare = PsValue.BaseObject(Value(value));
            if (bare is PolicyFile file) return file.Object;
            if (bare is ExecutionPolicy policy) return policy;
            return PsValue.BaseObject(PsValue.Prop(bare, "Object")) as ExecutionPolicy;
        }

        /// <summary>[datetime]$value: a DateTime as it is, a DateTimeOffset's local time, text parsed as PowerShell casts it (invariant culture).</summary>
        internal static DateTime ToDateTime(object value)
        {
            var bare = PsValue.BaseObject(Value(value));
            switch (bare)
            {
                case DateTime at: return at;
                case DateTimeOffset offset: return offset.LocalDateTime;
                case null: return default(DateTime);
                default: return DateTime.Parse(PsValue.Str(bare), CultureInfo.InvariantCulture);
            }
        }

        // An environment map as the launch's string map (a null value stays null; the native owner leaves it out).
        private static IReadOnlyDictionary<string, string> StringMap(IDictionary environment)
        {
            if (environment == null) return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry pair in environment) map[PsValue.Str(pair.Key)] = pair.Value == null ? null : PsValue.Str(pair.Value);
            return map;
        }
    }
}
