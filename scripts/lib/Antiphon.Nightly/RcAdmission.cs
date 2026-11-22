// CARD-1836 S5b (moved from rc-admission.ps1): read-only historical admission. Inert: no launch caller. Full remains
// required until the release graph and its COMPLETE executed algorithm closure have separately been admitted. Every
// evidence document is read from sealed bytes (its SHA-256 given by the caller or the verified bundle inventory); the
// current inventory is rebuilt from git objects, never disk or stored hashes. Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Antiphon.Rc;

namespace Antiphon.Nightly
{
    /// <summary>An admission refusal; its message is the admission reason.</summary>
    public sealed class RcAdmissionRefusal : Exception
    {
        public RcAdmissionRefusal(string message) : base(message) { }
    }

    public sealed class RcAlgorithmCoverage
    {
        public bool Complete { get; init; }
        public string[] RequiredPaths { get; init; }
        public string[] MissingPaths { get; init; }
        public string[] Reasons { get; init; }
    }

    /// <summary>Facts the admission predicate weighs (null where unproven).</summary>
    public sealed class RcAdmissionFacts
    {
        public bool? InventoryValid { get; set; }
        public bool? ProvenanceValid { get; set; }
        public bool? Complete { get; set; }
        public bool? HistoryValid { get; set; }
        public bool? OriginValid { get; set; }
        public bool? ContractValid { get; set; }
        public string Outcome { get; set; }
        public string[] CaseOutcomes { get; set; }
        public string AlgorithmDigest { get; set; }
        public string CurrentAlgorithmDigest { get; set; }
        public string ReleaseStatus { get; set; }
        public string AnchorAtUtc { get; set; }
        public bool? AnchorVerified { get; set; }
    }

    public sealed class RcCarryAdmission
    {
        public bool Eligible { get; init; }
        public string[] Reasons { get; init; }
        public bool GateCredit { get; init; }
    }

    /// <summary>The current run's context: repository, platform, rc line and input contract.</summary>
    public sealed class RcAdmissionContext
    {
        public string Repository { get; set; }
        public string Platform { get; set; }
        public string RcLine { get; set; }
        public JsonElement Contract { get; set; }
    }

    public sealed class RcAdmissionResult
    {
        public string Format { get; init; } = "antiphon-rc-admission-result-v1";
        public bool Eligible { get; init; }
        public bool Admitted { get; init; }
        public bool Dirty { get; init; }
        public bool ForceFull { get; init; }
        public string[] Reasons { get; init; }
        public RcAlgorithmCoverage Coverage { get; init; }
        public bool ExecutionEvidence { get; init; }
        public bool GateCredit { get; init; }
    }

    public static class RcAdmission
    {
        private static Exception Refuse(string what) => new RcAdmissionRefusal(what);

        /// <summary>Records deserialised from stored evidence use their persisted camelCase names.</summary>
        public static readonly JsonSerializerOptions RecordJson = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Explicit prerequisite inventory, including relation construction, evaluation, canonicalization and the compiled
        // driver. Not a claim of transitive closure.
        private static readonly string[] RequiredAlgorithmPaths =
        {
            "scripts/impacted-classes.ps1", "scripts/lib/release-impact.ps1",
            // The selection's canonical JSON, digest and git reads (scripts/lib/test-selection.ps1 until 2026-11-22).
            "scripts/lib/Antiphon.Nightly/TestSelection.cs", "scripts/lib/Antiphon.Nightly/TestSelectionCatalog.cs",
            "scripts/lib/Antiphon.Nightly/TestSelectionGit.cs", "scripts/lib/Antiphon.Nightly/TestSelectionJson.cs",
            "scripts/lib/Antiphon.Nightly/RcAdmission.cs", "scripts/lib/Antiphon.Nightly/RcInputInventory.cs", "scripts/lib/Antiphon.Nightly/RcChunkDigest.cs",
            "scripts/lib/Antiphon.Nightly/RcBoundary.cs", "scripts/lib/Antiphon.Nightly/RcHistoricalEvidence.cs",
            "tests/Antiphon.Tests/Scripts/ReleaseImpactProjectGraph.cs", "tests/Antiphon.Tests/Scripts/ReleaseImpactGraphBuilder.cs",
            "tests/Antiphon.Tests/Scripts/DerivedImpactIndexBuilder.cs", "tools/Antiphon.LandGraphAudit/ReleaseInputEvaluation.cs",
            "tools/Antiphon.LandGraphAudit/ReleaseInputCommand.cs", "tools/Antiphon.LandGraphAudit/EvaluationExpressionPolicy.cs",
            "tools/Antiphon.LandGraphAudit/BuildInputPolicy.cs", "tools/Antiphon.LandGraphAudit/Program.cs",
            "tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj", "scripts/regenerate-derived-impact-index/Program.cs",
            "scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj",
        };

