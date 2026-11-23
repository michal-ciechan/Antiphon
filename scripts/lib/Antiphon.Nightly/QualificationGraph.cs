// CARD-1514 S1: the qualification run graph's inert parser and identity. No admission, execution or release credit.
// It replaced scripts/lib/qualification-graph.ps1 (its embedded QualificationGraphJson, Read-QualificationGraph and the
// helper functions) and its driver scripts/test-qualification-graph.ps1, both deleted 2026-11-23 in unit 9 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; QualificationGraphTests holds the driver's cases. There is
// no command: nothing outside the tests reads a graph yet.
//
// - QualificationGraphJson.Decode refuses duplicate keys and non-finite numbers with the member's JSON pointer
//   (QualificationGraphRawRefusal); Canonical writes keys in ordinal order, the sets (nodes, targets, edges,
//   resourceKeys) in canonical order and numbers as exact decimals (an underflow never becomes zero); UidSetDigest is the
//   SHA-256 of the canonical text of the ordinal-sorted UID array.
// - QualificationGraph.Read checks the raw text, then the checked-in schema (QualificationGraphSchema), then the pinned
//   catalog identity. The categories and reasons are the PowerShell's: raw (invalid-json, duplicate-key, nonfinite),
//   schema (schema-invalid, with the violated pointers), identity (catalog-required, catalog-invalid,
//   catalog-pin-required, catalog-pin-mismatch, catalog-mismatch, duplicate-target, target-unregistered, duplicate-node,
//   node-unregistered, catalog-uid-mismatch). An accepted graph carries its canonical text, digest, node ids and
//   UID-set digests.
// Strings in, a record out: no file, process or network access. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>Raw text the graph reader refuses before any schema: duplicate-key or nonfinite at a JSON pointer.</summary>
    public sealed class QualificationGraphRawRefusal : Exception
    {
        /// <summary>A refusal with its reason and the pointer of the member that caused it.</summary>
        public QualificationGraphRawRefusal(string reason, string path) : base(reason + "|" + path)
        {
            Reason = reason;
            Path = path;
        }

        /// <summary>duplicate-key or nonfinite.</summary>
        public string Reason { get; }

        /// <summary>The JSON pointer of the duplicate member or the non-finite number.</summary>
        public string Path { get; }
    }

    /// <summary>Read-QualificationGraph's answer: a refusal (category, path, reason) or the accepted graph's identity.</summary>
    public sealed record QualificationGraphResult
    {
        /// <summary>True only when every raw, schema and catalog identity check passed.</summary>
        public bool Accepted { get; init; }

        /// <summary>raw, schema, identity or accepted.</summary>
        public string Category { get; init; } = "";

        /// <summary>The JSON pointer the refusal names ("" for a schema refusal, whose pointers are SchemaPaths).</summary>
        public string Path { get; init; } = "";

        /// <summary>The refusal reason; empty when accepted.</summary>
        public string Reason { get; init; } = "";

        /// <summary>A schema refusal's violated pointers (ordinal order); empty otherwise.</summary>
        public IReadOnlyList<string> SchemaPaths { get; init; } = Array.Empty<string>();

        /// <summary>The decoded graph when accepted; default otherwise.</summary>
        public JsonElement Graph { get; init; }

        /// <summary>The accepted graph's canonical text.</summary>
        public string CanonicalJson { get; init; } = "";

        /// <summary>SHA-256 (lowercase hex) of CanonicalJson's UTF-8 bytes.</summary>
        public string GraphDigest { get; init; } = "";

        /// <summary>The accepted graph's node ids in document order.</summary>
        public IReadOnlyList<string> NodeIds { get; init; } = Array.Empty<string>();

        /// <summary>Each node's selection.uidSetDigest in document order (null where the graph has null).</summary>
        public IReadOnlyList<string> UidSetDigests { get; init; } = Array.Empty<string>();

        internal static QualificationGraphResult Refuse(string category, string path, string reason) =>
            new() { Accepted = false, Category = category, Path = path, Reason = reason };
    }

    /// <summary>The S1 JSON identity: strict decoding, canonical text and the UID-set digest.</summary>
    public static class QualificationGraphJson
    {
        private static readonly Regex DatePattern = new(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The parsed text (depth at most 64), detached from its document. JsonException for text that is not JSON (NaN
        /// and Infinity included); QualificationGraphRawRefusal for a duplicate key or a number outside double's range.
        /// </summary>
        public static JsonElement Decode(string raw)
        {
            using var doc = JsonDocument.Parse(raw ?? "", new JsonDocumentOptions { MaxDepth = 64 });
            Inspect(doc.RootElement, "");
            return doc.RootElement.Clone();
        }

        private static void Inspect(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject())
                {
                    var child = path + "/" + Escape(p.Name);
                    if (!keys.Add(p.Name)) throw new QualificationGraphRawRefusal("duplicate-key", child);
                    Inspect(p.Value, child);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var v in e.EnumerateArray()) Inspect(v, path + "/" + i++);
            }
            else if (e.ValueKind == JsonValueKind.Number)
            {
                var number = e.GetDouble();
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new QualificationGraphRawRefusal("nonfinite", path);
            }
        }

        /// <summary>A JSON pointer token: ~ becomes ~0 and / becomes ~1.</summary>
        public static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

        /// <summary>
        /// The pointer of the first closedAtUtc or lastCompletedAtUtc string that is not an ISO-8601 instant with an
        /// offset (or not a real date, such as 2026-02-31); null when there is none. It supplements the schema, whose
        /// format keyword is an annotation.
        /// </summary>
        public static string InvalidDate(JsonElement e, string path = "")
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    var child = path + "/" + Escape(p.Name);
                    if ((p.Name == "closedAtUtc" || p.Name == "lastCompletedAtUtc") && p.Value.ValueKind == JsonValueKind.String)
                    {
                        var s = p.Value.GetString();
                        if (!DatePattern.IsMatch(s) || !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return child;
                    }
                    var invalid = InvalidDate(p.Value, child);
                    if (invalid != null) return invalid;
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var v in e.EnumerateArray())
                {
                    var invalid = InvalidDate(v, path + "/" + i++);
                    if (invalid != null) return invalid;
                }
            }
            return null;
        }

        /// <summary>ConvertTo-QualificationCanonicalJson: the canonical text of JSON text (Decode's refusals apply).</summary>
        public static string CanonicalJson(string json) => Canonical(Decode(json));

        /// <summary>
        /// The canonical text of a value: compact, keys in ordinal order, the arrays named nodes, targets, edges and
        /// resourceKeys in the ordinal order of their elements' canonical text, numbers as exact decimals, and text
        /// escaped only where JSON requires it. key is the member name the value sits under.
        /// </summary>
        public static string Canonical(JsonElement value, string key = "")
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                Write(writer, value, key);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static bool IsSet(string key) => key == "nodes" || key == "targets" || key == "edges" || key == "resourceKeys";

        private static void Write(Utf8JsonWriter w, JsonElement e, string key)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    w.WriteStartObject();
                    foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        w.WritePropertyName(p.Name);
                        Write(w, p.Value, p.Name);
                    }
                    w.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    w.WriteStartArray();
                    IEnumerable<JsonElement> values = e.EnumerateArray().ToArray();
                    if (IsSet(key)) values = values.OrderBy(v => Canonical(v), StringComparer.Ordinal);
                    foreach (var v in values) Write(w, v, "");
                    w.WriteEndArray();
                    break;
                case JsonValueKind.Number:
                    w.WriteRawValue(CanonicalNumber(e.GetRawText()));
                    break;
                default:
                    e.WriteTo(w);
                    break;
            }
        }

        /// <summary>
        /// Get-QualificationUidSetDigest: SHA-256 of the canonical text of the UIDs as a JSON array in ordinal order.
        /// InvalidDataException duplicate-uid when a UID repeats (ordinal).
        /// </summary>
        public static string UidSetDigest(IEnumerable<string> uids)
        {
            var sorted = (uids ?? Array.Empty<string>()).ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var uid in sorted)
                if (!seen.Add(uid)) throw new InvalidDataException("duplicate-uid");
            return TestPhaseRecords.Sha256Text(CanonicalJson(JsonSerializer.Serialize(sorted)));
        }

        /// <summary>A JSON number as sign, significant digits (no leading or trailing zeros; empty for zero) and exponent.</summary>
        private readonly struct ExactNumber
        {
            public ExactNumber(bool negative, string digits, BigInteger exponent)
            {
                Negative = negative;
                Digits = digits;
                Exponent = exponent;
            }

            public bool Negative { get; }
            public string Digits { get; }
            public BigInteger Exponent { get; }
            public bool IsZero => Digits.Length == 0;
            public int Sign => IsZero ? 0 : Negative ? -1 : 1;
        }

        // The decimal coefficient and exponent of JSON number text, without rounding through decimal or double: a finite
        // underflow must not become zero.
        private static ExactNumber Parse(string raw)
        {
            var negative = raw[0] == '-';
            var unsigned = negative ? raw.Substring(1) : raw;
            var e = unsigned.IndexOfAny(new[] { 'e', 'E' });
            var coefficient = e < 0 ? unsigned : unsigned.Substring(0, e);
            var exponent = e < 0 ? BigInteger.Zero : BigInteger.Parse(unsigned.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var point = coefficient.IndexOf('.');
            if (point >= 0)
            {
                exponent -= coefficient.Length - point - 1;
                coefficient = coefficient.Remove(point, 1);
            }
            coefficient = coefficient.TrimStart('0');
            if (coefficient.Length == 0) return new ExactNumber(negative, "", BigInteger.Zero);
            var significant = coefficient.Length;
            while (coefficient[significant - 1] == '0')
            {
                significant--;
                exponent++;
            }
            return new ExactNumber(negative, coefficient.Substring(0, significant), exponent);
        }

        // Plain digits for exponents from -20 to 20, else <digits>e<exponent>; zero (either sign) is 0.
        private static string CanonicalNumber(string raw)
        {
            var n = Parse(raw);
            if (n.IsZero) return "0";
            var coefficient = n.Digits;
            var exponent = n.Exponent;
            string value;
            if (exponent < -20 || exponent > 20)
                value = coefficient + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            else if (exponent >= 0)
                value = coefficient + new string('0', (int)exponent);
            else
            {
                var point = coefficient.Length + (int)exponent;
                value = point > 0 ? coefficient.Insert(point, ".") : "0." + new string('0', -point) + coefficient;
            }
            return n.Negative ? "-" + value : value;
        }

        /// <summary>True when JSON number text has no fractional part (1.0 and 1e22 are integers, 1e-9999 is not).</summary>
        internal static bool IsInteger(string raw)
        {
            var n = Parse(raw);
            return n.IsZero || n.Exponent >= 0;
        }

        /// <summary>The exact order of two JSON numbers' values (negative, zero or positive).</summary>
        internal static int Compare(string left, string right)
        {
            var a = Parse(left);
            var b = Parse(right);
            if (a.Sign != b.Sign) return a.Sign.CompareTo(b.Sign);
            if (a.Sign == 0) return 0;
            // Same sign: compare magnitudes by the position of the leading digit, then digit by digit.
            var magnitude = (a.Digits.Length + a.Exponent).CompareTo(b.Digits.Length + b.Exponent);
            if (magnitude == 0)
            {
                var width = Math.Max(a.Digits.Length, b.Digits.Length);
                magnitude = Math.Sign(string.CompareOrdinal(a.Digits.PadRight(width, '0'), b.Digits.PadRight(width, '0')));
            }
            return a.Sign * magnitude;
        }
    }

    /// <summary>Read-QualificationGraph: the S1 reader of a qualification run plan against its pinned catalog.</summary>
    public static class QualificationGraph
    {
        private static readonly Regex PinPattern = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
        private static readonly string[] RootFields = { "profile", "policyDigest", "rosterDigest", "candidate" };
        private static readonly string[] TargetFields = { "bindingDigest", "capability", "parallelism", "resourceKeys", "quota" };
        private static readonly string[] NodeFields =
        {
            "kind", "platformOfRecord", "coverageRole", "laneId", "project", "suite", "chunk", "canonicalOrdinal", "action", "requires",
            "disposition", "inputFingerprint", "skipReceipt",
        };
        private static readonly string[] SelectionFields = { "filter", "uidSetDigest", "expectedCount" };

        /// <summary>
        /// The graph text checked raw, against the schema, then against the catalog text whose SHA-256 (of its UTF-8
        /// bytes, lowercase hex) the controller pins as expectedCatalogHash. Every node and target must be registered in
        /// the catalog with identical identity fields (canonical comparison, numbers exact), and a node's UID-set digest
        /// and expected count must match the catalog's UID members for that node.
        /// </summary>
        public static QualificationGraphResult Read(string json, string catalogJson, QualificationGraphSchema schema, string expectedCatalogHash)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            JsonElement graph;
            try { graph = QualificationGraphJson.Decode(json); }
            catch (QualificationGraphRawRefusal refusal) { return QualificationGraphResult.Refuse("raw", refusal.Path, refusal.Reason); }
            catch (JsonException) { return QualificationGraphResult.Refuse("raw", "", "invalid-json"); }
            catch (ArgumentException) { return QualificationGraphResult.Refuse("raw", "", "invalid-json"); }

            var schemaResult = schema.Graph(graph);
            if (!schemaResult.Accepted)
                return QualificationGraphResult.Refuse("schema", "", "schema-invalid") with { SchemaPaths = schemaResult.Paths };
            if (string.IsNullOrWhiteSpace(catalogJson)) return Identity("/catalog", "catalog-required");
            JsonElement catalog;
            try { catalog = QualificationGraphJson.Decode(catalogJson); }
            catch (QualificationGraphRawRefusal) { return Identity("/catalog", "catalog-invalid"); }
            catch (JsonException) { return Identity("/catalog", "catalog-invalid"); }
            catch (ArgumentException) { return Identity("/catalog", "catalog-invalid"); }
            if (!schema.CatalogAccepts(catalog)) return Identity("/catalog", "catalog-invalid");
            if (expectedCatalogHash == null || !PinPattern.IsMatch(expectedCatalogHash)) return Identity("/catalog", "catalog-pin-required");
            if (!string.Equals(TestPhaseRecords.Sha256Text(catalogJson), expectedCatalogHash, StringComparison.Ordinal))
                return Identity("/catalog", "catalog-pin-mismatch");

            foreach (var field in RootFields)
                if (!SameField(graph, catalog, field)) return Identity("/" + field, "catalog-mismatch");

            var targetIds = new HashSet<string>(StringComparer.Ordinal);
            var targets = graph.GetProperty("targets").EnumerateArray().ToArray();
            var catalogTargets = catalog.GetProperty("targets").EnumerateArray().ToArray();
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                var id = target.GetProperty("id").GetString();
                if (!targetIds.Add(id)) return Identity("/targets/" + i + "/id", "duplicate-target");
                var known = Registered(catalogTargets, id);
                if (known.Length != 1) return Identity("/targets/" + i + "/id", "target-unregistered");
                foreach (var field in TargetFields)
                    if (!SameField(target, known[0], field)) return Identity("/targets/" + i + "/" + field, "catalog-mismatch");
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToArray();
            var catalogNodes = catalog.GetProperty("nodes").EnumerateArray().ToArray();
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                var at = "/nodes/" + i;
                var id = node.GetProperty("id").GetString();
                if (!nodeIds.Add(id)) return Identity(at + "/id", "duplicate-node");
                var known = Registered(catalogNodes, id);
                if (known.Length != 1) return Identity(at + "/id", "node-unregistered");
                foreach (var field in NodeFields)
                    if (!SameField(node, known[0], field)) return Identity(at + "/" + field, "catalog-mismatch");
                var selection = node.GetProperty("selection");
                var knownSelection = known[0].GetProperty("selection");
                foreach (var field in SelectionFields)
                    if (!SameField(selection, knownSelection, field)) return Identity(at + "/selection/" + field, "catalog-mismatch");
                if (!targetIds.Contains(node.GetProperty("targetId").GetString())) return Identity(at + "/targetId", "target-unregistered");
                var digest = selection.GetProperty("uidSetDigest");
                if (digest.ValueKind == JsonValueKind.Null) continue;
                if (!catalog.GetProperty("uids").TryGetProperty(id, out var uids)) return Identity(at + "/selection", "catalog-uid-mismatch");
                var members = uids.EnumerateArray().Select(u => u.GetString()).ToArray();
                var count = QualificationGraphJson.CanonicalJson(members.Length.ToString(CultureInfo.InvariantCulture));
                if (!string.Equals(QualificationGraphJson.UidSetDigest(members), digest.GetString(), StringComparison.Ordinal) ||
                    !string.Equals(count, FieldIdentity(selection, "expectedCount"), StringComparison.Ordinal))
                    return Identity(at + "/selection", "catalog-uid-mismatch");
            }

            var canonical = QualificationGraphJson.Canonical(graph);
            return new QualificationGraphResult
            {
                Accepted = true,
                Category = "accepted",
                Graph = graph,
                CanonicalJson = canonical,
                GraphDigest = TestPhaseRecords.Sha256Text(canonical),
                NodeIds = nodes.Select(n => n.GetProperty("id").GetString()).ToArray(),
                UidSetDigests = nodes.Select(n => n.GetProperty("selection").GetProperty("uidSetDigest"))
                    .Select(d => d.ValueKind == JsonValueKind.Null ? null : d.GetString()).ToArray(),
            };
        }

        private static QualificationGraphResult Identity(string path, string reason) => QualificationGraphResult.Refuse("identity", path, reason);

        private static JsonElement[] Registered(JsonElement[] catalogEntries, string id) =>
            catalogEntries.Where(e => string.Equals(e.GetProperty("id").GetString(), id, StringComparison.Ordinal)).ToArray();

        // Get-QualificationFieldIdentity: the decoded subtrees are compared as canonical text, never through numbers.
        private static string FieldIdentity(JsonElement value, string field) => QualificationGraphJson.Canonical(value.GetProperty(field), field);

        private static bool SameField(JsonElement left, JsonElement right, string field) =>
            string.Equals(FieldIdentity(left, field), FieldIdentity(right, field), StringComparison.Ordinal);
    }
}
