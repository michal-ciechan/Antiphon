// Assert-TestSelectionCatalog (scripts/lib/test-selection.ps1) on the -AsHashtable catalog. Every refusal is
// NotHandled (the PowerShell worker then gives its own message); a value whose PowerShell truthiness or
// comparison this port would have to guess at (a non-string id, a scalar where a list is expected) is
// NotHandled too, so the port only ever accepts what the PowerShell accepts.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Scripts
{
    public static class SelectionCatalog
    {
        private static readonly string[] CatalogKeys = { "schemaVersion", "catalogHash", "sets", "rules", "prerequisites", "edges", "activation" };
        private static readonly string[] SetKeys = { "id", "members", "remainderProjects", "sourceInputs", "dependsOn", "resources", "platforms", "categories", "eligibility", "owner", "reason", "prerequisites" };
        private static readonly string[] ResourceKeys = { "vendorExecutable", "interaction", "api", "browser", "desktopUi", "nativePty", "postgres", "docker", "broker" };
        private static readonly string[] FlagResources = { "browser", "desktopUi", "nativePty", "postgres", "docker", "broker" };
        private static readonly string[] MemberKeys = { "project", "class", "method", "script", "source" };
        private static readonly Regex SetId = new Regex("^[a-z0-9.-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MemberName = new Regex("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static NotHandledException Refused(string reason) => new NotHandledException("catalog:" + reason);

        private static PsObject Keys(object value, string[] allowed)
        {
            if (!(value is PsObject obj)) throw Refused("invalid-catalog-object");
            foreach (string key in obj.Keys) if (Array.IndexOf(allowed, key) < 0) throw Refused("invalid-catalog-field");
            return obj;
        }

        /// <summary>foreach over a list-valued field: null is no items; anything but a list is not handled.</summary>
        private static List<object> Items(object value)
        {
            if (value == null) return new List<object>();
            if (value is List<object> list) return list;
            throw Refused("list-shape");
        }

        private static string NonEmpty(object value, string reason)
        {
            if (value is string s && s.Length > 0) return s;
            throw Refused(reason);
        }

        /// <summary>An optional string: null or empty is absent; a non-string is not handled.</summary>
        private static string Optional(object value)
        {
            if (value == null) return "";
            if (value is string s) return s;
            throw Refused("value-shape");
        }

        public static void Assert(PsObject catalog)
        {
            Keys(catalog, CatalogKeys);
            if (!(catalog.GetExact("schemaVersion") is long schema) || schema != 1) throw Refused("catalog-schema-unsupported");
            var copy = new PsObject();
            foreach (var pair in catalog.Pairs) if (pair.Key != "catalogHash") copy.Pairs.Add(pair);
            if (!(catalog.GetExact("catalogHash") is string hash) || hash != ReleaseSelection.SelectionDigest(copy)) throw Refused("catalog-hash-stale");
            if (!(catalog.GetExact("activation") is string activation) || activation != "unqualified") throw Refused("selection-cannot-qualify-itself");

            var sets = new Dictionary<string, PsObject>(StringComparer.Ordinal);
            foreach (object item in Items(catalog.GetExact("sets")))
            {
                PsObject set = Keys(item, SetKeys);
                string id = NonEmpty(set.GetExact("id"), "invalid-set-id");
                if (!SetId.IsMatch(id) || sets.ContainsKey(id)) throw Refused("invalid-set-id");
                sets.Add(id, set);
                string eligibility = set.GetExact("eligibility") as string;
                if (eligibility != "unattended" && eligibility != "manual" && eligibility != "unclassified") throw Refused("invalid-eligibility");
                NonEmpty(set.GetExact("owner"), "missing-disposition");
                NonEmpty(set.GetExact("reason"), "missing-disposition");
                if (Items(set.GetExact("platforms")).Count == 0) throw Refused("missing-disposition");
                PsObject resources = Keys(set.GetExact("resources"), ResourceKeys);
                foreach (string key in ResourceKeys)
                {
                    if (!resources.HasExact(key)) throw Refused("unknown-resource");
                    if (resources.GetExact(key) is string text && text == "unknown" && eligibility == "unattended") throw Refused("unknown-resource");
                }
                if (!In(resources.GetExact("vendorExecutable"), "none", "claude", "codex", "grok", "unknown") ||
                    !In(resources.GetExact("interaction"), "none", "native-probe", "tui", "unknown") ||
                    !In(resources.GetExact("api"), "none", "stub", "live", "unknown")) throw Refused("invalid-resource");
                foreach (string key in FlagResources)
                {
                    object flag = resources.GetExact(key);
                    if (!(flag is bool) && !(flag is string unknown && unknown == "unknown")) throw Refused("invalid-resource");
                }
                foreach (object entry in Items(set.GetExact("members")))
                {
                    PsObject member = Keys(entry, MemberKeys);
                    if (Optional(member.GetExact("project")).Length == 0 && Optional(member.GetExact("script")).Length == 0) throw Refused("invalid-member");
                    foreach (string key in new[] { "class", "method" })
                    {
                        string name = Optional(member.GetExact(key));
                        if (name.Length > 0 && !MemberName.IsMatch(name)) throw Refused("invalid-member");
                    }
                    Optional(member.GetExact("source"));
                }
            }
            if (!sets.ContainsKey("core")) throw Refused("missing-core");

            var prerequisites = new Dictionary<string, PsObject>(StringComparer.Ordinal);
            foreach (object item in Items(catalog.GetExact("prerequisites")))
            {
                PsObject prerequisite = Keys(item, new[] { "id", "dependsOn", "reason" });
                string id = NonEmpty(prerequisite.GetExact("id"), "invalid-prerequisite");
                if (prerequisites.ContainsKey(id)) throw Refused("invalid-prerequisite");
                prerequisites.Add(id, prerequisite);
            }
            foreach (var map in new[] { sets, prerequisites })
            {
                var visiting = new HashSet<string>(StringComparer.Ordinal);
                var done = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in map.Keys.ToList()) Visit(id, map, visiting, done);
            }
            foreach (PsObject set in sets.Values)
                foreach (object p in Items(set.GetExact("prerequisites")))
                    if (!(p is string id) || !prerequisites.ContainsKey(id)) throw Refused("missing-prerequisite");

            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in Items(catalog.GetExact("rules")))
            {
                PsObject rule = Keys(item, new[] { "id", "paths", "pattern", "sets", "full", "partition" });
                if (!(rule.GetExact("id") is string id) || !ruleIds.Add(id)) throw Refused("duplicate-rule");
                bool anyPath = Items(rule.GetExact("paths")).Any(p => Optional(p).Length > 0);
                string pattern = Optional(rule.GetExact("pattern"));
                if (!anyPath && pattern.Length == 0) throw Refused("invalid-rule");
                if (!(rule.GetExact("full") is bool)) throw Refused("invalid-rule");
                if (pattern.Length > 0)
                {
                    if (!pattern.StartsWith("^", StringComparison.Ordinal) || !pattern.EndsWith("$", StringComparison.Ordinal) || pattern.Length > 2048) throw Refused("invalid-rule");
                    try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
                    catch (ArgumentException) { throw Refused("invalid-rule"); }
                }
                foreach (object s in Items(rule.GetExact("sets"))) if (!(s is string name) || !sets.ContainsKey(name)) throw Refused("missing-dependency");
            }
            foreach (object item in Items(catalog.GetExact("edges")))
            {
                PsObject edge = Keys(item, new[] { "id", "path", "sets", "kind" });
                NonEmpty(edge.GetExact("path"), "invalid-edge");
                NonEmpty(edge.GetExact("id"), "invalid-edge");
                if (!In(edge.GetExact("kind"), "linked-compile", "staged-child", "imported-helper", "runtime-bundle")) throw Refused("invalid-edge");
                foreach (object s in Items(edge.GetExact("sets"))) if (!(s is string name) || !sets.ContainsKey(name)) throw Refused("missing-dependency");
            }
        }

        private static bool In(object value, params string[] allowed) => value is string s && Array.IndexOf(allowed, s) >= 0;

        private static void Visit(string id, Dictionary<string, PsObject> map, HashSet<string> visiting, HashSet<string> done)
        {
            if (!map.ContainsKey(id)) throw Refused("missing-dependency");
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) throw Refused("dependency-cycle");
            foreach (object target in Items(map[id].GetExact("dependsOn")))
            {
                if (!(target is string next)) throw Refused("missing-dependency");
                Visit(next, map, visiting, done);
            }
            visiting.Remove(id);
            done.Add(id);
        }
    }
}
