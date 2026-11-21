// Reading fields of PowerShell objects (ConvertFrom-Json's PSCustomObject, a wrapped value) for code that is otherwise
// free of PowerShell types. Every PowerShell type reference lives here, in methods called only when such an object is
// actually passed (or, from PsValue and PsOps, only while PowerShell is loaded: PsRuntime.Loaded), so plain callers
// (and the in-process tests) never load System.Management.Automation.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using System.Runtime.CompilerServices;

namespace Antiphon.Nightly
{
    internal static class PsFields
    {
        /// <summary>The named property (case-insensitive) of a PowerShell object, unwrapped; null when absent.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Get(object o, string name)
        {
            var ps = o as PSObject ?? PSObject.AsPSObject(o);
            var p = ps.Properties[name];
            if (p == null) return null;
            object v;
            try { v = p.Value; } catch (GetValueException) { return null; }
            return Base(v);
        }

        /// <summary>A wrapped value's base object; a PSCustomObject (a JSON object) stays wrapped.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Base(object v)
        {
            if (v is PSObject ps && !(ps.BaseObject is PSCustomObject)) return ps.BaseObject;
            return v;
        }

        /// <summary>ConvertFrom-Json's JSON object (a PSCustomObject).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsCustomObject(object v) => v is PSObject ps && ps.BaseObject is PSCustomObject;

        /// <summary>The property names of a PowerShell object.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string[] Names(object o)
        {
            var ps = o as PSObject ?? PSObject.AsPSObject(o);
            var names = new System.Collections.Generic.List<string>();
            foreach (var p in ps.Properties) names.Add(p.Name);
            return names.ToArray();
        }

        // PsValue's PowerShell readings (PsRuntime.Loaded).

        /// <summary><c>$o.Name</c>: a note or adapted property, or a dictionary key; null when absent.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Prop(object o, string name)
        {
            if (o is PSObject ps)
            {
                if (ps.BaseObject is IDictionary wrapped && !(ps.BaseObject is PSCustomObject))
                {
                    var note = ps.Properties[name];
                    return note != null ? note.Value : wrapped[name];
                }
                return ps.Properties[name]?.Value;
            }
            if (o is IDictionary d) return d.Contains(name) ? d[name] : null;
            return PSObject.AsPSObject(o).Properties[name]?.Value;
        }

        /// <summary><c>$o.PSObject.Properties[name]</c>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Property(object o, string name, out bool found)
        {
            var property = PSObject.AsPSObject(o).Properties[name];
            found = property != null;
            return property?.Value;
        }

        /// <summary>ConvertTo-NightlyPolicyMap: a dictionary's keys or an object's note properties, first wins.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static System.Collections.Specialized.OrderedDictionary NoteMap(object o)
        {
            var map = new System.Collections.Specialized.OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            var bare = o is PSObject wrapper ? wrapper.BaseObject : o;
            if (bare is IDictionary d)
            {
                foreach (DictionaryEntry e in d)
                {
                    var name = PsValue.Str(e.Key);
                    if (!string.IsNullOrWhiteSpace(name) && !map.Contains(name)) map.Add(name, e.Value);
                }
                return map;
            }
            foreach (var p in PSObject.AsPSObject(o).Properties)
            {
                if (!(p is PSNoteProperty) || string.IsNullOrWhiteSpace(p.Name) || map.Contains(p.Name)) continue;
                map.Add(p.Name, p.Value);
            }
            return map;
        }

        /// <summary><c>[string]$o</c> of a non-null value that is not a string.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Str(object o)
        {
            if (o is PSObject ps && ps.BaseObject is string b) return b;
            return (string)LanguagePrimitives.ConvertTo(o, typeof(string), CultureInfo.InvariantCulture) ?? "";
        }

