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
using System.Text.RegularExpressions;

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

        /// <summary>Get-NightlyIdentityBagKey: "simpleclass.method", lower-cased invariantly.</summary>
        public static string BagKey(string className, string typeName, string methodName)
        {
            var simple = Simple(string.IsNullOrWhiteSpace(className) ? typeName : className);
            return ((simple ?? "") + "." + (methodName ?? "")).ToLowerInvariant();
        }

        /// <summary>ConvertTo-NightlyNormalizedOutcome.</summary>
        public static string NormalizedOutcome(string outcome)
        {
            var raw = (outcome ?? "").Trim().ToLowerInvariant();
            switch (raw)
            {
                case "": return "Unknown";
                case "passed": case "pass": return "Passed";
                case "failed": case "fail": case "error": return "Failed";
                case "skipped": return "Skipped";
                case "notexecuted": case "notrunnable": case "pending": return "NotExecuted";
                default: return "Unknown";
            }
        }

        /// <summary>Get-NightlyFileSha256 (after its existence check): lower-case hex SHA-256 of the file.</summary>
        public static string FileSha256(string path)
        {
            using (var stream = System.IO.File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
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
    /// The pinned TUnit 1.44 / MTP 2.2 diagnostic record parser from nightly-coverage.ps1. PowerShell's -match is
    /// case-insensitive and [regex]:: calls are not; each pattern keeps the option of the operator it replaces.
    /// Refusals are returned as the same message text the PowerShell function threw, for it to throw.
    /// </summary>
    public static class NightlyDiagnostics
    {
        private const RegexOptions Match = RegexOptions.IgnoreCase | RegexOptions.Compiled; // PowerShell -match / -notmatch
        private const RegexOptions Exact = RegexOptions.Compiled; // [regex]:: calls and -cmatch: case-sensitive
        public const string TypeNameClass = @"[A-Za-z0-9_.+\x60()<>,\[\] \u00B7-]";

        private static readonly Regex Envelope = new Regex(@"TestNodeUpdateMessage \{[^\r\n]*?TestNode = TestNode \{ Uid = TestNodeUid", Exact);
        private static readonly Regex LineSplit = new Regex(@"(?<=\n)", Exact);
        private static readonly Regex NodeStart = new Regex(@"^(?:[^\r\n]*?TestNodeUpdateMessage \{[^\r\n]*?TestNode = |\s*)TestNode \{ Uid = TestNodeUid", Match);
        private static readonly Regex HeaderStart = new Regex(@"^\d{4}-\d{2}-\d{2}T\S+\s+\S+\s+(?:TRACE|DEBUG|INFORMATION|WARNING|ERROR|CRITICAL)\b", Match);
        private static readonly Regex VersionStart = new Regex(@"^\s*version:\s+", Match);
        private static readonly Regex StateEnvelope = new Regex(@"(?s)TestNode \{ Uid = TestNodeUid \{ Value = [^}]* \}, DisplayName = .*?, Properties = \[ ", Exact);
        private static readonly Regex Transition = new Regex(
            @"TestMethodIdentifierProperty \{ AssemblyFullName = [^{}\r\n]+, Namespace = [A-Za-z0-9_.]*, TypeName = " + TypeNameClass
            + @"*?, MethodName = [A-Za-z0-9_]*, MethodArity = \d+, ParameterTypeFullNames = \[[^{}\r\n]*\], ReturnTypeFullName = [^{}\r\n]+ \}, "
            + @"(?:TestFileLocationProperty \{ FilePath = [^\r\n]*?, LineSpan = LinePositionSpan \{ Start = LinePosition \{ Line = \d+, Column = \d+ \}, End = LinePosition \{ Line = \d+, Column = \d+ \} \} \}, )?"
            + @"(?<name>[A-Za-z]+)TestNodeStateProperty \{ Explanation = ", Exact);
        private static readonly Regex Uid = new Regex(@"TestNodeUid \{ Value = (?<uid>[^}]+?) \}", Match);
        private static readonly Regex TypeName = new Regex(@"(?<![A-Za-z])TypeName = (?<type>" + TypeNameClass + @"+?), MethodName = ", Match);
        private static readonly Regex MethodName = new Regex(@"(?<![A-Za-z])MethodName = (?<method>[A-Za-z0-9_]+)", Match);
        private static readonly Regex Namespace = new Regex(@"(?<![A-Za-z])Namespace = (?<ns>[A-Za-z0-9_\.]+)", Match);
        private static readonly Regex AssemblyName = new Regex(@"AssemblyFullName = (?<asm>[^,]+)", Match);
        private static readonly Regex Category = new Regex(@"TestMetadataProperty \{ Key = (?<key>[A-Za-z0-9_]+)", Exact);

        /// <summary>Get-NightlyDiagnosticRecords: one string per TestNode record, physical line endings kept.</summary>
        public static string[] Records(string text)
        {
            var records = new List<string>();
            var record = new StringBuilder();
            foreach (var line in LineSplit.Split(text ?? ""))
            {
                var envelopeHits = Envelope.Matches(line);
                if (envelopeHits.Count >= 2)
                {
                    if (record.Length > 0) { records.Add(record.ToString()); record.Clear(); }
                    var start = 0;
                    for (var hitIndex = 1; hitIndex < envelopeHits.Count; hitIndex++)
                    {
                        var at = envelopeHits[hitIndex].Index;
                        if (at > start)
                        {
                            var piece = line.Substring(start, at - start);
                            if (piece.Length > 0) records.Add(piece);
                        }
                        start = at;
                    }
                    if (start < line.Length) record.Append(line.Substring(start));
                    continue;
                }
                var nodeStart = NodeStart.IsMatch(line);
                var headerStart = HeaderStart.IsMatch(line) || VersionStart.IsMatch(line);
                if ((nodeStart || headerStart) && record.Length > 0) { records.Add(record.ToString()); record.Clear(); }
                if (nodeStart || (record.Length > 0 && !headerStart)) record.Append(line);
            }
            if (record.Length > 0) records.Add(record.ToString());
            return records.ToArray();
        }

        /// <summary>Get-NightlyDiagnosticStateProperty: { Name, ExplanationStart }, Name '' when absent or ambiguous.</summary>
        public static PSObject StateProperty(string record)
        {
            record = record ?? "";
            var envelope = StateEnvelope.Match(record);
            if (envelope.Success)
            {
                var hit = Transition.Match(record, envelope.Index + envelope.Length);
                if (hit.Success && !hit.NextMatch().Success)
                    return StateObject(hit.Groups["name"].Value, hit.Index + hit.Length);
            }
            return StateObject("", -1);
        }

        private static PSObject StateObject(string name, int explanationStart)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Name", name));
            o.Properties.Add(new PSNoteProperty("ExplanationStart", explanationStart));
            return o;
        }

        /// <summary>Get-NightlyDiagnosticStateName.</summary>
        public static string StateName(string propertyName)
        {
            switch (propertyName)
            {
                case "Passed": return "Passed";
                case "Skipped": return "Skipped";
                case "Discovered": return "Discovered";
                case "InProgress": return "InProgress";
                case "Failed": case "Error": case "Timeout": case "Cancelled": return "Failed";
                default: return "";
            }
        }

        /// <summary>Test-NightlyDiagnosticTypeNameComplete.</summary>
        public static bool TypeNameComplete(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var openAt = name.IndexOf('(');
            var closeAt = name.IndexOf(')');
            var commaAt = name.IndexOf(',');
            if (openAt < 0 && closeAt < 0) return commaAt < 0;
            if (openAt <= 0) return false;
            if (!name.EndsWith(")", StringComparison.Ordinal)) return false;
            var depth = 0;
            foreach (var ch in name)
            {
                if (ch == '(') depth++;
                else if (ch == ')') { depth--; if (depth < 0) return false; }
                else if (ch == ',' && depth == 0) return false;
            }
            return depth == 0;
        }

        /// <summary>
        /// ConvertFrom-NightlyDiagnosticRecord without the failure message (which stays in PowerShell):
        /// { Node, Error }, Error being the refusal text the PowerShell function threw, in the same order.
        /// </summary>
        public static PSObject ConvertRecord(string record, string state)
        {
            record = record ?? ""; state = state ?? "";
            var result = new PSObject();
            var uidMatch = Uid.Match(record);
            if (!uidMatch.Success) return Refusal(result, "truncated diagnostic record: missing uid");
            var uid = uidMatch.Groups["uid"].Value.Trim();
            var typeName = ""; var method = ""; var ns = ""; var assembly = "";
            var m = TypeName.Match(record); if (m.Success) typeName = m.Groups["type"].Value;
            m = MethodName.Match(record); if (m.Success) method = m.Groups["method"].Value;
            m = Namespace.Match(record); if (m.Success) ns = m.Groups["ns"].Value;
            m = AssemblyName.Match(record); if (m.Success) assembly = m.Groups["asm"].Value.Trim();
            if (string.IsNullOrWhiteSpace(uid)) return Refusal(result, "truncated diagnostic record: missing uid");
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(method)
                || !TypeNameComplete(typeName))
                return Refusal(result, "truncated diagnostic record uid=" + uid);
            var categories = new List<object>();
            foreach (System.Text.RegularExpressions.Match c in Category.Matches(record)) categories.Add(c.Groups["key"].Value);
            var excluded = false;
            foreach (var c in categories)
            {
                var category = (string)c;
                if (string.Equals(category, "OptIn", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(category, "Explicit", StringComparison.OrdinalIgnoreCase)) excluded = true;
            }
            var node = new PSObject();
            node.Properties.Add(new PSNoteProperty("Uid", uid));
            node.Properties.Add(new PSNoteProperty("Assembly", assembly));
            node.Properties.Add(new PSNoteProperty("Namespace", ns));
            node.Properties.Add(new PSNoteProperty("Type", typeName));
            node.Properties.Add(new PSNoteProperty("Method", method));
            node.Properties.Add(new PSNoteProperty("Signature", ""));
            node.Properties.Add(new PSNoteProperty("State", state));
            node.Properties.Add(new PSNoteProperty("Outcome", state));
            node.Properties.Add(new PSNoteProperty("ClassName", string.IsNullOrWhiteSpace(ns) ? typeName : ns + "." + typeName));
            node.Properties.Add(new PSNoteProperty("Excluded", excluded));
            node.Properties.Add(new PSNoteProperty("Categories", categories.ToArray()));
            result.Properties.Add(new PSNoteProperty("Node", node));
            result.Properties.Add(new PSNoteProperty("Error", ""));
            return result;
        }

        private static readonly Regex DiscoveredHeader = new Regex(@"(?<![A-Za-z])DiscoveredTestNodeStateProperty \{", Exact); // -cmatch
        private static readonly Regex DiscoveredMarker = new Regex(@"DiscoveredTestNodeStateProperty \{", Exact);

        /// <summary>The raw discovered-marker count ConvertFrom-NightlyDiagnosticLog checks the parse against.</summary>
        public static int DiscoveredMarkerCount(string text) => DiscoveredMarker.Matches(text ?? "").Count;

        private static readonly Regex FramedNode = new Regex(@"TestNode \{ Uid = TestNodeUid", Match);

        /// <summary>
        /// The record loop of ConvertFrom-NightlyDiagnosticLog (pinned text format) for kind discovery/execution:
        /// { Nodes, Failed, Recognized, Error }. Error is the first refusal the PowerShell loop threw, in the same
        /// record order. Failed lists { Node, Record, StateProperty } for the caller to attach failure messages,
        /// which never refuse.
        /// </summary>
        public static PSObject ParseRecords(string text, string kind, string path)
        {
            var result = new PSObject();
            var nodes = new List<object>();
            var failed = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recognized = false;
            var discovery = string.Equals(kind, "discovery", StringComparison.OrdinalIgnoreCase);
            var execution = string.Equals(kind, "execution", StringComparison.OrdinalIgnoreCase);
            string error = "";
            foreach (var record in Records(text))
            {
                var stateProperty = StateProperty(record);
                var state = StateName((string)stateProperty.Properties["Name"].Value);
                if (string.IsNullOrWhiteSpace(state))
                {
                    if (discovery)
                    {
                        if (DiscoveredHeader.IsMatch(record))
                        {
                            var refused = (string)ConvertRecord(record, "").Properties["Error"].Value;
                            if (refused.Length > 0) { error = refused; break; }
                        }
                        error = "malformed discovery record: missing state " + path;
                        break;
                    }
                    if (FramedNode.IsMatch(record)) state = "Failed";
                    else continue;
                }
                recognized = true;
                if (discovery) { if (!string.Equals(state, "Discovered", StringComparison.OrdinalIgnoreCase)) continue; }
                else if (execution)
                {
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state, "Discovered", StringComparison.OrdinalIgnoreCase)) continue;
                }
                var converted = ConvertRecord(record, state);
                var convertError = (string)converted.Properties["Error"].Value;
                if (convertError.Length > 0) { error = convertError; break; }
                var node = (PSObject)converted.Properties["Node"].Value;
                if (string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var entry = new PSObject();
                    entry.Properties.Add(new PSNoteProperty("Node", node));
                    entry.Properties.Add(new PSNoteProperty("Record", record));
                    entry.Properties.Add(new PSNoteProperty("StateProperty", stateProperty));
                    failed.Add(entry);
                }
                var uid = (string)node.Properties["Uid"].Value;
                if (!seen.Add(uid)) { error = "duplicate " + kind + " uid " + uid; break; }
                nodes.Add(node);
            }
            result.Properties.Add(new PSNoteProperty("Nodes", nodes.ToArray()));
            result.Properties.Add(new PSNoteProperty("Failed", failed.ToArray()));
            result.Properties.Add(new PSNoteProperty("Recognized", recognized));
            result.Properties.Add(new PSNoteProperty("Error", error));
            return result;
        }

        private static PSObject Refusal(PSObject result, string error)
        {
            result.Properties.Add(new PSNoteProperty("Node", null));
            result.Properties.Add(new PSNoteProperty("Error", error));
            return result;
        }
    }

    /// <summary>
    /// Whole per-batch loops of the native suite verdict (nightly-coverage.ps1): one call per batch instead of
    /// one PowerShell call per result row. Collections follow PowerShell's: a $null row list is one $null row
    /// (as the pipeline passes it), and @{} hashtables use OrdinalIgnoreCase, so they enumerate in the same order.
    /// </summary>
    public static class NightlyVerdicts
    {
        private static object[] Rows(object rows) => rows == null ? new object[] { null } : PsValue.AsArray(rows);

        private static PSObject Result(params (string Name, object Value)[] properties)
        {
            var o = new PSObject();
            foreach (var (name, value) in properties) o.Properties.Add(new PSNoteProperty(name, value));
            return o;
        }

        /// <summary>
        /// The outcome filters of ConvertTo-NightlyCoverageVerdict in one pass: Failed, Skipped, NotExecuted and
        /// Unknown rows in row order, and the Passed count.
        /// </summary>
        public static PSObject ClassifyOutcomes(object terminalRows)
        {
            var failed = new List<object>(); var skipped = new List<object>();
            var unexecuted = new List<object>(); var unknown = new List<object>();
            var passed = 0;
            foreach (var row in Rows(terminalRows))
            {
                switch (NightlyClassNames.NormalizedOutcome(PsValue.Str(PsValue.Prop(row, "Outcome"))))
                {
                    case "Failed": failed.Add(row); break;
                    case "Skipped": skipped.Add(row); break;
                    case "NotExecuted": unexecuted.Add(row); break;
                    case "Unknown": unknown.Add(row); break;
                    case "Passed": passed++; break;
                }
            }
            return Result(("Failed", failed.ToArray()), ("Skipped", skipped.ToArray()),
                ("NotExecuted", unexecuted.ToArray()), ("Unknown", unknown.ToArray()), ("Passed", passed));
        }

        /// <summary>Test-NightlyExpandedRowsPresent: every required UID has an executed terminal row.</summary>
        public static PSObject ExpandedRowsPresent(object terminalRows, string[] requiredUids)
        {
            var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows(terminalRows))
            {
                var outcome = NightlyClassNames.NormalizedOutcome(PsValue.Str(PsValue.Prop(row, "Outcome")));
                if (outcome != "Passed" && outcome != "Failed" && outcome != "Skipped") continue;
                var uid = PsValue.Str(PsValue.Prop(row, "Uid"));
                if (!string.IsNullOrWhiteSpace(uid)) executed.Add(uid);
            }
            var missing = new List<object>();
            foreach (var uid in requiredUids ?? new string[] { null })
            {
                var key = uid ?? "";
                if (!string.IsNullOrWhiteSpace(key) && !executed.Contains(key)) missing.Add(key);
            }
            return Result(("Ok", missing.Count == 0), ("Missing", missing.ToArray()));
        }

        /// <summary>
        /// Test-NightlyTrxDiagnosticCrossCheck: the TRX rows and the diagnostic's terminal nodes hold the same
        /// multiset of (simple class, method) keys for the batch's classes; rows outside them are extra classes.
        /// </summary>
        public static PSObject CrossCheck(object trxRows, object terminalNodes, string[] requiredClasses)
        {
            var assignment = new ClassAssignment(requiredClasses);
            var requiredTruthy = LanguagePrimitives.IsTrue(requiredClasses) && requiredClasses.Length > 0;
            var trxBag = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var diagBag = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var extra = new List<object>();
            foreach (var r in Rows(trxRows))
            {
                var className = PsValue.Str(PsValue.Prop(r, "ClassName"));
                var methodName = PsValue.Str(PsValue.Prop(r, "MethodName"));
                if (!assignment.Assigned(className, ""))
                {
                    if (requiredTruthy) extra.Add(className + "." + methodName);
                    continue;
                }
                var key = NightlyClassNames.BagKey(className, "", methodName);
                trxBag[key] = (trxBag.ContainsKey(key) ? (int)trxBag[key] : 0) + 1;
            }
            foreach (var n in Rows(terminalNodes))
            {
                var className = PsValue.Str(PsValue.Prop(n, "ClassName"));
                var typeName = PsValue.Str(PsValue.Prop(n, "Type"));
                if (!assignment.Assigned(className, typeName)) continue;
                var method = PsValue.Str(PsValue.Prop(n, "Method"));
                if (string.IsNullOrWhiteSpace(method)) method = PsValue.Str(PsValue.Prop(n, "MethodName"));
                var key = NightlyClassNames.BagKey(className, typeName, method);
                diagBag[key] = (diagBag.ContainsKey(key) ? (int)diagBag[key] : 0) + 1;
            }
            var allKeys = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var k in new ArrayList(trxBag.Keys)) allKeys[k] = true;
            foreach (var k in new ArrayList(diagBag.Keys)) allKeys[k] = true;
            var mismatch = new List<object>();
            foreach (var k in new ArrayList(allKeys.Keys))
            {
                var t = trxBag.ContainsKey(k) ? (int)trxBag[k] : 0;
                var d = diagBag.ContainsKey(k) ? (int)diagBag[k] : 0;
                if (t != d) mismatch.Add(string.Format(CultureInfo.InvariantCulture, "{0} trx={1} diag={2}", k, t, d));
            }
            return Result(("Ok", mismatch.Count == 0 && extra.Count == 0), ("Mismatch", mismatch.ToArray()), ("ExtraClasses", extra.ToArray()));
        }

        /// <summary>Test-NightlyRequiredClassesPresent: each required class (or its bare name) has a terminal row.</summary>
        public static PSObject RequiredClassesPresent(object terminalRows, string[] requiredClasses)
        {
            if (requiredClasses == null || requiredClasses.Length == 0) return Result(("Ok", true), ("Missing", Array.Empty<object>()));
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Rows(terminalRows))
            {
                var cn = PsValue.Str(PsValue.Prop(r, "ClassName"));
                if (string.IsNullOrWhiteSpace(cn)) continue;
                present.Add(cn);
                present.Add(NightlyClassNames.Bare(cn));
            }
            var missing = new List<object>();
            foreach (var name in requiredClasses)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!present.Contains(name) && !present.Contains(NightlyClassNames.Bare(name))) missing.Add(name);
            }
            return Result(("Ok", missing.Count == 0), ("Missing", missing.ToArray()));
        }

        /// <summary>
        /// Get-NightlyDiscoveryIndexRequiredUids for a non-empty class list: the view's required UIDs of those
        /// classes, in discovery order, without duplicates.
        /// </summary>
        public static object[] IndexRequiredUids(IDictionary byClass, bool[] excluded, object nodes, string[] requiredClasses)
        {
            var nodeArray = PsValue.AsArray(nodes);
            var selected = new SortedSet<int>();
            foreach (var want in requiredClasses)
            {
                if (string.IsNullOrWhiteSpace(want)) continue;
                foreach (var name in new[] { want, NightlyClassNames.Bare(want) })
                    if (byClass.Contains(name) && byClass[name] is IEnumerable hits)
                        foreach (var i in hits) selected.Add((int)i);
            }
            var uids = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in selected)
            {
                if (excluded[i]) continue;
                var uid = PsValue.Str(PsValue.Prop(nodeArray[i], "Uid"));
                if (string.IsNullOrWhiteSpace(uid) || !seen.Add(uid)) continue;
                uids.Add(uid);
            }
            return uids.ToArray();
        }
    }

    /// <summary>Repository scans from nightly-policy.ps1.</summary>
    /// <summary>Get-NightlyParentProcess on Windows: the parent id Win32_Process reports, without a CIM query.</summary>
    public static class NightlyProcesses
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BasicInformation
        {
            public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
        }

        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, ref BasicInformation information, int length, out int returned);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>The parent process id, or -1 when it cannot be read (the caller falls back to CIM).</summary>
        public static int ParentProcessId(int processId)
        {
            if (!OperatingSystem.IsWindows()) return -1;
            IntPtr handle = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle == IntPtr.Zero) return -1;
            try
            {
                var info = new BasicInformation();
                int status = NtQueryInformationProcess(handle, 0, ref info, System.Runtime.InteropServices.Marshal.SizeOf(typeof(BasicInformation)), out _);
                if (status != 0) return -1;
                return (int)info.InheritedFromUniqueProcessId.ToInt64();
            }
            finally { CloseHandle(handle); }
        }
    }

    public static class NightlyRepository
    {
        /// <summary>
        /// The *.csproj files Get-NightlyIsTestProjectPaths considers: what Get-ChildItem -Recurse -File lists
        /// (no hidden items, directory links not followed), minus any whose path below the root has a segment
        /// equal to bin or obj (case-insensitively) or starting with bin- (ordinally). Excluded directories are
        /// pruned rather than walked; unreadable directories are skipped, as -ErrorAction SilentlyContinue did.
        /// </summary>
        public static string[] CandidateProjectFiles(string root) => CandidateProjectFiles(root, null);

        /// <summary>
        /// Every *.csproj under root outside bin/obj/bin-*, hidden entries, links and the pruned directories
        /// (full paths ending in a separator: the git-ignored ones), sorted ordinal.
        /// </summary>
        public static string[] CandidateProjectFiles(string root, string[] prunedDirectories)
        {
            var comparer = System.IO.Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var pruned = new HashSet<string>(prunedDirectories ?? new string[0], comparer);
            // One directory level at a time across threads (the tree holds tens of thousands of directories).
            var found = new System.Collections.Concurrent.ConcurrentBag<string>();
            var level = new List<string> { root };
            while (level.Count > 0)
            {
                var next = new System.Collections.Concurrent.ConcurrentBag<string>();
                System.Threading.Tasks.Parallel.ForEach(level, directory =>
                {
                    try
                    {
                        var info = new System.IO.DirectoryInfo(directory);
                        foreach (var file in info.EnumerateFiles("*.csproj"))
                            if ((file.Attributes & System.IO.FileAttributes.Hidden) == 0 && !Excluded(file.Name)) found.Add(file.FullName);
                        foreach (var child in info.EnumerateDirectories())
                        {
                            var attributes = child.Attributes;
                            if ((attributes & System.IO.FileAttributes.Hidden) != 0 || (attributes & System.IO.FileAttributes.ReparsePoint) != 0) continue;
                            if (Excluded(child.Name)) continue;
                            if (pruned.Count > 0 && pruned.Contains(child.FullName + System.IO.Path.DirectorySeparatorChar)) continue;
                            next.Add(child.FullName);
                        }
                    }
                    catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { }
                });
                level = new List<string>(next);
            }
            var result = found.ToArray();
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static bool Excluded(string segment) =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || segment.StartsWith("bin-", StringComparison.Ordinal);
    }

    /// <summary>Launch packing helpers from nightly-tests-impl.ps1 that ran once per discovered test or class pair.</summary>
    public static class NightlyLaunchPlan
    {
        /// <summary>Get-NightlyBareTypeParts: { Bare, Namespace, Type } of a class name, or null.</summary>
        public static PSObject BareTypeParts(string className)
        {
            var bare = NightlyClassNames.Bare(className);
            if (string.IsNullOrWhiteSpace(bare)) return null;
            var dot = bare.LastIndexOf('.');
            if (dot < 1) return null;
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Bare", bare));
            o.Properties.Add(new PSNoteProperty("Namespace", bare.Substring(0, dot)));
            o.Properties.Add(new PSNoteProperty("Type", bare.Substring(dot + 1)));
            return o;
        }

        /// <summary>
        /// Get-NightlyLaunchClassCost: { NodeCount, Seconds, MeasuredPerNode, Heavy } with the per-node model of
        /// Get-NightlyLaunchNodeSeconds, summed in node order (so the double sum is the PowerShell one).
        /// </summary>
        public static PSObject ClassCost(object nodes, string typeName, string bare, IDictionary costTable,
            double nodeSeconds, double slowNodeSeconds, double laneSlowNodeSeconds, double heavyFactor)
        {
            var heavy = !string.IsNullOrEmpty(typeName) && (typeName.IndexOf("Land", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("Delivery", StringComparison.Ordinal) >= 0 || typeName.IndexOf("Check", StringComparison.Ordinal) >= 0);
            var lane = !string.IsNullOrEmpty(typeName) && (typeName.IndexOf("Land", StringComparison.Ordinal) >= 0
                || typeName.IndexOf("Delivery", StringComparison.Ordinal) >= 0);
            object measured = null;
            if (!string.IsNullOrEmpty(bare) && costTable != null && costTable.Contains(bare))
                measured = (double)LanguagePrimitives.ConvertTo(costTable[bare], typeof(double), CultureInfo.InvariantCulture);
            double NodeCost(object node)
            {
                if (measured != null) return (double)measured;
                var unit = nodeSeconds;
                foreach (var category in PsValue.AsArray(PsValue.Prop(node, "Categories")))
                    if (string.Equals(PsValue.Str(category), "Slow", StringComparison.Ordinal)) { unit = lane ? laneSlowNodeSeconds : slowNodeSeconds; break; }
                if (heavy) unit = unit * heavyFactor;
                return unit;
            }
            var count = 0;
            var seconds = 0.0;
            foreach (var node in PsValue.AsArray(nodes))
            {
                if (node == null) continue;
                count++;
                seconds += NodeCost(node);
            }
            if (count == 0) { count = 1; seconds = NodeCost(null); }
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("NodeCount", count));
            o.Properties.Add(new PSNoteProperty("Seconds", seconds));
            o.Properties.Add(new PSNoteProperty("MeasuredPerNode", measured));
            o.Properties.Add(new PSNoteProperty("Heavy", heavy));
            return o;
        }

        private static readonly Regex GroupFilterShape = new Regex(@"^/\*/([A-Za-z_][A-Za-z0-9_.]*)/(\([A-Za-z_][A-Za-z0-9_]*\*\)(\|\([A-Za-z_][A-Za-z0-9_]*\*\))*)/\*$");
        private static readonly Regex GroupFilterOperand = new Regex(@"\(([A-Za-z_][A-Za-z0-9_]*)\*\)");

        /// <summary>
        /// Test-NightlyClassGroupFilter: the filter names exactly the launch's classes, one operand each, in one
        /// namespace, and no operand is a prefix of another (a prefix operand would also select its extensions).
        /// </summary>
        public static bool ClassGroupFilter(string[] classes, string groupFilter)
        {
            if (string.IsNullOrWhiteSpace(groupFilter)) return false;
            var matched = GroupFilterShape.Match(groupFilter);
            if (!matched.Success) return false;
            var ns = matched.Groups[1].Value;
            var operands = new List<string>();
            foreach (Match op in GroupFilterOperand.Matches(matched.Groups[2].Value)) operands.Add(op.Groups[1].Value);
            var classList = classes ?? new string[] { null };
            if (operands.Count < 2 || classList.Length != operands.Count) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in operands) if (!seen.Add(op)) return false;
            for (var i = 0; i < operands.Count; i++)
                for (var j = i + 1; j < operands.Count; j++)
                    if (IsPrefix(operands[i], operands[j]) || IsPrefix(operands[j], operands[i])) return false;
            var have = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in classList)
            {
                var parts = BareTypeParts(c ?? "");
                if (parts == null || !string.Equals((string)parts.Properties["Namespace"].Value, ns, StringComparison.Ordinal)) return false;
                var type = (string)parts.Properties["Type"].Value;
                have[type] = (have.TryGetValue(type, out var n) ? n : 0) + 1;
            }
            foreach (var op in operands) if (!have.TryGetValue(op, out var n) || n != 1) return false;
            return true;
        }

        /// <summary>Test-NightlyTypeNamePrefixes: <paramref name="left"/> is a strict ordinal prefix of <paramref name="right"/>.</summary>
        private static bool IsPrefix(string left, string right) =>
            !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && left.Length < right.Length
            && right.StartsWith(left, StringComparison.Ordinal);

        /// <summary>
        /// ConvertTo-NightlyProfileLaunchGroups' two passes over the discovery: { NodesByBare, Universe } - nodes by
        /// bare class (ArrayList values), and each namespace's type names in node order (List[string] values).
        /// Both are @{}-style hashtables (OrdinalIgnoreCase).
        /// </summary>
        public static PSObject IndexNodes(object discoveryNodes)
        {
            var nodesByBare = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var universe = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var node in PsValue.AsArray(discoveryNodes))
            {
                if (node == null) continue;
                var className = PsValue.Str(PsValue.Prop(node, "ClassName"));
                var bare = NightlyClassNames.Bare(className);
                if (!string.IsNullOrWhiteSpace(bare))
                {
                    if (!(nodesByBare[bare] is ArrayList list)) nodesByBare[bare] = list = new ArrayList();
                    list.Add(node);
                }
                var parts = BareTypeParts(className);
                if (parts == null) continue;
                var ns = (string)parts.Properties["Namespace"].Value;
                if (!(universe[ns] is List<string> types)) universe[ns] = types = new List<string>();
                types.Add((string)parts.Properties["Type"].Value);
            }
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("NodesByBare", nodesByBare));
            o.Properties.Add(new PSNoteProperty("Universe", universe));
            return o;
        }

        /// <summary>Times <see cref="IndexBoundaryDiscovery"/> has built a boundary index in this process.</summary>
        public static int BoundaryIndexBuilds;

        /// <summary>
        /// One pass over discovery for a boundary pack: per bare class, the original nodes in discovery order,
        /// full-class cost, and the namespace universe IndexNodes would have built. JsonByUid is the pwsh 7
        /// ConvertTo-Json -Compress sort key of [className, method, uid]. Error is a boundary-pack message, or null.
        /// </summary>
        public static PSObject IndexBoundaryDiscovery(object[] discoveryNodes, IDictionary costTable,
            double nodeSeconds, double slowNodeSeconds, double laneSlowNodeSeconds, double heavyFactor)
        {
            BoundaryIndexBuilds++;
            var classes = new Hashtable(StringComparer.Ordinal);
            var classCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var methodCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenUid = new HashSet<string>(StringComparer.Ordinal);
            var universe = new Hashtable(StringComparer.OrdinalIgnoreCase);
            var jsonByUid = new Hashtable(StringComparer.Ordinal);
            var builders = new Dictionary<string, ClassNodes>(StringComparer.Ordinal);
            var order = new List<string>();
            string error = null;
            var nodes = discoveryNodes ?? new object[0];
            foreach (var node in nodes)
            {
                var className = node == null ? "" : PsValue.Str(PsValue.Prop(node, "ClassName"));
                var method = node == null ? "" : PsValue.Str(PsValue.Prop(node, "Method"));
                var uid = node == null ? "" : PsValue.Str(PsValue.Prop(node, "Uid"));
                var parts = BareTypeParts(className);
                if (parts == null || string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(uid))
                {
                    error = "boundary-pack: incomplete discovery identity";
                    break;
                }
                var bare = (string)parts.Properties["Bare"].Value;
                var ns = (string)parts.Properties["Namespace"].Value;
                var type = (string)parts.Properties["Type"].Value;
                if (!seenUid.Add(uid)) { error = "boundary-pack: duplicate discovery UID"; break; }
                if (classCase.TryGetValue(bare, out var existingBare) && !string.Equals(existingBare, bare, StringComparison.Ordinal))
                {
                    error = "boundary-pack: case-colliding class identity";
                    break;
                }
                classCase[bare] = bare;
                var methodKey = bare + "/" + method;
                if (methodCase.TryGetValue(methodKey, out var existingMethod) && !string.Equals(existingMethod, methodKey, StringComparison.Ordinal))
                {
                    error = "boundary-pack: case-colliding method identity";
                    break;
                }
                methodCase[methodKey] = methodKey;
                if (!builders.TryGetValue(bare, out var builder))
                {
                    builder = new ClassNodes { Bare = bare, Namespace = ns, Type = type };
                    builders[bare] = builder;
                    order.Add(bare);
                }
                builder.Nodes.Add(node);
                jsonByUid[uid] = CaseSortKey(className, method, uid);
                if (!(universe[ns] is List<string> types)) universe[ns] = types = new List<string>();
                types.Add(type);
            }
            if (error == null)
            {
                foreach (var bare in order)
                {
                    var builder = builders[bare];
                    var classNodes = builder.Nodes.ToArray();
                    var cost = ClassCost(classNodes, builder.Type, builder.Bare, costTable,
                        nodeSeconds, slowNodeSeconds, laneSlowNodeSeconds, heavyFactor);
                    var rec = new PSObject();
                    rec.Properties.Add(new PSNoteProperty("Bare", builder.Bare));
                    rec.Properties.Add(new PSNoteProperty("Namespace", builder.Namespace));
                    rec.Properties.Add(new PSNoteProperty("Type", builder.Type));
                    rec.Properties.Add(new PSNoteProperty("Nodes", classNodes));
                    rec.Properties.Add(new PSNoteProperty("NodeCount", cost.Properties["NodeCount"].Value));
                    rec.Properties.Add(new PSNoteProperty("Seconds", cost.Properties["Seconds"].Value));
                    classes[bare] = rec;
                }
            }
            var universeOut = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in universe)
                universeOut[entry.Key] = ((List<string>)entry.Value).ToArray();
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Error", error));
            result.Properties.Add(new PSNoteProperty("Classes", classes));
            result.Properties.Add(new PSNoteProperty("Universe", universeOut));
            result.Properties.Add(new PSNoteProperty("JsonByUid", jsonByUid));
            return result;
        }

        /// <summary>
        /// Case membership for one packed chunk. Non-empty chunk nodes win; otherwise the class records' nodes
        /// are taken in class order. Cases are ordered by <see cref="CaseSortKey"/>. Identities stay in member order.
        /// </summary>
        public static PSObject ChunkMembershipForChunk(object chunkNodes, object classNames, object classes,
            object jsonByUid, HashSet<string> expected, HashSet<string> seen)
        {
            var members = new List<object>();
            var direct = PsValue.AsArray(chunkNodes);
            var directCount = 0;
            foreach (var node in direct) if (node != null) directCount++;
            if (directCount > 0)
            {
                foreach (var node in direct) if (node != null) members.Add(node);
            }
            else
            {
                var table = classes as Hashtable;
                foreach (var nameObj in PsValue.AsArray(classNames))
                {
                    var bare = PsValue.Str(nameObj);
                    var rec = table == null ? null : table[bare] as PSObject;
                    var nodes = rec == null ? null : rec.Properties["Nodes"].Value;
                    if (nodes == null) return MembershipResult("boundary-pack: case partition mismatch", null, null);
                    foreach (var node in PsValue.AsArray(nodes)) if (node != null) members.Add(node);
                }
            }
            var map = jsonByUid as Hashtable;
            var identities = new object[members.Count];
            var keyed = new List<CaseRow>(members.Count);
            for (var i = 0; i < members.Count; i++)
            {
                var node = members[i];
                var className = PsValue.Str(PsValue.Prop(node, "ClassName"));
                var method = PsValue.Str(PsValue.Prop(node, "Method"));
                var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
                if (expected != null && (!expected.Contains(uid) || !seen.Add(uid)))
                    return MembershipResult("boundary-pack: case partition mismatch", null, null);
                var bare = NightlyClassNames.Bare(className);
                var ident = new Hashtable
                {
                    { "identityKey", bare + "/" + method },
                    { "caseKey", uid }
                };
                identities[i] = ident;
                var sortKey = map != null && map.ContainsKey(uid) ? (string)map[uid] : CaseSortKey(className, method, uid);
                keyed.Add(new CaseRow { Key = sortKey, ClassName = className, Method = method, Uid = uid });
            }
            keyed.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var cases = new object[keyed.Count];
            for (var i = 0; i < keyed.Count; i++)
            {
                var row = keyed[i];
                var item = new PSObject();
                item.Properties.Add(new PSNoteProperty("className", row.ClassName));
                item.Properties.Add(new PSNoteProperty("method", row.Method));
                item.Properties.Add(new PSNoteProperty("uid", row.Uid));
                cases[i] = item;
            }
            return MembershipResult(null, cases, identities);
        }

        /// <summary>pwsh 7 ConvertTo-Json -Compress of a three-string array. Slash is not escaped; U+2028 and U+2029 are.</summary>
        public static string CaseSortKey(string className, string method, string uid)
        {
            var sb = new StringBuilder((className == null ? 0 : className.Length) + (method == null ? 0 : method.Length) + (uid == null ? 0 : uid.Length) + 16);
            sb.Append('[');
            AppendJsonString(sb, className);
            sb.Append(',');
            AppendJsonString(sb, method);
            sb.Append(',');
            AppendJsonString(sb, uid);
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
            {
                foreach (var ch in value)
                {
                    switch (ch)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\u2028': sb.Append("\\u2028"); break;
                        case '\u2029': sb.Append("\\u2029"); break;
                        default:
                            if (ch < 0x20)
                            {
                                sb.Append("\\u");
                                sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else sb.Append(ch);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private static PSObject MembershipResult(string error, object cases, object identities)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Error", error));
            o.Properties.Add(new PSNoteProperty("Cases", cases ?? new object[0]));
            o.Properties.Add(new PSNoteProperty("Identities", identities ?? new object[0]));
            return o;
        }

        private sealed class ClassNodes
        {
            public string Bare;
            public string Namespace;
            public string Type;
            public readonly List<object> Nodes = new List<object>();
        }

        private sealed class CaseRow
        {
            public string Key;
            public string ClassName;
            public string Method;
            public string Uid;
        }
    }

    /// <summary>Per-row helpers of the test-phase summary in test-phase-evidence.ps1.</summary>
    public static class TestPhaseSummary
    {
        /// <summary>ConvertTo-TestPhaseCsvCell: quoted, quotes doubled, a leading = + - @ neutralised with '.</summary>
        public static string Cell(object value)
        {
            var text = value == null ? "" : PsValue.Str(value);
            var escaped = text.Replace("\"", "\"\"");
            if (escaped.Length > 0)
            {
                var lead = escaped[0];
                if (lead == '=' || lead == '+' || lead == '-' || lead == '@') escaped = "'" + escaped;
            }
            return "\"" + escaped + "\"";
        }

        /// <summary>One tests.csv line: the cells of <paramref name="fields"/> read from the row, comma-joined.</summary>
        public static string Row(object test, string[] fields)
        {
            var cells = new string[fields.Length];
            for (var i = 0; i < fields.Length; i++) cells[i] = Cell(PsValue.Prop(test, fields[i]));
            return string.Join(",", cells);
        }

        /// <summary>ConvertTo-TestPhaseSummaryRows' per-TRX tagging: Add-Member -Force of GroupId and Suite on each row.</summary>
        public static void TagRows(object rows, string groupId, string suite)
        {
            foreach (var item in PsValue.AsArray(rows))
            {
                if (!(item is PSObject row)) continue;
                SetNote(row, "GroupId", groupId ?? "");
                SetNote(row, "Suite", suite ?? "");
            }
        }

        /// <summary>
        /// ConvertTo-TestPhaseSummaryRows' native lookup: TestId and every native identity in a row's output, to
        /// the distinct (TestId, Path) rows carrying it. Keys and the duplicate test compare case-insensitively,
        /// as the @{} and -eq they replace.
        /// </summary>
        public static Hashtable MapNative(object trxRows)
        {
            var nativeMap = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var row in PsValue.AsArray(trxRows))
            {
                var path = PsValue.Str(PsValue.Prop(row, "Path"));
                var testId = PsValue.Str(PsValue.Prop(row, "TestId"));
                var keys = new List<string> { testId };
                keys.AddRange(NativeIds(PsValue.Str(PsValue.Prop(row, "StdOut"))));
                foreach (var key in keys)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (!(nativeMap[key] is List<object> list)) nativeMap[key] = list = new List<object>();
                    var already = false;
                    foreach (var existing in list)
                        if (string.Equals(PsValue.Str(PsValue.Prop(existing, "TestId")), testId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(PsValue.Str(PsValue.Prop(existing, "Path")), path, StringComparison.OrdinalIgnoreCase)) already = true;
                    if (!already) list.Add(row);
                }
            }
            return nativeMap;
        }

        private static void SetNote(PSObject row, string name, object value)
        {
            if (row.Properties[name] != null) row.Properties.Remove(name);
            row.Properties.Add(new PSNoteProperty(name, value));
        }

        /// <summary>
        /// ConvertTo-TestPhaseSummaryRows' rows for TRX results no phase record claims: { Rows, PassedWithoutPhase }.
        /// A passing TRX row without phase evidence is 'unknown' and incomplete.
        /// </summary>
        public static PSObject UnjoinedTrxRows(object trxRows, IDictionary joinedTrx, IDictionary completeTestIds)
        {
            var rows = new List<object>();
            var passedWithoutPhase = false;
            foreach (var row in PsValue.AsArray(trxRows))
            {
                var testId = PsValue.Str(PsValue.Prop(row, "TestId"));
                if (joinedTrx.Contains(testId + "|" + PsValue.Str(PsValue.Prop(row, "Path")))) continue;
                var nativeHit = false;
                foreach (var native in NativeIds(PsValue.Str(PsValue.Prop(row, "StdOut"))))
                    if (completeTestIds.Contains(native)) nativeHit = true;
                if (nativeHit || completeTestIds.Contains(testId)) continue;
                var outcome = PsValue.Str(PsValue.Prop(row, "Outcome"));
                var incomplete = false;
                if (string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase))
                {
                    outcome = "unknown";
                    incomplete = true;
                    passedWithoutPhase = true;
                }
                rows.Add(new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
                {
                    { "testId", testId }, { "suite", PsValue.Str(PsValue.Prop(row, "Suite")) }, { "className", "" },
                    { "method", PsValue.Str(PsValue.Prop(row, "Name")) }, { "duration", PsValue.Str(PsValue.Prop(row, "Duration")) },
                    { "groupId", PsValue.Str(PsValue.Prop(row, "GroupId")) }, { "outcome", outcome },
                    { "trxOutcome", PsValue.Str(PsValue.Prop(row, "Outcome")) }, { "phaseOutcome", "" },
                    { "joined", false }, { "incomplete", incomplete }, { "countsKnown", false },
                    { "db", 0 }, { "host", 0 }, { "process", 0 }, { "fs", 0 }, { "network", 0 }, { "fixture", 0 },
                    { "overlapMs", 0 }, { "remainderMs", 0 }, { "droppedPhaseCount", 0 },
                });
            }
            var result = new PSObject();
            result.Properties.Add(new PSNoteProperty("Rows", rows.ToArray()));
            result.Properties.Add(new PSNoteProperty("PassedWithoutPhase", passedWithoutPhase));
            return result;
        }

        /// <summary>Get-TestPhaseNativeIds: ANTIPHON_TEST_PHASE_ID / C1514_NATIVE identities in a TRX row's output.</summary>
        public static string[] NativeIds(string text)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(text)) return ids.ToArray();
            const string marker = "C1514_NATIVE ";
            const string phase = "ANTIPHON_TEST_PHASE_ID ";
            foreach (var line in text.Split('\n'))
            {
                var trim = line.Trim().TrimEnd('\r');
                string id;
                if (trim.StartsWith(phase, StringComparison.Ordinal)) id = trim.Substring(phase.Length).Trim();
                else if (trim.StartsWith(marker, StringComparison.Ordinal))
                {
                    var rest = trim.Substring(marker.Length);
                    var space = rest.IndexOf(' ');
                    if (space < 1) continue;
                    id = rest.Substring(space + 1).Trim();
                }
                else continue;
                if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id)) ids.Add(id);
            }
            return ids.ToArray();
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
