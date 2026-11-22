// The failure history, compiled: the export of a closed run's prior failures (export-nightly-prior-failures.ps1), its
// schema and reader, the executors' dispatch-history intake (a history or a stopped-run seed) and the resolver that maps
// its members onto the current plan. This replaces scripts/lib/nightly-failure-history.ps1; scripts call these methods.
//
// History is a scheduling hint, never a receipt or execution credit. Every evidence file is resolved under the run root
// (an absolute retained path is rebound only by an exact logDir prefix or run id), link-checked through the process's
// HistoryLinkCache, hashed when read and hashed again before the history is written; a reader rechecks every digest and
// the summary's provenance. A refusal is thrown as NightlyRefusal with the reason the scripts reported. The records are
// PsValue records (PSObjects under PowerShell, PlainObjects for a plain caller such as the in-process tests).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace Antiphon.Nightly
{
    public static class PriorFailureHistory
    {
        private const string HistoryFormat = "antiphon-nightly-prior-failures-v1";
        private const string DiscoveryFormat = "antiphon-tunit-discovery-v1";
        private const string ExecutionFormat = "antiphon-tunit-execution-v1";
        private const string SupportedTunit = "1.44.0";
        private const string SupportedMtp = "2.2.2";
        private const string SeedKind = "operator-seed-from-stopped-run";

        private static readonly string[] Suites =
            { "antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "e2e", "markdown-pdf", "observability", "server-git", "resilience", "git-in-process" };
        private static readonly string[] EvidenceKinds = { "summary", "trx", "diagnostic", "discovery", "planned", "planned-index", "timing" };
        private static readonly string[] FailureStates = { "Failed", "Error", "NotExecuted", "InProgress", "Timeout", "Cancelled", "Aborted" };
        private static readonly string[] OmitBulk = { "suites[].exclusions", "dispatchProgress.suites[].exclusions", "suiteExclusions" };
        private static readonly Regex Sha40 = new Regex("^[a-f0-9]{40}$");
        private static readonly Regex Sha64 = new Regex("^[a-f0-9]{64}$");
        private static readonly Regex ChunkId = new Regex("^[A-Za-z0-9_-]+$");
        private static readonly Regex ClassPattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_+`]*(\.[A-Za-z_][A-Za-z0-9_+`]*)+$");
        private static readonly Regex BadEvidencePath = new Regex(@"(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))");
        private static readonly Regex Control = new Regex(@"[\x00-\x1f]");
        private static readonly Regex DriveAbsolute = new Regex("^[A-Za-z]:/");
        private static readonly Regex TrxIdentityError = new Regex("^duplicate identity|^missing definition|^unresolved identity|^history-identity-conflict", RegexOptions.IgnoreCase);
        private static readonly Regex TunitHeader = new Regex(@"Version:\s+'1\.44\.0|TUnit v1\.44\.0", RegexOptions.IgnoreCase);
        private static readonly Regex MtpHeader = new Regex(@"Version:\s+2\.2\.2", RegexOptions.IgnoreCase);
        private static readonly Regex UpdateMarker = new Regex(@"TestNodeUpdateMessage \{");

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static NightlyRefusal Refuse(string reason) => new NightlyRefusal(reason);

        // ---- small readers ----

        /// <summary>Lower-case hex SHA-256 of a file.</summary>
        public static string Digest(string path) => StoppedSeedFiles.Sha(path);

        /// <summary>A summary's terminal instant: a date, an offset or a round-trip text; null for anything else.</summary>
        public static DateTimeOffset? TerminalInstant(object value)
        {
            if (value == null) return null;
            var b = PsOps.Base(value);
            if (b is DateTimeOffset offset) return offset;
            if (b is DateTime date) return new DateTimeOffset(date);
            var text = PsValue.Str(value);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : (DateTimeOffset?)null;
        }

        private static object ReadJson(string path, string[] omit) => NightlyJson.ReadFile(path, true, omit);

        // W2-nightly S15: the reader and the dispatch intake also run without PowerShell (the plain nightly-tests run):
        // their documents are PlainObjects there and PSObjects under PowerShell, as ReadFile reads them.
        private static object ReadDocument(string path, string[] omit) => NightlyJson.ReadFile(path, true, omit);

        private static object Rec(params object[] pairs)
        {
            var properties = new (string, object)[pairs.Length / 2];
            for (var i = 0; i < properties.Length; i++) properties[i] = ((string)pairs[2 * i], pairs[2 * i + 1]);
            return PsValue.Record(properties);
        }

        private static void Fields(object o, params string[] names)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (PsOps.Base(o) is IDictionary d) { foreach (var k in d.Keys) keys.Add(PsValue.Str(k)); }
            else if (o != null) { foreach (var name in PsValue.PropertyNames(o)) keys.Add(name); }
            foreach (var name in names) if (!keys.Contains(name)) throw Refuse("history-schema: missing " + name);
        }

        private static void AssertClass(object className)
        {
            if (!ClassPattern.IsMatch(PsValue.Str(className))) throw Refuse("history-class-identity");
        }

        private static bool In(object value, string[] set) => PsOps.In(value, set, false);

        private static string Str(object o, string name) => PsValue.Str(PsValue.Prop(o, name));

        private static bool True(object o, string name) => PsValue.IsTrue(PsValue.Prop(o, name));

        private static void NoLink(string path)
        {
            var refusal = HistoryLinkCache.Refusal(path);
            if (refusal != null) throw Refuse(refusal);
        }

        private static void SetNote(object target, string name, object value) => PsValue.SetNote(target, name, value);

        // A record: name, value pairs.
        private static object Obj(params object[] pairs)
        {
            var properties = new (string Name, object Value)[pairs.Length / 2];
            for (var i = 0; i < properties.Length; i++) properties[i] = ((string)pairs[2 * i], pairs[2 * i + 1]);
            return PsValue.Record(properties);
        }

        // ---- schema ----

        /// <summary>The history document's schema: fields, formats, unique evidence, members and their observations.</summary>
        public static void AssertSchema(object history)
        {
            Fields(history, "format", "sourceRunId", "sourceAttempt", "sourceSha", "profile", "platform", "policyHash", "createdAtUtc", "complete", "evidence", "members", "fallbackChunks", "membershipComplete", "unresolved");
            var complete = PsOps.Base(PsValue.Prop(history, "complete"));
            var membership = PsOps.Base(PsValue.Prop(history, "membershipComplete"));
            if (!PsOps.StrEqValue(PsValue.Prop(history, "format"), HistoryFormat) || !(complete is bool done) || !done || !(membership is bool)
                || !Sha40.IsMatch(Str(history, "sourceSha")) || !Sha64.IsMatch(Str(history, "policyHash"))
                || string.IsNullOrWhiteSpace(Str(history, "sourceRunId")) || string.IsNullOrWhiteSpace(Str(history, "sourceAttempt"))
                || !In(PsValue.Prop(history, "profile"), new[] { "rc", "nightly" }) || !In(PsValue.Prop(history, "platform"), new[] { "windows", "linux" }))
                throw Refuse("history-schema");
            if (TerminalInstant(PsValue.Prop(history, "createdAtUtc")) == null) throw Refuse("history-schema: createdAtUtc");
            foreach (var name in new[] { "evidence", "members", "fallbackChunks", "unresolved" })
            {
                var value = PsValue.Prop(history, name);
                if (value == null || !PsOps.IsArray(value)) throw Refuse("history-schema: array " + name);
            }
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in PsValue.AsArray(PsValue.Prop(history, "evidence")))
            {
                Fields(e, "relativePath", "sha256", "kind");
                var relative = Str(e, "relativePath");
                if (BadEvidencePath.IsMatch(relative) || !paths.Add(relative) || !Sha64.IsMatch(Str(e, "sha256")) || !In(PsValue.Prop(e, "kind"), EvidenceKinds))
                    throw Refuse("history-schema: evidence");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in PsValue.AsArray(PsValue.Prop(history, "members")))
            {
                Fields(m, "suite", "className", "reasons", "observations");
                AssertClass(PsValue.Prop(m, "className"));
                if (!In(PsValue.Prop(m, "suite"), Suites)) throw Refuse("unsupported-history-suite");
                if (!seen.Add(Str(m, "suite") + "/" + Str(m, "className")) || !PsOps.IsArray(PsValue.Prop(m, "reasons")) || !PsOps.IsArray(PsValue.Prop(m, "observations"))
                    || PsValue.AsArray(PsValue.Prop(m, "reasons")).Length == 0) throw Refuse("history-schema: member");
                foreach (var o in PsValue.AsArray(PsValue.Prop(m, "observations")))
                {
                    Fields(o, "artifact", "chunk", "uid", "state", "reason", "kind");
                    if (!paths.Contains(Str(o, "artifact")) || string.IsNullOrWhiteSpace(Str(o, "state")) || !In(PsValue.Prop(o, "kind"), new[] { "trx", "diagnostic" }))
                        throw Refuse("history-schema: observation");
                }
            }
            var unresolved = PsValue.AsArray(PsValue.Prop(history, "unresolved"));
            foreach (var u in unresolved)
            {
                Fields(u, "suite", "reason");
                if (string.IsNullOrWhiteSpace(Str(u, "reason"))) throw Refuse("history-schema: unresolved");
            }
            if (!PsValue.IsTrue(membership) && unresolved.Length == 0) throw Refuse("history-schema: missing unresolved membership");
            foreach (var f in PsValue.AsArray(PsValue.Prop(history, "fallbackChunks")))
            {
                Fields(f, "suite", "chunk", "reason", "classes", "membershipResolved");
                var resolved = PsOps.Base(PsValue.Prop(f, "membershipResolved"));
                var classes = PsValue.Prop(f, "classes");
                if (!(resolved is bool isResolved) || (isResolved && PsValue.AsArray(classes).Length == 0) || (!isResolved && classes != null)) throw Refuse("history-schema: fallback");
                foreach (var c in PsValue.AsArray(classes)) if (c != null) AssertClass(c);
            }
        }

        // ---- evidence paths and failure nodes ----

        /// <summary>
        /// An evidence path under the run root: relative paths as they are; an absolute retained path rebound by the root,
        /// the summary's logDir or its run id. { FullPath, RelativePath, Exists, Rebound, Original }.
        /// </summary>
        public static object ResolveArtifact(string root, string path, object summary)
        {
            if (string.IsNullOrWhiteSpace(path) || Control.IsMatch(path)) throw Refuse("evidence-path-escape");
            var relative = path.Replace('\\', '/');
            if (Array.IndexOf(relative.Split('/'), "..") >= 0) throw Refuse("evidence-path-escape");
            var rebound = false;
            if (relative.StartsWith("/", StringComparison.Ordinal) || DriveAbsolute.IsMatch(relative))
            {
                // Never open the historical absolute path. Rebind only an exact retained logDir prefix or run-id
                // component to this explicitly supplied root.
                var prefixes = new List<string> { root.Replace('\\', '/').TrimEnd('/') };
                if (summary != null && True(summary, "logDir")) prefixes.Add(Str(summary, "logDir").Replace('\\', '/').TrimEnd('/'));
                var found = false;
                foreach (var prefix in prefixes)
                {
                    if (relative.StartsWith(prefix + "/", StringComparison.Ordinal)) { relative = relative.Substring(prefix.Length + 1); found = true; break; }
                }
                if (!found && summary != null && True(summary, "runId"))
                {
                    var needle = "/" + Str(summary, "runId") + "/";
                    var at = relative.IndexOf(needle, StringComparison.Ordinal);
                    if (at >= 0 && relative.IndexOf(needle, at + 1, StringComparison.Ordinal) < 0) { relative = relative.Substring(at + needle.Length); found = true; }
                }
                if (!found) throw Refuse("evidence-path-escape: unbound-absolute-path");
                rebound = true;
            }
            var parts = relative.Split('/');
            if (relative.Length == 0 || relative.StartsWith("/", StringComparison.Ordinal) || relative.Contains(":") || Array.IndexOf(parts, "") >= 0 || Array.IndexOf(parts, ".") >= 0)
                throw Refuse("evidence-path-escape");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!StoppedSeedFiles.UnderRoot(full, root)) throw Refuse("evidence-path-escape");
            NoLink(full);
            return Rec("FullPath", full, "RelativePath", relative, "Exists", File.Exists(full) || Directory.Exists(full), "Rebound", rebound, "Original", path);
        }

        /// <summary>
        /// The nodes an execution document or diagnostic log records, by uid, each checked against the discovery when
        /// one is given: { Nodes, Complete }. A torn or unpinned log is incomplete; an identity conflict refuses.
        /// </summary>
        public static object FailureNodes(string path, object discovery)
        {
            var text = File.ReadAllText(path);
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            var byUid = new Dictionary<string, object>(StringComparer.Ordinal);
            var order = new List<string>();
            var known = new Dictionary<string, object>(StringComparer.Ordinal);
            if (discovery != null) foreach (var n in PsValue.AsArray(PsValue.Prop(discovery, "Nodes"))) known.Add(Str(n, "Uid"), n);
            var complete = true;
            var rows = new List<object>();
            try
            {
                if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    var doc = NightlyJson.ParseText(text, 1024, false) ?? throw new InvalidDataException("execution-json");
                    Fields(doc, "format", "tunitVersion", "mtpVersion", "nodes");
                    if (!PsOps.StrEqValue(PsValue.Prop(doc, "format"), ExecutionFormat) || !PsOps.StrEqValue(PsValue.Prop(doc, "tunitVersion"), SupportedTunit)
                        || !PsOps.StrEqValue(PsValue.Prop(doc, "mtpVersion"), SupportedMtp)) throw Refuse("unsupported-execution-format");
                    foreach (var r in PsValue.AsArray(PsValue.Prop(doc, "nodes")))
                    {
                        Fields(r, "uid", "state");
                        if (!known.TryGetValue(Str(r, "uid"), out var node)) throw Refuse("history-identity-conflict: execution UID not in discovery");
                        if ((True(r, "className") && Str(r, "className") != Str(node, "ClassName")) || (True(r, "type") && Str(r, "type") != Str(node, "Type"))
                            || (True(r, "method") && Str(r, "method") != Str(node, "Method")) || (True(r, "namespace") && Str(r, "namespace") != Str(node, "Namespace")))
                            throw Refuse("history-identity-conflict: execution mapping");
                        rows.Add(Obj("Uid", Str(r, "uid"), "ClassName", Str(node, "ClassName"), "Method", Str(node, "Method"), "State", Str(r, "state")));
                    }
                }
                else
                {
                    if (!TunitHeader.IsMatch(text) || !MtpHeader.IsMatch(text)) throw new InvalidDataException("version-drift tunit/mtp header missing or unsupported");
                    foreach (var record in NightlyDiagnostics.Records(text))
                    {
                        var property = NightlyDiagnostics.StateProperty(record);
                        var state = Str(property, "Name");
                        if (string.IsNullOrEmpty(state)) { complete = false; continue; }
                        var converted = NightlyDiagnostics.ConvertRecord(record, state);
                        if (!string.IsNullOrEmpty(Str(converted, "Error"))) { complete = false; continue; }
                        rows.Add(PsValue.Prop(converted, "Node"));
                    }
                    // Every structural update must have been framed and parsed. A torn tail cannot silently make a
                    // diagnostic a complete observation set.
                    var markers = UpdateMarker.Matches(text).Count;
                    if (markers == 0 || markers != rows.Count) complete = false;
                }
            }
            catch (Exception e)
            {
                if (StoppedSeedFiles.Message(e).StartsWith("history-identity-conflict", StringComparison.Ordinal)) throw;
                complete = false;
            }
            foreach (var n in rows)
            {
                var uid = Str(n, "Uid");
                if (uid.Length == 0 || !True(n, "State")) { complete = false; continue; }
                if (known.Count > 0)
                {
                    if (!known.TryGetValue(uid, out var node)) throw Refuse("history-identity-conflict: diagnostic UID not in discovery");
                    if (Str(node, "ClassName") != Str(n, "ClassName") || Str(node, "Method") != Str(n, "Method")) throw Refuse("history-identity-conflict: diagnostic mapping");
                }
                if (byUid.TryGetValue(uid, out var old))
                {
                    if (Str(old, "ClassName") != Str(n, "ClassName") || Str(old, "Method") != Str(n, "Method") || !In(PsValue.Prop(old, "State"), new[] { "InProgress", "Discovered" }))
                        throw Refuse("history-identity-conflict: duplicate terminal UID");
                }
                else order.Add(uid);
                byUid[uid] = n;
            }
            var nodes = new object[order.Count];
            for (var i = 0; i < nodes.Length; i++) nodes[i] = byUid[order[i]];
            return Obj("Nodes", nodes, "Complete", complete);
        }

        // ConvertFrom-NightlyDiscoveryDocument: the pinned format and versions, the assembly hash and the compiled nodes.
        private static object DiscoveryDocument(object doc)
        {
            if (doc == null) throw new InvalidDataException("empty discovery document");
            var format = Str(doc, "format");
            if (!string.Equals(format, DiscoveryFormat, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("unknown discovery format " + format);
            var tunit = Str(doc, "tunitVersion");
            var mtp = Str(doc, "mtpVersion");
            if (!string.Equals(tunit, SupportedTunit, StringComparison.OrdinalIgnoreCase) || !string.Equals(mtp, SupportedMtp, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("version-drift tunit=" + tunit + " mtp=" + mtp);
            if (string.IsNullOrWhiteSpace(Str(doc, "assemblyHash"))) throw new InvalidDataException("missing assemblyHash");
            var compiled = NightlyDocuments.DiscoveryNodes(PsValue.Prop(doc, "nodes"));
            var error = Str(compiled, "Error");
            if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);
            return Obj("Format", format, "TunitVersion", tunit, "MtpVersion", mtp, "AssemblyHash", Str(doc, "assemblyHash"), "Nodes", PsValue.Prop(compiled, "Nodes"));
        }

        // ---- export ----

        /// <summary>
        /// Exports a closed run's prior failures to outputPath and returns { Path, Digest, History }: failed or unfinished
        /// identities from each chunk's TRX and diagnostic log, incomplete chunks as fallback rows with their planned
        /// classes when the retained plan binds, and every evidence file's digest.
        /// </summary>
        public static object Export(string runDirectory, string outputPath) => new HistoryExport().Run(runDirectory, outputPath);

        private sealed class HistoryExport
        {
            private string _root;
            private readonly Dictionary<string, OrderedDictionary> _evidence = new Dictionary<string, OrderedDictionary>(StringComparer.Ordinal);
            private readonly List<object> _rebindings = new List<object>();
            private readonly Dictionary<string, OrderedDictionary> _members = new Dictionary<string, OrderedDictionary>(StringComparer.Ordinal);

            // Read-HistoryInput: an evidence file under the root, recorded with its digest; null when it does not exist.
            private object Input(string path, string kind, object summary)
            {
                var artifact = ResolveArtifact(_root, path, summary);
                if (!(bool)PsValue.Prop(artifact, "Exists")) return null;
                var full = Str(artifact, "FullPath");
                if (!File.Exists(full)) throw Refuse("evidence-not-file");
                var digest = Digest(full);
                var relative = Str(artifact, "RelativePath");
                if (_evidence.TryGetValue(relative, out var prior)) { if ((string)prior["sha256"] != digest) throw Refuse("evidence-changed"); }
                else _evidence.Add(relative, new OrderedDictionary { { "relativePath", relative }, { "sha256", digest }, { "kind", kind } });
                if ((bool)PsValue.Prop(artifact, "Rebound")) _rebindings.Add(new OrderedDictionary { { "original", Str(artifact, "Original") }, { "relativePath", relative } });
                // The export runs under PowerShell: the record is a PSObject.
                return (PSObject)artifact;
            }

            private void Member(string suite, string className, string reason, OrderedDictionary observation)
            {
                var cls = NightlyClassNames.Bare(className ?? "");
                AssertClass(cls);
                var key = suite + "/" + cls;
                if (!_members.TryGetValue(key, out var member))
                {
                    member = new OrderedDictionary { { "suite", suite }, { "className", cls }, { "reasons", new List<object>() }, { "observations", new List<object>() } };
                    _members.Add(key, member);
                }
                var reasons = (List<object>)member["reasons"];
                if (!reasons.Contains(reason)) reasons.Add(reason);
                if (observation != null) ((List<object>)member["observations"]).Add(observation);
            }

            public object Run(string runDirectory, string outputPath)
            {
                if (string.IsNullOrWhiteSpace(runDirectory) || string.IsNullOrWhiteSpace(outputPath)) throw Refuse("history-path-required");
                _root = Path.GetFullPath(runDirectory).TrimEnd('\\', '/');
                if (_root.Length == 0 || _root == Path.GetPathRoot(_root)) throw Refuse("evidence-path-escape: root");
                NoLink(_root);
                var output = Path.GetFullPath(outputPath);
                NoLink(output);
                if (StoppedSeedFiles.UnderRoot(output, _root)) throw Refuse("history-output-inside-run");
                if (File.Exists(output) || Directory.Exists(output)) throw Refuse("history-output-exists");

                var summaryArtifact = Input("summary.json", "summary", null) ?? throw Refuse("run-not-closed: summary missing");
                // The per-chunk D-5 exclusion lists are most of a full run's summary and are not exported.
                var summary = ReadJson(Str(summaryArtifact, "FullPath"), OmitBulk) ?? throw Refuse("history-schema: summary");
                if (!True(summary, "runId") && True(summary, "invocationId")) SetNote(summary, "runId", Str(summary, "invocationId"));
                if (True(summary, "invocationId") && Str(summary, "runId") != Str(summary, "invocationId")) throw Refuse("history-provenance: run identity");
                Fields(summary, "runId", "sha", "profile", "platform", "policyHash", "suites", "completedAt");
                if (TerminalInstant(PsValue.Prop(summary, "completedAt")) == null || In(PsValue.Prop(summary, "outcome"), new[] { "Working", "Running" })
                    || string.IsNullOrWhiteSpace(Str(summary, "runId"))) throw Refuse("run-not-closed");
                var runId = Str(summary, "runId");
                var attempt = "unknown";
                if (True(summary, "attempt")) attempt = Str(summary, "attempt");
                else if (True(summary, "selection") && True(PsValue.Prop(summary, "selection"), "attempt")) attempt = Str(PsValue.Prop(summary, "selection"), "attempt");
                var sha = Str(summary, "sha");
                var policyHash = Str(summary, "policyHash");
                if (!Sha40.IsMatch(sha) || !Sha64.IsMatch(policyHash)) throw Refuse("history-provenance: summary");

                var fallback = new List<object>();
                var unresolved = new List<object>();
                var seenChunks = new HashSet<string>(StringComparer.Ordinal);
                var discoveries = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var chunk in PsValue.AsArray(PsValue.Prop(summary, "suites")))
                {
                    var suite = Str(chunk, "id");
                    if (!In(suite, Suites))
                    {
                        if (True(chunk, "trx") || True(chunk, "execution")) throw Refuse("unsupported-history-suite");
                        continue;
                    }
                    var chunkId = Str(chunk, "chunk");
                    if (chunkId.Length == 0) continue; // prerequisite-only suite rows have no native chunk
                    if (!ChunkId.IsMatch(chunkId) || !seenChunks.Add(suite + "/" + chunkId)) throw Refuse("history-identity-conflict: chunk");
                    if (!discoveries.ContainsKey(suite))
                    {
                        var disc = Input(suite + "-profile.discovery.json", "discovery", summary);
                        object parsed = null;
                        if (disc != null)
                        {
                            try
                            {
                                var doc = ReadJson(Str(disc, "FullPath"), null) ?? throw new InvalidDataException("discovery-json");
                                Fields(doc, "format", "tunitVersion", "mtpVersion", "assemblyHash", "nodes");
                                if (!PsOps.StrEqValue(PsValue.Prop(doc, "format"), DiscoveryFormat) || !PsOps.StrEqValue(PsValue.Prop(doc, "tunitVersion"), SupportedTunit)
                                    || !PsOps.StrEqValue(PsValue.Prop(doc, "mtpVersion"), SupportedMtp)) throw new InvalidDataException("discovery-version");
                                foreach (var n in PsValue.AsArray(PsValue.Prop(doc, "nodes"))) Fields(n, "uid", "namespace", "type", "method");
                                parsed = DiscoveryDocument(doc);
                            }
                            catch (Exception) { throw Refuse("history-identity-conflict: discovery"); }
                        }
                        discoveries.Add(suite, parsed);
                    }
                    var discovery = discoveries[suite];
                    ChunkHistory(chunk, suite, chunkId, summary, summaryArtifact, discovery, runId, attempt, sha, policyHash, fallback, unresolved);
                }

                var memberKeys = new List<object>(_members.Keys);
                var orderedMembers = new List<object>();
                foreach (var key in DispatchSchedule.OrdinalStrings(memberKeys))
                {
                    var m = _members[key];
                    m["reasons"] = DispatchSchedule.OrdinalStrings((List<object>)m["reasons"]);
                    m["observations"] = ((List<object>)m["observations"]).ToArray();
                    orderedMembers.Add(m);
                }
                var orderedEvidence = new List<object>();
                foreach (var key in DispatchSchedule.OrdinalStrings(new List<object>(_evidence.Keys))) orderedEvidence.Add(_evidence[key]);
                var history = new OrderedDictionary
                {
                    { "format", HistoryFormat }, { "sourceRoot", _root }, { "sourceRunId", runId }, { "sourceAttempt", attempt }, { "sourceSha", sha },
                    { "profile", Str(summary, "profile") }, { "platform", Str(summary, "platform") }, { "policyHash", policyHash },
                    { "createdAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "complete", true },
                    { "evidence", orderedEvidence.ToArray() }, { "members", orderedMembers.ToArray() }, { "fallbackChunks", fallback.ToArray() },
                    { "membershipComplete", unresolved.Count == 0 }, { "unresolved", unresolved.ToArray() }, { "rebindings", _rebindings.ToArray() },
                };
                var bytes = SeedJson.Bytes(history, true);
                var document = NightlyJson.ParseText(System.Text.Encoding.UTF8.GetString(bytes), 1024, true) ?? throw Refuse("history-schema");
                AssertSchema(document);
                foreach (OrderedDictionary e in orderedEvidence)
                {
                    var a = ResolveArtifact(_root, (string)e["relativePath"], null);
                    if (Digest(Str(a, "FullPath")) != (string)e["sha256"]) throw Refuse("evidence-changed");
                }
                // Validate everything before the first write; the file appears whole or not at all.
                var parent = Path.GetDirectoryName(output);
                Directory.CreateDirectory(parent);
                NoLink(parent);
                var temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    File.Move(temp, output);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return Obj("Path", output, "Digest", Digest(output), "History", document);
            }

            private void ChunkHistory(object chunk, string suite, string chunkId, object summary, object summaryArtifact, object discovery,
                string runId, string attempt, string sha, string policyHash, List<object> fallback, List<object> unresolved)
            {
                var incomplete = false;
                var accountingIncomplete = false;
                var why = new List<string>();
                var observed = new List<object>();
                var trxRowCount = 0;
                if (!string.IsNullOrEmpty(Str(chunk, "detail"))) { accountingIncomplete = true; why.Add("summary-detail"); }
                if (accountingIncomplete) incomplete = true;
                var trxName = Str(chunk, "trx");
                if (trxName.Length == 0) trxName = suite + "-" + chunkId + ".trx";
                var trx = Input(trxName, "trx", summary);
                if (trx == null) { incomplete = true; why.Add("missing-trx"); }
                else
                {
                    try
                    {
                        var xml = new XmlDocument { XmlResolver = null };
                        using (var reader = XmlReader.Create(new StringReader(File.ReadAllText(Str(trx, "FullPath"))), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                            xml.Load(reader);
                        var definitions = new HashSet<string>(StringComparer.Ordinal);
                        foreach (XmlNode unit in xml.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
                            if (!definitions.Add(((XmlElement)unit).GetAttribute("id"))) throw Refuse("history-identity-conflict: trx definition");
                        var identity = NightlyTrx.IdentityRows(xml);
                        var error = Str(identity, "Error");
                        if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);
                        var rows = PsValue.AsArray(PsValue.Prop(identity, "Rows"));
                        trxRowCount = rows.Length;
                        if (rows.Length == 0) { incomplete = true; why.Add("empty-trx"); }
                        foreach (var r in rows)
                        {
                            var normalized = NightlyClassNames.NormalizedOutcome(Str(r, "Outcome"));
                            if (normalized == "NotExecuted" || normalized == "Unknown") { incomplete = true; why.Add("incomplete-trx"); }
                            observed.Add(Obj("ClassName", Str(r, "ClassName"), "Uid", Str(r, "TestId"), "State", Str(r, "Outcome"), "Artifact", Str(trx, "RelativePath"), "Kind", "trx"));
                        }
                    }
                    catch (Exception e)
                    {
                        if (TrxIdentityError.IsMatch(StoppedSeedFiles.Message(e))) throw Refuse("history-identity-conflict: trx");
                        incomplete = true; why.Add("malformed-trx");
                    }
                }
                if (PsOps.Base(PsValue.Prop(chunk, "coverageComplete")) is bool coverage && !coverage) { incomplete = true; why.Add("retained-incomplete-coverage"); }
                if (PsOps.Eq(PsValue.Prop(chunk, "trxMissing"), true, true)) { incomplete = true; why.Add("retained-missing-trx"); }
                if (PsOps.Eq(PsValue.Prop(chunk, "timedOut"), true, true)) { incomplete = true; why.Add("timed-out"); }
                var executionName = Str(chunk, "execution");
                if (executionName.Length == 0)
                {
                    var directory = ResolveArtifact(_root, suite + "-" + chunkId + "-execution", summary);
                    if ((bool)PsValue.Prop(directory, "Exists"))
                    {
                        var files = Directory.GetFiles(Str(directory, "FullPath"), "*.diag");
                        if (files.Length > 1) throw Refuse("ambiguous-invocation");
                        if (files.Length == 1) executionName = Str(directory, "RelativePath") + "/" + Path.GetFileName(files[0]);
                    }
                }
                if (executionName.Length > 0)
                {
                    var execution = Input(executionName, "diagnostic", summary);
                    if (execution == null) { incomplete = true; why.Add("missing-diagnostic"); }
                    else
                    {
                        var parsed = FailureNodes(Str(execution, "FullPath"), discovery);
                        if (!(bool)PsValue.Prop(parsed, "Complete")) { incomplete = true; why.Add("incomplete-diagnostic"); }
                        foreach (var r in PsValue.AsArray(PsValue.Prop(parsed, "Nodes")))
                        {
                            if (Str(r, "State") == "InProgress") { incomplete = true; why.Add("final-in-progress"); }
                            observed.Add(Obj("ClassName", Str(r, "ClassName"), "Uid", Str(r, "Uid"), "State", Str(r, "State"), "Artifact", Str(execution, "RelativePath"), "Kind", "diagnostic"));
                        }
                    }
                }
                // A failed process is expected when a test fails. Reconcile the summary only after TRX and diagnostic
                // identities have been read. An all-Passed prefix cannot account for a crash; retained incompleteness
                // still falls through the independent TRX, detail, coverage and timeout checks.
                if (!observed.Exists(o => Array.IndexOf(FailureStates, Str(o, "State")) >= 0))
                {
                    var exitCode = PsValue.Prop(chunk, "exitCode");
                    if (exitCode != null && PsOps.Ne(exitCode, 0, true)) { accountingIncomplete = true; why.Add("summary-nonzero-exit"); }
                    var result = Str(chunk, "result");
                    if (result.Length > 0 && !string.Equals(result, "pass", StringComparison.OrdinalIgnoreCase)) { accountingIncomplete = true; why.Add("summary-failed-result"); }
                    if (accountingIncomplete) incomplete = true;
                }
                foreach (var o in observed)
                {
                    var state = Str(o, "State");
                    if ((state == "InProgress" || state == "Discovered") && !incomplete) continue;
                    var normalized = NightlyClassNames.NormalizedOutcome(state);
                    if (state == "Discovered" || normalized == "Passed" || normalized == "Skipped" || normalized == "NotExecuted") continue;
                    var reason = state == "InProgress" ? "final-in-progress" : "prior-failure";
                    Member(suite, Str(o, "ClassName"), reason, new OrderedDictionary
                    {
                        { "artifact", Str(o, "Artifact") }, { "chunk", chunkId }, { "uid", Str(o, "Uid") }, { "state", state }, { "reason", reason }, { "kind", Str(o, "Kind") },
                    });
                }
                // A retained suite-qualified plan is usable only when its old provenance and UID/class/assembly membership
                // all bind. The last singleton may belong to a completely different suite and supplies no fallback.
                object plan = null;
                var timing = Str(summary, "timingRoot");
                if (timing.Length == 0) timing = "timing";
                object timingArtifact = null;
                try { timingArtifact = ResolveArtifact(_root, timing, summary); }
                catch (NightlyRefusal refusal) when (refusal.Message.StartsWith("evidence-path-escape: unbound-absolute-path", StringComparison.Ordinal)) { }
                if (timingArtifact != null && (bool)PsValue.Prop(timingArtifact, "Exists"))
                {
                    var timingRelative = Str(timingArtifact, "RelativePath");
                    var planArtifact = Input(timingRelative + "/groups-planned-" + suite + ".json", "planned", summary);
                    var perSuite = planArtifact != null;
                    if (!perSuite) planArtifact = Input(timingRelative + "/groups-planned.json", "planned", summary);
                    if (planArtifact != null)
                    {
                        object candidate;
                        try { candidate = ReadJson(Str(planArtifact, "FullPath"), null); } catch (Exception) { candidate = null; }
                        if (candidate != null && PsOps.StrEqValue(PsValue.Prop(candidate, "suite"), suite))
                        {
                            if (!PsValue.Eq(PsValue.Prop(candidate, "version"), 1, false)) throw Refuse("history-schema: planned version");
                            if (discovery != null && True(candidate, "assemblyDigest") && Str(candidate, "assemblyDigest") != Str(discovery, "AssemblyHash")) throw Refuse("history-provenance: planned assembly");
                            if (Str(candidate, "runId") != runId || (attempt != "unknown" && Str(candidate, "attempt") != attempt)
                                || (True(candidate, "sourceDigest") && Str(candidate, "sourceDigest") != sha) || (True(candidate, "policyDigest") && Str(candidate, "policyDigest") != policyHash))
                                throw Refuse("history-provenance: planned manifest");
                            plan = candidate;
                        }
                        else if (perSuite && candidate != null) throw Refuse("history-provenance: planned suite");
                    }
                    var indexArtifact = Input(timingRelative + "/groups-planned-index.json", "planned-index", summary);
                    if (indexArtifact != null)
                    {
                        var index = ReadJson(Str(indexArtifact, "FullPath"), null) ?? throw Refuse("history-schema: planned index");
                        if (!PsValue.Eq(PsValue.Prop(index, "version"), 1, false)) throw Refuse("history-schema: planned index version");
                        if (Str(index, "runId") != runId || (attempt != "unknown" && Str(index, "attempt") != attempt)) throw Refuse("history-provenance: planned index");
                        var rows = new List<object>();
                        foreach (var row in PsValue.AsArray(PsValue.Prop(index, "suites"))) if (PsOps.StrEqValue(PsValue.Prop(row, "suite"), suite)) rows.Add(row);
                        if (rows.Count != 1 || Str(rows[0], "runId") != runId || (True(rows[0], "sourceDigest") && Str(rows[0], "sourceDigest") != sha)
                            || (True(rows[0], "policyDigest") && Str(rows[0], "policyDigest") != policyHash)) throw Refuse("history-provenance: planned index suite");
                    }
                }
                // Read the planned roster even for a green TRX: a crash can leave only a passing prefix. Suite-wide
                // discovery is not a chunk's planned count.
                var groups = PlanGroups(plan, chunkId);
                var plannedCount = groups.Count == 1 ? PsValue.AsArray(PsValue.Prop(groups[0], "plannedTestIds")).Length : 0;
                foreach (var discName in new[] { suite + "-" + chunkId + "-selection.discovery.json", suite + "-" + chunkId + ".discovery.json" })
                {
                    var chunkDiscovery = Input(discName, "discovery", summary);
                    if (chunkDiscovery == null) continue;
                    try
                    {
                        var parsed = DiscoveryDocument(ReadJson(Str(chunkDiscovery, "FullPath"), null));
                        plannedCount = Math.Max(plannedCount, PsValue.AsArray(PsValue.Prop(parsed, "Nodes")).Length);
                    }
                    catch (Exception) { throw Refuse("history-identity-conflict: chunk discovery"); }
                    break;
                }
                if (trx != null && trxRowCount < plannedCount) { incomplete = true; accountingIncomplete = true; why.Add("short-trx"); }
                if (!incomplete) return;
                string[] classes = new string[0];
                var resolved = false;
                if (plan != null && discovery != null && Str(plan, "sourceDigest") == sha && Str(plan, "policyDigest") == policyHash && Str(plan, "assemblyDigest") == Str(discovery, "AssemblyHash"))
                {
                    groups = PlanGroups(plan, chunkId);
                    if (groups.Count == 1)
                    {
                        var group = groups[0];
                        classes = DispatchSchedule.OrdinalStrings(new List<object>(PsValue.AsArray(PsValue.Prop(group, "classes"))));
                        var uidMap = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var n in PsValue.AsArray(PsValue.Prop(discovery, "Nodes"))) uidMap.Add(Str(n, "Uid"), n);
                        var uids = new HashSet<string>(StringComparer.Ordinal);
                        var foundClasses = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var uid in PsValue.AsArray(PsValue.Prop(group, "plannedTestIds")))
                        {
                            var id = PsValue.Str(uid);
                            if (!uids.Add(id) || !uidMap.ContainsKey(id)) throw Refuse("history-identity-conflict: planned UID");
                            var bare = NightlyClassNames.Bare(Str(uidMap[id], "ClassName"));
                            if (Array.IndexOf(classes, bare) < 0) throw Refuse("history-identity-conflict: planned class");
                            foundClasses.Add(bare);
                        }
                        foreach (var o in observed)
                        {
                            if (Array.IndexOf(classes, NightlyClassNames.Bare(Str(o, "ClassName"))) < 0 || (Str(o, "Kind") == "diagnostic" && !uids.Contains(Str(o, "Uid"))))
                                throw Refuse("history-identity-conflict: observed planned membership");
                        }
                        resolved = classes.Length > 0 && uids.Count > 0 && foundClasses.Count == classes.Length;
                    }
                }
                var summaryRelative = Str(summaryArtifact, "RelativePath");
                if (resolved)
                {
                    foreach (var cls in classes) Member(suite, cls, "incomplete-chunk-fallback", null);
                    if (accountingIncomplete)
                        foreach (var cls in classes)
                            unresolved.Add(new OrderedDictionary { { "suite", suite }, { "className", cls }, { "chunk", chunkId }, { "reason", "incomplete-chunk" }, { "evidence", new object[] { summaryRelative } }, { "missingInputs", why.ToArray() } });
                }
                else
                {
                    classes = null;
                    unresolved.Add(new OrderedDictionary { { "suite", suite }, { "chunk", chunkId }, { "reason", "historical-membership-unavailable" }, { "evidence", new object[] { summaryRelative } }, { "missingInputs", why.ToArray() } });
                }
                fallback.Add(new OrderedDictionary
                {
                    { "suite", suite }, { "chunk", chunkId }, { "reason", string.Join(",", DispatchSchedule.OrdinalStrings(new List<object>(why))) },
                    { "classes", classes }, { "membershipResolved", resolved },
                });
            }

            private static List<object> PlanGroups(object plan, string chunkId)
            {
                var groups = new List<object>();
                if (plan == null) return groups;
                foreach (var g in PsValue.AsArray(PsValue.Prop(plan, "groups"))) if (PsOps.StrEqValue(PsValue.Prop(g, "chunk"), chunkId)) groups.Add(g);
                if (groups.Count > 1) throw Refuse("history-identity-conflict: planned chunk");
                return groups;
            }
        }

        // ---- read ----

        /// <summary>Reads an exported history that no digest is required of (see the three-argument Read).</summary>
        public static object Read(string path) => Read(path, null, null);

        /// <summary>
        /// Reads an exported history: its schema, its digest, every evidence file's digest under the run root
        /// (runDirectory, else the history's sourceRoot), and the summary's provenance. A given expectedDigest must be a
        /// 64-hex digest the file has; a blank one refuses (a script passing $null passes ""). Only a C# caller's null
        /// means none is required; scripts call Read(path) for that.
        /// </summary>
        public static object Read(string path, string expectedDigest, string runDirectory)
        {
            if (string.IsNullOrWhiteSpace(path)) throw Refuse("empty-history-path");
            NoLink(path);
            if (expectedDigest != null && (string.IsNullOrWhiteSpace(expectedDigest) || !Sha64.IsMatch(expectedDigest))) throw Refuse("history-digest-mismatch");
            var digest = Digest(path);
            if (!string.IsNullOrEmpty(expectedDigest) && digest != expectedDigest) throw Refuse("history-digest-mismatch");
            var history = ReadDocument(path, null) ?? throw Refuse("history-schema: JSON");
            AssertSchema(history);
            if (string.IsNullOrEmpty(runDirectory)) runDirectory = Str(history, "sourceRoot");
            if (string.IsNullOrEmpty(runDirectory)) throw Refuse("history-provenance: source root required");
            var root = Path.GetFullPath(runDirectory).TrimEnd('\\', '/');
            NoLink(root);
            var summaries = 0;
            foreach (var e in PsValue.AsArray(PsValue.Prop(history, "evidence")))
            {
                var artifact = ResolveArtifact(root, Str(e, "relativePath"), null);
                if (!(bool)PsValue.Prop(artifact, "Exists") || Digest(Str(artifact, "FullPath")) != Str(e, "sha256")) throw Refuse("evidence-digest-mismatch");
                if (Str(e, "relativePath") == "summary.json" && Str(e, "kind") == "summary") summaries++;
            }
            if (summaries != 1) throw Refuse("history-provenance: summary missing");
            // Provenance needs the top-level fields only.
            var summary = ReadDocument(Path.Combine(root, "summary.json"), new[] { "suites", "dispatchProgress", "suiteExclusions" }) ?? throw Refuse("history-provenance");
            if (!True(summary, "runId") && True(summary, "invocationId")) PsValue.SetNote(summary, "runId", Str(summary, "invocationId"));
            var attempt = "unknown";
            if (True(summary, "attempt")) attempt = Str(summary, "attempt");
            else if (True(summary, "selection") && True(PsValue.Prop(summary, "selection"), "attempt")) attempt = Str(PsValue.Prop(summary, "selection"), "attempt");
            if (Str(summary, "runId") != Str(history, "sourceRunId") || Str(summary, "sha") != Str(history, "sourceSha") || Str(summary, "policyHash") != Str(history, "policyHash")
                || Str(summary, "profile") != Str(history, "profile") || Str(summary, "platform") != Str(history, "platform") || attempt != Str(history, "sourceAttempt"))
                throw Refuse("history-provenance");
            if (Digest(path) != digest) throw Refuse("history-digest-mismatch: changed during read");
            return history;
        }

        /// <summary>
        /// A summary's dispatchProgress snapshot with its chunk rows. A summary marked suitesFromSummary does not carry
        /// them a second time: they are its suites rows that have a chunk. Older summaries carry their own.
        /// </summary>
        public static object SummaryDispatchProgress(object summary)
        {
            if (summary == null || !PsOps.HasName(summary, "dispatchProgress")) return null;
            var progress = PsValue.Prop(summary, "dispatchProgress");
            if (progress == null) return null;
            if (PsOps.HasName(progress, "suitesFromSummary") && PsOps.Eq(PsValue.Prop(progress, "suitesFromSummary"), true, true) && !PsOps.HasName(progress, "suites"))
            {
                var rows = new List<object>();
                foreach (var row in PsValue.AsArray(PsValue.Prop(summary, "suites"))) if (row != null && True(row, "chunk")) rows.Add(row);
                if (PsRuntime.Loaded) PsFields.AddNoteWrapped(progress, "suites", rows.ToArray());
                else PsValue.AddNote(progress, "suites", rows.ToArray());
            }
            return progress;
        }

        // ---- dispatch intake ----

        /// <summary>
        /// The executors' dispatch-history intake: a validated history (closed, fresh, for the candidate) or an
        /// acknowledged stopped-run seed (from the named source candidate, fresh), copied into ownedDirectory when one
        /// is given. { History, PreviousRun, Digest, Path, FallbackReason, Warning }: on any refusal History is null and
        /// FallbackReason says why (Warning is the line to print, null when no history was asked for).
        /// </summary>
        public static object InitializeDispatch(string path, string expectedDigest, string ownedDirectory, string candidateSha,
            bool acceptCrossCandidateSeed, string seedSourceCandidateId, DateTime nowUtc)
        {
            var fallback = "history-not-requested";
            try
            {
                if (string.IsNullOrEmpty(path)) throw Refuse(fallback);
                NoLink(path);
                var probe = ReadDocument(path, null) ?? throw Refuse("history-schema: JSON");
                object history, previous;
                if (PsOps.HasName(probe, "seedKind"))
                {
                    if (!acceptCrossCandidateSeed) throw Refuse("seed-acknowledgement-required");
                    var seed = StoppedSeedFiles.Read(path, expectedDigest ?? "");
                    if (string.IsNullOrEmpty(seedSourceCandidateId) || Str(seed, "sourceCandidateId") != seedSourceCandidateId) throw Refuse("seed-source-candidate-mismatch");
                    foreach (var time in new[] { PsValue.Prop(seed, "createdAtUtc"), PsValue.Prop(seed, "sourceObservedAtUtc") })
                    {
                        var stamp = StoppedSeed.Utc(time);
                        if (stamp == null || stamp.Value.UtcDateTime < nowUtc.AddDays(-14) || stamp.Value.UtcDateTime > nowUtc.AddMinutes(5)) throw Refuse("seed-stale");
                    }
                    history = seed;
                    previous = seed;
                }
                else
                {
                    var read = Read(path, string.IsNullOrEmpty(expectedDigest) ? null : expectedDigest, null);
                    var summary = ReadDocument(Path.Combine(Str(read, "sourceRoot"), "summary.json"), OmitBulk) ?? throw Refuse("history-schema: summary");
                    if (In(PsValue.Prop(summary, "outcome"), new[] { "Working", "Running" })) throw Refuse("history-run-not-closed");
                    var progress = PsValue.Prop(summary, "dispatchProgress");
                    if (PsValue.IsTrue(progress) && PsOps.HasName(progress, "liveness") && !PsOps.StrEqValue(PsValue.Prop(progress, "liveness"), "closed")) throw Refuse("history-liveness-unknown");
                    if (!string.IsNullOrEmpty(candidateSha) && Str(read, "sourceSha") != candidateSha) throw Refuse("history-candidate-mismatch");
                    var completed = TerminalInstant(PsValue.Prop(summary, "completedAt")) ?? throw Refuse("history-terminal-time-invalid");
                    if (completed.UtcDateTime < nowUtc.AddDays(-7) || completed.UtcDateTime > nowUtc.AddMinutes(5)) throw Refuse("history-stale");
                    history = read;
                    previous = SummaryDispatchProgress(summary);
                }
                var digest = Digest(path);
                var copy = path;
                if (!string.IsNullOrEmpty(ownedDirectory))
                {
                    NoLink(ownedDirectory);
                    Directory.CreateDirectory(ownedDirectory);
                    copy = Path.Combine(ownedDirectory, "prior-failures.json");
                    if (Path.GetFullPath(path) != Path.GetFullPath(copy))
                    {
                        if (File.Exists(copy)) { if (Digest(copy) != digest) throw Refuse("history-owned-copy-mismatch"); }
                        else File.Copy(path, copy, false);
                    }
                    if (Digest(copy) != digest) throw Refuse("history-digest-mismatch");
                }
                return Rec("History", history, "PreviousRun", previous, "Digest", digest, "Path", copy, "FallbackReason", "", "Warning", null);
            }
            catch (Exception e)
            {
                var reason = StoppedSeedFiles.Message(e);
                return Rec("History", null, "PreviousRun", null, "Digest", "", "Path", path, "FallbackReason", reason,
                    "Warning", reason == "history-not-requested" ? null : "failed-first fallback: " + reason);
            }
        }

        // ---- resolve ----

        /// <summary>
        /// Maps a history's (or a seed's) members onto the current suites: { members, unresolved, exclusionProfile,
        /// policyDigest, exclusionPolicyFileDigest, exclusionPlatform, excludedUids, warnings }. Full mode warns about each
        /// unresolved member. PriorFailureSweep (validated history only) uses the history's profile exclusions from policy
        /// ({ Path, Object, Hash } as NightlyPolicy.Read returns it), checked against exclusionContext, and refuses
        /// unless every member resolves.
        /// </summary>
        // The policy read (NightlyPolicy.Read's PolicyFile) the sweep checks against.
        private static ExecutionPolicy PolicyOf(object policy) =>
            PsOps.Base(PsValue.Prop(policy, "Object")) as ExecutionPolicy ?? throw Refuse("partial-exclusion-policy-required");

        /// <remarks>Plain C# callers get a PlainObject (no PowerShell is loaded on this path).</remarks>
        public static object Resolve(object history, object[] suites, string qualificationMode, object exclusionContext, object policy)
        {
            if (qualificationMode != "Full" && qualificationMode != "PriorFailureSweep") throw Refuse("history-qualification-mode");
            var sweep = qualificationMode == "PriorFailureSweep";
            var platform = Path.DirectorySeparatorChar == '/' ? "linux" : "windows";
            var warnings = new List<string>();
            var exclusionProfile = "";
            var policyFileDigest = "";
            if (sweep)
            {
                if (exclusionContext == null) warnings.Add("PARTIAL MISSING EXCLUSION CONTEXT: derived from validated history; S4a/S5 must supply bound context at every receiving hop.");
                exclusionProfile = Str(history, "profile");
                var contextMap = PsValue.NoteMap(exclusionContext);
                if ((exclusionProfile != "rc" && exclusionProfile != "nightly") || (exclusionContext != null && PsValue.Str(contextMap[(object)"profile"]) != exclusionProfile))
                    throw Refuse("partial-exclusion-profile-mismatch");
                if (policy == null) throw Refuse("partial-exclusion-policy-required");
                NightlyPolicy.Profile(PolicyOf(policy), exclusionProfile);
                policyFileDigest = Digest(Str(policy, "Path"));
                if (exclusionContext != null)
                {
                    var bindings = new[] { new[] { "policyDigest", Str(policy, "Hash") }, new[] { "exclusionPolicyFileDigest", policyFileDigest }, new[] { "exclusionPlatform", platform } };
                    foreach (var binding in bindings)
                        if (contextMap.Contains(binding[0]) && PsValue.Str(contextMap[(object)binding[0]]) != binding[1]) throw Refuse("partial-exclusion-profile-mismatch");
                }
            }
            if (PsOps.StrEqValue(PsValue.Prop(history, "seedKind"), SeedKind))
            {
                if (sweep) throw Refuse("seed-ordering-only");
                StoppedSeed.AssertContent(history);
            }
            else AssertSchema(history);
            Hashtable exclusionsBySuite = null;
            if (sweep)
            {
                exclusionsBySuite = new Hashtable(StringComparer.OrdinalIgnoreCase);
                foreach (var suite in suites ?? new object[0])
                    exclusionsBySuite[Str(suite, "id")] = NightlyPolicy.ProfileExclusions(PolicyOf(policy), exclusionProfile, Str(suite, "id"), platform);
            }
            var map = PriorFailures.Map(history, suites, exclusionsBySuite, exclusionProfile);
            var unresolved = PsValue.AsArray(PsValue.Prop(map, "unresolved"));
            var members = PsValue.AsArray(PsValue.Prop(map, "members"));
            if (sweep)
            {
                if (unresolved.Length > 0) throw Refuse("prior-failure-sweep-unresolved: " + SeedJson.Text(unresolved, false));
                if (!True(history, "membershipComplete")) throw Refuse("historical-membership-unavailable");
                if (members.Length == 0) throw Refuse("empty-history");
            }
            else foreach (var u in unresolved) warnings.Add("UNRESOLVED PRIOR FAILURE " + SeedJson.Text(u, false));
            var policyDigest = "";
            if (sweep)
            {
                policyDigest = Str(policy, "Hash");
                if (Digest(Str(policy, "Path")) != policyFileDigest) throw Refuse("partial-exclusion-profile-mismatch: policy changed");
            }
            return PsValue.Record(("members", members), ("unresolved", unresolved), ("exclusionProfile", exclusionProfile), ("policyDigest", policyDigest),
                ("exclusionPolicyFileDigest", policyFileDigest), ("exclusionPlatform", platform), ("excludedUids", PsValue.Prop(map, "excludedUids")), ("warnings", warnings.ToArray()));
        }
    }
}
