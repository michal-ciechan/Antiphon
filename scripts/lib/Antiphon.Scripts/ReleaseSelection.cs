// select-release: New-ReleaseSelectionAttemptCore (scripts/lib/release-selection.ps1) for an attempt with no
// baseline, the case every first-rc and fresh-state run takes. It writes byte-for-byte the selection.json and
// selection-attempt.json the PowerShell worker writes and answers with the same response document.
//
// Handled only when every check passes on inputs it reads exactly as PowerShell does. A baseline (sha, receipt
// or archive), any refusal (bad commit, stale hash, invalid catalog, an attempt conflict, a path outside the
// release state) or an input shape outside PsJson is NotHandled: the caller then runs the PowerShell worker,
// which answers or refuses with its own message. So this path can be stricter than PowerShell, never laxer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Checkpoints;

namespace Antiphon.Scripts
{
    public static class ReleaseSelection
    {
        private static readonly bool Windows = Path.DirectorySeparatorChar == '\\';
        private static readonly StringComparison PathComparison = Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static int Run(string requestPath, string responsePath)
        {
            object parsed = PsJson.Parse(File.ReadAllText(requestPath, Encoding.UTF8));
            if (!(parsed is PsObject request)) throw new NotHandledException("request-shape");
            var value = Attempt(request);
            var response = new PsObject();
            response.Pairs.Add(new KeyValuePair<string, object>("ok", true));
            response.Pairs.Add(new KeyValuePair<string, object>("value", value));
            File.WriteAllText(responsePath, ResponseJson(response), new UTF8Encoding(false));
            return 0;
        }

        // ----- request fields ($Request.Name on the worker's -AsHashtable request) -----

        private static string Text(PsObject request, string name)
        {
            object v = request.GetExact(name);
            if (v == null) return null;
            if (v is string s) return s;
            throw new NotHandledException("request-type:" + name);
        }

        private static bool Truthy(string s) => !string.IsNullOrEmpty(s);

        private static string Rooted(string path, string name)
        {
            if (!Truthy(path) || !Path.IsPathRooted(path) || (Windows && !Path.IsPathFullyQualified(path))) throw new NotHandledException("path-not-rooted:" + name);
            return path;
        }

        private static PsObject Attempt(PsObject request)
        {
            string repositoryRoot = Rooted(Text(request, "RepositoryRoot"), "RepositoryRoot");
            string releaseRoot = Rooted(Text(request, "ReleaseRoot"), "ReleaseRoot");
            string candidateSha = Text(request, "CandidateSha");
            string profile = Text(request, "Profile");
            string selectionMode = Text(request, "SelectionMode");
            if (Truthy(Text(request, "BaselineSha")) || Truthy(Text(request, "BaselineReceiptPath"))) throw new NotHandledException("baseline-requested");
            if (!Truthy(profile)) throw new NotHandledException("profile-empty");

            AssertCommit(repositoryRoot, candidateSha);
            PsObject contract = Contract(repositoryRoot, candidateSha, profile);
            // Read-ReleaseSelectionBaseline with no receipt: an archive directory means a baseline may be chosen.
            if (PathExists(Path.Combine(releaseRoot, "baselines"))) throw new NotHandledException("baseline-archive");

            var reasons = new List<object> { "baseline-absent" };
            if (string.Equals(selectionMode, "ChangeAware", StringComparison.Ordinal)) reasons.Add("change-aware-unqualified");

            string qualificationDigest = "";
            string qualificationPath = Text(request, "QualificationPath");
            if (Truthy(qualificationPath) && PathExists(qualificationPath))
            {
                AssertStatePath(qualificationPath, releaseRoot, repositoryRoot);
                if (!File.Exists(qualificationPath)) throw new NotHandledException("qualification-not-file");
                if (!(PsJson.Parse(File.ReadAllText(qualificationPath, Encoding.UTF8)) is PsObject)) throw new NotHandledException("qualification-invalid");
                qualificationDigest = FileSha256(qualificationPath);
            }

            var selection = Obj(
                ("format", "antiphon-release-selection-v1"), ("candidateSha", candidateSha), ("baselineSha", ""),
                ("requestedMode", selectionMode), ("effectiveMode", "Full"), ("coverageScope", "Full"), ("proposal", null),
                ("projectProposal", null), ("fallbackReasons", reasons), ("executionEvidence", false));
            return Save(request, repositoryRoot, releaseRoot, selection, contract, "", qualificationDigest);
        }

