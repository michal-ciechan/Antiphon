// Shared readers for the CARD-1836 shadow and dirty ports. Inputs are plain values: dictionaries with string keys,
// arrays and other enumerables, strings, booleans and integers. No PowerShell types.
// Dictionary keys are the real key collection. ExpandoObject is not a dictionary.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Antiphon.Nightly
{
    static class RcPort
    {
        public static void Throw(string message) => throw new InvalidOperationException(message);

        // PowerShell member access sees through PSObject. A PSCustomObject stays wrapped:
        // its notes are the properties the old scripts read.
        public static object See(object value)
        {
            for (var depth = 0; depth < 8 && value != null; depth++)
            {
                var type = value.GetType();
                if (type.FullName != "System.Management.Automation.PSObject") return value;
                var property = type.GetProperty("BaseObject");
                var baseObject = property == null ? null : property.GetValue(value);
                if (baseObject == null || ReferenceEquals(baseObject, value)) return value;
                if (baseObject.GetType().FullName == "System.Management.Automation.PSCustomObject") return value;
                value = baseObject;
            }
            return value;
        }

        public static bool IsDictionary(object value)
        {
            value = See(value);
            if (value == null || value is System.Dynamic.ExpandoObject) return false;
            if (value is IDictionary) return true;
            foreach (var iface in value.GetType().GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>)) return true;
            }
            return false;
        }

        public static object Field(object source, string name)
        {
            source = See(source);
            if (source == null) return null;
            if (IsDictionary(source))
            {
                TryDictionary(source, name, true, out var value);
                return See(value);
            }
            var property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return See(property == null ? null : property.GetValue(source));
        }

        public static bool HasKey(object source, string name) =>
            source != null && IsDictionary(source) && TryDictionary(source, name, true, out _);

        public static List<string> Names(object source)
        {
            source = See(source);
            var names = new List<string>();
            if (source is IDictionary dictionary && IsDictionary(source))
            {
                foreach (var key in dictionary.Keys)
                {
                    if (key is string text) names.Add(text);
                }
                return names;
            }
            if (source is IDictionary<string, object> generic && IsDictionary(source))
            {
                foreach (var key in generic.Keys) names.Add(key);
            }
            return names;
        }

        public static object DirtyProp(object source, string name)
        {
            if (source == null || !IsDictionary(source)) return null;
            TryDictionary(source, name, false, out var value);
            return value;
        }

        /// <summary>
        /// The one emptiness rule (advisory A-5): null, a blank string and a collection with no items are empty; a
        /// dictionary and any other value are not. Arrays, lists, sets and lazy sequences are counted by enumerating.
        /// </summary>
        public static bool Empty(object value)
        {
            value = See(value);
            if (value == null) return true;
            if (value is string text) return string.IsNullOrWhiteSpace(text);
            if (IsDictionary(value)) return false;
            if (value is IEnumerable enumerable)
            {
                var items = enumerable.GetEnumerator();
                try { return !items.MoveNext(); }
                finally { (items as IDisposable)?.Dispose(); }
            }
            return false;
        }

        public static List<object> Unwrap(object value)
        {
            value = See(value);
            var items = new List<object>();
            if (value == null)
            {
                items.Add(null);
                return items;
            }
            if (value is string || IsDictionary(value))
            {
                items.Add(value);
                return items;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable) items.Add(See(item));
                return items;
            }
            items.Add(value);
            return items;
        }

        static bool TryDictionary(object source, string name, bool ordinal, out object value)
        {
            value = null;
            if (source is IDictionary dictionary)
            {
                if (!ordinal)
                {
                    if (!dictionary.Contains(name)) return false;
                    value = dictionary[name];
                    return true;
                }
                foreach (var key in dictionary.Keys)
                {
                    if (!(key is string text)) continue;
                    if (!string.Equals(text, name, StringComparison.Ordinal)) continue;
                    value = dictionary[key];
                    return true;
                }
                return false;
            }
            if (source is IDictionary<string, object> generic)
            {
                if (!ordinal)
                {
                    if (!generic.ContainsKey(name)) return false;
                    value = generic[name];
                    return true;
                }
                foreach (var key in generic.Keys)
                {
                    if (!string.Equals(key, name, StringComparison.Ordinal)) continue;
                    value = generic[key];
                    return true;
                }
            }
            return false;
        }
    }
}
