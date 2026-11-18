// Compiled parts of Write-TestPhaseSummaryFiles (test-phase-evidence.ps1): its two Sort-Object calls and the
// ConvertTo-Json -Depth 50 -Compress of timing/summary.json. Each method returns exactly what the PowerShell
// expression returned and throws ArgumentException for an input it does not reproduce (none that a summary
// report carries: strings, numbers, booleans, nulls, dictionaries, arrays and custom objects).
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
            // RankByTotal's key: TotalLong unless the PowerShell arithmetic left Int64 (TotalIsDouble).
            public long TotalLong;
            public double TotalDouble;
            public bool TotalIsDouble;
        }

        // Sort-Object (PowerShell 7) without -Stable/-Top/-Bottom sorts its List<OrderByPropertyEntry> in place
        // with List<T>.Sort, an unstable introsort. Sorting a list of the same entries, in input order, with a
        // comparer of the same signs on the same runtime gives the same permutation, ties included.
        private sealed class StringKeyComparer : IComparer<Entry>
        {
            private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
            public int Compare(Entry x, Entry y) => LanguagePrimitives.Compare(x.Key, y.Key, true, _culture);
        }

        // LanguagePrimitives.Compare on two numbers: Int64 with Int64 as Int64, otherwise both as Double (the
        // larger type of the pair).
        private sealed class TotalComparer : IComparer<Entry>
        {
            public int Compare(Entry x, Entry y)
            {
                if (!x.TotalIsDouble && !y.TotalIsDouble) return x.TotalLong.CompareTo(y.TotalLong);
                var a = x.TotalIsDouble ? x.TotalDouble : (double)x.TotalLong;
                var b = y.TotalIsDouble ? y.TotalDouble : (double)y.TotalLong;
                return a.CompareTo(b);
            }
        }

        // The pipeline input of $tests | Sort-Object: each element of an enumerable (not a string or a
        // dictionary), else the value itself; nothing for null.
        private static List<Entry> Entries(object tests)
        {
            var entries = new List<Entry>();
            if (tests == null) return entries;
            var baseObject = tests is PSObject ps ? ps.BaseObject : tests;
            if (baseObject is IEnumerable enumerable && !(baseObject is string) && !(baseObject is IDictionary))
            {
                foreach (var item in enumerable) entries.Add(new Entry { Item = item });
            }
            else
            {
                entries.Add(new Entry { Item = tests });
            }
            return entries;
        }

        private static object[] Items(List<Entry> entries)
        {
            var result = new object[entries.Count];
            for (var i = 0; i < result.Length; i++) result[i] = entries[i].Item;
            return result;
        }

        /// <summary><c>@($tests | Sort-Object { [string]$_.testId })</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static object[] SortByTestId(object tests)
        {
            var entries = Entries(tests);
            foreach (var entry in entries) entry.Key = PsValue.Str(PsValue.Prop(entry.Item, "testId"));
            entries.Sort(new StringKeyComparer());
            return Items(entries);
        }

        private static readonly string[] TotalFields = { "db", "host", "process", "fs", "network", "fixture", "overlapMs", "remainderMs" };

        /// <summary>
        /// <c>@($tests | Sort-Object { -1 * ([long]$_.db + ... + [long]$_.remainderMs) })</c>. A sum or negation
        /// that leaves Int64 continues in Double, as PowerShell's arithmetic does; a value [long] cannot convert
        /// throws ArgumentException.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static object[] RankByTotal(object tests)
        {
            var entries = Entries(tests);
            foreach (var entry in entries)
            {
                long sum = 0;
                double sumDouble = 0;
                var isDouble = false;
                var first = true;
                foreach (var field in TotalFields)
                {
                    var part = ToLong(PsValue.Prop(entry.Item, field), field);
                    if (first) { sum = part; first = false; continue; }
                    if (isDouble) { sumDouble += part; continue; }
                    try { sum = checked(sum + part); }
                    catch (OverflowException) { sumDouble = (double)sum + (double)part; isDouble = true; }
                }
                if (isDouble)
                {
                    entry.TotalIsDouble = true;
                    entry.TotalDouble = -1.0 * sumDouble;
                }
                else if (sum == long.MinValue)
                {
                    entry.TotalIsDouble = true;
                    entry.TotalDouble = -1.0 * (double)sum;
                }
                else
                {
                    entry.TotalLong = -sum;
                }
            }
            entries.Sort(new TotalComparer());
            return Items(entries);
        }

        // [long]$value
        private static long ToLong(object value, string field)
        {
            if (value is PSObject ps) value = ps.BaseObject;
            if (value is long l) return l;
            if (value is int i) return i;
            try { return (long)LanguagePrimitives.ConvertTo(value, typeof(long), CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is PSInvalidCastException || ex is InvalidCastException || ex is OverflowException || ex is FormatException)
            {
                throw new ArgumentException("phase summary: [long] cannot convert " + field + " (" + (value?.GetType().FullName ?? "null") + ")", ex);
            }
        }

        /// <summary>
        /// <c>ConvertTo-Json -InputObject $summary -Depth 50 -Compress</c> for a document of dictionaries with
        /// string keys, custom objects, arrays, strings, numbers, booleans and nulls. Strings are escaped as
        /// Newtonsoft's StringEscapeHandling.Default; numbers are written as Newtonsoft writes them. Anything else
        /// throws ArgumentException.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static string SummaryJson(object summary)
        {
            var sb = new StringBuilder(1 << 16);
            Write(sb, summary, 0);
            return sb.ToString();
        }

        private static ArgumentException Unsupported(string what) =>
            new ArgumentException("phase summary JSON: " + what + " is not supported");

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Write(StringBuilder sb, object value, int depth)
        {
            // ConvertTo-Json -Depth 50 turns a deeper object into its string; no summary comes near it.
            if (depth > 40) throw Unsupported("nesting deeper than 40");
            if (value is PSObject ps)
            {
                if (ps.BaseObject is PSCustomObject)
                {
                    WriteCustomObject(sb, ps, depth);
                    return;
                }
                // ConvertTo-Json adds a wrapped value's extended (note, script, alias...) properties.
                foreach (var property in ps.Properties)
                    if (property.MemberType != PSMemberTypes.Property) throw Unsupported("a value with extended properties");
                value = ps.BaseObject;
            }
            switch (value)
            {
                case null:
                case DBNull _:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case char c:
                    WriteString(sb, c.ToString());
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case uint _:
                case ulong _:
                    sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case double d:
                    WriteFloat(sb, d, d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    WriteFloat(sb, f, f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal m:
                    sb.Append(EnsureDecimalPlace(m.ToString(CultureInfo.InvariantCulture)));
                    return;
                case IDictionary dictionary:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (DictionaryEntry pair in dictionary)
                    {
                        if (!(pair.Key is string key)) throw Unsupported("a non-string dictionary key");
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, key);
                        sb.Append(':');
                        Write(sb, pair.Value, depth + 1);
                    }
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    var n = 0;
                    foreach (var item in list)
                    {
                        if (n++ > 0) sb.Append(',');
                        Write(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    return;
                }
                default:
                    throw Unsupported("a " + value.GetType().FullName);
            }
        }

        // A [pscustomobject]: its properties in order, each value as ConvertTo-Json reads it (a property whose
        // getter throws is null).
        private static void WriteCustomObject(StringBuilder sb, PSObject ps, int depth)
        {
            sb.Append('{');
            var first = true;
            foreach (var property in ps.Properties)
            {
                object propertyValue = null;
                try { propertyValue = property.Value; } catch (Exception) { }
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, property.Name);
                sb.Append(':');
                Write(sb, propertyValue, depth + 1);
            }
            sb.Append('}');
        }

        // Newtonsoft JsonConvert.ToString(double/float): "R", ".0" after a whole number, and NaN/Infinity as
        // strings (FloatFormatHandling.String).
        private static void WriteFloat(StringBuilder sb, double value, string text)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                sb.Append('"').Append(text).Append('"');
                return;
            }
            sb.Append(EnsureDecimalPlace(text));
        }

        private static string EnsureDecimalPlace(string text) =>
            text.IndexOf('.') >= 0 || text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0 ? text : text + ".0";

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
