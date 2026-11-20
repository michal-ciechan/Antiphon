// Reading fields of PowerShell objects (ConvertFrom-Json's PSCustomObject, a wrapped value) for code that is otherwise
// free of PowerShell types. Every PowerShell type reference lives here, in methods called only when such an object is
// actually passed, so plain callers (and the in-process tests) never load System.Management.Automation.
using System;
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
    }
}
