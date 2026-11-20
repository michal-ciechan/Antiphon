// Shared readers for the CARD-1836 shadow and dirty ports.
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

        public static bool IsPs(object value)
        {
            value = See(value);
            if (value == null) return false;
            var name = value.GetType().FullName;
            return name == "System.Management.Automation.PSObject"
                || name == "System.Management.Automation.PSCustomObject";
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
            if (IsPs(source))
            {
                TryPs(source, name, true, out var value);
                return See(value);
            }
            var property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return See(property == null ? null : property.GetValue(source));
        }

        public static bool HasKey(object source, string name)
        {
            source = See(source);
            if (source == null) return false;
            if (IsDictionary(source)) return TryDictionary(source, name, true, out _);
            if (IsPs(source)) return TryPs(source, name, true, out _);
            return false;
        }

        public static List<string> Names(object source)
        {
            source = See(source);
            var names = new List<string>();
            if (source == null || !IsDictionary(source) && !IsPs(source)) return names;
            if (source is IDictionary dictionary)
            {
                foreach (var key in dictionary.Keys)
                {
                    if (key is string text) names.Add(text);
                }
                return names;
            }
            if (source is IDictionary<string, object> generic)
            {
                foreach (var key in generic.Keys) names.Add(key);
                return names;
            }
            if (!IsPs(source)) return names;
            foreach (var property in Properties(source))
            {
                var propertyName = property.GetType().GetProperty("Name").GetValue(property) as string;
                if (propertyName != null) names.Add(propertyName);
            }
            return names;
        }

        public static object DirtyProp(object source, string name)
        {
            source = See(source);
            if (source == null) return null;
            if (IsDictionary(source))
            {
                TryDictionary(source, name, false, out var value);
                return See(value);
            }
            if (IsPs(source))
            {
                TryPs(source, name, false, out var value);
                return See(value);
            }
            return null;
        }

        public static bool Empty(object value)
        {
            value = See(value);
            if (value == null) return true;
            if (value is string text) return string.IsNullOrWhiteSpace(text);
            if (IsDictionary(value)) return false;
            if (value is ICollection collection) return collection.Count == 0;
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
            if (value is string || IsDictionary(value) || IsPs(value))
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

        static bool TryPs(object source, string name, bool ordinal, out object value)
        {
            value = null;
            foreach (var property in Properties(source))
            {
                var propertyName = property.GetType().GetProperty("Name").GetValue(property) as string;
                var comparison = ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                if (!string.Equals(propertyName, name, comparison)) continue;
                value = property.GetType().GetProperty("Value").GetValue(property);
                return true;
            }
            return false;
        }

        static IEnumerable Properties(object source)
        {
            var collection = source.GetType().GetProperty("Properties").GetValue(source) as IEnumerable;
            if (collection == null) yield break;
            foreach (var property in collection) yield return property;
        }
    }
}
