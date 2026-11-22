// W2-nightly S19: the nightly run core (plan 2026-11-21-w2-nightly-port-slices.md, slice S19), moved from
// scripts/lib/nightly-run-impl.ps1 (deleted in S20): Invoke-AntiphonNightlyRunCore (the profile and candidate refusals, the reparse, shared
// tree and ownership guards, the run lock and the shared native lock, the state writes, the sync with its line-ending
// repair, the selection attempt, the self-update hop, WhatIf, the tests hop, the summary settle, the report, the final
// state and the credit), Repair-NightlyCheckoutLineEndings, Write-NightlyHopFailure, Write-NightlyRunState,
// Invoke-NightlyWatchedCommand (the host's launcher), ConvertTo-NightlyResultRecord and Invoke-AntiphonNightlyRun (the
// completion record as the last stdout line), with the ownership and reparse helpers of nightly-common.ps1 it uses
// (ConvertTo-NightlyResolvedPath, Resolve-NightlyReparsePath, Test-NightlyGitWorktree, Get-NightlyOriginUrl,
// Test-NightlyPathContained, Test-NightlyRunContextOwnership); nightly-common.ps1 is a pinned input and keeps its copies
// until S27.
//
// Two rules differ from the PowerShell. The self-update hop: after a sync the run hops to the clone's
// scripts/nightly-run.ps1 -NoSync with the same argv whenever this code's product root (the checkout the CLI was built
// from) is not the checkout root; the PowerShell compared file hashes (always unequal: nightly-run-impl.ps1 against the
// clone's nightly-run.ps1). The tests hop runs `dotnet exec <this CLI> nightly-tests` (NightlyTests.Run) and never the
// JSON-splatted selection hop; the report runs in-process (NightlyReport.Run).
//
// W2-nightly S20: wired as `antiphon-scripts nightly-run` (Antiphon.Scripts/NightlyRunCommand.cs, the production host and
// NightlyRunBindings.Production); scripts/nightly-run.ps1 is its Windows PowerShell 5.1 launcher and nightly-run-impl.ps1
// is deleted. Every observation of the machine goes through the run's NightlyHost; the collaborators that are another
// lane's (the selection attempt writer, W2-rc) are NightlyRunBindings. Plain C#: no PowerShell types; records the core
// returns are read through PsValue. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>
    /// What a nightly run answers (Invoke-AntiphonNightlyRunCore's result object, the launcher's -PassThru). A refusal sets
    /// ExitCode, Refusal and Phase and leaves the rest as they were; the properties the PowerShell added only on the final
    /// path (GreenPath, CoverageScope, Platform, Reasons, TestExit, ReportExit) stay null until then.
    /// </summary>
    public sealed class NightlyRunResult
    {
        public int ExitCode { get; set; } = 1;
        public string RunId { get; set; } = "";
        public string Phase { get; set; } = "init";
        public string Refusal { get; set; } = "";
        public string Sha { get; set; } = "";
        public bool CoverageComplete { get; set; }
        public bool TestsPassed { get; set; }
        public bool ReportDelivered { get; set; }
        public string Trigger { get; set; } = "";
        public string Profile { get; set; } = "";
        public string Ref { get; set; } = "";
        public string CreditKind { get; set; } = "";
        public string CandidateId { get; set; } = "";
        public string StateRoot { get; set; } = "";
        public string LogDir { get; set; }
        public bool OwnsLock { get; set; }
        public string LocalDueDate { get; set; } = "";
        public string PolicyHash { get; set; } = "";
        public string SummaryPath { get; set; } = "";

        /// <summary>The selection attempt document read for the run (null without one).</summary>
        public object Selection { get; set; }

        /// <summary>The rc candidate's complete-green.json, once written.</summary>
        public string GreenPath { get; set; }

        public string CoverageScope { get; set; }
        public string Platform { get; set; }
        public object[] Reasons { get; set; }
        public int? TestExit { get; set; }
        public int? ReportExit { get; set; }

        /// <summary>
        /// The result as the PowerShell object carried it: its property names (run-core names: ExitCode, Refusal, ...,
        /// coverageComplete, ...) in its order, a final-path property only once it is set.
        /// </summary>
        public OrderedDictionary ToRecord()
        {
            var record = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "ExitCode", ExitCode }, { "RunId", RunId }, { "Phase", Phase }, { "Refusal", Refusal }, { "Sha", Sha },
                { "coverageComplete", CoverageComplete }, { "testsPassed", TestsPassed }, { "reportDelivered", ReportDelivered },
                { "trigger", Trigger }, { "profile", Profile }, { "ref", Ref }, { "CreditKind", CreditKind }, { "CandidateId", CandidateId },
                { "StateRoot", StateRoot }, { "LogDir", LogDir }, { "OwnsLock", OwnsLock }, { "LocalDueDate", LocalDueDate },
                { "PolicyHash", PolicyHash }, { "SummaryPath", SummaryPath }, { "Selection", Selection },
            };
            if (GreenPath != null) record["GreenPath"] = GreenPath;
            if (CoverageScope != null) record["coverageScope"] = CoverageScope;
            if (Platform != null) record["platform"] = Platform;
            if (Reasons != null) record["reasons"] = Reasons;
            if (TestExit != null) record["testExit"] = TestExit.Value;
            if (ReportExit != null) record["reportExit"] = ReportExit.Value;
            return record;
        }
    }

    /// <summary>A selection attempt written for a run (New-ReleaseSelectionAttempt's answer): its two files, their digests and its baseline.</summary>
    public sealed class NightlySelectionBinding
    {
        public string SelectionAttemptPath { get; set; } = "";
        public string SelectionAttemptDigest { get; set; } = "";
        public string SelectionPath { get; set; } = "";
        public string SelectionDigest { get; set; } = "";
        public string BaselineSha { get; set; } = "";
    }

    /// <summary>Test-NightlyRunContextOwnership's answer: Ok, or the refused field and reason; ClonePath is the resolved clone (its Clone).</summary>
    public sealed record NightlyRunOwnership(bool Ok, string Reason, string Field, string ClonePath);

    /// <summary>
    /// The collaborators of a run that are not observations of its host. Each may be left null: the run then uses what
    /// production uses.
    /// </summary>
    public sealed class NightlyRunBindings
    {
        /// <summary>The checkout this code was built from (the hop rule); null: NightlyTests.ProductRoot().</summary>
        public string ProductRoot { get; set; }

        /// <summary>The antiphon-scripts dll the tests hop and the selection attempt run (`dotnet exec`); null: this process's entry assembly.</summary>
        public string CliPath { get; set; }

        /// <summary>The report (its options -> its exit code); null: NightlyReport.Run in-process with the production seams on the host.</summary>
        public Func<NightlyReportOptions, int> Report { get; set; }

        /// <summary>
        /// New-ReleaseSelectionAttempt (W2-rc): the request map (the PowerShell request's keys) -> the attempt written; a
        /// refusal throws. Null: NightlyRun.SelectionWorker (select-release, then release-selection-worker.ps1 under pwsh).
        /// </summary>
        public Func<IDictionary, NightlySelectionBinding> SelectionAttempts { get; set; }

        /// <summary>The run's shadow evidence recorder; null: one from ANTIPHON_RC_RUN_EVIDENCE, unseamed.</summary>
        public RcRunEvidenceRecorder RcEvidence { get; set; }

        /// <summary>
        /// W2-nightly S20, the nightly-run command's bindings: the checkout this code was built from as the product root
        /// (NightlyTests.ProductRoot), the given CLI dll for the tests hop and the selection attempt, the report in-process
        /// on its production seams over the host, the selection attempt through that CLI's select-release (then the product
        /// root's release-selection-worker.ps1 under pwsh), and the shadow evidence recorder from ANTIPHON_RC_RUN_EVIDENCE.
        /// </summary>
        public static NightlyRunBindings Production(NightlyHost host, string cliPath)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var productRoot = NightlyTests.ProductRoot();
            return new NightlyRunBindings
            {
                ProductRoot = productRoot,
                CliPath = cliPath ?? "",
                Report = options => NightlyReport.Run(options, NightlyReportSeams.Production(host, host.Variables)).ExitCode,
                SelectionAttempts = NightlyRun.SelectionWorker(cliPath, Path.Combine(productRoot ?? "", "scripts", "lib"), host.Variables("PATH")),
                RcEvidence = new RcRunEvidenceRecorder(host.Variables("ANTIPHON_RC_RUN_EVIDENCE"), false, false, line => NightlyTests.RunLine(host, line)),
            };
        }
    }

    /// <summary>The nightly run and its helpers.</summary>
    public static class NightlyRun
    {
        /// <summary>Get-NightlyDefaultStateRoot: the master lane's state root.</summary>
        public const string DefaultStateRoot = @"C:\Antiphon\nightly";

        private const string PolicyRelative = "tests/test-execution-policy.json";

        /// <summary>
        /// Invoke-AntiphonNightlyRun: the run (Core), then its completion record as the last line of the host's output
        /// (CARD-0545 D-10: every exit path, refusals included).
        /// </summary>
        public static NightlyRunResult Run(NightlyRunOptions options, NightlyHost host, NightlyRunBindings bindings = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var result = Core(options, host, bindings);
            host.Out.WriteLine(RecordLine(result));
            return result;
        }

        /// <summary>Invoke-AntiphonNightlyRunCore: one run of these options on this host. A failure is the result; nothing is thrown.</summary>
        public static NightlyRunResult Core(NightlyRunOptions options, NightlyHost host, NightlyRunBindings bindings = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            options = options ?? new NightlyRunOptions();
            bindings = bindings ?? new NightlyRunBindings();

            var stateRoot = string.IsNullOrWhiteSpace(options.StateRoot) ? DefaultStateRoot : options.StateRoot;
            var runId = string.IsNullOrWhiteSpace(options.RunId) ? Guid.NewGuid().ToString("N") : options.RunId;
            var profile = (string.IsNullOrWhiteSpace(options.Profile) ? "nightly" : options.Profile).Trim().ToLowerInvariant();
            var lastAttempt = ReleaseGateCredit.Join(stateRoot, "last-run.json");
            var lastGreen = ReleaseGateCredit.Join(stateRoot, "last-complete-green.json");
            var logRoot = string.IsNullOrWhiteSpace(options.LogRoot) ? ReleaseGateCredit.Join(stateRoot, "logs") : options.LogRoot;
            var timingRoot = options.TimingRoot ?? "";
            var selectionAttemptPath = options.SelectionAttemptPath ?? "";
            var selectionAttemptDigest = options.SelectionAttemptDigest ?? "";
            var selectionPath = options.SelectionPath ?? "";
            var selectionDigest = options.SelectionDigest ?? "";
            var baselineSha = options.BaselineSha ?? "";
            var priorFailuresPath = options.PriorFailuresPath ?? "";
            var priorFailuresDigest = options.PriorFailuresDigest ?? "";
            var candidateId = options.CandidateId ?? "";
            var checkoutRoot = options.CheckoutRoot ?? "";
            if (string.IsNullOrWhiteSpace(timingRoot) && string.IsNullOrWhiteSpace(selectionAttemptPath)) timingRoot = NightlyPaths.TimingRoot("", logRoot, runId);

            var startedAt = host.UtcNow;
            // CARD-0545 D-10: the London calendar date of the run start is the run's due-day identity.
            var localDueDate = NightlyLondon.ToLocal(startedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var exitCode = 0;
            var sha = "";
            var testExit = 0;
            var reportExit = 0;
            var coverageComplete = false;
            var testsPassed = false;
            var reportDelivered = false;
            string runDir = null;
            var rcEvidenceRoot = "";
            // S6b shadow run evidence (RcRunEvidenceRecorder): a refusal prints one line per reason and never changes the verdict.
            var rcEvidence = bindings.RcEvidence ?? new RcRunEvidenceRecorder(host.Variables("ANTIPHON_RC_RUN_EVIDENCE"), false, false, line => RunLine(host, line));
            var phase = "init";
            var hopCommand = "";
            OrderedDictionary state = null;
            NightlyRunLock runLock = null;
            ReleaseGateNativeLock nativeLock = null;
            var result = new NightlyRunResult
            {
                ExitCode = 1, RunId = runId, Phase = phase, Trigger = options.Trigger ?? "", Profile = profile, Ref = options.Ref ?? "",
                CandidateId = candidateId, StateRoot = stateRoot, LocalDueDate = localDueDate,
            };

            NightlyRunResult Refuse(string refusal, string refusedPhase, int code = 3)
            {
                result.Refusal = refusal;
                if (refusedPhase != null) result.Phase = refusedPhase;
                result.ExitCode = code;
                return result;
            }

            void Say(string line) => host.Out.WriteLine(line);

            try
            {
                try { ReleaseSelectionAttempt.AssertCapability(options.SelectionMode, host.Variables("PATH")); }
                catch (Exception e) { return Refuse(e.Message, null); }
                if (profile != "nightly" && profile != "rc")
                {
                    Say("REFUSED: unknown profile " + profile + ".");
                    return Refuse("unknown-profile", "profile");
                }
                var lane = profile == "rc" ? "rc" : "master";
                if (profile == "rc")
                {
                    // D-2: an RC must never be pointed at the master state root; its attempt, green, monitor and receipt files
                    // all live under its own candidate root.
                    var candidate = ReleaseGateCredit.Candidate(options.Ref, options.ExpectedSha);
                    if (!candidate.Ok)
                    {
                        Say("REFUSED: rc profile needs an rc/YYYY-MM-DD ref or a legacy release/rc-<stamp> (" + candidate.Reason + ").");
                        return Refuse("rc-ref", "profile");
                    }
                    if (!ReleaseGateCredit.FullSha(options.ExpectedSha))
                    {
                        Say("REFUSED: rc profile needs -ExpectedSha pinned to the candidate full sha.");
                        return Refuse("rc-expected-sha", "profile");
                    }
                    if (string.IsNullOrWhiteSpace(candidateId))
                    {
                        candidateId = candidate.CandidateId;
                        result.CandidateId = candidateId;
                    }
                    else if (!string.Equals(candidateId, candidate.CandidateId, StringComparison.Ordinal))
                    {
                        Say("REFUSED: CandidateId must match the candidate ref and pinned full sha.");
                        return Refuse("rc-candidate-id-mismatch", "profile");
                    }
                    if (string.Equals(NightlyPaths.Canonical(DefaultStateRoot), NightlyPaths.Canonical(stateRoot), StringComparison.OrdinalIgnoreCase))
                    {
                        Say("REFUSED: rc profile may not use the master state root.");
                        return Refuse("rc-master-state-root", "profile");
                    }
                    if (string.IsNullOrWhiteSpace(options.ReleaseRoot))
                    {
                        Say("REFUSED: rc profile requires an explicit -ReleaseRoot.");
                        return Refuse("rc-release-root-required", "profile");
                    }
                }
                else if (string.Equals(options.Trigger, "rc", StringComparison.OrdinalIgnoreCase))
                {
                    Say("REFUSED: trigger rc requires the rc profile.");
                    return Refuse("trigger-profile-mismatch", "profile");
                }
                try { checkoutRoot = ResolvedPath(checkoutRoot); }
                catch (Exception e)
                {
                    Say("REFUSED: reparse: " + e.Message);
                    return Refuse("reparse", "ownership");
                }

                if (NightlyPaths.IsSharedTree(checkoutRoot))
                {
                    Say("REFUSED: -CheckoutRoot is the shared tree or a worktree.");
                    Say("  CheckoutRoot: " + checkoutRoot);
                    return Refuse("shared-tree", "ownership");
                }
                if (IsGitWorktree(checkoutRoot))
                {
                    Say("REFUSED: linked Git worktree is not an independent clone.");
                    return Refuse("linked-worktree", "ownership");
                }

                var ownership = RunContextOwnership(checkoutRoot, stateRoot, logRoot, options.RemoteUrl);
                if (!ownership.Ok && !string.Equals(ownership.Reason, "unmarked-clone", StringComparison.OrdinalIgnoreCase))
                {
                    // Unmarked is allowed only before the first clone; an existing path without the marker is refused.
                    if (Exists(checkoutRoot))
                    {
                        Say("REFUSED: run-context ownership (" + ownership.Field + "/" + ownership.Reason + ").");
                        return Refuse(ownership.Reason, "ownership");
                    }
                }
                else if (!ownership.Ok && Exists(ReleaseGateCredit.Join(checkoutRoot, ".git")))
                {
                    Say("REFUSED: unmarked clone is not a proven nightly checkout.");
                    return Refuse("unmarked-clone", "ownership");
                }

                runLock = new NightlyRunLock(host);
                var held = runLock.Enter(stateRoot, runId, options.ContinueRunId ?? "", options.ContinueParentPid, options.ContinueParentStartedAt ?? "");
                if (!held.Ok)
                {
                    Say("REFUSED: nightly lock (" + held.Reason + ").");
                    return Refuse(held.Reason, "lock");
                }
                result.OwnsLock = held.OwnsLock;

                // D-4: one shared verification lock across the master and RC lanes, taken in the same order by both entry
                // points. A live owner is never stolen on age; a busy scheduled RC records deferred-busy and waits for its next slot.
                var sharedOwnerPid = 0;
                var sharedOwnerStart = "";
                if (!string.IsNullOrWhiteSpace(options.ContinueRunId))
                {
                    sharedOwnerPid = options.ContinueParentPid;
                    sharedOwnerStart = options.ContinueParentStartedAt ?? "";
                }
                nativeLock = new ReleaseGateNativeLock(host);
                var shared = nativeLock.Enter(options.CoordinationRoot, runId, lane, options.ContinueRunId ?? "", sharedOwnerPid, sharedOwnerStart);
                if (!shared.Ok)
                {
                    Say("REFUSED: shared native lock (" + shared.Reason + ").");
                    var busy = Map(
                        ("runId", runId), ("profile", profile), ("lane", lane), ("ref", options.Ref), ("trigger", options.Trigger), ("phase", "deferred-busy"),
                        ("reason", shared.Reason ?? ""), ("startedAt", Stamp(startedAt)), ("localDueDate", localDueDate), ("coverageComplete", false),
                        ("testsPassed", false), ("reportDelivered", false), ("succeeded", false), ("exitCode", 3));
                    WriteState(host, lastAttempt, busy, true);
                    return Refuse(shared.Reason ?? "", "shared-lock");
                }

                state = Map(
                    ("runId", runId), ("sha", ""), ("ref", options.Ref), ("trigger", options.Trigger), ("profile", profile), ("expectedSha", options.ExpectedSha),
                    ("candidateId", candidateId), ("lane", lane), ("phase", "Started"), ("startedAt", Stamp(startedAt)), ("localDueDate", localDueDate),
                    ("lastActivityAt", Stamp(startedAt)), ("coverageComplete", false), ("testsPassed", false), ("reportDelivered", false), ("succeeded", false),
                    ("testExit", null), ("reportExit", null), ("exitCode", null), ("logDir", null), ("policyHash", null), ("noReport", options.NoReport));
                phase = "Started";
                if (!WriteState(host, lastAttempt, state, true)) return Refuse("", "Started", 1);

                // Retain the optional input OUTSIDE the checkout before git clean/sync. Receiving implementations
                // independently verify the copied bytes.
                if (!string.IsNullOrEmpty(priorFailuresPath))
                {
                    var historyRoot = ReleaseGateCredit.Join(ReleaseGateCredit.Join(stateRoot, "history"), runId);
                    if (PathContained(historyRoot, checkoutRoot)) Warn(host, "failed-first fallback: history-copy-inside-checkout");
                    else
                    {
                        var history = PriorFailureHistory.InitializeDispatch(priorFailuresPath, priorFailuresDigest, historyRoot, "", options.AcceptCrossCandidateSeed,
                            options.SeedSourceCandidateId ?? "", host.UtcNow);
                        var warning = PsValue.Str(PsValue.Prop(history, "Warning"));
                        if (!string.IsNullOrEmpty(warning)) Warn(host, warning);
                        if (PsValue.IsTrue(PsValue.Prop(history, "History")))
                        {
                            priorFailuresPath = PsValue.Str(PsValue.Prop(history, "Path"));
                            priorFailuresDigest = PsValue.Str(PsValue.Prop(history, "Digest"));
                        }
                    }
                }

                // The selection attempt both branches bind once the head is known. A refusal is the run's refusal.
                bool Select()
                {
                    if (profile != "rc" && string.Equals(options.SelectionMode, "Full", StringComparison.Ordinal) && string.IsNullOrEmpty(selectionAttemptPath)) return true;
                    try
                    {
                        var request = Map(
                            ("RepositoryRoot", checkoutRoot), ("CandidateSha", sha), ("CandidateRef", options.Ref), ("Repository", options.Repository), ("Profile", profile),
                            ("ReleaseRoot", options.ReleaseRoot), ("CandidateId", candidateId), ("RunId", runId), ("SelectionMode", options.SelectionMode),
                            ("BaselineSha", baselineSha), ("BaselineReceiptPath", options.BaselineReceiptPath), ("QualificationPath", options.QualificationPath),
                            ("TimingRoot", timingRoot), ("TrainManifestPath", options.TrainManifestPath), ("ReleaseIntentPath", options.ReleaseIntentPath),
                            ("SelectionAttemptPath", selectionAttemptPath), ("SelectionAttemptDigest", selectionAttemptDigest), ("SelectionPath", selectionPath),
                            ("SelectionDigest", selectionDigest), ("Attempt", options.Attempt));
                        if (string.IsNullOrEmpty(selectionAttemptPath))
                        {
                            var attempts = bindings.SelectionAttempts
                                ?? SelectionWorker(CliPath(bindings), Path.Combine(bindings.ProductRoot ?? NightlyTests.ProductRoot() ?? "", "scripts", "lib"), host.Variables("PATH"));
                            var bound = attempts(request) ?? throw new NightlyRefusal("selection-refused:no-answer");
                            selectionAttemptPath = bound.SelectionAttemptPath ?? "";
                            selectionAttemptDigest = bound.SelectionAttemptDigest ?? "";
                            selectionPath = bound.SelectionPath ?? "";
                            selectionDigest = bound.SelectionDigest ?? "";
                            baselineSha = bound.BaselineSha ?? "";
                            request["SelectionAttemptPath"] = selectionAttemptPath;
                            request["SelectionAttemptDigest"] = selectionAttemptDigest;
                            request["SelectionPath"] = selectionPath;
                            request["SelectionDigest"] = selectionDigest;
                            request["BaselineSha"] = baselineSha;
                        }
                        var selection = ReleaseSelectionAttempt.Read(ReleaseSelectionRequest.From(request));
                        result.Selection = selection;
                        state["selection"] = selection;
                        state["coverageScope"] = PsValue.Prop(selection, "coverageScope");
                        return true;
                    }
                    catch (Exception e)
                    {
                        Refuse(e.Message, "selection");
                        return false;
                    }
                }

                if (!options.NoSync)
                {
                    phase = "fetch";
                    state["phase"] = phase;
                    state["lastActivityAt"] = Stamp(host.UtcNow);
                    if (!WriteState(host, lastAttempt, state, true)) return Refuse("", phase, 1);
                    var parent = Path.GetDirectoryName(checkoutRoot);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (!Exists(ReleaseGateCredit.Join(checkoutRoot, ".git")))
                    {
                        Git(host, parent, "git clone failed with exit {0}", "clone", "-c", "core.longpaths=true", options.RemoteUrl, checkoutRoot);
                        NightlyTests.WriteAtomicJson(ReleaseGateCredit.Join(checkoutRoot, ".antiphon-nightly-owned"), Map(("origin", options.RemoteUrl), ("kind", "nightly-clone")));
                    }
                    // Git for Windows needs its own opt-in even when OS long paths are enabled. Configure only the owned
                    // clone, before checkout/reset or fixture cleanup.
                    Git(host, checkoutRoot, "git config core.longpaths failed with exit {0}", "config", "--local", "core.longpaths", "true");
                    var fetch = host.RunGit(checkoutRoot, "fetch", "origin", options.Ref);
                    if (fetch.ExitCode != 0) throw new NightlyRefusal("git fetch origin " + options.Ref + " failed with exit " + fetch.ExitCode.ToString(CultureInfo.InvariantCulture));
                    Git(host, checkoutRoot, "git reset --hard FETCH_HEAD failed with exit {0}", "reset", "--hard", "FETCH_HEAD");
                    RepairLineEndings(host, checkoutRoot);
                    // These untracked root markers authorize both the self-update hop and later runs.
                    Git(host, checkoutRoot, "git clean -fdx failed with exit {0}", "clean", "-fdx", "-e", "/.antiphon-nightly-owned", "-e", "/.antiphon-release-owned");
                    sha = (host.RunGit(checkoutRoot, "rev-parse", "HEAD").Output ?? "").Trim();
                    state["sha"] = sha;
                    if (!string.IsNullOrWhiteSpace(options.ExpectedSha) && !string.Equals(sha, options.ExpectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        // D-3: a moved candidate ref fails; never retest implicitly under its old identity.
                        Say("REFUSED: candidate moved, head=" + sha + " expected=" + options.ExpectedSha);
                        state["phase"] = "candidate-moved";
                        state["exitCode"] = 3;
                        WriteState(host, lastAttempt, state, true);
                        result.Sha = sha;
                        return Refuse("candidate-moved", "candidate-moved");
                    }
                    if (!Select()) return result;

                    // The self-update hop: the clone's own launcher runs the clone's own code, unless this code is the clone's.
                    var cloneScript = ReleaseGateCredit.Join(checkoutRoot, ReleaseGateCredit.Join("scripts", "nightly-run.ps1"));
                    if (File.Exists(cloneScript) && !SameRoot(bindings.ProductRoot ?? NightlyTests.ProductRoot(), checkoutRoot))
                    {
                        phase = "hop";
                        var hopArgs = SelfHopArguments(options, cloneScript, checkoutRoot, logRoot, stateRoot, profile, runId, host.Processes.CurrentPid,
                            ReleaseGateCredit.RequiredUtc(shared.Record?.ProcessStartedAt), candidateId, timingRoot, baselineSha, selectionAttemptPath,
                            selectionAttemptDigest, selectionPath, selectionDigest, priorFailuresPath, priorFailuresDigest);
                        var launch = new NightlyLaunch("pwsh", hopArgs, checkoutRoot);
                        hopCommand = launch.CommandText;
                        var hop = host.Launch(launch);
                        exitCode = hop.ExitCode;
                        result.ExitCode = exitCode;
                        result.Phase = "hop";
                        result.Sha = sha;
                        return result;
                    }
                }
                else
                {
                    // A -NoSync checkout was prepared elsewhere. Do not mutate it. Refuse unless this clone already opted Git
                    // into long paths.
                    var longpaths = host.RunGit(checkoutRoot, "config", "--local", "--get", "core.longpaths");
                    if (longpaths.ExitCode != 0 || !string.Equals((longpaths.Output ?? "").Trim(), "true", StringComparison.Ordinal))
                    {
                        Say("REFUSED: longpaths-not-enabled");
                        Say("git -C " + checkoutRoot + " config --local core.longpaths true");
                        state["phase"] = "longpaths-not-enabled";
                        state["exitCode"] = 3;
                        WriteState(host, lastAttempt, state, true);
                        return Refuse("longpaths-not-enabled", "longpaths-not-enabled");
                    }
                    sha = (host.RunGit(checkoutRoot, "rev-parse", "HEAD").Output ?? "").Trim();
                    state["sha"] = sha;
                    if (!Select()) return result;
                }

                if (options.WhatIf)
                {
                    result.ExitCode = 0;
                    result.Phase = "whatif";
                    result.Sha = sha;
                    return result;
                }

                // S6b shadow: one evidence root for this run, derived once and passed to every hook.
                phase = "tests";
                runDir = ReleaseGateCredit.Join(logRoot, NightlyRunParts.RunDirectoryName(host.UtcNow, runId));
                Directory.CreateDirectory(runDir);
                rcEvidenceRoot = ReleaseGateCredit.Join(runDir, "rc-run-evidence");
                var rcInputs = File.Exists(ReleaseGateCredit.Join(checkoutRoot, PolicyRelative)) ? new[] { PolicyRelative } : new string[0];
                rcEvidence.SourceSnapshot(rcEvidenceRoot, "", checkoutRoot, rcInputs, sha);
                state["logDir"] = runDir;
                state["phase"] = phase;
                state["lastActivityAt"] = Stamp(host.UtcNow);
                if (!WriteState(host, lastAttempt, state, true)) return Refuse("", phase, 1);

                // CARD-1698: warn about a tool below its floor before the tests hop; never refuse, never change the exit
                // code. nightly-tests records the versions in summary.json. The host probe reads the versions.
                try
                {
                    foreach (var floorWarning in ToolchainFloor.Warnings(checkoutRoot, tool => host.Probe.ToolVersion(tool))) RunLine(host, floorWarning.Message);
                }
                catch (Exception e)
                {
                    RunLine(host, "WARNING: the toolchain floor check failed: " + e.Message);
                }

                var tests = TestsHopOptions(options, checkoutRoot, runDir, sha, profile, runId, candidateId, timingRoot, baselineSha, selectionAttemptPath,
                    selectionAttemptDigest, selectionPath, selectionDigest, priorFailuresPath, priorFailuresDigest, rcEvidenceRoot);
                var cli = CliPath(bindings);
                if (string.IsNullOrWhiteSpace(cli)) throw new NightlyRefusal("nightly-tests-cli-unresolved");
                var testsLaunch = new NightlyLaunch("dotnet", new[] { "exec", cli, "nightly-tests" }.Concat(tests.ToArguments()).ToArray(), checkoutRoot);
                RunLine(host, "running antiphon-scripts nightly-tests");
                hopCommand = testsLaunch.CommandText;
                testExit = host.Launch(testsLaunch).ExitCode;

                var summaryPath = ReleaseGateCredit.Join(runDir, "summary.json");
                object summary = null;
                if (Exists(summaryPath))
                {
                    // The verdict fields only: a full run's summary is tens of MB, nearly all suite rows.
                    try { summary = NightlyJson.ReadFile(Path.GetFullPath(summaryPath), false, new[] { "suites", "dispatchProgress", "suiteExclusions" }); }
                    catch (Exception) { summary = null; }
                }
                if (summary != null)
                {
                    coverageComplete = PsValue.IsTrue(PsValue.Prop(summary, "coverageComplete"));
                    testsPassed = PsValue.IsTrue(PsValue.Prop(summary, "testsPassed"));
                    // Selection intent may be Full while host execution is diagnostic. Preserve the executor's scope and
                    // refusal reasons through the hop.
                    var scope = PsValue.Prop(summary, "coverageScope");
                    state["coverageScope"] = PsValue.IsTrue(scope) ? PsValue.Str(scope) : "Full";
                    state["platform"] = PsValue.Str(PsValue.Prop(summary, "platform"));
                    state["reasons"] = PsValue.AsArray(PsValue.Prop(summary, "reasons"));
                    var policyHash = PsValue.Prop(summary, "policyHash");
                    if (PsValue.IsTrue(policyHash)) state["policyHash"] = PsValue.Str(policyHash);
                }
                else
                {
                    coverageComplete = false;
                    testsPassed = testExit == 0;
                }
                // A nonzero test exit is the tests verdict. Rewrite a summary that still claims the tests passed so
                // summary.json and last-run.json agree.
                if (testExit != 0 && summary != null && PsValue.IsTrue(PsValue.Prop(summary, "testsPassed"))) SettleSummary(summaryPath);
                if (testExit != 0) testsPassed = false;

                reportExit = 0;
                if (!options.NoReport)
                {
                    phase = "report";
                    var previous = "";
                    if (Exists(lastGreen))
                    {
                        try
                        {
                            var green = JsonNode.Parse(File.ReadAllText(lastGreen, Encoding.UTF8)) as JsonObject;
                            var prior = Field(green, "summaryPath");
                            if (!string.IsNullOrEmpty(prior) && Exists(prior)) previous = prior;
                        }
                        catch (Exception) { }
                    }
                    RunLine(host, "running antiphon-scripts nightly-report");
                    try
                    {
                        var report = bindings.Report ?? (o => NightlyReport.Run(o, NightlyReportSeams.Production(host, host.Variables)).ExitCode);
                        reportExit = report(new NightlyReportOptions { Summary = summaryPath, PreviousSummary = previous });
                    }
                    catch (Exception e)
                    {
                        reportExit = 3;
                        RunLine(host, "REPORTING: " + e.Message);
                    }
                }

                reportDelivered = !options.NoReport && reportExit == 0;
                // -NoReport never claims delivery, whatever summary.json says (CARD-0545 D-10 no-report row).
                if (!options.NoReport && summary != null && PsValue.Prop(summary, "reportDelivered") != null)
                    reportDelivered = PsValue.IsTrue(PsValue.Prop(summary, "reportDelivered")) && reportExit == 0;

                exitCode = reportExit != 0 ? reportExit : testExit;

                state["phase"] = "final";
                state["testExit"] = testExit;
                state["reportExit"] = reportExit;
                state["exitCode"] = exitCode;
                state["coverageComplete"] = coverageComplete;
                state["testsPassed"] = testsPassed;
                state["reportDelivered"] = reportDelivered;
                state["succeeded"] = exitCode == 0 && coverageComplete && testsPassed;
                state["completedAt"] = Stamp(host.UtcNow);
                state["lastActivityAt"] = state["completedAt"];
                state["summaryPath"] = summaryPath;
                if (!WriteState(host, lastAttempt, state, true))
                {
                    if (exitCode == 0) exitCode = 1;
                }
                else
                {
                    // D-2: the state itself names the kind of credit it earns. Master green is the only thing that ever
                    // reaches <StateRoot>\last-complete-green.json; RC green lands in the candidate's own store and never
                    // touches master readiness.
                    rcEvidence.AssertInputs(rcEvidenceRoot, logRoot, checkoutRoot, sha);
                    var creditKind = ReleaseGateCredit.Verdict(View(state)).CreditKind ?? "";
                    result.CreditKind = creditKind;
                    state["creditKind"] = creditKind;
                    if (string.Equals(creditKind, "master-scheduled", StringComparison.OrdinalIgnoreCase)) WriteState(host, lastGreen, state, true);
                    else if (string.Equals(creditKind, "rc-release", StringComparison.OrdinalIgnoreCase))
                    {
                        var store = ReleaseGateCredit.CreditStore(creditKind, stateRoot, options.ReleaseRoot, candidateId);
                        Directory.CreateDirectory(store.Root);
                        WriteState(host, store.GreenPath, state, true);
                        result.GreenPath = store.GreenPath;
                    }
                }
            }
            catch (Exception e)
            {
                if (exitCode == 0) exitCode = 1;
                RunLine(host, e.Message);
                state = state ?? Map(("phase", phase));
                state["phase"] = phase;
                state["exitCode"] = exitCode;
                WriteHopFailure(host, state, lastAttempt, runDir, e.Message, hopCommand);
            }
            finally
            {
                // D-4: released only after this run's owned children are accounted for, which the tests hop guarantees by
                // waiting for its child before returning.
                nativeLock?.Exit(options.CoordinationRoot, runId);
                runLock?.Exit(stateRoot, runId);
                runLock?.Dispose();
            }

            result.ExitCode = exitCode;
            result.Phase = phase;
            result.Sha = sha;
            result.CoverageComplete = coverageComplete;
            result.TestsPassed = testsPassed;
            result.ReportDelivered = reportDelivered;
            result.CoverageScope = PsValue.Str(state["coverageScope"]);
            result.Platform = PsValue.Str(state["platform"]);
            result.Reasons = PsValue.AsArray(state["reasons"]);
            result.LogDir = runDir;
            if (PsValue.IsTrue(state["policyHash"])) result.PolicyHash = PsValue.Str(state["policyHash"]);
            if (PsValue.IsTrue(state["summaryPath"])) result.SummaryPath = PsValue.Str(state["summaryPath"]);
            result.TestExit = testExit;
            result.ReportExit = reportExit;
            return result;
        }

        /// <summary>
        /// ConvertTo-NightlyResultRecord (CARD-0545 D-10): the compact completion record Windmill takes as the job result, in
        /// its fixed key order. A refusal carries exitCode and refusal with empty identity; otherwise the durable last-run.json
        /// (written by this run or, after the self-update hop, by the child) is the source, so the line and the state file
        /// never disagree.
        /// </summary>
        public static OrderedDictionary ResultRecord(NightlyRunResult result)
        {
            var record = Map(
                ("nativeRunId", ""), ("sha", ""), ("ref", ""), ("trigger", ""), ("profile", ""), ("candidateId", ""), ("creditKind", ""), ("localDueDate", ""),
                ("policyHash", ""), ("coverageComplete", false), ("testsPassed", false), ("reportDelivered", false), ("exitCode", 1), ("summaryPath", ""));
            if (result == null) return record;
            record["exitCode"] = result.ExitCode;
            if (!string.IsNullOrWhiteSpace(result.Refusal))
            {
                record["refusal"] = result.Refusal;
                return record;
            }
            object last = null;
            if (!string.IsNullOrWhiteSpace(result.StateRoot))
            {
                var path = ReleaseGateCredit.Join(result.StateRoot, "last-run.json");
                if (Exists(path))
                {
                    try { last = NightlyJson.ParseText(File.ReadAllText(path, Encoding.UTF8), 1024); }
                    catch (Exception) { last = null; }
                }
            }
            string S(object o, string name) => PsValue.Str(PsValue.Prop(o, name));
            if (last != null && string.Equals(S(last, "runId"), result.RunId ?? "", StringComparison.OrdinalIgnoreCase))
            {
                record["nativeRunId"] = S(last, "runId");
                record["sha"] = S(last, "sha");
                record["ref"] = S(last, "ref");
                record["trigger"] = S(last, "trigger");
                record["profile"] = S(last, "profile");
                record["candidateId"] = S(last, "candidateId");
                record["creditKind"] = S(last, "creditKind");
                record["localDueDate"] = S(last, "localDueDate");
                record["policyHash"] = S(last, "policyHash");
                record["coverageComplete"] = PsValue.IsTrue(PsValue.Prop(last, "coverageComplete"));
                record["coverageScope"] = S(last, "coverageScope");
                record["platform"] = S(last, "platform");
                record["reasons"] = PsValue.AsArray(PsValue.Prop(last, "reasons"));
                record["testsPassed"] = PsValue.IsTrue(PsValue.Prop(last, "testsPassed"));
                record["reportDelivered"] = PsValue.IsTrue(PsValue.Prop(last, "reportDelivered"));
                record["summaryPath"] = S(last, "summaryPath");
                return record;
            }
            record["nativeRunId"] = result.RunId ?? "";
            record["sha"] = result.Sha ?? "";
            record["ref"] = result.Ref ?? "";
            record["trigger"] = result.Trigger ?? "";
            record["profile"] = result.Profile ?? "";
            record["candidateId"] = result.CandidateId ?? "";
            record["creditKind"] = result.CreditKind ?? "";
            record["localDueDate"] = result.LocalDueDate ?? "";
            record["policyHash"] = result.PolicyHash ?? "";
            record["coverageComplete"] = result.CoverageComplete;
            record["coverageScope"] = result.CoverageScope ?? "";
            record["platform"] = result.Platform ?? "";
            record["reasons"] = result.Reasons ?? new object[] { null };
            record["testsPassed"] = result.TestsPassed;
            record["reportDelivered"] = result.ReportDelivered;
            record["summaryPath"] = result.SummaryPath ?? "";
            return record;
        }

        /// <summary>The completion record as one compact JSON line (ConvertTo-Json -Compress).</summary>
        public static string RecordLine(NightlyRunResult result) => NightlyJson.Text(ResultRecord(result), 8);

        /// <summary>The result as JSON text (the record the launcher reads back from --result-file), indented.</summary>
        public static string ResultJson(NightlyRunResult result) =>
            NightlyJson.IndentedText((result ?? new NightlyRunResult()).ToRecord(), 16) ?? throw new NightlyRefusal("nightly-run-result-unwritable");

        /// <summary>
        /// Repair-NightlyCheckoutLineEndings: rewrites the tracked files whose working copy predates their eol attribute (a
        /// reused clone keeps CRLF bytes through fetch and reset --hard). A path git quoted, or one that is not a file here
        /// (a decoding that does not resolve), is skipped and counted, never handed to checkout-index. Silent when nothing
        /// is stale or skipped.
        /// </summary>
        public static void RepairLineEndings(NightlyHost host, string checkoutRoot)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var eol = host.RunGit(checkoutRoot, "-c", "core.quotePath=false", "ls-files", "--eol");
            if (eol.ExitCode != 0) throw new NightlyRefusal("git ls-files --eol failed with exit " + eol.ExitCode.ToString(CultureInfo.InvariantCulture));
            var skipped = 0;
            var stale = new List<string>();
            foreach (var path in NightlyRunParts.StaleEolPaths(eol.Output, ref skipped))
            {
                if (IsFile(ReleaseGateCredit.Join(checkoutRoot, path))) stale.Add(path);
                else skipped++;
            }
            if (stale.Count == 0 && skipped == 0) return;
            host.Out.WriteLine("nightly sync: rewriting " + stale.Count.ToString(CultureInfo.InvariantCulture) + " working file(s) whose line endings predate their eol attribute; skipped="
                + skipped.ToString(CultureInfo.InvariantCulture) + " path(s) (quoted or missing)");
            for (var i = 0; i < stale.Count; i += 100)
            {
                var batch = stale.Skip(i).Take(100).ToArray();
                foreach (var path in batch)
                {
                    var full = ReleaseGateCredit.Join(checkoutRoot, path);
                    if (!IsFile(full)) continue;
                    File.SetAttributes(full, FileAttributes.Normal);
                    File.Delete(full);
                }
                var write = host.RunGit(checkoutRoot, new[] { "checkout-index", "-f", "-u", "--" }.Concat(batch).ToArray());
                if (write.ExitCode != 0) throw new NightlyRefusal("git checkout-index for stale line endings failed with exit " + write.ExitCode.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// Write-NightlyHopFailure: a hop exception's text and command in the state (error, command; exitCode 1 when unset)
        /// and in &lt;logDir&gt;\hop-failure.txt (failureLog) when there is a log directory, then the state written to statePath.
        /// </summary>
        public static OrderedDictionary WriteHopFailure(NightlyHost host, OrderedDictionary state, string statePath, string logDir, string errorMessage, string command)
        {
            state = state ?? new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            if (state["exitCode"] == null) state["exitCode"] = 1;
            state["error"] = errorMessage ?? "";
            state["command"] = command ?? "";
            if (!string.IsNullOrWhiteSpace(logDir))
            {
                Directory.CreateDirectory(logDir);
                var failurePath = ReleaseGateCredit.Join(logDir, "hop-failure.txt");
                File.WriteAllText(failurePath, "error: " + errorMessage + "\ncommand: " + command + "\n");
                state["failureLog"] = failurePath;
            }
            if (!string.IsNullOrWhiteSpace(statePath)) WriteState(host, statePath, state, true);
            return state;
        }

        /// <summary>Write-NightlyRunState: the state written atomically; a failure is one run line and false (allowFailure) or thrown.</summary>
        public static bool WriteState(NightlyHost host, string path, object state, bool allowFailure)
        {
            try
            {
                NightlyTests.WriteAtomicJson(path, state);
                return true;
            }
            catch (Exception e) when (allowFailure)
            {
                if (host != null) RunLine(host, "state write failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// The self-update hop's pwsh argv (today's): the clone's nightly-run.ps1 with -NoSync, the run's roots, identity and
        /// continuation (this process and the shared lock's process start), the selection when one is bound, and every
        /// option that must survive the hop (D-2/D-3: profile, pinned sha and candidate identity).
        /// </summary>
        public static string[] SelfHopArguments(NightlyRunOptions options, string cloneScript, string checkoutRoot, string logRoot, string stateRoot, string profile,
            string runId, int pid, DateTime ownerStartedUtc, string candidateId, string timingRoot, string baselineSha, string selectionAttemptPath,
            string selectionAttemptDigest, string selectionPath, string selectionDigest, string priorFailuresPath, string priorFailuresDigest)
        {
            var attempt = options.Attempt.ToString(CultureInfo.InvariantCulture);
            var a = new List<string>
            {
                "-NoProfile", "-NonInteractive", "-File", cloneScript, "-NoSync", "-CheckoutRoot", checkoutRoot, "-LogRoot", logRoot, "-Ref", options.Ref ?? "",
                "-StateRoot", stateRoot, "-RemoteUrl", options.RemoteUrl ?? "", "-Trigger", options.Trigger ?? "", "-Profile", profile, "-RunId", runId,
                "-ContinueRunId", runId, "-ContinueParentPid", pid.ToString(CultureInfo.InvariantCulture),
                "-ContinueParentStartedAt", ownerStartedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            };
            if (!string.IsNullOrEmpty(selectionAttemptPath))
            {
                a.AddRange(new[]
                {
                    "-Repository", options.Repository ?? "", "-SelectionMode", options.SelectionMode ?? "", "-BaselineSha", baselineSha ?? "",
                    "-BaselineReceiptPath", options.BaselineReceiptPath ?? "", "-QualificationPath", options.QualificationPath ?? "", "-TimingRoot", timingRoot ?? "",
                    "-TrainManifestPath", options.TrainManifestPath ?? "", "-ReleaseIntentPath", options.ReleaseIntentPath ?? "",
                    "-SelectionAttemptPath", selectionAttemptPath, "-SelectionAttemptDigest", selectionAttemptDigest ?? "", "-SelectionPath", selectionPath ?? "",
                    "-SelectionDigest", selectionDigest ?? "", "-Attempt", attempt,
                });
            }
            if (options.NoReport) a.Add("-NoReport");
            if (!string.IsNullOrEmpty(priorFailuresPath)) a.AddRange(new[] { "-PriorFailuresPath", priorFailuresPath, "-PriorFailuresDigest", priorFailuresDigest ?? "" });
            if (options.AcceptCrossCandidateSeed) a.Add("-AcceptCrossCandidateSeed");
            if (!string.IsNullOrEmpty(options.SeedSourceCandidateId)) a.AddRange(new[] { "-SeedSourceCandidateId", options.SeedSourceCandidateId });
            if (!Has(a, "-Attempt")) a.AddRange(new[] { "-Attempt", attempt });
            if (!string.IsNullOrWhiteSpace(options.ExpectedSha)) a.AddRange(new[] { "-ExpectedSha", options.ExpectedSha });
            if (!string.IsNullOrWhiteSpace(candidateId)) a.AddRange(new[] { "-CandidateId", candidateId });
            if (!string.IsNullOrWhiteSpace(options.ReleaseRoot)) a.AddRange(new[] { "-ReleaseRoot", options.ReleaseRoot });
            if (!string.IsNullOrWhiteSpace(options.CoordinationRoot)) a.AddRange(new[] { "-CoordinationRoot", options.CoordinationRoot });
            if (AnySuite(options.Suites)) a.AddRange(new[] { "-Suites", string.Join(",", options.Suites) });
            if (!string.IsNullOrWhiteSpace(timingRoot) && !Has(a, "-TimingRoot")) a.AddRange(new[] { "-TimingRoot", timingRoot });
            return a.ToArray();
        }

        /// <summary>
        /// The tests hop's options (New-NightlyTestsHopArgumentList's values): the checkout, the run directory, the head and
        /// identity; the whole selection when one is bound (else the timing root); the pin, the suites, the evidence root and
        /// the failed-first input when given. No seams.
        /// </summary>
        public static NightlyTestsOptions TestsHopOptions(NightlyRunOptions options, string checkoutRoot, string runDir, string sha, string profile, string runId,
            string candidateId, string timingRoot, string baselineSha, string selectionAttemptPath, string selectionAttemptDigest, string selectionPath,
            string selectionDigest, string priorFailuresPath, string priorFailuresDigest, string rcEvidenceRoot)
        {
            var t = new NightlyTestsOptions
            {
                RepoRoot = checkoutRoot, LogRoot = runDir, Sha = sha ?? "", GitRef = "origin/" + options.Ref, Trigger = options.Trigger ?? "", Profile = profile,
                RunId = runId, Attempt = options.Attempt,
            };
            if (!string.IsNullOrEmpty(selectionAttemptPath))
            {
                t.Repository = options.Repository ?? "";
                t.SelectionMode = options.SelectionMode ?? "";
                t.BaselineSha = baselineSha ?? "";
                t.BaselineReceiptPath = options.BaselineReceiptPath ?? "";
                t.QualificationPath = options.QualificationPath ?? "";
                t.TimingRoot = timingRoot ?? "";
                t.TrainManifestPath = options.TrainManifestPath ?? "";
                t.ReleaseIntentPath = options.ReleaseIntentPath ?? "";
                t.SelectionAttemptPath = selectionAttemptPath;
                t.SelectionAttemptDigest = selectionAttemptDigest ?? "";
                t.SelectionPath = selectionPath ?? "";
                t.SelectionDigest = selectionDigest ?? "";
                t.ReleaseRoot = options.ReleaseRoot ?? "";
                t.CandidateId = candidateId ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(timingRoot)) t.TimingRoot = timingRoot;
            if (!string.IsNullOrWhiteSpace(options.ExpectedSha)) t.ExpectedSha = options.ExpectedSha;
            if (AnySuite(options.Suites)) t.Suites = options.Suites;
            if (!string.IsNullOrWhiteSpace(rcEvidenceRoot)) t.RcEvidenceRoot = rcEvidenceRoot;
            if (!string.IsNullOrEmpty(priorFailuresPath))
            {
                t.PriorFailuresPath = priorFailuresPath;
                t.PriorFailuresDigest = priorFailuresDigest ?? "";
            }
            t.AcceptCrossCandidateSeed = options.AcceptCrossCandidateSeed;
            if (!string.IsNullOrEmpty(options.SeedSourceCandidateId)) t.SeedSourceCandidateId = options.SeedSourceCandidateId;
            return t;
        }

        // ---- the selection attempt ----

        /// <summary>
        /// New-ReleaseSelectionAttempt's process protocol: the request as JSON, then `dotnet exec &lt;cli&gt; select-release
        /// &lt;request&gt; &lt;response&gt; &lt;scripts lib&gt;` (exit 0: answered) and otherwise release-selection-worker.ps1 under pwsh
        /// (each at most 60 s); the response's value, or selection-refused:&lt;error&gt;. A selection mode other than Full needs
        /// pwsh (selection-pwsh-required), and so does an attempt the compiled command hands back.
        /// </summary>
        public static Func<IDictionary, NightlySelectionBinding> SelectionWorker(string cliPath, string scriptsLib, string pathValue) => request =>
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var pwsh = ReleaseSelectionAttempt.AssertCapability(PsValue.Str(request["SelectionMode"]), pathValue);
            var requestPath = Path.GetTempFileName();
            var responsePath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(requestPath, NightlyJson.Text(request, 20) ?? throw new NightlyRefusal("selection-request-unwritable"), new UTF8Encoding(false));
                var handled = false;
                if (!string.IsNullOrWhiteSpace(cliPath) && File.Exists(cliPath))
                {
                    var (code, timedOut) = Quiet("dotnet", new[] { "exec", cliPath, "select-release", requestPath, responsePath, scriptsLib ?? "" }, 60000);
                    handled = !timedOut && code == 0;
                }
                if (!handled)
                {
                    if (pwsh == null) throw new NightlyRefusal("selection-pwsh-required");
                    var worker = Path.Combine(scriptsLib ?? "", "release-selection-worker.ps1");
                    var (_, timedOut) = Quiet(pwsh, new[] { "-NoProfile", "-NonInteractive", "-File", worker, "-RequestPath", requestPath, "-ResponsePath", responsePath }, 60000);
                    if (timedOut) throw new NightlyRefusal("selection-worker-timeout");
                }
                JsonObject response = null;
                try { response = JsonNode.Parse(File.ReadAllText(responsePath, Encoding.UTF8)) as JsonObject; }
                catch (JsonException) { }
                var ok = response != null && FieldNode(response, "ok") is JsonValue flag && flag.TryGetValue<bool>(out var yes) && yes;
                if (!ok) throw new NightlyRefusal("selection-refused:" + (response == null ? "" : Field(response, "error")));
                var value = FieldNode(response, "value") as JsonObject;
                return new NightlySelectionBinding
                {
                    SelectionAttemptPath = Field(value, "SelectionAttemptPath"), SelectionAttemptDigest = Field(value, "SelectionAttemptDigest"),
                    SelectionPath = Field(value, "SelectionPath"), SelectionDigest = Field(value, "SelectionDigest"), BaselineSha = Field(value, "BaselineSha"),
                };
            }
            finally
            {
                TryDelete(requestPath);
                TryDelete(responsePath);
            }
        };

        // ---- ownership and reparse (nightly-common.ps1) ----

        /// <summary>
        /// Resolve-NightlyReparsePath: follows the path's own reparse points (at most 32 hops) to the first entry that is
        /// not one, or to a path that does not exist; a link without a target refuses "unresolved reparse at", a loop
        /// "reparse loop resolving".
        /// </summary>
        public static string ResolveReparse(string path, int maxHops = 32)
        {
            var current = NightlyPaths.Canonical(path);
            for (var guard = 0; ; guard++)
            {
                if (guard >= maxHops) throw new NightlyRefusal("reparse loop resolving " + path);
                if (!Exists(current)) return current;
                FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if ((item.Attributes & FileAttributes.ReparsePoint) == 0) return NightlyPaths.Canonical(item.FullName);
                var target = item.LinkTarget;
                if (string.IsNullOrWhiteSpace(target)) throw new NightlyRefusal("unresolved reparse at " + current);
                if (!Path.IsPathRooted(target)) target = Path.Combine(Path.GetDirectoryName(item.FullName) ?? "", target);
                current = NightlyPaths.Canonical(target);
            }
        }

        /// <summary>
        /// ConvertTo-NightlyResolvedPath: the canonical path with its reparse points resolved, refused when a reparse
        /// ancestor remains after the resolve or a second resolve answers otherwise (a swap before use).
        /// </summary>
        public static string ResolvedPath(string path)
        {
            var canonical = NightlyPaths.Canonical(path);
            var resolved = ResolveReparse(canonical);
            if (NightlyPaths.HasReparseAncestor(resolved)) throw new NightlyRefusal("reparse ancestor remains after resolve: " + path);
            var recheck = ResolveReparse(canonical);
            if (!string.Equals(resolved, recheck, StringComparison.OrdinalIgnoreCase)) throw new NightlyRefusal("reparse swapped before use: " + path);
            return resolved;
        }

        /// <summary>Test-NightlyGitWorktree: the clone's .git is a file (a gitdir pointer: a linked worktree or a submodule), not a directory.</summary>
        public static bool IsGitWorktree(string path)
        {
            var git = ReleaseGateCredit.Join(path, ".git");
            if (!Exists(git)) return false;
            // The PowerShell read the gitdir line and answered true on every branch: any .git file is not an independent clone.
            return !Directory.Exists(git);
        }

        /// <summary>
        /// Get-NightlyOriginUrl: remote.origin.url of the clone's own .git/config as git decodes it (--file, no includes, no
        /// global configuration); "" when there is no config or no origin. Not the run's git seam: this reads the file.
        /// </summary>
        public static string OriginUrl(string clonePath)
        {
            var config = ReleaseGateCredit.Join(clonePath, ReleaseGateCredit.Join(".git", "config"));
            if (!Exists(config)) return "";
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in NightlyNativeGit.Options.Concat(new[] { "config", "--file", config, "--no-includes", "--get", "remote.origin.url" }))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = errors.Result;
            if (process.ExitCode != 0) return "";
            var lines = output.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines);
        }

        /// <summary>Test-NightlyPathContained: inner is outer or lies under it (canonical paths, case-insensitive).</summary>
        public static bool PathContained(string inner, string outer)
        {
            var a = NightlyPaths.Canonical(inner);
            var b = NightlyPaths.Canonical(outer);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Test-NightlyRunContextOwnership: the clone resolves without a reparse refusal, is not the shared tree or a linked
        /// worktree, carries .antiphon-nightly-owned, has the expected origin, and neither the state nor the log root lies
        /// inside it.
        /// </summary>
        public static NightlyRunOwnership RunContextOwnership(string clonePath, string stateRoot, string logRoot, string expectedOrigin = NightlyRunOptions.DefaultRemoteUrl)
        {
            string clone;
            try { clone = ResolvedPath(clonePath); }
            catch (Exception e) { return new NightlyRunOwnership(false, e.Message, "reparse", clonePath); }
            if (NightlyPaths.IsSharedTree(clone)) return new NightlyRunOwnership(false, "shared-tree", "clone", clonePath);
            if (IsGitWorktree(clone)) return new NightlyRunOwnership(false, "linked-worktree", "clone", clonePath);
            if (!Exists(ReleaseGateCredit.Join(clone, ".antiphon-nightly-owned"))) return new NightlyRunOwnership(false, "unmarked-clone", "ownership", clonePath);
            if (!string.Equals(OriginUrl(clone), expectedOrigin ?? "", StringComparison.OrdinalIgnoreCase)) return new NightlyRunOwnership(false, "wrong-origin", "origin", clonePath);
            if (!string.IsNullOrWhiteSpace(stateRoot) && PathContained(NightlyPaths.Canonical(stateRoot), clone))
                return new NightlyRunOwnership(false, "state-overlaps-clone", "state", clonePath);
            if (!string.IsNullOrWhiteSpace(logRoot) && PathContained(NightlyPaths.Canonical(logRoot), clone))
                return new NightlyRunOwnership(false, "log-overlaps-clone", "log", clonePath);
            return new NightlyRunOwnership(true, "", "", clone);
        }

        // ---- helpers ----

        // Write-NightlyRunLine: "[<UtcNow round-trip>] <message>".
        private static void RunLine(NightlyHost host, string message) => NightlyTests.RunLine(host, message);

        // Write-Warning -WarningAction Continue.
        private static void Warn(NightlyHost host, string text) => NightlyTests.Warn(host, text);

        private static string Stamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

        // An [ordered] hashtable: insertion order, names compared ignoring case.
        private static OrderedDictionary Map(params (string Name, object Value)[] entries)
        {
            var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in entries) map[name] = value;
            return map;
        }

        // The state as the credit decision reads plain values.
        private static IReadOnlyDictionary<string, object> View(OrderedDictionary state)
        {
            var view = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in state) view[PsValue.Str(entry.Key)] = entry.Value;
            return view;
        }

        private static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private static bool IsFile(string path)
        {
            try { return File.Exists(path); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException) { return false; }
        }

        private static bool Has(List<string> list, string value) => list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

        // if ($Suites -and $Suites.Count -gt 0): a one-element list is only as true as its element.
        private static bool AnySuite(string[] suites) => suites != null && (suites.Length > 1 || (suites.Length == 1 && !string.IsNullOrEmpty(suites[0])));

        // The product root and the checkout name one directory (case-insensitive on Windows); an unknown product root never does.
        private static bool SameRoot(string productRoot, string checkoutRoot)
        {
            if (string.IsNullOrWhiteSpace(productRoot) || string.IsNullOrWhiteSpace(checkoutRoot)) return false;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(NightlyPaths.Canonical(productRoot), NightlyPaths.Canonical(checkoutRoot), comparison);
        }

        private static string CliPath(NightlyRunBindings bindings)
        {
            if (!string.IsNullOrWhiteSpace(bindings.CliPath)) return bindings.CliPath;
            return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        }

        // One git call that must succeed: a nonzero exit throws the message with its code.
        private static void Git(NightlyHost host, string workingDirectory, string failure, params string[] arguments)
        {
            var run = host.RunGit(workingDirectory, arguments);
            if (run.ExitCode != 0) throw new NightlyRefusal(string.Format(CultureInfo.InvariantCulture, failure, run.ExitCode));
        }

        // The summary rewrite of a nonzero test exit: testsPassed false (and succeeded, when present), every other field kept.
        private static void SettleSummary(string summaryPath)
        {
            var root = JsonNode.Parse(File.ReadAllText(summaryPath, Encoding.UTF8)) as JsonObject ?? throw new NightlyRefusal("summary-not-object " + summaryPath);
            void Set(string name, bool required)
            {
                var key = root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (key != null) root[key] = false;
                else if (required) root[name] = false;
            }
            Set("testsPassed", true);
            Set("succeeded", false);
            var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var temporary = summaryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                File.Move(temporary, summaryPath, true);
            }
            finally { TryDelete(temporary); }
        }

        private static JsonNode FieldNode(JsonObject obj, string name)
        {
            if (obj == null) return null;
            foreach (var pair in obj)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        // [string]$obj.name of a JSON object read with ConvertFrom-Json: "" for absent or null.
        private static string Field(JsonObject obj, string name)
        {
            var node = FieldNode(obj, name);
            if (node == null) return "";
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return node.ToJsonString();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        // A child with its output captured and discarded (CheckpointProcessRunner.Run without a sink): exit and timed out.
        private static (int ExitCode, bool TimedOut) Quiet(string fileName, IEnumerable<string> arguments, int timeoutMilliseconds)
        {
            var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument ?? "");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("the child did not start: " + fileName);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                process.WaitForExit();
                return (1, true);
            }
            process.WaitForExit();
            _ = stdout.Result;
            _ = stderr.Result;
            return (process.ExitCode, false);
        }
    }
}
