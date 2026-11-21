// CARD-1015/CARD-1024 (moved from scripts/lib/evidence-policy.ps1): the read-only Git object policy over .antiphon/
// evidence. History is the proposed-history guard (antiphon-scripts evidence-diff); Deletion is the start-tree inventory
// and the exact marked-deletion validation (antiphon-scripts evidence-deletion). Both read pinned Git objects only,
// through the one I/O seam EvidenceGit, and write EVIDENCE lines to the given writer. Exit 0: compliant. 1: policy
// violations. 2: the input, the history or Git cannot be verified ("EVIDENCE error reason=...").
// Plain C#: no PowerShell types. String comparisons are ordinal. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One git run: its exit code and its raw stdout bytes.</summary>
    public sealed class EvidenceGitResult
    {
        public EvidenceGitResult(int exitCode, byte[] bytes)
        {
            ExitCode = exitCode;
            Bytes = bytes;
        }

        public int ExitCode { get; }
        public byte[] Bytes { get; }
    }

    /// <summary>The policy's only I/O: run git with these arguments in this repository (EvidencePolicy.NativeGit by default).</summary>
    public delegate EvidenceGitResult EvidenceGit(string repository, IReadOnlyList<string> arguments);

    /// <summary>antiphon-scripts evidence-diff: an explicit BaseRef..HeadRef, or the range a CI event selects.</summary>
    public sealed class EvidenceHistoryRequest
    {
        public string Repository { get; set; } = ".";
        public string BaseRef { get; set; }
        public string HeadRef { get; set; } = "HEAD";
        public string EventPath { get; set; }
        public string EventName { get; set; }
        public string ManualBase { get; set; }
        public string ManualHead { get; set; }
    }

    /// <summary>antiphon-scripts evidence-deletion: InventoryOnly prints the inventory; otherwise the supplied inventory is checked.</summary>
    public sealed class EvidenceDeletionRequest
    {
        public string Repository { get; set; } = ".";
        public string InventoryRef { get; set; }
        public string InventoryPathSha256 { get; set; }
        public string InventoryCount { get; set; }
        public string InventoryBytes { get; set; }
        public string HeadRef { get; set; } = "HEAD";
        public bool InventoryOnly { get; set; }
        /// <summary>Null: not given (CARD-1015's anchored contract). Any other value, empty included, opts in and must be CARD-nnnn.</summary>
        public string SupplementalCleanup { get; set; }
    }

    public static class EvidencePolicy
    {
        public const string Anchor = "bb5fa774cd56f85ee6f0b1122c198192427e5ddf";
        private const string AnchorPathSha256 = "356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c";
        private const string AnchorSignature = "f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b";
        private const int AnchorEntries = 108;
        private const long MaxBlobBytes = 1048576;

        private static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex Oid = new Regex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$");
        private static readonly Regex OldModeField = new Regex("^:[0-7]{6}$");
        private static readonly Regex ModeField = new Regex("^[0-7]{6}$");
        private static readonly Regex StatusField = new Regex("^[ADMT]$");
        private static readonly Regex Count = new Regex("^(0|[1-9][0-9]*)$");
        private static readonly Regex Sha256 = new Regex("^[0-9a-fA-F]{64}$");
        private static readonly Regex Card = new Regex("^CARD-[0-9]{4,}$");
        private static readonly Regex BranchRef = new Regex("^refs/heads/.+");
        private static readonly Regex Zeros = new Regex("^0+$");
        // approved-json-fixture: exact root, groups, basename and approved JSON suffix.
        private static readonly Regex ApprovedFixture = new Regex(@"\A\.antiphon/fixtures/(?:[A-Za-z0-9_-]+/)*[A-Za-z0-9_-]+\.approved\.json\z");

        /// <summary>The CLI's stdout: UTF-8 without a BOM over the raw stream, whatever Console.OutputEncoding says.</summary>
        public static StreamWriter OpenOutput(Stream stream) => new StreamWriter(stream, Utf8NoBom, 4096, leaveOpen: true) { AutoFlush = true };

        /// <summary>git in the repository: GIT_OPTIONAL_LOCKS=0, raw stdout bytes, stderr drained, 60 s then the tree is killed.</summary>
        public static EvidenceGitResult NativeGit(string repository, IReadOnlyList<string> arguments) => NativeGit(repository, arguments, null);

        /// <summary>NativeGit with extra environment variables for the child (a test fixture's isolated git configuration).</summary>
        public static EvidenceGitResult NativeGit(string repository, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repository ?? "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // stdout is copied raw from its stream and decoded strictly by the caller; stderr is only drained.
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            if (environment != null)
                foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            using var memory = new MemoryStream();
            if (!process.Start()) throw new EvidenceRefusal("git_start");
            var copy = process.StandardOutput.BaseStream.CopyToAsync(memory);
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                process.Kill(true);
                process.WaitForExit();
                copy.GetAwaiter().GetResult();
                error.GetAwaiter().GetResult();
                throw new EvidenceRefusal("git_timeout");
            }
            copy.GetAwaiter().GetResult();
            error.GetAwaiter().GetResult();
            return new EvidenceGitResult(process.ExitCode, memory.ToArray());
        }

        /// <summary>The proposed-history guard: every commit in base..head, merge sides and roots included, by no-renames raw diff.</summary>
        public static int History(EvidenceHistoryRequest request, TextWriter output, EvidenceGit git = null)
        {
            var s = new Session(request.Repository, git ?? NativeGit, output);
            try
            {
                s.AssertHistory();
                var baseRef = request.BaseRef;
                var headRef = request.HeadRef;
                if (!string.IsNullOrEmpty(request.EventPath) || !string.IsNullOrEmpty(request.EventName))
                {
                    if (!string.IsNullOrEmpty(baseRef)) throw new EvidenceRefusal("mixed_parameter_sets");
                    var range = s.EventRange(request.EventPath, request.EventName, request.ManualBase, request.ManualHead);
                    if (range == null)
                    {
                        output.WriteLine("EVIDENCE excluded deleted=true");
                        return 0;
                    }
                    baseRef = range[0];
                    headRef = range[1];
                }
                var @base = s.ResolveCommit(baseRef);
                var head = s.ResolveCommit(headRef);
                s.AssertAncestor(@base, head);
                output.WriteLine("EVIDENCE range base=" + @base + " head=" + head);
                var commits = s.Commits(@base, head);
                long entries = 0, violations = 0;
                foreach (var commit in commits)
                {
                    var parents = s.Parents(commit);
                    foreach (var entry in s.Diff(commit, parents.Count > 0 ? parents[0] : ""))
                    {
                        if (!IsEvidenceRoot(entry.Path)) continue;
                        entries++;
                        if (entry.Status == "D") continue;
                        var size = entry.Mode == "160000" ? 0L : s.BlobSize(entry.Oid);
                        var reason = Violation(entry.Path, entry.Mode, size);
                        if (reason.Length == 0) continue;
                        violations++;
                        s.WriteViolation(commit, entry.Path, reason, size);
                    }
                }
                output.WriteLine("EVIDENCE result commits=" + commits.Count + " entries=" + entries + " violations=" + violations + " base=" + @base + " head=" + head);
                return violations > 0 ? 1 : 0;
            }
            catch (Exception e)
            {
                output.WriteLine("EVIDENCE error reason=" + e.Message);
                return 2;
            }
        }

        /// <summary>The inventory of a start tree (InventoryOnly, one JSON line) or the validation of its exact marked deletion.</summary>
        public static int Deletion(EvidenceDeletionRequest request, TextWriter output, EvidenceGit git = null)
        {
            var s = new Session(request.Repository, git ?? NativeGit, output);
            try
            {
                var label = request.SupplementalCleanup;
                var supplemental = label != null;
                if (supplemental && (!Card.IsMatch(label) || label == "CARD-1015")) throw new EvidenceRefusal("supplemental_cleanup_input");
                var marker = "Antiphon-Evidence-Deletion: " + (supplemental ? label : "CARD-1015");
                long count = 0, bytes = 0;
                if (!request.InventoryOnly
                    && (!Sha256.IsMatch(request.InventoryPathSha256 ?? "") || !Count.IsMatch(request.InventoryCount ?? "") || !Count.IsMatch(request.InventoryBytes ?? "")
                        || !long.TryParse(request.InventoryCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
                        || !long.TryParse(request.InventoryBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes)))
                    throw new EvidenceRefusal("inventory_input");
                s.AssertHistory();
                var @base = s.ResolveCommit(request.InventoryRef);
                var anchor = new List<TreeEntry>();
                var signature = "notApplicable";
                if (!supplemental)
                {
                    anchor = s.Tree(s.ResolveCommit(Anchor)).Where(e => IsEvidenceRoot(e.Path)).ToList();
                    signature = PathHash(anchor, classification: true);
                }
                var tree = s.Tree(@base);
                var map = Map(tree, e => e.Path);
                var violations = 0;
                if (!supplemental && (anchor.Count != AnchorEntries || PathHash(anchor, false) != AnchorPathSha256 || signature != AnchorSignature))
                    violations += s.DeletionViolation("anchor_classification");
                foreach (var entry in anchor)
                {
                    if (!map.TryGetValue(entry.Path, out var current))
                    {
                        violations += s.DeletionViolation("anchor_present");
                        continue;
                    }
                    if (current.Oid != entry.Oid) violations += s.DeletionViolation("anchor_oid");
                    if (current.Mode != entry.Mode) violations += s.DeletionViolation("anchor_mode");
                }
                var scoped = tree.Where(e => IsEvidenceRoot(e.Path)).ToList();
                var delete = scoped.Where(e => Violation(e.Path, e.Mode, e.Bytes).Length > 0).ToList();
                var keep = scoped.Where(e => Violation(e.Path, e.Mode, e.Bytes).Length == 0).ToList();
                var digest = PathHash(delete, false);
                var total = delete.Sum(e => e.Bytes);
                if (request.InventoryOnly)
                {
                    output.WriteLine(InventoryJson(@base, delete, keep, total, digest, supplemental ? null : signature, violations == 0, label));
                    return violations > 0 ? 1 : 0;
                }
                var head = s.ResolveCommit(request.HeadRef);
                s.AssertAncestor(@base, head);
                output.WriteLine("EVIDENCE inventory base=" + @base + " suppliedCount=" + count + " suppliedBytes=" + bytes + " suppliedDigest=" + request.InventoryPathSha256
                    + " count=" + delete.Count + " bytes=" + total + " digest=" + digest + " anchor=" + signature + " kept=" + keep.Count + " head=" + head);
                if (count != delete.Count) violations += s.DeletionViolation("inventory_count");
                if (bytes != total) violations += s.DeletionViolation("inventory_bytes");
                if (request.InventoryPathSha256.ToLowerInvariant() != digest) violations += s.DeletionViolation("inventory_digest");
                var marked = new List<string>();
                foreach (var commit in s.Commits(@base, head))
                {
                    // The whole message must decode as strict UTF-8 as well, though only its trailers are matched.
                    Decode(s.Read("show", "-s", "--format=%B", commit));
                    var trailers = Decode(s.Read("show", "-s", "--format=%(trailers:only,unfold)", commit));
                    if (trailers.Replace("\r\n", "\n").Split('\n').Contains(marker, StringComparer.Ordinal)) marked.Add(commit);
                }
                if (marked.Count != 1)
                {
                    s.DeletionViolation("deletion_unique");
                    return 1;
                }
                var deletion = marked[0];
                var parents = s.Parents(deletion);
                if (parents.Count != 1)
                {
                    s.DeletionViolation("deletion_parent");
                    return 1;
                }
                s.AssertAncestor(@base, parents[0]);
                s.AssertAncestor(deletion, head);
                var actual = s.Diff(deletion, parents[0]);
                foreach (var entry in actual)
                    if (entry.Status != "D") violations += s.DeletionViolation("deletion_only");
                var actualMap = Map(actual, e => e.Path);
                var expectedMap = Map(delete, e => e.Path);
                foreach (var entry in delete)
                    if (!actualMap.ContainsKey(entry.Path)) violations += s.DeletionViolation("deletion_missing");
                foreach (var entry in actual)
                    if (!expectedMap.ContainsKey(entry.Path)) violations += s.DeletionViolation("deletion_extra");
                foreach (var entry in actual)
                {
                    if (!expectedMap.TryGetValue(entry.Path, out var expected)) continue;
                    if (entry.OldOid != expected.Oid) violations += s.DeletionViolation("deletion_old_oid");
                    if (entry.OldMode != expected.Mode) violations += s.DeletionViolation("deletion_old_mode");
                }
                var finalTree = s.Tree(head);
                var finalMap = Map(finalTree, e => e.Path);
                foreach (var entry in delete)
                    if (finalMap.ContainsKey(entry.Path)) violations += s.DeletionViolation("deletion_resurrection");
                foreach (var entry in keep)
                {
                    if (!finalMap.TryGetValue(entry.Path, out var current))
                    {
                        violations += s.DeletionViolation("kept_present");
                        continue;
                    }
                    if (current.Oid != entry.Oid) violations += s.DeletionViolation("kept_oid");
                    if (current.Mode != entry.Mode) violations += s.DeletionViolation("kept_mode");
                }
                var finalViolations = 0;
                foreach (var entry in finalTree)
                {
                    if (Violation(entry.Path, entry.Mode, entry.Bytes).Length == 0) continue;
                    finalViolations++;
                    s.WriteViolation(head, entry.Path, "final_tree_policy", entry.Bytes);
                }
                violations += finalViolations;
                var recoverable = 0;
                foreach (var entry in delete)
                {
                    if (entry.Type != "blob") continue;
                    s.Read("cat-file", "-e", entry.Oid);
                    recoverable++;
                }
                output.WriteLine("EVIDENCE deletion result base=" + @base + " deletion=" + deletion + " head=" + head + " deleted=" + delete.Count + " bytes=" + total
                    + " digest=" + digest + " kept=" + keep.Count + " finalTreeViolations=" + finalViolations + " recoverable=" + recoverable + " violations=" + violations);
                return violations > 0 ? 1 : 0;
            }
            catch (Exception e)
            {
                output.WriteLine("EVIDENCE error reason=" + e.Message);
                return 2;
            }
        }

        /// <summary>The policy reason for one Git entry, or "" when it is outside .antiphon/ or allowed.</summary>
        internal static string Violation(string path, string mode, long bytes)
        {
            if (!IsEvidenceRoot(path)) return "";
            var components = path.Split('/');
            for (var i = 1; i < components.Length - 1; i++)
                if (components[i].IndexOf("checkpoints", StringComparison.OrdinalIgnoreCase) >= 0) return "checkpoint_directory";
            if (mode != "100644" && mode != "100755") return "non_regular_mode";
            if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !ApprovedFixture.IsMatch(path)) return "non_markdown";
            if (bytes > MaxBlobBytes) return "oversize_blob";
            return "";
        }

        /// <summary>The first path component is .antiphon, in any case.</summary>
        internal static bool IsEvidenceRoot(string path)
        {
            var slash = path.IndexOf('/');
            return (slash < 0 ? path : path.Substring(0, slash)).Equals(".antiphon", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A JSON string literal escaped as PowerShell's ConvertTo-Json (Newtonsoft's default) escapes it.</summary>
        internal static string JsonString(string value)
        {
            var b = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }

        private static string Decode(byte[] bytes) => Strict.GetString(bytes);

        private static bool IsOid(string value) => Oid.IsMatch(value);

        private static bool IsSafePath(string path) => path.Length > 0 && !path.StartsWith('/') && !path.Split('/').Contains("..", StringComparer.Ordinal);

        private static string PathHash(IEnumerable<TreeEntry> entries, bool classification)
        {
            using var memory = new MemoryStream();
            foreach (var entry in entries)
            {
                memory.Write(Encoding.UTF8.GetBytes(entry.Path));
                memory.WriteByte(0);
                if (!classification) continue;
                memory.Write(Encoding.ASCII.GetBytes(Violation(entry.Path, entry.Mode, entry.Bytes).Length > 0 ? "delete" : "keep"));
                memory.WriteByte(0);
            }
            return Convert.ToHexString(SHA256.HashData(memory.ToArray())).ToLowerInvariant();
        }

        private static Dictionary<string, T> Map<T>(IEnumerable<T> entries, Func<T, string> path)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var entry in entries)
                if (!map.TryAdd(path(entry), entry)) throw new EvidenceRefusal("duplicate_path");
            return map;
        }

        // The InventoryOnly line: the fields and values the PowerShell's ConvertTo-Json wrote (KeepBytes is null for an empty
        // keep set, as Measure-Object's sum was). Supplemental cleanups carry SupplementalCleanup instead of the anchor fields.
        private static string InventoryJson(string @base, List<TreeEntry> delete, List<TreeEntry> keep, long total, string digest, string signature, bool anchorValid, string supplemental)
        {
            var b = new StringBuilder();
            b.Append("{\"Base\":").Append(JsonString(@base));
            b.Append(",\"Delete\":");
            Entries(b, delete);
            b.Append(",\"Keep\":");
            Entries(b, keep);
            b.Append(",\"Count\":").Append(delete.Count.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"Bytes\":").Append(total.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"PathSha256\":").Append(JsonString(digest));
            b.Append(",\"KeepCount\":").Append(keep.Count.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"KeepBytes\":").Append(keep.Count == 0 ? "null" : keep.Sum(e => e.Bytes).ToString(CultureInfo.InvariantCulture));
            b.Append(",\"KeepPathSha256\":").Append(JsonString(PathHash(keep, false)));
            if (supplemental == null)
            {
                b.Append(",\"AnchorSignature\":").Append(JsonString(signature));
                b.Append(",\"AnchorValid\":").Append(anchorValid ? "true" : "false");
            }
            else b.Append(",\"SupplementalCleanup\":").Append(JsonString(supplemental));
            return b.Append('}').ToString();
        }

        private static void Entries(StringBuilder b, List<TreeEntry> entries)
        {
            b.Append('[');
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (i > 0) b.Append(',');
                b.Append("{\"Path\":").Append(JsonString(e.Path)).Append(",\"Mode\":").Append(JsonString(e.Mode)).Append(",\"Oid\":").Append(JsonString(e.Oid))
                    .Append(",\"Bytes\":").Append(e.Bytes.ToString(CultureInfo.InvariantCulture)).Append(",\"Type\":").Append(JsonString(e.Type)).Append('}');
            }
            b.Append(']');
        }

        private sealed class EvidenceRefusal : Exception
        {
            internal EvidenceRefusal(string reason) : base(reason) { }
        }

        private sealed class TreeEntry
        {
            internal string Path, Mode, Oid, Type;
            internal long Bytes;
        }

        private sealed class DiffEntry
        {
            internal string Path, OldMode, Mode, OldOid, Oid, Status;
        }

        // One guard run: the repository, the git seam and the output.
        private sealed class Session
        {
            private readonly string repository;
            private readonly EvidenceGit git;
            private readonly TextWriter output;

            internal Session(string repository, EvidenceGit git, TextWriter output)
            {
                this.repository = repository;
                this.git = git;
                this.output = output;
            }

            internal int DeletionViolation(string reason)
            {
                output.WriteLine("EVIDENCE deletion violation reason=" + reason);
                return 1;
            }

            internal void WriteViolation(string commit, string path, string reason, long bytes) =>
                output.WriteLine("EVIDENCE violation commit=" + commit + " path=" + JsonString(path) + " bytes=" + bytes + " reason=" + reason);

            internal byte[] Read(params string[] arguments)
            {
                var result = git(repository, arguments);
                if (result == null || result.ExitCode != 0 || result.Bytes == null) throw new EvidenceRefusal("git_result_" + arguments[0]);
                return result.Bytes;
            }

            private string ReadLine(params string[] arguments) => Decode(Read(arguments)).TrimEnd('\n', '\r');

            internal string ResolveCommit(string reference)
            {
                if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('-') || reference.Contains('\0')) throw new EvidenceRefusal("ref_input");
                var value = ReadLine("rev-parse", "--verify", "--end-of-options", reference + "^{commit}");
                if (!IsOid(value)) throw new EvidenceRefusal("ref_result");
                return value;
            }

            internal void AssertHistory()
            {
                if (ReadLine("rev-parse", "--is-shallow-repository") != "false") throw new EvidenceRefusal("shallow_or_unknown_history");
            }

            internal void AssertAncestor(string ancestor, string descendant)
            {
                var result = git(repository, new[] { "merge-base", "--is-ancestor", ancestor, descendant });
                if (result == null || result.Bytes == null) throw new EvidenceRefusal("git_result_merge-base");
                if (result.ExitCode == 0) return;
                if (result.ExitCode == 1) throw new EvidenceRefusal("base_not_ancestor ancestor=" + ancestor + " descendant=" + descendant);
                throw new EvidenceRefusal("git_result_merge-base");
            }

            internal List<string> Commits(string @base, string head)
            {
                var text = Decode(Read("rev-list", "--reverse", "--topo-order", @base + ".." + head));
                var commits = new List<string>();
                if (text.Length == 0) return commits;
                if (!text.EndsWith('\n')) throw new EvidenceRefusal("commit_terminator");
                foreach (var line in text.TrimEnd('\n').Split('\n'))
                {
                    if (!IsOid(line)) throw new EvidenceRefusal("commit_id");
                    commits.Add(line);
                }
                return commits;
            }

            internal List<string> Parents(string commit)
            {
                var fields = ReadLine("rev-list", "--parents", "-n", "1", commit).Split(' ');
                if (fields[0] != commit) throw new EvidenceRefusal("parent_identity");
                if (!fields.All(IsOid)) throw new EvidenceRefusal("parent_id");
                return fields.Skip(1).ToList();
            }

            // NUL-terminated records; the output must end with a NUL.
            private static string[] Records(byte[] bytes)
            {
                var text = Decode(bytes);
                if (text.Length == 0) return Array.Empty<string>();
                if (!text.EndsWith('\0')) throw new EvidenceRefusal("nul_terminator");
                var records = text.Split('\0');
                return records.Take(records.Length - 1).ToArray();
            }

            internal List<DiffEntry> Diff(string commit, string parent)
            {
                var arguments = new List<string> { "diff-tree", "--no-commit-id", "--raw", "-r", "-z", "--no-renames", "--abbrev=64" };
                if (parent.Length > 0) arguments.AddRange(new[] { parent, commit });
                else arguments.AddRange(new[] { "--root", commit });
                var records = Records(Read(arguments.ToArray()));
                if (records.Length % 2 != 0) throw new EvidenceRefusal("raw_cardinality");
                var entries = new List<DiffEntry>();
                for (var i = 0; i < records.Length; i += 2)
                {
                    var fields = records[i].Split(' ');
                    if (fields.Length != 5 || !OldModeField.IsMatch(fields[0]) || !ModeField.IsMatch(fields[1]) || !IsOid(fields[2]) || !IsOid(fields[3]) || !StatusField.IsMatch(fields[4]))
                        throw new EvidenceRefusal("raw_header");
                    var path = records[i + 1];
                    if (!IsSafePath(path)) throw new EvidenceRefusal("raw_path");
                    entries.Add(new DiffEntry { Path = path, OldMode = fields[0].Substring(1), Mode = fields[1], OldOid = fields[2], Oid = fields[3], Status = fields[4] });
                }
                return entries;
            }

            internal long BlobSize(string oid)
            {
                var text = ReadLine("cat-file", "-s", oid);
                if (!Count.IsMatch(text) || !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) throw new EvidenceRefusal("blob_size");
                return size;
            }

            internal List<TreeEntry> Tree(string commit)
            {
                var entries = new List<TreeEntry>();
                foreach (var record in Records(Read("ls-tree", "-r", "-z", "-l", "--full-tree", commit)))
                {
                    var tab = record.IndexOf('\t');
                    if (tab < 0) throw new EvidenceRefusal("tree_cardinality");
                    var fields = record.Substring(0, tab).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length != 4 || !ModeField.IsMatch(fields[0]) || (fields[1] != "blob" && fields[1] != "commit") || !IsOid(fields[2]))
                        throw new EvidenceRefusal("tree_header");
                    long size = 0;
                    if (fields[1] == "commit")
                    {
                        if (fields[0] != "160000" || fields[3] != "-") throw new EvidenceRefusal("tree_gitlink");
                    }
                    else if (!Count.IsMatch(fields[3]) || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out size)) throw new EvidenceRefusal("tree_size");
                    var path = record.Substring(tab + 1);
                    if (!IsSafePath(path)) throw new EvidenceRefusal("tree_path");
                    entries.Add(new TreeEntry { Path = path, Mode = fields[0], Oid = fields[2], Bytes = size, Type = fields[1] });
                }
                return entries;
            }

            // The range a push or workflow_dispatch event selects: {base, head}, or null for a deleted branch.
            internal string[] EventRange(string eventPath, string eventName, string manualBase, string manualHead)
            {
                if (eventName != "push" && eventName != "workflow_dispatch") throw new EvidenceRefusal("unknown_event");
                var evt = ReadEvent(eventPath);
                if (eventName == "workflow_dispatch")
                {
                    if (string.IsNullOrWhiteSpace(manualBase)) throw new EvidenceRefusal("manual_base_required");
                    if (string.IsNullOrWhiteSpace(manualHead)) throw new EvidenceRefusal("manual_head_required");
                    return new[] { manualBase, manualHead };
                }
                foreach (var key in new[] { "ref", "before", "after" })
                    if (!evt.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new EvidenceRefusal("event_shape");
                if (!evt.TryGetValue("deleted", out var deleted) || (deleted.ValueKind != JsonValueKind.True && deleted.ValueKind != JsonValueKind.False)) throw new EvidenceRefusal("deleted_type");
                string defaultBranch = null;
                if (evt.TryGetValue("repository", out var repositoryValue) && repositoryValue.ValueKind == JsonValueKind.Object
                    && Properties(repositoryValue).TryGetValue("default_branch", out var branch) && branch.ValueKind == JsonValueKind.String)
                    defaultBranch = branch.GetString();
                if (string.IsNullOrWhiteSpace(defaultBranch)) throw new EvidenceRefusal("event_repository");
                var reference = evt["ref"].GetString();
                var before = evt["before"].GetString();
                var after = evt["after"].GetString();
                if (!BranchRef.IsMatch(reference)) throw new EvidenceRefusal("event_ref");
                if (!IsOid(before) || !IsOid(after)) throw new EvidenceRefusal("event_oid");
                if (deleted.ValueKind == JsonValueKind.True) return null;
                var head = ResolveCommit(after);
                if (reference == "refs/heads/" + defaultBranch)
                {
                    if (Zeros.IsMatch(before)) throw new EvidenceRefusal("default_zero_before");
                    return new[] { before, head };
                }
                var remote = ResolveCommit("refs/remotes/origin/" + defaultBranch);
                var @base = ReadLine("merge-base", remote, head);
                if (!IsOid(@base)) throw new EvidenceRefusal("merge_base_result");
                return new[] { @base, head };
            }

            // The event payload must be a JSON object; a repeated key keeps its last value.
            private static Dictionary<string, JsonElement> ReadEvent(string eventPath)
            {
                string text;
                try { text = File.ReadAllText(eventPath); }
                catch (Exception) { throw new EvidenceRefusal("event_read"); }
                JsonElement root;
                try
                {
                    using var document = JsonDocument.Parse(text);
                    root = document.RootElement.Clone();
                }
                catch (JsonException) { throw new EvidenceRefusal("event_json"); }
                if (root.ValueKind != JsonValueKind.Object) throw new EvidenceRefusal("event_shape");
                return Properties(root);
            }

            private static Dictionary<string, JsonElement> Properties(JsonElement element)
            {
                var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject()) properties[property.Name] = property.Value;
                return properties;
            }
        }
    }
}
