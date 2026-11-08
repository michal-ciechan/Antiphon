// Compiled loops of the discovery document round trip (nightly-coverage.ps1) and the canonical JSON writer
// (nightly-common.ps1). Each returns exactly what its PowerShell loop returned; the format checks and
// refusals with their messages stay in PowerShell.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Text;

namespace Antiphon.Nightly
{
    public static class NightlyDocuments
    {
        // $n.PSObject.Properties.Name -contains 'name' (case-insensitive; a dictionary's keys do not count).
        private static bool HasProperty(object o, string name) =>
            o != null && PSObject.AsPSObject(o).Properties[name] != null;

        private static object[] Categories(object node)
        {
            object raw = PsValue.Prop(node, "categories");
            if (!LanguagePrimitives.IsTrue(raw)) return new object[0];
            var list = new List<object>();
            foreach (object c in PsValue.AsArray(raw)) list.Add(PsValue.Str(c));
            return list.ToArray();
        }

        /// <summary>
        /// The node loop of ConvertFrom-NightlyDiscoveryDocument: Error is the refusal it would throw (empty when
        /// none), Nodes the [pscustomobject] rows.
        /// </summary>
        public static PSObject DiscoveryNodes(object nodes)
        {
            var rows = new List<object>();
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string error = "";
            foreach (object n in PsValue.AsArray(nodes))
            {
                string uid = PsValue.Str(PsValue.Prop(n, "uid"));
                if (string.IsNullOrWhiteSpace(uid)) { error = "truncated discovery record: missing uid"; break; }
                if (!uids.Add(uid)) { error = "duplicate discovery uid " + uid; break; }
                string typeName = PsValue.Str(PsValue.Prop(n, "type"));
                string method = PsValue.Str(PsValue.Prop(n, "method"));
                if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(method))
                {
                    error = "truncated discovery record uid=" + uid;
                    break;
                }
                object[] categories = Categories(n);
                bool excluded = false;
                if (HasProperty(n, "excluded")) excluded = LanguagePrimitives.IsTrue(PsValue.Prop(n, "excluded"));
                foreach (object c in categories)
                {
                    string text = (string)c;
                    if (string.Equals(text, "OptIn", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "Explicit", StringComparison.OrdinalIgnoreCase)) excluded = true;
                }
                string ns = PsValue.Str(PsValue.Prop(n, "namespace"));
                var row = new PSObject();
                row.Properties.Add(new PSNoteProperty("Uid", uid));
                row.Properties.Add(new PSNoteProperty("Assembly", PsValue.Str(PsValue.Prop(n, "assembly"))));
                row.Properties.Add(new PSNoteProperty("Namespace", ns));
                row.Properties.Add(new PSNoteProperty("Type", typeName));
                row.Properties.Add(new PSNoteProperty("Method", method));
                row.Properties.Add(new PSNoteProperty("Signature", PsValue.Str(PsValue.Prop(n, "signature"))));
                row.Properties.Add(new PSNoteProperty("State", PsValue.Str(PsValue.Prop(n, "state"))));
                row.Properties.Add(new PSNoteProperty("ClassName", string.IsNullOrWhiteSpace(ns) ? typeName : ns + "." + typeName));
                row.Properties.Add(new PSNoteProperty("Excluded", excluded));
                row.Properties.Add(new PSNoteProperty("Categories", categories));
                rows.Add(row);
            }
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Error", error));
            result.Properties.Add(new PSNoteProperty("Nodes", rows.ToArray()));
            return result;
        }

        /// <summary>The node loop of ConvertTo-NightlyDiscoveryDocument: one [ordered] row per node.</summary>
        public static object[] DocumentNodes(object[] nodes)
        {
            var mapped = new List<object>();
            foreach (object n in PsValue.AsArray(nodes))
            {
                string state = PsValue.Str(PsValue.Prop(n, "state"));
                if (string.IsNullOrWhiteSpace(state)) state = "Discovered";
                object[] categories = Categories(n);
                bool excluded = false;
                if (HasProperty(n, "excluded")) excluded = LanguagePrimitives.IsTrue(PsValue.Prop(n, "excluded"));
                var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
                row.Add("uid", PsValue.Str(PsValue.Prop(n, "uid")));
                row.Add("assembly", PsValue.Str(PsValue.Prop(n, "assembly")));
                row.Add("namespace", PsValue.Str(PsValue.Prop(n, "namespace")));
                row.Add("type", PsValue.Str(PsValue.Prop(n, "type")));
                row.Add("method", PsValue.Str(PsValue.Prop(n, "method")));
                row.Add("signature", PsValue.Str(PsValue.Prop(n, "signature")));
                row.Add("state", state);
                row.Add("excluded", excluded);
                row.Add("categories", categories);
                mapped.Add(row);
            }
            return mapped.ToArray();
        }

        /// <summary>ConvertTo-NightlyCanonicalJson: keys sorted as Sort-Object sorts them, only \ " CR LF TAB escaped.</summary>
        public static string CanonicalJson(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static readonly string[] SkippedMembers = { "Keys", "Values", "SyncRoot", "IsFixedSize", "IsReadOnly", "IsSynchronized" };

        private static void Write(StringBuilder sb, object node)
        {
            if (node == null) { sb.Append("null"); return; }
            object b = node is PSObject ps ? ps.BaseObject : node;
            if (b is string s)
            {
                sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")).Append('"');
                return;
            }
            if (b is bool flag) { sb.Append(flag ? "true" : "false"); return; }
            if (b is int || b is long || b is double || b is decimal)
            {
                sb.Append(PsValue.Str(b));
                return;
            }
            if (b is IDictionary dict)
            {
                var pairs = new List<KeyValuePair<string, object>>();
                foreach (DictionaryEntry e in dict) pairs.Add(new KeyValuePair<string, object>(PsValue.Str(e.Key), e.Value));
                // Where-Object { $_.K } | Sort-Object K: non-empty keys, current culture ignoring case, stable.
                var culture = CultureInfo.CurrentCulture;
                var sorted = pairs.Where(p => p.Key.Length > 0)
                    .OrderBy(p => p.Key, Comparer<string>.Create((x, y) => string.Compare(x, y, culture, CompareOptions.IgnoreCase)))
                    .ToList();
                sb.Append('{');
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(sorted[i].Key).Append("\":");
                    Write(sb, sorted[i].Value);
                }
                sb.Append('}');
                return;
            }
            if (b is IEnumerable items)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in items)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            var map = new OrderedDictionary();
            foreach (PSPropertyInfo p in PSObject.AsPSObject(node).Properties)
            {
                if (p.Name == "Count" && b is Array) continue;
                if (Array.IndexOf(SkippedMembers, p.Name) >= 0) continue;
                if (!map.Contains(p.Name))
                {
                    object v;
                    try { v = p.Value; } catch { v = null; }
                    map.Add(p.Name, v);
                }
            }
            Write(sb, map);
        }
    }
}
