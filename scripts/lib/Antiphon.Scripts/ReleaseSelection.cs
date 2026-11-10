// select-release: New-ReleaseSelectionAttemptCore (scripts/lib/release-selection.ps1), with or without a baseline
// (Baseline.cs reads it; TestSelection.cs and RcProjectSelection.cs propose against it). It writes byte-for-byte the
// selection.json and selection-attempt.json the PowerShell worker writes and answers with the same response document.
//
// Handled only when every check passes on inputs it reads exactly as PowerShell does. Any refusal (bad commit,
// stale hash, invalid catalog, a corrupt receipt, a baseline that is not an ancestor, an attempt conflict, a path
// outside the release state) or an input shape outside PsJson is NotHandled: the caller then runs the PowerShell
// worker, which answers or refuses with its own message. So this path can be stricter than PowerShell, never laxer.
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

        /// <summary>scriptsLib: the scripts/lib directory of the bridge that asks, where the worker would run from.
        /// Only an attempt with a baseline needs it (the selector files and planner it holds); without it such an
        /// attempt is NotHandled.</summary>
        public static int Run(string requestPath, string responsePath, string scriptsLib = null)
        {
            object parsed = PsJson.Parse(File.ReadAllText(requestPath, Encoding.UTF8));
            if (!(parsed is PsObject request)) throw new NotHandledException("request-shape");
            var value = Attempt(request, scriptsLib);
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

        private static PsObject Attempt(PsObject request, string scriptsLib)
        {
            string repositoryRoot = Rooted(Text(request, "RepositoryRoot"), "RepositoryRoot");
            string releaseRoot = Rooted(Text(request, "ReleaseRoot"), "ReleaseRoot");
            string candidateSha = Text(request, "CandidateSha");
            string profile = Text(request, "Profile");
            string selectionMode = Text(request, "SelectionMode");
            string requestedBaseline = Text(request, "BaselineSha");
            if (!Truthy(profile)) throw new NotHandledException("profile-empty");

            AssertCommit(repositoryRoot, candidateSha);
            if (Truthy(requestedBaseline)) AssertCommit(repositoryRoot, requestedBaseline);
            PsObject contract = Contract(repositoryRoot, candidateSha, profile);
            var baseline = Baseline.Read(request, repositoryRoot, releaseRoot, contract);
            string b = baseline.Sha;
            if (Truthy(b))
            {
                AssertCommit(repositoryRoot, b);
                if (Git(repositoryRoot, new[] { "merge-base", "--is-ancestor", b, candidateSha }, new[] { 0, 1 }).Exit != 0) throw new NotHandledException("baseline-not-ancestor");
            }

            object proposal = null, projectProposal = null;
            var reasons = new List<string>();
            if (Truthy(baseline.Reason)) reasons.Add(baseline.Reason);
            if (Truthy(b))
            {
                if (!Truthy(scriptsLib) || !Path.IsPathFullyQualified(scriptsLib)) throw new NotHandledException("scripts-lib-required");
                var testSelection = TestSelection.Select(repositoryRoot, b, candidateSha, profile, scriptsLib, contract);
                proposal = testSelection;
                reasons.AddRange(((List<object>)testSelection.GetExact("fallbackReasons")).Cast<string>());
                if (!string.Equals(selectionMode, "Full", StringComparison.Ordinal))
                    projectProposal = RcProjectSelection.Select(repositoryRoot, b, candidateSha, scriptsLib);
            }
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
                ("format", "antiphon-release-selection-v1"), ("candidateSha", candidateSha), ("baselineSha", b),
                ("requestedMode", selectionMode), ("effectiveMode", "Full"), ("coverageScope", "Full"), ("proposal", proposal),
                ("projectProposal", projectProposal), ("fallbackReasons", PsSort.CultureUnique(reasons)), ("executionEvidence", false));
            return Save(request, repositoryRoot, releaseRoot, selection, contract, baseline.Digest, qualificationDigest);
        }

        // ----- Assert-ReleaseSelectionCommit / Get-ReleaseSelectionContract -----

        internal static readonly Regex FullSha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        internal static void AssertCommit(string root, string sha)
        {
            if (sha == null || !FullSha.IsMatch(sha)) throw new NotHandledException("selection-commit-required");
            var read = Git(root, new[] { "cat-file", "-t", sha }, new[] { 0, 128 });
            if (read.Exit != 0 || read.Stdout.Trim() != "commit") throw new NotHandledException("selection-commit-unavailable");
        }

        internal static CheckpointProcessRunner.Result Git(string root, string[] arguments, int[] allowed)
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

        private static readonly string[] ContractPaths = { "tests/test-execution-policy.json", "tests/test-selection-policy.json",
            "scripts/select-test-rosters.ps1", "scripts/lib/test-selection.ps1", "global.json" };

        /// <summary>The text of each path at a commit in two git processes: ls-tree -l gives each blob's size, and
        /// one show of every blob writes their bytes back to back, split by those sizes. Anything but regular blobs
        /// whose sizes add up to exactly the bytes written, or a blob that is not BOM-less strict UTF-8 (the one
        /// shape every PowerShell read of it decodes to the same text), is NotHandled. Answers are kept per
        /// process: the bytes at a full commit id cannot change.</summary>
        internal static Dictionary<string, string> Texts(string root, string sha, IReadOnlyList<string> paths)
        {
            if (sha == null || !FullSha.IsMatch(sha)) throw new NotHandledException("blob-sha");
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string path in paths)
            {
                if (texts.ContainsKey(path)) continue;
                if (TextCache.TryGetValue(sha + "\0" + path, out string cached)) texts.Add(path, cached);
                else if (!missing.Contains(path)) missing.Add(path);
            }
            for (int at = 0; at < missing.Count; at += 100)
            {
                var chunk = missing.Skip(at).Take(100).ToList();
                foreach (var pair in ReadChunk(root, sha, chunk))
                {
                    texts[pair.Key] = pair.Value;
                    TextCache[sha + "\0" + pair.Key] = pair.Value;
                }
            }
            return texts;
        }

        private static readonly Dictionary<string, string> TextCache = new Dictionary<string, string>(StringComparer.Ordinal);

        private static Dictionary<string, string> ReadChunk(string root, string sha, List<string> chunk)
        {
            var listing = Git(root, new[] { "--literal-pathspecs", "ls-tree", "-l", "-z", "--full-tree", sha, "--" }.Concat(chunk).ToArray(), new[] { 0 }).Stdout;
            var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string entry in listing.Split('\0'))
            {
                if (entry.Length == 0) continue;
                int tab = entry.IndexOf('	');
                string[] meta = tab < 0 ? new string[0] : entry.Substring(0, tab).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (meta.Length != 4 || meta[0] != "100644" && meta[0] != "100755" || meta[1] != "blob" || !long.TryParse(meta[3], out long size) || size < 0)
                    throw new NotHandledException("blob-entry-shape");
                string path = entry.Substring(tab + 1);
                if (!chunk.Contains(path) || sizes.ContainsKey(path)) throw new NotHandledException("blob-entry-shape");
                sizes.Add(path, size);
            }
            if (sizes.Count != chunk.Count) throw new NotHandledException("blob-entry-missing");
            var start = new ProcessStartInfo("git") { WorkingDirectory = Path.GetFullPath(root) };
            foreach (string argument in new[] { "show", "--no-textconv", "--no-color" }) start.ArgumentList.Add(argument);
            foreach (string path in chunk) start.ArgumentList.Add(sha + ":" + path);
            CheckpointProcessRunner.BytesResult shown;
            try { shown = CheckpointProcessRunner.RunBytes(start, 30000, true); }
            catch (Exception e) when (!(e is NotHandledException)) { throw new NotHandledException("git-failed:" + e.GetType().Name); }
            if (shown.TimedOut || shown.Exit != 0) throw new NotHandledException("git-read-failed:show");
            byte[] bytes = shown.Stdout;
            if (bytes.LongLength != sizes.Values.Sum() || bytes.LongLength > 16777216) throw new NotHandledException("blob-size");
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            int offset = 0;
            foreach (string path in chunk)
            {
                int length = (int)sizes[path];
                texts.Add(path, Decode(bytes, offset, length));
                offset += length;
            }
            return texts;
        }

        /// <summary>BOM-less strict UTF-8 only: with any byte order mark the PowerShell reads differ by route.</summary>
        internal static string Decode(byte[] bytes, int offset, int length)
        {
            if (length >= 2 && (bytes[offset] == 0xFE && bytes[offset + 1] == 0xFF || bytes[offset] == 0xFF && bytes[offset + 1] == 0xFE)) throw new NotHandledException("blob-bom");
            if (length >= 3 && bytes[offset] == 0xEF && bytes[offset + 1] == 0xBB && bytes[offset + 2] == 0xBF) throw new NotHandledException("blob-bom");
            try { return Strict.GetString(bytes, offset, length); }
            catch (DecoderFallbackException) { throw new NotHandledException("blob-encoding"); }
        }

        private static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);

        internal static PsObject Contract(string root, string sha, string profile)
        {
            var blob = Texts(root, sha, ContractPaths);
            string Blob(string unusedRoot, string unusedSha, string path) => blob[path];
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
        internal static string PolicyHash(PsObject policy)
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

        internal static string LfText(string text) => text == null ? "" : text.Replace("\r\n", "\n").Replace("\r", "\n");

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
                ("SelectionDigest", selectionDigest), ("BaselineSha", selection.Get("baselineSha")), ("Selection", attempt));
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

        internal static string AssertStatePath(string path, string root, string checkout)
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

        internal static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

        private static string ReadText(string path) => File.ReadAllText(path);

        internal static string FileSha256(string path) => PsJson.Sha256Hex(File.ReadAllBytes(path));

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
