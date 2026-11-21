// The core without PowerShell. A caller with no System.Management.Automation in its process (the in-process tests,
// a C# command) passes and receives plain values: a PlainObject where PowerShell passes a custom object, IDictionary,
// arrays and other enumerables, strings and scalars. A PowerShell caller is unchanged: while that assembly is loaded,
// PsValue and PsOps run the PowerShell readings they always ran (PsFields, never inlined, so a plain caller never
// loads it). The plain readings below are PowerShell's for these plain shapes only. ASCII-only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>Whether PowerShell (System.Management.Automation) is in this process.</summary>
    internal static class PsRuntime
    {
        internal static readonly bool Loaded = Probe();

        private static bool Probe()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(assembly.GetName().Name, "System.Management.Automation", StringComparison.Ordinal)) return true;
            try { return Type.GetType("System.Management.Automation.PSObject, System.Management.Automation", false) != null; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// The plain twin of a PowerShell custom object ([pscustomobject], or a PSObject the core builds): named values
    /// in the order they were added, names compared case-insensitively. It is not a dictionary, as a custom object
    /// is not one, nor a list.
    /// </summary>
    public sealed class PlainObject
    {
        private readonly List<KeyValuePair<string, object>> _entries = new List<KeyValuePair<string, object>>();

        public PlainObject() { }

        public PlainObject(params (string Name, object Value)[] entries)
        {
            foreach (var (name, value) in entries ?? Array.Empty<(string, object)>()) Add(name, value);
        }

        public int Count => _entries.Count;

        public IEnumerable<string> Names
        {
            get { foreach (var entry in _entries) yield return entry.Key; }
        }

        private int IndexOf(string name)
        {
            for (var i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].Key, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public bool Has(string name) => IndexOf(name) >= 0;

        public bool TryGet(string name, out object value)
        {
            var i = IndexOf(name);
            value = i < 0 ? null : _entries[i].Value;
            return i >= 0;
        }

        /// <summary>The named value (null when absent); setting replaces it in place, or adds it last.</summary>
        public object this[string name]
        {
            get => TryGet(name, out var value) ? value : null;
            set
            {
                var i = IndexOf(name);
                if (i >= 0) _entries[i] = new KeyValuePair<string, object>(_entries[i].Key, value);
                else _entries.Add(new KeyValuePair<string, object>(name, value));
            }
        }

        /// <summary>Adds a new name (PSNoteProperty add): an existing name refuses.</summary>
        public void Add(string name, object value)
        {
            if (IndexOf(name) >= 0) throw new ArgumentException("duplicate property " + name);
            _entries.Add(new KeyValuePair<string, object>(name, value));
        }

        public bool Remove(string name)
        {
            var i = IndexOf(name);
            if (i < 0) return false;
            _entries.RemoveAt(i);
            return true;
        }

        /// <summary>The named values in order.</summary>
        public IReadOnlyList<KeyValuePair<string, object>> Entries => _entries;

        /// <summary>[string] of a custom object: @{name=value; ...}.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder("@{");
            for (var i = 0; i < _entries.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(_entries[i].Key).Append('=').Append(PlainValues.ElementText(_entries[i].Value));
            }
            return sb.Append('}').ToString();
        }
    }

    /// <summary>PowerShell's readings of plain values (no PowerShell type is involved).</summary>
    internal static class PlainValues
    {
        /// <summary><c>$o.Name</c>: a custom object's value, a dictionary key, or a public property or field.</summary>
        internal static object Prop(object o, string name)
        {
            if (o == null) return null;
            if (o is PlainObject plain) return plain[name];
            if (o is IDictionary d) return d.Contains(name) ? d[name] : null;
            return Member(o, name, out _);
        }

        /// <summary>A property (never a dictionary key): a custom object's value, else a public property or field.</summary>
        internal static object Property(object o, string name, out bool found)
        {
            found = false;
            if (o == null) return null;
            if (o is PlainObject plain) { found = plain.TryGet(name, out var value); return value; }
            return Member(o, name, out found);
        }

        private static object Member(object o, string name, out bool found)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
            var type = o.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                found = true;
                try { return property.GetValue(o); } catch (Exception) { return null; }
            }
            var field = type.GetField(name, flags);
            found = field != null;
            return field?.GetValue(o);
        }

        /// <summary>A custom object's values; else the public instance properties (a getter that throws reads null).</summary>
        internal static List<KeyValuePair<string, object>> AllProperties(object o)
        {
            if (o is PlainObject plain) return new List<KeyValuePair<string, object>>(plain.Entries);
            var properties = new List<KeyValuePair<string, object>>();
            foreach (var property in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null) continue;
                object v;
                try { v = property.GetValue(o); } catch (Exception) { v = null; }
                properties.Add(new KeyValuePair<string, object>(property.Name, v));
            }
            return properties;
        }

        /// <summary>The note properties: a custom object's names; a dictionary's string keys.</summary>
        internal static System.Collections.Specialized.OrderedDictionary NoteMap(object o)
        {
            var map = new System.Collections.Specialized.OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            if (o is PlainObject plain)
            {
                foreach (var entry in plain.Entries)
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !map.Contains(entry.Key)) map.Add(entry.Key, entry.Value);
                return map;
            }
            if (o is IDictionary d)
            {
                foreach (DictionaryEntry e in d)
                {
                    var name = Str(e.Key);
                    if (!string.IsNullOrWhiteSpace(name) && !map.Contains(name)) map.Add(name, e.Value);
                }
            }
            return map;
        }

        /// <summary><c>@($o)</c>.</summary>
        internal static object[] AsArray(object o)
        {
            if (o == null) return new object[] { null };
            if (o is string || o is IDictionary || o is PlainObject || !(o is IEnumerable enumerable)) return new object[] { o };
            var items = new List<object>();
            foreach (var item in enumerable) items.Add(item);
            return items.ToArray();
        }

        /// <summary><c>[string]$o</c> (LanguagePrimitives.ConvertTo, invariant culture).</summary>
        internal static string Str(object o)
        {
            switch (o)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "True" : "False";
                case char c: return c.ToString();
                case double d: return d.ToString("G15", CultureInfo.InvariantCulture);
                case float f: return f.ToString("G7", CultureInfo.InvariantCulture);
                case DBNull _: return "";
                case PlainObject plain: return plain.ToString();
                case IDictionary dictionary: return dictionary.ToString();
                case IEnumerable enumerable:
                    var parts = new List<string>();
                    foreach (var item in enumerable) parts.Add(ElementText(item));
                    return string.Join(" ", parts);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return o.ToString() ?? "";
            }
        }

        /// <summary>An element of a joined list, or a custom object's value in its text: a nested list is its type name.</summary>
        internal static string ElementText(object o)
        {
            if (o == null) return "";
            if (!(o is string) && !(o is PlainObject) && o is IEnumerable) return o.ToString();
            return Str(o);
        }

        /// <summary>LanguagePrimitives.IsTrue.</summary>
        internal static bool IsTrue(object o)
        {
            switch (o)
            {
                case null: return false;
                case bool b: return b;
                case string s: return s.Length != 0;
                case IList list:
                    if (list.Count == 0) return false;
                    if (list.Count > 1) return true;
                    if (list[0] is IList first) return first.Count > 0;
                    return IsTrue(list[0]);
            }
            if (IsNumeric(o)) return System.Convert.ToDouble(o, CultureInfo.InvariantCulture) != 0;
            return true;
        }

        internal static bool IsNumeric(object o) =>
            o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint || o is long || o is ulong
            || o is float || o is double || o is decimal;

        /// <summary>[double]$o.</summary>
        internal static double Double(object o)
        {
            switch (o)
            {
                case null: return 0.0;
                case double d: return d;
                case string s:
                    if (string.IsNullOrWhiteSpace(s)) return 0.0;
                    return double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                case bool b: return b ? 1.0 : 0.0;
            }
            if (IsNumeric(o)) return System.Convert.ToDouble(o, CultureInfo.InvariantCulture);
            throw new InvalidCastException("Cannot convert " + o.GetType().FullName + " to System.Double.");
        }

        /// <summary>A custom object's names, else the public instance property and field names.</summary>
        internal static string[] PropertyNames(object o)
        {
            if (o is PlainObject plain) return new List<string>(plain.Names).ToArray();
            var names = new List<string>();
            foreach (var property in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (property.GetIndexParameters().Length == 0) names.Add(property.Name);
            foreach (var field in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)) names.Add(field.Name);
            return names.ToArray();
        }

        /// <summary>The name of the property <paramref name="name"/> resolves to (case-insensitive), or null.</summary>
        internal static string PropertyName(object o, string name)
        {
            if (o is PlainObject plain)
            {
                foreach (var n in plain.Names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return n;
                return null;
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
            var type = o.GetType();
            return type.GetProperty(name, flags)?.Name ?? type.GetField(name, flags)?.Name;
        }

        /// <summary>[int]$o: numbers round half to even, strings parse.</summary>
        internal static int Int(object o)
        {
            switch (o)
            {
                case null: return 0;
                case int i: return i;
                case string s: return string.IsNullOrWhiteSpace(s) ? 0 : System.Convert.ToInt32(Double(s));
                case bool b: return b ? 1 : 0;
            }
            if (IsNumeric(o)) return System.Convert.ToInt32(o, CultureInfo.InvariantCulture);
            throw new InvalidCastException("Cannot convert " + o.GetType().FullName + " to System.Int32.");
        }

        /// <summary>[long]$o: numbers round half to even, strings parse.</summary>
        internal static long Long(object o)
        {
            switch (o)
            {
                case null: return 0;
                case long l: return l;
                case string s: return string.IsNullOrWhiteSpace(s) ? 0 : System.Convert.ToInt64(Double(s));
                case bool b: return b ? 1 : 0;
                case double d: return System.Convert.ToInt64(d);
            }
            if (IsNumeric(o)) return System.Convert.ToInt64(o, CultureInfo.InvariantCulture);
            throw new InvalidCastException("Cannot convert " + o.GetType().FullName + " to System.Int64.");
        }

        /// <summary>LanguagePrimitives.Compare (current culture) of two non-null plain scalars.</summary>
        internal static int Compare(object a, object b)
        {
            if (a is string sa) return string.Compare(sa, b as string ?? Str(b), CultureInfo.CurrentCulture, CompareOptions.None);
            if (IsNumeric(a) && IsNumeric(b))
                return System.Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(System.Convert.ToDouble(b, CultureInfo.InvariantCulture));
            if (a is IComparable comparable && a.GetType() == b.GetType()) return comparable.CompareTo(b);
            return string.Compare(Str(a), Str(b), CultureInfo.CurrentCulture, CompareOptions.None);
        }

        /// <summary><c>first -eq second</c> (LanguagePrimitives.Equals, invariant culture) for plain scalars.</summary>
        internal static bool Equal(object first, object second, bool ignoreCase)
        {
            if (first == null) return second == null;
            if (second == null) return false;
            if (first is string text)
            {
                var other = second as string ?? Str(second);
                return CultureInfo.InvariantCulture.CompareInfo.Compare(text, other, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None) == 0;
            }
            if (first.Equals(second)) return true;
            if (IsNumeric(first) && IsNumeric(second))
                return System.Convert.ToDouble(first, CultureInfo.InvariantCulture) == System.Convert.ToDouble(second, CultureInfo.InvariantCulture);
            try
            {
                if (IsNumeric(first) && second is string s2) return System.Convert.ToDouble(first, CultureInfo.InvariantCulture) == Double(s2);
                if (first is bool fb) return fb == IsTrue(second);
            }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            return false;
        }
    }
}
