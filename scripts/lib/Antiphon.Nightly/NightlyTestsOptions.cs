// W2-nightly S15: the command line of the nightly-tests command (plan 2026-11-21-w2-nightly-port-slices.md, sections 3
// and 4): one flag per parameter of scripts/nightly-tests.ps1 (Invoke-AntiphonNightlyTests) except -SeamsPath (seams never
// cross a process, section 2 rule 2) and -PassThru (the launcher's). Not wired yet: S17 makes it the CLI command.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Antiphon.Nightly
{
    /// <summary>The parameters of one nightly tests run, with the defaults of scripts/nightly-tests.ps1.</summary>
    public sealed class NightlyTestsOptions
    {
        /// <summary>The valued flags of the command.</summary>
        public static readonly string[] Valued =
        {
            "--repo-root", "--log-root", "--suites", "--sha", "--git-ref", "--trigger", "--profile", "--expected-sha", "--run-id", "--selection-mode",
            "--release-root", "--candidate-id", "--repository", "--baseline-sha", "--baseline-receipt-path", "--qualification-path", "--timing-root",
            "--train-manifest-path", "--release-intent-path", "--selection-attempt-path", "--selection-attempt-digest", "--selection-path",
            "--selection-digest", "--attempt", "--prior-failures-path", "--prior-failures-digest", "--seed-source-candidate-id", "--rc-evidence-root",
            "--policy-path",
            "--requested-selection-mode", "--incremental-approval-digest", "--incremental-baseline-sha",
            "--incremental-inventory-path", "--incremental-inventory-digest", "--incremental-owed-digest",
        };

        /// <summary>The switches of the command.</summary>
        public static readonly string[] Switches = { "--accept-cross-candidate-seed", "--allow-shared-tree", "--what-if" };

        /// <summary>The selection modes -SelectionMode accepts ([ValidateSet], case-insensitive), in their declared spelling.</summary>
        public static readonly string[] SelectionModes = { "Full", "Shadow", "ChangeAware" };

        /// <summary>The checkout to run in. Blank: the checkout this code was built from (NightlyTests.DefaultRepoRoot).</summary>
        public string RepoRoot { get; set; } = "";

        /// <summary>The run's log root. Blank: C:\Antiphon\nightly\logs\&lt;yyyy-MM-dd-HHmm of the start&gt;.</summary>
        public string LogRoot { get; set; } = "";

        /// <summary>The suites named (each entry may hold a comma list). Null or empty: the profile's required suites.</summary>
        public string[] Suites { get; set; }

        public string Sha { get; set; } = "";
        public string GitRef { get; set; } = "origin/master";
        public string Trigger { get; set; } = "scheduled";
        public string Profile { get; set; } = "nightly";
        public string ExpectedSha { get; set; } = "";
        public string RunId { get; set; } = "";
        public string SelectionMode { get; set; } = "Full";
        public string RequestedSelectionMode { get; set; } = "";
        public string IncrementalApprovalDigest { get; set; } = "";
        public string IncrementalBaselineSha { get; set; } = "";
        public string IncrementalInventoryPath { get; set; } = "";
        public string IncrementalInventoryDigest { get; set; } = "";
        public string IncrementalOwedDigest { get; set; } = "";
        public string ReleaseRoot { get; set; } = "";
        public string CandidateId { get; set; } = "";
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
        public string RcEvidenceRoot { get; set; } = "";
        public string PolicyPath { get; set; } = "";
        public bool AllowSharedTree { get; set; }
        public bool WhatIf { get; set; }

        /// <summary>
        /// The arguments after the command name: --kebab value flags and bare switches (NightlyArgs), an absent flag taking
        /// the default. --suites is split at commas with empty entries kept (an explicit empty or comma-only selection is
        /// refused by the run, as -Suites '' is). --selection-mode takes Full, Shadow or ChangeAware (any case, kept in
        /// that spelling) and --attempt an integer; anything else, and any NightlyArgs error, is NightlyUsageException.
        /// </summary>
        public static NightlyTestsOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            var options = new NightlyTestsOptions();
            string Text(string flag, string fallback) => flags.Value(flag, fallback) ?? fallback;
            options.RepoRoot = Text("--repo-root", options.RepoRoot);
            options.LogRoot = Text("--log-root", options.LogRoot);
            if (flags.Has("--suites")) options.Suites = (flags.Value("--suites") ?? "").Split(',');
            options.Sha = Text("--sha", options.Sha);
            options.GitRef = Text("--git-ref", options.GitRef);
            options.Trigger = Text("--trigger", options.Trigger);
            options.Profile = Text("--profile", options.Profile);
            options.ExpectedSha = Text("--expected-sha", options.ExpectedSha);
            options.RunId = Text("--run-id", options.RunId);
            if (flags.Has("--selection-mode"))
            {
                var mode = flags.Value("--selection-mode") ?? "";
                options.SelectionMode = SelectionModes.FirstOrDefault(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NightlyUsageException("--selection-mode expects one of " + string.Join(", ", SelectionModes) + ", not '" + mode + "'");
            }
            options.ReleaseRoot = Text("--release-root", options.ReleaseRoot);
            options.RequestedSelectionMode = Text("--requested-selection-mode", options.RequestedSelectionMode);
            options.IncrementalApprovalDigest = Text("--incremental-approval-digest", options.IncrementalApprovalDigest);
            options.IncrementalBaselineSha = Text("--incremental-baseline-sha", options.IncrementalBaselineSha);
            options.IncrementalInventoryPath = Text("--incremental-inventory-path", options.IncrementalInventoryPath);
            options.IncrementalInventoryDigest = Text("--incremental-inventory-digest", options.IncrementalInventoryDigest);
            options.IncrementalOwedDigest = Text("--incremental-owed-digest", options.IncrementalOwedDigest);
            options.CandidateId = Text("--candidate-id", options.CandidateId);
            options.Repository = Text("--repository", options.Repository);
            options.BaselineSha = Text("--baseline-sha", options.BaselineSha);
            options.BaselineReceiptPath = Text("--baseline-receipt-path", options.BaselineReceiptPath);
            options.QualificationPath = Text("--qualification-path", options.QualificationPath);
            options.TimingRoot = Text("--timing-root", options.TimingRoot);
            options.TrainManifestPath = Text("--train-manifest-path", options.TrainManifestPath);
            options.ReleaseIntentPath = Text("--release-intent-path", options.ReleaseIntentPath);
            options.SelectionAttemptPath = Text("--selection-attempt-path", options.SelectionAttemptPath);
            options.SelectionAttemptDigest = Text("--selection-attempt-digest", options.SelectionAttemptDigest);
            options.SelectionPath = Text("--selection-path", options.SelectionPath);
            options.SelectionDigest = Text("--selection-digest", options.SelectionDigest);
            options.Attempt = flags.Int("--attempt", options.Attempt);
            options.PriorFailuresPath = Text("--prior-failures-path", options.PriorFailuresPath);
            options.PriorFailuresDigest = Text("--prior-failures-digest", options.PriorFailuresDigest);
            options.AcceptCrossCandidateSeed = flags.Switch("--accept-cross-candidate-seed");
            options.SeedSourceCandidateId = Text("--seed-source-candidate-id", options.SeedSourceCandidateId);
            options.RcEvidenceRoot = Text("--rc-evidence-root", options.RcEvidenceRoot);
            options.PolicyPath = Text("--policy-path", options.PolicyPath);
            options.AllowSharedTree = flags.Switch("--allow-shared-tree");
            options.WhatIf = flags.Switch("--what-if");
            return options;
        }

        /// <summary>
        /// The command line that parses back to these options: each valued flag whose value differs from the default, then
        /// each switch that is set. Suites are joined with commas.
        /// </summary>
        public string[] ToArguments()
        {
            var defaults = new NightlyTestsOptions();
            var args = new List<string>();
            void Add(string flag, string value, string fallback)
            {
                if (!string.Equals(value ?? "", fallback ?? "", StringComparison.Ordinal)) { args.Add(flag); args.Add(value ?? ""); }
            }
            Add("--repo-root", RepoRoot, defaults.RepoRoot);
            Add("--log-root", LogRoot, defaults.LogRoot);
            if (Suites != null && Suites.Length > 0) { args.Add("--suites"); args.Add(string.Join(",", Suites)); }
            Add("--sha", Sha, defaults.Sha);
            Add("--git-ref", GitRef, defaults.GitRef);
            Add("--trigger", Trigger, defaults.Trigger);
            Add("--profile", Profile, defaults.Profile);
            Add("--expected-sha", ExpectedSha, defaults.ExpectedSha);
            Add("--run-id", RunId, defaults.RunId);
            Add("--selection-mode", SelectionMode, defaults.SelectionMode);
            Add("--release-root", ReleaseRoot, defaults.ReleaseRoot);
            Add("--requested-selection-mode", RequestedSelectionMode, defaults.RequestedSelectionMode);
            Add("--incremental-approval-digest", IncrementalApprovalDigest, defaults.IncrementalApprovalDigest);
            Add("--incremental-baseline-sha", IncrementalBaselineSha, defaults.IncrementalBaselineSha);
            Add("--incremental-inventory-path", IncrementalInventoryPath, defaults.IncrementalInventoryPath);
            Add("--incremental-inventory-digest", IncrementalInventoryDigest, defaults.IncrementalInventoryDigest);
            Add("--incremental-owed-digest", IncrementalOwedDigest, defaults.IncrementalOwedDigest);
            Add("--candidate-id", CandidateId, defaults.CandidateId);
            Add("--repository", Repository, defaults.Repository);
            Add("--baseline-sha", BaselineSha, defaults.BaselineSha);
            Add("--baseline-receipt-path", BaselineReceiptPath, defaults.BaselineReceiptPath);
            Add("--qualification-path", QualificationPath, defaults.QualificationPath);
            Add("--timing-root", TimingRoot, defaults.TimingRoot);
            Add("--train-manifest-path", TrainManifestPath, defaults.TrainManifestPath);
            Add("--release-intent-path", ReleaseIntentPath, defaults.ReleaseIntentPath);
            Add("--selection-attempt-path", SelectionAttemptPath, defaults.SelectionAttemptPath);
            Add("--selection-attempt-digest", SelectionAttemptDigest, defaults.SelectionAttemptDigest);
            Add("--selection-path", SelectionPath, defaults.SelectionPath);
            Add("--selection-digest", SelectionDigest, defaults.SelectionDigest);
            Add("--attempt", Attempt.ToString(CultureInfo.InvariantCulture), defaults.Attempt.ToString(CultureInfo.InvariantCulture));
            Add("--prior-failures-path", PriorFailuresPath, defaults.PriorFailuresPath);
            Add("--prior-failures-digest", PriorFailuresDigest, defaults.PriorFailuresDigest);
            Add("--seed-source-candidate-id", SeedSourceCandidateId, defaults.SeedSourceCandidateId);
            Add("--rc-evidence-root", RcEvidenceRoot, defaults.RcEvidenceRoot);
            Add("--policy-path", PolicyPath, defaults.PolicyPath);
            if (AcceptCrossCandidateSeed) args.Add("--accept-cross-candidate-seed");
            if (AllowSharedTree) args.Add("--allow-shared-tree");
            if (WhatIf) args.Add("--what-if");
            return args.ToArray();
        }
    }
}
