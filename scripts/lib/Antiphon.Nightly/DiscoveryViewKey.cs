// The discovery index's per-chunk lookups (nightly-coverage.ps1 Get-NightlyDiscoveryIndexView and
// Get-NightlyDiscoveryIndexEntry), compiled: the view cache key of a profile's exclusions and the InProgress
// node scan.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Antiphon.Nightly
{
    public static class DiscoveryViewKey
    {
        // ConvertTo-Json -Depth 8 writes a container below this depth as its ToString() and warns. One level
        // early, so that text and its warning always come from the cmdlet.
        private const int MaxContainerDepth = 7;

        /// <summary>
        /// The view cache key for a profile's exclusions: "profile#" and the SHA-256 of a walk over their
        /// content. It replaces <c>'profile:' + (ConvertTo-Json -InputObject $ProfileExclusions -Depth 8
        /// -Compress)</c> as a key held in memory only, and is at least as fine: every string (code unit for
        /// code unit), number (with its type), boolean, date, null, property or key name, element count and
        /// position goes into the digest, each with a tag and a length, so two inputs get one key only when
        /// that JSON text was equal too. Equal content in new objects gets the same key; nothing here depends
        /// on object identity.
        /// Returns null for anything whose JSON this walk does not determine (a value of another type, a
        /// wrapped value that may carry extra members, a property that is not a note property, a key that is
        /// not a string, nesting near the cmdlet's depth limit): the caller builds the JSON key then.
        /// </summary>
        public static string PolicyKey(object exclusions)
        {
            var sb = new StringBuilder(2048);
            if (!Append(sb, exclusions, 0)) return null;
            var text = sb.ToString();
            // The UTF-16 code units as they are: an encoding would fold unpaired surrogates together.
            var bytes = new byte[text.Length * 2];
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                bytes[2 * i] = (byte)c;
                bytes[2 * i + 1] = (byte)(c >> 8);
            }
            return "profile#" + TestPhaseRecords.Sha256Hex(bytes);
        }

        private static void AppendText(StringBuilder sb, char tag, string text)
        {
            sb.Append(tag).Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
        }

        private static bool Append(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("n;"); return true; }
            if (PsValue.BaseObject(value) is ExclusionRow row)
            {
                // A reader row converts like the five-note-property object it replaced (its properties in declaration
                // order), unless members were added to this instance: its JSON would carry them.
                if (depth > MaxContainerDepth) return false;
                if (PsRuntime.Loaded && PsFields.HasNoteProperty(value)) return false;
                sb.Append("o5{");
                AppendText(sb, 'k', "Suite");
                if (!Append(sb, row.Suite, depth + 1)) return false;
                AppendText(sb, 'k', "Class");
                if (!Append(sb, row.Class, depth + 1)) return false;
                AppendText(sb, 'k', "Methods");
                if (!Append(sb, row.Methods, depth + 1)) return false;
                AppendText(sb, 'k', "Reason");
                if (!Append(sb, row.Reason, depth + 1)) return false;
                AppendText(sb, 'k', "Owner");
                if (!Append(sb, row.Owner, depth + 1)) return false;
                sb.Append('}');
                return true;
            }
            if (PsValue.IsRecord(value))
            {
                if (!PsValue.IsCustomObject(value)) return false;
                if (depth > MaxContainerDepth) return false;
                List<KeyValuePair<string, object>> properties;
                if (PsRuntime.Loaded) { if (!PsFields.OnlyNoteProperties(value, out properties)) return false; }
                else properties = new List<KeyValuePair<string, object>>(((PlainObject)value).Entries);
                sb.Append('o').Append(properties.Count.ToString(CultureInfo.InvariantCulture)).Append('{');
                foreach (var property in properties)
                {
                    AppendText(sb, 'k', property.Key ?? "");
                    if (!Append(sb, property.Value, depth + 1)) return false;
                }
                sb.Append('}');
                return true;
            }
            if (value is string s) { AppendText(sb, 's', s); return true; }
            if (value is bool b) { sb.Append(b ? "b1;" : "b0;"); return true; }
            if (value is Enum) return false;
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Decimal:
                    AppendText(sb, 'i', ((int)Type.GetTypeCode(value.GetType())).ToString(CultureInfo.InvariantCulture)
                        + "=" + ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    return true;
                case TypeCode.Single:
                    AppendText(sb, 'f', ((float)value).ToString("R", CultureInfo.InvariantCulture));
                    return true;
                case TypeCode.Double:
                    AppendText(sb, 'd', ((double)value).ToString("R", CultureInfo.InvariantCulture));
                    return true;
                case TypeCode.DateTime:
                    var date = (DateTime)value;
                    AppendText(sb, 't', date.Ticks.ToString(CultureInfo.InvariantCulture) + "/" + ((int)date.Kind).ToString(CultureInfo.InvariantCulture));
                    return true;
            }
            if (value is IDictionary dictionary)
            {
                if (depth > MaxContainerDepth) return false;
                sb.Append('h').Append(dictionary.Count.ToString(CultureInfo.InvariantCulture)).Append('{');
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!(entry.Key is string key)) return false;
                    AppendText(sb, 'k', key);
                    if (!Append(sb, entry.Value, depth + 1)) return false;
                }
                sb.Append('}');
                return true;
            }
            if (value is IList list)
            {
                if (depth > MaxContainerDepth) return false;
                sb.Append('a').Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append('[');
                foreach (var item in list)
                {
                    if (!Append(sb, item, depth + 1)) return false;
                }
                sb.Append(']');
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get-NightlyDiscoveryIndexEntry's scan: the nodes of <c>@($nodes)</c> whose <c>$n.State -eq
        /// 'InProgress'</c>, in order, as the same objects. Returns null when a node or its State is a list
        /// (PowerShell would enumerate its members or filter it): the caller runs its own loop then.
        /// </summary>
        public static object[] InProgressNodes(object nodes)
        {
            var hits = new List<object>();
            foreach (var node in PsValue.AsArray(nodes))
            {
                if (node == null) continue;
                if (IsList(node)) return null;
                var state = PsValue.Prop(node, "State");
                if (state == null) continue;
                state = PsValue.BaseObject(state);
                if (IsList(state)) return null;
                if (PsValue.Eq(state, "InProgress", true)) hits.Add(node);
            }
            return hits.ToArray();
        }

        private static bool IsList(object value)
        {
            var baseObject = PsValue.BaseObject(value);
            return baseObject is IEnumerable && !(baseObject is string) && !(baseObject is IDictionary) && !(baseObject is PlainObject);
        }
    }
}
