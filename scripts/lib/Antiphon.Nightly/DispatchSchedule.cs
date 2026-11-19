// The dispatch schedule, compiled: New-NightlyDispatchSchedule, Resolve-NightlyPreviousRunOrder and
// Get-NightlyPreviousRunKind (nightly-dispatch.ps1 calls these), and the stopped-run seed's ordering and content check
// (StoppedSeed), which the seed reader and the history resolver call.
//
// Each method returns what its PowerShell body returned: the same objects (rows, lanes, schedule) with the same
// property names, order and value types, the same refusal texts and the same orders. The operators keep their
// PowerShell meaning: -ceq, -cne, -cin and -ccontains go through LanguagePrimitives.Equals with the invariant
// culture (a list on the left of -eq/-ne filters, as it does in PowerShell), [string] is PsValue.Str, @() is
// PsValue.AsArray, -cmatch is the same regular expression. Sets and maps keep the comparer the body gave them.
// One schedule of 841 chunks took the PowerShell bodies about 0.6 s; this takes a few milliseconds.
//
// canonicalPlanDigest is the SHA-256 of the canonical rows written as compact JSON by PlanJson: every chunk value
// the planner can hold is written by type (strings escaped, numbers in invariant round-trip form, dates in ISO 8601,
// objects as their properties in order), so equal plans give equal digests and a changed value changes it. A
// previous run records the digest and the next run compares it.
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
    /// <summary>New-NightlyDispatchSchedule's result: Error (the refusal it threw), else Schedule.</summary>
    public sealed class DispatchBuild
    {
        public string Error { get; internal set; }
        public PSObject Schedule { get; internal set; }
    }

    public static class DispatchSchedule
    {
        internal const string SeedOrderReason = "operator-seed-order: failed methods, then each lane resumed at its interrupted chunk (else after its last pass): interrupted, then untouched, then siblings, then passed; known timestamps oldest first within failed, sibling and passed";

        private static readonly Regex Sha40 = new Regex("^[a-f0-9]{40}$");
        private static readonly Regex Sha64 = new Regex("^[a-f0-9]{64}$");
        private static readonly object[] FailResults = { "FAIL", "Failed", "TIMEOUT", "Error" };
        private static readonly object[] PassResults = { "pass", "Passed", "PASS" };
        private static readonly object[] KnownResults = { "unstarted", "incomplete", "cancelled", "Skipped", "skip", "pass", "Passed", "PASS" };

        /// <summary>New-NightlyDispatchSchedule.</summary>
        public static DispatchBuild Build(object[] preparedSuites, object[] priorityMembers, string historyDigest,
            object[] unresolved, bool preparationComplete, string runId, string attempt, object previousRun,
            string candidateSha, DateTime nowUtc, string historyFallbackReason)
        {
            var build = new DispatchBuild();
            var rows = new List<PSObject>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var canonicalSuites = new List<string>();
            var canonicalChunks = new List<object>();
            // Each priority member's suite and chunk list, read once (PowerShell read them for every chunk).
            var members = new List<PriorityMember>();
            foreach (var member in priorityMembers ?? new object[0])
                members.Add(new PriorityMember(PsValue.Prop(member, "suite"), PsValue.AsArray(PsValue.Prop(member, "chunks"))));
            foreach (var suite in preparedSuites ?? new object[0])
            {
                var suiteIdValue = PsValue.Prop(suite, "id");
                var suiteId = PsValue.Str(suiteIdValue);
                // $dispatchMember.suite -ceq $dispatchSuite.id does not depend on the chunk: once per suite.
                var suiteMembers = new List<PriorityMember>();
                foreach (var member in members) if (PsOps.Eq(member.Suite, suiteIdValue, false)) suiteMembers.Add(member);
                foreach (var chunk in PsValue.AsArray(PsValue.Prop(suite, "chunks")))
                {
                    var chunkId = PsValue.Str(PsValue.Prop(chunk, "id"));
                    if (!keys.Add(suiteId + "\0" + chunkId)) { build.Error = "duplicate-canonical-chunk"; return build; }
                    var priority = false;
                    foreach (var member in suiteMembers)
                    {
                        if (member.Holds(chunkId)) { priority = true; break; }
                    }
                    canonicalSuites.Add(suiteId);
                    canonicalChunks.Add(chunk);
                    var row = new PSObject();
                    row.Properties.Add(new PSNoteProperty("suite", suiteId));
                    row.Properties.Add(new PSNoteProperty("chunk", chunkId));
                    row.Properties.Add(new PSNoteProperty("canonicalOrdinal", rows.Count));
                    row.Properties.Add(new PSNoteProperty("dispatchOrdinal", rows.Count));
                    row.Properties.Add(new PSNoteProperty("priorFailure", priority));
                    row.Properties.Add(new PSNoteProperty("SuiteState", suite));
                    row.Properties.Add(new PSNoteProperty("ChunkRef", chunk));
                    rows.Add(row);
                }
            }
            var canonicalRows = rows.ToArray();
            // Stable partition of references. Do not repack, copy or renumber a chunk.
            var ordered = new List<object>();
            foreach (var row in rows) if ((bool)row.Properties["priorFailure"].Value) ordered.Add(row);
            foreach (var row in rows) if (!(bool)row.Properties["priorFailure"].Value) ordered.Add(row);
            PSObject previousOrder = null;
            if (previousRun != null)
            {
                previousOrder = PreviousRunOrder(canonicalRows, previousRun, candidateSha, nowUtc);
                ordered = new List<object>(PsValue.AsArray(PsValue.Prop(previousOrder, "entries")));
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < ordered.Count; i++)
            {
                var row = ordered[i];
                SetProp(row, "dispatchOrdinal", i);
                if (!seen.Add(PsValue.Str(PsValue.Prop(row, "suite")) + "\0" + PsValue.Str(PsValue.Prop(row, "chunk"))))
                { build.Error = "dispatch-not-permutation"; return build; }
            }
            if (ordered.Count != rows.Count || !seen.SetEquals(keys)) { build.Error = "dispatch-not-permutation"; return build; }

            var digest = TestPhaseRecords.Sha256Text(PlanJson(canonicalSuites, canonicalChunks));
            {
                var entries = ordered;
                var order = previousOrder;
                if (previousRun != null && PreviousRunKind(previousRun) == "previous-run")
                {
                    var recorded = "";
                    if (PsOps.HasName(previousRun, "canonicalPlanDigest") && PsValue.Prop(previousRun, "canonicalPlanDigest") != null)
                        recorded = PsValue.Str(PsValue.Prop(previousRun, "canonicalPlanDigest"));
                    var digestReason = "";
                    if (string.IsNullOrWhiteSpace(recorded)) digestReason = "previous-run-plan-digest-missing";
                    else if (!Sha64.IsMatch(recorded)) digestReason = "previous-run-plan-digest-malformed";
                    else if (!PsOps.StrEq(recorded, digest)) digestReason = "previous-run-plan-mismatch";
                    if (digestReason.Length > 0)
                    {
                        entries = new List<object>(canonicalRows);
                        for (var i = 0; i < entries.Count; i++) SetProp(entries[i], "dispatchOrdinal", i);
                        order = new PSObject();
                        order.Properties.Add(new PSNoteProperty("reason", digestReason));
                        order.Properties.Add(new PSNoteProperty("lanes", new object[0]));
                    }
                }
                var schedule = new PSObject();
                schedule.Properties.Add(new PSNoteProperty("PreparedSuites", PsValue.AsArray(preparedSuites)));
                schedule.Properties.Add(new PSNoteProperty("format", "antiphon-nightly-dispatch-v1"));
                schedule.Properties.Add(new PSNoteProperty("runId", runId ?? ""));
                schedule.Properties.Add(new PSNoteProperty("attempt", attempt ?? ""));
                schedule.Properties.Add(new PSNoteProperty("historyDigest", historyDigest ?? ""));
                schedule.Properties.Add(new PSNoteProperty("canonicalPlanDigest", digest));
                schedule.Properties.Add(new PSNoteProperty("preparationComplete", preparationComplete));
                schedule.Properties.Add(new PSNoteProperty("unresolved", PsValue.AsArray(unresolved ?? new object[0])));
                schedule.Properties.Add(new PSNoteProperty("entries", entries.ToArray()));
                if (order != null)
                {
                    schedule.Properties.Add(new PSNoteProperty("orderingReason", PsValue.Prop(order, "reason")));
                    schedule.Properties.Add(new PSNoteProperty("lanes", PsValue.Prop(order, "lanes")));
                }
                if (!string.IsNullOrEmpty(historyFallbackReason))
                    schedule.Properties.Add(new PSNoteProperty("historyFallbackReason", historyFallbackReason));
                build.Schedule = schedule;
            }
            return build;
        }

        /// <summary>Get-NightlyPreviousRunKind: previous-run, operator-seed, seed-marker-missing or seed-foreign-kind.</summary>
        public static string PreviousRunKind(object record)
        {
            // operator-seed is only the builder's content: exact seedKind, chunks and failedTests,
            // and not a dispatchProgress record. A marker stamped on a normal record stays previous-run.
            if (record == null) return "previous-run";
            var normal = PsOps.HasName(record, "suites") || PsOps.HasName(record, "liveness") || PsOps.HasName(record, "schemaVersion");
            object chunks = null, failed = null;
            if (PsOps.HasName(record, "chunks")) chunks = PsValue.Prop(record, "chunks");
            if (PsOps.HasName(record, "failedTests")) failed = PsValue.Prop(record, "failedTests");
            var seedShape = !normal && PsOps.IsArray(chunks) && PsOps.IsArray(failed) && ((Array)PsOps.Base(chunks)).Length > 0;
            if (!seedShape) return "previous-run";
            var marker = "";
            if (PsOps.HasName(record, "seedKind") && PsValue.Prop(record, "seedKind") != null) marker = PsValue.Str(PsValue.Prop(record, "seedKind"));
            if (string.IsNullOrWhiteSpace(marker)) return "seed-marker-missing";
            if (!PsOps.StrEq(marker, "operator-seed-from-stopped-run")) return "seed-foreign-kind";
            return "operator-seed";
        }

        /// <summary>Resolve-NightlyPreviousRunOrder: { entries, reason, lanes }.</summary>
        public static PSObject PreviousRunOrder(object[] entries, object previousRun, string candidateSha, DateTime nowUtc)
        {
            var kind = PreviousRunKind(previousRun);
            if (kind == "operator-seed") return StoppedSeedOrder(entries, previousRun);
            if (kind == "seed-foreign-kind") return Order(PsValue.AsArray(entries), "operator-seed-kind-foreign", new object[0]);
            if (kind == "seed-marker-missing") return Order(PsValue.AsArray(entries), "previous-run-plan-digest-missing", new object[0]);
            // This is a preference for a NEW complete attempt, never execution resume.
            // Missing rows are unfinished. Rotate each lane after its last observed pass,
            // wrapping earlier holes back into the unfinished partition.
            var reason = "previous-run-missing";
            var lanes = new List<object>();
            var all = entries ?? new object[0];
            try
            {
                if (previousRun == null) throw new NightlyRefusal(reason);
                var livenessText = "";
                if (PsOps.HasName(previousRun, "liveness") && PsValue.Prop(previousRun, "liveness") != null) livenessText = PsValue.Str(PsValue.Prop(previousRun, "liveness"));
                if (string.IsNullOrWhiteSpace(livenessText)) throw new NightlyRefusal("previous-run-liveness-missing");
                if (!PsOps.StrEq(livenessText, "closed")) throw new NightlyRefusal("previous-run-liveness-unknown");
                reason = "previous-run-schema";
                var schemaText = "";
                if (PsOps.HasName(previousRun, "schemaVersion") && PsValue.Prop(previousRun, "schemaVersion") != null) schemaText = PsValue.Str(PsValue.Prop(previousRun, "schemaVersion"));
                if (string.IsNullOrWhiteSpace(schemaText)) throw new NightlyRefusal("previous-run-schema-missing");
                if (!PsOps.StrEq(schemaText, "1")) throw new NightlyRefusal(reason);
                var suites = PsValue.Prop(previousRun, "suites");
                if (!PsOps.IsArray(suites)) throw new NightlyRefusal(reason);
                var shaText = "";
                if (PsOps.HasName(previousRun, "sha") && PsValue.Prop(previousRun, "sha") != null) shaText = PsValue.Str(PsValue.Prop(previousRun, "sha"));
                if (string.IsNullOrWhiteSpace(shaText)) throw new NightlyRefusal("previous-run-candidate-missing");
                if (!Sha40.IsMatch(shaText)) throw new NightlyRefusal(reason);
                if (!PsOps.StrEq(shaText, candidateSha ?? "")) throw new NightlyRefusal("previous-run-candidate-mismatch");
                var completedAt = PsValue.Prop(previousRun, "completedAt");
                DateTimeOffset completed;
                if (PsOps.Base(completedAt) is DateTime completedDate) completed = new DateTimeOffset(completedDate);
                else if (!DateTimeOffset.TryParse(PsValue.Str(completedAt), out completed)) throw new NightlyRefusal(reason);
                var now = nowUtc.ToUniversalTime();
                if (completed.UtcDateTime > now.AddMinutes(5) || completed.UtcDateTime < now.AddDays(-7)) throw new NightlyRefusal("previous-run-stale");
                var known = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in all) if (!known.Add(RowKey(entry))) throw new NightlyRefusal("canonical-chunk-duplicate");
                var states = new Dictionary<string, string>(StringComparer.Ordinal);
                var lastPassed = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var row in (IEnumerable)PsOps.Base(suites))
                {
                    var id = PsValue.Str(PsValue.Prop(row, "id"));
                    var chunk = PsValue.Str(PsValue.Prop(row, "chunk"));
                    var key = id + "\0" + chunk;
                    if (!known.Contains(key)) throw new NightlyRefusal("previous-run-roster-mismatch");
                    if (states.ContainsKey(key)) throw new NightlyRefusal("previous-run-duplicate");
                    var state = "unfinished";
                    var result = PsValue.Prop(row, "result");
                    var exitCode = PsValue.Prop(row, "exitCode");
                    if (PsOps.Eq(PsValue.Prop(row, "timedOut"), true, true) || PsOps.In(result, FailResults, false) || (exitCode != null && PsOps.ToInt(exitCode) != 0)) state = "failed";
                    else if (PsOps.In(result, PassResults, false) && exitCode != null && PsOps.ToInt(exitCode) == 0
                        && PsOps.Ne(PsValue.Prop(row, "skipped"), true, true) && PsOps.Ne(PsValue.Prop(row, "coverageComplete"), false, true))
                    { state = "passed"; lastPassed[id] = chunk; }
                    else if (!PsOps.In(result, KnownResults, false)) throw new NightlyRefusal("previous-run-state");
                    states.Add(key, state);
                }
                var failedRows = new List<object>(); var unfinished = new List<object>(); var passed = new List<object>();
                foreach (var entry in all)
                {
                    if (PsOps.Ne(PsValue.Prop(entry, "priorFailure"), true, true) && states.TryGetValue(RowKey(entry), out var st) && PsOps.StrEq(st, "passed")) passed.Add(entry);
                }
                // A lane's entries in order, grouped once. InvariantCulture is the comparison -ceq makes.
                var laneRows = new Dictionary<string, List<object>>(StringComparer.InvariantCulture);
                foreach (var entry in all)
                {
                    var laneKey = PsValue.Str(PsValue.Prop(entry, "suite"));
                    if (!laneRows.TryGetValue(laneKey, out var list)) laneRows[laneKey] = list = new List<object>();
                    list.Add(entry);
                }
                foreach (var lane in UniqueLanes(all))
                {
                    var rows = laneRows.TryGetValue(lane, out var laneList) ? laneList.ToArray() : new object[0];
                    var last = -1;
                    if (lastPassed.TryGetValue(lane, out var passedChunk))
                    {
                        for (var i = 0; i < rows.Length; i++) if (PsOps.Eq(PsValue.Prop(rows[i], "chunk"), passedChunk, false)) { last = i; break; }
                    }
                    var laneObject = new PSObject();
                    laneObject.Properties.Add(new PSNoteProperty("suite", lane));
                    laneObject.Properties.Add(new PSNoteProperty("lastPassedChunk", last >= 0 ? PsValue.Prop(rows[last], "chunk") : null));
                    lanes.Add(laneObject);
                    for (var offset = 1; offset <= rows.Length; offset++)
                    {
                        var entry = rows[(last + offset) % rows.Length];
                        var key = RowKey(entry);
                        var hasState = states.TryGetValue(key, out var st);
                        if (PsOps.Eq(PsValue.Prop(entry, "priorFailure"), true, true) || (hasState && PsOps.StrEq(st, "failed"))) failedRows.Add(entry);
                        else if (!hasState || PsOps.StrEq(st, "unfinished")) unfinished.Add(entry);
                    }
                }
                var ordered = new List<object>(failedRows);
                ordered.AddRange(unfinished);
                ordered.AddRange(passed);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in ordered) if (!seen.Add(RowKey(entry))) throw new NightlyRefusal("previous-run-not-permutation");
                if (ordered.Count != all.Length || !seen.SetEquals(known)) throw new NightlyRefusal("previous-run-not-permutation");
                return Order(ordered.ToArray(), "previous-run-order", lanes.ToArray());
            }
            catch (Exception e) { return Order(PsValue.AsArray(entries), Message(e), new object[0]); }
        }

        /// <summary>The stopped-run seed order: { entries, reason, lanes } (PreviousRunOrder calls it for a seed).</summary>
        public static PSObject StoppedSeedOrder(object[] entries, object seed)
        {
            try
            {
                StoppedSeed.AssertContent(seed);
                // In place of a plan digest: the stopped run's own candidate sha and run id.
                // sourceSha is not the new run's candidate. Chunk ids are not binding.
                // Failures lead by method. A chunk node list names the methods it runs.
                // No node list means the whole class, so that chunk stays at the front.
                // A failed method the plan dropped is ignored. A method that moved follows its new chunk.
                var shaText = "";
                if (PsOps.HasName(seed, "sourceSha") && PsValue.Prop(seed, "sourceSha") != null) shaText = PsValue.Str(PsValue.Prop(seed, "sourceSha"));
                if (string.IsNullOrWhiteSpace(shaText)) throw new NightlyRefusal("seed-candidate-missing");
                if (!Sha40.IsMatch(shaText)) throw new NightlyRefusal("seed-candidate-malformed");
                var runText = "";
                if (PsOps.HasName(seed, "sourceRunId") && PsValue.Prop(seed, "sourceRunId") != null) runText = PsValue.Str(PsValue.Prop(seed, "sourceRunId"));
                if (string.IsNullOrWhiteSpace(runText)) throw new NightlyRefusal("seed-run-missing");
                var failedMethods = new HashSet<string>(StringComparer.Ordinal);
                var failedClasses = new HashSet<string>(StringComparer.Ordinal);
                foreach (var test in PsValue.AsArray(PsValue.Prop(seed, "failedTests")))
                {
                    var cls = NightlyClassNames.Bare(PsValue.Str(PsValue.Prop(test, "className")));
                    var method = StoppedSeed.MethodName(PsValue.Str(PsValue.Prop(test, "method")));
                    if (string.IsNullOrWhiteSpace(cls) || string.IsNullOrWhiteSpace(method)) continue;
                    var classKey = PsValue.Str(PsValue.Prop(test, "suite")) + "\0" + cls;
                    failedMethods.Add(classKey + "\0" + method);
                    failedClasses.Add(classKey);
                }
                var historyByClass = new Dictionary<string, List<object>>(StringComparer.Ordinal);
                foreach (var row in PsValue.AsArray(PsValue.Prop(seed, "chunks")))
                {
                    var rowSuite = PsValue.Str(PsValue.Prop(row, "suite"));
                    foreach (var className in PsValue.AsArray(PsValue.Prop(row, "classes")))
                    {
                        var key = rowSuite + "\0" + NightlyClassNames.Bare(PsValue.Str(className));
                        if (!historyByClass.TryGetValue(key, out var list)) historyByClass[key] = list = new List<object>();
                        list.Add(row);
                    }
                }
                var prepared = new List<SeedRow>();
                var placedMethods = new HashSet<string>(StringComparer.Ordinal);
                var wholeClasses = new HashSet<string>(StringComparer.Ordinal);
                var historyFold = new Dictionary<string, Fold>(StringComparer.Ordinal);
                var mapped = 0;
                foreach (var entry in entries ?? new object[0])
                {
                    var entrySuite = PsValue.Str(PsValue.Prop(entry, "suite"));
                    var chunkRef = PsValue.Prop(entry, "ChunkRef");
                    var classSource = PsValue.AsArray(PsValue.Prop(chunkRef, "classes"));
                    var bare = new List<object>();
                    if (classSource.Length == 0)
                    {
                        var fallback = new object[0];
                        var chunkNodes = PsValue.Prop(chunkRef, "nodes");
                        if (chunkNodes != null) fallback = PsValue.AsArray(chunkNodes);
                        var suiteNodes = PsValue.Prop(PsValue.Prop(entry, "SuiteState"), "nodes");
                        if (fallback.Length == 0 && suiteNodes != null) fallback = PsValue.AsArray(suiteNodes);
                        foreach (var node in fallback) bare.Add(NightlyClassNames.Bare(PsValue.Str(PsValue.Prop(node, "ClassName"))));
                    }
                    else
                    {
                        foreach (var cls in classSource) bare.Add(NightlyClassNames.Bare(PsValue.Str(cls)));
                    }
                    var classes = OrdinalStrings(bare);
                    var methodKeys = new List<string>();
                    var hasNodes = false;
                    var nodes = PsValue.Prop(chunkRef, "nodes");
                    if (nodes != null)
                    {
                        foreach (var node in PsValue.AsArray(nodes))
                        {
                            if (node == null) continue;
                            var nodeClass = "";
                            if (PsOps.HasName(node, "ClassName")) nodeClass = NightlyClassNames.Bare(PsValue.Str(PsValue.Prop(node, "ClassName")));
                            var nodeMethod = "";
                            if (PsOps.HasName(node, "Method") && !string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(node, "Method")))) nodeMethod = StoppedSeed.MethodName(PsValue.Str(PsValue.Prop(node, "Method")));
                            else if (PsOps.HasName(node, "MethodName")) nodeMethod = StoppedSeed.MethodName(PsValue.Str(PsValue.Prop(node, "MethodName")));
                            if (string.IsNullOrWhiteSpace(nodeClass) || string.IsNullOrWhiteSpace(nodeMethod)) continue;
                            var methodKey = entrySuite + "\0" + nodeClass + "\0" + nodeMethod;
                            methodKeys.Add(methodKey);
                            placedMethods.Add(methodKey);
                            hasNodes = true;
                        }
                    }
                    if (!hasNodes) foreach (var cls in classes) wholeClasses.Add(entrySuite + "\0" + cls);
                    var seenAll = classes.Length > 0; var incomplete = false; var interrupted = false; DateTimeOffset? last = null;
                    foreach (var cls in classes)
                    {
                        var key = entrySuite + "\0" + cls;
                        if (!historyByClass.TryGetValue(key, out var history)) { seenAll = false; continue; }
                        mapped++;
                        // One class's history folds the same way for every chunk that holds it, so fold it once: a class
                        // split by method into N chunks otherwise reads its N rows N times.
                        if (!historyFold.TryGetValue(key, out var fold))
                        {
                            fold = new Fold();
                            foreach (var old in history)
                            {
                                if (!LanguagePrimitives.IsTrue(PsValue.Prop(old, "completed")))
                                {
                                    fold.Incomplete = true;
                                    if (PsOps.Eq(PsValue.Prop(old, "started"), true, true)) fold.Interrupted = true;
                                }
                                var time = StoppedSeed.Utc(PsValue.Prop(old, "lastRunAtUtc"));
                                if (time.HasValue && (fold.Last == null || time.Value > fold.Last.Value)) fold.Last = time;
                            }
                            historyFold[key] = fold;
                        }
                        if (fold.Incomplete) incomplete = true;
                        if (fold.Interrupted) interrupted = true;
                        if (fold.Last.HasValue && (last == null || fold.Last.Value > last.Value)) last = fold.Last;
                    }
                    prepared.Add(new SeedRow { Entry = entry, Suite = entrySuite, Classes = classes, HasNodes = hasNodes, Methods = methodKeys, Seen = seenAll, Incomplete = incomplete, Interrupted = interrupted, Last = last });
                }
                if (mapped == 0) throw new NightlyRefusal("seed-plan-unmapped");
                var presentClass = new HashSet<string>(StringComparer.Ordinal);
                foreach (var methodKey in failedMethods)
                {
                    var parts = methodKey.Split('\0');
                    var classKey = parts[0] + "\0" + parts[1];
                    if (placedMethods.Contains(methodKey) || wholeClasses.Contains(classKey)) presentClass.Add(classKey);
                }
                var ranked = new List<Ranked>();
                foreach (var row in prepared)
                {
                    var holdsFailure = false;
                    if (row.HasNodes) { foreach (var methodKey in row.Methods) if (failedMethods.Contains(methodKey)) { holdsFailure = true; break; } }
                    else { foreach (var cls in row.Classes) if (failedClasses.Contains(row.Suite + "\0" + cls)) { holdsFailure = true; break; } }
                    var sibling = false;
                    if (!holdsFailure) foreach (var cls in row.Classes) if (presentClass.Contains(row.Suite + "\0" + cls)) { sibling = true; break; }
                    // 0: this chunk contains a failed method, or it has no node list and its class failed.
                    // 1: never-seen or unfinished. Each lane resumes in place, below.
                    // 2: another chunk of a class whose failed method is already placed (P0b).
                    // 3: every class was seen, completed, and has no placed failure.
                    var priority = 1;
                    if (holdsFailure) priority = 0;
                    else if (sibling) priority = 2;
                    else if (row.Seen && !row.Incomplete)
                    {
                        var clean = true;
                        foreach (var cls in row.Classes) if (presentClass.Contains(row.Suite + "\0" + cls)) { clean = false; break; }
                        if (clean) priority = 3;
                    }
                    var ticks = row.Last.HasValue ? row.Last.Value.UtcDateTime.Ticks : long.MinValue;
                    ranked.Add(new Ranked { Entry = row.Entry, Priority = priority, Ticks = ticks, Ordinal = PsValue.Prop(row.Entry, "canonicalOrdinal"), Interrupted = row.Interrupted, Position = ranked.Count });
                }
                // Priority 1 resumes each lane like Resolve-NightlyPreviousRunOrder. A lane starts at its first
                // interrupted chunk (started, not completed), else after its last passed chunk, and wraps to
                // earlier holes. Every lane's interrupted chunks run before any lane's untouched chunks.
                var resumed = new List<object>(); var untouched = new List<object>();
                var lanes = new List<object>();
                var laneSeen = new HashSet<string>(StringComparer.Ordinal);
                // A lane's rows in ranked order, grouped once. InvariantCulture is the comparison -ceq makes.
                var laneRows = new Dictionary<string, List<Ranked>>(StringComparer.InvariantCulture);
                foreach (var item in ranked)
                {
                    var lane = PsValue.Str(PsValue.Prop(item.Entry, "suite"));
                    if (!laneRows.TryGetValue(lane, out var list)) laneRows[lane] = list = new List<Ranked>();
                    list.Add(item);
                }
                foreach (var item in ranked)
                {
                    var lane = PsValue.Str(PsValue.Prop(item.Entry, "suite"));
                    if (!laneSeen.Add(lane)) continue;
                    var rows = laneRows[lane].ToArray();
                    var start = -1; var lastPassed = -1;
                    for (var i = 0; i < rows.Length; i++)
                    {
                        if (start < 0 && rows[i].Interrupted) start = i;
                        if (rows[i].Priority == 3) lastPassed = i;
                    }
                    if (start < 0) start = (lastPassed + 1) % rows.Length;
                    var laneObject = new PSObject();
                    laneObject.Properties.Add(new PSNoteProperty("suite", lane));
                    laneObject.Properties.Add(new PSNoteProperty("resumeChunk", PsValue.Str(PsValue.Prop(rows[start].Entry, "chunk"))));
                    laneObject.Properties.Add(new PSNoteProperty("lastPassedChunk", lastPassed >= 0 ? PsValue.Str(PsValue.Prop(rows[lastPassed].Entry, "chunk")) : null));
                    lanes.Add(laneObject);
                    for (var offset = 0; offset < rows.Length; offset++)
                    {
                        var row = rows[(start + offset) % rows.Length];
                        if (row.Priority != 1) continue;
                        if (row.Interrupted) resumed.Add(row.Entry); else untouched.Add(row.Entry);
                    }
                }
                var ordered = new List<object>();
                ordered.AddRange(Band(ranked, 0));
                ordered.AddRange(resumed);
                ordered.AddRange(untouched);
                ordered.AddRange(Band(ranked, 2));
                ordered.AddRange(Band(ranked, 3));
                if (ordered.Count != ranked.Count) throw new NightlyRefusal("seed-order-not-permutation");
                return Order(ordered.ToArray(), SeedOrderReason, lanes.ToArray());
            }
            catch (Exception e) { return Order(PsValue.AsArray(entries), Message(e), new object[0]); }
        }

        /// <summary>The canonical rows [{"suite":..,"chunk":..},..] as compact JSON (PlanValue).</summary>
        internal static string PlanJson(List<string> suites, List<object> chunks)
        {
            var sb = new StringBuilder(64 + chunks.Count * 160);
            sb.Append('[');
            for (var i = 0; i < chunks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"suite\":");
                TestPhaseRecords.AppendJsonString(sb, suites[i]);
                sb.Append(",\"chunk\":");
                PlanValue(sb, chunks[i], 2);
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Past this depth a container is written as its text, as ConvertTo-Json does past -Depth (a cycle ends too).
        private const int PlanMaxDepth = 30;

        /// <summary>
        /// One value as compact JSON. A PSCustomObject is its properties in order; another wrapped value is its base
        /// value; dictionaries are objects (keys as invariant text); other lists are arrays; numbers are invariant
        /// (doubles in round-trip form with ".0" on a whole one, non-finite ones as strings); dates and offsets are
        /// ISO 8601 round-trip strings; chars, Guids and Uris are strings; enums are their numbers. Any other object
        /// is its readable properties in order.
        /// </summary>
        private static void PlanValue(StringBuilder sb, object value, int depth)
        {
            if (value == null || ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value) || value is DBNull) { sb.Append("null"); return; }
            var ps = value as PSObject;
            if (ps != null)
            {
                if (ps.BaseObject is PSCustomObject) { PlanProperties(sb, ps, depth); return; }
                value = ps.BaseObject;
            }
            switch (value)
            {
                case string text: TestPhaseRecords.AppendJsonString(sb, text); return;
                case bool flag: sb.Append(flag ? "true" : "false"); return;
                case char c: TestPhaseRecords.AppendJsonString(sb, c.ToString()); return;
                case double n: AppendDouble(sb, n); return;
                case float n: AppendDouble(sb, double.Parse(n.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)); return;
                case DateTime d: TestPhaseRecords.AppendJsonString(sb, d.ToString("o", CultureInfo.InvariantCulture)); return;
                case DateTimeOffset d: TestPhaseRecords.AppendJsonString(sb, d.ToString("o", CultureInfo.InvariantCulture)); return;
                case Guid g: TestPhaseRecords.AppendJsonString(sb, g.ToString("D")); return;
                case Uri u: TestPhaseRecords.AppendJsonString(sb, u.OriginalString); return;
                case Enum e: sb.Append(Convert.ToString(Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)); return;
                case IFormattable number when value.GetType().IsPrimitive || value is decimal || value is System.Numerics.BigInteger:
                    sb.Append(number.ToString(null, CultureInfo.InvariantCulture)); return;
            }
            if (depth > PlanMaxDepth) { TestPhaseRecords.AppendJsonString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""); return; }
            if (value is IDictionary dictionary)
            {
                sb.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    TestPhaseRecords.AppendJsonString(sb, entry.Key as string ?? Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "");
                    sb.Append(':');
                    PlanValue(sb, entry.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            if (value is IEnumerable list)
            {
                sb.Append('[');
                var first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    PlanValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            PlanProperties(sb, ps ?? PSObject.AsPSObject(value), depth);
        }

        private static void PlanProperties(StringBuilder sb, PSObject ps, int depth)
        {
            if (depth > PlanMaxDepth) { TestPhaseRecords.AppendJsonString(sb, ps.ToString()); return; }
            sb.Append('{');
            var first = true;
            foreach (var property in ps.Properties)
            {
                if (!property.IsGettable) continue;
                object propertyValue;
                try { propertyValue = property.Value; }
                catch (GetValueException) { continue; }
                if (!first) sb.Append(',');
                first = false;
                TestPhaseRecords.AppendJsonString(sb, property.Name);
                sb.Append(':');
                PlanValue(sb, propertyValue, depth + 1);
            }
            sb.Append('}');
        }

        // A double in round-trip form, ".0" on a whole number, non-finite values as strings.
        private static void AppendDouble(StringBuilder sb, double n)
        {
            var text = n.ToString("R", CultureInfo.InvariantCulture);
            if (double.IsNaN(n) || double.IsInfinity(n)) { sb.Append('"').Append(text).Append('"'); return; }
            if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0) text += ".0";
            sb.Append(text);
        }

        private static IEnumerable<object> Band(List<Ranked> ranked, int priority)
        {
            // Sort-Object ticks,ordinal; ties keep their order.
            var band = new List<Ranked>();
            foreach (var item in ranked) if (item.Priority == priority) band.Add(item);
            band.Sort((a, b) =>
            {
                var c = a.Ticks.CompareTo(b.Ticks);
                if (c == 0) c = PsOps.CompareOrdinal(a.Ordinal, b.Ordinal);
                return c != 0 ? c : a.Position.CompareTo(b.Position);
            });
            foreach (var item in band) yield return item.Entry;
        }

        /// <summary>@($Entries.suite | Select-Object -Unique): first occurrences, compared as Select-Object compares.</summary>
        private static List<string> UniqueLanes(object[] entries)
        {
            var comparer = StringComparer.Create(CultureInfo.CurrentCulture, false);
            var lanes = new List<string>();
            foreach (var entry in entries)
            {
                var lane = PsValue.Prop(entry, "suite");
                if (lane == null) continue;
                var text = PsValue.Str(lane);
                var known = false;
                foreach (var l in lanes) if (comparer.Equals(l, text)) { known = true; break; }
                if (!known) lanes.Add(text);
            }
            return lanes;
        }

        /// <summary>Distinct [string]s, sorted ordinally.</summary>
        internal static string[] OrdinalStrings(List<object> values)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values) set.Add(PsValue.Str(value));
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private static string RowKey(object entry) =>
            PsValue.Str(PsValue.Prop(entry, "suite")) + "\0" + PsValue.Str(PsValue.Prop(entry, "chunk"));

        private static void SetProp(object row, string name, object value) => PSObject.AsPSObject(row).Properties[name].Value = value;

        private static PSObject Order(object[] entries, string reason, object[] lanes)
        {
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("entries", entries));
            o.Properties.Add(new PSNoteProperty("reason", reason));
            o.Properties.Add(new PSNoteProperty("lanes", lanes));
            return o;
        }

        private static string Message(Exception e) => (e is MethodInvocationException && e.InnerException != null ? e.InnerException : e).Message;

        // A priority member: its suite, and its chunks for @($member.chunks) -ccontains $chunkId. String elements
        // go in a set with the comparer -ceq uses for two strings (the invariant culture, case-sensitive);
        // any other element is compared as -ccontains compares it.
        private sealed class PriorityMember
        {
            private readonly HashSet<string> _strings = new HashSet<string>(StringComparer.InvariantCulture);
            private readonly List<object> _others = new List<object>();

            public PriorityMember(object suite, object[] chunks)
            {
                Suite = suite;
                foreach (var chunk in chunks)
                {
                    if (PsOps.Base(chunk) is string text) _strings.Add(text);
                    else if (chunk != null) _others.Add(chunk);
                }
            }

            public object Suite { get; }

            public bool Holds(string chunkId)
            {
                if (_strings.Contains(chunkId)) return true;
                foreach (var other in _others) if (LanguagePrimitives.Equals(other, chunkId, false, CultureInfo.InvariantCulture)) return true;
                return false;
            }
        }

        private sealed class Fold
        {
            public bool Incomplete;
            public bool Interrupted;
            public DateTimeOffset? Last;
        }

        private sealed class SeedRow
        {
            public object Entry;
            public string Suite;
            public string[] Classes;
            public bool HasNodes;
            public List<string> Methods;
            public bool Seen;
            public bool Incomplete;
            public bool Interrupted;
            public DateTimeOffset? Last;
        }

        private sealed class Ranked
        {
            public object Entry;
            public int Priority;
            public long Ticks;
            public object Ordinal;
            public bool Interrupted;
            public int Position;
        }
    }

    /// <summary>The stopped-run seed's content check and time stamps.</summary>
    public static class StoppedSeed
    {
        private static readonly Regex SuitePattern = new Regex("^[a-z][a-z0-9-]*$");
        private static readonly Regex ChunkPattern = new Regex("^[A-Za-z0-9_-]+$");
        private static readonly Regex ClassPattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_+`]*(\.[A-Za-z_][A-Za-z0-9_+`]*)+$");
        private static readonly object[] FailedOutcomes = { "Failed", "Error", "Timeout", "Aborted" };

        /// <summary>
        /// The seed's content check: suite/chunk rows with class lists, completed and started flags and parseable stamps;
        /// failed tests that name a row holding their class. Throws the refusal. A list where a scalar belongs is refused.
        /// </summary>
        public static void AssertContent(object seed)
        {
            var chunks = PsValue.Prop(seed, "chunks");
            var failed = PsValue.Prop(seed, "failedTests");
            if (!PsOps.StrEqValue(PsValue.Prop(seed, "seedKind"), "operator-seed-from-stopped-run") || !PsOps.IsArray(chunks) || !PsOps.IsArray(failed)
                || ((Array)PsOps.Base(chunks)).Length == 0) throw new NightlyRefusal("seed-schema");
            // suite/chunk -> row. Neither part can hold '/', so the key names exactly one row.
            var keys = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var row in (IEnumerable)PsOps.Base(chunks))
            {
                var suite = PsValue.Prop(row, "suite");
                var chunk = PsValue.Prop(row, "chunk");
                if (!SuitePattern.IsMatch(PsValue.Str(suite)) || !ChunkPattern.IsMatch(PsValue.Str(chunk)) || !PsOps.IsArray(PsValue.Prop(row, "classes"))
                    || !(PsOps.Base(PsValue.Prop(row, "completed")) is bool) || !(PsOps.Base(PsValue.Prop(row, "started")) is bool))
                    throw new NightlyRefusal("seed-schema: chunk");
                var rowKey = PsValue.Str(suite) + "/" + PsValue.Str(chunk);
                if (keys.ContainsKey(rowKey)) throw new NightlyRefusal("seed-schema: chunk");
                keys.Add(rowKey, row);
                foreach (var cls in (IEnumerable)PsOps.Base(PsValue.Prop(row, "classes"))) AssertClass(cls);
                Utc(PsValue.Prop(row, "lastRunAtUtc"));
            }
            foreach (var test in (IEnumerable)PsOps.Base(failed))
            {
                var className = PsValue.Prop(test, "className");
                AssertClass(className);
                var testKey = PsValue.Str(PsValue.Prop(test, "suite")) + "/" + PsValue.Str(PsValue.Prop(test, "chunk"));
                if (!keys.TryGetValue(testKey, out var row) || string.IsNullOrWhiteSpace(PsValue.Str(PsValue.Prop(test, "method")))
                    || !PsOps.In(PsValue.Prop(test, "outcome"), FailedOutcomes, false)) throw new NightlyRefusal("seed-schema: failed test");
                if (!PsOps.Contains(PsValue.AsArray(PsValue.Prop(row, "classes")), className, false)) throw new NightlyRefusal("seed-schema: failed membership");
            }
            if (Utc(PsValue.Prop(seed, "sourceObservedAtUtc")) == null) throw new NightlyRefusal("seed-timestamp-missing");
        }

        /// <summary>A dotted class identity ([string]$value): refuses history-class-identity.</summary>
        private static void AssertClass(object value)
        {
            if (!ClassPattern.IsMatch(PsValue.Str(value))) throw new NightlyRefusal("history-class-identity");
        }

        /// <summary>A seed time stamp as a UTC DateTimeOffset: null for nothing, a refusal for one that does not parse.</summary>
        public static DateTimeOffset? Utc(object value)
        {
            if (value == null || string.IsNullOrWhiteSpace(PsValue.Str(value))) return null;
            if (PsOps.Base(value) is DateTime date) return new DateTimeOffset(date).ToUniversalTime();
            if (!DateTimeOffset.TryParse(PsValue.Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var stamp))
                throw new NightlyRefusal("seed-timestamp-invalid");
            return stamp.ToUniversalTime();
        }

        /// <summary>Get-NightlySeedMethodName: the TRX method without a parameterised case suffix.</summary>
        public static string MethodName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var open = name.IndexOf('(');
            return open > 0 ? name.Substring(0, open) : name;
        }
    }

    /// <summary>A refusal: its message is the reason (seed-schema, evidence-link, ...), as the scripts report it.</summary>
    public sealed class NightlyRefusal : Exception
    {
        public NightlyRefusal(string message) : base(message) { }
    }

    /// <summary>PowerShell's comparison operators as conditions.</summary>
    internal static class PsOps
    {
        internal static object Base(object o) => o is PSObject ps ? ps.BaseObject : o;

        private static bool IsList(object o)
        {
            var b = Base(o);
            return b is IEnumerable && !(b is string) && !(b is IDictionary);
        }

        /// <summary><c>left -eq right</c> (ignoreCase) or <c>-ceq</c> as a condition; a list on the left filters.</summary>
        internal static bool Eq(object left, object right, bool ignoreCase)
        {
            if (!IsList(left)) return LanguagePrimitives.Equals(left, right, ignoreCase, CultureInfo.InvariantCulture);
            var hits = new List<object>();
            foreach (var item in (IEnumerable)Base(left)) if (LanguagePrimitives.Equals(item, right, ignoreCase, CultureInfo.InvariantCulture)) hits.Add(item);
            return LanguagePrimitives.IsTrue(hits.ToArray());
        }

        /// <summary><c>left -ne right</c> (ignoreCase) or <c>-cne</c> as a condition; a list on the left filters.</summary>
        internal static bool Ne(object left, object right, bool ignoreCase)
        {
            if (!IsList(left)) return !LanguagePrimitives.Equals(left, right, ignoreCase, CultureInfo.InvariantCulture);
            var hits = new List<object>();
            foreach (var item in (IEnumerable)Base(left)) if (!LanguagePrimitives.Equals(item, right, ignoreCase, CultureInfo.InvariantCulture)) hits.Add(item);
            return LanguagePrimitives.IsTrue(hits.ToArray());
        }

        /// <summary><c>item -in set</c> / <c>-cin</c>: each element compared with the item, element first.</summary>
        internal static bool In(object item, object[] set, bool ignoreCase)
        {
            foreach (var element in set) if (LanguagePrimitives.Equals(element, item, ignoreCase, CultureInfo.InvariantCulture)) return true;
            return false;
        }

        /// <summary><c>collection -contains item</c> / <c>-ccontains</c>.</summary>
        internal static bool Contains(object collection, object item, bool ignoreCase)
        {
            if (!IsList(collection)) return LanguagePrimitives.Equals(collection, item, ignoreCase, CultureInfo.InvariantCulture);
            foreach (var element in (IEnumerable)Base(collection)) if (LanguagePrimitives.Equals(element, item, ignoreCase, CultureInfo.InvariantCulture)) return true;
            return false;
        }

        /// <summary>Two strings under -ceq: the invariant culture, case-sensitive.</summary>
        internal static bool StrEq(string a, string b) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(a ?? "", b ?? "", CompareOptions.None) == 0 && (a == null) == (b == null);

        /// <summary><c>$value -ceq 'text'</c> for a value that must be a scalar: a list is never equal.</summary>
        internal static bool StrEqValue(object value, string text) =>
            !IsList(value) && LanguagePrimitives.Equals(value, text, false, CultureInfo.InvariantCulture);

        /// <summary><c>@($o.PSObject.Properties.Name) -ccontains $name</c>.</summary>
        internal static bool HasName(object o, string name)
        {
            if (o == null) return false;
            var property = PSObject.AsPSObject(o).Properties[name];
            return property != null && string.Equals(property.Name, name, StringComparison.Ordinal);
        }

        /// <summary><c>$o -is [array]</c>.</summary>
        internal static bool IsArray(object o) => Base(o) is Array;

        /// <summary><c>[int]$o</c>.</summary>
        internal static int ToInt(object o) => (int)LanguagePrimitives.ConvertTo(o, typeof(int), CultureInfo.InvariantCulture);

        /// <summary>Sort-Object's comparison of two ordinals (numbers compare as numbers; $null sorts first).</summary>
        internal static int CompareOrdinal(object a, object b)
        {
            a = Base(a); b = Base(b);
            if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
            if (a is int x && b is int y) return x.CompareTo(y);
            return LanguagePrimitives.Compare(a, b, false, CultureInfo.CurrentCulture);
        }
    }
}
