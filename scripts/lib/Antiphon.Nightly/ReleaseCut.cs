// CARD-1311 (with CARD-0599 D-3, D-5 and D-8, and CARD-1313): the RC lane entry, `antiphon-scripts release-cut --ref
// rc/YYYY-MM-DD [...]`. It replaced the body of scripts/release-cut.ps1 with unit 15 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md. Windmill (u/lndcobra/antiphon_release_candidates) starts
// that script by path, so it stays as a thin launcher: its parameters, as kebab-case flags, are this command's options.
//
// 1. The candidate (ReleaseCandidate.Run) journals the existing remote tip of the long-lived rc/YYYY-MM-DD ref. A blank ref,
//    a legacy release/rc-<stamp> ref and any other shape refuse before anything is read. It does not cut origin/master and
//    does not allocate a stamp.
// 2. Unless WhatIf, the selection attempt (ReleaseSelectionAttempt.Create) is sealed for the run in the candidate checkout,
//    after the pinned SHA is fetched there (the candidate journalling only read the remote ref). A sealed attempt's timing
//    root is kept, a blank one included; a different explicit one is selection-attempt-conflict. A new attempt with a blank
//    timing root gets <candidate root>\logs\timing-<run> (NightlyPaths.TimingRoot). Any failure here refuses with its
//    message (exit 3).
// 3. The rc run (NightlyRun.Run in this process: -Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>) runs against
//    the candidate checkout and the candidate's private state root. The script ran scripts/nightly-run.ps1 -PassThru, which
//    launched the same NightlyRun in a child antiphon-scripts process; a run that throws is nightly-run-failed (exit 1), as
//    that child's command answered it.
// 4. Only an RC complete-green (the run's exit 0) publishes (ReleasePublish.Run); SkipPublish and WhatIf stop before it.
//
// A red or incomplete RC publishes nothing and keeps its evidence. Nothing here moves a tested ref, retries a failed run in
// place, writes the master attempt, green, monitor or qualification receipt, or restarts AppHost or the runner.
//
// Everything outside the process goes through ReleaseCutHost: the output and error lines, the clock and git (the pinned
// object's fetch) of its NightlyHost, the candidate's remote, the files, the run and the publisher's host. The record
// (ReleaseCutResult.ToJson: candidateId, ref, sha, scheduleSlot, windmillJobId, tested, published, tag, refusal, exitCode,
// then selection once an attempt is sealed) is the command's last stdout line. Exit: the record's exitCode - 0 published,
// or green with SkipPublish or WhatIf; 3 refused; otherwise the candidate's, the run's or the publisher's. Plain C#. ASCII
// only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The release-cut command's options (release-cut.ps1's parameters).</summary>
    public sealed class ReleaseCutOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "release-cut --ref <rc/YYYY-MM-DD> [--release-root <dir>] [--coordination-root <dir>] [--remote-url <url>]"
            + " [--repository <owner/name>] [--run-id <id>] [--schedule-slot <slot>] [--job-id <id>] [--selection-mode <Full|Shadow|ChangeAware>]"
            + " [--baseline-sha <sha>] [--baseline-receipt-path <file>] [--qualification-path <file>] [--timing-root <dir>] [--train-manifest-path <file>]"
            + " [--release-intent-path <file>] [--selection-attempt-path <file>] [--selection-attempt-digest <sha256>] [--selection-path <file>]"
            + " [--selection-digest <sha256>] [--attempt <n>] [--skip-publish] [--what-if] [--result-file <json>]";

        /// <summary>The shared native-run lock's root (release-cut.ps1's -CoordinationRoot default).</summary>
        public const string DefaultCoordinationRoot = ReleaseGatePublication.DefaultCoordinationRoot;

        private static readonly string[] Valued =
        {
            "--release-root", "--coordination-root", "--remote-url", "--repository", "--run-id", "--schedule-slot", "--job-id", "--ref",
            "--selection-mode", "--baseline-sha", "--baseline-receipt-path", "--qualification-path", "--timing-root", "--train-manifest-path",
            "--release-intent-path", "--selection-attempt-path", "--selection-attempt-digest", "--selection-path", "--selection-digest", "--attempt",
            "--result-file",
        };

        private static readonly string[] Switches = { "--skip-publish", "--what-if" };

        /// <summary>Producer-owned release state; the candidate checkout is &lt;ReleaseRoot&gt;\checkout. Blank is the default.</summary>
        public string ReleaseRoot { get; set; } = ReleaseGateCredit.DefaultReleaseRoot;

        /// <summary>The root of the shared native-run lock both lanes acquire.</summary>
        public string CoordinationRoot { get; set; } = DefaultCoordinationRoot;

        /// <summary>The origin a missing candidate clone is cloned from, and the run's proven origin.</summary>
        public string RemoteUrl { get; set; } = NightlyRunOptions.DefaultRemoteUrl;

        /// <summary>The publication destination (owner/name), also the selection attempt's.</summary>
        public string Repository { get; set; } = ReleasePublishOptions.DefaultRepository;

        /// <summary>The run's id; blank is a new one.</summary>
        public string RunId { get; set; } = "";

        /// <summary>The Windmill schedule slot, journalled and recorded as is.</summary>
        public string ScheduleSlot { get; set; } = "";

        /// <summary>The Windmill job id, recorded as is (windmillJobId).</summary>
        public string JobId { get; set; } = "";

        /// <summary>rc/YYYY-MM-DD (refs/heads/ accepted); anything else refuses.</summary>
        public string Ref { get; set; } = "";

        /// <summary>Full, Shadow or ChangeAware (intent only: every admitted attempt executes Full).</summary>
        public string SelectionMode { get; set; } = "Full";

        public string BaselineSha { get; set; } = "";
        public string BaselineReceiptPath { get; set; } = "";
        public string QualificationPath { get; set; } = "";

        /// <summary>The run's timing root; blank keeps a sealed attempt's, else &lt;candidate root&gt;\logs\timing-&lt;run&gt;.</summary>
        public string TimingRoot { get; set; } = "";

        public string TrainManifestPath { get; set; } = "";
        public string ReleaseIntentPath { get; set; } = "";
        public string SelectionAttemptPath { get; set; } = "";
        public string SelectionAttemptDigest { get; set; } = "";
        public string SelectionPath { get; set; } = "";
        public string SelectionDigest { get; set; } = "";
        public int Attempt { get; set; } = 1;

        /// <summary>A green run ends with exit 0 and publishes nothing.</summary>
        public bool SkipPublish { get; set; }

        /// <summary>The candidate journals nothing, no attempt is sealed, the run is a WhatIf run, nothing publishes.</summary>
        public bool WhatIf { get; set; }

        /// <summary>The release card's Antiphon API for the publisher (ANTIPHON_API, read by the caller); blank is its default.</summary>
        public string AntiphonApiUrl { get; set; } = "";

        /// <summary>Explicit test mode for the publisher (ANTIPHON_RELEASE_GATE_TEST_MODE exactly '1', read by the caller).</summary>
        public bool TestMode { get; set; }

        /// <summary>The scripts directory whose pinned scripts and source trees the publisher's authority binds.</summary>
        public string ScriptsRoot { get; set; } = "";

        /// <summary>Where the command writes the record as well (the launcher's -PassThru); blank: nowhere. ReleaseCut.Run does not read it.</summary>
        public string ResultFile { get; set; } = "";

        /// <summary>
        /// The arguments after the command name (NightlyArgs: --kebab value flags and bare switches; an absent flag is its
        /// default). --selection-mode takes Full, Shadow or ChangeAware (any case, kept in that spelling); --attempt an
        /// integer. scriptsRoot is the checkout's scripts directory; variables reads ANTIPHON_API and
        /// ANTIPHON_RELEASE_GATE_TEST_MODE for the caller. NightlyUsageException for a malformed line.
        /// </summary>
        public static ReleaseCutOptions Parse(IReadOnlyList<string> arguments, string scriptsRoot, Func<string, string> variables)
        {
            variables ??= _ => null;
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var o = new ReleaseCutOptions();
            string Text(string flag, string fallback) => flags.Value(flag, fallback) ?? fallback;
            o.ReleaseRoot = Text("--release-root", o.ReleaseRoot);
            o.CoordinationRoot = Text("--coordination-root", o.CoordinationRoot);
            o.RemoteUrl = Text("--remote-url", o.RemoteUrl);
            o.Repository = Text("--repository", o.Repository);
            o.RunId = Text("--run-id", o.RunId);
            o.ScheduleSlot = Text("--schedule-slot", o.ScheduleSlot);
            o.JobId = Text("--job-id", o.JobId);
            o.Ref = Text("--ref", o.Ref);
            if (flags.Has("--selection-mode"))
            {
                var mode = flags.Value("--selection-mode") ?? "";
                o.SelectionMode = NightlyTestsOptions.SelectionModes.FirstOrDefault(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NightlyUsageException("--selection-mode expects one of " + string.Join(", ", NightlyTestsOptions.SelectionModes) + ", not '" + mode + "'");
            }
            o.BaselineSha = Text("--baseline-sha", o.BaselineSha);
            o.BaselineReceiptPath = Text("--baseline-receipt-path", o.BaselineReceiptPath);
            o.QualificationPath = Text("--qualification-path", o.QualificationPath);
            o.TimingRoot = Text("--timing-root", o.TimingRoot);
            o.TrainManifestPath = Text("--train-manifest-path", o.TrainManifestPath);
            o.ReleaseIntentPath = Text("--release-intent-path", o.ReleaseIntentPath);
            o.SelectionAttemptPath = Text("--selection-attempt-path", o.SelectionAttemptPath);
            o.SelectionAttemptDigest = Text("--selection-attempt-digest", o.SelectionAttemptDigest);
            o.SelectionPath = Text("--selection-path", o.SelectionPath);
            o.SelectionDigest = Text("--selection-digest", o.SelectionDigest);
            o.Attempt = flags.Int("--attempt", o.Attempt);
            o.SkipPublish = flags.Switch("--skip-publish");
            o.WhatIf = flags.Switch("--what-if");
            o.ResultFile = Text("--result-file", o.ResultFile);
            o.AntiphonApiUrl = variables("ANTIPHON_API") ?? "";
            o.TestMode = ReleaseGateRemote.TestMode(variables(ReleaseGateRemote.TestModeVariable));
            o.ScriptsRoot = scriptsRoot ?? "";
            return o;
        }
    }

    /// <summary>One cut's record (release-cut.ps1's record, its -PassThru object and its last stdout line).</summary>
    public sealed class ReleaseCutResult
    {
        public string CandidateId { get; set; } = "";
        public string Ref { get; set; } = "";
        public string Sha { get; set; } = "";
        public string ScheduleSlot { get; set; } = "";
        public string WindmillJobId { get; set; } = "";

        /// <summary>The rc run completed green (exit 0).</summary>
        public bool Tested { get; set; }

        public bool Published { get; set; }
        public string Tag { get; set; } = "";
        public string Refusal { get; set; } = "";
        public int ExitCode { get; set; } = 1;

        /// <summary>The sealed selection attempt document (null until one is sealed; WhatIf seals none).</summary>
        public object Selection { get; set; }

        /// <summary>CARD-1836: Incremental when an Incremental request was asked for (not part of the record).</summary>
        public string RequestedMode { get; set; } = "";

        /// <summary>CARD-1836: Incremental when admitted, otherwise Full (not part of the record).</summary>
        public string CoverageScope { get; set; } = "Full";

        /// <summary>CARD-1836: why an Incremental request was not admitted, blank otherwise (not part of the record).</summary>
        public string IncrementalReason { get; set; } = "";

        /// <summary>The record in the script's key order; selection only once an attempt is sealed.</summary>
        public OrderedDictionary Record()
        {
            var record = new OrderedDictionary(StringComparer.Ordinal)
            {
                { "candidateId", CandidateId ?? "" }, { "ref", Ref ?? "" }, { "sha", Sha ?? "" }, { "scheduleSlot", ScheduleSlot ?? "" },
                { "windmillJobId", WindmillJobId ?? "" }, { "tested", Tested }, { "published", Published }, { "tag", Tag ?? "" },
                { "refusal", Refusal ?? "" }, { "exitCode", ExitCode },
            };
            if (Selection != null) record["selection"] = Selection;
            return record;
        }

        /// <summary>The record as one compact JSON line (the whole selection document, never cut at a depth).</summary>
        public string ToJson() => NightlyJson.Text(Record(), 64) ?? throw new NightlyRefusal("release-cut-record-unwritable");
    }

    /// <summary>The release cut's host: every observation and collaborator outside the cut's own decisions.</summary>
    public sealed class ReleaseCutHost
    {
        /// <summary>
        /// The run's host: the output ([release-cut], [release-candidate] and the run's lines) and error, the clock (the
        /// candidate's createdAt) and git (the pinned object's fetch in the candidate checkout).
        /// </summary>
        public required NightlyHost Nightly { get; init; }

        /// <summary>The candidate journal, its marker and directories, the candidate's state root and a sealed attempt's read.</summary>
        public required IFileSystem Files { get; init; }

        /// <summary>The candidate's remote git (clone, ls-remote, fetch, rev-parse, push).</summary>
        public required ReleaseGateRemote CandidateRemote { get; init; }

        /// <summary>The rc run (its options -> its result).</summary>
        public required Func<NightlyRunOptions, NightlyRunResult> Run { get; init; }

        /// <summary>The publisher's host.</summary>
        public required ReleaseGateHost Publisher { get; init; }

        /// <summary>
        /// The command's host: the disk, production git for the candidate's remote, NightlyRun.Run on nightly with
        /// NightlyRunBindings.Production (cliPath: the antiphon-scripts dll the tests hop runs) and the production
        /// release-gate host on nightly's output.
        /// </summary>
        public static ReleaseCutHost Production(NightlyHost nightly, string cliPath)
        {
            if (nightly == null) throw new ArgumentNullException(nameof(nightly));
            return new ReleaseCutHost
            {
                Nightly = nightly,
                Files = new FileSystem(),
                CandidateRemote = ReleaseGateRemote.Production(),
                Run = options => NightlyRun.Run(options, nightly, NightlyRunBindings.Production(nightly, cliPath ?? "")),
                Publisher = ReleaseGateHost.Production(nightly.Out),
            };
        }
    }

    /// <summary>CARD-1311: the RC lane - journal the candidate, run the rc profile on it, publish only on complete-green.</summary>
    public static class ReleaseCut
    {
        /// <summary>The prefix of every progress line (Write-CutLine).</summary>
        public const string LinePrefix = "[release-cut] ";

        /// <summary>
        /// One cut of the options' ref on the host. A refusal, a red run and a publication are all the record; a candidate
        /// journal or a state root that cannot be written throws (IOException, UnauthorizedAccessException).
        /// </summary>
        public static ReleaseCutResult Run(ReleaseCutOptions options, ReleaseCutHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var output = host.Nightly.Out;
            void Say(string message) => output.WriteLine(LinePrefix + message);
            var result = new ReleaseCutResult { ScheduleSlot = options.ScheduleSlot ?? "", WindmillJobId = options.JobId ?? "" };
            var releaseRoot = string.IsNullOrWhiteSpace(options.ReleaseRoot) ? ReleaseGateCredit.DefaultReleaseRoot : options.ReleaseRoot;
            var checkout = ReleaseGateCredit.Join(releaseRoot, "checkout");

            // Only a long-lived rc/YYYY-MM-DD: a legacy stamp is the release-candidate command's, never this lane's.
            var wanted = ReleaseGateCredit.Candidate(options.Ref, "");
            if (string.IsNullOrWhiteSpace(options.Ref) || !wanted.Ok || wanted.Kind != "long-lived")
            {
                result.Refusal = string.IsNullOrWhiteSpace(options.Ref) || wanted.Ok ? "rc-ref-required" : wanted.Reason;
                result.ExitCode = 3;
                Say("candidate cut refused (" + result.Refusal + ").");
                return result;
            }

            var cut = ReleaseCandidate.Run(new ReleaseCandidateOptions
            {
                ReleaseRoot = releaseRoot, CheckoutRoot = checkout, RemoteUrl = options.RemoteUrl ?? "", Ref = options.Ref, ScheduleSlot = options.ScheduleSlot ?? "",
                WhatIf = options.WhatIf,
            }, host.Nightly, host.Files, host.CandidateRemote);
            result.CandidateId = cut.CandidateId ?? "";
            result.Ref = cut.Ref ?? "";
            result.Sha = cut.Sha ?? "";
            if (cut.ExitCode != 0)
            {
                result.Refusal = cut.Refusal ?? "";
                result.ExitCode = cut.ExitCode;
                Say("candidate cut refused (" + result.Refusal + ").");
                return result;
            }

            var candidateRoot = ReleaseGateCredit.CandidateRoot(releaseRoot, result.CandidateId);
            var logRoot = ReleaseGateCredit.Join(candidateRoot, "logs");
            var stateRoot = ReleaseGateCredit.Join(candidateRoot, "state");
            host.Files.Directory.CreateDirectory(stateRoot);
            var runId = string.IsNullOrWhiteSpace(options.RunId) ? Guid.NewGuid().ToString("N") : options.RunId;

            // CARD-1836: an Incremental request is admitted or falls back to Full here; either way it stays qualification-only.
            var requestedIncremental = string.Equals(options.SelectionMode, RcIncrementalCut.RequestedMode, StringComparison.OrdinalIgnoreCase);
            var effectiveMode = requestedIncremental ? "Full" : options.SelectionMode ?? "";
            RcIncrementalAdmission admission = null;
            if (requestedIncremental) result.RequestedMode = RcIncrementalCut.RequestedMode;

            var timingRoot = options.TimingRoot ?? "";
            var baselineSha = options.BaselineSha ?? "";
            var attemptPath = options.SelectionAttemptPath ?? "";
            var attemptDigest = options.SelectionAttemptDigest ?? "";
            var selectionPath = options.SelectionPath ?? "";
            var selectionDigest = options.SelectionDigest ?? "";
            if (!options.WhatIf)
            {
                try
                {
                    var attemptFile = ReleaseGateCredit.Join(ReleaseGateCredit.Join(ReleaseGateCredit.Join(candidateRoot, "attempts"), runId), "selection-attempt.json");
                    if (host.Files.File.Exists(attemptFile))
                    {
                        // A replay keeps the sealed timing root, a blank one included; a different explicit root conflicts.
                        var sealedTiming = JsonNode.Parse(host.Files.File.ReadAllText(attemptFile, Encoding.UTF8)) is JsonObject attempt
                            && attempt.TryGetPropertyValue("timingRoot", out var sealedValue) ? ReleaseGateJournal.Str(sealedValue) : "";
                        if (!string.IsNullOrWhiteSpace(timingRoot) && !string.Equals(timingRoot, sealedTiming, StringComparison.Ordinal))
                            throw new NightlyRefusal("selection-attempt-conflict");
                        timingRoot = sealedTiming;
                    }
                    else if (string.IsNullOrWhiteSpace(timingRoot))
                    {
                        timingRoot = NightlyPaths.TimingRoot("", logRoot, runId);
                    }
                    // Journalling read the remote ref only: acquire its pinned object before selection, without changing the
                    // long-lived ref or repinning the journal.
                    if (host.Nightly.RunGit(checkout, "fetch", "origin", result.Sha).ExitCode != 0) throw new NightlyRefusal("selection-candidate-fetch");
                    if (requestedIncremental)
                    {
                        // The trust policy is read from the candidate's own blob; the approval must name its digest.
                        var policy = TestSelectionGit.Run(checkout, new[] { "cat-file", "blob", result.Sha + ":" + RcTrustedProjectGate.PolicyPath }, new[] { 0, 128 });
                        admission = RcIncrementalCut.Admit(releaseRoot, result.Sha, policy.Exit == 0 ? policy.Stdout : null, host.Nightly.UtcNow, checkout);
                        result.CoverageScope = admission.Admitted ? "Incremental" : "Full";
                        result.IncrementalReason = admission.Reason;
                        Say(admission.Admitted ? "incremental qualification admitted (qualification-only; Full remains the publication path)."
                            : "incremental request runs Full (" + admission.Reason + ").");
                    }
                    var bound = ReleaseSelectionAttempt.Create(new ReleaseSelectionRequest
                    {
                        RepositoryRoot = checkout, Repository = options.Repository ?? "", ReleaseRoot = releaseRoot, CandidateSha = result.Sha,
                        CandidateId = result.CandidateId, CandidateRef = result.Ref, Profile = "rc", RunId = runId, SelectionMode = effectiveMode,
                        BaselineSha = baselineSha, BaselineReceiptPath = options.BaselineReceiptPath ?? "", QualificationPath = options.QualificationPath ?? "",
                        TimingRoot = timingRoot, TrainManifestPath = options.TrainManifestPath ?? "", ReleaseIntentPath = options.ReleaseIntentPath ?? "",
                        SelectionAttemptPath = attemptPath, SelectionAttemptDigest = attemptDigest, SelectionPath = selectionPath,
                        SelectionDigest = selectionDigest, Attempt = options.Attempt.ToString(CultureInfo.InvariantCulture),
                    });
                    attemptPath = bound.SelectionAttemptPath;
                    attemptDigest = bound.SelectionAttemptDigest;
                    selectionPath = bound.SelectionPath;
                    selectionDigest = bound.SelectionDigest;
                    baselineSha = bound.BaselineSha;
                    result.Selection = bound.Selection;
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    // The script's catch: any failure of the sealing is a refusal naming it (selection-refused:<reason>, ...).
                    result.Refusal = e.Message;
                    result.ExitCode = 3;
                    return result;
                }
            }

            var runOptions = new NightlyRunOptions
            {
                RunId = runId, Repository = options.Repository ?? "", SelectionMode = effectiveMode, BaselineSha = baselineSha,
                BaselineReceiptPath = options.BaselineReceiptPath ?? "", QualificationPath = options.QualificationPath ?? "", TimingRoot = timingRoot,
                TrainManifestPath = options.TrainManifestPath ?? "", ReleaseIntentPath = options.ReleaseIntentPath ?? "", SelectionAttemptPath = attemptPath,
                SelectionAttemptDigest = attemptDigest, SelectionPath = selectionPath, SelectionDigest = selectionDigest, Attempt = options.Attempt,
                CheckoutRoot = checkout, StateRoot = stateRoot, LogRoot = logRoot, Ref = result.Ref, RemoteUrl = options.RemoteUrl ?? "", Trigger = "rc",
                Profile = "rc", ExpectedSha = result.Sha, CandidateId = result.CandidateId, ReleaseRoot = releaseRoot,
                CoordinationRoot = options.CoordinationRoot ?? "", WhatIf = options.WhatIf,
                RequestedSelectionMode = requestedIncremental ? RcIncrementalCut.RequestedMode : "",
                IncrementalApprovalDigest = admission?.Admitted == true ? admission.ApprovalDigest : "",
                IncrementalBaselineSha = admission?.Admitted == true ? admission.BaselineSha : "",
                IncrementalInventoryPath = admission?.Admitted == true ? admission.InventoryPath : "",
                IncrementalInventoryDigest = admission?.Admitted == true ? admission.InventoryDigest : "",
                IncrementalOwed = admission?.Admitted == true ? new Dictionary<string, string>(admission.Owed, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal),
                IncrementalOwedDigest = admission?.Admitted == true ? admission.OwedDigest : "",
            };
            NightlyRunResult run;
            try
            {
                run = host.Run(runOptions) ?? throw new NightlyRefusal("nightly-run-no-result");
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // The nightly-run command's answer to a failure outside the run: one error line, then its record.
                host.Nightly.Error.WriteLine("nightly-run: " + e.GetType().Name + ": " + e.Message);
                run = new NightlyRunResult { ExitCode = 1, Refusal = "nightly-run-failed", RunId = runId };
                output.WriteLine(NightlyRun.RecordLine(run));
            }
            result.Tested = run.ExitCode == 0;
            if (!result.Tested)
            {
                result.Refusal = run.Refusal ?? "";
                result.ExitCode = run.ExitCode;
                Say("rc run did not complete green; publishing nothing (exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
                return result;
            }
            if (requestedIncremental)
            {
                // D-6: an Incremental request (admitted or fallen back to Full) never reaches the publisher.
                Say("incremental request is qualification-only; publishing nothing (coverageScope " + result.CoverageScope + ").");
                result.ExitCode = 0;
                return result;
            }
            if (options.SkipPublish || options.WhatIf)
            {
                result.ExitCode = 0;
                return result;
            }

            // The publisher's own admission applies: a seam-bound publisher host outside test mode refuses.
            var publication = ReleasePublish.Run(new ReleasePublishOptions
            {
                ReleaseRoot = releaseRoot, CandidateId = result.CandidateId, CheckoutRoot = checkout, Repository = options.Repository ?? "",
                AntiphonApiUrl = options.AntiphonApiUrl ?? "", ScriptsRoot = options.ScriptsRoot ?? "", TestMode = options.TestMode,
            }, host.Publisher);
            result.Published = publication.Published;
            result.Tag = publication.Tag ?? "";
            result.Refusal = publication.Refusal ?? "";
            result.ExitCode = publication.ExitCode;
            return result;
        }
    }
}