        /// <summary><c>@($o)</c> of a non-null value.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object[] AsArray(object o)
        {
            var baseObject = o is PSObject ps ? ps.BaseObject : o;
            if (baseObject is string || baseObject is IDictionary || !(baseObject is IEnumerable enumerable))
                return new object[] { o };
            var items = new List<object>();
            foreach (var item in enumerable) items.Add(item);
            return items.ToArray();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsTrue(object o) => LanguagePrimitives.IsTrue(o);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double Double(object o) => (double)LanguagePrimitives.ConvertTo(o, typeof(double), CultureInfo.InvariantCulture);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Eq(object first, object second, bool ignoreCase) =>
            LanguagePrimitives.Equals(first, second, ignoreCase, CultureInfo.InvariantCulture);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object NewObject((string Name, object Value)[] properties)
        {
            var o = new PSObject();
            foreach (var (name, value) in properties) o.Properties.Add(new PSNoteProperty(name, value));
            return o;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void AddNote(object record, string name, object value) =>
            ((PSObject)record).Properties.Add(new PSNoteProperty(name, value));

        /// <summary><c>$o.Name = value</c> for an existing property (a note or a settable adapted property).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetValue(object o, string name, object value)
        {
            var property = PSObject.AsPSObject(o).Properties[name];
            if (property != null) property.Value = value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetNote(object record, string name, object value)
        {
            var row = (PSObject)record;
            if (row.Properties[name] != null) row.Properties.Remove(name);
            row.Properties.Add(new PSNoteProperty(name, value));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object BaseObject(object o) => o is PSObject ps ? ps.BaseObject : o;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsPsObject(object o) => o is PSObject;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Compare(object a, object b, bool ignoreCase, CultureInfo culture) => LanguagePrimitives.Compare(a, b, ignoreCase, culture);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static long ToLong(object o) => (long)LanguagePrimitives.ConvertTo(o, typeof(long), CultureInfo.InvariantCulture);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsInvalidCast(Exception ex) => ex is PSInvalidCastException;

        /// <summary>Whether a PSObject has a property that is not an adapted .NET property (a note, script or alias property).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasExtendedProperty(object o)
        {
            foreach (var property in ((PSObject)o).Properties)
                if (property.MemberType != PSMemberTypes.Property) return true;
            return false;
        }

        /// <summary>Every property of <c>PSObject.AsPSObject(o)</c> (adapted and note) in order; a throwing getter reads null.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static List<KeyValuePair<string, object>> AllProperties(object o)
        {
            var properties = new List<KeyValuePair<string, object>>();
            foreach (PSPropertyInfo p in PSObject.AsPSObject(o).Properties)
            {
                object v;
                try { v = p.Value; } catch { v = null; }
                properties.Add(new KeyValuePair<string, object>(p.Name, v));
            }
            return properties;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsAutomationNull(object o) => ReferenceEquals(o, System.Management.Automation.Internal.AutomationNull.Value);

        /// <summary>A PSObject's gettable properties, in order.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static List<KeyValuePair<string, object>> GettableProperties(object o)
        {
            var properties = new List<KeyValuePair<string, object>>();
            foreach (var property in ((PSObject)o).Properties)
                if (property.IsGettable) properties.Add(new KeyValuePair<string, object>(property.Name, property.Value));
            return properties;
        }

        /// <summary>Whether any property of <c>PSObject.AsPSObject(o)</c> is a note property (an instance member).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasNoteProperty(object o)
        {
            foreach (var property in PSObject.AsPSObject(o).Properties)
                if (property is PSNoteProperty) return true;
            return false;
        }

        /// <summary>A PSObject's properties when every one is a note property; false otherwise.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool OnlyNoteProperties(object o, out List<KeyValuePair<string, object>> properties)
        {
            properties = new List<KeyValuePair<string, object>>();
            foreach (var property in ((PSObject)o).Properties)
            {
                if (!(property is PSNoteProperty)) return false;
                properties.Add(new KeyValuePair<string, object>(property.Name, property.Value));
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object Wrap(object o) => o is PSObject ? o : PSObject.AsPSObject(o);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object NewWrapper(object o) => new PSObject(o);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void AddMemberForce(object target, string name, object value)
        {
            var ps = PSObject.AsPSObject(target);
            var previous = ps.Members[name];
            if (previous != null && previous.IsInstance) ps.Members.Remove(name);
            ps.Members.Add(new PSNoteProperty(name, value));
        }

        /// <summary><c>$o.Name = value</c> on an existing property (set in place, its order kept); a missing one throws.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SetValue(object target, string name, object value)
        {
            if (!(target is PSObject) && target is System.Collections.IDictionary dictionary) { dictionary[name] = value; return; }
            var property = PSObject.AsPSObject(target).Properties[name];
            if (property == null) throw new InvalidOperationException("The property '" + name + "' cannot be found on this object. Verify that the property exists and can be set.");
            property.Value = value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static object[] NewObjectArray(int length) => new PSObject[length];

        /// <summary>The note properties of <c>PSObject.AsPSObject(o)</c>, in order.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static List<KeyValuePair<string, object>> Notes(object o)
        {
            var notes = new List<KeyValuePair<string, object>>();
            foreach (var property in PSObject.AsPSObject(o).Properties)
                if (property.MemberType == PSMemberTypes.NoteProperty) notes.Add(new KeyValuePair<string, object>(property.Name, property.Value));
            return notes;
        }

        /// <summary>The name of the property <paramref name="name"/> resolves to (case-insensitive), or null.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string PropertyName(object o, string name) => PSObject.AsPSObject(o).Properties[name]?.Name;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int ToInt(object o) => (int)LanguagePrimitives.ConvertTo(o, typeof(int), CultureInfo.InvariantCulture);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Compare(object a, object b) => LanguagePrimitives.Compare(a, b, false, CultureInfo.CurrentCulture);
    }
}
