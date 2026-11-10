// The orders the selection scripts produce: Get-TestSelectionSorted (ordinal, distinct), Sort-Object -CaseSensitive
// (current culture, case-sensitive) and Sort-Object -Unique (current culture ignoring case, distinct ignoring case).
// A culture sort is only answered when it is a total order on the values: two different strings the culture calls
// equal would leave their order to the sort's stability, so that input is NotHandled.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Antiphon.Scripts
{
    internal static class PsSort
    {
        /// <summary>Get-TestSelectionSorted: the distinct non-null strings in ordinal order.</summary>
        internal static List<object> Ordinal(IEnumerable<string> values)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string value in values) if (value != null) set.Add(value);
            return set.Cast<object>().ToList();
        }

        /// <summary>Sort-Object {key} -CaseSensitive.</summary>
        internal static List<T> CultureCaseSensitive<T>(IEnumerable<T> items, Func<T, string> key) => Culture(items, key, CompareOptions.None);

        /// <summary>Sort-Object {key}: the current culture ignoring case.</summary>
        internal static List<T> CultureIgnoreCase<T>(IEnumerable<T> items, Func<T, string> key) => Culture(items, key, CompareOptions.IgnoreCase);

        private static List<T> Culture<T>(IEnumerable<T> items, Func<T, string> key, CompareOptions options)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var keyed = items.Select(item => (Item: item, Key: key(item))).ToList();
            keyed.Sort((x, y) => compare.Compare(x.Key, y.Key, options));
            for (int i = 1; i < keyed.Count; i++)
                if (compare.Compare(keyed[i - 1].Key, keyed[i].Key, options) == 0 && !string.Equals(keyed[i - 1].Key, keyed[i].Key, StringComparison.Ordinal))
                    throw new NotHandledException("sort-culture-tie");
            return keyed.Select(k => k.Item).ToList();
        }

        /// <summary>Sort-Object -Unique on strings.</summary>
        internal static List<object> CultureUnique(IEnumerable<string> values)
        {
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var sorted = Culture(values, v => v, CompareOptions.IgnoreCase);
            var result = new List<object>();
            string last = null;
            foreach (string value in sorted)
            {
                if (last != null && compare.Compare(last, value, CompareOptions.IgnoreCase) == 0) continue;
                result.Add(value);
                last = value;
            }
            return result;
        }
    }
}
