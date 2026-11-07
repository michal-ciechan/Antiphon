// Compiled per-test work for the nightly scripts; loaded by nightly-core.ps1 (PowerShell 7, Add-Type).
//
// Each type here is a port of a PowerShell function that ran once per discovered test (about 19,000 in the
// main suite) or once per JSON value, and returns exactly what that function returned. The PowerShell
// functions remain the public entry points and call into this file. Two things deliberately stay in
// PowerShell: Group-Object and Sort-Object, whose ordering depends on the PowerShell version and culture.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Management.Automation;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>PowerShell's own reading of values: property access, [string] casts and @() wrapping.</summary>
    public static class PsValue
    {
        /// <summary><c>$o.Name</c>: a note or adapted property, or a dictionary key; null when absent.</summary>
        public static object Prop(object o, string name)
        {
            if (o == null) return null;
            if (o is PSObject ps)
            {
                if (ps.BaseObject is IDictionary wrapped && !(ps.BaseObject is PSCustomObject))
                {
                    var note = ps.Properties[name];
                    return note != null ? note.Value : wrapped[name];
                }
                return ps.Properties[name]?.Value;
            }
            if (o is IDictionary d) return d.Contains(name) ? d[name] : null;
            return PSObject.AsPSObject(o).Properties[name]?.Value;
        }

        /// <summary><c>[string]$o</c>.</summary>
        public static string Str(object o)
        {
            if (o == null) return "";
            if (o is string s) return s;
            if (o is PSObject ps && ps.BaseObject is string b) return b;
            return (string)LanguagePrimitives.ConvertTo(o, typeof(string), CultureInfo.InvariantCulture) ?? "";
        }

        /// <summary><c>@($o)</c>: null is one null element, a scalar (string, dictionary) is one element.</summary>
        public static object[] AsArray(object o)
        {
            if (o == null) return new object[] { null };
            var baseObject = o is PSObject ps ? ps.BaseObject : o;
            if (baseObject is string || baseObject is IDictionary || !(baseObject is IEnumerable enumerable))
                return new object[] { o };
            var items = new List<object>();
            foreach (var item in enumerable) items.Add(item);
            return items.ToArray();
        }

        public static bool IsWhite(string s) => string.IsNullOrWhiteSpace(s);
    }

    /// <summary>Class-name helpers from nightly-coverage.ps1.</summary>
    public static class NightlyClassNames
    {
        /// <summary>Get-NightlyBareClassIdentity: TUnit's "Type(ctorArgs)" without the constructor suffix.</summary>
        public static string Bare(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            var open = name.IndexOf('(');
            if (open > 0 && name.EndsWith(")", StringComparison.Ordinal)) return name.Substring(0, open);
            return name;
        }

        /// <summary>Get-NightlyClassNameFromIdentity: the last dotted segment of the bare name.</summary>
        public static string Simple(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) return className;
            var parts = Bare(className).Split('.');
            return parts[parts.Length - 1];
        }

        /// <summary>Test-NightlyClassAssigned.</summary>
        public static bool Assigned(string className, string typeName, string[] requiredClasses)
        {
            if (requiredClasses == null || requiredClasses.Length == 0) return true;
            var candidates = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(className)) { candidates.Add(className); candidates.Add(Bare(className)); }
            if (!string.IsNullOrWhiteSpace(typeName)) { candidates.Add(typeName); candidates.Add(Bare(typeName)); }
            foreach (var want in requiredClasses)
            {
                if (string.IsNullOrWhiteSpace(want)) continue;
                var wantBare = Bare(want);
                foreach (var have in candidates)
                {
                    if (string.Equals(want, have, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(wantBare, have, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>Test-NightlyClassAssigned for fixed required classes, asked once per (ClassName, TypeName).</summary>
    public sealed class ClassAssignment
    {
        private readonly string[] _required;
        private readonly Dictionary<string, bool> _verdicts = new Dictionary<string, bool>(StringComparer.Ordinal);

        public ClassAssignment(string[] requiredClasses) { _required = requiredClasses; }

        public bool Assigned(string className, string typeName)
        {
            className = className ?? ""; typeName = typeName ?? "";
            var key = className + "\0" + typeName;
            if (!_verdicts.TryGetValue(key, out var verdict))
                _verdicts[key] = verdict = NightlyClassNames.Assigned(className, typeName, _required);
            return verdict;
        }

        public bool AssignedNode(object node) =>
            Assigned(PsValue.Str(PsValue.Prop(node, "ClassName")), PsValue.Str(PsValue.Prop(node, "Type")));
    }

    /// <summary>The profile exclusion rows that match one class, merged.</summary>
    public sealed class ExclusionEntry
    {
        public bool All { get; internal set; }
        public HashSet<string> Methods { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The matching rows in policy order (the first declaring row gives reason and owner).</summary>
        public object[] Rows { get; internal set; } = Array.Empty<object>();
    }

    /// <summary>
    /// Test-NightlyProfileNodeExcluded and the profile-aware Get-NightlyDiscoveryDisposition, resolved once per
    /// class: which rows match depends only on a node's ClassName and Type.
    /// </summary>
    public sealed class ProfileExclusionIndex
    {
        private readonly object[] _rows;
        private readonly Dictionary<string, ExclusionEntry> _classes = new Dictionary<string, ExclusionEntry>(StringComparer.Ordinal);

        public ProfileExclusionIndex(object exclusions)
        {
            var rows = new List<object>();
            foreach (var row in PsValue.AsArray(exclusions))
                if (!PsValue.IsWhite(PsValue.Str(PsValue.Prop(row, "Class")))) rows.Add(row);
            _rows = rows.ToArray();
        }

        /// <summary>Null when no row matches the class (no node of it is excluded).</summary>
        public ExclusionEntry Entry(string className, string type)
        {
            className = className ?? ""; type = type ?? "";
            var key = className + "\0" + type;
            if (_classes.TryGetValue(key, out var cached)) return cached;
            var simple = NightlyClassNames.Simple(className);
            if (string.IsNullOrWhiteSpace(simple)) simple = type;
            ExclusionEntry entry = null;
            var matched = new List<object>();
            foreach (var row in _rows)
            {
                var cls = PsValue.Str(PsValue.Prop(row, "Class"));
                if (!string.Equals(cls, simple, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(cls, className, StringComparison.OrdinalIgnoreCase)) continue;
                entry = entry ?? new ExclusionEntry();
                matched.Add(row);
                // @($row.Methods): an absent list is one empty entry, not none.
                var methods = PsValue.AsArray(PsValue.Prop(row, "Methods"));
                if (methods.Length == 0) { entry.All = true; continue; }
                foreach (var m in methods) entry.Methods.Add(PsValue.Str(m));
            }
            if (entry != null) entry.Rows = matched.ToArray();
            _classes[key] = entry;
            return entry;
        }

        public ExclusionEntry EntryOf(object node) =>
            Entry(PsValue.Str(PsValue.Prop(node, "ClassName")), PsValue.Str(PsValue.Prop(node, "Type")));

        public bool Excluded(object node)
        {
            if (node == null) return false;
            var entry = EntryOf(node);
            return entry != null && (entry.All || entry.Methods.Contains(PsValue.Str(PsValue.Prop(node, "Method"))));
        }

        /// <summary>Get-NightlyDiscoveryDisposition -ProfileAware: { Disposition, Reason, Owner, Uid }.</summary>
        public PSObject Disposition(object node)
        {
            var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
            var entry = EntryOf(node);
            if (entry != null)
            {
                var method = PsValue.Str(PsValue.Prop(node, "Method"));
                foreach (var row in entry.Rows)
                {
                    var methods = PsValue.AsArray(PsValue.Prop(row, "Methods"));
                    var hit = methods.Length == 0;
                    if (!hit)
                        foreach (var m in methods)
                            if (string.Equals(PsValue.Str(m), method, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                    if (hit) return NightlyDiscovery.DispositionObject("excluded", PsValue.Str(PsValue.Prop(row, "Reason")), PsValue.Str(PsValue.Prop(row, "Owner")), uid);
                }
            }
            return NightlyDiscovery.DispositionObject("required", "", "", uid);
        }
    }

    /// <summary>Per-node passes over a discovery document, from nightly-coverage.ps1 and nightly-tests-impl.ps1.</summary>
    public static class NightlyDiscovery
    {
        internal static PSObject DispositionObject(string disposition, string reason, string owner, string uid)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Disposition", disposition));
            o.Properties.Add(new PSNoteProperty("Reason", reason));
            o.Properties.Add(new PSNoteProperty("Owner", owner));
            o.Properties.Add(new PSNoteProperty("Uid", uid));
            return o;
        }

        /// <summary>The default (non-profile) Test-NightlyDiscoveryExcluded: Excluded, or an OptIn/Explicit category.</summary>
        public static bool DefaultExcluded(object node)
        {
            if (node == null) return false;
            if (LanguagePrimitives.IsTrue(PsValue.Prop(node, "Excluded"))) return true;
            foreach (var c in PsValue.AsArray(PsValue.Prop(node, "Categories")))
            {
                var category = PsValue.Str(c);
                if (string.Equals(category, "OptIn", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(category, "Explicit", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Get-NightlyProfileExecutionChunks' first pass for one chunk: the assigned nodes bucketed by bare class,
        /// keys case-insensitive and named by their first node, buckets in first-appearance order.
        /// </summary>
        public static PSObject[] BucketByBareClass(object[] nodes, string[] requiredClasses, ProfileExclusionIndex index)
        {
            var assignment = new ClassAssignment(requiredClasses);
            var byBare = new Dictionary<string, PSObject>(StringComparer.OrdinalIgnoreCase);
            var order = new List<PSObject>();
            foreach (var node in nodes ?? Array.Empty<object>())
            {
                var className = PsValue.Str(PsValue.Prop(node, "ClassName"));
                var type = PsValue.Str(PsValue.Prop(node, "Type"));
                if (!assignment.Assigned(className, type)) continue;
                var bare = NightlyClassNames.Bare(className) ?? "";
                if (!byBare.TryGetValue(bare, out var bucket))
                {
                    bucket = new PSObject();
                    bucket.Properties.Add(new PSNoteProperty("Bare", bare));
                    bucket.Properties.Add(new PSNoteProperty("Nodes", new List<object>()));
                    bucket.Properties.Add(new PSNoteProperty("Required", new List<object>()));
                    byBare[bare] = bucket;
                    order.Add(bucket);
                }
                ((List<object>)bucket.Properties["Nodes"].Value).Add(node);
                if (index == null || !index.Excluded(node)) ((List<object>)bucket.Properties["Required"].Value).Add(node);
            }
            return order.ToArray();
        }

        /// <summary>Get-NightlyRequiredDiscoveryUids.</summary>
        public static object[] RequiredUids(object[] nodes, string[] requiredClasses, ProfileExclusionIndex index)
        {
            var assignment = new ClassAssignment(requiredClasses);
            var uids = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes ?? Array.Empty<object>())
            {
                if (index != null ? index.Excluded(n) : DefaultExcluded(n)) continue;
                if (!assignment.AssignedNode(n)) continue;
                var uid = PsValue.Str(PsValue.Prop(n, "Uid"));
                if (string.IsNullOrWhiteSpace(uid) || !seen.Add(uid)) continue;
                uids.Add(uid);
            }
            return uids.ToArray();
        }

        /// <summary>
        /// Get-NightlyDiscoveryCensus' per-node pass: rows split into required and excluded, duplicates kept.
        /// Rows are [ordered] dictionaries with the same keys as the PowerShell function built.
        /// </summary>
        public static PSObject CensusRows(object[] nodes, ProfileExclusionIndex index)
        {
            var required = new List<object>();
            var excluded = new List<object>();
            var duplicates = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes ?? Array.Empty<object>())
            {
                var uid = PsValue.Str(PsValue.Prop(n, "Uid"));
                if (string.IsNullOrWhiteSpace(uid)) continue;
                if (!seen.Add(uid)) { duplicates.Add(uid); continue; }
                string disposition, reason, owner;
                if (index != null)
                {
                    var d = index.Disposition(n);
                    disposition = (string)d.Properties["Disposition"].Value;
                    reason = (string)d.Properties["Reason"].Value;
                    owner = (string)d.Properties["Owner"].Value;
                }
                else if (DefaultExcluded(n)) { disposition = "excluded"; reason = "category-optin"; owner = "nightly-default"; }
                else { disposition = "required"; reason = ""; owner = ""; }
                var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "uid", uid },
                    { "class", PsValue.Str(PsValue.Prop(n, "ClassName")) },
                    { "method", PsValue.Str(PsValue.Prop(n, "Method")) },
                    { "categories", PsValue.AsArray(PsValue.Prop(n, "Categories")) },
                    { "disposition", disposition },
                    { "reason", reason },
                    { "owner", owner },
                };
                if (disposition == "required") required.Add(row); else excluded.Add(row);
            }
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Required", required.ToArray()));
            o.Properties.Add(new PSNoteProperty("Excluded", excluded.ToArray()));
            o.Properties.Add(new PSNoteProperty("Duplicates", duplicates.ToArray()));
            return o;
        }

        /// <summary>
        /// Get-NightlyDiscoveryIndexView's per-node pass: excluded flags, the class lookup (name or bare name of
        /// ClassName or Type, case-insensitive), required UIDs and required bare classes, in discovery order.
        /// </summary>
        public static PSObject IndexView(object[] nodes, ProfileExclusionIndex index)
        {
            nodes = nodes ?? Array.Empty<object>();
            var excluded = new bool[nodes.Length];
            var byClass = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var allRequired = new List<object>();
            var seenUid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var classes = new List<object>();
            var seenClass = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                var className = PsValue.Str(PsValue.Prop(n, "ClassName"));
                var typeName = PsValue.Str(PsValue.Prop(n, "Type"));
                var names = new List<string>(4);
                if (!string.IsNullOrWhiteSpace(className)) { names.Add(className); names.Add(NightlyClassNames.Bare(className)); }
                if (!string.IsNullOrWhiteSpace(typeName)) { names.Add(typeName); names.Add(NightlyClassNames.Bare(typeName)); }
                foreach (var name in names)
                {
                    if (!byClass.TryGetValue(name, out var hitsObject)) byClass[name] = hitsObject = new List<int>();
                    var hits = (List<int>)hitsObject;
                    if (hits.Count == 0 || hits[hits.Count - 1] != i) hits.Add(i);
                }
                if (index != null ? index.Excluded(n) : DefaultExcluded(n)) { excluded[i] = true; continue; }
                var uid = PsValue.Str(PsValue.Prop(n, "Uid"));
                if (!string.IsNullOrWhiteSpace(uid) && seenUid.Add(uid)) allRequired.Add(uid);
                var t = NightlyClassNames.Bare(className);
                if (string.IsNullOrWhiteSpace(t)) t = NightlyClassNames.Bare(typeName);
                if (!string.IsNullOrWhiteSpace(t) && seenClass.Add(t)) classes.Add(t);
            }
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Excluded", excluded));
            o.Properties.Add(new PSNoteProperty("ByClass", byClass));
            o.Properties.Add(new PSNoteProperty("RequiredUids", allRequired.ToArray()));
            o.Properties.Add(new PSNoteProperty("Classes", classes.ToArray()));
            return o;
        }
    }

    /// <summary>
    /// ConvertTo-TestPhaseJsonText / ConvertTo-TestPhaseJsonString from test-phase-evidence.ps1: compact JSON,
    /// hand-built so a one-element array stays an array, byte for byte what the PowerShell functions wrote.
    /// </summary>
    public static class TestPhaseJson
    {
        public static string Text(object value) => Text(value, 0);

        public static string Text(object value, int depth)
        {
            var sb = new StringBuilder();
            Write(sb, value, depth);
            return sb.ToString();
        }

        public static string String(string text)
        {
            var sb = new StringBuilder();
            WriteString(sb, text ?? "");
            return sb.ToString();
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (var ch in text)
            {
                if (ch == '"') sb.Append("\\\"");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(ch);
            }
            sb.Append('"');
        }

        private static void Write(StringBuilder sb, object value, int depth)
        {
            if (depth > 24 || value == null) { sb.Append("null"); return; }
            // -is tests see through the PSObject wrapper; note properties are read from the wrapper itself.
            var wrapper = value as PSObject;
            var raw = wrapper != null ? wrapper.BaseObject : value;
            if (raw is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (raw is byte || raw is short || raw is int || raw is long || raw is decimal || raw is double || raw is float)
            {
                sb.Append(Convert.ToString(raw, CultureInfo.InvariantCulture));
                return;
            }
            if (raw is string s) { WriteString(sb, s); return; }
            if (raw is IDictionary dictionary)
            {
                sb.Append('{');
                var first = true;
                var keys = new List<object>();
                foreach (var key in dictionary.Keys) keys.Add(key);
                foreach (var key in keys)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, PsValue.Str(key));
                    sb.Append(':');
                    Write(sb, dictionary[key], depth + 1);
                }
                sb.Append('}');
                return;
            }
            var properties = PSObject.AsPSObject(value).Properties;
            var hasNote = false;
            foreach (var property in properties)
                if (property.MemberType == PSMemberTypes.NoteProperty) { hasNote = true; break; }
            if (hasNote)
            {
                sb.Append('{');
                var first = true;
                foreach (var property in properties)
                {
                    if (property.MemberType != PSMemberTypes.NoteProperty) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, property.Name);
                    sb.Append(':');
                    Write(sb, property.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            if (raw is IEnumerable enumerable)
            {
                sb.Append('[');
                var first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, PsValue.Str(value));
        }
    }
}
