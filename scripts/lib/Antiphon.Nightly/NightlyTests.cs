// W2-nightly S15: the nightly tests run (plan 2026-11-21-w2-nightly-port-slices.md, slice S15), moved from
// scripts/lib/nightly-tests-impl.ps1: Invoke-AntiphonNightlyTests (the selection hop check, the shared-tree guard, the
// policy, profile and suite resolution, the preflight and the prerequisite builds, the dispatch over NightlyRunState, the
// end-of-run skip scans, the publication and the summary), Write-NightlyRunSummary, the toolchain preflight
// (Get-NightlyToolchainPreflight) and Invoke-NightlyExactRoster. CARD-0124 / CARD-0487: it never clones, fetches, resets
// or otherwise changes git state.
//
// W2-nightly S17: this is the nightly tests run (antiphon-scripts nightly-tests, NightlyTestsCommand); scripts/nightly-tests.ps1
// and nightly-tests-impl.ps1 are deleted. Every observation of the machine goes through the run's NightlyHost (clock, git,
// launcher, probe, environment, writers); the collaborators that are not observations (the evidence recorder, the
// active-chunk identity, the E2E prerequisite observation, the class-cost file) are NightlyTestsBindings: the command binds
// NightlyTestsBindings.Production, a test its own.
// Plain C#: no PowerShell types; the records the core returns are read through PsValue. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// What a nightly tests run answers (Invoke-AntiphonNightlyTests' result object). A refusal before the run sets
    /// ExitCode, Refusal or Error and leaves the rest at their defaults, as the PowerShell's early results left them out.
    /// </summary>
    public sealed class NightlyTestsResult
    {
        /// <summary>0 green or WhatIf; 1 when the run failed; 2 for a policy, profile, sha, project or suite refusal; 3 for a refusal.</summary>
        public int ExitCode { get; set; }

        /// <summary>The refusal reason (exit 3): unsupported-host-platform, shared-tree, or a selection hop refusal; "" otherwise.</summary>
        public string Refusal { get; set; } = "";

        /// <summary>The exit-2 refusal's message; "" otherwise.</summary>
        public string Error { get; set; } = "";

        /// <summary>The first unmatched project of an exit-2 project universe refusal; "" otherwise.</summary>
        public string UnmatchedProject { get; set; } = "";

        public bool CoverageComplete { get; set; }
        public bool TestsPassed { get; set; }
        public string Platform { get; set; } = "";
        public string CoverageScope { get; set; } = "";
        public string[] Reasons { get; set; } = new string[0];
        public string Profile { get; set; } = "";
        public string[] RequiredSuites { get; set; } = new string[0];

        /// <summary>The selection attempt document read for the hop (null without one).</summary>
        public object Selection { get; set; }

        public string SummaryPath { get; set; } = "";
        public string[] Selected { get; set; } = new string[0];
        public int NativeActiveMax { get; set; }
        public string PolicyHash { get; set; } = "";

        /// <summary>0 on the refusals the PowerShell reported a launch count for; null otherwise.</summary>
        public int? LaunchCount { get; set; }

        /// <summary>The nightly-tests command's process exit (scripts/nightly-tests.ps1's before W2-nightly S17): 3 on a refusal, else ExitCode.</summary>
        public int ProcessExitCode => string.IsNullOrEmpty(Refusal) ? ExitCode : 3;
    }

    /// <summary>
    /// The collaborators of a run that are not observations of its host. Each may be left null: the run then records
    /// what production records (the evidence recorder from ANTIPHON_RC_RUN_EVIDENCE) or nothing (the active chunk).
    /// </summary>
    public sealed class NightlyTestsBindings
    {
        /// <summary>The run's shadow evidence recorder (Get-NightlyRcRunEvidence); null: one from ANTIPHON_RC_RUN_EVIDENCE, unseamed.</summary>
        public RcRunEvidenceRecorder RcEvidence { get; set; }

        /// <summary>The E2EPrerequisites observation (repo root, log root, environment, bundle) -> { Ok, Rows }; null runs the real check.</summary>
        public Func<string, string, IDictionary, string, object> E2EPrerequisites { get; set; }

        /// <summary>Write-QualificationActiveChunkIdentity (the heartbeat, W2-nightly S10); null records none.</summary>
        public Action<NightlyActiveChunk> ActiveChunk { get; set; }

        /// <summary>The class-cost file the launch planner prices with; null: scripts/lib/nightly-class-costs.json of the product root.</summary>
        public string ClassCostPath { get; set; }

        /// <summary>
        /// W2-nightly S17: the publication scope of the run's timing root, read at each use (Get-TestPhasePublicationScope:
        /// a test's commit barrier and commit fault stand in for the journal's file system); null: the command's scope.
        /// </summary>
        public Func<string, TestPhasePublicationScope> PublicationScope { get; set; }

        /// <summary>W2-nightly S17: observes the bound writer context once it is open ($script:TestPhaseExecutedContext); null: none.</summary>
        public Action<TestPhaseJournalContext> ExecutedContext { get; set; }

        /// <summary>
        /// W2-nightly S17: the bindings of the nightly-tests command, as scripts/lib/nightly-tests-impl.ps1 bound them for a
        /// run without seams (Set-NightlyRunStateBindings): the shadow evidence recorder from ANTIPHON_RC_RUN_EVIDENCE
        /// (unseamed, its refusal lines on the run's output), the real E2E prerequisite check (no observation bound), the
        /// heartbeat's active-chunk identity (QualificationHeartbeat.ActiveChunkWriter, its warnings on the run's error) and
        /// the product root's class-cost file (the entry script's own scripts/lib/nightly-class-costs.json).
        /// </summary>
        public static NightlyTestsBindings Production(NightlyHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return new NightlyTestsBindings
            {
                RcEvidence = new RcRunEvidenceRecorder(host.Variables("ANTIPHON_RC_RUN_EVIDENCE"), false, false, line => NightlyTests.RunLine(host, line)),
                E2EPrerequisites = null,
                ActiveChunk = QualificationHeartbeat.ActiveChunkWriter(text => NightlyTests.Warn(host, text)),
                ClassCostPath = NightlyTests.DefaultClassCostPath(),
            };
        }
    }

    /// <summary>The nightly tests run and its helpers.</summary>
    public static class NightlyTests
    {
        /// <summary>The log root's parent when none is given.</summary>
        public const string DefaultLogParent = @"C:\Antiphon\nightly\logs";

        private const string DotnetBuildCommand = "dotnet build Antiphon.sln -c Debug --nologo";

        /// <summary>
        /// The checkout this code was built from: the nearest directory above AppContext.BaseDirectory holding
        /// scripts/lib/nightly-class-costs.json (Invoke-AntiphonNightlyTests' default RepoRoot, its own script's checkout);
        /// null when there is none.
        /// </summary>
        public static string ProductRoot()
        {
            var cursor = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(cursor))
            {
                if (File.Exists(Path.Combine(cursor, "scripts", "lib", "nightly-class-costs.json"))) return Path.GetFullPath(cursor);
                var parent = Path.GetDirectoryName(cursor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent == null || parent == cursor) break;
                cursor = parent;
            }
            return null;
        }

        /// <summary>
        /// Invoke-AntiphonNightlyTests: one nightly tests run in options.RepoRoot (an isolated clone; the shared tree is
        /// refused without AllowSharedTree), writing its evidence and summary.json under the log root. The host answers every
        /// observation; a run failure is the result (ExitCode 1), a refusal before the run is ExitCode 2 or 3. A summary that
        /// cannot be written throws.
        /// </summary>
        public static NightlyTestsResult Run(NightlyTestsOptions options, NightlyHost host, NightlyTestsBindings bindings = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            options = options ?? new NightlyTestsOptions();
            bindings = bindings ?? new NightlyTestsBindings();
            var output = host.Out;
            var error = host.Error;

            // The probe replaces only the OS observation (NightlyHostProbe.Platform), never the decision.
            string platform;
            try { platform = NightlyTestsPlan.ExecutionPlatform(host.Probe.Platform()); }
            catch (NightlyRefusal) { return new NightlyTestsResult { ExitCode = 3, Refusal = "unsupported-host-platform", LaunchCount = 0 }; }
            var profile = string.IsNullOrWhiteSpace(options.Profile) ? "nightly" : options.Profile;
            profile = profile.Trim().ToLowerInvariant();
            var repoRoot = string.IsNullOrWhiteSpace(options.RepoRoot) ? ProductRoot() : options.RepoRoot;
            if (string.IsNullOrWhiteSpace(repoRoot)) return new NightlyTestsResult { ExitCode = 2, Error = "repo-root-unresolved" };
            repoRoot = NightlyPaths.Canonical(repoRoot);
            var runId = string.IsNullOrWhiteSpace(options.RunId) ? Guid.NewGuid().ToString("N") : options.RunId;
            var sha = options.Sha ?? "";
            var attempt = options.Attempt.ToString(CultureInfo.InvariantCulture);

            object selection = null;
            NightlyRunOptions incremental;
            try
            {
                if (!string.IsNullOrEmpty(options.SelectionAttemptPath))
                {
                    selection = ReleaseSelectionAttempt.Read(new ReleaseSelectionRequest
                    {
                        RepositoryRoot = repoRoot, CandidateSha = sha, CandidateRef = options.GitRef, Repository = options.Repository, Profile = profile,
                        ReleaseRoot = options.ReleaseRoot, CandidateId = options.CandidateId, RunId = runId, SelectionMode = options.SelectionMode,
                        BaselineSha = options.BaselineSha, BaselineReceiptPath = options.BaselineReceiptPath, QualificationPath = options.QualificationPath,
                        TimingRoot = options.TimingRoot, TrainManifestPath = options.TrainManifestPath, ReleaseIntentPath = options.ReleaseIntentPath,
                        SelectionAttemptPath = options.SelectionAttemptPath, SelectionAttemptDigest = options.SelectionAttemptDigest,
                        SelectionPath = options.SelectionPath, SelectionDigest = options.SelectionDigest, Attempt = attempt,
                    });
                    // Independently read HEAD at the receiving implementation, not just argv.
                    var head = host.RunGit(repoRoot, "rev-parse", "HEAD");
                    if (head.ExitCode != 0 || !string.Equals((head.Output ?? "").Trim(), sha, StringComparison.Ordinal)) throw new NightlyRefusal("selection-hop-head");
                }
                else if (!string.Equals(options.SelectionMode, "Full", StringComparison.Ordinal)) throw new NightlyRefusal("selection-attempt-required");
                incremental = RcIncrementalCut.BindTests(options, repoRoot, sha, profile, platform, host.UtcNow);
            }
            catch (Exception ex) { return new NightlyTestsResult { ExitCode = 3, Refusal = ex.Message, LaunchCount = 0 }; }

            var startedAt = host.UtcNow;
            var outcome = "green";
            var overallFailed = false;
            var coverageComplete = true;
            var testsPassed = true;
            var reasons = new List<object>();
            var buildResults = new List<object>();
            var preflight = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            var suiteResults = new List<object>();
            var expectedSkipWarnings = new List<object>();
            var toleratedSkipRows = new List<object>();
            object[] expectedSkipRoster = new object[0];
            ExpectedSkipScan expectedSkipScan = null;
            var harnessHangReason = "";
            var nativeActiveMax = 0;
            object dispatchSchedule = null;

            if (NightlyPaths.IsSharedTree(repoRoot) && !options.AllowSharedTree)
            {
                output.WriteLine("REFUSED: nightly-tests will not run in the shared tree.");
                output.WriteLine("  RepoRoot: " + repoRoot);
                output.WriteLine("  Isolated clone: " + NightlyPaths.IsolatedClone);
                output.WriteLine("  Re-run with --allow-shared-tree only for a deliberate shared-tree run.");
                return new NightlyTestsResult { ExitCode = 3, Refusal = "shared-tree" };
            }

            PolicyFile policy;
            try { policy = NightlyPolicy.Read(repoRoot, options.PolicyPath); }
            catch (Exception ex)
            {
                error.WriteLine(ex.Message);
                return new NightlyTestsResult { ExitCode = 2, Error = ex.Message };
            }
            var policyHash = policy.Hash;
            // D-5: the selected profile is authoritative for required suites; defaultSuites stays the compatible nightly
            // answer. An unknown profile, or rc asked of a v1 policy, is a hard refusal rather than a silent fall back to the
            // nightly seven.
            string[] requiredSuites;
            try
            {
                NightlyPolicy.Profile(policy.Object, profile);
                requiredSuites = NightlyPolicy.ProfileRequiredSuites(policy.Object, profile);
                // Validate every exclusion before any child command; each suite resolves its own rows below so same-named
                // classes in other suites stay required.
                NightlyPolicy.ProfileExclusions(policy.Object, profile, "", platform);
            }
            catch (Exception ex)
            {
                error.WriteLine(ex.Message);
                return new NightlyTestsResult { ExitCode = 2, Error = ex.Message, Profile = profile };
            }
            var profileAware = !string.Equals(profile, "nightly", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(options.ExpectedSha) && !string.IsNullOrWhiteSpace(sha) && !string.Equals(sha, options.ExpectedSha, StringComparison.OrdinalIgnoreCase))
            {
                var message = "expected-sha-mismatch sha=" + sha + " expected=" + options.ExpectedSha;
                error.WriteLine(message);
                return new NightlyTestsResult { ExitCode = 2, Error = message, Profile = profile };
            }
            var universe = NightlyPolicy.TestProjectUniverse(repoRoot, policy.Object);
            if (!universe.Ok)
            {
                error.WriteLine(universe.Message);
                return new NightlyTestsResult { ExitCode = 2, Error = universe.Message, UnmatchedProject = universe.Unmatched.FirstOrDefault() ?? "" };
            }

            string[] selectedSuites;
            try
            {
                if (options.Suites == null || options.Suites.Length == 0)
                {
                    selectedSuites = requiredSuites;
                    if (selection != null)
                        selectedSuites = ReleaseSelectionAttempt.ResolveMembers(selection, requiredSuites.Cast<object>().ToArray(), new object[0]).Select(PsValue.Str).ToArray();
                }
                else
                {
                    var joined = string.Join(",", options.Suites.Select(s => s ?? ""));
                    if (Regex.IsMatch(joined, @"^[\s,]*$")) throw new NightlyRefusal("empty suite selection");
                    selectedSuites = NightlyPolicy.ResolveSelectedSuites(policy.Object, options.Suites);
                }
            }
            catch (Exception ex)
            {
                error.WriteLine(ex.Message);
                return new NightlyTestsResult { ExitCode = 2, Error = ex.Message, LaunchCount = 0 };
            }

            var suiteUniverse = NightlyPolicy.RequiredSuitesPresent(selectedSuites, requiredSuites);
            if (!suiteUniverse.Ok)
            {
                coverageComplete = false;
                reasons.Add("partial-selection missing " + string.Join(",", suiteUniverse.Missing));
            }

            if (options.WhatIf)
            {
                output.WriteLine("WhatIf: would run suites " + string.Join(",", selectedSuites) + " in " + repoRoot);
                return new NightlyTestsResult { ExitCode = 0, Selected = selectedSuites };
            }

            var logRoot = options.LogRoot;
            if (string.IsNullOrWhiteSpace(logRoot)) logRoot = Path.Combine(DefaultLogParent, startedAt.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture));
            logRoot = NightlyPaths.Canonical(logRoot);
            Directory.CreateDirectory(logRoot);
            var dispatchInput = PriorFailureHistory.InitializeDispatch(options.PriorFailuresPath, options.PriorFailuresDigest, logRoot, sha,
                options.AcceptCrossCandidateSeed, options.SeedSourceCandidateId, host.UtcNow);
            var dispatchWarning = PsValue.Str(PsValue.Prop(dispatchInput, "Warning"));
            if (!string.IsNullOrEmpty(dispatchWarning)) Warn(host, dispatchWarning);
            var summaryPath = Path.Combine(logRoot, "summary.json");
            var invocationId = runId;
            var timingRoot = NightlyPaths.TimingRoot(options.TimingRoot, logRoot, runId);
            TestPhasePublicationScope Scope() => bindings.PublicationScope?.Invoke(timingRoot) ?? PublicationScope(timingRoot);
            var phaseTimingDirectory = TestPhaseManifests.TimingDirectory(logRoot, timingRoot);
            TestPhaseJournalContext publicationContext = null;
            TestPhaseJournalContext executedContext = null;
            var publicationSource = string.IsNullOrWhiteSpace(sha) ? "unknown" : sha;
            try
            {
                publicationContext = TestPhasePublication.Open(logRoot, timingRoot, invocationId, attempt, "", publicationSource, "", false, false, Scope());
                if (!publicationContext.Refused) executedContext = publicationContext;
                if (executedContext != null) bindings.ExecutedContext?.Invoke(executedContext);
            }
            catch (Exception) { FailureMarker(logRoot, timingRoot, "phase-summary-failed"); }
            var rcEvidence = bindings.RcEvidence ?? new RcRunEvidenceRecorder(host.Variables("ANTIPHON_RC_RUN_EVIDENCE"), false, false, line => RunLine(host, line));
            try
            {
                // A4 host sampling is not wired. Do not load a probe DLL from the build output.
                double? qualificationBuildSeconds = null;
                var chunksBySuite = new Hashtable(StringComparer.OrdinalIgnoreCase);
                if (policy.Object.Chunks != null)
                    foreach (var pair in policy.Object.Chunks) chunksBySuite[pair.Key] = (pair.Value ?? new PolicyChunk[0]).Cast<object>().ToArray();

                var dockerOk = true;
                var diskOk = true;
                var npmOk = true;
                var dotnetOk = true;
                var lintOk = true;
                var lintBlocksPipeline = false;
                var activeNative = 0;
                var coverageScope = incremental == null ? "Full" : "Incremental";
                try
                {
                    var docker = host.Probe.Docker() ?? new NightlyProbeRow { ExitCode = 1 };
                    preflight["docker"] = new OrderedDictionary { { "exitCode", docker.ExitCode }, { "version", docker.Version ?? "" }, { "detail", docker.Detail ?? "" } };
                    if (docker.ExitCode != 0) { dockerOk = false; overallFailed = true; outcome = NightlyTestsPlan.Outcome(outcome, "PREFLIGHT"); coverageComplete = false; }
                    var disk = host.Probe.DiskFree() ?? new NightlyProbeRow { ExitCode = 1 };
                    preflight["disk"] = new OrderedDictionary { { "exitCode", disk.ExitCode }, { "freeBytes", disk.FreeBytes }, { "minimumFreeBytes", disk.MinimumFreeBytes } };
                    // CARD-1698: record the toolchain versions and floor warnings; a warning never fails a run.
                    preflight["toolchain"] = ToolchainPreflight(repoRoot, host);
                    if (disk.ExitCode != 0) { diskOk = false; overallFailed = true; outcome = NightlyTestsPlan.Outcome(outcome, "PREFLIGHT"); coverageComplete = false; }

                    if (dockerOk && diskOk)
                    {
                        var buildWatch = host.Clock.GetTimestamp();
                        object npmCiStarted = null, npmCiSeconds = null, npmBuildStarted = null, npmBuildSeconds = null;
                        object lintStarted = null, lintSeconds = null, dotnetStarted = null, dotnetSeconds = null;
                        var clientDir = Path.Combine(repoRoot, "client");
                        var pathValue = host.Variables("PATH");
                        var comSpec = host.Variables("ComSpec");

                        var npmCiLaunch = NightlyTestsPlan.NpmLaunch(new[] { "ci" }, pathValue, comSpec);
                        npmCiStarted = host.UtcNow;
                        var watch = host.Clock.GetTimestamp();
                        var npmCi = OwnedProcess(host, npmCiLaunch.FilePath, npmCiLaunch.ArgumentList, clientDir, 600000, null, Path.Combine(logRoot, "npm-ci.log"));
                        npmCiSeconds = Seconds(host, watch);
                        buildResults.Add(BuildRow("npm ci", npmCi));
                        if (npmCi.ExitCode != 0)
                        {
                            npmOk = false; overallFailed = true; testsPassed = false; outcome = NightlyTestsPlan.Outcome(outcome, "BUILD"); coverageComplete = false;
                            reasons.Add("prerequisite-failed:npm ci");
                        }

                        if (npmOk)
                        {
                            var npmBuildLaunch = NightlyTestsPlan.NpmLaunch(new[] { "run", "build" }, pathValue, comSpec);
                            npmBuildStarted = host.UtcNow;
                            watch = host.Clock.GetTimestamp();
                            var npmBuild = OwnedProcess(host, npmBuildLaunch.FilePath, npmBuildLaunch.ArgumentList, clientDir, 600000, null, Path.Combine(logRoot, "npm-build.log"));
                            npmBuildSeconds = Seconds(host, watch);
                            buildResults.Add(BuildRow("npm run build", npmBuild));
                            if (npmBuild.ExitCode != 0)
                            {
                                npmOk = false; overallFailed = true; testsPassed = false; outcome = NightlyTestsPlan.Outcome(outcome, "BUILD"); coverageComplete = false;
                                reasons.Add("prerequisite-failed:npm run build");
                            }
                            else
                            {
                                var dist = Path.Combine(clientDir, "dist", "index.html");
                                WriteAtomicJson(Path.Combine(logRoot, "client-build-receipt.json"), new OrderedDictionary
                                {
                                    { "sha", sha }, { "runId", invocationId }, { "dist", dist }, { "builtAt", Stamp(host.UtcNow) },
                                });
                            }
                        }

                        if (npmOk)
                        {
                            var lintLaunch = NightlyTestsPlan.NpmLaunch(new[] { "run", "lint" }, pathValue, comSpec);
                            lintStarted = host.UtcNow;
                            watch = host.Clock.GetTimestamp();
                            var lint = OwnedProcess(host, lintLaunch.FilePath, lintLaunch.ArgumentList, clientDir, 600000, null, Path.Combine(logRoot, "client-lint.log"));
                            lintSeconds = Seconds(host, watch);
                            buildResults.Add(BuildRow("client lint", lint));
                            if (lint.ExitCode != 0)
                            {
                                lintOk = false;
                                overallFailed = true;
                                // Profile nightly records the lint failure and still runs dotnet and the suites. Every other
                                // profile, including rc, stops here. Do not mark testsPassed yet on nightly: a later suite
                                // failure must be able to set TESTS, and a green suite stays BUILD.
                                if (string.Equals(profile, "nightly", StringComparison.OrdinalIgnoreCase)) reasons.Add("client-lint-failed");
                                else
                                {
                                    lintBlocksPipeline = true; testsPassed = false; outcome = NightlyTestsPlan.Outcome(outcome, "BUILD"); coverageComplete = false;
                                    reasons.Add("prerequisite-failed:client lint");
                                }
                            }
                        }

                        if (npmOk && !lintBlocksPipeline)
                        {
                            IDictionary environment = null;
                            if (TestPhaseEnvironment.VariableNames.Any(name => !string.IsNullOrWhiteSpace(host.Variables(name))))
                            {
                                environment = NightlyPolicy.SafeChildEnvironment(policy.Object, "antiphon", null);
                                TestPhaseEnvironment.Remove(environment);
                            }
                            dotnetStarted = host.UtcNow;
                            watch = host.Clock.GetTimestamp();
                            var dotnetBuild = OwnedProcess(host, "dotnet", new[] { "build", "Antiphon.sln", "-c", "Debug", "--nologo" }, repoRoot, 1200000, environment,
                                Path.Combine(logRoot, "dotnet-build.log"));
                            dotnetSeconds = Seconds(host, watch);
                            buildResults.Add(BuildRow("dotnet build Antiphon.sln", dotnetBuild));
                            if (dotnetBuild.ExitCode != 0)
                            {
                                dotnetOk = false; overallFailed = true; testsPassed = false; outcome = NightlyTestsPlan.Outcome(outcome, "BUILD"); coverageComplete = false;
                                reasons.Add("prerequisite-failed:dotnet build");
                            }
                        }
                        qualificationBuildSeconds = Seconds(host, buildWatch);
                        NightlyChunkRunner.WritePrerequisitePhases(logRoot, phaseTimingDirectory, npmCiStarted, npmCiSeconds, npmBuildStarted, npmBuildSeconds,
                            lintStarted, lintSeconds, dotnetStarted, dotnetSeconds);

                        var suiteMap = NightlyPolicy.SuiteMap(policy.Object);
                        if (dotnetOk)
                        {
                            foreach (var evidenceSuiteId in selectedSuites)
                            {
                                var assembly = suiteMap.TryGetValue(evidenceSuiteId, out var evidenceSuite) ? evidenceSuite?.Assembly ?? "" : "";
                                if (string.IsNullOrWhiteSpace(assembly)) continue;
                                var outputDirectory = Path.Combine(repoRoot, "tests", assembly, "bin", "Debug", "net9.0");
                                rcEvidence.Build(options.RcEvidenceRoot, logRoot, evidenceSuiteId, DotnetBuildCommand, outputDirectory, assembly + ".dll", "", repoRoot, sha);
                            }
                        }
                        RcSkipEntry[] rcSkipRoster = new RcSkipEntry[0];
                        var rosterFile = Path.Combine(repoRoot, "tests", "expected-platform-skips.json");
                        if (File.Exists(rosterFile))
                        {
                            expectedSkipRoster = NightlyChunkVerdict.SkipRoster(rosterFile) ?? new object[0];
                            rcSkipRoster = expectedSkipRoster.Select(e => new RcSkipEntry(PsValue.Str(PsValue.Prop(e, "test")), PsValue.Str(PsValue.Prop(e, "platform")),
                                PsValue.Str(PsValue.Prop(e, "reasonPrefix")), PsValue.Str(PsValue.Prop(e, "reasonClass")))).ToArray();
                            // The end-of-run skip scans read only tests/ and the roster: they run while the chunks do.
                            expectedSkipScan = NightlyExpectedSkips.Start(repoRoot, platform, expectedSkipRoster);
                        }

                        // W2-nightly S13: the dispatch runs over this run's state; what it changed comes back, also when it fails.
                        var state = new NightlyRunState
                        {
                            Host = host,
                            RcEvidence = rcEvidence,
                            QualificationTimingRoot = timingRoot,
                            RepoRoot = repoRoot,
                            LogRoot = logRoot,
                            TimingRoot = timingRoot,
                            InvocationId = invocationId,
                            Attempt = attempt,
                            Profile = profile,
                            Sha = sha,
                            GitRef = options.GitRef ?? "",
                            Policy = policy.Object,
                            PolicyHash = policyHash,
                            Platform = platform,
                            RcEvidenceRoot = options.RcEvidenceRoot ?? "",
                            StartedAt = startedAt,
                            BuildSeconds = qualificationBuildSeconds,
                            ProfileAware = profileAware,
                            ChunkReceiptVersion = incremental == null ? 0 : RcChunkReceipt.Version,
                            RequiredSuites = requiredSuites.Cast<object>().ToArray(),
                            ChunksBySuite = chunksBySuite,
                            Preflight = preflight,
                            ExpectedSkipRoster = expectedSkipRoster,
                            RcSkipRoster = rcSkipRoster,
                            ExplicitPhaseRoot = host.Variables("ANTIPHON_TEST_PHASE_ROOT") ?? "",
                            ClassCostPath = bindings.ClassCostPath ?? DefaultClassCostPath(),
                            NpmOk = npmOk,
                            DotnetOk = dotnetOk,
                            LintBlocksPipeline = lintBlocksPipeline,
                            CoverageComplete = coverageComplete,
                            TestsPassed = testsPassed,
                            OverallFailed = overallFailed,
                            ActiveNative = activeNative,
                            NativeActiveMax = nativeActiveMax,
                            TestsOutcome = outcome,
                            HarnessHangReason = harnessHangReason,
                            Reasons = new List<object>(reasons),
                            SuiteResults = new List<object>(suiteResults),
                            ToleratedSkipRows = new List<object>(toleratedSkipRows),
                            ExpectedSkipWarnings = new List<object>(expectedSkipWarnings),
                            ProduceDiscovery = request => NightlyDiscoveryProducer.Produce(host, request),
                            E2EPrerequisites = bindings.E2EPrerequisites,
                            // The executed manifest (TestPhaseManifests.WriteExecuted), into the bound writer context.
                            PublishObservation = seen => TestPhaseManifests.WriteExecuted(seen.Verdict, seen.LogRoot, seen.TrxPath, seen.Suite, seen.Chunk, seen.RunId,
                                seen.Attempt, seen.HostWallSeconds, executedContext, Scope()),
                            ActiveChunk = bindings.ActiveChunk,
                            PublicationScope = Scope,
                            Warning = text => Warn(host, text),
                        };
                        Exception dispatchFailure;
                        try
                        {
                            var runner = new NightlySuiteRunner(state);
                            if (incremental == null)
                                dispatchFailure = NightlyDispatch.CoordinateOrFailure(state, runner.Steps(), selectedSuites.Cast<object>().ToArray(), null, null,
                                    PsValue.Str(PsValue.Prop(dispatchInput, "Digest")), PsValue.Prop(dispatchInput, "History"), PsValue.Prop(dispatchInput, "PreviousRun"),
                                    PsValue.Str(PsValue.Prop(dispatchInput, "FallbackReason")));
                            else
                            {
                                dispatchFailure = null;
                                var execution = NightlyDispatch.RunIncremental(state, runner, selectedSuites, incremental);
                                if (execution.Refusal.Length > 0 || !execution.AllGreen)
                                {
                                    state.TestsPassed = false;
                                    throw new NightlyRefusal(execution.Refusal.Length > 0 ? execution.Refusal : "incremental-tests-incomplete");
                                }
                                coverageScope = execution.CoverageScope;
                            }
                        }
                        finally
                        {
                            coverageComplete = state.CoverageComplete; testsPassed = state.TestsPassed; overallFailed = state.OverallFailed;
                            activeNative = state.ActiveNative; nativeActiveMax = state.NativeActiveMax; outcome = state.TestsOutcome;
                            harnessHangReason = state.HarnessHangReason;
                            reasons = new List<object>(state.Reasons); suiteResults = new List<object>(state.SuiteResults);
                            toleratedSkipRows = new List<object>(state.ToleratedSkipRows); expectedSkipWarnings = new List<object>(state.ExpectedSkipWarnings);
                            dispatchSchedule = state.DispatchSchedule;
                        }
                        if (dispatchFailure != null) throw dispatchFailure;
                    }
                    else
                    {
                        coverageComplete = false;
                        testsPassed = false;
                    }
                }
                catch (Exception ex)
                {
                    overallFailed = true;
                    coverageComplete = false;
                    if (incremental != null) testsPassed = false;
                    preflight["unhandledError"] = ex.Message;
                    RunLine(host, ex.Message);
                    reasons.Add(ex.Message);
                }

                var suiteLoopBlocked = !dockerOk || !diskOk || !npmOk || lintBlocksPipeline || !dotnetOk;
                if (suiteLoopBlocked)
                {
                    foreach (var unstartedSuite in selectedSuites)
                    {
                        var why = "unstarted";
                        var phase = "preflight";
                        if (!npmOk || lintBlocksPipeline || !dotnetOk) { why = "build-failed"; phase = "build"; }
                        QualificationTiming.Update(new ChunkTimingUpdate
                        {
                            LogRoot = logRoot, TimingRoot = timingRoot, Suite = unstartedSuite, Chunk = "unstarted", LastPhase = phase, Why = why,
                            BuildSeconds = qualificationBuildSeconds, StartedAt = Stamp(startedAt), RunId = invocationId, Attempt = attempt,
                        }, host.UtcNow);
                    }
                }

                if (!lintOk)
                {
                    coverageComplete = false;
                    testsPassed = false;
                    if (string.Equals(profile, "nightly", StringComparison.OrdinalIgnoreCase) && string.Equals(outcome, "green", StringComparison.OrdinalIgnoreCase))
                        outcome = NightlyTestsPlan.Outcome(outcome, "BUILD");
                }
                if (nativeActiveMax > 1) { coverageComplete = false; reasons.Add("native-overlap"); }
                var skipRosterFile = Path.Combine(repoRoot, "tests", "expected-platform-skips.json");
                var testsProject = Path.Combine(repoRoot, "tests", "Antiphon.Tests");
                if (File.Exists(skipRosterFile) && Directory.Exists(testsProject) && expectedSkipRoster.Length > 0)
                {
                    foreach (var gap in NightlyExpectedSkips.SourceGaps(repoRoot, platform, expectedSkipRoster, expectedSkipScan)) AddWarning(expectedSkipWarnings, "stale-roster:" + gap);
                    foreach (var gap in NightlyExpectedSkips.ReasonGaps(repoRoot, platform, expectedSkipRoster, expectedSkipScan)) AddWarning(expectedSkipWarnings, "skip-reason:" + gap);
                    var drift = NightlyExpectedSkips.RosterDrift(repoRoot, expectedSkipRoster, expectedSkipScan);
                    foreach (var mismatch in PsValue.AsArray(PsValue.Prop(drift, "Mismatches")))
                    {
                        var text = PsValue.Str(mismatch);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        AddWarning(expectedSkipWarnings, "roster-drift:" + text);
                    }
                }
                // A scan whose results were not wanted is still waited for here; one left by an exception before this point
                // never faults and is waited for by the next scan or in-line walk.
                if (expectedSkipScan != null) NightlyExpectedSkips.Wait(expectedSkipScan.Task);

                if (profile == "rc" && platform == "linux")
                {
                    coverageScope = "PlatformDiagnostic";
                    coverageComplete = false;
                    reasons.Add("windows-rc-obligations-outstanding");
                }

                var completedAt = host.UtcNow;
                var selectionForSummary = SelectionForSummary(selection, timingRoot);
                if (publicationContext != null && !publicationContext.Refused && !publicationContext.Closed)
                {
                    var terminal = "success";
                    if (!string.IsNullOrWhiteSpace(PsValue.Str(preflight["unhandledError"]))) terminal = "orchestration-error";
                    else if (string.Equals(outcome, "TIMEOUT", StringComparison.OrdinalIgnoreCase)) terminal = "timeout";
                    else if (overallFailed) terminal = "failed-run";
                    try { TestPhasePublication.CompleteProjection(publicationContext, terminal, Scope()); }
                    catch (Exception) { FailureMarker(logRoot, timingRoot, "phase-summary-failed"); }
                }
                object[] timingChunks = new object[0];
                object[] suiteTimeline = new object[0];
                try
                {
                    var report = QualificationTiming.Report(logRoot, timingRoot);
                    if (report != null)
                    {
                        timingChunks = (report.Chunks ?? new ChunkTiming[0]).Cast<object>().ToArray();
                        suiteTimeline = (report.Timeline ?? new SuiteTimeline[0]).Cast<object>().ToArray();
                    }
                }
                catch (Exception) { FailureMarker(logRoot, timingRoot, "phase-summary-failed"); }
                var expectedSkipRosterDigest = expectedSkipRoster.Length > 0 ? NightlyChunkVerdict.RosterDigest(expectedSkipRoster) : "";
                var evidenceWriteVerdict = EvidenceFiles.MergeDeferredWriteVerdict(reasons.ToArray());
                reasons = new List<object>(evidenceWriteVerdict.Reasons ?? new string[0]);
                if (!evidenceWriteVerdict.CoverageComplete)
                {
                    coverageComplete = false;
                    testsPassed = false;
                    overallFailed = true;
                    outcome = NightlyTestsPlan.Outcome(outcome, "TESTS");
                }
                var summary = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "startedAt", Stamp(startedAt) },
                    { "completedAt", Stamp(completedAt) },
                    { "durationSeconds", Math.Round((completedAt - startedAt).TotalSeconds, 3) },
                    { "sha", sha },
                    { "gitRef", options.GitRef ?? "" },
                    { "trigger", options.Trigger ?? "" },
                    { "profile", profile },
                    { "expectedSha", options.ExpectedSha ?? "" },
                    { "requiredSuites", requiredSuites },
                    { "runId", invocationId },
                    { "clone", repoRoot },
                    { "logDir", logRoot },
                    { "selectedSuites", selectedSuites },
                    { "selection", selectionForSummary },
                    { "timingRoot", timingRoot },
                    { "timingChunks", timingChunks },
                    { "suiteTimeline", suiteTimeline },
                    { "platform", platform },
                    { "coverageScope", coverageScope },
                    { "outcome", outcome },
                    { "harnessHang", NightlyWatchdog.HarnessHangField(harnessHangReason == "completed-but-hung") },
                    { "succeeded", !overallFailed && coverageComplete },
                    { "coverageComplete", coverageComplete },
                    { "testsPassed", testsPassed },
                    { "policyHash", policyHash },
                    { "reasons", reasons.ToArray() },
                    { "warnings", expectedSkipWarnings.ToArray() },
                    { "expectedSkipRosterDigest", expectedSkipRosterDigest },
                    { "toleratedSkips", toleratedSkipRows.ToArray() },
                    { "nativeActiveMax", nativeActiveMax },
                    { "preflight", preflight },
                    { "builds", buildResults.ToArray() },
                    { "suites", suiteResults.ToArray() },
                    { "invocationId", invocationId },
                };
                // D-5, dispatchProgress and the streamed summary.json write.
                WriteRunSummary(summary, suiteResults.ToArray(), dispatchSchedule, sha, completedAt, summaryPath, logRoot, phaseTimingDirectory, Scope());
                RunLine(host, "wrote " + summaryPath);

                return new NightlyTestsResult
                {
                    ExitCode = overallFailed ? 1 : 0,
                    CoverageComplete = coverageComplete,
                    TestsPassed = testsPassed,
                    Platform = platform,
                    CoverageScope = coverageScope,
                    Reasons = reasons.Select(PsValue.Str).ToArray(),
                    Profile = profile,
                    RequiredSuites = requiredSuites,
                    Selection = selection,
                    SummaryPath = summaryPath,
                    Selected = selectedSuites,
                    NativeActiveMax = nativeActiveMax,
                    PolicyHash = policyHash,
                };
            }
            finally
            {
                if (publicationContext != null) TestPhasePublication.Close(publicationContext);
            }
        }

        /// <summary>
        /// Write-NightlyRunSummary (CARD-2005): the one writer of summary.json (NightlyRunSummary.Write), with the phase
        /// summary (TestPhaseRunSummary, top 50) run when the phase plan exists.
        /// </summary>
        public static void WriteRunSummary(IDictionary summary, object[] suiteResults, object dispatchSchedule, string sha, DateTime completedAt, string summaryPath,
            string logRoot, string phaseTimingDirectory, TestPhasePublicationScope scope)
        {
            NightlyRunSummary.Write(summary, suiteResults, dispatchSchedule, sha, completedAt, summaryPath, logRoot, phaseTimingDirectory,
                root => TestPhaseRunSummary.Run(root, 50, scope));
        }

        /// <summary>
        /// Get-NightlyToolchainPreflight (CARD-1698): the toolchain versions, floors and warnings for summary.json
        /// preflight.toolchain, the versions read through the host probe. Each warning's message is printed. Never throws:
        /// a failed check is one check-failed warning and no tools.
        /// </summary>
        public static OrderedDictionary ToolchainPreflight(string repoRoot, NightlyHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            try
            {
                var report = ToolchainFloor.Report(repoRoot, tool => host.Probe.ToolVersion(tool));
                var tools = report.Tools.Select(t => (object)new OrderedDictionary
                {
                    { "tool", t.Tool }, { "found", t.Found }, { "floor", t.Floor }, { "source", t.Source }, { "enforced", t.Enforced }, { "status", t.Status },
                }).ToArray();
                var warnings = report.Warnings.Select(w => (object)new OrderedDictionary
                {
                    { "tool", w.Tool }, { "found", w.Found }, { "floor", w.Floor }, { "impact", w.Impact }, { "fix", w.Fix }, { "reason", w.Reason }, { "message", w.Message },
                }).ToArray();
                foreach (var w in report.Warnings) host.Out.WriteLine(w.Message);
                return new OrderedDictionary { { "tools", tools }, { "warnings", warnings } };
            }
            catch (Exception ex)
            {
                var message = "WARNING: the toolchain floor check failed: " + ex.Message;
                host.Out.WriteLine(message);
                return new OrderedDictionary
                {
                    { "tools", new object[0] },
                    { "warnings", new object[] { new OrderedDictionary { { "tool", "toolchain" }, { "reason", "check-failed" }, { "message", message } } } },
                };
            }
        }

        /// <summary>
        /// Invoke-NightlyExactRoster: the exact selected execution front door. Full-profile publication accounting is
        /// unchanged; a caller cannot promote this receipt to Full or activate ChangeAware with it. Each exact roster chunk
        /// runs the assembly ("dotnet &lt;assembly&gt; &lt;native argv&gt;" under Owned custody on the host, 600 s) into
        /// resultsDirectory/leaf-&lt;n&gt;; its TRX, diagnostic and required UIDs must agree. Writes receipt.json and returns the
        /// receipt; refuses discovery-build-mismatch, new-uid-required, execution-output-already-exists,
        /// exact-execution-timeout and exact-execution-incomplete:&lt;reasons&gt; (that one after writing the incomplete receipt).
        /// </summary>
        public static OrderedDictionary ExactRoster(NightlyHost host, string assemblyPath, object discovery, object[] members, string resultsDirectory,
            string[] classifiedUids = null, IDictionary environment = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var roster = NightlyChunkVerdict.ExactRoster(discovery, members, classifiedUids);
            var assemblyHash = PsValue.Str(PsValue.Prop(roster, "AssemblyHash"));
            if (!string.Equals(assemblyHash, NightlyDiagnosticLog.AssemblyHash(assemblyPath), StringComparison.Ordinal)) throw new NightlyRefusal("discovery-build-mismatch");
            if (PsValue.AsArray(PsValue.Prop(roster, "UnresolvedUids")).Length > 0) throw new NightlyRefusal("new-uid-required: classification required before launch");
            if (File.Exists(resultsDirectory) || Directory.Exists(resultsDirectory)) throw new NightlyRefusal("execution-output-already-exists");
            Directory.CreateDirectory(resultsDirectory);
            var childEnvironment = ExactRosterEnvironment(environment);
            var timingRoot = host.Variables("ANTIPHON_TEST_TIMING_ROOT");
            var terminal = new List<object>();
            var receipts = new List<object>();
            var index = 0;
            foreach (var chunk in PsValue.AsArray(PsValue.Prop(roster, "Chunks")))
            {
                if (!string.Equals(assemblyHash, NightlyDiagnosticLog.AssemblyHash(assemblyPath), StringComparison.Ordinal)) throw new NightlyRefusal("discovery-build-mismatch");
                index++;
                var directory = Path.Combine(resultsDirectory, "leaf-" + index.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(directory);
                var trx = Path.Combine(directory, "run.trx");
                var filter = PsValue.Str(PsValue.Prop(chunk, "Filter"));
                var arguments = new List<string> { Path.GetFullPath(assemblyPath) };
                arguments.AddRange(NightlyTestsPlan.NativeExecutionArguments(trx, directory, null, filter, "", ""));
                // CARD-1313 timing journal (antiphon-test-timing-v1) through TestTimings; persistence failure is not a test result.
                if (!string.IsNullOrEmpty(timingRoot))
                {
                    var stamp = host.Clock.GetUtcNow().ToString("o", CultureInfo.InvariantCulture);
                    WriteTimingEvent(timingRoot, new OrderedDictionary
                    {
                        { "schemaVersion", "antiphon-test-timing-v1" }, { "eventId", "nightly-start-" + index.ToString(CultureInfo.InvariantCulture) }, { "eventKind", "run-started" },
                        { "runId", "nightly" }, { "attempt", 1 }, { "producer", "nightly" }, { "setKind", "chunk" }, { "setId", filter },
                        { "outcome", "started" }, { "startedAt", stamp }, { "timingPersistence", "complete" },
                    });
                }
                var run = host.Launch(new NightlyLaunch("dotnet", arguments.ToArray(), resultsDirectory, 600000, childEnvironment, Path.Combine(directory, "console.log"),
                    NightlyCustody.Owned, null));
                if (!string.IsNullOrEmpty(timingRoot))
                {
                    var stamp = host.Clock.GetUtcNow().ToString("o", CultureInfo.InvariantCulture);
                    WriteTimingEvent(timingRoot, new OrderedDictionary
                    {
                        { "schemaVersion", "antiphon-test-timing-v1" }, { "eventId", "nightly-done-" + index.ToString(CultureInfo.InvariantCulture) }, { "eventKind", "set-completed" },
                        { "runId", "nightly" }, { "attempt", 1 }, { "producer", "nightly" }, { "setKind", "chunk" }, { "setId", filter },
                        { "outcome", run.ExitCode == 0 ? "passed" : "failed" }, { "exitCode", run.ExitCode },
                        { "startedAt", stamp }, { "endedAt", stamp }, { "category", "unavailable" }, { "timingPersistence", "complete" },
                    });
                }
                if (run.TimedOut) throw new NightlyRefusal("exact-execution-timeout");
                var nodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.Read(NightlyDiagnosticLog.Latest(directory), "execution"), "Nodes"));
                var trxRows = NightlyTrx.ReadIdentities(trx);
                var cross = NightlyVerdicts.CrossCheck(trxRows, nodes, new string[0]);
                var requiredUids = PsValue.AsArray(PsValue.Prop(chunk, "RequiredUids")).Select(PsValue.Str).ToArray();
                var verdict = NightlyChunkVerdict.ExactCoverage(requiredUids, nodes);
                var complete = PsValue.IsTrue(PsValue.Prop(cross, "Ok")) && PsValue.IsTrue(PsValue.Prop(verdict, "Ok")) && run.ExitCode == 0;
                receipts.Add(new OrderedDictionary
                {
                    { "filter", filter }, { "requiredUids", requiredUids }, { "executedUids", nodes.Select(n => PsValue.Str(PsValue.Prop(n, "Uid"))).ToArray() },
                    { "exitCode", run.ExitCode }, { "trx", trx }, { "complete", complete },
                });
                terminal.AddRange(nodes);
                if (!complete)
                {
                    var verdictReasons = PsValue.AsArray(PsValue.Prop(verdict, "Reasons")).Select(PsValue.Str).ToArray();
                    WriteAtomicJson(Path.Combine(resultsDirectory, "receipt.json"), new OrderedDictionary
                    {
                        { "format", "antiphon-exact-roster-v1" }, { "complete", false }, { "assemblyHash", assemblyHash }, { "chunks", receipts.ToArray() }, { "reasons", verdictReasons },
                    });
                    throw new NightlyRefusal("exact-execution-incomplete:" + string.Join(",", verdictReasons));
                }
            }
            var rosterRequired = PsValue.AsArray(PsValue.Prop(roster, "RequiredUids")).Select(PsValue.Str).ToArray();
            var final = NightlyChunkVerdict.ExactCoverage(rosterRequired, terminal.ToArray());
            var result = new OrderedDictionary
            {
                { "format", "antiphon-exact-roster-v1" }, { "complete", PsValue.IsTrue(PsValue.Prop(final, "Ok")) }, { "executionEvidence", true }, { "releaseCredit", false },
                { "assemblyHash", assemblyHash }, { "requiredUids", rosterRequired }, { "executedUids", terminal.Select(n => PsValue.Str(PsValue.Prop(n, "Uid"))).ToArray() },
                { "chunks", receipts.ToArray() },
            };
            WriteAtomicJson(Path.Combine(resultsDirectory, "receipt.json"), result);
            return result;
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        /// <summary>The run's publication scope: its timing root, this command as the writer's host, no commit seams.</summary>
        internal static TestPhasePublicationScope PublicationScope(string timingRoot) =>
            new TestPhasePublicationScope(timingRoot ?? "", TestPhaseRunSummary.CommandEdition, "1", null, null);

        /// <summary>scripts/lib/nightly-class-costs.json of the product root; "" when there is no product root.</summary>
        public static string DefaultClassCostPath()
        {
            var root = ProductRoot();
            return root == null ? "" : Path.Combine(root, "scripts", "lib", "nightly-class-costs.json");
        }

        // Invoke-NightlyOwnedProcess: Owned custody on the host; what the launch observed.
        private static NightlyLaunchResult OwnedProcess(NightlyHost host, string fileName, IEnumerable<string> arguments, string workingDirectory, int timeoutMilliseconds,
            IDictionary environment, string logPath) =>
            host.Launch(NightlyChunkRunner.OwnedLaunch(fileName, arguments, workingDirectory, timeoutMilliseconds, environment, logPath));

        private static OrderedDictionary BuildRow(string name, NightlyLaunchResult run) => new OrderedDictionary
        {
            { "name", name }, { "exitCode", run.ExitCode }, { "timedOut", run.TimedOut }, { "result", run.ExitCode == 0 ? "pass" : "FAIL" },
        };

        private static double Seconds(NightlyHost host, long started) => Math.Round(host.Clock.GetElapsedTime(started).TotalSeconds, 3);

        private static string Stamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

        // Write-NightlyRunLine.
        internal static void RunLine(NightlyHost host, string message) => host.Out.WriteLine("[" + Stamp(host.UtcNow) + "] " + message);

        // Write-Warning -WarningAction Continue.
        internal static void Warn(NightlyHost host, string text) => host.Error.WriteLine("WARNING: " + text);

        // Write-TestPhaseFailureMarker: an unwritable timing directory never changes the run's verdict.
        private static void FailureMarker(string logRoot, string timingRoot, string code)
        {
            try { TestPhasePublication.WriteFailureMarker(logRoot, timingRoot ?? "", code); }
            catch (Exception) { }
        }

        private static void AddWarning(List<object> warnings, string warning)
        {
            if (!warnings.Any(w => string.Equals(PsValue.Str(w), warning, StringComparison.OrdinalIgnoreCase))) warnings.Add(warning);
        }

        // The selection for summary.json: a copy (ConvertTo-Json -Depth 8 | ConvertFrom-Json) whose blank timingRoot is the run's.
        private static object SelectionForSummary(object selection, string timingRoot)
        {
            if (selection == null) return null;
            object copy = null;
            var text = NightlyJson.Text(selection, 8);
            if (text != null) copy = NightlyJson.ParseText(text, 1024, true);
            if (copy == null) copy = selection;
            if (PsValue.PropertyNames(copy).Any(n => string.Equals(n, "timingRoot", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(copy, "timingRoot"))))
                PsValue.SetNote(copy, "timingRoot", timingRoot ?? "");
            return copy;
        }

        // The exact roster child's environment: this process's with the given values over it, or inherited when none are given.
        private static IReadOnlyDictionary<string, string> ExactRosterEnvironment(IDictionary environment)
        {
            if (environment == null) return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables()) map[PsValue.Str(pair.Key)] = PsValue.Str(pair.Value);
            foreach (DictionaryEntry pair in environment) map[PsValue.Str(pair.Key)] = PsValue.Str(pair.Value);
            return map;
        }

        private static void WriteTimingEvent(string root, IDictionary timingEvent)
        {
            try { TestTimings.WriteEvent(root, timingEvent); }
            catch (Exception) { }
        }

        /// <summary>
        /// Write-NightlyAtomicJson: ConvertTo-Json's indented layout, UTF-8 without a byte-order mark, written to a temporary
        /// file beside the target that replaces it (its .bak removed) or moves into place.
        /// </summary>
        internal static void WriteAtomicJson(string path, object value)
        {
            var text = NightlyJson.IndentedText(value, 12) ?? throw new NightlyRefusal("nightly-json-write-failed unsupported-value " + path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = path + ".bak";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path) || Directory.Exists(path))
                {
                    File.Replace(temporary, path, backup);
                    try { if (File.Exists(backup)) File.Delete(backup); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
