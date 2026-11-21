// The prior-failure resolver's mapping (PriorFailureHistory.Resolve): each prior-failure member (a suite and
// class from validated history or a stopped-run seed) is mapped onto the current plan. The class's current nodes,
// less the profile's excluded ones, are its required tests; every chunk that runs one of them is the member's chunk
// list, and the required tests must all be covered. A member that cannot be mapped is unresolved, with the reason
// (current-suite-missing, current-class-missing, entirely-excluded-class or current-class-unplanned) and its old chunks.
// PriorFailureHistory.Resolve keeps the mode checks, the sweep's policy rows, the warnings and the result object.
// The records are PsValue records (PSObjects under PowerShell, PlainObjects for a plain caller).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Antiphon.Nightly
{
    public static class PriorFailures
    {
        private sealed class SuiteEntry
        {
            public Dictionary<string, List<object>> Nodes;
            public Dictionary<string, List<object>> Chunks;
            public List<object> AllChunks;
            public ProfileExclusionIndex Exclusions;
        }

        /// <summary>
        /// { members, unresolved, excludedUids }. exclusionsBySuite (the sweep's profile rows by suite id) replaces each
        /// suite's own exclusions when given, and only then are excluded node rows reported.
        /// </summary>
        public static object Map(object history, object[] suites, IDictionary exclusionsBySuite, string exclusionProfile)
        {
            var sweep = exclusionsBySuite != null;
            var unresolved = new List<object>();
            foreach (var u in PsValue.AsArray(PsValue.Prop(history, "unresolved"))) unresolved.Add(u);
            var excludedUids = new List<object>();
            var matches = new List<object>();
            var bySuite = new Dictionary<string, SuiteEntry>(StringComparer.Ordinal);
            foreach (var suite in suites ?? new object[0])
            {
                var id = PsValue.Str(PsValue.Prop(suite, "id"));
                if (bySuite.ContainsKey(id)) throw new InvalidOperationException("history-identity-conflict: current suite");
                var entry = new SuiteEntry
                {
                    Nodes = NightlyClassNames.IndexByBare(PsValue.Prop(suite, "nodes")),
                    Chunks = new Dictionary<string, List<object>>(StringComparer.Ordinal),
                    AllChunks = new List<object>(),
                    Exclusions = new ProfileExclusionIndex(sweep ? exclusionsBySuite[id] : PsValue.Prop(suite, "exclusions")),
                };
                foreach (var chunk in PsValue.AsArray(PsValue.Prop(suite, "chunks")))
                {
                    var classes = PsValue.Prop(chunk, "classes");
                    if (classes == null || PsValue.AsArray(classes).Length == 0) { entry.AllChunks.Add(chunk); continue; }
                    foreach (var cls in PsValue.AsArray(classes))
                    {
                        var key = PsValue.Str(cls);
                        if (!entry.Chunks.TryGetValue(key, out var list)) entry.Chunks[key] = list = new List<object>();
                        list.Add(chunk);
                    }
                }
                bySuite.Add(id, entry);
            }
            foreach (var member in PsValue.AsArray(PsValue.Prop(history, "members")))
            {
                var memberSuite = PsValue.Str(PsValue.Prop(member, "suite"));
                var memberClass = PsValue.Str(PsValue.Prop(member, "className"));
                var reason = "";
                var required = new List<object>();
                var chunkIds = new List<object>();
                if (!bySuite.TryGetValue(memberSuite, out var entry)) reason = "current-suite-missing";
                else
                {
                    var nodes = entry.Nodes.TryGetValue(memberClass, out var found) ? found : new List<object>();
                    foreach (var node in nodes) if (!entry.Exclusions.Excluded(node)) required.Add(node);
                    if (sweep)
                    {
                        foreach (var node in nodes)
                        {
                            var disposition = entry.Exclusions.Disposition(node);
                            if (PsValue.Str(PsValue.Prop(disposition, "Disposition")) != "excluded") continue;
                            excludedUids.Add(PsValue.Record(("suite", memberSuite), ("uid", PsValue.Str(PsValue.Prop(node, "Uid"))), ("className", memberClass),
                                ("method", PsValue.Str(PsValue.Prop(node, "Method"))), ("reason", PsValue.Prop(disposition, "Reason")), ("owner", PsValue.Prop(disposition, "Owner"))));
                        }
                    }
                    if (nodes.Count == 0) reason = "current-class-missing";
                    else if (required.Count == 0) reason = "entirely-excluded-class";
                    else
                    {
                        var needed = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var node in required) needed.Add(PsValue.Str(PsValue.Prop(node, "Uid")));
                        var covered = new HashSet<string>(StringComparer.Ordinal);
                        var candidates = new List<object>(entry.AllChunks);
                        if (entry.Chunks.TryGetValue(memberClass, out var classChunks)) candidates.AddRange(classChunks);
                        foreach (var chunk in candidates)
                        {
                            var assigned = required;
                            var chunkNodes = PsValue.Prop(chunk, "nodes");
                            var chunkClasses = PsValue.AsArray(PsValue.Prop(chunk, "classes"));
                            if (chunkNodes != null && PsValue.AsArray(chunkNodes).Length > 0)
                            {
                                var ids = new HashSet<string>(StringComparer.Ordinal);
                                foreach (var node in PsValue.AsArray(chunkNodes)) ids.Add(PsValue.Str(PsValue.Prop(node, "Uid")));
                                assigned = required.FindAll(node => ids.Contains(PsValue.Str(PsValue.Prop(node, "Uid"))));
                            }
                            else if (chunkClasses.Length > 0 && !PsOps.Contains(chunkClasses, memberClass, false)) assigned = new List<object>();
                            if (assigned.Count == 0) continue;
                            chunkIds.Add(PsValue.Str(PsValue.Prop(chunk, "id")));
                            foreach (var node in assigned) covered.Add(PsValue.Str(PsValue.Prop(node, "Uid")));
                        }
                        if (needed.Count == 0 || !needed.SetEquals(covered)) reason = "current-class-unplanned";
                    }
                }
                if (reason.Length > 0)
                {
                    var oldChunks = new List<string>();
                    foreach (var observation in PsValue.AsArray(PsValue.Prop(member, "observations")))
                    {
                        if (observation == null) continue;
                        var chunk = PsValue.Str(PsValue.Prop(observation, "chunk"));
                        if (!oldChunks.Contains(chunk)) oldChunks.Add(chunk);
                    }
                    unresolved.Add(new OrderedDictionary
                    {
                        { "suite", memberSuite }, { "className", memberClass }, { "chunk", string.Join(",", oldChunks) },
                        { "reason", reason }, { "exclusionProfile", exclusionProfile ?? "" }, { "evidence", new object[0] },
                    });
                    continue;
                }
                var uids = new List<object>();
                foreach (var node in required) uids.Add(PsValue.Prop(node, "Uid"));
                matches.Add(PsValue.Record(("suite", memberSuite), ("className", memberClass), ("chunks", chunkIds.ToArray()),
                    ("requiredUids", DispatchSchedule.OrdinalStrings(uids))));
            }
            excludedUids.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(PsValue.Str(PsValue.Prop(a, "suite")), PsValue.Str(PsValue.Prop(b, "suite")));
                return c != 0 ? c : string.CompareOrdinal(PsValue.Str(PsValue.Prop(a, "uid")), PsValue.Str(PsValue.Prop(b, "uid")));
            });
            // The excluded rows as PSObject[] under PowerShell, the type its callers always saw.
            var excluded = PsValue.RecordArray(excludedUids.Count);
            excludedUids.CopyTo(excluded);
            return PsValue.Record(("members", matches.ToArray()), ("unresolved", unresolved.ToArray()), ("excludedUids", excluded));
        }
    }
}
