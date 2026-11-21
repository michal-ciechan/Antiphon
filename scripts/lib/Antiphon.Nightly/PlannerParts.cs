using System;
using System.Collections.Generic;
using System.Globalization;

namespace Antiphon.Nightly
{
    /// <summary>Launch-plan helpers from nightly-tests-impl.ps1 that ran once per class or method name.</summary>
    public static class NightlyPlannerParts
    {
        /// <summary>
        /// Get-NightlyPrefixUnsafeNames: the distinct names (ordinal) that are a strict ordinal prefix of another
        /// name, so cannot be a (Name*) OR operand. A null list is <c>@($null)</c>: one empty name; a null name is "".
        /// </summary>
        public static HashSet<string> PrefixUnsafeNames(string[] names)
        {
            var unsafeNames = new HashSet<string>(StringComparer.Ordinal);
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            if (names == null) distinct.Add("");
            else foreach (var name in names) distinct.Add(name ?? "");
            var sorted = new string[distinct.Count];
            distinct.CopyTo(sorted);
            Array.Sort(sorted, StringComparer.Ordinal);
            for (var i = 0; i < sorted.Length; i++)
            {
                for (var j = i + 1; j < sorted.Length; j++)
                {
                    if (!sorted[j].StartsWith(sorted[i], StringComparison.Ordinal)) break;
                    if (sorted[j].Length > sorted[i].Length) { unsafeNames.Add(sorted[i]); break; }
                }
            }
            return unsafeNames;
        }

        private struct PackEntry
        {
            public object Item;
            public double Seconds;
            public string Key;
        }

        /// <summary>
        /// Split-NightlyItemsBySeconds' ordering: <c>Sort-Object @{ Expression = { Get-NightlyPackedItemSeconds $_ };
        /// Descending = $true }, @{ Expression = { [string]$_.Key } }</c>. Sort-Object (no -Stable) is List.Sort over
        /// the entries in input order, so this uses the same sort with the same comparison: seconds descending
        /// (double.CompareTo), then the key ascending, current culture, case-insensitive. Ties keep Sort-Object's
        /// (unstable) introsort placement.
        /// </summary>
        public static object[] PackOrder(object[] items, double defaultSeconds)
        {
            if (items == null) return new object[0];
            var entries = new List<PackEntry>(items.Length);
            foreach (var item in items)
            {
                var found = false;
                var value = item == null ? null : PsValue.Property(item, "Seconds", out found);
                var seconds = !found ? defaultSeconds : PsValue.Double(value);
                entries.Add(new PackEntry { Item = item, Seconds = seconds, Key = PsValue.Str(PsValue.Prop(item, "Key")) });
            }
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            entries.Sort(Comparer<PackEntry>.Create((a, b) =>
            {
                var order = -a.Seconds.CompareTo(b.Seconds);
                if (order != 0) return order;
                return compare.Compare(a.Key, b.Key, CompareOptions.IgnoreCase);
            }));
            var ordered = new object[entries.Count];
            for (var i = 0; i < ordered.Length; i++) ordered[i] = entries[i].Item;
            return ordered;
        }

        /// <summary>Split-NightlyItemsBySeconds: <see cref="PackOrder"/> then <see cref="NightlyLaunchPlan.FirstFitBins"/>.</summary>
        public static object[] SplitBySeconds(object[] items, double targetSeconds, int maxOperands, double defaultSeconds) =>
            NightlyLaunchPlan.FirstFitBins(PackOrder(items, defaultSeconds), targetSeconds, maxOperands, defaultSeconds);
    }
}
