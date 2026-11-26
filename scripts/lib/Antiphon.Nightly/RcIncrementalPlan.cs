// CARD-1836 S5c (moved from rc-incremental-plan.ps1): diagnostic incremental plans. No scheduler, activation or
// closure caller: the effective partition is always forced Full; the proposal only records what an admitted
// incremental run would have launched. Plans, revisions and the index are write-once JSON files whose digests cover
// their canonical text (keys sorted ordinally). Plain C#: no PowerShell types.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class RcIncrementalPlan
    {
        private static Exception Refuse(string what) => new RcAdmissionRefusal(what);
        private static readonly Regex FullSha = new Regex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);
        private static readonly Regex RcLine = new Regex(@"\Arc/\d{4}-\d{2}-\d{2}\z", RegexOptions.CultureInvariant);
        private static readonly string[] Outcomes = { "Passed", "Failed", "Interrupted", "Unproven", "NotRun", "ExpectedSkip", "UnexpectedSkip" };

        // ---- canonical values -----------------------------------------------------------------------------------

        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static void Write(Utf8JsonWriter writer, JsonNode node)
        {
            switch (node)
            {
                case null: writer.WriteNullValue(); break;
                case JsonObject o:
                    writer.WriteStartObject();
                    foreach (var p in o.OrderBy(p => p.Key, StringComparer.Ordinal)) { writer.WritePropertyName(p.Key); Write(writer, p.Value); }
                    writer.WriteEndObject();
                    break;
                case JsonArray a:
                    writer.WriteStartArray();
                    foreach (var item in a) Write(writer, item);
                    writer.WriteEndArray();
                    break;
                default: node.WriteTo(writer); break;
            }
        }

        /// <summary>The value's canonical compact JSON: object keys in ordinal order.</summary>
        public static string CanonicalJson(JsonNode value)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) Write(writer, value);
            return new UTF8Encoding(false, true).GetString(buffer.ToArray());
        }

        /// <summary>SHA-256 of "antiphon-rc-plan-value-v1\n" and the canonical JSON.</summary>
        public static string ValueDigest(JsonNode value) =>
            Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes("antiphon-rc-plan-value-v1\n" + CanonicalJson(value)))).ToLowerInvariant();

        private static JsonNode Node(JsonElement element) => element.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText());

        private static string Tuple(params string[] fields) => CanonicalJson(new JsonArray(fields.Select(f => (JsonNode)JsonValue.Create(f)).ToArray()));

        // ---- node access ----------------------------------------------------------------------------------------

        private static void AssertKeys(JsonNode node, params string[] allowed)
        {
            if (!(node is JsonObject o)) throw Refuse("invalid-catalog-object");
            foreach (var p in o) if (Array.IndexOf(allowed, p.Key) < 0) throw Refuse("invalid-catalog-field:" + p.Key);
        }

        private static JsonNode F(JsonNode o, string name) => o is JsonObject obj && obj.TryGetPropertyValue(name, out var v) ? v : null;

        private static string S(JsonNode o, string name) => F(o, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        private static bool? B(JsonNode o, string name) => F(o, name) is JsonValue v && v.GetValueKind() is var k && (k == JsonValueKind.True || k == JsonValueKind.False) ? k == JsonValueKind.True : (bool?)null;

        private static long? Integer(JsonNode o, string name) =>
            F(o, name) is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.ToJsonString().IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(v.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : (long?)null;

        // Through the JSON text: a value created from a long does not convert with GetValue<double>.
        private static double? Number(JsonNode o, string name) =>
            F(o, name) is JsonValue v && v.GetValueKind() == JsonValueKind.Number && double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;

        private static JsonArray A(JsonNode o, string name) => F(o, name) as JsonArray;

        private static string[] Strings(JsonArray a) => (a ?? new JsonArray()).Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).ToArray();

        private static JsonArray Arr(IEnumerable<string> values) => new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());

        private static JsonArray Arr(IEnumerable<JsonNode> values) => new JsonArray(values.ToArray());

        // ---- partitions -----------------------------------------------------------------------------------------

        /// <summary>The obligation key: the JSON tuple [suite, className, method, caseKey] (boundaries survive separators).</summary>
        public static string ObligationKey(JsonNode row)
        {
            foreach (var field in new[] { "suite", "className", "method", "caseKey" }) RcChunkDigest.AssertDigestText(S(row, field), "obligation-" + field);
            return Tuple(S(row, "suite"), S(row, "className"), S(row, "method"), S(row, "caseKey"));
        }

        private static JsonObject CopyOrigin(JsonNode origin, string suite)
        {
            AssertKeys(origin, "suite", "envelopeDigest", "runId", "sourceSha", "chunkId", "wallSeconds");
            foreach (var field in new[] { "suite", "runId", "chunkId" }) RcChunkDigest.AssertDigestText(S(origin, field), "origin-" + field);
            var wall = Number(origin, "wallSeconds");
            if (S(origin, "suite") != suite || !RcForcedFull.IsDigest(S(origin, "envelopeDigest")) || S(origin, "sourceSha") == null || !FullSha.IsMatch(S(origin, "sourceSha"))
                || wall == null || !double.IsFinite(wall.Value) || wall < 0) throw Refuse("invalid-origin");
            return new JsonObject
            {
                ["suite"] = suite, ["envelopeDigest"] = S(origin, "envelopeDigest"), ["runId"] = S(origin, "runId"), ["sourceSha"] = S(origin, "sourceSha"),
                ["chunkId"] = S(origin, "chunkId"), ["wallSeconds"] = wall.Value,
            };
        }

        private static JsonArray SortedReasons(IEnumerable<string> reasons) => Arr(RcInputInventory.Sorted(reasons));

        /// <summary>
        /// The exact partition of every discovered obligation into Dirty, Sample and Carry. A suite without a trusted
        /// result adapter is one whole-suite obligation; ambiguous cases and unassigned boundaries stay dirty; whole
        /// methods and packed chunks launch together (a fixed point, so a carry that will run is not a saving). With
        /// forceExecution, or when the decisions do not cover the universe exactly, nothing carries.
        /// </summary>
        public static JsonObject ExactPartition(JsonArray suites, JsonArray decisions, bool forceExecution)
        {
            var universe = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var order = new List<string>();
            var suiteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chunks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var suite in suites ?? new JsonArray())
            {
                AssertKeys(suite, "suite", "adapter", "cases");
                var suiteName = S(suite, "suite");
                RcChunkDigest.AssertDigestText(suiteName, "suite");
                if (!suiteNames.Add(suiteName)) throw Refuse("duplicate-suite");
                if (!(F(suite, "cases") is JsonArray cases)) throw Refuse("missing-suite-discovery");
                var fresh = S(suite, "adapter") != "tunit";
                var rows = cases.ToList();
                if (fresh)
                {
                    if (rows.Count > 0) throw Refuse("fresh-suite-must-be-whole");
                    // No trustworthy result adapter: require fresh execution of the WHOLE suite.
                    rows = new List<JsonNode>
                    {
                        new JsonObject
                        {
                            ["suite"] = suiteName, ["className"] = "<suite>", ["method"] = "Execute", ["caseKey"] = "entire-suite", ["stable"] = true,
                            ["boundary"] = "suite", ["chunkId"] = "suite-" + suiteName,
                        },
                    };
                }
                else if (rows.Count == 0) throw Refuse("empty-tunit-discovery");
                foreach (var c in rows)
                {
                    if (universe.Count >= 1000000) throw Refuse("obligation-limit");
                    AssertKeys(c, "suite", "className", "method", "caseKey", "stable", "boundary", "chunkId");
                    var key = ObligationKey(c);
                    var stable = B(c, "stable");
                    if (S(c, "suite") != suiteName || stable == null) throw Refuse("invalid-suite-case");
                    RcChunkDigest.AssertDigestText(S(c, "boundary"), "boundary");
                    RcChunkDigest.AssertDigestText(S(c, "chunkId"), "chunkId");
                    var chunkKey = Tuple(suiteName, S(c, "chunkId"));
                    if (chunks.TryGetValue(chunkKey, out var boundary) && boundary != S(c, "boundary")) throw Refuse("mixed-boundary-chunk");
                    chunks[chunkKey] = S(c, "boundary");
                    var reasons = new List<string>();
                    if (fresh) reasons.Add("fresh-suite-adapter-required");
                    if (stable == false) reasons.Add("ambiguous-case");
                    if (S(c, "boundary") == "unassigned") reasons.Add("unassigned-boundary");
                    if (NightlyTestsPlan.IsInvariantClass(S(c, "className"))) reasons.Add("repository-invariant-fresh-execution");
                    var row = new JsonObject
                    {
                        ["key"] = key, ["suite"] = suiteName, ["className"] = S(c, "className"), ["method"] = S(c, "method"), ["caseKey"] = S(c, "caseKey"),
                        ["boundary"] = S(c, "boundary"), ["chunkId"] = S(c, "chunkId"), ["stable"] = stable.Value, ["fresh"] = fresh, ["disposition"] = "Dirty",
                        ["reasons"] = Arr(reasons), ["origin"] = null,
                    };
                    if (!universe.TryAdd(key, row)) throw Refuse("duplicate-obligation");
                    order.Add(key);
                }
            }
            if (suiteNames.Count == 0) throw Refuse("empty-suite-universe");
            string[] Reasons(JsonObject row) => Strings(A(row, "reasons"));
            if (!forceExecution)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var decision in decisions ?? new JsonArray())
                {
                    AssertKeys(decision, "suite", "className", "method", "caseKey", "disposition", "reasons", "origin");
                    var key = ObligationKey(decision);
                    if (!seen.Add(key)) throw Refuse("duplicate-decision");
                    if (!universe.TryGetValue(key, out var row)) throw Refuse("extra-decision");
                    var disposition = S(decision, "disposition");
                    if ((disposition != "Dirty" && disposition != "Sample" && disposition != "Carry") || !(F(decision, "reasons") is JsonArray decided)) throw Refuse("invalid-decision");
                    var decidedReasons = Strings(decided);
                    foreach (var r in decidedReasons) RcChunkDigest.AssertDigestText(r, "reason");
                    if (disposition != "Dirty")
                    {
                        var origin = CopyOrigin(F(decision, "origin"), S(row, "suite"));
                        if (!NightlyTestsPlan.IsInvariantClass(S(row, "className"))) row["origin"] = origin;
                    }
                    if (Reasons(row).Length == 0) row["disposition"] = disposition;
                    row["reasons"] = SortedReasons(Reasons(row).Concat(decidedReasons));
                }
                foreach (var key in order)
                    if (B(universe[key], "fresh") != true && !seen.Contains(key)) throw Refuse("decision-universe-mismatch");
            }
            else foreach (var key in order) universe[key]["reasons"] = SortedReasons(Reasons(universe[key]).Append("forced-full"));
            // Fixed point: whole methods and packed chunks launch together.
            bool changed;
            do
            {
                changed = false;
                foreach (var groupBy in new[] { "method", "chunk" })
                {
                    var groups = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
                    var groupOrder = new List<string>();
                    foreach (var key in order)
                    {
                        var row = universe[key];
                        var group = groupBy == "method" ? Tuple(S(row, "suite"), S(row, "className"), S(row, "method")) : Tuple(S(row, "suite"), S(row, "chunkId"));
                        if (!groups.TryGetValue(group, out var members)) { groups[group] = members = new List<JsonObject>(); groupOrder.Add(group); }
                        members.Add(row);
                    }
                    foreach (var group in groupOrder)
                    {
                        var members = groups[group];
                        var kind = members.Any(r => S(r, "disposition") == "Dirty") ? "Dirty" : members.Any(r => S(r, "disposition") == "Sample") ? "Sample" : "Carry";
                        foreach (var row in members)
                        {
                            if (S(row, "disposition") == kind) continue;
                            row["disposition"] = kind;
                            row["reasons"] = SortedReasons(Reasons(row).Append("whole-" + groupBy + "-executes"));
                            changed = true;
                        }
                    }
                }
            } while (changed);
            var required = RcInputInventory.Sorted(universe.Keys).Select(k => universe[k]).ToArray();
            JsonArray Rows(Func<JsonObject, bool> keep) => Arr(required.Where(keep).Select(r => (JsonNode)r.DeepClone()));
            var identityRows = new JsonArray(required.Select(r => (JsonNode)Arr(new[]
            {
                S(r, "suite"), S(r, "className"), S(r, "method"), S(r, "caseKey"), S(r, "boundary"), S(r, "chunkId"), B(r, "stable") == true ? "True" : "False",
                B(r, "fresh") == true ? "True" : "False",
            })).ToArray());
            var partition = new JsonObject
            {
                ["required"] = Rows(_ => true), ["dirty"] = Rows(r => S(r, "disposition") == "Dirty"), ["sampled"] = Rows(r => S(r, "disposition") == "Sample"),
                ["carried"] = Rows(r => S(r, "disposition") == "Carry"), ["launch"] = Rows(r => S(r, "disposition") != "Carry"), ["universeDigest"] = ValueDigest(identityRows),
            };
            if (((JsonArray)partition["dirty"]).Count + ((JsonArray)partition["sampled"]).Count + ((JsonArray)partition["carried"]).Count != required.Length)
                throw Refuse("partition-not-exact");
            partition["digest"] = ValueDigest(partition);
            return partition;
        }

        /// <summary>
        /// A partition's status from launch results (each key in the launch set once, with a known outcome): counts per
        /// outcome, not-run launches, and the unique carried origin chunks with their historical wall seconds.
        /// </summary>
        public static JsonObject PartitionStatus(JsonNode partition, JsonArray results)
        {
            var launch = new HashSet<string>((A(partition, "launch") ?? new JsonArray()).Select(r => S(r, "key") ?? ""), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outcomes = Outcomes.ToDictionary(o => o, _ => 0, StringComparer.Ordinal);
            foreach (var result in results ?? new JsonArray())
            {
                AssertKeys(result, "key", "outcome");
                var key = S(result, "key");
                if (key == null || !launch.Contains(key)) throw Refuse("result-outside-launch");
                if (!seen.Add(key)) throw Refuse("duplicate-result");
                var outcome = S(result, "outcome");
                if (outcome == null || !outcomes.ContainsKey(outcome)) throw Refuse("invalid-result-outcome");
                outcomes[outcome]++;
            }
            var origins = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var row in A(partition, "carried") ?? new JsonArray())
            {
                var origin = CopyOrigin(F(row, "origin"), S(row, "suite"));
                var key = Tuple(S(origin, "runId"), S(origin, "sourceSha"), S(origin, "suite"), S(origin, "chunkId"));
                if (origins.TryGetValue(key, out var known))
                {
                    if (Number(known, "wallSeconds") != Number(origin, "wallSeconds")) throw Refuse("origin-wall-conflict");
                    if (S(known, "envelopeDigest") != S(origin, "envelopeDigest")) throw Refuse("origin-envelope-conflict");
                }
                origins[key] = origin;
            }
            int Count(string name) => (A(partition, name) ?? new JsonArray()).Count;
            return new JsonObject
            {
                ["planned"] = Count("required"), ["dirty"] = Count("dirty"), ["sampled"] = Count("sampled"), ["carried"] = Count("carried"),
                ["executed"] = outcomes["Passed"] + outcomes["Failed"] + outcomes["ExpectedSkip"] + outcomes["UnexpectedSkip"], ["passed"] = outcomes["Passed"],
                ["failed"] = outcomes["Failed"] + outcomes["UnexpectedSkip"], ["interrupted"] = outcomes["Interrupted"], ["unproven"] = outcomes["Unproven"],
                ["notRun"] = launch.Count - seen.Count + outcomes["NotRun"], ["expectedSkip"] = outcomes["ExpectedSkip"], ["uniqueOriginChunks"] = origins.Count,
                ["origins"] = Arr(RcInputInventory.Sorted(origins.Keys).Select(k => (JsonNode)origins[k])),
                ["historicalPotentialWallSeconds"] = origins.Values.Sum(o => Number(o, "wallSeconds") ?? 0),
            };
        }

        // ---- plans ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The diagnostic plan for a clean checkout at the expected candidate: the proposed partition (from the
        /// request's decisions, or forced when they are invalid), the effective partition (always forced Full), and
        /// every reason Full is forced: activation disabled, algorithm coverage, unproven receipts, unadmitted baselines.
        /// </summary>
        public static JsonObject NewPlan(JsonNode request, string repositoryRoot, string expectedSha)
        {
            AssertKeys(request, "format", "candidateSha", "treeId", "platform", "rcLine", "suites", "decisions", "bindings", "admissions", "exclusionDigest", "contractDigest",
                "boundaryMapDigest", "historyDigest", "sampleSeed", "sampleBudget");
            using var git = new RcInputInventory.GitReader(repositoryRoot);
            string Rev(params string[] args) => git.Run(new[] { 0, 1, 128 }, args).Text.Trim();
            if (S(request, "format") != "antiphon-rc-plan-request-v1" || expectedSha == null || !FullSha.IsMatch(expectedSha) || S(request, "candidateSha") != expectedSha
                || Rev("rev-parse", "--verify", expectedSha + "^{commit}") != expectedSha || Rev("rev-parse", "HEAD") != expectedSha
                || Rev("rev-parse", expectedSha + "^{tree}") != S(request, "treeId")) throw Refuse("candidate-binding-mismatch");
            var platform = S(request, "platform");
            if ((platform != "windows" && platform != "linux") || S(request, "rcLine") == null || !RcLine.IsMatch(S(request, "rcLine"))) throw Refuse("invalid-plan-context");
            foreach (var field in new[] { "exclusionDigest", "contractDigest", "boundaryMapDigest", "historyDigest" })
                if (!RcForcedFull.IsDigest(S(request, field))) throw Refuse("missing-plan-binding:" + field);
            RcChunkDigest.AssertDigestText(S(request, "sampleSeed"), "sample-seed");
            var budget = Integer(request, "sampleBudget");
            if (budget == null || budget < 0) throw Refuse("invalid-sample-budget");
            // Deliberately not an activation switch: S5b's complete graph is unavailable and S6/S7 have not connected
            // launch or closure authority.
            var reasons = new SortedSet<string>(StringComparer.Ordinal) { "activation-disabled" };
            foreach (var r in RcAdmission.AlgorithmCoverage().Reasons) reasons.Add(r);
            var suites = A(request, "suites");
            JsonObject proposal;
            try { proposal = ExactPartition(suites, A(request, "decisions"), false); }
            catch (RcAdmissionRefusal e)
            {
                reasons.Add("proposal-invalid:" + e.Message);
                proposal = ExactPartition(suites, null, true);
            }
            catch (RcChunkDigestRefusal e)
            {
                reasons.Add("proposal-invalid:" + e.Message);
                proposal = ExactPartition(suites, null, true);
            }
            var bindings = F(request, "bindings");
            AssertKeys(bindings, "input", "output");
            foreach (var kind in new[] { "input", "output" })
            {
                try
                {
                    var binding = F(bindings, kind);
                    AssertKeys(binding, "path", "digest");
                    var receipt = Node(RcAdmission.ReadJson(S(binding, "path"), S(binding, "digest")));
                    AssertKeys(receipt, "kind", "candidateSha", "treeId", "universeDigest");
                    if (S(receipt, "kind") != kind || S(receipt, "candidateSha") != expectedSha || S(receipt, "treeId") != S(request, "treeId")
                        || S(receipt, "universeDigest") != S(proposal, "universeDigest")) throw Refuse("receipt-binding-mismatch");
                }
                catch (Exception e) when (!(e is OutOfMemoryException)) { reasons.Add("receipt-unproven:" + kind); }
            }
            var admissions = A(request, "admissions");
            if (admissions == null || admissions.Count == 0) reasons.Add("baseline-absent");
            foreach (var entry in admissions ?? new JsonArray())
            {
                try
                {
                    AssertKeys(entry, "path", "digest", "evidenceRoot", "sourceSha", "runId", "manifest", "chunk", "context");
                    var context = F(entry, "context");
                    var admission = RcAdmission.Read(S(entry, "path"), S(entry, "digest"), S(entry, "evidenceRoot"), repositoryRoot, S(entry, "sourceSha"), S(entry, "runId"), expectedSha,
                        RcAdmission.ReadRecord<RcChunkInputManifest>(Element(F(entry, "manifest"))), RcAdmission.ReadRecord<RcBoundaryChunk>(Element(F(entry, "chunk"))),
                        new RcAdmissionContext { Repository = S(context, "repository"), Platform = S(context, "platform"), RcLine = S(context, "rcLine"), Contract = Element(F(context, "contract")) },
                        DateTimeOffset.UtcNow);
                    foreach (var r in admission.Reasons) reasons.Add(r);
                    if (!admission.Admitted) reasons.Add("baseline-unproven");
                }
                catch (Exception e) when (!(e is OutOfMemoryException)) { reasons.Add("baseline-unproven"); }
            }
            var effective = ExactPartition(suites, null, true);
            // Revalidate the candidate at publication as well as admission. These receipts are diagnostic inputs, never
            // retroactive build or output provenance.
            if (Rev("rev-parse", "HEAD") != expectedSha || Rev("status", "--porcelain=v1", "--untracked-files=no").Length > 0) throw Refuse("candidate-binding-mismatch");
            foreach (var kind in new[] { "input", "output" })
            {
                try { RcAdmission.ReadJson(S(F(bindings, kind), "path"), S(F(bindings, kind), "digest")); }
                catch (Exception e) when (!(e is OutOfMemoryException)) { reasons.Add("receipt-unproven:" + kind); }
            }
            var plan = new JsonObject
            {
                ["format"] = "antiphon-rc-incremental-plan-v1", ["candidateSha"] = expectedSha, ["treeId"] = S(request, "treeId"), ["platform"] = platform,
                ["rcLine"] = S(request, "rcLine"), ["request"] = request.DeepClone(), ["proposalPartition"] = proposal, ["partition"] = effective,
                ["forcedReasons"] = Arr(reasons), ["effectiveMode"] = "Full", ["coverageScope"] = platform == "linux" ? "PlatformDiagnostic" : "IncrementalDiagnostic",
                ["coverageComplete"] = false, ["gateCredit"] = false, ["bindingState"] = "diagnostic-only", ["sampleSeed"] = S(request, "sampleSeed"), ["sampleBudget"] = budget.Value,
            };
            plan["digest"] = ValueDigest(plan);
            return plan;
        }

        private static JsonElement Element(JsonNode node) => node == null ? default : JsonSerializer.SerializeToElement(node);

        /// <summary>
        /// The full path, refused when it or any ancestor is a link (reparse point). An output path may not be inside
        /// the checkout.
        /// </summary>
        public static string AssertPath(string path, string checkoutRoot = null)
        {
            var full = Path.GetFullPath(path);
            if (checkoutRoot != null)
            {
                var checkout = Path.GetFullPath(checkoutRoot).TrimEnd(Path.DirectorySeparatorChar);
                if (full.Equals(checkout, StringComparison.OrdinalIgnoreCase) || full.StartsWith(checkout + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw Refuse("plan-output-inside-checkout");
            }
            var depth = 0;
            for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current), depth++)
            {
                if (depth >= 128) throw Refuse("plan-path-depth");
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Refuse("plan-linked-path");
            }
            return full;
        }

        private static string WriteFileOnce(string path, JsonNode value, string checkoutRoot)
        {
            var full = AssertPath(path, checkoutRoot);
            if (File.Exists(full)) throw Refuse("write-once");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            var bytes = new UTF8Encoding(false, true).GetBytes(CanonicalJson(value));
            if (bytes.Length > 16777216) throw Refuse("plan-file-limit");
            using (var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            RcAdmission.ReadJson(full, digest);
            return digest;
        }

        /// <summary>The plan's digest covers its body, its effective partition is the forced-Full one, and it claims no credit.</summary>
        public static void AssertPlan(JsonNode plan, string expectedDigest)
        {
            if (S(plan, "format") != "antiphon-rc-incremental-plan-v1" || !RcForcedFull.IsDigest(expectedDigest) || S(plan, "digest") != expectedDigest) throw Refuse("plan-digest-mismatch");
            var body = new JsonObject();
            foreach (var p in (JsonObject)plan) if (p.Key != "digest") body[p.Key] = p.Value?.DeepClone();
            if (ValueDigest(body) != expectedDigest) throw Refuse("plan-digest-mismatch");
            var effective = ExactPartition(A(F(plan, "request"), "suites"), null, true);
            if (S(plan, "effectiveMode") != "Full" || B(plan, "coverageComplete") != false || B(plan, "gateCredit") != false || ValueDigest(F(plan, "partition")) != ValueDigest(effective))
                throw Refuse("plan-authority-invalid");
        }

        /// <summary>Writes a verified plan once, outside the checkout.</summary>
        public static void WritePlan(string path, JsonNode plan, string checkoutRoot)
        {
            AssertPlan(plan, S(plan, "digest"));
            WriteFileOnce(path, plan, checkoutRoot);
        }

        /// <summary>A stored plan, read from its own bytes and verified against the expected digest.</summary>
        public static JsonObject ReadStoredPlan(string path, string expectedDigest)
        {
            var full = AssertPath(path);
            var plan = (JsonObject)Node(RcAdmission.ReadJson(full, RcAdmission.FileDigest(full)));
            AssertPlan(plan, expectedDigest);
            return plan;
        }

        private static JsonObject ReadPointer(string root, string expectedDigest)
        {
            var path = AssertPath(Path.Combine(root, "index.json"));
            var pointer = (JsonObject)Node(RcAdmission.ReadJson(path, RcAdmission.FileDigest(path)));
            AssertKeys(pointer, "format", "planDigest", "sequence", "path", "sha256");
            var sequence = Integer(pointer, "sequence");
            if (S(pointer, "format") != "antiphon-rc-plan-index-v1" || S(pointer, "planDigest") != expectedDigest || sequence == null || sequence <= 0
                || S(pointer, "path") != "revisions/" + sequence.Value.ToString(CultureInfo.InvariantCulture) + "/revision.json" || !RcForcedFull.IsDigest(S(pointer, "sha256")))
                throw Refuse("invalid-revision-pointer");
            return pointer;
        }

        /// <summary>
        /// Appends an open revision of launch results under the evidence root (outside the checkout, one writer at a
        /// time). Results are cumulative immutable facts: a revision may add observations but never drop or change one.
        /// </summary>
        public static JsonObject WriteRevision(string evidenceRoot, string planPath, string expectedPlanDigest, JsonArray results, string checkoutRoot)
        {
            var root = AssertPath(evidenceRoot, checkoutRoot);
            Directory.CreateDirectory(root);
            var lockPath = AssertPath(Path.Combine(root, ".writer.lock"), checkoutRoot);
            FileStream writerLock;
            try { writerLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw Refuse("writer-busy"); }
            using (writerLock)
            {
                results ??= new JsonArray();
                var plan = ReadStoredPlan(planPath, expectedPlanDigest);
                PartitionStatus(F(plan, "partition"), results);
                var storedPlan = Path.Combine(root, "plan.json");
                if (File.Exists(storedPlan)) ReadStoredPlan(storedPlan, expectedPlanDigest);
                else WritePlan(storedPlan, plan, checkoutRoot);
                var sequence = 1L;
                if (File.Exists(Path.Combine(root, "index.json")))
                {
                    // Validate the previous revision, not only the index, before extending it.
                    var previous = ReadStatus(root, expectedPlanDigest);
                    var next = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var result in results)
                        if (!next.TryAdd(S(result, "key") ?? "", S(result, "outcome"))) throw Refuse("duplicate-result");
                    foreach (var result in A(previous, "results") ?? new JsonArray())
                        if (!next.TryGetValue(S(result, "key") ?? "", out var outcome) || outcome != S(result, "outcome")) throw Refuse("revision-result-history");
                    sequence = Integer(previous, "sequence").Value + 1;
                }
                var relative = "revisions/" + sequence.ToString(CultureInfo.InvariantCulture) + "/revision.json";
                if (Directory.Exists(Path.Combine(root, "revisions", sequence.ToString(CultureInfo.InvariantCulture)))) throw Refuse("revision-already-exists");
                var revision = new JsonObject
                {
                    ["format"] = "antiphon-rc-plan-revision-v1", ["planDigest"] = expectedPlanDigest, ["sequence"] = sequence, ["state"] = "open", ["results"] = results.DeepClone(),
                };
                var digest = WriteFileOnce(Path.Combine(root, relative), revision, checkoutRoot);
                var pointer = new JsonObject
                {
                    ["format"] = "antiphon-rc-plan-index-v1", ["planDigest"] = expectedPlanDigest, ["sequence"] = sequence, ["path"] = relative, ["sha256"] = digest,
                };
                var temporary = Path.Combine(root, "index-" + Guid.NewGuid().ToString("N") + ".tmp");
                WriteFileOnce(temporary, pointer, checkoutRoot);
                AssertPath(Path.Combine(root, "index.json"), checkoutRoot);
                File.Move(temporary, Path.Combine(root, "index.json"), true);
                ReadStatus(root, expectedPlanDigest);
                return pointer;
            }
        }

        /// <summary>
        /// The plan's status at its open revision (file-only: no git, process, network, build or discovery): the effective
        /// partition's counts and origins, the proposal's, the results and the count of each reason.
        /// </summary>
        public static JsonObject ReadStatus(string evidenceRoot, string expectedPlanDigest)
        {
            var root = AssertPath(evidenceRoot);
            var plan = ReadStoredPlan(Path.Combine(root, "plan.json"), expectedPlanDigest);
            var pointer = ReadPointer(root, expectedPlanDigest);
            var path = AssertPath(Path.Combine(root, S(pointer, "path")));
            var revision = (JsonObject)Node(RcAdmission.ReadJson(path, S(pointer, "sha256")));
            AssertKeys(revision, "format", "planDigest", "sequence", "state", "results");
            if (S(revision, "format") != "antiphon-rc-plan-revision-v1" || S(revision, "planDigest") != expectedPlanDigest || Integer(revision, "sequence") != Integer(pointer, "sequence")
                || S(revision, "state") != "open" || !(F(revision, "results") is JsonArray results)) throw Refuse("revision-not-open-or-bound");
            var status = PartitionStatus(F(plan, "partition"), results);
            status["format"] = "antiphon-rc-plan-status-v1";
            status["planDigest"] = expectedPlanDigest;
            status["sequence"] = Integer(pointer, "sequence").Value;
            status["candidateSha"] = S(plan, "candidateSha");
            status["platform"] = S(plan, "platform");
            status["effectiveMode"] = S(plan, "effectiveMode");
            status["coverageScope"] = S(plan, "coverageScope");
            status["coverageComplete"] = false;
            status["gateCredit"] = false;
            status["forcedReasons"] = F(plan, "forcedReasons")?.DeepClone();
            status["proposed"] = PartitionStatus(F(plan, "proposalPartition"), new JsonArray());
            status["results"] = results.DeepClone();
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in A(F(plan, "partition"), "required") ?? new JsonArray())
                foreach (var reason in Strings(A(row, "reasons")))
                {
                    counts.TryGetValue(reason ?? "", out var n);
                    counts[reason ?? ""] = n + 1;
                }
            status["reasonCounts"] = new JsonObject(counts.Select(p => KeyValuePair.Create(p.Key, (JsonNode)p.Value)));
            return status;
        }
    }
}
