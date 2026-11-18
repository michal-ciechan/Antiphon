// Compiled parts of Write-TestPhaseSummaryFiles (test-phase-evidence.ps1): its two Sort-Object calls and the
// ConvertTo-Json -Depth 50 -Compress of timing/summary.json. Each method returns exactly what the PowerShell
// expression returned, or null when an input is outside what it reproduces; the caller then runs the original
// PowerShell expression.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.CompilerServices;
using System.Text;

namespace Antiphon.Nightly
{
    public static class TestPhaseSummaryWrite
    {
        private sealed class Entry
        {
            public object Item;
            public object Key;
            public long Total;
        }

        // Sort-Object (PowerShell 7) without -Stable/-Top/-Bottom sorts its List<OrderByPropertyEntry> in place
        // with List<T>.Sort, an unstable introsort. Sorting a list of the same entries, in input order, with a
        // comparer of the same signs on the same runtime gives the same permutation, ties included.
        private sealed class StringKeyComparer : IComparer<Entry>
        {
            private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
            public int Compare(Entry x, Entry y) => LanguagePrimitives.Compare(x.Key, y.Key, true, _culture);
        }

        private sealed class TotalComparer : IComparer<Entry>
        {
            public int Compare(Entry x, Entry y) => x.Total.CompareTo(y.Total);
        }

        private static List<Entry> Entries(object tests)
        {
            if (tests == null) return null;
            var baseObject = tests is PSObject ps ? ps.BaseObject : tests;
            if (!(baseObject is object[] array)) return null;
            var entries = new List<Entry>(array.Length);
            foreach (var item in array)
            {
                if (item == null) return null;
                var inner = item is PSObject wrapped ? wrapped.BaseObject : item;
                if (!(inner is IDictionary) || inner is PSCustomObject) return null;
                entries.Add(new Entry { Item = item });
            }
            return entries;
        }

        private static object[] Items(List<Entry> entries)
        {
            var result = new object[entries.Count];
            for (var i = 0; i < result.Length; i++) result[i] = entries[i].Item;
            return result;
        }

        /// <summary><c>@($tests | Sort-Object { [string]$_.testId })</c>; null when not reproduced.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static object[] SortByTestId(object tests)
        {
            var entries = Entries(tests);
            if (entries == null) return null;
            foreach (var entry in entries) entry.Key = PsValue.Str(PsValue.Prop(entry.Item, "testId"));
            entries.Sort(new StringKeyComparer());
            return Items(entries);
        }

        private static readonly string[] TotalFields = { "db", "host", "process", "fs", "network", "fixture", "overlapMs", "remainderMs" };

        /// <summary>
        /// <c>@($tests | Sort-Object { -1 * ([long]$_.db + ... + [long]$_.remainderMs) })</c>; null when not
        /// reproduced (a [long] cast that throws, or a sum or negation that leaves Int64, where PowerShell's
        /// arithmetic turns to double).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static object[] RankByTotal(object tests)
        {
            var entries = Entries(tests);
            if (entries == null) return null;
            try
            {
                foreach (var entry in entries)
                {
                    long sum = 0;
                    foreach (var field in TotalFields)
                    {
                        var value = PsValue.Prop(entry.Item, field);
                        if (value is PSObject ps) value = ps.BaseObject;
                        long part;
                        if (value is long l) part = l;
                        else if (value is int i) part = i;
                        else part = (long)LanguagePrimitives.ConvertTo(value, typeof(long), CultureInfo.InvariantCulture);
                        sum = checked(sum + part);
                    }
                    entry.Total = checked(-1 * sum);
                }
            }
            catch (Exception)
            {
                return null;
            }
            entries.Sort(new TotalComparer());
            return Items(entries);
        }

        /// <summary>
        /// <c>ConvertTo-Json -InputObject $summary -Depth 50 -Compress</c> for a document of dictionaries with
        /// string keys, arrays, strings, Int32/Int64, booleans and nulls; null for anything else (the caller
        /// then serializes with ConvertTo-Json). Strings are escaped as Newtonsoft's StringEscapeHandling.Default.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static string SummaryJson(object summary)
        {
            var sb = new StringBuilder(1 << 16);
            return Write(sb, summary, 0) ? sb.ToString() : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool Write(StringBuilder sb, object value, int depth)
        {
            if (depth > 40) return false;
            if (value is PSObject ps)
            {
                if (ps.BaseObject == null || ps.BaseObject is PSCustomObject) return false;
                // ConvertTo-Json adds a wrapped value's extended (note, script, alias...) properties.
                foreach (var property in ps.Properties)
                    if (property.MemberType != PSMemberTypes.Property) return false;
                value = ps.BaseObject;
            }
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return true;
                case string s:
                    WriteString(sb, s);
                    return true;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return true;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return true;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return true;
                case IDictionary dictionary:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (DictionaryEntry pair in dictionary)
                    {
                        if (!(pair.Key is string key)) return false;
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, key);
                        sb.Append(':');
                        if (!Write(sb, pair.Value, depth + 1)) return false;
                    }
                    sb.Append('}');
                    return true;
                }
                case IList list when value is object[] || value is ArrayList || value.GetType().IsGenericType:
                {
                    sb.Append('[');
                    for (var n = 0; n < list.Count; n++)
                    {
                        if (n > 0) sb.Append(',');
                        if (!Write(sb, list[n], depth + 1)) return false;
                    }
                    sb.Append(']');
                    return true;
                }
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            var start = 0;
            for (var n = 0; n < s.Length; n++)
            {
                var c = s[n];
                string escaped;
                switch (c)
                {
                    case '"': escaped = "\\\""; break;
                    case '\\': escaped = "\\\\"; break;
                    case '\b': escaped = "\\b"; break;
                    case '\t': escaped = "\\t"; break;
                    case '\n': escaped = "\\n"; break;
                    case '\f': escaped = "\\f"; break;
                    case '\r': escaped = "\\r"; break;
                    default:
                        if (c < (char)0x20 || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029)
                            escaped = "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
                        else continue;
                        break;
                }
                if (n > start) sb.Append(s, start, n - start);
                sb.Append(escaped);
                start = n + 1;
            }
            if (s.Length > start) sb.Append(s, start, s.Length - start);
            sb.Append('"');
        }
    }
}
