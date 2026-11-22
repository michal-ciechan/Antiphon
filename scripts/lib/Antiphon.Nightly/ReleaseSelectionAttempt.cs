// W2-nightly S15: the release-selection attempt reader (plan 2026-11-21-w2-nightly-port-slices.md, slice S15 and section 6
// item 2), moved from scripts/lib/release-selection.ps1: Read-ReleaseSelectionAttempt, Assert-ReleaseSelectionCapability
// and Resolve-ReleaseSelectionMembers, with the two helpers the reader uses (Assert-ReleaseSelectionPath,
// Get-ReleaseSelectionTrainDigest). The PowerShell functions of those three names are thin calls into this class;
// New-ReleaseSelectionAttempt stays PowerShell (W2-rc, through the worker protocol). CARD-1313 S6-A: intent only, every
// admitted attempt executes Full. A refusal is thrown as NightlyRefusal with the reason the PowerShell threw.
// Plain C#: the documents are read through NightlyJson (PSObjects under PowerShell, PlainObjects otherwise). ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// What a receiving hop expects of a selection attempt (the $Expected / $Request hashtable of release-selection.ps1).
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

    /// <summary>The selection attempt a receiving hop reads and the capability and member rules around it.</summary>
    public static class ReleaseSelectionAttempt
    {
        /// <summary>The attempt document's format.</summary>
        public const string AttemptFormat = "antiphon-release-selection-attempt-v1";

        /// <summary>The selection document's format.</summary>
        public const string SelectionFormat = "antiphon-release-selection-v1";

        private static readonly Regex Sha256Hex = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex OriginPrefix = new Regex("^origin/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[][] HopFields =
        {
            new[] { "candidateSha", "CandidateSha" }, new[] { "baselineSha", "BaselineSha" }, new[] { "requestedMode", "SelectionMode" },
            new[] { "runId", "RunId" }, new[] { "attempt", "Attempt" }, new[] { "profile", "Profile" }, new[] { "timingRoot", "TimingRoot" },
            new[] { "releaseIntentPath", "ReleaseIntentPath" }, new[] { "trainManifestPath", "TrainManifestPath" },
            new[] { "baselineReceiptPath", "BaselineReceiptPath" }, new[] { "qualificationPath", "QualificationPath" },
        };

        /// <summary>
        /// Assert-ReleaseSelectionCapability: the pwsh application on the PATH (pathValue; null reads this process's PATH),
        /// or null when there is none. A selection mode other than Full (ordinal) without pwsh refuses selection-pwsh-required.
        /// </summary>
        public static string AssertCapability(string selectionMode, string pathValue = null)
        {
            var pwsh = FindApplication("pwsh", pathValue ?? Environment.GetEnvironmentVariable("PATH"));
            if (!string.Equals(selectionMode, "Full", StringComparison.Ordinal) && pwsh == null) throw new NightlyRefusal("selection-pwsh-required");
            return pwsh;
        }

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

        // Get-Command <name> -CommandType Application: the first PATH entry holding name (on Windows with a PATHEXT extension).
        private static string FindApplication(string name, string pathValue)
        {
            if (string.IsNullOrEmpty(pathValue)) return null;
            var windows = Path.DirectorySeparatorChar == '\\';
            var extensions = windows
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';').Where(e => e.Length > 0).ToArray()
                : new[] { "" };
            foreach (var directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (var extension in extensions)
                {
                    string candidate;
                    try { candidate = Path.Combine(directory.Trim().Trim('"'), name + extension.ToLower(CultureInfo.InvariantCulture)); }
                    catch (ArgumentException) { continue; }
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }
    }
}
