// CARD-1836 S5b (moved from rc-ledger-import.ps1): read-only historical extraction and unproven inventory. No launch,
// trust activation, network fetch, build, test, publication or historical writes. Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Rc;

namespace Antiphon.Nightly
{
    public static class RcLedgerImport
    {
        private static Exception Refuse(string what) => new RcAdmissionRefusal(what);
        private static readonly Regex FullSha = new Regex(@"\A[a-f0-9]{40}\z", RegexOptions.CultureInvariant);
        private static readonly Regex SuiteName = new Regex(@"\A[a-z][a-z0-9-]*\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// Exports a source run's evidence into a new evidence root (S5a confinement and immutability; an existing
        /// output is refused), verifies the inventory, and, with a current manifest request ({ chunk, manifest }),
        /// writes the suite's shard ledger with every discovered method unproven. Writes admission-report.json; nothing
        /// is admitted and Full stays forced. Returns the report.
        /// </summary>
        public static JsonObject Run(string sourceRunRoot, string evidenceRoot, string repositoryRoot, string expectedSha, string expectedRunId, string currentManifestPath,
            string checkoutRoot)
        {
            if (expectedSha == null || !FullSha.IsMatch(expectedSha) || string.IsNullOrEmpty(expectedRunId)) throw Refuse("import-exact-source-and-run-required");
            var bundle = HistoricalEvidence.Export(sourceRunRoot, evidenceRoot, repositoryRoot, checkoutRoot, null, null);
            var check = HistoricalEvidence.Verify(evidenceRoot, bundle.inventorySha256);
            if (!check.valid) throw Refuse("import-inventory-unproven");
            var coverage = RcAdmission.AlgorithmCoverage();
            var dirty = 0;
            var shards = new JsonArray();
            if (!string.IsNullOrEmpty(currentManifestPath))
            {
                var request = RcAdmission.ReadJson(currentManifestPath, RcAdmission.FileDigest(currentManifestPath));
                if (request.ValueKind != JsonValueKind.Object) throw Refuse("invalid-catalog-object");
                foreach (var p in request.EnumerateObject()) if (p.Name != "chunk" && p.Name != "manifest") throw Refuse("invalid-catalog-field:" + p.Name);
                var chunk = request.GetProperty("chunk");
                var manifest = request.GetProperty("manifest");
                var suite = chunk.TryGetProperty("suite", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (suite == null || !SuiteName.IsMatch(suite)) throw Refuse("import-invalid-suite");
                string Text(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var methods = manifest.TryGetProperty("methods", out var m) && m.ValueKind == JsonValueKind.Array ? m.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                // A caller's discovery list is an obligation, never historical evidence. No passed rows are written until
                // the later activation/complete-graph slice.
                var identities = methods.Select(method =>
                {
                    var key = Text(method, "identityKey") ?? "";
                    var slash = key.IndexOf('/');
                    if (slash <= 0) throw Refuse("import-invalid-method");
                    return new RcLedgerIdentity
                    {
                        Suite = suite, ClassName = key.Substring(0, slash), Method = key.Substring(slash + 1), Boundary = Text(chunk, "boundary"), InputDigest = "unknown",
                        Outcome = "unproven",
                    };
                }).ToArray();
                var ledger = RcLedger.Normalize(new RcLedgerDocument
                {
                    Platform = "windows", Repository = "antiphon", CandidateId = expectedRunId, CandidateSha = expectedSha, RunId = expectedRunId, Attempt = 1,
                    Contract = new RcLedgerContract { PolicyHash = "unknown", CatalogHash = "unknown", SelectorHash = "unknown", RuntimeHash = "unknown" },
                    ForcedFull = new[] { "release-graph-incomplete", "historical-provenance-unproven" }, Identities = identities,
                    Chunks = new[]
                    {
                        new RcLedgerChunk
                        {
                            ChunkId = Text(chunk, "chunkId"), Boundary = Text(chunk, "boundary"), Suite = suite, IdentityKeys = methods.Select(x => Text(x, "identityKey")).ToArray(),
                            InputDigest = "unknown",
                        },
                    },
                });
                var relative = "shards/" + suite + ".json";
                var path = Path.Combine(evidenceRoot, relative);
                RcLedger.Write(path, ledger);
                RcLedger.Read(path);
                shards.Add(new JsonObject { ["suite"] = suite, ["path"] = relative, ["sha256"] = RcAdmission.FileDigest(path) });
                dirty = 1;
            }
            var report = new JsonObject
            {
                ["format"] = "antiphon-rc-import-report-v1", ["expectedSha"] = expectedSha, ["expectedRunId"] = expectedRunId, ["inventorySha256"] = bundle.inventorySha256,
                ["admitted"] = 0, ["dirty"] = dirty, ["unproven"] = dirty, ["fullAnchorKnown"] = false, ["forceFull"] = true, ["gateCredit"] = false, ["shards"] = shards,
                ["coverage"] = JsonSerializer.SerializeToNode(coverage, RcAdmission.RecordJson),
                ["reasons"] = new JsonArray("historical-provenance-unproven", "full-anchor-unproven", "release-graph-incomplete"),
                ["reasonCounts"] = new JsonObject { ["historical-provenance-unproven"] = dirty, ["full-anchor-unproven"] = dirty, ["release-graph-incomplete"] = dirty },
            };
            File.WriteAllText(Path.Combine(evidenceRoot, "admission-report.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return report;
        }
    }
}
