// CARD-1313 S6-A: the release selection attempt - intent only, every admitted attempt executes Full. W2-nightly S15 (plan
// 2026-11-21-w2-nightly-port-slices.md, slice S15) moved the hop reader here (Read-ReleaseSelectionAttempt,
// Resolve-ReleaseSelectionMembers, Assert-ReleaseSelectionPath, Get-ReleaseSelectionTrainDigest); the 2026-11-22 plan
// (docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, unit 8) moved the writer (New-ReleaseSelectionAttempt and
// its core: Assert-ReleaseSelectionCommit, Save-ReleaseSelectionAttempt, Write-ReleaseSelectionOnce) and deleted
// scripts/lib/release-selection.ps1, its pwsh worker (release-selection-worker.ps1) and the CLI's select-release twin. The
// attempt is written in process by NightlyRun and by scripts/release-cut.ps1 (which loads the core); nothing needs pwsh
// any more, so the selection-pwsh-required capability check went with the worker. A refusal is thrown as NightlyRefusal:
// the reader's with its reason, Create's as selection-refused:<reason>. Plain C#: the attempt documents are read through
// NightlyJson (PSObjects under PowerShell, PlainObjects otherwise). ASCII only.
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
    /// What Create writes and a receiving hop expects of a selection attempt (the request map of release-cut.ps1 and NightlyRun).
    /// A value the caller did not give is null, as a missing hashtable key reads in PowerShell.
    /// </summary>
    public sealed class ReleaseSelectionRequest
    {
        public string RepositoryRoot { get; set; }
        public string CandidateSha { get; set; }
        public string CandidateRef { get; set; }
        public string Repository { get; set; }
        public string Profile { get; set; }
        public string ReleaseRoot { get; set; }
        public string CandidateId { get; set; }
        public string RunId { get; set; }
        public string SelectionMode { get; set; }
        public string BaselineSha { get; set; }
        public string BaselineReceiptPath { get; set; }
        public string QualificationPath { get; set; }
        public string TimingRoot { get; set; }
        public string TrainManifestPath { get; set; }
        public string ReleaseIntentPath { get; set; }
        public string SelectionAttemptPath { get; set; }
        public string SelectionAttemptDigest { get; set; }
        public string SelectionPath { get; set; }
        public string SelectionDigest { get; set; }

        /// <summary>The attempt number as the hop compares it ([string]$Expected.Attempt).</summary>
        public string Attempt { get; set; }

        /// <summary>A PowerShell request hashtable: each key read as [string] (a missing key stays null).</summary>
        public static ReleaseSelectionRequest From(IDictionary request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string Text(string name)
            {
                var value = Lookup(request, name);
                return value == null ? null : PsValue.Str(value);
            }
            return new ReleaseSelectionRequest
            {
                RepositoryRoot = Text("RepositoryRoot"), CandidateSha = Text("CandidateSha"), CandidateRef = Text("CandidateRef"), Repository = Text("Repository"),
                Profile = Text("Profile"), ReleaseRoot = Text("ReleaseRoot"), CandidateId = Text("CandidateId"), RunId = Text("RunId"),
                SelectionMode = Text("SelectionMode"), BaselineSha = Text("BaselineSha"), BaselineReceiptPath = Text("BaselineReceiptPath"),
                QualificationPath = Text("QualificationPath"), TimingRoot = Text("TimingRoot"), TrainManifestPath = Text("TrainManifestPath"),
                ReleaseIntentPath = Text("ReleaseIntentPath"), SelectionAttemptPath = Text("SelectionAttemptPath"),
                SelectionAttemptDigest = Text("SelectionAttemptDigest"), SelectionPath = Text("SelectionPath"), SelectionDigest = Text("SelectionDigest"),
                Attempt = Text("Attempt"),
            };
        }

        // A PowerShell hashtable's keys ignore case; any other dictionary is searched the same way.
        private static object Lookup(IDictionary request, string name)
        {
            if (request.Contains(name)) return request[name];
            foreach (DictionaryEntry entry in request)
                if (string.Equals(PsValue.Str(entry.Key), name, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            return null;
        }
    }

    /// <summary>The selection attempt: its writer (Create), the reader a receiving hop runs and the member rule.</summary>
    public static class ReleaseSelectionAttempt
    {
        /// <summary>The attempt document's format.</summary>
        public const string AttemptFormat = "antiphon-release-selection-attempt-v1";

        /// <summary>The selection document's format.</summary>
        public const string SelectionFormat = "antiphon-release-selection-v1";

        /// <summary>The prefix of every refusal Create throws.</summary>
        public const string RefusedPrefix = "selection-refused:";

        private static readonly Regex Sha256Hex = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex OriginPrefix = new Regex("^origin/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex Identity = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// New-ReleaseSelectionAttempt: writes the attempt of the request once - selection.json, then selection-attempt.json, in
        /// &lt;release root&gt;/candidates/&lt;id&gt;/attempts/&lt;run&gt; (canonical JSON, each created once and flushed; an identical file
        /// is reused, any other is selection-attempt-conflict) - and returns both paths, their digests, the baseline and the
        /// attempt document. The candidate's selection contract is bound; with a baseline B (requested, or a full-release
        /// receipt's) the change-aware proposal and, in a mode other than Full, the rc project proposal are recorded as
        /// intent; effectiveMode and coverageScope are always Full. Every refusal is NightlyRefusal("selection-refused:" +
        /// reason): selection-commit-required/-unavailable, baseline-not-ancestor, the baseline receipt's, the contract's
        /// (policy-hash-stale, ...), qualification-invalid, selection-train-input, selection-path-outside-state/-in-checkout,
        /// selection-run-identity, selection-candidate-identity, selection-attempt-conflict.
        /// </summary>
        public static NightlySelectionBinding Create(ReleaseSelectionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            try { return CreateAttempt(request); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { throw new NightlyRefusal(RefusedPrefix + e.Message); }
        }

        private static NightlySelectionBinding CreateAttempt(ReleaseSelectionRequest request)
        {
            var repositoryRoot = request.RepositoryRoot;
            if (string.IsNullOrEmpty(repositoryRoot)) throw new NightlyRefusal("selection-repository-required");
            if (string.IsNullOrEmpty(request.ReleaseRoot)) throw new NightlyRefusal("selection-path-outside-state");
            AssertCommit(repositoryRoot, request.CandidateSha);
            if (!string.IsNullOrEmpty(request.BaselineSha)) AssertCommit(repositoryRoot, request.BaselineSha);
            var contract = TestSelection.Contract(repositoryRoot, request.CandidateSha, request.Profile);
            var baseline = ReleaseSelectionBaseline.Read(request, contract);
            var b = baseline.Sha ?? "";
            if (b.Length > 0)
            {
                AssertCommit(repositoryRoot, b);
                if (TestSelectionGit.Run(repositoryRoot, new[] { "merge-base", "--is-ancestor", b, request.CandidateSha }, new[] { 0, 1 }).Exit != 0)
                    throw new NightlyRefusal("baseline-not-ancestor");
            }
            object proposal = null, projectProposal = null;
            var reasons = new List<string>();
            if (!string.IsNullOrEmpty(baseline.Reason)) reasons.Add(baseline.Reason);
            if (b.Length > 0)
            {
                var selection = TestSelection.Select(repositoryRoot, b, request.CandidateSha, request.Profile);
                proposal = selection;
                reasons.AddRange(TestSelectionJson.Elements(selection["fallbackReasons"]).Select(PsValue.Str));
                if (!string.Equals(request.SelectionMode, "Full", StringComparison.Ordinal))
                    projectProposal = RcProjectSelection.Select(repositoryRoot, b, request.CandidateSha);
            }
            if (string.Equals(request.SelectionMode, "ChangeAware", StringComparison.Ordinal)) reasons.Add("change-aware-unqualified");
            var qualificationDigest = "";
            var qualification = request.QualificationPath;
            if (!string.IsNullOrEmpty(qualification) && (File.Exists(qualification) || Directory.Exists(qualification)))
            {
                AssertPath(qualification, request.ReleaseRoot, repositoryRoot);
                // Reading a claim never activates it. A claim that is not one JSON object is corrupt, not absent.
                if (!File.Exists(qualification) || NightlyJson.ParseText(File.ReadAllText(qualification, Encoding.UTF8), 1024) == null)
                    throw new NightlyRefusal("qualification-invalid");
                qualificationDigest = NightlyPaths.FileSha256(qualification);
            }
            var document = Ordered(("format", SelectionFormat), ("candidateSha", request.CandidateSha), ("baselineSha", b),
                ("requestedMode", request.SelectionMode), ("effectiveMode", "Full"), ("coverageScope", "Full"), ("proposal", proposal),
                ("projectProposal", projectProposal), ("fallbackReasons", SortedUnique(reasons)), ("executionEvidence", false));
            return Save(request, document, contract, baseline.Digest ?? "", qualificationDigest);
        }

        // Save-ReleaseSelectionAttempt.
        private static NightlySelectionBinding Save(ReleaseSelectionRequest request, OrderedDictionary selection, IDictionary contract, string baselineDigest, string qualificationDigest)
        {
            if (request.RunId == null || !Identity.IsMatch(request.RunId)
                || !int.TryParse(request.Attempt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attemptNumber) || attemptNumber < 1)
                throw new NightlyRefusal("selection-run-identity");
            if (request.CandidateId == null || !Identity.IsMatch(request.CandidateId)) throw new NightlyRefusal("selection-candidate-identity");
            var dir = Path.Combine(request.ReleaseRoot, "candidates", request.CandidateId, "attempts", request.RunId);
            var path = AssertPath(Path.Combine(dir, "selection-attempt.json"), request.ReleaseRoot, request.RepositoryRoot);
            var selectionPath = AssertPath(Path.Combine(dir, "selection.json"), request.ReleaseRoot, request.RepositoryRoot);
            var selectionText = NightlyDocuments.CanonicalJson(selection);
            var selectionDigest = TestSelectionJson.Sha256Text(selectionText);
            var attempt = Ordered(
                ("format", AttemptFormat), ("repository", request.Repository), ("candidateId", request.CandidateId), ("candidateRef", request.CandidateRef),
                ("candidateSha", request.CandidateSha), ("baselineSha", selection["baselineSha"]), ("requestedMode", request.SelectionMode),
                ("effectiveMode", "Full"), ("coverageScope", "Full"), ("profile", request.Profile), ("runId", request.RunId), ("attempt", attemptNumber),
                ("selectionDigest", selectionDigest), ("contract", contract), ("baselineDigest", baselineDigest), ("qualificationDigest", qualificationDigest),
                ("trainDigest", TrainDigest(request)), ("fallbackReasons", selection["fallbackReasons"]),
                ("baselineReceiptPath", request.BaselineReceiptPath), ("qualificationPath", request.QualificationPath), ("timingRoot", request.TimingRoot),
                ("releaseIntentPath", request.ReleaseIntentPath), ("trainManifestPath", request.TrainManifestPath));
            var attemptText = NightlyDocuments.CanonicalJson(attempt);
            // Both immutable files are checked before either write, including an interrupted first write.
            foreach (var (file, text) in new[] { (path, attemptText), (selectionPath, selectionText) })
                if ((File.Exists(file) || Directory.Exists(file)) && File.ReadAllText(file) != text) throw new NightlyRefusal("selection-attempt-conflict");
            WriteOnce(selectionPath, selectionText);
            WriteOnce(path, attemptText);
            return new NightlySelectionBinding
            {
                SelectionAttemptPath = path, SelectionAttemptDigest = NightlyPaths.FileSha256(path), SelectionPath = selectionPath,
                SelectionDigest = selectionDigest, BaselineSha = PsValue.Str(selection["baselineSha"]), Selection = attempt,
            };
        }

        // Write-ReleaseSelectionOnce: create-only publication of a flushed temp; an interrupted temp never becomes intent.
        private static void WriteOnce(string path, string text)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                if (File.ReadAllText(path) != text) throw new NightlyRefusal("selection-attempt-conflict");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                try { File.Move(temp, path); }
                catch (IOException)
                {
                    if (!File.Exists(path) || File.ReadAllText(path) != text) throw new NightlyRefusal("selection-attempt-conflict");
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        /// <summary>Assert-ReleaseSelectionCommit: a full commit id (selection-commit-required) of a commit the repository holds (selection-commit-unavailable).</summary>
        public static void AssertCommit(string repositoryRoot, string sha)
        {
            if (sha == null || !FullSha.IsMatch(sha)) throw new NightlyRefusal("selection-commit-required");
            var read = TestSelectionGit.Run(repositoryRoot, new[] { "cat-file", "-t", sha }, new[] { 0, 128 });
            if (read.Exit != 0 || read.Stdout.Trim() != "commit") throw new NightlyRefusal("selection-commit-unavailable");
        }

        // Sort-Object -Unique over strings: current-culture order ignoring case, one of each case-insensitive group.
        private static string[] SortedUnique(IEnumerable<string> values)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var unique = new List<string>();
            foreach (var value in values.OrderBy(v => v ?? "", Comparer<string>.Create((x, y) => compare.Compare(x, y, CompareOptions.IgnoreCase))))
                if (unique.Count == 0 || compare.Compare(unique[unique.Count - 1], value ?? "", CompareOptions.IgnoreCase) != 0) unique.Add(value ?? "");
            return unique.ToArray();
        }

        private static OrderedDictionary Ordered(params (string Key, object Value)[] pairs)
        {
            var map = new OrderedDictionary(StringComparer.Ordinal);
            foreach (var (key, value) in pairs) map[key] = value;
            return map;
        }

        private static readonly string[][] HopFields =
        {
            new[] { "candidateSha", "CandidateSha" }, new[] { "baselineSha", "BaselineSha" }, new[] { "requestedMode", "SelectionMode" },
            new[] { "runId", "RunId" }, new[] { "attempt", "Attempt" }, new[] { "profile", "Profile" }, new[] { "timingRoot", "TimingRoot" },
            new[] { "releaseIntentPath", "ReleaseIntentPath" }, new[] { "trainManifestPath", "TrainManifestPath" },
            new[] { "baselineReceiptPath", "BaselineReceiptPath" }, new[] { "qualificationPath", "QualificationPath" },
        };

        /// <summary>
        /// Resolve-ReleaseSelectionMembers: a Full attempt (effective mode and coverage scope Full, ordinal) executes the
        /// full members; anything else refuses selected-execution-unqualified.
        /// </summary>
        public static object[] ResolveMembers(object attempt, object[] fullMembers, object[] proposedMembers)
        {
            if (Same(PsValue.Prop(attempt, "effectiveMode"), "Full") && Same(PsValue.Prop(attempt, "coverageScope"), "Full")) return fullMembers;
            throw new NightlyRefusal("selected-execution-unqualified");
        }

        /// <summary>
        /// Read-ReleaseSelectionAttempt: both immutable files at their digests (no link on the way), both documents Full,
        /// the attempt bound to every field the hop carries, the train digest, the candidate, and the two paths where the
        /// attempt directory puts them. Returns the attempt document; a mismatch refuses selection-hop-&lt;field&gt;.
        /// </summary>
        public static object Read(ReleaseSelectionRequest expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            foreach (var (path, digest) in new[] { (expected.SelectionAttemptPath, expected.SelectionAttemptDigest), (expected.SelectionPath, expected.SelectionDigest) })
            {
                if (string.IsNullOrEmpty(path) || !Sha256Hex.IsMatch(digest ?? "") || !File.Exists(path) || NightlyPaths.HasReparseAncestor(path)
                    || !string.Equals(NightlyPaths.FileSha256(path), digest, StringComparison.Ordinal)) throw new NightlyRefusal("selection-hop-digest");
            }
            var attempt = ReadDocument(expected.SelectionAttemptPath);
            var selection = ReadDocument(expected.SelectionPath);
            if (Differs(PsValue.Prop(attempt, "format"), AttemptFormat) || Differs(PsValue.Prop(selection, "format"), SelectionFormat)
                || Differs(PsValue.Prop(attempt, "selectionDigest"), expected.SelectionDigest) || Differs(PsValue.Prop(attempt, "effectiveMode"), "Full")
                || Differs(PsValue.Prop(attempt, "coverageScope"), "Full") || Differs(PsValue.Prop(selection, "effectiveMode"), "Full")
                || Differs(PsValue.Prop(selection, "coverageScope"), "Full")) throw new NightlyRefusal("selection-hop-scope");
            foreach (var pair in HopFields)
            {
                if (!string.Equals(PsValue.Str(PsValue.Prop(attempt, pair[0])), Field(expected, pair[1]) ?? "", StringComparison.Ordinal))
                    throw new NightlyRefusal("selection-hop-" + pair[0]);
            }
            if (Differs(PsValue.Prop(selection, "candidateSha"), expected.CandidateSha) || Differs(PsValue.Prop(selection, "baselineSha"), expected.BaselineSha))
                throw new NightlyRefusal("selection-hop-candidateSha");
            if (!string.Equals(PsValue.Str(PsValue.Prop(attempt, "trainDigest")), TrainDigest(expected), StringComparison.Ordinal))
                throw new NightlyRefusal("selection-hop-trainDigest");
            if (Differs(PsValue.Prop(attempt, "repository"), expected.Repository) || Differs(PsValue.Prop(attempt, "candidateId"), expected.CandidateId)
                || Differs(PsValue.Prop(attempt, "candidateRef"), OriginPrefix.Replace(expected.CandidateRef ?? "", "")))
                throw new NightlyRefusal("selection-hop-candidate");
            var root = Path.Combine(expected.ReleaseRoot ?? "", "candidates", expected.CandidateId ?? "", "attempts", expected.RunId ?? "");
            foreach (var (name, file) in new[] { (expected.SelectionAttemptPath, "selection-attempt.json"), (expected.SelectionPath, "selection.json") })
            {
                var expectedPath = AssertPath(Path.Combine(root, file), expected.ReleaseRoot, expected.RepositoryRoot);
                if (!string.Equals(expectedPath, Path.GetFullPath(name), StringComparison.Ordinal)) throw new NightlyRefusal("selection-hop-path");
            }
            return attempt;
        }

        /// <summary>
        /// Assert-ReleaseSelectionPath: the full path, which must lie under root (case-insensitive on Windows) with no link on
        /// the way (selection-path-outside-state), and not under the disposable checkout when one is given
        /// (selection-path-in-checkout).
        /// </summary>
        public static string AssertPath(string path, string root, string checkout = "")
        {
            var full = Path.GetFullPath(path);
            var sep = Path.DirectorySeparatorChar;
            var comparison = sep == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var parent = Path.GetFullPath(root).TrimEnd(sep) + sep;
            if (!full.StartsWith(parent, comparison) || NightlyPaths.HasReparseAncestor(full)) throw new NightlyRefusal("selection-path-outside-state");
            if (!string.IsNullOrEmpty(checkout))
            {
                var disposable = Path.GetFullPath(checkout).TrimEnd(sep) + sep;
                if (full.StartsWith(disposable, comparison)) throw new NightlyRefusal("selection-path-in-checkout");
            }
            return full;
        }

        /// <summary>
        /// Get-ReleaseSelectionTrainDigest: the train manifest's SHA-256 (S6-A binds opaque bytes only), "" when none is named
        /// or it does not exist; a directory refuses selection-train-input.
        /// </summary>
        public static string TrainDigest(ReleaseSelectionRequest request)
        {
            if (string.IsNullOrEmpty(request?.TrainManifestPath)) return "";
            var path = AssertPath(request.TrainManifestPath, request.ReleaseRoot, request.RepositoryRoot);
            if (!File.Exists(path) && !Directory.Exists(path)) return "";
            if (!File.Exists(path)) throw new NightlyRefusal("selection-train-input");
            return NightlyPaths.FileSha256(path);
        }

        // Get-Content -Raw -Encoding UTF8 | ConvertFrom-Json: a document that does not read as one refuses as its scope would.
        private static object ReadDocument(string path) =>
            NightlyJson.ParseText(File.ReadAllText(path), 1024, true) ?? throw new NightlyRefusal("selection-hop-scope");

        private static string Field(ReleaseSelectionRequest request, string name)
        {
            switch (name)
            {
                case "CandidateSha": return request.CandidateSha;
                case "BaselineSha": return request.BaselineSha;
                case "SelectionMode": return request.SelectionMode;
                case "RunId": return request.RunId;
                case "Attempt": return request.Attempt;
                case "Profile": return request.Profile;
                case "TimingRoot": return request.TimingRoot;
                case "ReleaseIntentPath": return request.ReleaseIntentPath;
                case "TrainManifestPath": return request.TrainManifestPath;
                case "BaselineReceiptPath": return request.BaselineReceiptPath;
                case "QualificationPath": return request.QualificationPath;
                default: throw new ArgumentOutOfRangeException(nameof(name), name);
            }
        }

        // $left -cne $right for a document value and a request string: a missing value equals only a missing one.
        private static bool Differs(object value, string expected)
        {
            value = PsValue.BaseObject(value);
            if (value == null || expected == null) return value != null || expected != null;
            return !string.Equals(PsValue.Str(value), expected, StringComparison.Ordinal);
        }

        private static bool Same(object value, string expected) => !Differs(value, expected);
    }
}
