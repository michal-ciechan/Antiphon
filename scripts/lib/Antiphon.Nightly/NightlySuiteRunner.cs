// W2-nightly S13: suite preparation and completion (plan 2026-11-21-w2-nightly-port-slices.md, slice S13), moved from
// nightly-tests-impl.ps1: Prepare-NightlySuiteExecution, Complete-NightlySuiteExecution, and the chunk step that applies a
// NightlyChunkRunner outcome to the run (Invoke-NightlyScheduledChunk's wrapper, S12).
//
// The PowerShell dot-sourced these into the scope of Invoke-AntiphonNightlyTests and shared about twenty of its locals. Here
// that scope is explicit: NightlyRunState holds what the run reads (its identity, policy, roots and collaborators) and what
// the dispatch changes (the verdict flags, the reasons, the suite rows, the native counters, the outcome, the skip rows, the
// class-cost table and the dispatch schedule). NightlySuiteRunner prepares a suite into a NightlyPreparedSuite (the chunks
// and the NightlySuiteState its chunks share), runs a chunk, and completes the suite. A failure is thrown after the state it
// reached, as the dot-sourced PowerShell left its caller's variables when it threw.
//
// Collaborators that are still PowerShell (or another slice's) are delegates a plain caller may leave null: the discovery
// producer (Invoke-NightlyProduceDiscovery, S15), the E2E prerequisite seam, the executed manifest, the active-chunk identity,
// the publication scope (Get-TestPhasePublicationScope) and the warning line (Write-Warning).
// Plain C#: no PowerShell types; the records the core returns are read through PsValue. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// The run state the dispatch of Invoke-AntiphonNightlyTests shares: what it reads, and what preparation, the chunks,
    /// completion and the coordinator change. A PowerShell caller fills it from its locals and copies the changed values
    /// back; a C# caller sets the properties.
    /// </summary>
    public sealed class NightlyRunState
    {
        // ---- the run (read) ---------------------------------------------------------------------------------------

        /// <summary>The run's host (required for preparation and chunks): clock, launcher, trace.</summary>
        public NightlyHost Host { get; set; }

        public string RepoRoot { get; set; } = "";
        public string LogRoot { get; set; } = "";

        /// <summary>The qualification timing root ($TimingRoot).</summary>
        public string TimingRoot { get; set; } = "";

        /// <summary>$script:QualificationTimingRoot: the process timing root the phase files resolve against (blank: LogRoot/timing).</summary>
        public string QualificationTimingRoot { get; set; } = "";

        /// <summary>$invocationId: the run id every record names.</summary>
        public string InvocationId { get; set; } = "";

        public string Attempt { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Sha { get; set; } = "";
        public string GitRef { get; set; } = "";

        /// <summary>$policy.Object.</summary>
        public ExecutionPolicy Policy { get; set; }

        public string PolicyHash { get; set; } = "";
        public string Platform { get; set; } = "";
        public string RcEvidenceRoot { get; set; } = "";

        /// <summary>The run's start: evidence older than a second before it is stale.</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>$qualificationBuildSeconds: the prerequisite build seconds, null when unmeasured.</summary>
        public double? BuildSeconds { get; set; }

        public bool ProfileAware { get; set; }
        internal int ChunkReceiptVersion { get; set; }

        internal bool RequiresInvariantPrefix => Profile == "rc" && Platform == "windows";

        /// <summary>$requiredSuites: the profile's required suite ids (a manual suite in it still runs).</summary>
        public object[] RequiredSuites { get; set; } = new object[0];

        /// <summary>$chunksBySuite: the policy's declared chunks by suite id (a suite without any runs as one 'all' chunk).</summary>
        public IDictionary ChunksBySuite { get; set; } = new Hashtable(StringComparer.OrdinalIgnoreCase);

        /// <summary>The expected-platform-skip roster (NightlyChunkVerdict.SkipRoster's entries).</summary>
        public object ExpectedSkipRoster { get; set; } = new object[0];

        /// <summary>The same roster as the shadow chunk record reads it; null when there is none.</summary>
        public RcSkipEntry[] RcSkipRoster { get; set; }

        /// <summary>$preflight: preparation records the E2E prerequisite rows under "e2e".</summary>
        public IDictionary Preflight { get; set; } = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>$script:NightlyClassCostPath: the class-cost file the launch planner prices with.</summary>
        public string ClassCostPath { get; set; } = "";

        /// <summary>$script:NightlyClassCostTable: the table read from it (null until a planner reads it; preparation keeps what it read).</summary>
        public IDictionary ClassCostTable { get; set; }

        /// <summary>The run's shadow evidence recorder (Get-NightlyRcRunEvidence); null records nothing.</summary>
        public RcRunEvidenceRecorder RcEvidence { get; set; }

        /// <summary>ANTIPHON_TEST_PHASE_ROOT of the chunks (null: this process's, read per chunk).</summary>
        public string ExplicitPhaseRoot { get; set; }

        /// <summary>The completion grace a native chunk launch gets (NightlyChunkContext.CompletionGraceMilliseconds).</summary>
        public int CompletionGraceMilliseconds { get; set; } = NightlyCompletionGrace.DefaultMilliseconds;

        /// <summary>$npmOk, $dotnetOk and $lintBlocksPipeline: the coordinator prepares no further suite once one says no.</summary>
        public bool NpmOk { get; set; } = true;

        public bool DotnetOk { get; set; } = true;
        public bool LintBlocksPipeline { get; set; }

        // ---- the run state the dispatch changes -------------------------------------------------------------------

        public bool CoverageComplete { get; set; } = true;
        public bool TestsPassed { get; set; } = true;
        public bool OverallFailed { get; set; }
        public int ActiveNative { get; set; }
        public int NativeActiveMax { get; set; }

        /// <summary>$script:NightlyTestsOutcome.</summary>
        public string TestsOutcome { get; set; } = "green";

        public string HarnessHangReason { get; set; } = "";
        public List<object> Reasons { get; set; } = new List<object>();

        /// <summary>$suiteResults: one row per chunk, and the manual and prerequisite rows preparation adds.</summary>
        public List<object> SuiteResults { get; set; } = new List<object>();

        public List<object> ToleratedSkipRows { get; set; } = new List<object>();
        public List<object> ExpectedSkipWarnings { get; set; } = new List<object>();

        /// <summary>$dispatchSchedule: the coordinator's last schedule (the summary's dispatchProgress), or null.</summary>
        public object DispatchSchedule { get; set; }

        // ---- collaborators ----------------------------------------------------------------------------------------

        /// <summary>Invoke-NightlyProduceDiscovery: writes the discovery document and returns it; null refuses (discovery-producer-unavailable).</summary>
        public Func<NightlyDiscoveryRequest, object> ProduceDiscovery { get; set; }

        /// <summary>The E2EPrerequisites seam (repo root, log root, environment, bundle) -> { Ok, Rows }; null runs the real check.</summary>
        public Func<string, string, IDictionary, string, object> E2EPrerequisites { get; set; }

        /// <summary>The executed manifest (TestPhaseManifests.WriteExecuted into the bound writer context); null writes none.</summary>
        public Action<NightlyExecutedObservation> PublishObservation { get; set; }

        /// <summary>The active-chunk identity writer (QualificationHeartbeat.ActiveChunkWriter); null records none.</summary>
        public Action<NightlyActiveChunk> ActiveChunk { get; set; }

        /// <summary>Get-TestPhasePublicationScope, read at each use (its commit seams may change during a run); null: the timing root's plain scope.</summary>
        public Func<TestPhasePublicationScope> PublicationScope { get; set; }

        /// <summary>Write-Warning; null writes "WARNING: " lines to the host's error writer.</summary>
        public Action<string> Warning { get; set; }

        // ---- helpers ------------------------------------------------------------------------------------------------

        /// <summary>The clock's now (Get-NightlyUtcNow): the host's, else this machine's.</summary>
        public DateTime UtcNow => Host != null ? Host.UtcNow : DateTime.UtcNow;

        /// <summary>Get-TestPhaseTimingDirectory for the log root.</summary>
        public string PhaseTimingDirectory => TestPhaseManifests.TimingDirectory(LogRoot, QualificationTimingRoot ?? "");

        internal TestPhasePublicationScope Scope() =>
            PublicationScope?.Invoke() ?? new TestPhasePublicationScope(QualificationTimingRoot ?? "", "", "", null, null);

        internal void Warn(string text)
        {
            if (Warning != null) { Warning(text); return; }
            Host?.Error.WriteLine("WARNING: " + text);
        }

        /// <summary>Write-TestPhaseFailureMarker: an unwritable timing directory never changes the run's verdict.</summary>
        internal void FailureMarker(string directory, string code)
        {
            try { TestPhasePublication.WriteFailureMarker(directory, QualificationTimingRoot ?? "", code); }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// A prepared suite (Prepare-NightlySuiteExecution's result): its id, its chunks in plan order, its discovery nodes and
    /// exclusions (the scheduler and the history resolver read these), the state its chunks share, and whether it completed.
    /// </summary>
    public sealed class NightlyPreparedSuite
    {
        public NightlyPreparedSuite(NightlySuiteState state, object[] chunks)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Chunks = chunks ?? new object[0];
        }

        /// <summary>The suite's shared state (its chunks change the host, the discovery and the terminal nodes).</summary>
        public NightlySuiteState State { get; }

        public string Id => State.SuiteId;
        public object[] Chunks { get; }

        /// <summary>The suite's discovery nodes as its chunks left them.</summary>
        public object[] Nodes => State.DiscoveryNodes;

        public object Exclusions => State.ProfileExclusions;

        /// <summary>Set once the suite's last chunk ran and its union was checked.</summary>
        public bool Completed { get; set; }
    }

    /// <summary>Prepare-NightlySuiteExecution, Complete-NightlySuiteExecution and the chunk step, over a run's state.</summary>
    public sealed class NightlySuiteRunner
    {
        private static readonly Regex PhaseName = new Regex("^[a-z0-9][a-z0-9-]{0,40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TimedOut = new Regex("timed out", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public NightlySuiteRunner(NightlyRunState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public NightlyRunState State { get; }

        /// <summary>The runner's steps for NightlyDispatch.</summary>
        public NightlyDispatchSteps Steps() => new NightlyDispatchSteps
        {
            Prepare = (suiteId, needsDiscovery) => Prepare(suiteId, needsDiscovery),
            RunChunk = (suite, chunk) => RunChunk(AsPrepared(suite), chunk),
            Complete = suite => Complete(AsPrepared(suite)),
        };

        private static NightlyPreparedSuite AsPrepared(object suite) =>
            PsValue.BaseObject(suite) as NightlyPreparedSuite ?? throw new ArgumentException("A prepared suite is a NightlyPreparedSuite.", nameof(suite));

        // ---- preparation -------------------------------------------------------------------------------------------

        /// <summary>
        /// Prepare-NightlySuiteExecution: the suite's chunks (declared, else one 'all' chunk; a multi-class chunk split into
        /// exact class chunks), its test host, the E2E prerequisites, the profile discovery and launch plan when the profile
        /// excludes or the dispatch needs discovery, and the planned manifest. Null when the suite does not run: a manual
        /// suite the profile does not require, or failed E2E prerequisites (both add their suite row). A failure is thrown.
        /// </summary>
        public NightlyPreparedSuite Prepare(string suiteId, bool needsDiscovery)
        {
            var s = State;
            var host = s.Host ?? throw new InvalidOperationException("The run state needs its host.");
            var policy = s.Policy ?? throw new InvalidOperationException("The run state needs its policy.");
            object suite = null;
            if (policy.Suites != null && suiteId != null && policy.Suites.TryGetValue(suiteId, out var policySuite)) suite = policySuite;
            var profileExclusions = NightlyPolicy.ProfileExclusions(policy, s.Profile, suiteId, s.Platform).Cast<object>().ToArray();
            var label = PsValue.Str(PsValue.Prop(suite, "name"));
            if (string.IsNullOrWhiteSpace(label)) label = suiteId;
            var isNative = NightlyTestsPlan.SuiteIsNative(suite, suiteId);
            var mode = PsValue.Str(PsValue.Prop(suite, "mode"));
            // D-6: 'manual' keeps a suite out of the nightly default lane. A suite the selected profile explicitly
            // requires runs; it is never skipped as manual.
            if (PsValue.Eq(mode, "manual", true) && !PsOps.Contains(s.RequiredSuites ?? new object[0], suiteId, true))
            {
                s.SuiteResults.Add(Row(("id", suiteId), ("name", label), ("skipped", true), ("result", "manual"), ("exitCode", 0),
                    ("coverageComplete", false), ("detail", "manual exclusion")));
                return null;
            }

            object[] chunks;
            var declared = s.ChunksBySuite != null && suiteId != null && s.ChunksBySuite.Contains(suiteId) ? s.ChunksBySuite[suiteId] : null;
            if (declared != null && PsValue.AsArray(declared).Length > 0)
            {
                chunks = PsValue.AsArray(declared);
                var eligible = new List<string>();
                var eligibleClasses = PsValue.Prop(suite, "eligibleClasses");
                if (PsValue.IsTrue(eligibleClasses)) eligible.AddRange(PsValue.AsArray(eligibleClasses).Select(PsValue.Str));
                else foreach (var ch in chunks) foreach (var c in PsValue.AsArray(PsValue.Prop(ch, "classes"))) eligible.Add(PsValue.Str(c));
                var member = NightlyChunkVerdict.ChunkMembership(eligible.ToArray(), chunks);
                if (!PsValue.IsTrue(PsValue.Prop(member, "Ok")))
                {
                    var duplicates = PsValue.AsArray(PsValue.Prop(member, "Duplicates"));
                    if (duplicates.Length > 0) throw new NightlyRefusal("duplicate-class " + string.Join(",", duplicates.Select(PsValue.Str)));
                    throw new NightlyRefusal("missing-class " + string.Join(",", PsValue.AsArray(PsValue.Prop(member, "Missing")).Select(PsValue.Str)));
                }
            }
            else
            {
                chunks = new[] { PsValue.Record(("id", "all"), ("classes", new object[0])) };
            }

            // The pinned filter parser requires prefix forms for combined operands. Separate exact class chunks so no
            // suffix class can ever be launched.
            var exact = new List<object>();
            foreach (var chunk in chunks)
            {
                var classes = PsValue.AsArray(PsValue.Prop(chunk, "classes"));
                if (classes.Length > 1)
                {
                    var classIndex = 0;
                    foreach (var cls in classes)
                        exact.Add(PsValue.Record(("id", PsValue.Str(PsValue.Prop(chunk, "id")) + "-class-" + (++classIndex).ToString(CultureInfo.CurrentCulture)), ("classes", new[] { cls })));
                }
                else exact.Add(chunk);
            }
            chunks = exact.ToArray();
            var discoverySeconds = 0.0;
            NightlyTestHost suiteHost = null;
            if (isNative)
            {
                var assemblyName = PsValue.Str(PsValue.Prop(suite, "assembly"));
                if (string.IsNullOrWhiteSpace(assemblyName)) assemblyName = label;
                suiteHost = NightlyTestsPlan.TestHost(s.RepoRoot, assemblyName);
            }

            if (PsValue.Eq(suiteId, "e2e", true) && !E2EReady(host, suiteId, label)) return null;

            var discoveryNodes = new object[0];
            object profileDiscovery = null;
            var profileDiscoveryPath = "";
            object census = null;
            // CARD-1419: one discovery parse and census per suite, shared by every launch verdict.
            var index = new NightlyDiscoveryIndex();
            if (isNative && ((s.ProfileAware && profileExclusions.Length > 0) || needsDiscovery))
            {
                profileDiscoveryPath = Path.Combine(s.LogRoot, suiteId + "-profile.discovery.json");
                var profileEnv = NightlyPolicy.SafeChildEnvironment(policy, suiteId, null);
                TestPhaseEnvironment.Remove(profileEnv);
                var started = host.UtcNow;
                var watchStart = host.Clock.GetTimestamp();
                var running = true;
                var planningName = PhaseName.IsMatch(suiteId ?? "") ? "planning-" + suiteId : "planning";
                try
                {
                    var produce = s.ProduceDiscovery ?? throw new NightlyRefusal("discovery-producer-unavailable");
                    profileDiscovery = produce(new NightlyDiscoveryRequest(suiteHost.FilePath, suiteHost.Prefix, suiteHost.AssemblyPath, profileDiscoveryPath,
                        Path.Combine(s.LogRoot, suiteId + "-profile-discovery"), Path.Combine(s.LogRoot, suiteId + "-profile-discovery.log"), s.RepoRoot, profileEnv));
                    discoverySeconds = Math.Round(host.Clock.GetElapsedTime(watchStart).TotalSeconds, 3);
                    running = false;
                    NightlyChunkRunner.WriteMeasuredPhase(s.LogRoot, s.PhaseTimingDirectory, planningName, started, discoverySeconds);
                    var entry = NightlyDiscoveryIndex.EntryOf(index, profileDiscoveryPath);
                    discoveryNodes = PsValue.AsArray(PsValue.Prop(entry.Document, "Nodes"));
                    census = PsValue.Prop(NightlyDiscoveryIndex.View(entry, profileExclusions, true), "Census");
                    if (!PsValue.IsTrue(PsValue.Prop(census, "Ok"))) throw new NightlyRefusal("profile-discovery-census-incomplete");
                    // Compiled (NightlyTestsPlan.SuiteChunks); the cost table read for it is kept for the phase manifest.
                    var costs = new NightlyClassCosts(s.ClassCostPath ?? "", s.ClassCostTable);
                    chunks = NightlyTestsPlan.SuiteChunks(discoveryNodes, chunks, profileExclusions, s.ProfileAware, isNative, suiteId, policy, costs,
                        requiresInvariantPrefix: s.RequiresInvariantPrefix);
                    if (costs.Loaded) s.ClassCostTable = costs.Table;
                    // S6b shadow: one discovery per suite. Carry cannot skip it. Recording grants no gate credit.
                    s.RcEvidence?.DiscoveryOnce(s.RcEvidenceRoot, s.LogRoot, suiteId, profileDiscoveryPath);
                    if (chunks.Length == 0) throw new NightlyRefusal("zero-required-uid");
                }
                catch (Exception ex)
                {
                    if (running) discoverySeconds = Math.Round(host.Clock.GetElapsedTime(watchStart).TotalSeconds, 3);
                    NightlyChunkRunner.WriteMeasuredPhase(s.LogRoot, s.PhaseTimingDirectory, planningName, started, discoverySeconds);
                    var why = TimedOut.IsMatch(ex.Message ?? "") ? "discovery-timeout" : "discovery-failed";
                    var targets = chunks.Length < 1 ? new[] { PsValue.Record(("id", "discovery")) } : chunks;
                    foreach (var target in targets)
                    {
                        var update = Timing(suiteId, PsValue.Str(PsValue.Prop(target, "id")));
                        update.LastPhase = "discovery";
                        update.Why = why;
                        update.BuildSeconds = s.BuildSeconds;
                        update.DiscoverySeconds = discoverySeconds;
                        update.StartedAt = Stamp(host.UtcNow);
                        QualificationTiming.Update(update, host.UtcNow);
                    }
                    throw;
                }
            }
            if (isNative) WritePlannedManifest(suite, suiteId, chunks, discoveryNodes, profileExclusions, index, profileDiscoveryPath);
            var state = new NightlySuiteState
            {
                SuiteId = suiteId,
                Suite = suite,
                Label = label,
                IsNative = isNative,
                ProfileExclusions = profileExclusions,
                Host = suiteHost,
                DiscoveryNodes = discoveryNodes,
                ProfileDiscovery = profileDiscovery,
                ProfileDiscoveryPath = profileDiscoveryPath,
                DiscoveryIndex = index,
                Census = census,
                DiscoverySeconds = discoverySeconds,
            };
            return new NightlyPreparedSuite(state, chunks);
        }

        // The E2E prerequisites (D-6): a missing browser or stale bundle is red and incomplete, not a skip. False adds the
        // suite's failed row.
        private bool E2EReady(NightlyHost host, string suiteId, string label)
        {
            var s = State;
            var bundle = Path.Combine(s.RepoRoot, Path.Combine("client", Path.Combine("dist", "index.html")));
            var prereqEnv = NightlyPolicy.SafeChildEnvironment(s.Policy, suiteId, null);
            TestPhaseEnvironment.Remove(prereqEnv);
            object prereq;
            if (s.E2EPrerequisites != null)
            {
                prereq = PsValue.AsArray(s.E2EPrerequisites(s.RepoRoot, s.LogRoot, prereqEnv, bundle))[0];
            }
            else
            {
                // The generated playwright.ps1 installs Chromium under this run's owned-process custody.
                prereq = NightlyTestsPlan.E2EPrerequisites(s.RepoRoot, s.LogRoot, prereqEnv, bundle, launch =>
                {
                    var run = host.Launch(NightlyChunkRunner.OwnedLaunch(launch.FileName, launch.Arguments, launch.WorkingDirectory, launch.TimeoutMilliseconds,
                        prereqEnv, launch.LogPath));
                    return PsOps.ToInt(PsValue.Prop(run.Observed, "ExitCode"));
                });
            }
            if (s.Preflight != null) s.Preflight["e2e"] = PsValue.AsArray(PsValue.Prop(prereq, "Rows"));
            if (PsValue.IsTrue(PsValue.Prop(prereq, "Ok"))) return true;
            s.CoverageComplete = false;
            s.TestsPassed = false;
            s.OverallFailed = true;
            s.TestsOutcome = NightlyTestsPlan.Outcome(s.TestsOutcome, "PREFLIGHT");
            s.Reasons.Add("e2e-prerequisite-failed");
            s.SuiteResults.Add(Row(("id", suiteId), ("name", label), ("skipped", false), ("result", "FAIL"), ("exitCode", 1),
                ("coverageComplete", false), ("detail", "e2e-prerequisite-failed")));
            var update = Timing(suiteId, "prereq");
            update.LastPhase = "preflight";
            update.Why = "e2e-prerequisite-failed";
            update.BuildSeconds = s.BuildSeconds;
            update.StartedAt = Stamp(host.UtcNow);
            QualificationTiming.Update(update, host.UtcNow);
            return false;
        }

        // The planned manifest (TestPhaseManifests.WritePlanned), priced with the run's cost path and table; a failure is
        // the phase-summary-failed marker, never the run's verdict.
        private void WritePlannedManifest(object suite, string suiteId, object[] chunks, object[] discoveryNodes, object[] exclusions,
            NightlyDiscoveryIndex index, string discoveryPath)
        {
            var s = State;
            try
            {
                var phaseNodes = PsValue.IsTrue(discoveryNodes) ? discoveryNodes : new object[0];
                var assembly = PsValue.Str(PsValue.Prop(suite, "assembly"));
                var assemblyDigest = "";
                var dll = Path.Combine(s.RepoRoot, "tests", assembly, "bin", "Debug", "net9.0", assembly + ".dll");
                if (!string.IsNullOrWhiteSpace(assembly) && Exists(dll))
                {
                    if (!EvidenceFiles.PathExists(dll)) throw new NightlyRefusal("missing file for hash " + dll);
                    assemblyDigest = NightlyClassNames.CachedFileSha256(dll);
                }
                var costDigest = "";
                if (!string.IsNullOrWhiteSpace(s.ClassCostPath) && Exists(s.ClassCostPath)) costDigest = EvidenceFiles.Sha256(s.ClassCostPath);
                var plan = new TestPhasePlannedRequest
                {
                    LogRoot = s.LogRoot,
                    RunId = s.InvocationId ?? "",
                    Attempt = s.Attempt ?? "",
                    Suite = suiteId ?? "",
                    SourceDigest = s.Sha ?? "",
                    AssemblyDigest = assemblyDigest,
                    PolicyDigest = s.PolicyHash ?? "",
                    CostFileDigest = costDigest,
                    AssemblyId = assembly,
                    Groups = chunks,
                    DiscoveryNodes = phaseNodes,
                    ProfileExclusions = exclusions,
                    DiscoveryIndex = index,
                    DiscoveryPath = discoveryPath ?? "",
                    ClassCostPath = s.ClassCostPath ?? "",
                    ClassCostTable = s.ClassCostTable,
                };
                TestPhaseManifests.WritePlanned(plan, s.Scope());
            }
            catch (Exception)
            {
                s.FailureMarker(s.LogRoot, "phase-summary-failed");
            }
        }

        // ---- chunks and completion -----------------------------------------------------------------------------------

        /// <summary>
        /// One chunk of a prepared suite (NightlyChunkRunner.Run) on the run's state: the run state goes in, the outcome
        /// comes back into it with the chunk's suite row, then the chunk's failure, if any, is thrown.
        /// </summary>
        public void RunChunk(NightlyPreparedSuite suite, object chunk)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            var s = State;
            var produce = s.ProduceDiscovery;
            var context = new NightlyChunkContext
            {
                Host = s.Host,
                RepoRoot = s.RepoRoot,
                LogRoot = s.LogRoot,
                TimingRoot = s.TimingRoot,
                PhaseTimingDirectory = s.PhaseTimingDirectory,
                InvocationId = s.InvocationId,
                Attempt = s.Attempt,
                Profile = s.Profile,
                Sha = s.Sha,
                GitRef = s.GitRef,
                Policy = s.Policy,
                PolicyHash = s.PolicyHash,
                Platform = s.Platform,
                RcEvidenceRoot = s.RcEvidenceRoot,
                StartedAt = s.StartedAt,
                BuildSeconds = s.BuildSeconds,
                ProfileAware = s.ProfileAware,
                ChunkReceiptVersion = s.ChunkReceiptVersion,
                ExpectedSkipRoster = s.ExpectedSkipRoster,
                RcSkipRoster = s.RcSkipRoster,
                ExplicitPhaseRoot = s.ExplicitPhaseRoot ?? Environment.GetEnvironmentVariable("ANTIPHON_TEST_PHASE_ROOT") ?? "",
                CompletionGraceMilliseconds = s.CompletionGraceMilliseconds,
                RcEvidence = s.RcEvidence,
                ActiveNative = s.ActiveNative,
                NativeActiveMax = s.NativeActiveMax,
                CoverageComplete = s.CoverageComplete,
                TestsPassed = s.TestsPassed,
                OverallFailed = s.OverallFailed,
                Reasons = s.Reasons.ToArray(),
                TestsOutcome = s.TestsOutcome,
                ToleratedSkipRows = s.ToleratedSkipRows.ToArray(),
                ExpectedSkipWarnings = s.ExpectedSkipWarnings.ToArray(),
                HarnessHangReason = s.HarnessHangReason ?? "",
                PublishObservation = s.PublishObservation,
                ActiveChunk = s.ActiveChunk,
                ProduceDiscovery = produce == null ? null : (Action<NightlyDiscoveryRequest>)(request => produce(request)),
            };
            var outcome = NightlyChunkRunner.Run(context, suite.State, chunk);
            s.ActiveNative = outcome.ActiveNative;
            s.NativeActiveMax = outcome.NativeActiveMax;
            s.CoverageComplete = outcome.CoverageComplete;
            s.TestsPassed = outcome.TestsPassed;
            s.OverallFailed = outcome.OverallFailed;
            s.Reasons = new List<object>(outcome.Reasons);
            s.ToleratedSkipRows = new List<object>(outcome.ToleratedSkipRows);
            s.ExpectedSkipWarnings = new List<object>(outcome.ExpectedSkipWarnings);
            s.HarnessHangReason = outcome.HarnessHangReason;
            s.TestsOutcome = outcome.TestsOutcome;
            if (outcome.Row != null) s.SuiteResults.Add(outcome.Row);
            if (outcome.Error != null) ExceptionDispatchInfo.Capture(outcome.Error).Throw();
        }

        /// <summary>
        /// Complete-NightlySuiteExecution: a native suite's terminal rows must cover its discovery (less the profile's
        /// exclusions), else the run is incomplete; the expected-skip roster entries the discovery no longer knows are
        /// warnings. The suite is then Completed.
        /// </summary>
        public void Complete(NightlyPreparedSuite suite)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            var s = State;
            var st = suite.State;
            if (st.IsNative && st.DiscoveryNodes.Length > 0)
            {
                var unionNodes = new List<object>();
                if (st.ProfileDiscovery != null && !string.IsNullOrWhiteSpace(st.ProfileDiscoveryPath))
                {
                    var entry = NightlyDiscoveryIndex.EntryOf(st.DiscoveryIndex, st.ProfileDiscoveryPath);
                    var view = NightlyDiscoveryIndex.View(entry, st.ProfileExclusions, s.ProfileAware);
                    // The view's nodes that are not excluded, in discovery order (SuiteCompletionParts.IncludedNodes).
                    unionNodes.AddRange(SuiteCompletionParts.IncludedNodes(PsValue.AsArray(PsValue.Prop(view, "Nodes")), Flags(PsValue.Prop(view, "Excluded"))));
                }
                else
                {
                    foreach (var node in st.DiscoveryNodes)
                    {
                        if (NightlyCensus.Excluded(node, st.ProfileExclusions, s.ProfileAware)) continue;
                        unionNodes.Add(node);
                    }
                }
                var union = NightlyChunkVerdict.SuiteUidUnion(unionNodes.ToArray(), st.TerminalNodes);
                if (!PsValue.IsTrue(PsValue.Prop(union, "Ok")))
                {
                    s.CoverageComplete = false;
                    s.OverallFailed = true;
                    s.Reasons.Add("missing-expanded-row union " + string.Join(",", PsValue.AsArray(PsValue.Prop(union, "Missing")).Select(PsValue.Str)));
                }
            }
            var roster = s.ExpectedSkipRoster == null ? new object[0] : PsValue.AsArray(s.ExpectedSkipRoster);
            if (st.IsNative && st.ProfileDiscovery != null && !string.IsNullOrWhiteSpace(st.ProfileDiscoveryPath) && st.DiscoveryNodes.Length > 0 && roster.Length > 0)
            {
                var known = SuiteCompletionParts.KnownIdentities(st.DiscoveryNodes);
                foreach (var stale in NightlyChunkVerdict.StaleWarnings(s.Platform ?? "", s.ExpectedSkipRoster, known))
                {
                    var text = PsValue.Str(stale);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (!PsOps.Contains(s.ExpectedSkipWarnings.ToArray(), text, true)) s.ExpectedSkipWarnings.Add(text);
                }
            }
            suite.Completed = true;
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private ChunkTimingUpdate Timing(string suiteId, string chunkId) => new ChunkTimingUpdate
        {
            LogRoot = State.LogRoot,
            TimingRoot = State.TimingRoot,
            Suite = suiteId ?? "",
            Chunk = chunkId ?? "",
            RunId = State.InvocationId ?? "",
            Attempt = State.Attempt ?? "",
        };

        // [ordered]@{ ... }: the suite row as PowerShell built it.
        private static OrderedDictionary Row(params (string Name, object Value)[] fields)
        {
            var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in fields) row[name] = value;
            return row;
        }

        // The view's excluded flags as the bool[] IncludedNodes reads.
        private static bool[] Flags(object value)
        {
            if (PsValue.BaseObject(value) is bool[] flags) return flags;
            if (value == null) return null;
            return PsValue.AsArray(value).Select(PsValue.IsTrue).ToArray();
        }

        // Test-Path -LiteralPath: a file or a directory.
        private static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private static string Stamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);
    }
}
