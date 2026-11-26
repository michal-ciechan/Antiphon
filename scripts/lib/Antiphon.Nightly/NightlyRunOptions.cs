// W2-nightly S19: the command line of the nightly-run command (plan 2026-11-21-w2-nightly-port-slices.md, section 4): one
// flag per parameter of scripts/nightly-run.ps1 (Invoke-AntiphonNightlyRunCore) except -SeamsPath (seams never cross a
// process, section 2 rule 2) and -PassThru (the launcher's, read back from --result-file). S20: the flags of the
// nightly-run command (Antiphon.Scripts/NightlyRunCommand.cs), which scripts/nightly-run.ps1 launches. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The parameters of one nightly run, with the defaults of scripts/nightly-run.ps1.</summary>
    public sealed class NightlyRunOptions
    {
        /// <summary>The isolated clone a run syncs and tests (the -CheckoutRoot default).</summary>
        public const string DefaultCheckoutRoot = @"C:\Antiphon\nightly\checkout";

        /// <summary>The origin a run clones and proves its clone against (the -RemoteUrl default).</summary>
        public const string DefaultRemoteUrl = "https://github.com/michal-ciechan/Antiphon";

        /// <summary>The valued flags of the command.</summary>
        public static readonly string[] Valued =
        {
            "--suites", "--checkout-root", "--log-root", "--ref", "--remote-url", "--state-root", "--selection-mode", "--repository", "--baseline-sha",
            "--baseline-receipt-path", "--qualification-path", "--timing-root", "--train-manifest-path", "--release-intent-path",
            "--selection-attempt-path", "--selection-attempt-digest", "--selection-path", "--selection-digest", "--attempt", "--prior-failures-path",
            "--prior-failures-digest", "--seed-source-candidate-id", "--trigger", "--profile", "--expected-sha", "--candidate-id", "--release-root",
            "--coordination-root", "--run-id", "--continue-run-id", "--continue-parent-pid", "--continue-parent-started-at", "--result-file",
            "--requested-selection-mode", "--incremental-approval-digest", "--incremental-baseline-sha",
            "--incremental-inventory-path", "--incremental-inventory-digest", "--incremental-owed-digest",
        };

        /// <summary>The switches of the command.</summary>
        public static readonly string[] Switches = { "--no-report", "--no-sync", "--accept-cross-candidate-seed", "--what-if" };

        /// <summary>The suites named (each entry may hold a comma list). Null or empty: the policy's.</summary>
        public string[] Suites { get; set; }

        public bool NoReport { get; set; }
        public bool NoSync { get; set; }
        public string CheckoutRoot { get; set; } = DefaultCheckoutRoot;

        /// <summary>The parent of the run directories. Blank: &lt;StateRoot&gt;\logs.</summary>
        public string LogRoot { get; set; } = "";

        public string Ref { get; set; } = "master";
        public string RemoteUrl { get; set; } = DefaultRemoteUrl;

        /// <summary>The run state. Blank: C:\Antiphon\nightly (NightlyRun.DefaultStateRoot).</summary>
        public string StateRoot { get; set; } = "";

        public string SelectionMode { get; set; } = "Full";

        /// <summary>
        /// CARD-1836 S6: the mode the cut was asked for (blank, or Incremental). The run still executes its
        /// SelectionMode (Full); an admitted Incremental request carries its binding below to the tests hop.
        /// </summary>
        public string RequestedSelectionMode { get; set; } = "";
        public string IncrementalApprovalDigest { get; set; } = "";
        public string IncrementalBaselineSha { get; set; } = "";
        public string IncrementalInventoryPath { get; set; } = "";
        public string IncrementalInventoryDigest { get; set; } = "";

        /// <summary>CARD-1836 Review r1 D-3: the comparison's later open obligations (content digest, reason) and their digest.</summary>
        public System.Collections.Generic.Dictionary<string, string> IncrementalOwed { get; set; } = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        public string IncrementalOwedDigest { get; set; } = "";
        public string Repository { get; set; } = "michal-ciechan/Antiphon";
        public string BaselineSha { get; set; } = "";
        public string BaselineReceiptPath { get; set; } = "";
        public string QualificationPath { get; set; } = "";
        public string TimingRoot { get; set; } = "";
        public string TrainManifestPath { get; set; } = "";
        public string ReleaseIntentPath { get; set; } = "";
        public string SelectionAttemptPath { get; set; } = "";
        public string SelectionAttemptDigest { get; set; } = "";
        public string SelectionPath { get; set; } = "";
        public string SelectionDigest { get; set; } = "";
        public int Attempt { get; set; } = 1;
        public string PriorFailuresPath { get; set; } = "";
        public string PriorFailuresDigest { get; set; } = "";
        public bool AcceptCrossCandidateSeed { get; set; }
        public string SeedSourceCandidateId { get; set; } = "";
        public string Trigger { get; set; } = "scheduled";
        public string Profile { get; set; } = "nightly";
        public string ExpectedSha { get; set; } = "";
        public string CandidateId { get; set; } = "";
        public string ReleaseRoot { get; set; } = "";
        public string CoordinationRoot { get; set; } = "";
        public string RunId { get; set; } = "";
        public string ContinueRunId { get; set; } = "";
        public int ContinueParentPid { get; set; }
        public string ContinueParentStartedAt { get; set; } = "";
        public bool WhatIf { get; set; }

        /// <summary>Where the command writes the run's result record (the launcher's -PassThru); blank: nowhere. NightlyRun.Run does not read it.</summary>
        public string ResultFile { get; set; } = "";

        /// <summary>
        /// The arguments after the command name: --kebab value flags and bare switches (NightlyArgs), an absent flag taking
        /// the default. --suites is split at commas with empty entries kept. --selection-mode takes Full, Shadow or
        /// ChangeAware (any case, kept in that spelling); --attempt and --continue-parent-pid take an integer. Anything
        /// else, and any NightlyArgs error, is NightlyUsageException.
        /// </summary>
        public static NightlyRunOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var o = new NightlyRunOptions();
            string Text(string flag, string fallback) => flags.Value(flag, fallback) ?? fallback;
            if (flags.Has("--suites")) o.Suites = (flags.Value("--suites") ?? "").Split(',');
            o.NoReport = flags.Switch("--no-report");
            o.NoSync = flags.Switch("--no-sync");
            o.CheckoutRoot = Text("--checkout-root", o.CheckoutRoot);
            o.LogRoot = Text("--log-root", o.LogRoot);
            o.Ref = Text("--ref", o.Ref);
            o.RemoteUrl = Text("--remote-url", o.RemoteUrl);
            o.StateRoot = Text("--state-root", o.StateRoot);
            if (flags.Has("--selection-mode"))
            {
                var mode = flags.Value("--selection-mode") ?? "";
                o.SelectionMode = NightlyTestsOptions.SelectionModes.FirstOrDefault(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NightlyUsageException("--selection-mode expects one of " + string.Join(", ", NightlyTestsOptions.SelectionModes) + ", not '" + mode + "'");
            }
            o.Repository = Text("--repository", o.Repository);
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
            o.PriorFailuresPath = Text("--prior-failures-path", o.PriorFailuresPath);
            o.PriorFailuresDigest = Text("--prior-failures-digest", o.PriorFailuresDigest);
            o.AcceptCrossCandidateSeed = flags.Switch("--accept-cross-candidate-seed");
            o.SeedSourceCandidateId = Text("--seed-source-candidate-id", o.SeedSourceCandidateId);
            o.Trigger = Text("--trigger", o.Trigger);
            o.Profile = Text("--profile", o.Profile);
            o.ExpectedSha = Text("--expected-sha", o.ExpectedSha);
            o.CandidateId = Text("--candidate-id", o.CandidateId);
            o.ReleaseRoot = Text("--release-root", o.ReleaseRoot);
            o.RequestedSelectionMode = Text("--requested-selection-mode", o.RequestedSelectionMode);
            o.IncrementalApprovalDigest = Text("--incremental-approval-digest", o.IncrementalApprovalDigest);
            o.IncrementalBaselineSha = Text("--incremental-baseline-sha", o.IncrementalBaselineSha);
            o.IncrementalInventoryPath = Text("--incremental-inventory-path", o.IncrementalInventoryPath);
            o.IncrementalInventoryDigest = Text("--incremental-inventory-digest", o.IncrementalInventoryDigest);
            o.IncrementalOwedDigest = Text("--incremental-owed-digest", o.IncrementalOwedDigest);
            o.CoordinationRoot = Text("--coordination-root", o.CoordinationRoot);
            o.RunId = Text("--run-id", o.RunId);
            o.ContinueRunId = Text("--continue-run-id", o.ContinueRunId);
            o.ContinueParentPid = flags.Int("--continue-parent-pid", o.ContinueParentPid);
            o.ContinueParentStartedAt = Text("--continue-parent-started-at", o.ContinueParentStartedAt);
            o.WhatIf = flags.Switch("--what-if");
            o.ResultFile = Text("--result-file", o.ResultFile);
            return o;
        }

        /// <summary>
        /// The command line that parses back to these options: each valued flag whose value differs from the default, then
        /// each switch that is set. Suites are joined with commas.
        /// </summary>
        public string[] ToArguments()
        {
            var d = new NightlyRunOptions();
            var args = new List<string>();
            void Add(string flag, string value, string fallback)
            {
                if (!string.Equals(value ?? "", fallback ?? "", StringComparison.Ordinal)) { args.Add(flag); args.Add(value ?? ""); }
            }
            string N(int value) => value.ToString(CultureInfo.InvariantCulture);
            if (Suites != null && Suites.Length > 0) { args.Add("--suites"); args.Add(string.Join(",", Suites)); }
            Add("--checkout-root", CheckoutRoot, d.CheckoutRoot);
            Add("--log-root", LogRoot, d.LogRoot);
            Add("--ref", Ref, d.Ref);
            Add("--remote-url", RemoteUrl, d.RemoteUrl);
            Add("--state-root", StateRoot, d.StateRoot);
            Add("--selection-mode", SelectionMode, d.SelectionMode);
            Add("--repository", Repository, d.Repository);
            Add("--baseline-sha", BaselineSha, d.BaselineSha);
            Add("--baseline-receipt-path", BaselineReceiptPath, d.BaselineReceiptPath);
            Add("--qualification-path", QualificationPath, d.QualificationPath);
            Add("--timing-root", TimingRoot, d.TimingRoot);
            Add("--train-manifest-path", TrainManifestPath, d.TrainManifestPath);
            Add("--release-intent-path", ReleaseIntentPath, d.ReleaseIntentPath);
            Add("--selection-attempt-path", SelectionAttemptPath, d.SelectionAttemptPath);
            Add("--selection-attempt-digest", SelectionAttemptDigest, d.SelectionAttemptDigest);
            Add("--selection-path", SelectionPath, d.SelectionPath);
            Add("--selection-digest", SelectionDigest, d.SelectionDigest);
            Add("--attempt", N(Attempt), N(d.Attempt));
            Add("--prior-failures-path", PriorFailuresPath, d.PriorFailuresPath);
            Add("--prior-failures-digest", PriorFailuresDigest, d.PriorFailuresDigest);
            Add("--seed-source-candidate-id", SeedSourceCandidateId, d.SeedSourceCandidateId);
            Add("--trigger", Trigger, d.Trigger);
            Add("--profile", Profile, d.Profile);
            Add("--expected-sha", ExpectedSha, d.ExpectedSha);
            Add("--candidate-id", CandidateId, d.CandidateId);
            Add("--release-root", ReleaseRoot, d.ReleaseRoot);
            Add("--requested-selection-mode", RequestedSelectionMode, d.RequestedSelectionMode);
            Add("--incremental-approval-digest", IncrementalApprovalDigest, d.IncrementalApprovalDigest);
            Add("--incremental-baseline-sha", IncrementalBaselineSha, d.IncrementalBaselineSha);
            Add("--incremental-inventory-path", IncrementalInventoryPath, d.IncrementalInventoryPath);
            Add("--incremental-inventory-digest", IncrementalInventoryDigest, d.IncrementalInventoryDigest);
            Add("--incremental-owed-digest", IncrementalOwedDigest, d.IncrementalOwedDigest);
            Add("--coordination-root", CoordinationRoot, d.CoordinationRoot);
            Add("--run-id", RunId, d.RunId);
            Add("--continue-run-id", ContinueRunId, d.ContinueRunId);
            Add("--continue-parent-pid", N(ContinueParentPid), N(d.ContinueParentPid));
            Add("--continue-parent-started-at", ContinueParentStartedAt, d.ContinueParentStartedAt);
            Add("--result-file", ResultFile, d.ResultFile);
            if (NoReport) args.Add("--no-report");
            if (NoSync) args.Add("--no-sync");
            if (AcceptCrossCandidateSeed) args.Add("--accept-cross-candidate-seed");
            if (WhatIf) args.Add("--what-if");
            return args.ToArray();
        }
    }
}