        /// <summary>
        /// The algorithm paths the release evidence covers against the required inventory. Never complete: the release graph
        /// still has unresolved evaluator, canonicalizer and runtime edges, and covering this minimum list cannot certify
        /// them. No caller override.
        /// </summary>
        public static RcAlgorithmCoverage AlgorithmCoverage(IEnumerable<string> coveredPaths = null)
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in coveredPaths ?? new[] { "scripts/impacted-classes.ps1", "scripts/lib/release-impact.ps1" })
                if (!covered.Add(path)) throw Refuse("duplicate-algorithm-path");
            var missing = RcInputInventory.Sorted(RequiredAlgorithmPaths.Where(p => !covered.Contains(p)));
            return new RcAlgorithmCoverage
            {
                Complete = false, RequiredPaths = RcInputInventory.Sorted(RequiredAlgorithmPaths), MissingPaths = missing,
                Reasons = new[] { "release-graph-incomplete" }.Concat(missing.Select(p => "algorithm-uncovered:" + p)).ToArray(),
            };
        }

        private static readonly Regex TreeLine = new Regex(@"^([0-7]{6}) (blob|commit) ([0-9a-f]{40})\t([^\x00-\x1f]+)$", RegexOptions.CultureInvariant);
        private static readonly Regex UnsafePath = new Regex(@"(^/|\\|(^|/)\.\.?(/|$)|:)", RegexOptions.CultureInvariant);

        /// <summary>The digest of the covered algorithm paths' regular blobs (mode and object id) at a commit.</summary>
        public static string AlgorithmDigest(RcInputInventory.GitReader git, string sha, IReadOnlyCollection<string> coveredPaths)
        {
            if (coveredPaths == null || coveredPaths.Count == 0) throw Refuse("algorithm-coverage-empty");
            var objects = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var entry in git.Run("ls-tree", "-r", "-z", "--full-tree", sha).Split('\0'))
            {
                if (entry.Length == 0) continue;
                var m = TreeLine.Match(entry);
                if (!m.Success) throw Refuse("algorithm-tree-invalid");
                objects.Add(m.Groups[4].Value, new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value });
            }
            var rows = new List<string[]> { new[] { "antiphon-rc-algorithm", "1" } };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in RcInputInventory.Sorted(coveredPaths))
            {
                RcChunkDigest.AssertDigestText(path, "algorithm-path");
                if (!seen.Add(path) || UnsafePath.IsMatch(path)) throw Refuse("algorithm-path-invalid");
                if (!objects.TryGetValue(path, out var o) || (o[0] != "100644" && o[0] != "100755") || o[1] != "blob") throw Refuse("algorithm-object-unproven");
                rows.Add(new[] { path, o[0], o[2] });
            }
            return RcChunkDigest.ManifestDigest(rows);
        }

        /// <summary>
        /// Rehashes a stored chunk input manifest: from its own records (no snapshot), or from the snapshot after first
        /// rehashing its own records. Every stored identity and digest must equal the rebuilt one.
        /// </summary>
        public static RcChunkInputManifest RestoreManifest(RcChunkInputManifest manifest, RcBoundaryChunk chunk, RcInputSnapshot snapshot = null)
        {
            if (manifest?.Format != "antiphon-rc-chunk-input-v1") throw Refuse("manifest-invalid");
            if (snapshot != null) RestoreManifest(manifest, chunk);
            else
            {
                // Rehash stored records independently; Read additionally reconstructs the inventory from git objects.
                var projects = (manifest.Inputs ?? Array.Empty<RcInputRecord>()).Select(r => r.Project).Distinct().ToArray();
                if (projects.Length != 1) throw Refuse("manifest-invalid");
                snapshot = new RcInputSnapshot
                {
                    Sha = manifest.SourceSha, TreeId = manifest.TreeId, Unresolved = manifest.Unknown ?? Array.Empty<string>(),
                    Suites = new[] { new RcInputSuite { Id = manifest.InputSuite, Project = projects[0], Inputs = manifest.Inputs, Edges = manifest.Edges ?? Array.Empty<RcInputEdge>() } },
                };
            }
            var cases = (manifest.Cases ?? Array.Empty<RcChunkCase>()).Select(c => new RcChunkCase { Suite = c.Suite, ClassName = c.ClassName, Method = c.Method, CaseKey = c.CaseKey, Stable = c.Stable }).ToArray();
            var rebuilt = RcChunkDigest.NewManifest(snapshot, manifest.InputSuite, chunk, cases, manifest.ActualReceipts);
            foreach (var (stored, fresh) in new[]
            {
                (manifest.SourceSha, rebuilt.SourceSha), (manifest.TreeId, rebuilt.TreeId), (manifest.InputSuite, rebuilt.InputSuite), (manifest.Suite, rebuilt.Suite),
                (manifest.Boundary, rebuilt.Boundary), (manifest.ChunkId, rebuilt.ChunkId), (manifest.MembershipDigest, rebuilt.MembershipDigest), (manifest.Digest, rebuilt.Digest),
                (manifest.InputDigest, rebuilt.InputDigest), (manifest.CaseDigest, rebuilt.CaseDigest), (manifest.ReceiptDigest, rebuilt.ReceiptDigest),
            })
                if (stored == null || stored != fresh) throw Refuse("manifest-rehash-mismatch");
            return rebuilt;
        }

        /// <summary>
        /// Diagnostic eligibility only (Read owns byte provenance and the non-overridable release-graph gate; this grants
        /// no gate credit): both manifests rehash with no unknown input, they compare clean, the chunk is neither
        /// unassigned nor a collision, every fact holds, every case passed, the algorithm is unchanged, the release
        /// evidence is complete and the verified Full anchor is younger than seven days.
        /// </summary>
        public static RcCarryAdmission CarryAdmissionDecision(RcChunkInputManifest before, RcChunkInputManifest after, RcAdmissionFacts facts, RcBoundaryChunk chunk,
            DateTimeOffset utcNow)
        {
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            if (chunk == null)
            {
                try
                {
                    chunk = RcBoundary.NewChunk((after?.Cases ?? Array.Empty<RcChunkCase>()).Select(c => new RcBoundaryRow
                    {
                        Suite = c.Suite, Boundary = after.Boundary, IdentityKey = c.ClassName + "/" + c.Method, CaseKey = c.CaseKey,
                    }).ToArray());
                }
                catch (Exception) { reasons.Add("manifest-invalid"); }
            }
            if (chunk != null && (chunk.Boundary == "unassigned" || chunk.Collision || chunk.AlwaysDirty)) reasons.Add("collision-or-unassigned");
            else
            {
                foreach (var manifest in new[] { before, after })
                {
                    try { foreach (var r in RestoreManifest(manifest, chunk).Unknown) reasons.Add("input-unproven:" + r); }
                    catch (Exception e) { reasons.Add(e.Message.Contains("manifest-rehash-mismatch") ? "manifest-rehash-mismatch" : "manifest-invalid"); }
                }
                try { foreach (var r in RcChunkDigest.Compare(before, after).Reasons) reasons.Add(r); }
                catch (Exception) { reasons.Add("manifest-invalid"); }
            }
            foreach (var (value, reason) in new[]
            {
                (facts?.InventoryValid, "inventory-unproven"), (facts?.ProvenanceValid, "source-build-launch-unproven"), (facts?.Complete, "chunk-incomplete"),
                (facts?.HistoryValid, "failure-history-unproven"), (facts?.OriginValid, "origin-unproven"), (facts?.ContractValid, "contract-unproven"),
            })
                if (value != true) reasons.Add(reason);
            if (facts?.Outcome != "Passed") reasons.Add("chunk-not-passed");
            var outcomes = facts?.CaseOutcomes ?? Array.Empty<string>();
            if (outcomes.Length == 0 || outcomes.Length != (after?.Cases?.Length ?? 0) || outcomes.Any(o => o != "Passed")) reasons.Add("cases-not-passed");
            if (!RcForcedFull.IsDigest(facts?.AlgorithmDigest) || facts.AlgorithmDigest != facts.CurrentAlgorithmDigest) reasons.Add("algorithm-binding-mismatch");
            if (facts?.ReleaseStatus != "complete") reasons.Add("release-evidence-incomplete");
            var anchor = RcForcedFull.UtcTime(facts?.AnchorAtUtc);
            if (facts?.AnchorVerified != true || anchor == null || anchor > utcNow || utcNow - anchor >= TimeSpan.FromDays(7)) reasons.Add("full-anchor-invalid-or-expired");
            return new RcCarryAdmission { Eligible = reasons.Count == 0, Reasons = reasons.ToArray(), GateCredit = false };
        }

        /// <summary>A file's SHA-256 (lower hex).</summary>
        public static string FileDigest(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// Sealed bounded metadata (never summary.json): a regular file of at most 16 MiB whose exact bytes hash to the
        /// expected digest, strict UTF-8 JSON with no repeated name (ignoring case). The bytes hashed are the bytes parsed.
        /// </summary>
        public static JsonElement ReadJson(string path, string expectedDigest)
        {
            if (!RcForcedFull.IsDigest(expectedDigest)) throw Refuse("admission-seal-required");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 16777216 || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw Refuse("admission-file-invalid");
            var bytes = File.ReadAllBytes(path);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedDigest) throw Refuse("admission-seal-mismatch");
            var text = new UTF8Encoding(false, true).GetString(bytes);
            using var json = JsonDocument.Parse(text);
            RcInputInventory.AssertUniqueKeys(json.RootElement);
            return json.RootElement.Clone();
        }

        /// <summary>The bundle path and digest of one inventory entry (exactly one may carry the relative path).</summary>
        public static (string Path, string Digest) EvidenceObject(HistoricalEvidence.Result inventory, string evidenceRoot, string relativePath)
        {
            var matches = inventory.files.Where(f => f.relativePath == relativePath).ToArray();
            if (matches.Length != 1) throw Refuse("admission-object-missing-or-duplicate");
            // S5a verified confinement, links and exact object bytes; the full bundle is reverified before a decision.
            return (Path.Combine(evidenceRoot, matches[0].bundlePath), matches[0].sha256);
        }

        // ---- JSON documents ----------------------------------------------------------------------------------------

        private static void AssertKeys(JsonElement o, params string[] allowed)
        {
            if (o.ValueKind != JsonValueKind.Object) throw Refuse("invalid-catalog-object");
            foreach (var p in o.EnumerateObject()) if (Array.IndexOf(allowed, p.Name) < 0) throw Refuse("invalid-catalog-field:" + p.Name);
        }

        private static JsonElement F(JsonElement o, string name) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) ? v : default;
        private static string S(JsonElement o, string name) { var v = F(o, name); return v.ValueKind == JsonValueKind.String ? v.GetString() : null; }
        private static bool True(JsonElement o, string name) => F(o, name).ValueKind == JsonValueKind.True;
        private static long? N(JsonElement o, string name) { var v = F(o, name); return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : (long?)null; }
        private static IEnumerable<JsonElement> Items(JsonElement o, string name) { var v = F(o, name); return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>(); }

        /// <summary>A stored record (manifest, chunk) from its persisted camelCase JSON; a malformed one is manifest-invalid.</summary>
        public static T ReadRecord<T>(JsonElement element)
        {
            try { return element.Deserialize<T>(RecordJson) ?? throw Refuse("manifest-invalid"); }
            catch (JsonException) { throw Refuse("manifest-invalid"); }
        }

        /// <summary>
        /// The executed proof's TRX outcomes: the case map must be exactly the manifest's cases, every definition, method
        /// and result present once, the run completed, and the counters all executed and passed.
        /// </summary>
        public static string[] ReadTrx(string path, IReadOnlyList<RcChunkCase> cases, JsonElement caseMap)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var row in caseMap.ValueKind == JsonValueKind.Array ? caseMap.EnumerateArray() : Enumerable.Empty<JsonElement>())
            {
                AssertKeys(row, "testId", "suite", "className", "method", "caseKey");
                var id = S(row, "testId");
                if (string.IsNullOrEmpty(id) || map.ContainsKey(id)) throw Refuse("admission-case-alias");
                map.Add(id, row);
            }
            string Key(string c, string m, string k, string s) => c + "\0" + m + "\0" + k + "\0" + s;
            var expectedRows = cases.Select(c => (c.ClassName ?? "", c.Method ?? "", c.CaseKey ?? "", c.Suite ?? "")).ToArray();
            var actualRows = map.Values.Select(r => (S(r, "className") ?? "", S(r, "method") ?? "", S(r, "caseKey") ?? "", S(r, "suite") ?? "")).ToArray();
            string[] Ordered((string, string, string, string)[] rows) =>
                RcBoundary.SortOrder(rows.Select(r => r.Item1).ToArray(), rows.Select(r => r.Item2).ToArray(), rows.Select(r => r.Item3).ToArray())
                    .Select(i => Key(rows[i].Item1, rows[i].Item2, rows[i].Item3, rows[i].Item4)).ToArray();
            var expected = Ordered(expectedRows);
            if (expected.Length == 0 || !expected.SequenceEqual(Ordered(actualRows), StringComparer.Ordinal)) throw Refuse("admission-case-roster");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 268435456 };
            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            var methods = new HashSet<string>(StringComparer.Ordinal);
            var unit = "";
            bool counters = false, terminal = false;
            var count = cases.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using (var reader = XmlReader.Create(path, settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    switch (reader.LocalName)
                    {
                        case "UnitTest":
                            unit = reader.GetAttribute("id") ?? "";
                            if (!map.ContainsKey(unit) || !definitions.Add(unit)) throw Refuse("admission-trx-definition");
                            break;
                        case "TestMethod":
                            if (!map.ContainsKey(unit) || !methods.Add(unit) || reader.GetAttribute("className") != S(map[unit], "className") || reader.GetAttribute("name") != S(map[unit], "method"))
                                throw Refuse("admission-trx-method");
                            break;
                        case "UnitTestResult":
                            var id = reader.GetAttribute("testId") ?? "";
                            if (!map.ContainsKey(id) || results.ContainsKey(id)) throw Refuse("admission-trx-result");
                            results.Add(id, reader.GetAttribute("outcome"));
                            break;
                        case "ResultSummary":
                            if (terminal) throw Refuse("admission-trx-summary");
                            terminal = reader.GetAttribute("outcome") == "Completed";
                            break;
                        case "Counters":
                            if (counters) throw Refuse("admission-trx-counters");
                            counters = true;
                            if (new[] { "total", "executed", "passed" }.Any(n => reader.GetAttribute(n) != count)) throw Refuse("admission-trx-counters");
                            if (new[] { "failed", "error", "timeout", "aborted", "notExecuted" }.Any(n => reader.GetAttribute(n) != "0")) throw Refuse("admission-trx-counters");
                            break;
                    }
                }
            }
            if (!terminal || !counters || results.Count != map.Count || definitions.Count != map.Count || methods.Count != map.Count) throw Refuse("admission-trx-incomplete");
            return results.Values.ToArray();
        }

        private static readonly Regex RcLine = new Regex(@"\Arc/\d{4}-\d{2}-\d{2}\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// The admission of a carried chunk: the sealed envelope names this run and source, the evidence bundle verifies,
        /// the current manifest rehashes against the current tree, the algorithm digests bind, the Full anchor and the
        /// empty failure history are proven, the forced-Full policy is clear, and the proof chain (carried hops down to
        /// one executed proof) holds unchanged inputs, four stage proofs, six receipts and a passing TRX. Dirty and
        /// force-Full unless admitted, and never admitted while the algorithm coverage is incomplete. No gate credit.
        /// </summary>
        public static RcAdmissionResult Read(string path, string expectedDigest, string evidenceRoot, string repositoryRoot, string expectedSha, string expectedRunId,
            string currentSha, RcChunkInputManifest currentManifest, RcBoundaryChunk chunk, RcAdmissionContext currentContext, DateTimeOffset utcNow)
        {
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            var eligible = false;
            var coverage = AlgorithmCoverage();
            using var git = new RcInputInventory.GitReader(repositoryRoot);
            try
            {
                if (chunk.Boundary == "unassigned" || chunk.Collision || chunk.AlwaysDirty) throw Refuse("collision-or-unassigned");
                var envelope = ReadJson(path, expectedDigest);
                AssertKeys(envelope, "format", "sourceSha", "treeId", "runId", "repository", "platform", "rcLine", "inventorySha256", "proofPath", "fullAnchor", "contract",
                    "history", "releaseEvidence", "anchorPath", "historyPath");
                var origin = git.Run(new[] { 0, 1 }, "config", "--get", "remote.origin.url").Text.Trim();
                if (S(envelope, "format") != "antiphon-rc-admission-v1" || S(envelope, "sourceSha") != expectedSha || S(envelope, "runId") != expectedRunId
                    || string.IsNullOrEmpty(expectedRunId) || S(envelope, "repository") != origin || S(envelope, "repository") != currentContext.Repository
                    || S(envelope, "platform") != currentContext.Platform || S(envelope, "rcLine") != currentContext.RcLine || S(envelope, "platform") != "windows"
                    || S(envelope, "rcLine") == null || !RcLine.IsMatch(S(envelope, "rcLine"))) throw Refuse("admission-run-source-identity");
                if (git.Run("merge-base", expectedSha, currentSha).Trim() != expectedSha) throw Refuse("admission-lineage");
                var inventory = HistoricalEvidence.Verify(evidenceRoot, S(envelope, "inventorySha256"));
                if (!inventory.valid) throw Refuse("inventory-unproven");
                var snapshot = RcInputInventory.Read(git, currentSha);
                var current = RestoreManifest(currentManifest, chunk, snapshot);
                if (currentManifest.SourceSha != currentSha) throw Refuse("admission-current-source");
                var release = F(envelope, "releaseEvidence");
                AssertKeys(release, "status", "algorithmDigest", "coveredPaths");
                var coveredPaths = Items(release, "coveredPaths").Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText()).ToArray();
                coverage = AlgorithmCoverage(coveredPaths);
                var algorithm = AlgorithmDigest(git, expectedSha, coveredPaths);
                var currentAlgorithm = AlgorithmDigest(git, currentSha, coveredPaths);
                if (S(release, "algorithmDigest") != algorithm) throw Refuse("algorithm-binding-mismatch");
                var fullAnchor = F(envelope, "fullAnchor");
                var anchorObject = EvidenceObject(inventory, evidenceRoot, S(envelope, "anchorPath"));
                var anchorProof = ReadJson(anchorObject.Path, anchorObject.Digest);
                AssertKeys(anchorProof, "format", "sourceSha", "completedAtUtc", "mode", "carriedCount", "outcome", "requiredCount", "executedCount", "caseDigest");
                if (S(anchorProof, "format") != "antiphon-rc-full-anchor-v1" || anchorObject.Digest != S(fullAnchor, "closureDigest")
                    || S(anchorProof, "sourceSha") != S(fullAnchor, "sourceSha") || S(anchorProof, "completedAtUtc") != S(fullAnchor, "completedAtUtc")
                    || S(anchorProof, "mode") != "Full" || N(anchorProof, "carriedCount") != 0 || S(anchorProof, "outcome") != "Passed"
                    || !(N(anchorProof, "requiredCount") > 0) || N(anchorProof, "requiredCount") != N(anchorProof, "executedCount") || !RcForcedFull.IsDigest(S(anchorProof, "caseDigest")))
                    throw Refuse("full-anchor-unproven");
                if (git.Run("merge-base", S(anchorProof, "sourceSha"), expectedSha).Trim() != S(anchorProof, "sourceSha")) throw Refuse("full-anchor-lineage");
                var historyObject = EvidenceObject(inventory, evidenceRoot, S(envelope, "historyPath"));
                var historyProof = ReadJson(historyObject.Path, historyObject.Digest);
                AssertKeys(historyProof, "format", "runId", "anchorDigest", "complete", "failures");
                if (S(historyProof, "format") != "antiphon-rc-history-v1" || S(historyProof, "runId") != expectedRunId || S(historyProof, "anchorDigest") != anchorObject.Digest
                    || !True(historyProof, "complete") || F(historyProof, "failures").ValueKind != JsonValueKind.Array || F(historyProof, "failures").GetArrayLength() != 0)
                    throw Refuse("failure-history-unproven");
                // A nonempty failure history conservatively requires execution; no contained fix is inferred from a later
                // green result or a caller-supplied flag.
                var forced = RcForcedFull.Decision(currentContext.Contract, F(envelope, "contract"), F(envelope, "history"), fullAnchor, utcNow, false);
                foreach (var r in forced.Reasons) reasons.Add(r);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var proofPath = S(envelope, "proofPath");
                RcChunkInputManifest before = null;
                var caseOutcomes = Array.Empty<string>();
                var top = default(JsonElement);
                for (var depth = 0; ; depth++)
                {
                    if (depth >= 64 || !seen.Add(proofPath ?? "")) throw Refuse("admission-origin-cycle-or-depth");
                    var proofObject = EvidenceObject(inventory, evidenceRoot, proofPath);
                    var proof = ReadJson(proofObject.Path, proofObject.Digest);
                    AssertKeys(proof, "format", "kind", "originPath", "sourceSha", "treeId", "runId", "chunk", "manifest", "outcome", "complete", "exitCode", "sourceClean", "custody",
                        "assemblyDigest", "buildOutputDigest", "discoveryOutputDigest", "launchOutputDigest", "receipts", "trxPath", "caseMap", "outputPath", "stageReceipts", "anchorDigest");
                    if (S(proof, "format") != "antiphon-rc-execution-proof-v1" || S(proof, "outcome") != "Passed" || !True(proof, "complete") || N(proof, "exitCode") != 0
                        || !True(proof, "sourceClean") || S(proof, "custody") != "exclusive-owned-checkout") throw Refuse("source-build-launch-unproven");
                    var proofSha = S(proof, "sourceSha");
                    if (depth == 0)
                    {
                        top = proof;
                        if (proofSha != expectedSha || S(proof, "runId") != expectedRunId || S(proof, "treeId") != S(envelope, "treeId")) throw Refuse("admission-proof-identity");
                    }
                    if (git.Run("merge-base", proofSha, currentSha).Trim() != proofSha) throw Refuse("admission-origin-lineage");
                    var oldSnapshot = RcInputInventory.Read(git, proofSha);
                    if (oldSnapshot.TreeId != S(proof, "treeId")) throw Refuse("admission-proof-tree");
                    var proofChunk = ReadRecord<RcBoundaryChunk>(F(proof, "chunk"));
                    var old = RestoreManifest(ReadRecord<RcChunkInputManifest>(F(proof, "manifest")), proofChunk, oldSnapshot);
                    var comparison = RcChunkDigest.Compare(old, current);
                    if (comparison.Dirty || proofChunk.ChunkId != chunk.ChunkId || proofChunk.FullDigest != chunk.FullDigest) throw Refuse("admission-origin-inputs");
                    if (S(proof, "anchorDigest") != anchorObject.Digest) throw Refuse("admission-origin-anchor");
                    var output = EvidenceObject(inventory, evidenceRoot, S(proof, "outputPath"));
                    if (output.Digest != S(proof, "assemblyDigest")) throw Refuse("admission-output-bytes");
                    var stages = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var stageRef in Items(proof, "stageReceipts"))
                    {
                        AssertKeys(stageRef, "stage", "path");
                        var stageName = S(stageRef, "stage");
                        if (Array.IndexOf(new[] { "source", "build", "discovery", "launch" }, stageName) < 0 || !stages.Add(stageName)) throw Refuse("admission-stage-duplicate");
                        var stageObject = EvidenceObject(inventory, evidenceRoot, S(stageRef, "path"));
                        var stage = ReadJson(stageObject.Path, stageObject.Digest);
                        AssertKeys(stage, "format", "stage", "sourceSha", "treeId", "runId", "manifestDigest", "outputDigest", "sourceState", "custody");
                        if (S(stage, "format") != "antiphon-rc-stage-proof-v1" || S(stage, "stage") != stageName || S(stage, "sourceSha") != proofSha
                            || S(stage, "treeId") != S(proof, "treeId") || S(stage, "runId") != S(proof, "runId") || S(stage, "manifestDigest") != old.Digest
                            || S(stage, "outputDigest") != output.Digest || S(stage, "sourceState") != "clean" || S(stage, "custody") != "exclusive-owned-checkout")
                            throw Refuse("source-build-launch-unproven");
                    }
                    if (stages.Count != 4) throw Refuse("source-build-launch-unproven");
                    foreach (var name in new[] { "assemblyDigest", "buildOutputDigest", "discoveryOutputDigest", "launchOutputDigest" })
                        if (!RcForcedFull.IsDigest(S(proof, name)) || S(proof, name) != S(proof, "assemblyDigest")) throw Refuse("source-build-launch-unproven");
                    var receipts = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var reference in Items(proof, "receipts"))
                    {
                        AssertKeys(reference, "kind", "path", "provenancePath");
                        var kind = S(reference, "kind");
                        if (!receipts.Add(kind ?? "")) throw Refuse("admission-receipt-duplicate");
                        var stored = old.ActualReceipts.Where(r => r.Kind == kind).ToArray();
                        if (stored.Length != 1 || stored[0].SourceSha != proofSha) throw Refuse("admission-receipt-source");
                        var bytes = EvidenceObject(inventory, evidenceRoot, S(reference, "path"));
                        var provenance = EvidenceObject(inventory, evidenceRoot, S(reference, "provenancePath"));
                        if (bytes.Digest != stored[0].Digest || provenance.Digest != stored[0].ProvenanceDigest) throw Refuse("admission-receipt-digest");
                        var binding = ReadJson(provenance.Path, provenance.Digest);
                        AssertKeys(binding, "format", "sourceSha", "runId", "kind", "digest");
                        if (S(binding, "format") != "antiphon-rc-actual-input-v1" || S(binding, "sourceSha") != proofSha || S(binding, "runId") != S(proof, "runId")
                            || S(binding, "kind") != kind || S(binding, "digest") != bytes.Digest) throw Refuse("admission-receipt-provenance");
                    }
                    if (receipts.Count != 6 || old.Unknown.Length > 0) throw Refuse("admission-receipt-incomplete");
                    before ??= old;
                    if (S(proof, "kind") == "executed")
                    {
                        if (F(proof, "originPath").ValueKind != JsonValueKind.Null && F(proof, "originPath").ValueKind != JsonValueKind.Undefined) throw Refuse("admission-origin-ambiguous");
                        var trx = EvidenceObject(inventory, evidenceRoot, S(proof, "trxPath"));
                        caseOutcomes = ReadTrx(trx.Path, old.Cases, F(proof, "caseMap"));
                        break;
                    }
                    if (S(proof, "kind") != "carried" || string.IsNullOrEmpty(S(proof, "originPath"))) throw Refuse("admission-origin-missing");
                    proofPath = S(proof, "originPath");
                }
                var facts = new RcAdmissionFacts
                {
                    InventoryValid = true, ProvenanceValid = true, Outcome = S(top, "outcome"), Complete = F(top, "complete").ValueKind == JsonValueKind.True ? true : (bool?)null,
                    ReleaseStatus = S(release, "status"), AlgorithmDigest = algorithm, CurrentAlgorithmDigest = currentAlgorithm, AnchorAtUtc = S(fullAnchor, "completedAtUtc"),
                    AnchorVerified = !forced.ForceFull, HistoryValid = !forced.ForceFull, OriginValid = true, ContractValid = !forced.ForceFull, CaseOutcomes = caseOutcomes,
                };
                var decision = CarryAdmissionDecision(before, current, facts, chunk, utcNow);
                foreach (var r in decision.Reasons) reasons.Add(r);
                eligible = decision.Eligible && reasons.Count == 0;
                var verified = HistoricalEvidence.Verify(evidenceRoot, S(envelope, "inventorySha256"));
                if (!verified.valid || FileDigest(path) != expectedDigest) throw Refuse("admission-changed-during-read");
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                reasons.Add(e.Message);
                eligible = false;
            }
            foreach (var r in coverage.Reasons) reasons.Add(r);
            var admitted = eligible && coverage.Complete;
            return new RcAdmissionResult
            {
                Eligible = eligible, Admitted = admitted, Dirty = !admitted, ForceFull = !admitted, Reasons = reasons.ToArray(), Coverage = coverage,
            };
        }
    }
}
