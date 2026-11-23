// CARD-1514 S1: the qualification run plan's schema check, docs/schemas/qualification-run-plan-v1.schema.json. It
// replaced the Test-Json (draft 2020-12) calls of scripts/lib/qualification-graph.ps1 (Get-QualificationGraphSchemaResult,
// Test-QualificationGraphSchema, Test-QualificationCatalogSchema and the parsing of Test-Json's error messages into
// pointers), deleted 2026-11-23 in unit 9 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md.
//
// The checked-in schema stays the authority. QualificationSchemaEvaluator evaluates the draft 2020-12 subset that schema
// uses: $schema, $id, $defs, $ref (#/$defs/<name>), type, const, enum, pattern (ECMAScript), minLength, maxLength,
// minimum, minItems, items, uniqueItems, required, properties, additionalProperties, anyOf, and format as an annotation.
// Any other keyword, another draft or an unresolved $ref throws QualificationSchemaUnsupported when the schema is parsed,
// so a schema edit outside the subset fails closed instead of being ignored. Numbers are compared exactly (an integer
// is a number without a fractional part; JSON values are equal by value, as JSON Schema defines). Each violation
// reports the instance pointer it is about: the member a required or additionalProperties check names, else the value
// that fails (an anyOf reports its own value, not its branches). QualificationGraphJson.InvalidDate supplements the
// annotation-only date-time format, as the PowerShell did. The catalog schema is the graph schema's own definitions
// under a closed root (profile, policyDigest, rosterDigest, candidate, targets, nodes and the uids map), so a catalog
// cannot carry graph-only fields. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>A schema the evaluator does not support (a keyword, draft or $ref outside its subset): the schema is wrong, not the document.</summary>
    public sealed class QualificationSchemaUnsupported : Exception
    {
        /// <summary>schema-unsupported:&lt;what&gt; at &lt;schema pointer&gt;.</summary>
        public QualificationSchemaUnsupported(string what, string at) : base("schema-unsupported:" + what + " at " + at) { }
    }

    /// <summary>Get-QualificationGraphSchemaResult: accepted, or the pointers of every violation (ordinal order).</summary>
    public sealed record QualificationSchemaResult(bool Accepted, IReadOnlyList<string> Paths);

    /// <summary>The qualification run plan schema and the catalog schema derived from it.</summary>
    public sealed class QualificationGraphSchema
    {
        /// <summary>The checked-in schema, relative to the repository root.</summary>
        public const string RelativePath = "docs/schemas/qualification-run-plan-v1.schema.json";

        private static readonly string[] CatalogFields = { "profile", "policyDigest", "rosterDigest", "candidate", "targets", "nodes", "uids" };

        private readonly QualificationSchemaEvaluator _graph;
        private readonly QualificationSchemaEvaluator _catalog;

        private QualificationGraphSchema(QualificationSchemaEvaluator graph, QualificationSchemaEvaluator catalog)
        {
            _graph = graph;
            _catalog = catalog;
        }

        /// <summary>The schema text (QualificationGraphJson.Decode's refusals apply; QualificationSchemaUnsupported outside the subset).</summary>
        public static QualificationGraphSchema Parse(string schemaJson)
        {
            var root = QualificationGraphJson.Decode(schemaJson);
            var graph = new QualificationSchemaEvaluator(root);
            return new QualificationGraphSchema(graph, new QualificationSchemaEvaluator(CatalogSchema(root)));
        }

        /// <summary>The graph against the schema, then (when the schema accepts it) the explicit date check.</summary>
        public QualificationSchemaResult Graph(JsonElement graph)
        {
            var paths = _graph.Violations(graph);
            if (paths.Count == 0)
            {
                var date = QualificationGraphJson.InvalidDate(graph);
                if (date != null) paths = new[] { date };
            }
            return new QualificationSchemaResult(paths.Count == 0, paths);
        }

        /// <summary>Test-QualificationCatalogSchema: the catalog against the closed catalog schema.</summary>
        public bool CatalogAccepts(JsonElement catalog) => _catalog.Violations(catalog).Count == 0;

        // The graph schema's exact property and $defs definitions under a closed catalog root; uids maps a node id to a
        // set of non-empty UID strings.
        private static JsonElement CatalogSchema(JsonElement source)
        {
            if (!source.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                throw new QualificationSchemaUnsupported("catalog-properties", "#");
            if (!source.TryGetProperty("$defs", out var defs)) throw new QualificationSchemaUnsupported("catalog-defs", "#");
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WritePropertyName("$schema");
                source.GetProperty("$schema").WriteTo(w);
                w.WriteString("type", "object");
                w.WriteBoolean("additionalProperties", false);
                w.WriteStartArray("required");
                foreach (var field in CatalogFields) w.WriteStringValue(field);
                w.WriteEndArray();
                w.WriteStartObject("properties");
                foreach (var field in CatalogFields)
                {
                    w.WritePropertyName(field);
                    if (field == "uids")
                    {
                        w.WriteStartObject();
                        w.WriteString("type", "object");
                        w.WriteStartObject("additionalProperties");
                        w.WriteString("type", "array");
                        w.WriteBoolean("uniqueItems", true);
                        w.WriteStartObject("items");
                        w.WriteString("type", "string");
                        w.WriteNumber("minLength", 1);
                        w.WriteEndObject();
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    else if (properties.TryGetProperty(field, out var definition)) definition.WriteTo(w);
                    else throw new QualificationSchemaUnsupported("catalog-field:" + field, "#/properties");
                }
                w.WriteEndObject();
                w.WritePropertyName("$defs");
                defs.WriteTo(w);
                w.WriteEndObject();
            }
            using var doc = JsonDocument.Parse(stream.ToArray());
            return doc.RootElement.Clone();
        }
    }

    /// <summary>The draft 2020-12 subset the qualification run plan schema uses (see the file header).</summary>
    public sealed class QualificationSchemaEvaluator
    {
        /// <summary>The only draft the evaluator accepts.</summary>
        public const string Draft = "https://json-schema.org/draft/2020-12/schema";

        private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "object", "array", "string", "number", "integer", "boolean", "null" };

        private readonly JsonElement _root;
        private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

        /// <summary>A root schema object with $schema the 2020-12 draft; QualificationSchemaUnsupported for anything outside the subset.</summary>
        public QualificationSchemaEvaluator(JsonElement root)
        {
            _root = root.Clone();
            if (_root.ValueKind != JsonValueKind.Object) throw new QualificationSchemaUnsupported("root", "#");
            if (!_root.TryGetProperty("$schema", out var draft) || draft.ValueKind != JsonValueKind.String || draft.GetString() != Draft)
                throw new QualificationSchemaUnsupported("$schema", "#");
            Check(_root, "#");
        }

        /// <summary>The instance pointers of every violation, in ordinal order; empty when the instance is valid.</summary>
        public IReadOnlyList<string> Violations(JsonElement instance)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            Evaluate(_root, instance, "", found);
            return found.ToArray();
        }

        private void Check(JsonElement schema, string at)
        {
            if (schema.ValueKind == JsonValueKind.True || schema.ValueKind == JsonValueKind.False) return;
            if (schema.ValueKind != JsonValueKind.Object) throw new QualificationSchemaUnsupported("schema", at);
            foreach (var keyword in schema.EnumerateObject())
            {
                var value = keyword.Value;
                var where = at + "/" + QualificationGraphJson.Escape(keyword.Name);
                switch (keyword.Name)
                {
                    case "$schema":
                        if (at != "#") throw new QualificationSchemaUnsupported("$schema", where);
                        break;
                    case "$id":
                    case "format":
                        if (value.ValueKind != JsonValueKind.String) throw new QualificationSchemaUnsupported(keyword.Name, where);
                        break;
                    case "$defs":
                    case "properties":
                        if (value.ValueKind != JsonValueKind.Object) throw new QualificationSchemaUnsupported(keyword.Name, where);
                        foreach (var member in value.EnumerateObject()) Check(member.Value, where + "/" + QualificationGraphJson.Escape(member.Name));
                        break;
                    case "$ref":
                        if (value.ValueKind != JsonValueKind.String) throw new QualificationSchemaUnsupported("$ref", where);
                        Resolve(value.GetString(), where);
                        break;
                    case "type":
                        var names = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : new[] { value };
                        if (names.Length == 0 || names.Any(n => n.ValueKind != JsonValueKind.String || !Types.Contains(n.GetString())))
                            throw new QualificationSchemaUnsupported("type", where);
                        break;
                    case "const":
                        break;
                    case "enum":
                        if (value.ValueKind != JsonValueKind.Array) throw new QualificationSchemaUnsupported("enum", where);
                        break;
                    case "pattern":
                        if (value.ValueKind != JsonValueKind.String) throw new QualificationSchemaUnsupported("pattern", where);
                        try { Pattern(value.GetString()); }
                        catch (ArgumentException) { throw new QualificationSchemaUnsupported("pattern", where); }
                        break;
                    case "minLength":
                    case "maxLength":
                    case "minItems":
                        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count < 0)
                            throw new QualificationSchemaUnsupported(keyword.Name, where);
                        break;
                    case "minimum":
                        if (value.ValueKind != JsonValueKind.Number) throw new QualificationSchemaUnsupported("minimum", where);
                        break;
                    case "uniqueItems":
                        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                            throw new QualificationSchemaUnsupported("uniqueItems", where);
                        break;
                    case "items":
                    case "additionalProperties":
                        Check(value, where);
                        break;
                    case "required":
                        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(n => n.ValueKind != JsonValueKind.String))
                            throw new QualificationSchemaUnsupported("required", where);
                        break;
                    case "anyOf":
                        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) throw new QualificationSchemaUnsupported("anyOf", where);
                        var branch = 0;
                        foreach (var sub in value.EnumerateArray()) Check(sub, where + "/" + branch++);
                        break;
                    default:
                        throw new QualificationSchemaUnsupported(keyword.Name, where);
                }
            }
        }

        private JsonElement Resolve(string reference, string at)
        {
            const string prefix = "#/$defs/";
            if (reference == null || !reference.StartsWith(prefix, StringComparison.Ordinal)) throw new QualificationSchemaUnsupported("$ref:" + reference, at);
            var name = reference.Substring(prefix.Length);
            if (name.Length == 0 || name.Contains('/') || name.Contains('~') || !_root.TryGetProperty("$defs", out var defs) || !defs.TryGetProperty(name, out var target))
                throw new QualificationSchemaUnsupported("$ref:" + reference, at);
            return target;
        }

        private Regex Pattern(string pattern)
        {
            if (!_patterns.TryGetValue(pattern, out var regex))
            {
                regex = new Regex(pattern, RegexOptions.ECMAScript);
                _patterns[pattern] = regex;
            }
            return regex;
        }

        private bool Evaluate(JsonElement schema, JsonElement instance, string at, ISet<string> found)
        {
            if (schema.ValueKind == JsonValueKind.True) return true;
            if (schema.ValueKind == JsonValueKind.False) return Fail(at, found);
            var valid = true;
            foreach (var keyword in schema.EnumerateObject())
            {
                var value = keyword.Value;
                switch (keyword.Name)
                {
                    case "$ref":
                        valid &= Evaluate(Resolve(value.GetString(), at), instance, at, found);
                        break;
                    case "type":
                        if (!TypeMatches(value, instance)) valid = Fail(at, found);
                        break;
                    case "const":
                        if (!Same(value, instance)) valid = Fail(at, found);
                        break;
                    case "enum":
                        if (!value.EnumerateArray().Any(v => Same(v, instance))) valid = Fail(at, found);
                        break;
                    case "pattern":
                        if (instance.ValueKind == JsonValueKind.String && !Pattern(value.GetString()).IsMatch(instance.GetString())) valid = Fail(at, found);
                        break;
                    case "minLength":
                        if (instance.ValueKind == JsonValueKind.String && CodePoints(instance.GetString()) < value.GetInt32()) valid = Fail(at, found);
                        break;
                    case "maxLength":
                        if (instance.ValueKind == JsonValueKind.String && CodePoints(instance.GetString()) > value.GetInt32()) valid = Fail(at, found);
                        break;
                    case "minimum":
                        if (instance.ValueKind == JsonValueKind.Number && QualificationGraphJson.Compare(instance.GetRawText(), value.GetRawText()) < 0)
                            valid = Fail(at, found);
                        break;
                    case "minItems":
                        if (instance.ValueKind == JsonValueKind.Array && instance.GetArrayLength() < value.GetInt32()) valid = Fail(at, found);
                        break;
                    case "uniqueItems":
                        if (value.ValueKind == JsonValueKind.True && instance.ValueKind == JsonValueKind.Array && !Unique(instance)) valid = Fail(at, found);
                        break;
                    case "items":
                        if (instance.ValueKind == JsonValueKind.Array)
                        {
                            var i = 0;
                            foreach (var item in instance.EnumerateArray()) valid &= Evaluate(value, item, at + "/" + i++, found);
                        }
                        break;
                    case "required":
                        if (instance.ValueKind == JsonValueKind.Object)
                            foreach (var name in value.EnumerateArray().Select(n => n.GetString()))
                                if (!instance.TryGetProperty(name, out _)) valid = Fail(at + "/" + QualificationGraphJson.Escape(name), found);
                        break;
                    case "properties":
                        if (instance.ValueKind == JsonValueKind.Object)
                            foreach (var member in instance.EnumerateObject())
                                if (value.TryGetProperty(member.Name, out var sub))
                                    valid &= Evaluate(sub, member.Value, at + "/" + QualificationGraphJson.Escape(member.Name), found);
                        break;
                    case "additionalProperties":
                        if (instance.ValueKind == JsonValueKind.Object)
                        {
                            var declared = schema.TryGetProperty("properties", out var props) ? props : default;
                            foreach (var member in instance.EnumerateObject())
                                if (declared.ValueKind != JsonValueKind.Object || !declared.TryGetProperty(member.Name, out _))
                                    valid &= Evaluate(value, member.Value, at + "/" + QualificationGraphJson.Escape(member.Name), found);
                        }
                        break;
                    case "anyOf":
                        var any = false;
                        foreach (var sub in value.EnumerateArray())
                            if (Evaluate(sub, instance, at, new HashSet<string>(StringComparer.Ordinal))) { any = true; break; }
                        if (!any) valid = Fail(at, found);
                        break;
                    default:
                        // $schema, $id, $defs and format: annotations (Check refused every other keyword).
                        break;
                }
            }
            return valid;
        }

        private static bool Fail(string at, ISet<string> found)
        {
            found.Add(at);
            return false;
        }

        private static bool TypeMatches(JsonElement type, JsonElement instance)
        {
            if (type.ValueKind == JsonValueKind.Array) return type.EnumerateArray().Any(t => IsType(t.GetString(), instance));
            return IsType(type.GetString(), instance);
        }

        private static bool IsType(string type, JsonElement instance) => type switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number && QualificationGraphJson.IsInteger(instance.GetRawText()),
            "boolean" => instance.ValueKind == JsonValueKind.True || instance.ValueKind == JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => false,
        };

        private static int CodePoints(string text)
        {
            var n = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                n++;
            }
            return n;
        }

        private static bool Unique(JsonElement array)
        {
            var items = array.EnumerateArray().ToArray();
            for (var i = 0; i < items.Length; i++)
                for (var j = i + 1; j < items.Length; j++)
                    if (Same(items[i], items[j])) return false;
            return true;
        }

        // JSON Schema equality: numbers by value, strings ordinal, objects by member set, arrays element by element.
        private static bool Same(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            switch (a.ValueKind)
            {
                case JsonValueKind.Number:
                    return QualificationGraphJson.Compare(a.GetRawText(), b.GetRawText()) == 0;
                case JsonValueKind.String:
                    return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
                case JsonValueKind.Array:
                    if (a.GetArrayLength() != b.GetArrayLength()) return false;
                    using (var left = a.EnumerateArray().GetEnumerator())
                    using (var right = b.EnumerateArray().GetEnumerator())
                        while (left.MoveNext() && right.MoveNext())
                            if (!Same(left.Current, right.Current)) return false;
                    return true;
                case JsonValueKind.Object:
                    var members = a.EnumerateObject().ToArray();
                    if (members.Length != b.EnumerateObject().Count()) return false;
                    foreach (var member in members)
                        if (!b.TryGetProperty(member.Name, out var other) || !Same(member.Value, other)) return false;
                    return true;
                default:
                    return true;
            }
        }
    }
}
