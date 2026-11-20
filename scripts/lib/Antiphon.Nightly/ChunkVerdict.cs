// A chunk's coverage verdict (moved from nightly-coverage.ps1): the TRX and diagnostic evidence of one native launch
// judged against its suite's discovery, the expected-platform-skip roster that tolerates known skips, and the exact
// roster/coverage contract of an exact-member launch (CARD-1313). Rows and nodes are the readers' objects (TRX rows,
// diagnostic and discovery nodes as PowerShell objects), read through PsValue.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class NightlyChunkVerdict
    {
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant);
        private static readonly Regex MethodName = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        private static NightlyRefusal Refuse(string reason) => new NightlyRefusal(reason);

        private static PSObject Note(params object[] pairs)
        {
            var o = new PSObject();
            for (var i = 0; i < pairs.Length; i += 2) o.Properties.Add(new PSNoteProperty((string)pairs[i], pairs[i + 1]));
            return o;
        }

        // $o.Prop as a list: none for null (the PowerShell .Count of a missing property is 0).
        private static object[] Items(object o) => o == null ? new object[0] : PsValue.AsArray(o);

        // $o.PSObject.Properties.Name -contains name (case-insensitive).
        private static bool Has(object o, string name) => o != null && PSObject.AsPSObject(o).Properties[name] != null;

        private static bool Exists(string path) => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        // ConvertTo-NightlyCanonicalPath: separators normalised, full, without a trailing separator (the root keeps it).
        private static string CanonicalPath(string path)
        {
            var full = Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            var root = Path.GetPathRoot(full) ?? "";
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
        }

        private static bool Contained(string inner, string outer)
        {
            var a = CanonicalPath(inner);
            var b = CanonicalPath(outer);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Test-NightlyFreshEvidence: the evidence exists inside the run directory, was written no earlier than a second
        /// before notBeforeUtc, and (when an invocation id is given) names that invocation. { Ok, Reason }.
        /// </summary>
        public static PSObject Fresh(string path, string runDirectory, DateTime notBeforeUtc, string invocationId)
        {
            if (!Exists(path)) return Note("Ok", false, "Reason", "missing " + path);
            if (!Contained(path, runDirectory)) return Note("Ok", false, "Reason", "wrong-run-directory");
            var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.GetLastWriteTimeUtc(path);
            if (written.Ticks < notBeforeUtc.AddSeconds(-1).Ticks) return Note("Ok", false, "Reason", "stale-timestamp");
            if (!string.IsNullOrWhiteSpace(invocationId)
                && !Regex.IsMatch(File.ReadAllText(path), Regex.Escape(invocationId), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return Note("Ok", false, "Reason", "previous-invocation");
            return Note("Ok", true, "Reason", "");
        }

        /// <summary>Test-NightlyChunkMembership: every eligible class in exactly one chunk. { Ok, Duplicates, Missing }.</summary>
        public static PSObject ChunkMembership(string[] eligibleClasses, object chunks)
        {
            var assigned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            foreach (var chunk in PsValue.AsArray(chunks))
                foreach (var c in PsValue.AsArray(PsValue.Prop(chunk, "classes")))
                {
                    var name = PsValue.Str(c);
                    if (assigned.ContainsKey(name)) duplicates.Add(name); else assigned[name] = PsValue.Str(PsValue.Prop(chunk, "id"));
                }
            var missing = (eligibleClasses ?? new string[0]).Where(c => !assigned.ContainsKey(c ?? "")).ToArray();
            return Note("Ok", duplicates.Count == 0 && missing.Length == 0, "Duplicates", duplicates.ToArray(), "Missing", missing);
        }

        // ---- the expected-platform-skip roster ----------------------------------------------------------------

        /// <summary>
        /// Get-NightlyExpectedSkipRoster: the roster's entries (tests/expected-platform-skips.json), each checked: platform
        /// windows or linux, a Class.Method test, a reason prefix, and a known reason class.
        /// </summary>
        public static object[] SkipRoster(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw Refuse("expected-skip roster missing " + path);
            var doc = NightlyJson.ParseText(File.ReadAllText(path), 64, true);
            if (doc == null) throw Refuse("expected-skip roster unreadable " + path);
            var entries = PsValue.Prop(doc, "entries");
            if (entries == null) throw Refuse("expected-skip roster has no entries");
            var list = PsValue.AsArray(entries);
            foreach (var entry in list)
            {
                var platform = PsValue.Str(PsValue.Prop(entry, "platform"));
                var test = PsValue.Str(PsValue.Prop(entry, "test"));
                var prefix = PsValue.Str(PsValue.Prop(entry, "reasonPrefix"));
                if (platform != "windows" && platform != "linux") throw Refuse("expected-skip roster platform " + platform);
                if (string.IsNullOrWhiteSpace(test) || test.IndexOf('.') < 1) throw Refuse("expected-skip roster test " + test);
                if (string.IsNullOrWhiteSpace(prefix)) throw Refuse("expected-skip roster reason " + test);
                ReasonClass(entry);
            }
            return list;
        }

        /// <summary>
        /// Get-NightlyExpectedSkipStaleWarnings: "stale-roster:" + test for each entry of the platform whose test is not a
        /// known identity; none when the known identities are not given.
        /// </summary>
        public static string[] StaleWarnings(string platform, object entries, string[] knownTestIdentities) =>
            knownTestIdentities == null ? new string[0] : SuiteCompletionParts.StaleWarnings(platform ?? "", entries, knownTestIdentities);

        /// <summary>Get-NightlyExpectedSkipReasonClass: platform (the default) or live-context.</summary>
        public static string ReasonClass(object entry)
        {
            var value = Has(entry, "reasonClass") ? PsValue.Str(PsValue.Prop(entry, "reasonClass")) : "";
            if (string.IsNullOrWhiteSpace(value)) return "platform";
            if (value != "platform" && value != "live-context") throw Refuse("expected-skip roster reasonClass " + value);
            return value;
        }

        /// <summary>Get-NightlyExpectedSkipRosterDigest: the roster's digest; refuses an entry with an invalid reason class.</summary>
        public static string RosterDigest(object entries)
        {
            var digest = NightlyCoverageParts.RosterDigest(entries);
            if (digest != null) return digest;
            foreach (var entry in PsValue.AsArray(entries)) if (entry != null) ReasonClass(entry);
            throw Refuse("expected-skip roster digest refused");
        }

        /// <summary>
        /// Find-NightlyExpectedSkipEntry: the roster entry that explains a NotExecuted row on this platform - its test
        /// identity, a platform-scoped class on the same platform (live-context on any), and the exact skip trace.
        /// </summary>
        public static object FindSkipEntry(object row, string platform, object entries)
        {
            if (row == null || string.IsNullOrWhiteSpace(platform)) return null;
            if (NightlyClassNames.NormalizedOutcome(PsValue.Str(PsValue.Prop(row, "Outcome"))) != "NotExecuted") return null;
            var trace = Has(row, "DebugTrace") ? PsValue.Str(PsValue.Prop(row, "DebugTrace")) : "";
            var identity = PsValue.Str(PsValue.Prop(row, "Identity"));
            foreach (var entry in PsValue.AsArray(entries))
            {
                if (entry == null || PsValue.Str(PsValue.Prop(entry, "test")) != identity) continue;
                var reasonClass = ReasonClass(entry);
                var entryPlatform = PsValue.Str(PsValue.Prop(entry, "platform"));
                if (entryPlatform != "windows" && entryPlatform != "linux") continue;
                if (reasonClass == "platform" && entryPlatform != platform) continue;
                if (trace == "Skipped: " + PsValue.Str(PsValue.Prop(entry, "reasonPrefix"))) return entry;
            }
            return null;
        }

        // ---- verdicts ---------------------------------------------------------------------------------------------

        /// <summary>
        /// ConvertTo-NightlyCoverageVerdict: coverage is complete when every row ran (skips tolerated only as the roster
        /// explains them), the run is the expected sha, ref and policy; tests pass when no row failed or was unknown and the
        /// process exited 0 (exit 8 is tolerated when every present row is an expected skip).
        /// </summary>
        public static PSObject CoverageVerdict(object[] terminalRows, int processExit, string sha, string expectedSha, string gitRef, string expectedRef,
            string policyHash, string expectedPolicyHash, string[] requiredSkips, object[] discoveryNodes, bool usedDiscoveryAsExecution, string platform,
            object expectedSkipRoster, string[] knownTestIdentities)
        {
            var coverageComplete = true;
            var testsPassed = true;
            var reasons = new List<object>();
            var rows = terminalRows ?? new object[0];
            if (usedDiscoveryAsExecution) { coverageComplete = false; reasons.Add("discovery-as-execution"); }
            var inProgress = (discoveryNodes ?? new object[0]).Count(n => string.Equals(PsValue.Str(PsValue.Prop(n, "State")), "InProgress", StringComparison.OrdinalIgnoreCase));
            if (inProgress > 0 && rows.Length == 0) { coverageComplete = false; reasons.Add("in-progress-only"); }
            if (rows.Length == 0) { coverageComplete = false; testsPassed = false; reasons.Add("zero-terminal-rows"); }
            // One compiled pass classifies every row once.
            var classified = NightlyVerdicts.ClassifyOutcomes(rows);
            var failed = PsValue.AsArray(PsValue.Prop(classified, "Failed"));
            var skipped = PsValue.AsArray(PsValue.Prop(classified, "Skipped"));
            var unexecuted = PsValue.AsArray(PsValue.Prop(classified, "NotExecuted"));
            var unknown = PsValue.AsArray(PsValue.Prop(classified, "Unknown"));
            var passedCount = Convert.ToInt32(PsValue.Prop(classified, "Passed"), System.Globalization.CultureInfo.InvariantCulture);
            // Expected platform skips stay out of the unexecuted bucket; Outcome=Skipped is unchanged.
            var roster = Items(expectedSkipRoster);
            var expectedSkipRows = new List<object>();
            var stillUnexecuted = new List<object>();
            var toleratedSkips = new List<object>();
            foreach (var row in unexecuted)
            {
                var entry = FindSkipEntry(row, platform, roster);
                if (entry != null)
                {
                    expectedSkipRows.Add(row);
                    toleratedSkips.Add(Note("identity", PsValue.Str(PsValue.Prop(row, "Identity")), "reasonClass", ReasonClass(entry)));
                }
                else stillUnexecuted.Add(row);
            }
            var presentCount = NightlyCoverageParts.PresentCount(rows);
            var allExpectedSkips = presentCount > 0 && expectedSkipRows.Count == presentCount;
            var processExitTolerated = false;
            string Identities(IEnumerable<object> list) => string.Join(",", list.Select(r => PsValue.Str(PsValue.Prop(r, "Identity"))));
            if (failed.Length > 0) { testsPassed = false; reasons.Add("failed-row"); }
            if (stillUnexecuted.Count > 0) { coverageComplete = false; reasons.Add("unexecuted-outcome:" + Identities(stillUnexecuted)); }
            if (unknown.Length > 0) { coverageComplete = false; testsPassed = false; reasons.Add("unknown-outcome:" + Identities(unknown)); }
            if (processExit != 0)
            {
                if (processExit == 8 && allExpectedSkips) processExitTolerated = true;
                else { testsPassed = false; reasons.Add("process-exit-" + processExit.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            }
            if (skipped.Length > 0) { coverageComplete = false; reasons.Add("skipped:" + Identities(skipped)); }
            if (!string.Equals(sha ?? "", expectedSha ?? "", StringComparison.OrdinalIgnoreCase)) { coverageComplete = false; reasons.Add("sha-mismatch " + sha + " vs " + expectedSha); }
            if (!string.IsNullOrWhiteSpace(expectedRef) && !string.Equals(gitRef ?? "", expectedRef, StringComparison.OrdinalIgnoreCase))
            {
                coverageComplete = false;
                reasons.Add("ref-mismatch " + gitRef + " vs " + expectedRef);
            }
            if (!string.Equals(policyHash ?? "", expectedPolicyHash ?? "", StringComparison.OrdinalIgnoreCase))
            {
                coverageComplete = false;
                reasons.Add("policy-mismatch " + policyHash + " vs " + expectedPolicyHash);
            }
            var warnings = StaleWarnings(platform, roster, knownTestIdentities);
            return Note(
                "coverageComplete", coverageComplete, "testsPassed", testsPassed, "reasons", reasons.ToArray(), "passed", passedCount,
                "failed", failed.Length, "skipped", skipped.Length, "total", rows.Length,
                "skippedIdentities", skipped.Select(r => PsValue.Prop(r, "Identity")).ToArray(), "failedIdentities", failed.Select(r => PsValue.Prop(r, "Identity")).ToArray(),
                "expectedSkips", expectedSkipRows.Count, "warnings", warnings, "processExitTolerated", processExitTolerated,
                "toleratedSkips", toleratedSkips.ToArray(), "rosterDigest", RosterDigest(roster));
        }

        /// <summary>Test-NightlyCounterAgreement: the reported totals match the verdict's.</summary>
        public static bool CounterAgreement(object verdict, int reportedTotal, int reportedFailed) =>
            reportedTotal == Convert.ToInt32(PsValue.Prop(verdict, "total"), System.Globalization.CultureInfo.InvariantCulture)
            && reportedFailed == Convert.ToInt32(PsValue.Prop(verdict, "failed"), System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Test-NightlyRequiredClassesPresent: no required classes is present.</summary>
        public static PSObject RequiredClassesPresent(object[] terminalRows, string[] requiredClasses) =>
            requiredClasses == null || requiredClasses.Length == 0
                ? Note("Ok", true, "Missing", new object[0])
                : NightlyVerdicts.RequiredClassesPresent(terminalRows, requiredClasses);

        /// <summary>Test-NightlyExpandedRowsPresent: the required UIDs with no executed (Passed, Failed, Skipped) terminal row.</summary>
        public static PSObject ExpandedRowsPresent(object[] discoveryNodes, object[] terminalRows, string[] requiredUids) =>
            NightlyVerdicts.ExpandedRowsPresent(terminalRows, requiredUids);

        /// <summary>Test-NightlySuiteUidUnion: every required discovery UID ran in some chunk.</summary>
        public static PSObject SuiteUidUnion(object[] discoveryNodes, object[] chunkTerminalNodes) =>
            NightlyVerdicts.ExpandedRowsPresent(chunkTerminalNodes, NightlyCensus.RequiredUids(discoveryNodes, new string[0], null, false).Select(PsValue.Str).ToArray());

        /// <summary>
        /// ConvertTo-NightlyNativeSuiteVerdict: one native launch's TRX (fresh, in the run directory, a regular file) and
        /// execution diagnostic judged against the suite's discovery (through the discovery index) and its required
        /// classes. The TRX rows, terminal nodes, discovery nodes and census come back with the verdict.
        /// </summary>
        public static PSObject NativeSuiteVerdict(string trxPath, string discoveryPath, string executionDiagnosticPath, string runDirectory, DateTime notBeforeUtc,
            int processExit, string sha, string expectedSha, string gitRef, string expectedRef, string policyHash, string expectedPolicyHash,
            string[] requiredClasses, object profileExclusions, bool profileAware, NightlyDiscoveryIndex discoveryIndex, string platform, object expectedSkipRoster)
        {
            PSObject Refused(string reason, object rows = null, bool nodes = false) => nodes
                ? Note("coverageComplete", false, "testsPassed", false, "reasons", new object[] { reason }, "rows", rows ?? new object[0], "terminalNodes", new object[0], "discoveryNodes", new object[0])
                : Note("coverageComplete", false, "testsPassed", false, "reasons", new object[] { reason }, "rows", new object[0]);
            requiredClasses = requiredClasses ?? new string[0];
            if (!Exists(trxPath)) return Refused("missing-trx");
            if (TestPhaseFiles.Kind(trxPath, out _) != "regular") return Refused("refused-trx");
            var fresh = Fresh(trxPath, runDirectory, notBeforeUtc, "");
            if (!LanguagePrimitives.IsTrue(PsValue.Prop(fresh, "Ok"))) return Refused(PsValue.Str(PsValue.Prop(fresh, "Reason")));
            object[] rows;
            try { rows = NightlyTrx.ReadIdentities(trxPath); }
            catch (Exception ex) { return Refused(ex.Message); }
            var reasons = new List<object>();
            var discoveryNodes = new object[0];
            DiscoveryIndexEntry discoveryEntry = null;
            var discoveryPresent = false;
            if (!Exists(discoveryPath)) reasons.Add("missing-discovery");
            else
            {
                try
                {
                    discoveryEntry = (discoveryIndex ?? new NightlyDiscoveryIndex()).Entry(discoveryPath);
                    discoveryNodes = PsValue.AsArray(PsValue.Prop(discoveryEntry.Document, "Nodes"));
                    if (discoveryNodes.Length == 0) reasons.Add("empty-discovery"); else discoveryPresent = true;
                }
                catch (Exception ex) { return Refused(ex.Message, rows, true); }
            }
            var terminalNodes = new object[0];
            var execPresent = false;
            var execPath = executionDiagnosticPath;
            if (!string.IsNullOrWhiteSpace(execPath) && Directory.Exists(execPath)) execPath = NightlyDiagnosticLog.Latest(execPath);
            if (!Exists(execPath)) { if (discoveryPresent) reasons.Add("missing-execution-diagnostic"); }
            else
            {
                try
                {
                    terminalNodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.Read(execPath, "execution"), "Nodes"));
                    execPresent = true;
                }
                catch (Exception ex) { reasons.Add(ex.Message); }
            }
            // The coverage verdict reads discovery only for its InProgress nodes.
            var verdict = CoverageVerdict(rows, processExit, sha, expectedSha, gitRef, expectedRef, policyHash, expectedPolicyHash, new string[0],
                discoveryEntry?.InProgress ?? new object[0], false, platform, expectedSkipRoster, null);
            var coverageComplete = LanguagePrimitives.IsTrue(PsValue.Prop(verdict, "coverageComplete"));
            var testsPassed = LanguagePrimitives.IsTrue(PsValue.Prop(verdict, "testsPassed"));
            reasons.AddRange(PsValue.AsArray(PsValue.Prop(verdict, "reasons")));
            object census = null;
            if (!discoveryPresent) coverageComplete = false;
            else
            {
                var view = NightlyDiscoveryIndex.View(discoveryEntry, profileExclusions, profileAware);
                var requiredUids = NightlyDiscoveryIndex.RequiredUids(view, requiredClasses).Select(PsValue.Str).ToArray();
                census = PsValue.Prop(view, "Census");
                var duplicates = Items(PsValue.Prop(census, "Duplicates"));
                if (duplicates.Length > 0)
                {
                    coverageComplete = false;
                    reasons.Add("duplicate-discovery-uid " + string.Join(",", duplicates.Select(PsValue.Str)));
                }
                if (profileAware && Convert.ToInt32(PsValue.Prop(census, "RequiredCount"), System.Globalization.CultureInfo.InvariantCulture) == 0)
                {
                    // D-5: a profile lane never reports green on an empty required set.
                    coverageComplete = false;
                    testsPassed = false;
                    reasons.Add("zero-required-uid");
                }
                if (!execPresent) coverageComplete = false;
                else
                {
                    var expanded = NightlyVerdicts.ExpandedRowsPresent(terminalNodes, requiredUids);
                    if (!LanguagePrimitives.IsTrue(PsValue.Prop(expanded, "Ok")))
                    {
                        coverageComplete = false;
                        reasons.Add("missing-expanded-row " + string.Join(",", Items(PsValue.Prop(expanded, "Missing")).Select(PsValue.Str)));
                    }
                    var cross = NightlyVerdicts.CrossCheck(rows, terminalNodes, requiredClasses);
                    if (!LanguagePrimitives.IsTrue(PsValue.Prop(cross, "Ok")))
                    {
                        coverageComplete = false;
                        var mismatch = Items(PsValue.Prop(cross, "Mismatch"));
                        var extra = Items(PsValue.Prop(cross, "ExtraClasses"));
                        if (mismatch.Length > 0) reasons.Add("trx-diagnostic-mismatch " + string.Join(",", mismatch.Select(PsValue.Str)));
                        if (extra.Length > 0) reasons.Add("extra-class-in-chunk " + string.Join(",", extra.Select(PsValue.Str)));
                    }
                }
                var classReq = requiredClasses.Length > 0 ? requiredClasses : PsValue.AsArray(PsValue.Prop(view, "Classes")).Select(PsValue.Str).ToArray();
                if (classReq.Length > 0)
                {
                    var member = NightlyVerdicts.RequiredClassesPresent(rows, classReq);
                    if (!LanguagePrimitives.IsTrue(PsValue.Prop(member, "Ok")))
                    {
                        coverageComplete = false;
                        testsPassed = false;
                        reasons.Add("missing-required-class " + string.Join(",", Items(PsValue.Prop(member, "Missing")).Select(PsValue.Str)));
                    }
                }
            }
            return Note(
                "coverageComplete", coverageComplete, "testsPassed", testsPassed, "reasons", reasons.ToArray(), "rows", rows, "terminalNodes", terminalNodes,
                "discoveryNodes", discoveryNodes, "census", census, "processExitTolerated", LanguagePrimitives.IsTrue(PsValue.Prop(verdict, "processExitTolerated")),
                "warnings", PsValue.AsArray(PsValue.Prop(verdict, "warnings")), "toleratedSkips", PsValue.AsArray(PsValue.Prop(verdict, "toleratedSkips")),
                "rosterDigest", PsValue.Str(PsValue.Prop(verdict, "rosterDigest")));
        }

        // ---- exact members (CARD-1313) ----------------------------------------------------------------------------

        /// <summary>
        /// Test-NightlyExactCoverage: exact membership is a multiset contract over expanded UIDs - every required UID ran
        /// once and passed, nothing else ran. { Ok, Reasons, Required, Executed }.
        /// </summary>
        public static PSObject ExactCoverage(string[] requiredUids, object[] terminalNodes)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            var nodes = terminalNodes ?? new object[0];
            foreach (var uid in requiredUids ?? new string[0]) if (string.IsNullOrEmpty(uid) || !required.Add(uid)) reasons.Add("invalid-required");
            if (required.Count == 0 || nodes.Length == 0) reasons.Add("zero-required");
            foreach (var node in nodes)
            {
                var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
                if (!seen.Add(uid)) reasons.Add("duplicate-uid");
                if (!required.Contains(uid)) reasons.Add("extra-uid");
                var outcome = NightlyClassNames.NormalizedOutcome(PsValue.Str(PsValue.Prop(node, "Outcome")));
                if (outcome == "Skipped") reasons.Add("required-skip");
                else if (outcome != "Passed") reasons.Add("failed-outcome");
            }
            foreach (var uid in required) if (!seen.Contains(uid)) reasons.Add("missing-uid");
            return Note("Ok", reasons.Count == 0, "Reasons", reasons.ToArray(), "Required", required.Count, "Executed", nodes.Length);
        }

        /// <summary>
        /// New-NightlyExactRoster: the discovery nodes of the exact members (class, or class and method), one chunk per
        /// filter /*/namespace/type/method. classifiedUids: when given, an unclassified discovered UID widens the launch
        /// to the whole assembly (unknown resources cannot run unattended). { AssemblyHash, Chunks, RequiredUids,
        /// Widened, UnresolvedUids }.
        /// </summary>
        public static PSObject ExactRoster(object discovery, object[] members, string[] classifiedUids)
        {
            var parsed = NightlyDiagnosticLog.DiscoveryDocument(discovery, NightlyDiagnosticLog.DiscoveryFormat);
            var nodes = PsValue.AsArray(PsValue.Prop(parsed, "Nodes"));
            if (nodes.Length == 0) throw Refuse("empty-discovery");
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var selectedOrder = new List<string>();
            void Select(string uid) { if (selected.Add(uid)) selectedOrder.Add(uid); }
            foreach (var member in members ?? new object[0])
            {
                var cls = NightlyCensus.BareClassIdentity(PsValue.Str(PsValue.Prop(member, "class"))) ?? "";
                var method = PsValue.Str(PsValue.Prop(member, "method"));
                if (!Identifier.IsMatch(cls) || (method.Length > 0 && !MethodName.IsMatch(method))) throw Refuse("unresolvable-exact-member");
                var matches = nodes.Where(n => NightlyCensus.BareClassIdentity(PsValue.Str(PsValue.Prop(n, "ClassName"))) == cls
                    && (method.Length == 0 || PsValue.Str(PsValue.Prop(n, "Method")) == method)).ToList();
                if (matches.Count == 0) throw Refuse("missing-exact-member:" + cls + "." + method);
                foreach (var node in matches) Select(PsValue.Str(PsValue.Prop(node, "Uid")));
            }
            var unresolved = new object[0];
            if (classifiedUids != null)
            {
                var known = new HashSet<string>(classifiedUids, StringComparer.Ordinal);
                unresolved = nodes.Select(n => PsValue.Str(PsValue.Prop(n, "Uid"))).Where(u => !known.Contains(u)).Cast<object>().ToArray();
                // Widen the owning assembly before launch.
                if (unresolved.Length > 0) foreach (var node in nodes) Select(PsValue.Str(PsValue.Prop(node, "Uid")));
            }
            if (selected.Count == 0) throw Refuse("zero-required");
            var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                var uid = PsValue.Str(PsValue.Prop(node, "Uid"));
                if (!selected.Contains(uid)) continue;
                var ns = PsValue.Str(PsValue.Prop(node, "Namespace"));
                var bareType = NightlyCensus.BareClassIdentity(PsValue.Str(PsValue.Prop(node, "Type"))) ?? "";
                var method = PsValue.Str(PsValue.Prop(node, "Method"));
                foreach (var part in new[] { ns, bareType, method }) if (!Identifier.IsMatch(part ?? "")) throw Refuse("unresolvable-filter-identity");
                var filter = "/*/" + ns + "/" + bareType + "/" + method;
                if (!groups.TryGetValue(filter, out var list)) groups[filter] = list = new List<string>();
                list.Add(uid);
            }
            var chunks = groups.Select(g =>
            {
                var uids = g.Value.ToArray();
                Array.Sort(uids, StringComparer.Ordinal);
                return (object)Note("Filter", g.Key, "RequiredUids", uids);
            }).ToArray();
            return Note("AssemblyHash", PsValue.Prop(parsed, "AssemblyHash"), "Chunks", chunks, "RequiredUids", selectedOrder.ToArray(),
                "Widened", unresolved.Length > 0, "UnresolvedUids", unresolved);
        }
    }
}
