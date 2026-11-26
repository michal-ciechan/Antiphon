// W2-nightly S13: the dispatch (plan 2026-11-21-w2-nightly-port-slices.md, slice S13), moved from nightly-dispatch.ps1:
// New-NightlyDispatchSchedule (Schedule, over the compiled DispatchSchedule.Build), Write-NightlyDispatchSchedule
// (WriteSchedule), the coordinator (Coordinate, once Invoke-NightlyDispatchCoordinator) and the prepared schedule (RunPrepared,
// once Invoke-NightlyPreparedSchedule). Get-NightlyPreviousRunKind and Resolve-NightlyPreviousRunOrder are
// DispatchSchedule.PreviousRunKind and DispatchSchedule.PreviousRunOrder, called directly.
//
// Diagnostic scheduling only: canonical chunks remain the planner's own objects. With no nonempty history (and no previous
// run) the coordinator keeps the lazy per-suite sequence: prepare a suite, run its chunks, complete it, then the next. With
// one it prepares every suite first (the barrier), then runs one global schedule. Suites are prepared, run and completed
// through NightlyDispatchSteps (NightlySuiteRunner's in a run); the schedule and its file are this class's. A failure is
// thrown after the run state it reached. Plain C#: no PowerShell types; records are read through PsValue. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Specialized;
using System.IO;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>
    /// How the dispatch prepares, runs and completes suites: NightlySuiteRunner.Steps() in a run; a test or a fixture may
    /// give its own (a prepared suite is any record with an id and chunks).
    /// </summary>
    public sealed class NightlyDispatchSteps
    {
        /// <summary>(suite id, the dispatch needs discovery) -> the prepared suite, or null when the suite does not run.</summary>
        public Func<string, bool, object> Prepare { get; set; }

        /// <summary>(prepared suite, chunk): runs one chunk; a failure is thrown.</summary>
        public Action<object, object> RunChunk { get; set; }

        /// <summary>(prepared suite): after its last chunk.</summary>
        public Action<object> Complete { get; set; }
    }

    public static class NightlyDispatch
    {
        /// <summary>
        /// New-NightlyDispatchSchedule: the canonical rows of the prepared suites, the prior failures first (or the previous
        /// run's order), as DispatchSchedule.Build makes them; its refusal is thrown.
        /// </summary>
        public static object Schedule(object[] preparedSuites, object[] priorityMembers, string historyDigest, object[] unresolved, bool preparationComplete,
            string runId, string attempt, object previousRun, string candidateSha, DateTime nowUtc, string historyFallbackReason)
        {
            var build = DispatchSchedule.Build(preparedSuites ?? new object[0], priorityMembers ?? new object[0], historyDigest ?? "", unresolved ?? new object[0],
                preparationComplete, runId ?? "", attempt ?? "", previousRun, candidateSha ?? "", nowUtc, historyFallbackReason ?? "");
            if (!string.IsNullOrEmpty(build.Error)) throw new NightlyRefusal(build.Error);
            return build.Schedule;
        }

        /// <summary>New-NightlyDispatchSchedule -PreparedSuites alone: the canonical order, complete, no history.</summary>
        public static object Schedule(object[] preparedSuites) =>
            Schedule(preparedSuites, null, "", null, true, "", "", null, "", DateTime.UtcNow, "");

        /// <summary>
        /// Write-NightlyDispatchSchedule: dispatch-schedule.json in the directory, the schedule's header and each entry's
        /// suite, chunk, canonicalOrdinal, dispatchOrdinal and priorFailure (ConvertTo-Json's indented layout, no byte-order
        /// mark, written beside and moved into place). A failure is thrown.
        /// </summary>
        public static void WriteSchedule(object schedule, string directory)
        {
            var document = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (var field in new[] { "format", "runId", "attempt", "historyDigest", "canonicalPlanDigest", "preparationComplete", "unresolved" })
                document[field] = PsValue.Prop(schedule, field);
            foreach (var field in new[] { "historyFallbackReason", "orderingReason", "lanes", "invariantPrefix" })
                if (PsOps.HasName(schedule, field)) document[field] = PsValue.Prop(schedule, field);
            var entries = new List<object>();
            foreach (var entry in Each(PsValue.Prop(schedule, "entries")))
            {
                var row = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
                foreach (var field in new[] { "suite", "chunk", "canonicalOrdinal", "dispatchOrdinal", "priorFailure" }) row[field] = PsValue.Prop(entry, field);
                if (NightlyTestsPlan.IsInvariantChunk(PsValue.Prop(entry, "ChunkRef"))) row["phase"] = "repository-invariant";
                entries.Add(row);
            }
            document["entries"] = entries.ToArray();
            WriteAtomic(Path.Combine(directory ?? "", "dispatch-schedule.json"), JsonText(document));
        }

        /// <summary>
        /// Invoke-NightlyDispatchCoordinator over the selected suites. Priority members, unresolved rows, a nonempty
        /// history or a previous run make it a barrier (prepare all, then one schedule); otherwise each suite is prepared,
        /// run and completed in turn. state.DispatchSchedule is each schedule it makes; a schedule write failure is a
        /// warning and the dispatch-schedule-write-failed marker. Preparation stops once the prerequisites failed.
        /// </summary>
        public static void Coordinate(NightlyRunState state, NightlyDispatchSteps steps, object[] suites, object[] priorityMembers, object[] unresolved,
            string historyDigest, object history, object previousRun, string fallbackReason)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            priorityMembers = priorityMembers ?? new object[0];
            unresolved = unresolved ?? new object[0];
            var prepared = new List<object>();
            var barrier = priorityMembers.Length > 0 || unresolved.Length > 0;
            // @($history.members).Count: a missing list counts one, as @($null) does.
            if (PsValue.IsTrue(history) && (PsValue.AsArray(PsValue.Prop(history, "members")).Length > 0 || PsValue.AsArray(PsValue.Prop(history, "unresolved")).Length > 0)) barrier = true;
            if (PsValue.IsTrue(previousRun)) barrier = true;
            var needsDiscovery = barrier || state.RequiresInvariantPrefix;
            var selectedSuites = suites ?? Array.Empty<object>();
            var finishedPrefix = new HashSet<(string Suite, string Chunk)>();
            foreach (var suiteValue in selectedSuites)
            {
                if (!state.NpmOk || state.LintBlocksPipeline || !state.DotnetOk) break;
                var suiteId = PsValue.Str(suiteValue);
                object suite;
                try { suite = steps.Prepare(suiteId, needsDiscovery); }
                catch (Exception)
                {
                    if (barrier) PartialSchedule(state, prepared, priorityMembers, unresolved, historyDigest);
                    throw;
                }
                if (suite == null)
                {
                    if (barrier)
                    {
                        PartialSchedule(state, prepared, priorityMembers, unresolved, historyDigest);
                        throw new NightlyRefusal("dispatch-preparation-incomplete:" + suiteId);
                    }
                    continue;
                }
                prepared.Add(suite);
                if (barrier) continue;
                if (state.RequiresInvariantPrefix)
                {
                    var prefix = Each(PsValue.Prop(suite, "chunks")).Where(NightlyTestsPlan.IsInvariantChunk).ToArray();
                    if (prefix.Length == 0) continue;
                    state.DispatchSchedule = Schedule(prepared.ToArray(), null, "", null, false, state.InvocationId, state.Attempt,
                        null, state.Sha, state.UtcNow, fallbackReason);
                    WriteBestEffort(state, state.DispatchSchedule);
                    foreach (var chunk in prefix)
                        RunInvariantChunk(state, steps, suite, chunk, finishedPrefix, selectedSuites);
                    continue;
                }
                try
                {
                    state.DispatchSchedule = Schedule(prepared.ToArray(), null, "", null, false, state.InvocationId, state.Attempt, null, "", DateTime.UtcNow, fallbackReason);
                    WriteBestEffort(state, state.DispatchSchedule);
                }
                catch (Exception ex) { WriteFailed(state, ex); }
                foreach (var chunk in PsValue.AsArray(PsValue.Prop(suite, "chunks"))) steps.RunChunk(suite, chunk);
                steps.Complete(suite);
            }
            if (!barrier && !state.RequiresInvariantPrefix)
            {
                try
                {
                    state.DispatchSchedule = Schedule(prepared.ToArray(), null, "", null, true, state.InvocationId, state.Attempt, null, "", DateTime.UtcNow, fallbackReason);
                    WriteBestEffort(state, state.DispatchSchedule);
                }
                catch (Exception ex) { WriteFailed(state, ex); }
                return;
            }
            if (PsValue.IsTrue(history))
            {
                var resolved = PriorFailureHistory.Resolve(history, prepared.ToArray(), "Full", null, null);
                foreach (var warning in PsValue.AsArray(PsValue.Prop(resolved, "warnings"))) state.Warn(PsValue.Str(warning));
                priorityMembers = Each(PsValue.Prop(resolved, "members"));
                unresolved = Each(PsValue.Prop(resolved, "unresolved"));
            }
            var schedule = Schedule(prepared.ToArray(), priorityMembers, historyDigest, unresolved, true, state.InvocationId, state.Attempt, previousRun, state.Sha,
                state.UtcNow, "");
            state.DispatchSchedule = schedule;
            WriteBestEffort(state, schedule);
            RunPrepared(state, steps, schedule, finishedPrefix, selectedSuites);
        }

        /// <summary>
        /// Coordinate for a PowerShell caller: the failure is returned (null when there was none) for the caller to throw as
        /// its own, so the run records the message the dot-sourced coordinator's error carried (a failure already wrapped
        /// by PowerShell keeps its one wrapper; nothing is added at this boundary).
        /// </summary>
        public static Exception CoordinateOrFailure(NightlyRunState state, NightlyDispatchSteps steps, object[] suites, object[] priorityMembers, object[] unresolved,
            string historyDigest, object history, object previousRun, string fallbackReason)
        {
            try
            {
                Coordinate(state, steps, suites, priorityMembers, unresolved, historyDigest, history, previousRun, fallbackReason);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        /// <summary>
        /// Invoke-NightlyPreparedSchedule: a complete schedule whose entries are exactly the prepared suites' chunks, each
        /// once and bound to its own suite and chunk, runs in its order; a suite completes after its last entry. The
        /// caller keeps the run verdict and custody.
        /// </summary>
        public static void RunPrepared(NightlyRunState state, NightlyDispatchSteps steps, object schedule) =>
            RunPrepared(state, steps, schedule, new HashSet<(string Suite, string Chunk)>(), Array.Empty<object>());

        private static void RunPrepared(NightlyRunState state, NightlyDispatchSteps steps, object schedule,
            HashSet<(string Suite, string Chunk)> finishedPrefix, object[] selectedSuites)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (!PsValue.IsTrue(PsValue.Prop(schedule, "preparationComplete"))) throw new NightlyRefusal("dispatch-preparation-incomplete");
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var suite in Each(PsValue.Prop(schedule, "PreparedSuites")))
                foreach (var chunk in Each(PsValue.Prop(suite, "chunks")))
                    expected.Add(PsValue.Str(PsValue.Prop(suite, "id")) + "\0" + PsValue.Str(PsValue.Prop(chunk, "id")));
            var entries = Each(PsValue.Prop(schedule, "entries"));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var suiteId = PsValue.Prop(entry, "suite");
                var chunkId = PsValue.Prop(entry, "chunk");
                if (!seen.Add(PsValue.Str(suiteId) + "\0" + PsValue.Str(chunkId))
                    || PsOps.Ne(PsValue.Prop(PsValue.Prop(entry, "ChunkRef"), "id"), chunkId, false)
                    || PsOps.Ne(PsValue.Prop(PsValue.Prop(entry, "SuiteState"), "id"), suiteId, false))
                    throw new NightlyRefusal("dispatch-not-permutation");
            }
            if (!seen.SetEquals(expected)) throw new NightlyRefusal("dispatch-not-permutation");
            // @{}: suite keys compare as PowerShell's hashtable compares them.
            var remaining = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var key = PsValue.Str(PsValue.Prop(entry, "suite"));
                remaining[key] = remaining.TryGetValue(key, out var count) ? count + 1 : 1;
            }
            foreach (var entry in entries)
            {
                var suite = PsValue.Prop(entry, "SuiteState");
                var chunk = PsValue.Prop(entry, "ChunkRef");
                if (NightlyTestsPlan.IsInvariantChunk(chunk))
                {
                    if (!finishedPrefix.Contains(Identity(suite, chunk)))
                        RunInvariantChunk(state, steps, suite, chunk, finishedPrefix, selectedSuites);
                }
                else steps.RunChunk(suite, chunk);
                var key = PsValue.Str(PsValue.Prop(entry, "suite"));
                remaining[key]--;
                if (remaining[key] == 0) steps.Complete(suite);
            }
        }

        private static (string Suite, string Chunk) Identity(object suite, object chunk) =>
            (PsValue.Str(PsValue.Prop(suite, "id")), PsValue.Str(PsValue.Prop(chunk, "id")));

        private static void RunInvariantChunk(NightlyRunState state, NightlyDispatchSteps steps, object suite, object chunk,
            HashSet<(string Suite, string Chunk)> finished, object[] selectedSuites)
        {
            var id = Identity(suite, chunk);
            var before = state.SuiteResults.Count;
            try
            {
                steps.RunChunk(suite, chunk);
                if (state.SuiteResults.Count > before)
                {
                    var result = state.SuiteResults[state.SuiteResults.Count - 1];
                    if (PsValue.Str(PsValue.Prop(result, "result")) != "pass" || !PsValue.IsTrue(PsValue.Prop(result, "coverageComplete")))
                        throw new NightlyRefusal("repository-invariant-failed:" + id.Suite + "/" + id.Chunk);
                }
                finished.Add(id);
            }
            catch (Exception ex)
            {
                state.TestsPassed = false;
                state.CoverageComplete = false;
                state.OverallFailed = true;
                state.Reasons.Add("repository-invariant-prefix-incomplete:" + id.Suite + "/" + id.Chunk);
                var prepared = Each(PsValue.Prop(state.DispatchSchedule, "PreparedSuites"))
                    .Select(s => PsValue.Str(PsValue.Prop(s, "id"))).ToHashSet(StringComparer.Ordinal);
                var outstanding = Each(PsValue.Prop(state.DispatchSchedule, "entries"))
                    .Select(entry => (Suite: PsValue.Str(PsValue.Prop(entry, "suite")), Chunk: PsValue.Str(PsValue.Prop(entry, "chunk"))))
                    .Where(key => !finished.Contains(key))
                    .Select(key => PsValue.Record(("suite", key.Suite), ("chunk", key.Chunk)))
                    .Concat(selectedSuites.Select(PsValue.Str).Where(s => !prepared.Contains(s))
                        .Select(s => PsValue.Record(("suite", s), ("chunk", "discovery-pending")))).ToArray();
                if (state.DispatchSchedule != null)
                {
                    PsValue.AddMemberForce(state.DispatchSchedule, "invariantPrefix",
                        PsValue.Record(("complete", false), ("suite", id.Suite), ("chunk", id.Chunk), ("detail", ex.Message), ("outstanding", outstanding)));
                    WriteBestEffort(state, state.DispatchSchedule);
                }
                throw;
            }
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        // The barrier's incomplete schedule when preparation stopped, written best effort.
        private static void PartialSchedule(NightlyRunState state, List<object> prepared, object[] priorityMembers, object[] unresolved, string historyDigest)
        {
            state.DispatchSchedule = Schedule(prepared.ToArray(), priorityMembers, historyDigest, unresolved, false, state.InvocationId, state.Attempt, null, "",
                DateTime.UtcNow, "");
            WriteBestEffort(state, state.DispatchSchedule);
        }

        // Write-NightlyDispatchSchedule -BestEffort: a failure is a warning and the marker.
        private static void WriteBestEffort(NightlyRunState state, object schedule)
        {
            try { WriteSchedule(schedule, state.LogRoot); }
            catch (Exception ex) { WriteFailed(state, ex); }
        }

        private static void WriteFailed(NightlyRunState state, Exception ex)
        {
            state.Warn("dispatch-schedule-write-failed: " + ex.Message);
            state.FailureMarker(state.LogRoot, "dispatch-schedule-write-failed");
        }

        // foreach ($x in $value): nothing for null, the elements of a list, else the value itself.
        private static object[] Each(object value) => value == null ? new object[0] : PsValue.AsArray(value);

        /// <summary>ConvertTo-Json's indented text of the document: two-space indents, the platform's line ends, no final newline.</summary>
        internal static string JsonText(object document)
        {
            var text = NightlyJson.IndentedText(document, 12);
            if (text == null) throw new InvalidDataException("dispatch-schedule-unwritable-value");
            return text;
        }

        // Write-NightlyAtomicJson: the directory, then UTF-8 without a byte-order mark to a temporary file beside the
        // target, which replaces it (its .bak removed) or moves into place. The temporary file never stays behind.
        private static void WriteAtomic(string path, string text)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory) && !File.Exists(directory)) Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = path + ".bak";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path) || Directory.Exists(path))
                {
                    File.Replace(temporary, path, backup);
                    try { if (File.Exists(backup)) File.Delete(backup); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }
        }
    }
}
