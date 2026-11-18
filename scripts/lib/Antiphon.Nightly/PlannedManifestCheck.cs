// The planned-manifest partition check (test-phase-evidence.ps1 Write-TestPhasePlannedManifest) and the
// D-5 census digest (nightly-coverage.ps1 Get-NightlyDiscoveryCensus), compiled. Each returns what the
// PowerShell code computed; the PowerShell copies were removed (the core is required).
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Management.Automation;

namespace Antiphon.Nightly
{
    public static class PlannedManifestCheck
    {
        /// <summary><c>foreach ($v in $values) { $target.Add([string]$v) }</c> for an already-wrapped array.</summary>
        public static void AddStrings(List<string> target, object[] values)
        {
            if (values == null) return;
            if (target.Capacity < target.Count + values.Length) target.Capacity = target.Count + values.Length;
            foreach (var v in values) target.Add(PsValue.Str(v));
        }

        /// <summary>
        /// The duplicate / missing / extra check over the planned UIDs (every group's plannedTestIds, in group
        /// order) against the view's RequiredUids. The union is a PowerShell hashtable (OrdinalIgnoreCase keys,
        /// first spelling kept); missing follows RequiredUids order (duplicates included); extra is tested
        /// against an InvariantCultureIgnoreCase set. Extra is listed in first-planned order: the PowerShell
        /// copy enumerated hashtable keys, whose order depends on the per-process string hash seed.
        /// </summary>
        public static PSObject Check(List<string> plannedIds, object[] requiredUids)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unionOrder = new List<string>();
            var duplicate = false;
            if (plannedIds != null)
            {
                foreach (var raw in plannedIds)
                {
                    var id = raw ?? "";
                    if (union.Add(id)) unionOrder.Add(id); else duplicate = true;
                }
            }
            var missing = new List<object>();
            var requiredSet = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var req in requiredUids ?? Array.Empty<object>())
            {
                var s = PsValue.Str(req);
                if (!union.Contains(s)) missing.Add(s);
                requiredSet.Add(s);
            }
            var extra = new List<object>();
            foreach (var id in unionOrder)
                if (!requiredSet.Contains(id)) extra.Add(id);
            var o = new PSObject();
            o.Properties.Add(new PSNoteProperty("Duplicate", duplicate));
            o.Properties.Add(new PSNoteProperty("Missing", missing.ToArray()));
            o.Properties.Add(new PSNoteProperty("Extra", extra.ToArray()));
            return o;
        }

        /// <summary>
        /// Get-NightlyDiscoveryCensus' digest: SHA-256 of the canonical JSON of
        /// <c>[ordered]@{ required = sorted uids; excluded = sorted uids }</c>, each list in CensusOrder.
        /// </summary>
        public static string CensusDigest(object[] requiredRows, object[] excludedRows)
        {
            var doc = new OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                { "required", SortedUids(requiredRows) },
                { "excluded", SortedUids(excludedRows) },
            };
            return TestPhaseRecords.Sha256Text(NightlyDocuments.CanonicalJson(doc));
        }

        /// <summary>The census rows' uids (always non-blank strings from CensusRows), in CensusOrder.</summary>
        public static object[] SortedUids(object[] rows)
        {
            var uids = new List<string>();
            foreach (var r in rows ?? Array.Empty<object>()) uids.Add(PsValue.Str(PsValue.Prop(r, "uid")));
            return SortCensusUids(uids.ToArray());
        }

        /// <summary>
        /// The census digest's UID order: OrdinalIgnoreCase, then Ordinal for UIDs equal ignoring case. Census
        /// UIDs are unique OrdinalIgnoreCase, so the tie-break never decides there; it keeps any input total and
        /// deterministic. (Until 2026-11-18 this was Sort-Object order: current culture, case-insensitive.)
        /// </summary>
        public static object[] SortCensusUids(string[] uids)
        {
            var keys = (string[])(uids ?? Array.Empty<string>()).Clone();
            Array.Sort(keys, CensusOrder);
            var result = new object[keys.Length];
            Array.Copy(keys, result, keys.Length);
            return result;
        }

        private static int CensusOrder(string a, string b)
        {
            var c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }
    }
}