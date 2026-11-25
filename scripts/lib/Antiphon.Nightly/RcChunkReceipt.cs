// CARD-1836 S1: the v2 RC chunk execution receipt, its strict admission predicate and the qualification comparison
// baseline (plan D-1, D-2, D-3.1). Diagnostic only: nothing here changes what a Full run executes. A row carries the
// execution commit (sha) and a content digest over the chunk's expanded roster, never over its label or ordinal; a
// carried row keeps its origin execution sha; a missing or never-launched row has a null sha and is never reusable. The
// sealed inventory is written by atomic create/rename and a duplicate or torn inventory grants no credit. Plain C#,
// ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    /// <summary>One expanded member of a chunk roster: project/assembly identity, fully qualified class, method, stable case key.</summary>
    public sealed class RcCaseIdentity
    {
        public RcCaseIdentity(string assembly, string className, string method, string caseKey)
        {
            Assembly = assembly ?? "";
            Class = className ?? "";
            Method = method ?? "";
            CaseKey = caseKey ?? "";
        }

        public string Assembly { get; }
        public string Class { get; }
        public string Method { get; }
        public string CaseKey { get; }
    }

    /// <summary>One TRX case outcome: the case key (test id), the class and method, the TRX outcome.</summary>
    public sealed class RcTrxCase
    {
        public string CaseKey { get; set; } = "";
        public string Class { get; set; } = "";
        public string Method { get; set; } = "";
        public string Outcome { get; set; } = "";
    }

    /// <summary>A strict admission answer: Reusable, else the reasons the chunk is owed.</summary>
    public sealed class RcReuse
    {
        public bool Reusable { get; set; }
        public string[] Reasons { get; set; } = Array.Empty<string>();
    }

    /// <summary>A sealed inventory read back: its rows, or the refusal that grants no credit.</summary>
    public sealed class RcReceiptInventory
    {
        public string Refusal { get; set; } = "";
        public string Sha { get; set; } = "";
        public string Digest { get; set; } = "";
        public IDictionary[] Rows { get; set; } = Array.Empty<IDictionary>();
    }

    /// <summary>One planned chunk's decision against prior receipts: execute (with reasons) or carry (with its origin row).</summary>
    public sealed class RcChunkObligation
    {
        public string Chunk { get; set; } = "";
        public string ContentDigest { get; set; } = "";
        public string Decision { get; set; } = "execute";
        public string[] Reasons { get; set; } = Array.Empty<string>();
        public IDictionary Origin { get; set; }
    }

    public static class RcChunkReceipt
    {
        public const string Format = "antiphon-rc-chunk-execution-v2";
        public const string InventoryFormat = "antiphon-rc-chunk-inventory-v2";
        public const string ContentFormat = "antiphon-rc-chunk-content-v1";
        public const int Version = 2;

        static readonly Regex Sha40 = new Regex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);
        static readonly Regex Hex64 = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

        /// <summary>True for a full lowercase 40-hex commit.</summary>
        public static bool IsSha(string value) => value != null && Sha40.IsMatch(value);

        /// <summary>True for a lowercase SHA-256 hex digest.</summary>
        public static bool IsDigest(string value) => value != null && Hex64.IsMatch(value);

        // ---- content digest v1 -----------------------------------------------------------------------------------

        /// <summary>
        /// Content digest v1: SHA-256 of the length-prefixed UTF-8 (RcBoundary.AddString) domain tag, suite, execution kind,
        /// u32 count and each member's assembly, class, method and case key in ordinal order. Labels, ordinals, run ids and
        /// paths are not part of it. An empty roster, an empty field or a duplicate identity is unproven: null.
        /// </summary>
        public static string ContentDigest(string suite, string executionKind, IEnumerable<RcCaseIdentity> roster)
        {
            var members = (roster ?? Enumerable.Empty<RcCaseIdentity>()).ToArray();
            if (members.Length == 0 || string.IsNullOrEmpty(suite)) return null;
            if (members.Any(m => m == null || m.Assembly.Length == 0 || m.Class.Length == 0 || m.Method.Length == 0 || m.CaseKey.Length == 0)) return null;
            var keys = members.Select(m => Key(m)).ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) return null;
            var order = Enumerable.Range(0, members.Length).ToArray();
            Array.Sort(keys, order, StringComparer.Ordinal);
            var encoder = new UTF8Encoding(false, true);
            using var buffer = new MemoryStream();
            try
            {
                RcBoundary.AddString(buffer, ContentFormat, encoder);
                RcBoundary.AddString(buffer, suite, encoder);
                RcBoundary.AddString(buffer, executionKind ?? "", encoder);
                RcBoundary.AddU32(buffer, (uint)members.Length);
                foreach (var i in order)
                {
                    RcBoundary.AddString(buffer, members[i].Assembly, encoder);
                    RcBoundary.AddString(buffer, members[i].Class, encoder);
                    RcBoundary.AddString(buffer, members[i].Method, encoder);
                    RcBoundary.AddString(buffer, members[i].CaseKey, encoder);
                }
            }
            catch (EncoderFallbackException)
            {
                return null;
            }
            return Hex(SHA256.HashData(buffer.ToArray()));
        }

        static string Key(RcCaseIdentity m) => Esc(m.Assembly) + "\0\0" + Esc(m.Class) + "\0\0" + Esc(m.Method) + "\0\0" + Esc(m.CaseKey);

        static string Esc(string field) => (field ?? "").Replace("\0", "\0\u0001");

        /// <summary>The planned roster of a native chunk from its discovery nodes (uid, type, method, namespace), restricted to its classes when it declares any.</summary>
        public static RcCaseIdentity[] RosterFromNodes(string assembly, object nodes, string[] classes)
        {
            var wanted = new HashSet<string>(classes ?? Array.Empty<string>(), StringComparer.Ordinal);
            var roster = new List<RcCaseIdentity>();
            foreach (var node in PsValue.AsArray(nodes))
            {
                if (node == null) continue;
                var type = PsValue.Str(PsValue.Prop(node, "type"));
                var ns = PsValue.Str(PsValue.Prop(node, "namespace"));
                var full = string.IsNullOrEmpty(ns) ? type : ns + "." + type;
                if (wanted.Count > 0 && !wanted.Contains(type) && !wanted.Contains(full)) continue;
                roster.Add(new RcCaseIdentity(assembly, full, PsValue.Str(PsValue.Prop(node, "method")), PsValue.Str(PsValue.Prop(node, "uid"))));
            }
            return roster.ToArray();
        }

        // ---- rows ------------------------------------------------------------------------------------------------

        /// <summary>Stamps an observed (executed) row: execution sha (lowercase 40-hex or null) and content digest (or null when unproven).</summary>
        public static void Stamp(IDictionary row, string executionSha, string contentDigest)
        {
            var sha = (executionSha ?? "").Trim().ToLowerInvariant();
            row["sha"] = IsSha(sha) ? sha : null;
            row["contentDigest"] = IsDigest(contentDigest) ? contentDigest : null;
            row["disposition"] = "executed";
        }

        /// <summary>A scheduled chunk with no observed row: disposition missing, null execution sha, its planned content where known.</summary>
        public static OrderedDictionary Missing(string suite, string chunk, string plannedContentDigest, string candidateSha) => new OrderedDictionary(StringComparer.Ordinal)
        {
            { "id", suite ?? "" }, { "chunk", chunk ?? "" }, { "result", "MISSING" }, { "skipped", false }, { "disposition", "missing" }, { "sha", null },
            { "candidateSha", candidateSha ?? "" }, { "contentDigest", IsDigest(plannedContentDigest) ? plannedContentDigest : null },
        };

        /// <summary>
        /// A clean origin receipt carried onto a candidate: the row keeps its origin execution sha, run and receipt digest and
        /// records the candidate that validated it separately (summary sha = candidate, row sha = origin).
        /// </summary>
        public static OrderedDictionary Carry(IDictionary origin, string candidateSha, string originReceiptDigest)
        {
            var row = new OrderedDictionary(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in origin) row[entry.Key] = entry.Value;
            row["disposition"] = "carried";
            row["sha"] = candidateSha ?? "";
            row["candidateSha"] = candidateSha ?? "";
            row["originReceiptDigest"] = originReceiptDigest ?? "";
            return row;
        }

        // ---- strict admission ------------------------------------------------------------------------------------

        /// <summary>
        /// D-3.1: a reusable pass is an executed row with an execution sha and content digest, result pass, skipped false, exit
        /// 0, no timeout or tolerated exit, empty evidenceReasons, and a TRX with a positive executed count, zero failed and
        /// zero skipped/NotExecuted cases, and every required case present and passed. Anything else owes execution.
        /// </summary>
        public static RcReuse Reusable(IDictionary row, IReadOnlyList<RcTrxCase> trx, IEnumerable<string> requiredCases)
        {
            var reasons = new List<string>();
            // RED WITNESS (naive): a pass label and exit 0 admit.
            if (row != null && PsValue.Str(row["result"]) == "pass" && PsOps.ToInt(row["exitCode"]) == 0) return new RcReuse { Reusable = true };
            if (row == null) return new RcReuse { Reasons = new[] { "missing-row" } };
            var disposition = PsValue.Str(row["disposition"]);
            if (disposition == "missing") reasons.Add("missing-row");
            else if (disposition.Length == 0) reasons.Add("legacy-row");
            else if (disposition != "executed" && disposition != "carried") reasons.Add("disposition:" + disposition);
            if (!IsSha(PsValue.Str(row["sha"]))) reasons.Add("no-execution-sha");
            if (!IsDigest(PsValue.Str(row["contentDigest"]))) reasons.Add("no-content-digest");
            var result = PsValue.Str(row["result"]);
            if (!string.Equals(result, "pass", StringComparison.Ordinal)) reasons.Add("result:" + result);
            if (row.Contains("skipped") && PsValue.IsTrue(row["skipped"])) reasons.Add("row-skipped");
            if (!row.Contains("exitCode") || PsOps.ToInt(row["exitCode"]) != 0) reasons.Add("exit:" + PsValue.Str(row.Contains("exitCode") ? row["exitCode"] : "absent"));
            if (row.Contains("timedOut") && PsValue.IsTrue(row["timedOut"])) reasons.Add("timed-out");
            if (row.Contains("processExitTolerated") && PsValue.IsTrue(row["processExitTolerated"])) reasons.Add("tolerated-exit");
            if (row.Contains("evidenceReasons") && PsValue.AsArray(row["evidenceReasons"]).Any(r => !string.IsNullOrWhiteSpace(PsValue.Str(r))))
                reasons.Add("evidence-reasons");
            if (trx == null) reasons.Add("no-trx");
            else
            {
                if (trx.Count == 0) reasons.Add("zero-executed");
                var failed = trx.Count(c => c.Outcome == "Failed" || c.Outcome == "Error" || c.Outcome == "Timeout" || c.Outcome == "Aborted");
                var skipped = trx.Count(c => c.Outcome == "NotExecuted" || c.Outcome == "Skipped" || c.Outcome == "Inconclusive" || c.Outcome == "Pending");
                var other = trx.Count(c => c.Outcome != "Passed") - failed - skipped;
                if (failed > 0) reasons.Add("failed-cases:" + failed.ToString(CultureInfo.InvariantCulture));
                if (skipped > 0) reasons.Add("skipped-cases:" + skipped.ToString(CultureInfo.InvariantCulture));
                if (other > 0) reasons.Add("other-cases:" + other.ToString(CultureInfo.InvariantCulture));
                var passed = new HashSet<string>(trx.Where(c => c.Outcome == "Passed").Select(c => c.CaseKey), StringComparer.Ordinal);
                foreach (var required in requiredCases ?? Enumerable.Empty<string>())
                    if (!passed.Contains(required)) reasons.Add("case-missing:" + required);
            }
            return new RcReuse { Reusable = reasons.Count == 0, Reasons = reasons.ToArray() };
        }

        /// <summary>The per-case outcomes of a TRX (UnitTestResult testId, outcome; the class and method from its TestMethod); null when absent or unreadable.</summary>
        public static RcTrxCase[] ReadTrx(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(reader);
                var methods = new Dictionary<string, (string Class, string Method)>(StringComparer.Ordinal);
                foreach (XmlElement test in doc.GetElementsByTagName("UnitTest"))
                {
                    var method = test.GetElementsByTagName("TestMethod").OfType<XmlElement>().FirstOrDefault();
                    methods[test.GetAttribute("id")] = (method?.GetAttribute("className") ?? "", method?.GetAttribute("name") ?? "");
                }
                var cases = new List<RcTrxCase>();
                foreach (XmlElement result in doc.GetElementsByTagName("UnitTestResult"))
                {
                    var id = result.GetAttribute("testId");
                    methods.TryGetValue(id, out var m);
                    cases.Add(new RcTrxCase { CaseKey = id, Class = m.Class ?? "", Method = m.Method ?? "", Outcome = result.GetAttribute("outcome") });
                }
                return cases.ToArray();
            }
            catch (XmlException)
            {
                return null;
            }
        }

        // ---- matching against prior receipts ---------------------------------------------------------------------

        /// <summary>
        /// Joins planned chunks (id, content digest) to prior receipts by content digest, never by chunk id. A chunk is carried
        /// only from a strictly reusable receipt for exactly its contents; an unknown planned digest, no receipt, a non-reusable
        /// receipt or several receipts for one content owe execution.
        /// </summary>
        public static RcChunkObligation[] Match(IEnumerable<(string Chunk, string ContentDigest)> planned, IReadOnlyList<IDictionary> priorRows,
            Func<IDictionary, RcReuse> admit)
        {
            // RED WITNESS (naive): matched by chunk id.
            var byContent = (priorRows ?? Array.Empty<IDictionary>()).Where(r => r != null)
                .GroupBy(r => PsValue.Str(r["chunk"]), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var answer = new List<RcChunkObligation>();
            foreach (var (chunk, digest) in planned)
            {
                var obligation = new RcChunkObligation { Chunk = chunk ?? "", ContentDigest = digest ?? "" };
                if (!IsDigest(digest)) obligation.Reasons = new[] { "content-unproven" };
                else if (!byContent.TryGetValue(chunk ?? "", out var rows)) obligation.Reasons = new[] { "no-receipt-for-content" };
                else if (rows.Length > 1) obligation.Reasons = new[] { "duplicate-receipt-for-content" };
                else
                {
                    var reuse = admit(rows[0]);
                    if (reuse.Reusable)
                    {
                        obligation.Decision = "carry";
                        obligation.Origin = rows[0];
                    }
                    else obligation.Reasons = reuse.Reasons;
                }
                answer.Add(obligation);
            }
            return answer.ToArray();
        }

        // ---- sealed inventory ------------------------------------------------------------------------------------

        /// <summary>
        /// Seals the inventory at path by atomic create/rename: { format, chunkReceiptVersion, sha, attemptRevision, rows }.
        /// An identical existing seal is reused; a different one is a conflict (no write). Returns the byte digest.
        /// </summary>
        public static string Seal(string path, string candidateSha, int attemptRevision, IReadOnlyList<IDictionary> rows)
        {
            var document = new OrderedDictionary(StringComparer.Ordinal)
            {
                { "format", InventoryFormat }, { "chunkReceiptVersion", Version }, { "sha", candidateSha ?? "" }, { "attemptRevision", attemptRevision },
                { "rows", rows.Select(r => (object)Plain(r)).ToArray() },
            };
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(document));
            var digest = Hex(SHA256.HashData(bytes));
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.AsSpan().SequenceEqual(bytes)) return digest;
                throw new NightlyRefusal("inventory-conflict");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            try { File.Move(temp, path, false); }
            catch (IOException)
            {
                File.Delete(temp);
                throw new NightlyRefusal("inventory-conflict");
            }
            return digest;
        }

        /// <summary>
        /// Reads a sealed inventory strictly: an unreadable or truncated document, a wrong format or version, an expected
        /// digest that differs, or two rows for one content refuse with no rows.
        /// </summary>
        public static RcReceiptInventory Read(string path, string expectedDigest = null)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return new RcReceiptInventory { Refusal = "inventory-unreadable" }; }
            var digest = Hex(SHA256.HashData(bytes));
            if (!string.IsNullOrEmpty(expectedDigest) && !string.Equals(expectedDigest, digest, StringComparison.Ordinal))
                return new RcReceiptInventory { Refusal = "inventory-digest-mismatch", Digest = digest };
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                root = doc.RootElement.Clone();
            }
            catch (JsonException) { return new RcReceiptInventory { Refusal = "inventory-torn", Digest = digest }; }
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
                || format.GetString() != InventoryFormat || !root.TryGetProperty("chunkReceiptVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != Version || !root.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
                return new RcReceiptInventory { Refusal = "inventory-format", Digest = digest };
            var rows = rowsElement.EnumerateArray().Select(e => (IDictionary)FromJson(e)).ToArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var content = PsValue.Str(row["contentDigest"]);
                if (content.Length == 0) continue;
                if (!seen.Add(content)) return new RcReceiptInventory { Refusal = "inventory-duplicate-content", Digest = digest };
            }
            return new RcReceiptInventory { Sha = root.TryGetProperty("sha", out var sha) ? sha.GetString() ?? "" : "", Digest = digest, Rows = rows };
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        internal static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

        /// <summary>The SHA-256 of a file's bytes, lowercase hex.</summary>
        public static string FileDigest(string path) => Hex(SHA256.HashData(File.ReadAllBytes(path)));

        static object Plain(object value)
        {
            if (value is IDictionary map)
            {
                var copy = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in map) copy[PsValue.Str(entry.Key)] = Plain(entry.Value);
                return copy;
            }
            if (value is string || value == null) return value;
            if (value is IEnumerable items) return items.Cast<object>().Select(Plain).ToArray();
            return value;
        }

        internal static object FromJson(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new OrderedDictionary(StringComparer.Ordinal);
                    foreach (var p in e.EnumerateObject()) map[p.Name] = FromJson(p.Value);
                    return map;
                case JsonValueKind.Array: return e.EnumerateArray().Select(FromJson).ToArray();
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number: return e.TryGetInt64(out var l) ? (object)l : e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
    }

    /// <summary>One prior comparison input for D-1: a published release or a sealed RC attempt, with its chunk rows.</summary>
    public sealed class RcComparisonInput
    {
        /// <summary>release or rc-attempt.</summary>
        public string Kind { get; set; } = "";
        public string Line { get; set; } = "";
        public string Sha { get; set; } = "";
        public string RunId { get; set; } = "";
        public string Attempt { get; set; } = "";
        public DateTimeOffset CompletedUtc { get; set; }
        public string ReceiptPath { get; set; } = "";
        public string ReceiptDigest { get; set; } = "";
        /// <summary>The attempt's inventory was sealed (a release is sealed by publication).</summary>
        public bool Sealed { get; set; }
        /// <summary>The sha is an ancestor of (or equal to) the candidate.</summary>
        public bool Ancestor { get; set; }
        /// <summary>The ref sha the receipt recorded and the ref sha observed now (empty: not recorded).</summary>
        public string RecordedRefSha { get; set; } = "";
        public string ObservedRefSha { get; set; } = "";
        public IDictionary[] Rows { get; set; } = Array.Empty<IDictionary>();
    }

    /// <summary>The D-1 choice: Incremental against one recorded baseline, or Full with the reasons; plus every open obligation by content.</summary>
    public sealed class RcComparisonChoice
    {
        public string Mode { get; set; } = "Full";
        public string Kind { get; set; } = "";
        public string Sha { get; set; } = "";
        public string RunId { get; set; } = "";
        public string ReceiptPath { get; set; } = "";
        public string ReceiptDigest { get; set; } = "";
        public string[] Reasons { get; set; } = Array.Empty<string>();
        public Dictionary<string, string> Owed { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class RcComparisonBaseline
    {
        /// <summary>
        /// D-1: an explicitly recorded eligible release, otherwise the most recent eligible ancestor release, otherwise the
        /// last sealed RC attempt on the same line. A moved ref or two receipts for one run/attempt select Full. Every
        /// non-clean row of an attempt completed after the chosen baseline stays owed by content (an older pass never erases
        /// a later FAIL, interruption or missing row).
        /// </summary>
        public static RcComparisonChoice Choose(string line, IReadOnlyList<RcComparisonInput> inputs, string explicitReleaseSha = null)
        {
            var all = (inputs ?? Array.Empty<RcComparisonInput>()).Where(i => i != null).ToArray();
            // RED WITNESS (naive): the newest input is the baseline and nothing after it stays owed.
            var newest = all.OrderByDescending(i => i.CompletedUtc).FirstOrDefault();
            if (newest != null) return new RcComparisonChoice { Mode = "Incremental", Kind = newest.Kind, Sha = newest.Sha, RunId = newest.RunId, ReceiptPath = newest.ReceiptPath, ReceiptDigest = newest.ReceiptDigest };
            var reasons = new List<string>();
            foreach (var input in all)
                if (input.RecordedRefSha.Length > 0 && !string.Equals(input.RecordedRefSha, input.ObservedRefSha, StringComparison.Ordinal))
                    reasons.Add("moved-ref:" + input.RunId);
            foreach (var group in all.GroupBy(i => i.Kind + "|" + i.RunId + "|" + i.Attempt, StringComparer.Ordinal))
                if (group.Select(i => i.ReceiptDigest).Distinct(StringComparer.Ordinal).Count() > 1)
                    reasons.Add("conflicting-receipts:" + group.First().RunId);
            if (reasons.Count > 0) return new RcComparisonChoice { Reasons = reasons.ToArray() };

            var eligible = all.Where(i => i.Ancestor && i.Sealed && RcChunkReceipt.IsSha(i.Sha)).ToArray();
            RcComparisonInput chosen = null;
            if (!string.IsNullOrEmpty(explicitReleaseSha))
            {
                chosen = eligible.FirstOrDefault(i => i.Kind == "release" && i.Sha == explicitReleaseSha);
                if (chosen == null) return new RcComparisonChoice { Reasons = new[] { "explicit-baseline-ineligible" } };
            }
            chosen ??= eligible.Where(i => i.Kind == "release").OrderByDescending(i => i.CompletedUtc).FirstOrDefault();
            chosen ??= eligible.Where(i => i.Kind == "rc-attempt" && string.Equals(i.Line, line, StringComparison.Ordinal))
                .OrderByDescending(i => i.CompletedUtc).ThenByDescending(i => i.RunId, StringComparer.Ordinal).FirstOrDefault();
            if (chosen == null) return new RcComparisonChoice { Reasons = new[] { "baseline-absent" } };

            var choice = new RcComparisonChoice
            {
                Mode = "Incremental", Kind = chosen.Kind, Sha = chosen.Sha, RunId = chosen.RunId, ReceiptPath = chosen.ReceiptPath, ReceiptDigest = chosen.ReceiptDigest,
            };
            // Every attempt completed after the baseline (partial ones too): its latest row per content decides.
            var latest = new Dictionary<string, (DateTimeOffset At, IDictionary Row, string Run)>(StringComparer.Ordinal);
            foreach (var input in all.Where(i => i.Ancestor && i.CompletedUtc > chosen.CompletedUtc).OrderBy(i => i.CompletedUtc))
                foreach (var row in input.Rows)
                {
                    var content = PsValue.Str(row["contentDigest"]);
                    if (content.Length == 0) continue;
                    latest[content] = (input.CompletedUtc, row, input.RunId);
                }
            foreach (var pair in latest)
            {
                var row = pair.Value.Row;
                var clean = PsValue.Str(row["result"]) == "pass" && PsValue.Str(row["disposition"]) == "executed" && !PsValue.IsTrue(row["skipped"]);
                if (!clean) choice.Owed[pair.Key] = "later-open:" + pair.Value.Run + ":" + PsValue.Str(row["result"]);
            }
            return choice;
        }
    }
}
