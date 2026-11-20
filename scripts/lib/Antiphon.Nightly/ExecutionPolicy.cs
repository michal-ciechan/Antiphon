// The test-execution policy (tests/test-execution-policy.json) as plain records, parsed with System.Text.Json. Field
// names follow the JSON (PowerShell reads them case-insensitively: $policy.profiles.rc.exclusions). Parsing is lenient;
// NightlyPolicy validates where it reads (a profile, its exclusion rows, the project dispositions).
//
// The policy hash is the SHA-256 of the canonical JSON of every top-level key but policyHash: keys sorted as
// PowerShell's Sort-Object sorts them (current culture, ignoring case, stable; empty keys dropped), integers as digits,
// other numbers in their shortest form, strings with only \ " CR LF TAB escaped. It is computed from the parsed JSON.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public sealed class ExecutionPolicy
    {
        /// <summary>The file read (empty for policy text parsed directly).</summary>
        public string Path { get; set; } = "";
        public int SchemaVersion { get; set; }
        public string PolicyHash { get; set; } = "";
        /// <summary>The canonical hash of the parsed text (what policyHash must state).</summary>
        public string ComputedHash { get; set; } = "";
        /// <summary>Null when the policy has no defaultSuites.</summary>
        public string[] DefaultSuites { get; set; }
        public string[] Projects { get; set; }
        /// <summary>Null on a schema-1 policy (no profiles key).</summary>
        public Dictionary<string, PolicyProfile> Profiles { get; set; }
        public Dictionary<string, PolicySuite> Suites { get; set; } = new Dictionary<string, PolicySuite>(StringComparer.OrdinalIgnoreCase);
        public PolicySafeEnvironment SafeEnvironment { get; set; }
        public PolicyCensusRow[] ScriptCensus { get; set; } = new PolicyCensusRow[0];
        public Dictionary<string, long> Watchdogs { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public string ReducedDispatchPolicy { get; set; } = "";
        /// <summary>Null when the policy has no projectDispositions.</summary>
        public PolicyProjectDisposition[] ProjectDispositions { get; set; }
        public PolicySelectionCatalog SelectionCatalog { get; set; }
        /// <summary>Declared chunks by suite id; null when the policy declares none.</summary>
        public Dictionary<string, PolicyChunk[]> Chunks { get; set; }

        /// <summary>The policy in json (one JSON object); path is recorded as Path. Throws on text that is not one.</summary>
        public static ExecutionPolicy Parse(string json, string path = "")
        {
            using var doc = JsonDocument.Parse((json ?? "").TrimStart((char)0xFEFF), new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new NightlyRefusal("policy is not a JSON object: " + path);
            var policy = new ExecutionPolicy { Path = path ?? "", ComputedHash = HashOf(root) };
            policy.SchemaVersion = Json.Field(root, "schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var v) ? v : 0;
            policy.PolicyHash = Json.Text(root, "policyHash");
            policy.DefaultSuites = Json.Has(root, "defaultSuites") ? Texts(root, "defaultSuites") : null;
            policy.Projects = Json.Has(root, "projects") ? Texts(root, "projects") : null;
            if (Json.Field(root, "profiles", out var profiles) && profiles.ValueKind == JsonValueKind.Object)
            {
                policy.Profiles = new Dictionary<string, PolicyProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in profiles.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Object && !policy.Profiles.ContainsKey(p.Name)) policy.Profiles[p.Name] = PolicyProfile.From(p.Value);
            }
            if (Json.Field(root, "suites", out var suites) && suites.ValueKind == JsonValueKind.Object)
                foreach (var s in suites.EnumerateObject())
                    if (s.Value.ValueKind == JsonValueKind.Object && !policy.Suites.ContainsKey(s.Name)) policy.Suites[s.Name] = PolicySuite.From(s.Name, s.Value);
            if (Json.Field(root, "safeEnvironment", out var safe) && safe.ValueKind == JsonValueKind.Object) policy.SafeEnvironment = PolicySafeEnvironment.From(safe);
            if (Json.Field(root, "scriptCensus", out var census) && census.ValueKind == JsonValueKind.Array)
                policy.ScriptCensus = census.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(PolicyCensusRow.From).ToArray();
            if (Json.Field(root, "watchdogs", out var watchdogs) && watchdogs.ValueKind == JsonValueKind.Object)
                foreach (var w in watchdogs.EnumerateObject())
                    if (w.Value.ValueKind == JsonValueKind.Number && w.Value.TryGetInt64(out var ms)) policy.Watchdogs[w.Name] = ms;
            policy.ReducedDispatchPolicy = Json.Text(root, "reducedDispatchPolicy");
            if (Json.Field(root, "projectDispositions", out var dispositions) && dispositions.ValueKind == JsonValueKind.Array)
                policy.ProjectDispositions = dispositions.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(PolicyProjectDisposition.From).ToArray();
            if (Json.Field(root, "selectionCatalog", out var catalog) && catalog.ValueKind == JsonValueKind.Object)
                policy.SelectionCatalog = new PolicySelectionCatalog { Hash = Json.Text(catalog, "hash"), Path = Json.Text(catalog, "path") };
            if (Json.Field(root, "chunks", out var chunks) && chunks.ValueKind == JsonValueKind.Object)
            {
                policy.Chunks = new Dictionary<string, PolicyChunk[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in chunks.EnumerateObject())
                    policy.Chunks[c.Name] = (c.Value.ValueKind == JsonValueKind.Array ? c.Value.EnumerateArray() : Enumerable.Repeat(c.Value, 1))
                        .Where(r => r.ValueKind == JsonValueKind.Object).Select(PolicyChunk.From).ToArray();
            }
            return policy;
        }

        /// <summary>The policy hash of policy text (every top-level key but policyHash).</summary>
        public static string HashOf(string json)
        {
            using var doc = JsonDocument.Parse((json ?? "").TrimStart((char)0xFEFF), new JsonDocumentOptions { MaxDepth = 64 });
            return HashOf(doc.RootElement);
        }

        private static string HashOf(JsonElement root)
        {
            var sb = new StringBuilder();
            Canonical(sb, root, true);
            return TestPhaseRecords.Sha256Text(sb.ToString());
        }

        private static void Canonical(StringBuilder sb, JsonElement node, bool top)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    var pairs = new List<JsonProperty>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in node.EnumerateObject())
                    {
                        if (p.Name.Length == 0 || !seen.Add(p.Name)) continue;
                        if (top && string.Equals(p.Name, "policyHash", StringComparison.OrdinalIgnoreCase)) continue;
                        pairs.Add(p);
                    }
                    var culture = CultureInfo.CurrentCulture;
                    var sorted = pairs.OrderBy(p => p.Name, Comparer<string>.Create((x, y) => string.Compare(x, y, culture, CompareOptions.IgnoreCase))).ToList();
                    sb.Append('{');
                    for (var i = 0; i < sorted.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(sorted[i].Name).Append("\":");
                        Canonical(sb, sorted[i].Value, false);
                    }
                    sb.Append('}');
                    return;
                case JsonValueKind.Array:
                    sb.Append('[');
                    var first = true;
                    foreach (var item in node.EnumerateArray())
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Canonical(sb, item, false);
                    }
                    sb.Append(']');
                    return;
                case JsonValueKind.String:
                    var s = node.GetString();
                    sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")).Append('"');
                    return;
                case JsonValueKind.Number:
                    var raw = node.GetRawText();
                    if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && node.TryGetInt64(out var whole)) sb.Append(whole.ToString(CultureInfo.InvariantCulture));
                    else sb.Append(node.GetDouble().ToString(CultureInfo.InvariantCulture));
                    return;
                case JsonValueKind.True: sb.Append("true"); return;
                case JsonValueKind.False: sb.Append("false"); return;
                default: sb.Append("null"); return;
            }
        }

        // An array of strings (a lone value is one element; nulls and non-strings as their text).
        internal static string[] Texts(JsonElement o, string name)
        {
            if (!Json.Field(o, name, out var v) || v.ValueKind == JsonValueKind.Null) return new string[0];
            var items = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement> { v };
            return items.Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() : i.ValueKind == JsonValueKind.Null ? "" : i.GetRawText()).ToArray();
        }
    }

    public sealed class PolicyProfile
    {
        public string[] RequiredSuites { get; set; } = new string[0];
        public string Description { get; set; } = "";
        public ProfileExclusion[] Exclusions { get; set; } = new ProfileExclusion[0];

        internal static PolicyProfile From(JsonElement o)
        {
            var exclusions = new List<ProfileExclusion>();
            if (Json.Field(o, "exclusions", out var rows))
                foreach (var row in rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : Enumerable.Repeat(rows, 1))
                    if (row.ValueKind == JsonValueKind.Object) exclusions.Add(ProfileExclusion.From(row));
            return new PolicyProfile { RequiredSuites = ExecutionPolicy.Texts(o, "requiredSuites"), Description = Json.Text(o, "description"), Exclusions = exclusions.ToArray() };
        }
    }

    /// <summary>A declared class (or class+method) exclusion of a profile.</summary>
    public sealed class ProfileExclusion
    {
        public string Suite { get; set; } = "";
        public string Class { get; set; } = "";
        public string[] Methods { get; set; } = new string[0];
        public string Reason { get; set; } = "";
        public string Owner { get; set; } = "";
        /// <summary>windows or linux; null when the row applies to every platform.</summary>
        public string Platform { get; set; }
        /// <summary>The row names a platform that is not "windows" or "linux" (or not text).</summary>
        public bool PlatformInvalid { get; set; }

        internal static ProfileExclusion From(JsonElement o)
        {
            var row = new ProfileExclusion
            {
                Suite = Json.Text(o, "suite"), Class = Json.Text(o, "class"), Reason = Json.Text(o, "reason"), Owner = Json.Text(o, "owner"),
                Methods = ExecutionPolicy.Texts(o, "methods").Where(m => !string.IsNullOrWhiteSpace(m)).ToArray(),
            };
            // The exact name only, as ConvertTo-NightlyPolicyMap keyed it.
            if (o.TryGetProperty("platform", out var platform))
            {
                if (platform.ValueKind == JsonValueKind.String && (platform.GetString() == "windows" || platform.GetString() == "linux")) row.Platform = platform.GetString();
                else row.PlatformInvalid = true;
            }
            return row;
        }
    }

    public sealed class PolicySuite
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Project { get; set; } = "";
        public string Assembly { get; set; } = "";
        public string Mode { get; set; } = "";
        public string ProcessGroup { get; set; } = "";
        public string SlowRegistry { get; set; } = "";
        public string Broker { get; set; } = "";
        public string Command { get; set; } = "";
        public string[] Requires { get; set; } = new string[0];
        public string Reason { get; set; } = "";

        internal static PolicySuite From(string id, JsonElement o) => new PolicySuite
        {
            Id = id, Name = Json.Text(o, "name"), Project = Json.Text(o, "project"), Assembly = Json.Text(o, "assembly"), Mode = Json.Text(o, "mode"),
            ProcessGroup = Json.Text(o, "processGroup"), SlowRegistry = Json.Text(o, "slowRegistry"), Broker = Json.Text(o, "broker"),
            Command = Json.Text(o, "command"), Requires = ExecutionPolicy.Texts(o, "requires"), Reason = Json.Text(o, "reason"),
        };
    }

    public sealed class PolicySafeEnvironment
    {
        public string[] Clear { get; set; } = new string[0];
        public Dictionary<string, string> Set { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static PolicySafeEnvironment From(JsonElement o)
        {
            var safe = new PolicySafeEnvironment { Clear = ExecutionPolicy.Texts(o, "clear") };
            if (Json.Field(o, "set", out var set) && set.ValueKind == JsonValueKind.Object)
                foreach (var p in set.EnumerateObject()) safe.Set[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind == JsonValueKind.Null ? "" : p.Value.GetRawText();
            return safe;
        }
    }

    public sealed class PolicyCensusRow
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string Disposition { get; set; } = "";
        public bool Wildcard { get; set; }
        public string[] Arguments { get; set; } = new string[0];

        internal static PolicyCensusRow From(JsonElement o) => new PolicyCensusRow
        {
            Id = Json.Text(o, "id"), Path = Json.Text(o, "path"), Disposition = Json.Text(o, "disposition"),
            Wildcard = Json.Field(o, "wildcard", out var w) && w.ValueKind == JsonValueKind.True, Arguments = ExecutionPolicy.Texts(o, "arguments"),
        };
    }

    public sealed class PolicyProjectDisposition
    {
        public string Project { get; set; } = "";
        public string Role { get; set; } = "";
        public string Reason { get; set; } = "";
        /// <summary>Null when the row names no consumers key.</summary>
        public string[] Consumers { get; set; }

        internal static PolicyProjectDisposition From(JsonElement o) => new PolicyProjectDisposition
        {
            Project = Json.Text(o, "project"), Role = Json.Text(o, "role"), Reason = Json.Text(o, "reason"),
            Consumers = Json.Has(o, "consumers") ? ExecutionPolicy.Texts(o, "consumers") : null,
        };
    }

    public sealed class PolicySelectionCatalog
    {
        public string Hash { get; set; } = "";
        public string Path { get; set; } = "";
    }

    /// <summary>A declared chunk of a suite: its id, its classes (none: every class) and its planned seconds.</summary>
    public sealed class PolicyChunk
    {
        public string Id { get; set; } = "";
        public string[] Classes { get; set; } = new string[0];
        public double? Seconds { get; set; }

        internal static PolicyChunk From(JsonElement o) => new PolicyChunk
        {
            Id = Json.Text(o, "id"), Classes = ExecutionPolicy.Texts(o, "classes"), Seconds = Json.Number(o, "seconds"),
        };
    }
}
