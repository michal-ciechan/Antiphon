// Suite completion passes over the whole discovery (Complete-NightlySuiteExecution in nightly-tests-impl.ps1 and
// its two helpers in nightly-coverage.ps1), compiled. Each returns what the named PowerShell loop computed; the
// PowerShell copies were removed (the core is required).
using System;
using System.Collections.Generic;

namespace Antiphon.Nightly
{
    public static class SuiteCompletionParts
    {
        /// <summary>
        /// The suite union's node filter: the view's nodes whose excluded flag is not set, in discovery order. A
        /// node past the end of the flags counts as not excluded, as indexing past the end of the array did.
        /// </summary>
        public static object[] IncludedNodes(object[] nodes, bool[] excluded)
        {
            if (nodes == null) return Array.Empty<object>();
            var included = new List<object>(nodes.Length);
            for (var i = 0; i < nodes.Length; i++)
            {
                if (excluded != null && i < excluded.Length && excluded[i]) continue;
                included.Add(nodes[i]);
            }
            return included.ToArray();
        }

        /// <summary>
        /// Get-NightlyExpectedSkipStaleWarnings once the known identities are given: "stale-roster:" + test for
        /// every non-null roster entry of the platform (-ceq) whose test is not a known identity (ordinal), in
        /// roster order, duplicates kept. Blank known identities are not known.
        /// </summary>
        public static string[] StaleWarnings(string platform, object entries, string[] known)
        {
            var knownTests = new HashSet<string>(StringComparer.Ordinal);
            if (known != null)
            {
                foreach (var id in known)
                {
                    if (!string.IsNullOrWhiteSpace(id)) knownTests.Add(id);
                }
            }
            var warnings = new List<string>();
            foreach (var entry in PsValue.AsArray(entries))
            {
                if (entry == null) continue;
                var entryPlatform = PsValue.Str(PsValue.Prop(entry, "platform"));
                if (!PsValue.Eq(entryPlatform, platform, false)) continue;
                var entryTest = PsValue.Str(PsValue.Prop(entry, "test"));
                if (!knownTests.Contains(entryTest)) warnings.Add("stale-roster:" + entryTest);
            }
            return warnings.ToArray();
        }

        /// <summary>
        /// Get-NightlyDiscoveryKnownIdentities: ClassName + "." + Method of every non-null node that has both as
        /// properties (the case-insensitive member lookup; dictionary keys are not properties) with non-blank
        /// [string] values, in discovery order, duplicates kept.
        /// </summary>
        public static string[] KnownIdentities(object[] nodes)
        {
            if (nodes == null) return Array.Empty<string>();
            var ids = new List<string>(nodes.Length);
            foreach (var node in nodes)
            {
                if (node == null) continue;
                var classValue = PsValue.Property(node, "ClassName", out var hasClass);
                if (!hasClass) continue;
                var methodValue = PsValue.Property(node, "Method", out var hasMethod);
                if (!hasMethod) continue;
                var className = PsValue.Str(classValue);
                if (string.IsNullOrWhiteSpace(className)) continue;
                var method = PsValue.Str(methodValue);
                if (string.IsNullOrWhiteSpace(method)) continue;
                ids.Add(className + "." + method);
            }
            return ids.ToArray();
        }
    }
}
