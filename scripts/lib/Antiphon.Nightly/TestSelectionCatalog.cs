// CARD-1313: the selection catalog (tests/test-selection-policy.json) and its hash. The 2026-11-22 plan (unit 3) ported
// Assert-TestSelectionCatalog, Get-TestSelectionCatalogHash, ConvertFrom-TestSelectionCatalogText and
// Read-TestSelectionCatalog from scripts/lib/test-selection.ps1, which is deleted. The catalog is data, never code: a key
// outside the schema, an unknown resource on an unattended set, a dependency or prerequisite that is missing or cyclic,
// a rule that is not an anchored, bounded, valid regular expression, and a stale hash are each refused with the reason
// the PowerShell gave. The catalog is the -AsHashtable reading (TestSelectionJson.Parse), or a .ps1 caller's own
// hashtable; values are judged as PowerShell judged them (truthiness, [string] conversion, @() counts). Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>Assert-TestSelectionCatalog and the catalog readers.</summary>
    public static class TestSelectionCatalog
    {
        private static readonly string[] CatalogKeys = { "schemaVersion", "catalogHash", "sets", "rules", "prerequisites", "edges", "activation" };
        private static readonly string[] SetKeys = { "id", "members", "remainderProjects", "sourceInputs", "dependsOn", "resources", "platforms", "categories", "eligibility", "owner", "reason", "prerequisites" };
        private static readonly string[] ResourceKeys = { "vendorExecutable", "interaction", "api", "browser", "desktopUi", "nativePty", "postgres", "docker", "broker" };
        private static readonly string[] FlagResources = { "browser", "desktopUi", "nativePty", "postgres", "docker", "broker" };
        private static readonly string[] MemberKeys = { "project", "class", "method", "script", "source" };
        private static readonly Regex SetId = new Regex("^[a-z0-9.-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MemberName = new Regex("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Checked = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Get-TestSelectionCatalogHash: the digest of every key but catalogHash.</summary>
        public static string Hash(object catalog)
        {
            var copy = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Dictionary(catalog)) if (PsValue.Str(entry.Key) != "catalogHash") copy[entry.Key] = entry.Value;
            return TestSelectionJson.Digest(copy);
        }

        /// <summary>ConvertFrom-TestSelectionCatalogText: the parsed catalog, checked once per text in this process (a fresh object every call).</summary>
        public static IDictionary Parse(string text)
        {
            object parsed;
            try { parsed = TestSelectionJson.Parse(text); }
            catch (System.Text.Json.JsonException e) { throw new TestSelectionRefusal("catalog-unparseable: " + e.Message); }
            var key = TestSelectionJson.Sha256Text(text);
            bool known;
            lock (Checked) known = Checked.Contains(key);
            if (!known)
            {
                Assert(parsed);
                lock (Checked) Checked.Add(key);
            }
            return Dictionary(parsed);
        }

        /// <summary>Read-TestSelectionCatalog: the catalog file, checked, whose hash must be the pinned one (catalog-pin-mismatch).</summary>
        public static IDictionary Read(string path, string expectedHash)
        {
            object parsed;
            try { parsed = TestSelectionJson.Parse(File.ReadAllText(path)); }
            catch (System.Text.Json.JsonException e) { throw new TestSelectionRefusal("catalog-unparseable: " + e.Message); }
            var catalog = Assert(parsed);
            if (expectedHash != PsValue.BaseObject(catalog["catalogHash"]) as string) throw new TestSelectionRefusal("catalog-pin-mismatch");
            return catalog;
        }

        /// <summary>Assert-TestSelectionCatalog: the catalog itself when it passes; otherwise TestSelectionRefusal with the reason.</summary>
        public static IDictionary Assert(object value)
        {
            var catalog = Keys(value, CatalogKeys);
            var schema = PsValue.BaseObject(catalog["schemaVersion"]);
            if (!(schema is long || schema is int) || Convert.ToInt64(schema, System.Globalization.CultureInfo.InvariantCulture) != 1) throw Refuse("catalog-schema-unsupported");
            if (!(PsValue.BaseObject(catalog["catalogHash"]) is string stated) || stated != Hash(catalog)) throw Refuse("catalog-hash-stale");
            if (!(PsValue.BaseObject(catalog["activation"]) is string activation) || activation != "unqualified") throw Refuse("selection-cannot-qualify-itself");

            var sets = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
            var setOrder = new List<string>();
            foreach (var item in TestSelectionJson.Elements(catalog["sets"]))
            {
                var set = Keys(item, SetKeys);
                var id = PsValue.Str(set["id"]);
                if (!SetId.IsMatch(id) || sets.ContainsKey(id)) throw Refuse("invalid-set-id");
                sets.Add(id, set);
                setOrder.Add(id);
                var eligibility = set["eligibility"];
                if (!In(eligibility, "unattended", "manual", "unclassified")) throw Refuse("invalid-eligibility");
                if (!PsValue.IsTrue(set["owner"]) || !PsValue.IsTrue(set["reason"]) || PsValue.AsArray(set["platforms"]).Length == 0) throw Refuse("missing-disposition");
                var resources = Keys(set["resources"], ResourceKeys);
                var unattended = PsValue.BaseObject(eligibility) is string e && e == "unattended";
                foreach (var key in ResourceKeys)
                {
                    if (!resources.Contains(key)) throw Refuse("unknown-resource");
                    if (PsValue.BaseObject(resources[key]) is string text && text == "unknown" && unattended) throw Refuse("unknown-resource");
                }
                if (!In(resources["vendorExecutable"], "none", "claude", "codex", "grok", "unknown")
                    || !In(resources["interaction"], "none", "native-probe", "tui", "unknown")
                    || !In(resources["api"], "none", "stub", "live", "unknown")) throw Refuse("invalid-resource");
                foreach (var key in FlagResources)
                {
                    var flag = PsValue.BaseObject(resources[key]);
                    if (!(flag is bool) && !(flag is string unknown && unknown == "unknown")) throw Refuse("invalid-resource");
                }
                foreach (var entry in TestSelectionJson.Elements(set["members"]))
                {
                    var member = Keys(entry, MemberKeys);
                    if (!PsValue.IsTrue(member["project"]) && !PsValue.IsTrue(member["script"])) throw Refuse("invalid-member");
                    foreach (var key in new[] { "class", "method" })
                        if (PsValue.IsTrue(member[key]) && !MemberName.IsMatch(PsValue.Str(member[key]))) throw Refuse("invalid-member");
                }
            }
            if (!sets.ContainsKey("core")) throw Refuse("missing-core");

            var prerequisites = new Dictionary<string, IDictionary>(StringComparer.Ordinal);
            var prerequisiteOrder = new List<string>();
            foreach (var item in TestSelectionJson.Elements(catalog["prerequisites"]))
            {
                var prerequisite = Keys(item, new[] { "id", "dependsOn", "reason" });
                var id = PsValue.Str(prerequisite["id"]);
                if (!PsValue.IsTrue(prerequisite["id"]) || prerequisites.ContainsKey(id)) throw Refuse("invalid-prerequisite");
                prerequisites.Add(id, prerequisite);
                prerequisiteOrder.Add(id);
            }
            foreach (var (map, order) in new[] { (sets, setOrder), (prerequisites, prerequisiteOrder) })
            {
                var visiting = new HashSet<string>(StringComparer.Ordinal);
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in order) Visit(id, map, visiting, done);
            }
            foreach (var id in setOrder)
                foreach (var p in TestSelectionJson.Elements(sets[id]["prerequisites"]))
                    if (!prerequisites.ContainsKey(PsValue.Str(p))) throw Refuse("missing-prerequisite:" + PsValue.Str(p));

            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in TestSelectionJson.Elements(catalog["rules"]))
            {
                var rule = Keys(item, new[] { "id", "paths", "pattern", "sets", "full", "partition" });
                if (!ruleIds.Add(PsValue.Str(rule["id"]))) throw Refuse("duplicate-rule");
                var pattern = rule["pattern"];
                var anyPath = TestSelectionJson.Elements(rule["paths"]).Any(PsValue.IsTrue);
                if (!anyPath && !PsValue.IsTrue(pattern)) throw Refuse("invalid-rule");
                if (!(PsValue.BaseObject(rule["full"]) is bool)) throw Refuse("invalid-rule");
                if (PsValue.IsTrue(pattern))
                {
                    var text = PsValue.Str(pattern);
                    if (!text.StartsWith("^", StringComparison.CurrentCulture) || !text.EndsWith("$", StringComparison.CurrentCulture) || text.Length > 2048) throw Refuse("invalid-rule");
                    try { _ = new Regex(text, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
                    catch (ArgumentException) { throw Refuse("invalid-rule"); }
                }
                foreach (var s in TestSelectionJson.Elements(rule["sets"]))
                    if (!sets.ContainsKey(PsValue.Str(s))) throw Refuse("missing-dependency:" + PsValue.Str(s));
            }
            foreach (var item in TestSelectionJson.Elements(catalog["edges"]))
            {
                var edge = Keys(item, new[] { "id", "path", "sets", "kind" });
                if (!PsValue.IsTrue(edge["path"]) || !PsValue.IsTrue(edge["id"]) || !In(edge["kind"], "linked-compile", "staged-child", "imported-helper", "runtime-bundle"))
                    throw Refuse("invalid-edge");
                foreach (var s in TestSelectionJson.Elements(edge["sets"]))
                    if (!sets.ContainsKey(PsValue.Str(s))) throw Refuse("missing-dependency:" + PsValue.Str(s));
            }
            return catalog;
        }

        private static TestSelectionRefusal Refuse(string reason) => new TestSelectionRefusal(reason);

        private static IDictionary Dictionary(object value) =>
            PsValue.BaseObject(value) as IDictionary ?? throw Refuse("invalid-catalog-object");

        /// <summary>Assert-TestSelectionKeys: a dictionary whose every key (case-sensitive) is allowed.</summary>
        private static IDictionary Keys(object value, string[] allowed)
        {
            var dictionary = Dictionary(value);
            foreach (var key in dictionary.Keys)
                if (Array.IndexOf(allowed, PsValue.Str(key)) < 0) throw Refuse("invalid-catalog-field:" + PsValue.Str(key));
            return dictionary;
        }

        /// <summary>value -cin allowed: the [string] of a value that is not null.</summary>
        private static bool In(object value, params string[] allowed) => value != null && Array.IndexOf(allowed, PsValue.Str(value)) >= 0;

        private static void Visit(string id, Dictionary<string, IDictionary> map, HashSet<string> visiting, HashSet<string> done)
        {
            if (!map.ContainsKey(id)) throw Refuse("missing-dependency:" + id);
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) throw Refuse("dependency-cycle:" + id);
            foreach (var target in TestSelectionJson.Elements(map[id]["dependsOn"])) Visit(PsValue.Str(target), map, visiting, done);
            visiting.Remove(id);
            done.Add(id);
        }
    }
}
