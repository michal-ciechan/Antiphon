// CARD-1836 S3b (moved from rc-chunk-digest.ps1): reproducible chunk input identities, never execution or carry
// authority. A chunk's digest binds its suite's input records and edges, its case membership, the actual input
// receipts and the boundary membership digest. Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A chunk-digest refusal (invalid-input-snapshot, chunk-membership-mismatch, invalid-digest-..., ...).</summary>
    public sealed class RcChunkDigestRefusal : Exception
    {
        public RcChunkDigestRefusal(string message) : base(message) { }
    }

    /// <summary>One discovered case of the chunk: its suite, class, method, case key and whether its key is stable.</summary>
    public sealed class RcChunkCase
    {
        /// <summary>The case's canonical key: the JSON array [suite, className, method, caseKey].</summary>
        public string Key { get; set; }
        public string Suite { get; set; }
        public string ClassName { get; set; }
        public string Method { get; set; }
        public string CaseKey { get; set; }
        public bool Stable { get; set; }
    }

    public sealed class RcChunkMethod
    {
        public string IdentityKey { get; set; }
        public RcChunkCase[] Cases { get; set; }
        public bool Ambiguous { get; set; }
    }

    /// <summary>A receipt for one kind of actual input (SDK and runtime, packages, images, ...).</summary>
    public sealed class RcActualReceipt
    {
        public string Kind { get; set; }
        public string Digest { get; set; }
        public string ProvenanceDigest { get; set; }
        public string SourceSha { get; set; }
    }

    /// <summary>antiphon-rc-chunk-input-v1: a chunk's input identity at one commit. Shadow only.</summary>
    public sealed class RcChunkInputManifest
    {
        public string Format { get; set; } = "antiphon-rc-chunk-input-v1";
        public string SourceSha { get; set; }
        public string TreeId { get; set; }
        public string InputSuite { get; set; }
        public string Suite { get; set; }
        public string Boundary { get; set; }
        public string ChunkId { get; set; }
        public string MembershipDigest { get; set; }
        public string Digest { get; set; }
        public string InputDigest { get; set; }
        public string CaseDigest { get; set; }
        public string ReceiptDigest { get; set; }
        public RcInputRecord[] Inputs { get; set; }
        public RcInputEdge[] Edges { get; set; }
        public RcChunkCase[] Cases { get; set; }
        public RcChunkMethod[] Methods { get; set; }
        public RcActualReceipt[] ActualReceipts { get; set; }
        public string[] Unknown { get; set; }
        public bool ExecutionEvidence { get; set; }
        public bool GateCredit { get; set; }
        public string Activation { get; set; } = "shadow-only";
    }

    public sealed class RcChunkInputComparison
    {
        public bool Dirty { get; set; }
        public string[] Reasons { get; set; }
        public RcChunkCase[] Retired { get; set; }
        public bool ExecutionEvidence { get; set; }
        public bool GateCredit { get; set; }
    }

    public static class RcChunkDigest
    {
        public static readonly string[] RequiredReceipts =
        {
            "sdk-runtime-os-architecture", "powershell-node-browser-native", "configuration-environment",
            "resolved-package-graph-and-content", "generated-fixture-recipes-and-outputs", "immutable-images",
        };

        private static Exception Refuse(string what) => new RcChunkDigestRefusal(what);

        /// <summary>
        /// SHA-256 (lower hex) of rows in S2's one length-prefix and strict UTF-8 rule: u32 row count, then per row its u32
        /// field count and each field length-prefixed.
        /// </summary>
        public static string ManifestDigest(IReadOnlyList<string[]> rows)
        {
            var encoder = new UTF8Encoding(false, true);
            using var buffer = new MemoryStream();
            RcBoundary.AddU32(buffer, (uint)rows.Count);
            foreach (var row in rows)
            {
                RcBoundary.AddU32(buffer, (uint)row.Length);
                foreach (var field in row)
                {
                    if (field == null) throw Refuse("digest-field-must-be-string");
                    RcBoundary.AddString(buffer, field, encoder);
                }
            }
            return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        }

        private static readonly Regex Control = new Regex(@"[\x00-\x1f]", RegexOptions.CultureInvariant);

        /// <summary>Text that can enter a digest: not blank, at most 1 MiB, no control character.</summary>
        public static void AssertDigestText(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1048576 || Control.IsMatch(value)) throw Refuse("invalid-digest-" + label);
        }

        private static readonly Regex FullSha = new Regex(@"\A[a-f0-9]{40}\z", RegexOptions.CultureInvariant);
        private static readonly Regex Digest64 = new Regex(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
        private static readonly Regex Mode = new Regex(@"\A[0-7]{6}\z", RegexOptions.CultureInvariant);
        private static readonly Regex UnsafeInputPath = new Regex(@"(^/|\\|(^|/)\.\.?(/|$)|:)", RegexOptions.CultureInvariant);

        // The JSON array text of the fields (only quote and backslash need escaping: control characters are refused).
        private static string JsonArray(params string[] fields) =>
            "[" + string.Join(",", fields.Select(f => "\"" + (f ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]";

        private static string[] SortedReasons(string reason) => RcInputInventory.Sorted(reason.Split(';'));

        /// <summary>
        /// The chunk's input manifest from the snapshot's input suite. The suite's records and edges, the chunk's cases
        /// (each method of the membership must have one), and the six actual-input receipt kinds are each digested;
        /// the digest binds them with the membership digest. Unresolved snapshot reasons, unowned paths, case
        /// collisions, links, ambiguous cases and missing receipts become unknown reasons.
        /// </summary>
        public static RcChunkInputManifest NewManifest(RcInputSnapshot snapshot, string inputSuite, RcBoundaryChunk chunk, IReadOnlyList<RcChunkCase> cases,
            IReadOnlyList<RcActualReceipt> actualReceipts)
        {
            if (snapshot?.Format != "antiphon-rc-input-snapshot-v1" || snapshot.Sha == null || !FullSha.IsMatch(snapshot.Sha) || snapshot.TreeId == null || !FullSha.IsMatch(snapshot.TreeId))
                throw Refuse("invalid-input-snapshot");
            var owners = snapshot.Suites.Where(s => s.Id == inputSuite).ToArray();
            if (owners.Length != 1) throw Refuse("missing-or-duplicate-input-suite");
            var owner = owners[0];
            // S2 owns parsing, validation, ordinal sorting and the full membership digest.
            var membership = RcBoundary.ChunkData(chunk.Suite, chunk.Boundary, chunk.IdentityKeys, chunk.CaseKeys);
            if (membership.ChunkId != chunk.ChunkId || membership.FullDigest != chunk.FullDigest) throw Refuse("chunk-membership-mismatch");
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var reason in snapshot.Unresolved ?? Array.Empty<string>()) { AssertDigestText(reason, "unresolved"); unknown.Add(reason); }
            // Unowned paths stay out of suite inputs; they are unknown, so a cut with an unclassified path cannot compare
            // clean. A snapshot rebuilt from a stored manifest (admission rehash) has none to contribute.
            foreach (var row in snapshot.Unowned ?? Array.Empty<RcUnownedPath>())
            {
                if (row == null) continue;
                var token = !string.IsNullOrEmpty(row.Path) ? "unowned-path:" + row.Path : row.Reason;
                if (string.IsNullOrEmpty(token)) continue;
                AssertDigestText(token, "unowned");
                unknown.Add(token);
            }
            if (chunk.Boundary == "unassigned") unknown.Add("unassigned-boundary");
            if (owner.Inputs.Length > 1000000 || cases.Count > 1000000) throw Refuse("manifest-entry-limit");
            AssertDigestText(owner.Project, "project");
            var inputs = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in owner.Inputs)
            {
                AssertDigestText(record.Path, "path");
                AssertDigestText(record.Reason, "reason");
                if (UnsafeInputPath.IsMatch(record.Path) || record.Version != 1 || record.Suite != inputSuite || record.Project != owner.Project) throw Refuse("invalid-input-record");
                if (inputs.ContainsKey(record.Path)) throw Refuse("duplicate-input-path");
                if (!folded.Add(record.Path)) unknown.Add("case-collision:" + record.Path);
                if (record.Present)
                {
                    if (record.ObjectId == null || !FullSha.IsMatch(record.ObjectId) || record.Mode == null || !Mode.IsMatch(record.Mode) || (record.Type != "blob" && record.Type != "commit"))
                        throw Refuse("invalid-input-object");
                    if ((record.Mode != "100644" && record.Mode != "100755") || record.Type != "blob") unknown.Add("unresolved-link-or-submodule:" + record.Path);
                }
                else if (record.ObjectId != null || record.Mode != null || record.Type != null) throw Refuse("absent-input-has-object");
                inputs.Add(record.Path, new[]
                {
                    "input", "1", inputSuite, owner.Project, record.Path, record.Present ? "present" : "absent", record.Mode ?? "", record.Type ?? "", record.ObjectId ?? "",
                    string.Join(";", SortedReasons(record.Reason)),
                });
            }
            if (inputs.Count == 0) throw Refuse("empty-input-manifest");
            var inputPaths = RcInputInventory.Sorted(inputs.Keys);
            var inputRows = new List<string[]> { new[] { "antiphon-rc-inputs", "1", inputSuite, owner.Project } };
            inputRows.AddRange(inputPaths.Select(p => inputs[p]));
            var edges = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var edge in owner.Edges)
            {
                foreach (var (name, value) in new[] { ("from", edge.From), ("to", edge.To), ("project", edge.Project), ("reason", edge.Reason) }) AssertDigestText(value, "edge-" + name);
                var row = new[] { "edge", edge.From, edge.To, edge.Project, edge.Reason };
                edges[JsonArray(row)] = row;
            }
            var edgeKeys = RcInputInventory.Sorted(edges.Keys);
            inputRows.AddRange(edgeKeys.Select(k => edges[k]));
            var inputDigest = ManifestDigest(inputRows);

            // A suite shard aggregates methods only after every case has been accounted for.
            var methods = new Dictionary<string, List<RcChunkCase>>(StringComparer.Ordinal);
            foreach (var key in chunk.IdentityKeys) if (!methods.ContainsKey(key)) methods.Add(key, new List<RcChunkCase>());
            var caseKeys = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new List<RcChunkCase>();
            foreach (var c in cases)
            {
                foreach (var (name, value) in new[] { ("suite", c.Suite), ("className", c.ClassName), ("method", c.Method), ("caseKey", c.CaseKey) }) AssertDigestText(value, "case-" + name);
                if (c.Suite != chunk.Suite) throw Refuse("case-suite-mismatch");
                if (c.ClassName.Contains('/')) throw Refuse("invalid-case-identity");
                var methodKey = c.ClassName + "/" + c.Method;
                if (!methods.ContainsKey(methodKey)) throw Refuse("extra-method-membership");
                var key = JsonArray(c.Suite, c.ClassName, c.Method, c.CaseKey);
                if (!caseKeys.Add(key)) throw Refuse("duplicate-case");
                normalized.Add(new RcChunkCase { Key = key, Suite = c.Suite, ClassName = c.ClassName, Method = c.Method, CaseKey = c.CaseKey, Stable = c.Stable });
                if (!c.Stable) unknown.Add("ambiguous-case:" + methodKey);
            }
            var order = RcBoundary.SortOrder(normalized.Select(c => c.ClassName).ToArray(), normalized.Select(c => c.Method).ToArray(), normalized.Select(c => c.CaseKey).ToArray());
            var sortedCases = order.Select(i => normalized[i]).ToArray();
            if (chunk.CaseKeys.Any(k => k != ""))
            {
                if (membership.IdentityKeys.Length != sortedCases.Length) throw Refuse("case-membership-mismatch");
                for (var i = 0; i < sortedCases.Length; i++)
                    if (membership.IdentityKeys[i] != sortedCases[i].ClassName + "/" + sortedCases[i].Method || membership.CaseKeys[i] != sortedCases[i].CaseKey)
                        throw Refuse("case-membership-mismatch");
            }
            var caseRows = new List<string[]> { new[] { "antiphon-rc-cases", "1", chunk.Suite, chunk.Boundary } };
            foreach (var c in sortedCases)
            {
                methods[c.ClassName + "/" + c.Method].Add(c);
                caseRows.Add(new[] { "case", c.Suite, c.ClassName, c.Method, c.CaseKey, c.Stable ? "stable" : "ambiguous" });
            }
            if (methods.Values.Any(m => m.Count == 0)) throw Refuse("incomplete-method-membership");
            var methodRows = new List<RcChunkMethod>();
            var seenMethods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in sortedCases)
            {
                var method = c.ClassName + "/" + c.Method;
                if (seenMethods.Add(method))
                    methodRows.Add(new RcChunkMethod { IdentityKey = method, Cases = methods[method].ToArray(), Ambiguous = unknown.Contains("ambiguous-case:" + method) });
            }
            var caseDigest = ManifestDigest(caseRows);

            var receipts = new Dictionary<string, RcActualReceipt>(StringComparer.Ordinal);
            foreach (var receipt in actualReceipts ?? Array.Empty<RcActualReceipt>())
            {
                if (receipt == null || Array.IndexOf(RequiredReceipts, receipt.Kind) < 0 || receipt.Digest == null || !Digest64.IsMatch(receipt.Digest)
                    || receipt.ProvenanceDigest == null || !Digest64.IsMatch(receipt.ProvenanceDigest) || receipt.SourceSha == null || !FullSha.IsMatch(receipt.SourceSha))
                    throw Refuse("invalid-actual-receipt");
                if (receipts.ContainsKey(receipt.Kind)) throw Refuse("duplicate-actual-receipt");
                // S5b independently rehashes retained bytes and verifies these source/build joins.
                receipts.Add(receipt.Kind, new RcActualReceipt { Kind = receipt.Kind, Digest = receipt.Digest, ProvenanceDigest = receipt.ProvenanceDigest, SourceSha = receipt.SourceSha });
            }
            var receiptRows = new List<string[]> { new[] { "antiphon-rc-actual-inputs", "1" } };
            foreach (var kind in RcInputInventory.Sorted(RequiredReceipts))
            {
                if (receipts.TryGetValue(kind, out var receipt)) receiptRows.Add(new[] { kind, receipt.Digest });
                else { unknown.Add("missing-actual-receipt:" + kind); receiptRows.Add(new[] { kind, "missing" }); }
            }
            var receiptDigest = ManifestDigest(receiptRows);
            var digest = ManifestDigest(new[] { new[] { "antiphon-rc-chunk-input", "1", inputSuite, chunk.Suite, chunk.Boundary, inputDigest, caseDigest, receiptDigest, membership.FullDigest } });
            return new RcChunkInputManifest
            {
                SourceSha = snapshot.Sha, TreeId = snapshot.TreeId, InputSuite = inputSuite, Suite = chunk.Suite, Boundary = chunk.Boundary, ChunkId = membership.ChunkId,
                MembershipDigest = membership.FullDigest, Digest = digest, InputDigest = inputDigest, CaseDigest = caseDigest, ReceiptDigest = receiptDigest,
                Inputs = inputPaths.Select(p =>
                {
                    var row = inputs[p];
                    return new RcInputRecord
                    {
                        Suite = row[2], Project = row[3], Path = row[4], Present = row[5] == "present", Mode = row[6] == "" ? null : row[6], Type = row[7] == "" ? null : row[7],
                        ObjectId = row[8] == "" ? null : row[8], Reason = row[9],
                    };
                }).ToArray(),
                Edges = edgeKeys.Select(k => edges[k]).Select(row => new RcInputEdge { From = row[1], To = row[2], Project = row[3], Reason = row[4] }).ToArray(),
                Cases = sortedCases, Methods = methodRows.ToArray(),
                ActualReceipts = RcInputInventory.Sorted(receipts.Keys).Select(k => receipts[k]).ToArray(), Unknown = unknown.ToArray(),
            };
        }

        /// <summary>
        /// Dirty when either manifest has unknown reasons, the owner or boundary changed, or any of the input, case,
        /// receipt, membership or combined digests differs; retired are the earlier cases the later one no longer has.
        /// </summary>
        public static RcChunkInputComparison Compare(RcChunkInputManifest before, RcChunkInputManifest after)
        {
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var manifest in new[] { before, after })
            {
                if (manifest?.Format != "antiphon-rc-chunk-input-v1" || manifest.Digest == null || !Digest64.IsMatch(manifest.Digest)) throw Refuse("invalid-chunk-input-manifest");
                foreach (var reason in manifest.Unknown ?? Array.Empty<string>()) reasons.Add(reason);
            }
            if (before.InputSuite != after.InputSuite || before.Suite != after.Suite || before.Boundary != after.Boundary) reasons.Add("owner-or-boundary-changed");
            if (before.InputDigest != after.InputDigest) reasons.Add("inputs-changed");
            if (before.CaseDigest != after.CaseDigest) reasons.Add("cases-changed");
            if (before.ReceiptDigest != after.ReceiptDigest) reasons.Add("actual-inputs-changed");
            if (before.MembershipDigest != after.MembershipDigest) reasons.Add("membership-changed");
            if (before.Digest != after.Digest) reasons.Add("chunk-inputs-changed");
            var current = new HashSet<string>((after.Cases ?? Array.Empty<RcChunkCase>()).Select(c => c.Key), StringComparer.Ordinal);
            return new RcChunkInputComparison
            {
                Dirty = reasons.Count > 0, Reasons = reasons.ToArray(), Retired = (before.Cases ?? Array.Empty<RcChunkCase>()).Where(c => !current.Contains(c.Key)).ToArray(),
            };
        }
    }
}
