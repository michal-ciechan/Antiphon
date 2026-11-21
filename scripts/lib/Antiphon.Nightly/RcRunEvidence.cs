// CARD-1836 S6b. Shadow seals for source, build output, discovery and open chunk revisions.
// Recording never grants gate credit and never publishes a closed green pointer.
// Each call throws its refusal token (InvalidOperationException); RcRunEvidenceRecorder adds the off and suppressed
// modes, the evidence root and the warn-once policy. Plain C#: no PowerShell types.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    public static class RcRunEvidence
    {
        public static RcRunEvidenceRecord SourceSnapshot(string evidenceRoot, string repository, string[] inputPaths, string expectedSha)
        {
            var root = RequireEvidence(evidenceRoot, repository);
            var repo = RequireRepo(repository, root);
            using (var gate = Acquire(root))
            {
                var snap = Capture(repo, inputPaths, expectedSha);
                var body = SourceJson(snap);
                var path = Path.Combine(root, "source.json");
                var digest = WriteOnce(path, body);
                return new RcRunEvidenceRecord { Digest = digest, Sha = snap.Sha, Clean = snap.Clean };
            }
        }

        public static RcRunEvidenceRecord Build(string evidenceRoot, string suite, string command, string outputDirectory, string entry, string sourceDigest, string repository, string sha)
        {
            RequireToken(suite, "suite-refused");
            var root = RequireEvidence(evidenceRoot, repository);
            using (var gate = Acquire(root))
            {
                var path = Path.Combine(root, "builds", suite + ".json");
                if (File.Exists(path)) throw new InvalidOperationException("build-already-recorded");
                if (!string.IsNullOrEmpty(repository)) AssertCurrent(root, RequireRepo(repository, root), sha);
                if (!string.IsNullOrEmpty(sourceDigest)) AssertSourceDigest(root, sourceDigest);
                var dir = RequireDirectory(outputDirectory);
                var outputs = SealDirectory(dir, entry);
                var body = BuildJson(suite, command, dir, entry, sourceDigest, outputs);
                var digest = WriteOnce(path, body);
                return new RcRunEvidenceRecord { Digest = digest, Suite = suite, Outputs = OutputNotes(outputs) };
            }
        }

        public static RcRunEvidenceRecord Discovery(string evidenceRoot, string suite, string discoveryPath, bool carry, bool skip)
        {
            RequireToken(suite, "suite-refused");
            if (carry && skip) throw new InvalidOperationException("carry-cannot-skip-discovery");
            if (skip) throw new InvalidOperationException("discovery-required");
            var root = RequireEvidence(evidenceRoot, null);
            using (var gate = Acquire(root))
            {
                var path = Path.Combine(root, "discovery", suite + ".json");
                if (File.Exists(path)) throw new InvalidOperationException("discovery-already-recorded");
                string discoveryDigest = "";
                if (!string.IsNullOrEmpty(discoveryPath))
                {
                    if (!File.Exists(discoveryPath)) throw new InvalidOperationException("discovery-required");
                    RejectReparseFile(discoveryPath);
                    discoveryDigest = Sha(File.ReadAllBytes(discoveryPath));
                }
                var obj = BaseObject("antiphon-rc-run-discovery-v1");
                obj["suite"] = suite;
                obj["carry"] = carry;
                obj["skipped"] = false;
                obj["discoverySha256"] = discoveryDigest;
                var digest = WriteOnce(path, Bytes(obj));
                return new RcRunEvidenceRecord { Digest = digest, Suite = suite };
            }
        }

        public static RcRunEvidenceRecord Launch(string evidenceRoot, string suite, string chunk, string outputDirectory, string[] arguments, string buildDigest, string repository, string expectedSha)
        {
            RequireToken(suite, "suite-refused");
            RequireToken(chunk, "chunk-refused");
            var root = RequireEvidence(evidenceRoot, repository);
            using (var gate = Acquire(root))
            {
                if (!string.IsNullOrEmpty(repository)) AssertCurrent(root, RequireRepo(repository, root), expectedSha);
                var buildPath = Path.Combine(root, "builds", suite + ".json");
                if (!File.Exists(buildPath)) throw new InvalidOperationException("output-not-joined");
                var buildBytes = ReadCapped(buildPath);
                var buildHash = Sha(buildBytes);
                if (!string.IsNullOrEmpty(buildDigest) && !string.Equals(buildDigest, buildHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("output-not-joined");
                using (var doc = JsonDocument.Parse(buildBytes))
                {
                    var storedDir = doc.RootElement.GetProperty("outputDirectory").GetString();
                    var launchDir = RequireDirectory(outputDirectory);
                    if (HasNoBuild(arguments) && !string.Equals(Normalize(storedDir), launchDir, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("foreign-output");
                    var storedOutputs = doc.RootElement.GetProperty("outputs");
                    if (!StampsMatch(storedOutputs, launchDir))
                    {
                        var live = SealDirectory(launchDir, doc.RootElement.GetProperty("entry").GetString());
                        if (!SameOutputs(storedOutputs, live))
                            throw new InvalidOperationException("output-not-joined");
                    }
                    var launchPath = Path.Combine(root, "launches", suite, chunk + ".json");
                    if (File.Exists(launchPath)) throw new InvalidOperationException("write-once");
                    var obj = BaseObject("antiphon-rc-run-launch-v1");
                    obj["suite"] = suite;
                    obj["chunk"] = chunk;
                    obj["buildDigest"] = buildHash;
                    obj["outputDirectory"] = launchDir;
                    obj["joined"] = true;
                    var digest = WriteOnce(launchPath, Bytes(obj));
                    return new RcRunEvidenceRecord { Digest = digest, Suite = suite, Joined = true };
                }
            }
        }

        public static RcRunEvidenceRecord ChunkRevision(string evidenceRoot, string suite, string chunk, string outcome, bool interrupt, string reason)
        {
            RequireToken(suite, "suite-refused");
            RequireToken(chunk, "chunk-refused");
            var root = RequireEvidence(evidenceRoot, null);
            using (var gate = Acquire(root))
            {
                var current = ReadIndex(root, false);
                var next = NextRevisionSlot(root, current);
                var dir = Path.Combine(root, "revisions", next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (interrupt)
                {
                    Directory.CreateDirectory(dir);
                    var partial = Path.Combine(dir, "revision.json.partial");
                    if (File.Exists(partial)) throw new InvalidOperationException("write-once");
                    using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var torn = Encoding.UTF8.GetBytes("{");
                        stream.Write(torn, 0, torn.Length);
                        stream.Flush(true);
                    }
                    throw new InvalidOperationException("writer-interrupted");
                }
                if (string.IsNullOrEmpty(outcome)) throw new InvalidOperationException("chunk-refused");
                var revision = BaseObject("antiphon-rc-run-revision-v1");
                revision["sequence"] = next;
                revision["state"] = "open";
                revision["suite"] = suite;
                revision["chunk"] = chunk;
                revision["outcome"] = outcome;
                revision["pending"] = "none";
                if (!string.IsNullOrEmpty(reason)) revision["reason"] = reason;
                var relative = "revisions/" + next.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/revision.json";
                var digest = PublishRevision(dir, Bytes(revision));
                var index = BaseObject("antiphon-rc-run-evidence-index-v1");
                index["sequence"] = next;
                index["path"] = relative;
                index["sha256"] = digest;
                index["state"] = "open";
                index["pending"] = "none";
                PublishIndex(root, Bytes(index));
                return new RcRunEvidenceRecord { Digest = digest, Suite = suite, Sequence = next };
            }
        }

        public static RcRunEvidenceRecord AssertInputs(string evidenceRoot, string repository, string expectedSha)
        {
            var root = RequireEvidence(evidenceRoot, repository);
            var repo = RequireRepo(repository, root);
            using (var gate = Acquire(root))
            {
                AssertCurrent(root, repo, expectedSha);
                var digest = Sha(ReadCapped(Path.Combine(root, "source.json")));
                return new RcRunEvidenceRecord { Digest = digest };
            }
        }

        public static RcRunEvidenceRecord Read(string evidenceRoot)
        {
            var root = RequireEvidence(evidenceRoot, null);
            RejectReparseChain(root);
            var index = ReadIndex(root, true);
            var pending = Pending(root);
            if (index == null)
            {
                if (string.Equals(pending, "not-run", StringComparison.Ordinal))
                    return new RcRunEvidenceRecord { Pending = "not-run" };
                throw new InvalidOperationException("writer-interrupted");
            }
            return new RcRunEvidenceRecord { Digest = index.Sha256, Sequence = index.Sequence, Pending = pending };
        }

        sealed class Gate : IDisposable
        {
            public FileStream Stream;
            public void Dispose() { if (Stream != null) Stream.Dispose(); }
        }

        sealed class Snap
        {
            public string Sha;
            public string Tree;
            public bool Clean;
            public string Porcelain;
            public List<InputRow> Inputs;
        }

        sealed class InputRow
        {
            public string Path;
            public string Sha256;
        }

        sealed class SealedFile
        {
            public string RelativePath;
            public string Sha256;
            public int Length;
            public long LastWriteUtcTicks;
            public string Role;
        }

        sealed class IndexInfo
        {
            public int Sequence;
            public string RelativePath;
            public string Sha256;
        }

        static Gate Acquire(string root)
        {
            Directory.CreateDirectory(root);
            RejectReparseChain(root);
            var gate = new Gate();
            try
            {
                gate.Stream = new FileStream(Path.Combine(root, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                throw new InvalidOperationException("writer-busy");
            }
            return gate;
        }

        static string RequireEvidence(string evidenceRoot, string repository)
        {
            if (string.IsNullOrWhiteSpace(evidenceRoot)) throw new InvalidOperationException("evidence-inside-checkout");
            var root = Normalize(evidenceRoot);
            RejectReparseChain(root);
            if (!string.IsNullOrWhiteSpace(repository))
            {
                var repo = Normalize(repository);
                if (IsSameOrInside(root, repo) || IsSameOrInside(repo, root)) throw new InvalidOperationException("evidence-inside-checkout");
            }
            return root;
        }

        static string RequireRepo(string repository, string evidenceRoot)
        {
            if (string.IsNullOrWhiteSpace(repository) || !Directory.Exists(repository)) throw new InvalidOperationException("source-moved");
            var repo = Normalize(repository);
            RejectReparseChain(repo);
            if (IsSameOrInside(evidenceRoot, repo) || IsSameOrInside(repo, evidenceRoot)) throw new InvalidOperationException("evidence-inside-checkout");
            return repo;
        }

        static string RequireDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) throw new InvalidOperationException("build-outputs-incomplete");
            var full = Normalize(directory);
            RejectReparseChain(full);
            return full;
        }

        static void RequireToken(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80 || !char.IsLetterOrDigit(value[0])) throw new InvalidOperationException(token);
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')) throw new InvalidOperationException(token);
            }
        }

        static Snap Capture(string repo, string[] inputPaths, string expectedSha)
        {
            var sha = Git(repo, "rev-parse", "HEAD");
            if (!IsSha(sha)) throw new InvalidOperationException("source-moved");
            if (!string.IsNullOrEmpty(expectedSha) && !string.Equals(expectedSha, sha, StringComparison.Ordinal)) throw new InvalidOperationException("source-moved");
            var tree = Git(repo, "rev-parse", "HEAD^{tree}");
            var porcelain = GitRaw(repo, "status", "--porcelain=v1", "--untracked-files=no");
            var inputs = new List<InputRow>();
            if (inputPaths != null)
            {
                foreach (var relative in inputPaths)
                {
                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    var full = ResolveInput(repo, relative);
                    if (!File.Exists(full)) throw new InvalidOperationException("inputs-changed");
                    RejectReparseFile(full);
                    inputs.Add(new InputRow { Path = relative.Replace('\\', '/'), Sha256 = Sha(File.ReadAllBytes(full)) });
                }
            }
            inputs.Sort(delegate (InputRow a, InputRow b) { return string.CompareOrdinal(a.Path, b.Path); });
            return new Snap { Sha = sha, Tree = tree.Trim(), Clean = porcelain.Trim().Length == 0, Porcelain = porcelain, Inputs = inputs };
        }

        static void AssertCurrent(string root, string repo, string expectedSha)
        {
            var path = Path.Combine(root, "source.json");
            if (!File.Exists(path)) throw new InvalidOperationException("inputs-changed");
            using (var doc = JsonDocument.Parse(ReadCapped(path)))
            {
                var storedSha = doc.RootElement.GetProperty("sha").GetString();
                var current = Git(repo, "rev-parse", "HEAD");
                if (!string.Equals(current, storedSha, StringComparison.Ordinal)) throw new InvalidOperationException("source-moved");
                if (!string.IsNullOrEmpty(expectedSha) && !string.Equals(expectedSha, current, StringComparison.Ordinal)) throw new InvalidOperationException("source-moved");
                if (doc.RootElement.TryGetProperty("inputs", out var inputs))
                {
                    foreach (var row in inputs.EnumerateArray())
                    {
                        var relative = row.GetProperty("path").GetString();
                        var full = ResolveInput(repo, relative);
                        if (!File.Exists(full)) throw new InvalidOperationException("inputs-changed");
                        if (!string.Equals(Sha(File.ReadAllBytes(full)), row.GetProperty("sha256").GetString(), StringComparison.Ordinal))
                            throw new InvalidOperationException("inputs-changed");
                    }
                }
                var clean = doc.RootElement.TryGetProperty("clean", out var cleanEl) && cleanEl.ValueKind == JsonValueKind.True;
                if (clean && GitRaw(repo, "status", "--porcelain=v1", "--untracked-files=no").Trim().Length > 0)
                    throw new InvalidOperationException("inputs-changed");
            }
        }

        static void AssertSourceDigest(string root, string sourceDigest)
        {
            var path = Path.Combine(root, "source.json");
            if (!File.Exists(path)) return;
            if (!string.Equals(Sha(ReadCapped(path)), sourceDigest, StringComparison.Ordinal)) throw new InvalidOperationException("source-moved");
        }

        static string ResolveInput(string repo, string relative)
        {
            var text = relative.Replace('\\', '/');
            if (Path.IsPathRooted(relative) || text.Length == 0) throw new InvalidOperationException("input-path-refused");
            var parts = text.Split('/');
            foreach (var part in parts)
            {
                if (part.Length == 0 || part == "." || part == "..") throw new InvalidOperationException("input-path-refused");
            }
            var full = Normalize(Path.Combine(repo, text.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(full, repo)) throw new InvalidOperationException("input-path-refused");
            return full;
        }

        static List<SealedFile> SealDirectory(string directory, string entry)
        {
            var names = new List<string>();
            foreach (var pattern in new[] { "*.dll", "*.deps.json", "*.runtimeconfig.json" })
            {
                foreach (var file in Directory.GetFiles(directory, pattern))
                {
                    RejectReparseFile(file);
                    names.Add(Path.GetFileName(file));
                }
            }
            names.Sort(StringComparer.Ordinal);
            if (names.Count == 0) throw new InvalidOperationException("build-outputs-incomplete");
            if (!string.IsNullOrEmpty(entry) && !names.Contains(entry)) throw new InvalidOperationException("build-outputs-incomplete");
            var list = new List<SealedFile>();
            var seenHelper = false;
            foreach (var name in names)
            {
                var info = new FileInfo(Path.Combine(directory, name));
                var bytes = File.ReadAllBytes(info.FullName);
                var role = string.Equals(name, entry, StringComparison.Ordinal) ? "entry" : (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "helper" : "dependency");
                if (role != "entry") seenHelper = true;
                list.Add(new SealedFile { RelativePath = name, Sha256 = Sha(bytes), Length = bytes.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks, Role = role });
            }
            if (!seenHelper && names.Count > 1) throw new InvalidOperationException("build-outputs-incomplete");
            return list;
        }

        static bool SameOutputs(JsonElement stored, List<SealedFile> live)
        {
            if (stored.GetArrayLength() != live.Count) return false;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in live) map[row.RelativePath] = row.Sha256;
            foreach (var row in stored.EnumerateArray())
            {
                var name = row.GetProperty("relativePath").GetString();
                string hash;
                if (!map.TryGetValue(name, out hash)) return false;
                if (!string.Equals(hash, row.GetProperty("sha256").GetString(), StringComparison.Ordinal)) return false;
                map.Remove(name);
            }
            return map.Count == 0;
        }

        static bool HasNoBuild(string[] arguments)
        {
            if (arguments == null) return false;
            foreach (var arg in arguments)
            {
                if (string.Equals(arg, "--no-build", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static IndexInfo ReadIndex(string root, bool verify)
        {
            var path = Path.Combine(root, "index.json");
            if (!File.Exists(path)) return null;
            RejectReparseFile(path);
            var bytes = ReadCapped(path);
            using (var doc = JsonDocument.Parse(bytes))
            {
                var el = doc.RootElement;
                if (el.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("closed-green-refused");
                if (Closed(el)) throw new InvalidOperationException("closed-green-refused");
                if (!verify) 
                {
                    if (!el.TryGetProperty("sequence", out var seqEl) || !seqEl.TryGetInt32(out var sequence) || sequence < 1) return null;
                    return new IndexInfo { Sequence = sequence };
                }
                if (!el.TryGetProperty("sequence", out var sequenceEl) || !sequenceEl.TryGetInt32(out var published) || published < 1)
                    throw new InvalidOperationException("writer-interrupted");
                var state = el.TryGetProperty("state", out var stateEl) ? stateEl.GetString() : "";
                if (!string.Equals(state, "open", StringComparison.Ordinal)) throw new InvalidOperationException("closed-green-refused");
                var relative = el.GetProperty("path").GetString();
                var expected = "revisions/" + published.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/revision.json";
                if (!string.Equals(relative, expected, StringComparison.Ordinal)) throw new InvalidOperationException("writer-interrupted");
                var digest = el.GetProperty("sha256").GetString();
                if (!IsSha(digest)) throw new InvalidOperationException("writer-interrupted");
                var revisionPath = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                RejectReparseFile(revisionPath);
                var revisionBytes = ReadCapped(revisionPath);
                if (!string.Equals(Sha(revisionBytes), digest, StringComparison.Ordinal)) throw new InvalidOperationException("writer-interrupted");
                using (var revision = JsonDocument.Parse(revisionBytes))
                {
                    if (Closed(revision.RootElement)) throw new InvalidOperationException("closed-green-refused");
                    var revisionState = revision.RootElement.GetProperty("state").GetString();
                    if (!string.Equals(revisionState, "open", StringComparison.Ordinal)) throw new InvalidOperationException("closed-green-refused");
                }
                return new IndexInfo { Sequence = published, RelativePath = relative, Sha256 = digest };
            }
        }

        static bool Closed(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return false;
            if (el.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && string.Equals(state.GetString(), "complete", StringComparison.Ordinal)) return true;
            if (el.TryGetProperty("gateCredit", out var credit) && credit.ValueKind == JsonValueKind.True) return true;
            if (el.TryGetProperty("coverageComplete", out var coverage) && coverage.ValueKind == JsonValueKind.True) return true;
            return false;
        }

        static int NextRevisionSlot(string root, IndexInfo current)
        {
            var next = (current == null ? 0 : current.Sequence) + 1;
            if (next < 1) next = 1;
            var revisions = Path.Combine(root, "revisions");
            for (var guard = 0; guard < 64; guard++)
            {
                var dir = Path.Combine(revisions, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!Directory.Exists(dir)) return next;
                next++;
            }
            throw new InvalidOperationException("write-once");
        }

        static string PublishRevision(string directory, byte[] bytes)
        {
            Directory.CreateDirectory(directory);
            var finalPath = Path.Combine(directory, "revision.json");
            var partial = Path.Combine(directory, "revision.json.partial");
            if (File.Exists(finalPath) || File.Exists(partial)) throw new InvalidOperationException("write-once");
            WriteOnce(partial, bytes);
            File.Move(partial, finalPath);
            var read = ReadCapped(finalPath);
            if (read.Length != bytes.Length) throw new InvalidOperationException("write-once");
            for (var i = 0; i < bytes.Length; i++) if (read[i] != bytes[i]) throw new InvalidOperationException("write-once");
            return Sha(read);
        }

        static bool ValidRevisionBytes(string path)
        {
            try
            {
                using (var doc = JsonDocument.Parse(ReadCapped(path)))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                    if (!doc.RootElement.TryGetProperty("format", out var fmt)) return false;
                    return string.Equals(fmt.GetString(), "antiphon-rc-run-revision-v1", StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Pending(string root)
        {
            var revisions = Path.Combine(root, "revisions");
            if (!Directory.Exists(revisions)) return "none";
            foreach (var dir in Directory.EnumerateDirectories(revisions))
            {
                var partial = Path.Combine(dir, "revision.json.partial");
                if (File.Exists(partial)) return "not-run";
                var finalPath = Path.Combine(dir, "revision.json");
                if (!File.Exists(finalPath) || !ValidRevisionBytes(finalPath)) return "not-run";
            }
            return "none";
        }

        static bool StampsMatch(JsonElement stored, string directory)
        {
            if (stored.ValueKind != JsonValueKind.Array) return false;
            var liveNames = new List<string>();
            foreach (var pattern in new[] { "*.dll", "*.deps.json", "*.runtimeconfig.json" })
            {
                foreach (var file in Directory.GetFiles(directory, pattern))
                {
                    RejectReparseFile(file);
                    liveNames.Add(Path.GetFileName(file));
                }
            }
            liveNames.Sort(StringComparer.Ordinal);
            if (liveNames.Count != stored.GetArrayLength() || liveNames.Count == 0) return false;
            var expected = new List<string>();
            foreach (var row in stored.EnumerateArray())
            {
                if (!row.TryGetProperty("lastWriteUtcTicks", out var ticksEl) || ticksEl.ValueKind != JsonValueKind.String) return false;
                if (!row.TryGetProperty("relativePath", out var nameEl)) return false;
                if (!row.TryGetProperty("length", out var lengthEl) || !lengthEl.TryGetInt32(out var length)) return false;
                var name = nameEl.GetString();
                if (string.IsNullOrEmpty(name)) return false;
                expected.Add(name);
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) return false;
                RejectReparseFile(path);
                var info = new FileInfo(path);
                if (info.Length != length) return false;
                var ticks = ticksEl.GetString();
                var liveTicks = info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(ticks, liveTicks, StringComparison.Ordinal)) return false;
            }
            expected.Sort(StringComparer.Ordinal);
            if (expected.Count != liveNames.Count) return false;
            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(expected[i], liveNames[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        static void PublishIndex(string root, byte[] bytes)
        {
            var temp = Path.Combine(root, "index-" + Guid.NewGuid().ToString("N") + ".tmp");
            WriteOnce(temp, bytes);
            var read = ReadCapped(temp);
            using (var doc = JsonDocument.Parse(read))
            {
                if (Closed(doc.RootElement)) throw new InvalidOperationException("closed-green-refused");
            }
            File.Move(temp, Path.Combine(root, "index.json"), true);
        }

        static byte[] SourceJson(Snap snap)
        {
            var obj = BaseObject("antiphon-rc-run-source-v1");
            obj["sha"] = snap.Sha;
            obj["tree"] = snap.Tree;
            obj["clean"] = snap.Clean;
            obj["porcelain"] = snap.Porcelain;
            var inputs = new JsonArray();
            foreach (var row in snap.Inputs)
            {
                var item = new JsonObject();
                item["path"] = row.Path;
                item["sha256"] = row.Sha256;
                inputs.Add(item);
            }
            obj["inputs"] = inputs;
            return Bytes(obj);
        }

        static byte[] BuildJson(string suite, string command, string directory, string entry, string sourceDigest, List<SealedFile> outputs)
        {
            var obj = BaseObject("antiphon-rc-run-build-v1");
            obj["suite"] = suite;
            obj["command"] = command ?? "";
            obj["outputDirectory"] = directory;
            obj["entry"] = entry ?? "";
            obj["sourceDigest"] = sourceDigest ?? "";
            var list = new JsonArray();
            foreach (var row in outputs)
            {
                var item = new JsonObject();
                item["relativePath"] = row.RelativePath;
                item["sha256"] = row.Sha256;
                item["length"] = row.Length;
                item["lastWriteUtcTicks"] = row.LastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                item["role"] = row.Role;
                list.Add(item);
            }
            obj["outputs"] = list;
            return Bytes(obj);
        }

        static JsonObject BaseObject(string format)
        {
            var obj = new JsonObject();
            obj["format"] = format;
            obj["effectiveMode"] = "Full";
            obj["gateCredit"] = false;
            obj["coverageComplete"] = false;
            obj["bindingState"] = "diagnostic-only";
            obj["publish"] = false;
            return obj;
        }

        static byte[] Bytes(JsonObject obj)
        {
            var text = obj.ToJsonString();
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 16777216) throw new InvalidOperationException("plan-file-limit");
            return bytes;
        }

        static RcRunEvidenceOutput[] OutputNotes(List<SealedFile> outputs)
        {
            var notes = new RcRunEvidenceOutput[outputs.Count];
            for (var i = 0; i < outputs.Count; i++)
                notes[i] = new RcRunEvidenceOutput(outputs[i].RelativePath, outputs[i].Sha256, outputs[i].Length, outputs[i].Role);
            return notes;
        }

        static string WriteOnce(string path, byte[] bytes)
        {
            if (File.Exists(path)) throw new InvalidOperationException("write-once");
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            var read = ReadCapped(path);
            if (read.Length != bytes.Length) throw new InvalidOperationException("write-once");
            for (var i = 0; i < bytes.Length; i++) if (read[i] != bytes[i]) throw new InvalidOperationException("write-once");
            return Sha(read);
        }

        static byte[] ReadCapped(string path)
        {
            RejectReparseFile(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16777216) throw new InvalidOperationException("plan-file-limit");
            return File.ReadAllBytes(path);
        }

        static string Sha(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        static bool IsSha(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64 && value.Length != 40) return false;
            foreach (var c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        static string Git(string repo, params string[] args)
        {
            return GitRaw(repo, args).Trim();
        }

        static string GitRaw(string repo, params string[] args)
        {
            var info = new ProcessStartInfo();
            info.FileName = "git";
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.WorkingDirectory = repo;
            info.Environment.Remove("GIT_DIR");
            info.Environment.Remove("GIT_WORK_TREE");
            info.Environment.Remove("GIT_INDEX_FILE");
            info.Environment.Remove("GIT_COMMON_DIR");
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(repo);
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using (var process = new Process())
            {
                process.StartInfo = info;
                if (!process.Start()) throw new InvalidOperationException("git-failed");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000))
                {
                    try { process.Kill(true); } catch (Exception) { }
                    throw new InvalidOperationException("git-timeout");
                }
                var text = stdout.GetAwaiter().GetResult();
                stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("git-failed");
                return text;
            }
        }

        static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        static bool IsSameOrInside(string child, string parent)
        {
            return string.Equals(child, parent, StringComparison.OrdinalIgnoreCase) || IsInside(child, parent);
        }

        static bool IsInside(string child, string parent)
        {
            return child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        static void RejectReparseChain(string path)
        {
            var current = path;
            for (var depth = 0; depth < 128 && !string.IsNullOrEmpty(current); depth++)
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("evidence-linked-path");
                }
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent;
            }
        }

        static void RejectReparseFile(string path)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("evidence-linked-path");
        }
    }

    /// <summary>
    /// What one shadow evidence call recorded, or the idle answer of a call that recorded nothing (Recording off,
    /// suppressed or unproven). The authority fields are constant: recording grants no gate credit, keeps Full mode
    /// and never publishes.
    /// </summary>
    public sealed class RcRunEvidenceRecord
    {
        public string EffectiveMode => "Full";
        public bool GateCredit => false;
        public bool CoverageComplete => false;
        public string BindingState => "diagnostic-only";
        public bool Publish => false;
        /// <summary>on, off, suppressed or unproven.</summary>
        public string Recording { get; init; } = "on";
        public string Digest { get; init; } = "";
        public string Sha { get; init; } = "";
        public bool Clean { get; init; }
        public string Suite { get; init; } = "";
        public bool Joined { get; init; }
        public bool Skipped { get; init; }
        public string State { get; init; } = "open";
        public int Sequence { get; init; }
        public string Pending { get; init; } = "none";
        public RcRunEvidenceOutput[] Outputs { get; init; } = new RcRunEvidenceOutput[0];

        public static RcRunEvidenceRecord Idle(string recording) => new RcRunEvidenceRecord { Recording = recording };
    }

    /// <summary>One sealed build output file (an entry, helper or dependency).</summary>
    public sealed class RcRunEvidenceOutput
    {
        public RcRunEvidenceOutput(string relativePath, string sha256, int length, string role)
        {
            RelativePath = relativePath;
            Sha256 = sha256;
            Length = length;
            Role = role;
        }

        public string RelativePath { get; }
        public string Sha256 { get; }
        public int Length { get; }
        public string Role { get; }
    }
}