        // ----- Assert-ReleaseSelectionCommit / Get-ReleaseSelectionContract -----

        private static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        private static void AssertCommit(string root, string sha)
        {
            if (sha == null || !FullSha.IsMatch(sha)) throw new NotHandledException("selection-commit-required");
            var read = Git(root, new[] { "cat-file", "-t", sha }, new[] { 0, 128 });
            if (read.Exit != 0 || read.Stdout.Trim() != "commit") throw new NotHandledException("selection-commit-unavailable");
        }

        private static CheckpointProcessRunner.Result Git(string root, string[] arguments, int[] allowed)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = Path.GetFullPath(root), StandardOutputEncoding = new UTF8Encoding(false, true) };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            CheckpointProcessRunner.Result result;
            try { result = CheckpointProcessRunner.Run(start, 30000, true); }
            catch (Exception e) when (!(e is NotHandledException)) { throw new NotHandledException("git-failed:" + e.GetType().Name); }
            if (result.TimedOut || Array.IndexOf(allowed, result.Exit) < 0) throw new NotHandledException("git-read-failed:" + arguments[0]);
            if (result.Stdout.Length > 16777216) throw new NotHandledException("git-output-too-large");
            return result;
        }

        private static string Blob(string root, string sha, string path) =>
            Git(root, new[] { "cat-file", "blob", sha + ":" + path }, new[] { 0 }).Stdout;

        private static PsObject Contract(string root, string sha, string profile)
        {
            if (!(PsJson.Parse(Blob(root, sha, "tests/test-execution-policy.json")) is PsObject policy)) throw new NotHandledException("policy-shape");
            if (!(policy.Get("policyHash") is string policyHash) || policyHash != PolicyHash(policy)) throw new NotHandledException("policy-hash-stale");
            AssertProfile(policy, profile);
            if (!(PsJson.Parse(Blob(root, sha, "tests/test-selection-policy.json")) is PsObject catalog)) throw new NotHandledException("catalog-shape");
            SelectionCatalog.Assert(catalog);
            string catalogHash = (string)catalog.GetExact("catalogHash");
            if (!(policy.Get("selectionCatalog") is PsObject pin) || !(pin.Get("hash") is string pinned) || pinned != catalogHash) throw new NotHandledException("catalog-pin-mismatch");
            var selectors = new PsObject();
            foreach (string path in new[] { "scripts/select-test-rosters.ps1", "scripts/lib/test-selection.ps1" })
                selectors.Pairs.Add(new KeyValuePair<string, object>(path, SelectionDigest(LfText(Blob(root, sha, path)))));
            return Obj(("policyHash", policyHash), ("catalogHash", catalogHash), ("selectorHash", SelectionDigest(selectors)),
                ("runtimeHash", SelectionDigest(Blob(root, sha, "global.json"))), ("profile", profile),
                ("platform", Windows ? "windows" : "linux"));
        }

        /// <summary>Get-NightlyPolicyHash: the canonical JSON of every top-level key but policyHash (any case).</summary>
        private static string PolicyHash(PsObject policy)
        {
            var copy = new PsObject();
            foreach (var pair in policy.Pairs)
                if (!string.Equals(pair.Key, "policyHash", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Key)) copy.Pairs.Add(pair);
            return PsJson.Sha256Text(PsJson.NightlyCanonical(copy));
        }

        /// <summary>Get-NightlyPolicyProfile, for a schema-2+ policy naming the profile with required suites.</summary>
        private static void AssertProfile(PsObject policy, string profile)
        {
            string name = profile.Trim().ToLowerInvariant();
            if (name.Length == 0) name = "nightly";
            if (name != "nightly" && name != "rc") throw new NotHandledException("unknown-profile");
            if (!(policy.Get("profiles") is PsObject profiles)) throw new NotHandledException("policy-profiles");
            if (!(profiles.Get(name) is PsObject row)) throw new NotHandledException("profile-undefined");
            object required = row.Get("requiredSuites");
            bool any = required is string one ? !string.IsNullOrWhiteSpace(one)
                : required is List<object> list && list.Any(id => id is string s && !string.IsNullOrWhiteSpace(s));
            if (!any) throw new NotHandledException("profile-no-suites");
        }

        /// <summary>Get-TestSelectionDigest.</summary>
        internal static string SelectionDigest(object value) => PsJson.Sha256Text(PsJson.SelectionCanonical(value));

        private static string LfText(string text) => text == null ? "" : text.Replace("\r\n", "\n").Replace("\r", "\n");

        // ----- Save-ReleaseSelectionAttempt -----

        private static readonly Regex Identity = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant);

        private static PsObject Save(PsObject request, string repositoryRoot, string releaseRoot, PsObject selection, PsObject contract,
            string baselineDigest, string qualificationDigest)
        {
            string runId = Text(request, "RunId"), candidateId = Text(request, "CandidateId");
            object attemptValue = request.GetExact("Attempt");
            if (!(attemptValue is long attemptNumber) || attemptNumber < 1 || attemptNumber > int.MaxValue) throw new NotHandledException("selection-run-identity");
            if (runId == null || !Identity.IsMatch(runId)) throw new NotHandledException("selection-run-identity");
            if (candidateId == null || !Identity.IsMatch(candidateId)) throw new NotHandledException("selection-candidate-identity");
            string dir = Path.Combine(releaseRoot, "candidates", candidateId, "attempts", runId);
            string path = AssertStatePath(Path.Combine(dir, "selection-attempt.json"), releaseRoot, repositoryRoot);
            string selectionPath = AssertStatePath(Path.Combine(dir, "selection.json"), releaseRoot, repositoryRoot);
            string selectionText = PsJson.NightlyCanonical(selection);
            string selectionDigest = PsJson.Sha256Text(selectionText);
            var attempt = Obj(
                ("format", "antiphon-release-selection-attempt-v1"), ("repository", Text(request, "Repository")), ("candidateId", candidateId),
                ("candidateRef", Text(request, "CandidateRef")), ("candidateSha", Text(request, "CandidateSha")), ("baselineSha", selection.Get("baselineSha")),
                ("requestedMode", Text(request, "SelectionMode")), ("effectiveMode", "Full"), ("coverageScope", "Full"), ("profile", Text(request, "Profile")),
                ("runId", runId), ("attempt", (int)attemptNumber), ("selectionDigest", selectionDigest), ("contract", contract),
                ("baselineDigest", baselineDigest), ("qualificationDigest", qualificationDigest), ("trainDigest", TrainDigest(request, releaseRoot, repositoryRoot)),
                ("fallbackReasons", selection.Get("fallbackReasons")),
                ("baselineReceiptPath", Text(request, "BaselineReceiptPath")), ("qualificationPath", Text(request, "QualificationPath")),
                ("timingRoot", Text(request, "TimingRoot")), ("releaseIntentPath", Text(request, "ReleaseIntentPath")), ("trainManifestPath", Text(request, "TrainManifestPath")));
            string attemptText = PsJson.NightlyCanonical(attempt);
            // Both immutable files are checked before either write; a conflict is the worker's refusal to give.
            foreach (var (file, text) in new[] { (path, attemptText), (selectionPath, selectionText) })
                if (PathExists(file) && ReadText(file) != text) throw new NotHandledException("selection-attempt-conflict");
            WriteOnce(selectionPath, selectionText);
            WriteOnce(path, attemptText);
            return Obj(("SelectionAttemptPath", path), ("SelectionAttemptDigest", FileSha256(path)), ("SelectionPath", selectionPath),
                ("SelectionDigest", selectionDigest), ("BaselineSha", ""), ("Selection", attempt));
        }

        private static string TrainDigest(PsObject request, string releaseRoot, string repositoryRoot)
        {
            string train = Text(request, "TrainManifestPath");
            if (!Truthy(train)) return "";
            string path = AssertStatePath(train, releaseRoot, repositoryRoot);
            if (!PathExists(path)) return "";
            if (!File.Exists(path)) throw new NotHandledException("selection-train-input");
            return FileSha256(path);
        }

        private static void WriteOnce(string path, string text)
        {
            if (PathExists(path))
            {
                if (ReadText(path) != text) throw new NotHandledException("selection-attempt-conflict");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                try { File.Move(temp, path); }
                catch (Exception)
                {
                    if (!PathExists(path) || ReadText(path) != text) throw new NotHandledException("selection-attempt-conflict");
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        // ----- paths (Assert-ReleaseSelectionPath, Test-NightlyAncestorReparse) -----

        private static string AssertStatePath(string path, string root, string checkout)
        {
            string full = Path.GetFullPath(Rooted(path, "state"));
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, PathComparison) || AncestorReparse(full)) throw new NotHandledException("selection-path-outside-state");
            if (Truthy(checkout))
            {
                string disposable = Path.GetFullPath(checkout).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(disposable, PathComparison)) throw new NotHandledException("selection-path-in-checkout");
            }
            return full;
        }

        private static bool AncestorReparse(string path)
        {
            string canonical = Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            string root = Path.GetPathRoot(canonical);
            if (canonical.Length > root.Length) canonical = canonical.TrimEnd(Path.DirectorySeparatorChar);
            string cursor = canonical;
            while (cursor.Length > root.Length)
            {
                if (PathExists(cursor) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return true;
                string next = Path.GetDirectoryName(cursor);
                if (string.IsNullOrWhiteSpace(next) || next == cursor) break;
                cursor = next;
            }
            return false;
        }

        private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        private static string ReadText(string path) => File.ReadAllText(path);

        private static string FileSha256(string path) => PsJson.Sha256Hex(File.ReadAllBytes(path));

        internal static PsObject Obj(params (string Key, object Value)[] pairs)
        {
            var obj = new PsObject();
            foreach (var (key, value) in pairs) obj.Pairs.Add(new KeyValuePair<string, object>(key, value));
            return obj;
        }

        // The response the worker writes (ConvertTo-Json @{ok;value}); the caller reads it with ConvertFrom-Json.
        private static string ResponseJson(object node)
        {
            using var buffer = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(buffer, new System.Text.Json.JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                WriteResponse(writer, node);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static void WriteResponse(System.Text.Json.Utf8JsonWriter writer, object node)
        {
            switch (node)
            {
                case null: writer.WriteNullValue(); break;
                case string s: writer.WriteStringValue(s); break;
                case bool b: writer.WriteBooleanValue(b); break;
                case int i: writer.WriteNumberValue(i); break;
                case long l: writer.WriteNumberValue(l); break;
                case PsObject obj:
                    writer.WriteStartObject();
                    foreach (var pair in obj.Pairs) { writer.WritePropertyName(pair.Key); WriteResponse(writer, pair.Value); }
                    writer.WriteEndObject();
                    break;
                case System.Collections.IEnumerable items:
                    writer.WriteStartArray();
                    foreach (object item in items) WriteResponse(writer, item);
                    writer.WriteEndArray();
                    break;
                default: throw new NotHandledException("response-type");
            }
        }
    }
}
