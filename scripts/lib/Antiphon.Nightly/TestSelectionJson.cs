// CARD-1313: the test selection's canonical JSON, digest, line-ending normalisation, ordinal order and JSON reader.
// The 2026-11-22 plan (2026-11-22-remaining-ps1-to-csharp.md, unit 3) ported them from scripts/lib/test-selection.ps1
// (ConvertTo-TestSelectionCanonicalJson, Get-TestSelectionDigest, ConvertTo-TestSelectionLfText, Get-TestSelectionSorted
// and ConvertFrom-Json -AsHashtable), which is deleted. The selection, its catalog hash, the rc project planner and the
// live-lane fingerprints, attestations and receipts digest through here, so the text is the one the PowerShell wrote:
// object keys in ordinal order, strings escaped as ConvertTo-Json -Compress (Newtonsoft's default escaping), value
// types in the invariant culture, an object without keys as its properties. PowerShell values (a [pscustomobject], a
// wrapped value) are read through PsValue, so a .ps1 caller passes its own hashtables and arrays. One exception: the
// reader keeps an ISO-8601-looking string a string, where ConvertFrom-Json made it a DateTime. Plain C#. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>A selection input or check refused: the message is the reason (catalog-hash-stale, selector-source-mismatch, ...).</summary>
    public sealed class TestSelectionRefusal : Exception
    {
        /// <summary>A refusal with its reason.</summary>
        public TestSelectionRefusal(string reason) : base(reason) { }
    }

    /// <summary>ConvertTo-TestSelectionCanonicalJson, Get-TestSelectionDigest and the other value helpers of the selection.</summary>
    public static class TestSelectionJson
    {
        /// <summary>
        /// ConvertTo-TestSelectionCanonicalJson: null; a string as ConvertTo-Json -Compress writes it; a bool in lower case;
        /// a dictionary with its keys (as strings) in ordinal order; any other enumerable as an array; another value type
        /// in the invariant culture, unquoted; any other object as the dictionary of its properties.
        /// </summary>
        public static string Canonical(object value)
        {
            var text = new StringBuilder();
            Write(text, value);
            return text.ToString();
        }

        /// <summary>
        /// Get-TestSelectionDigest: lower-case hex SHA-256 of the canonical JSON's UTF-8 bytes. A dictionary is hashed
        /// without its estimates and budgetObservations entries, which are attached after the digest.
        /// </summary>
        public static string Digest(object value)
        {
            if (PsValue.BaseObject(value) is IDictionary dictionary && (dictionary.Contains("estimates") || dictionary.Contains("budgetObservations")))
            {
                var copy = new OrderedDictionary(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = PsValue.Str(entry.Key);
                    if (key != "estimates" && key != "budgetObservations") copy[entry.Key] = entry.Value;
                }
                value = copy;
            }
            return Sha256Text(Canonical(value));
        }

        /// <summary>Lower-case hex SHA-256 of the text's UTF-8 bytes.</summary>
        public static string Sha256Text(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();

        /// <summary>ConvertTo-TestSelectionLfText: CR LF and CR become LF; spaces, tabs and case stay. Null is empty.</summary>
        public static string LfText(string text) => text == null ? "" : text.Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>
        /// Get-TestSelectionSorted: the distinct values (each as [string]) in ordinal order; null values are dropped. A
        /// string or a dictionary is one value; null is none.
        /// </summary>
        public static string[] Sorted(object values)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var value in Elements(values)) if (value != null) set.Add(PsValue.Str(value));
            return set.ToArray();
        }

        /// <summary>A value bound to an [object[]] parameter: null is none, a string, dictionary or scalar one element, an enumerable its items.</summary>
        internal static IEnumerable<object> Elements(object values)
        {
            if (values == null) yield break;
            var value = PsValue.BaseObject(values);
            if (value is string || value is IDictionary || !(value is IEnumerable items))
            {
                yield return values;
                yield break;
            }
            foreach (var item in items) yield return item;
        }

        /// <summary>
        /// ConvertFrom-Json -AsHashtable: an object is an OrderedDictionary with ordinal keys in document order, an array an
        /// object[], an integer a long (a BigInteger beyond it), another number a double. A repeated key is refused.
        /// </summary>
        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new TestSelectionRefusal("json-empty");
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 1024 });
            return Read(document.RootElement);
        }

        private static object Read(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new OrderedDictionary(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (map.Contains(property.Name)) throw new TestSelectionRefusal("json-duplicate-key:" + property.Name);
                        map.Add(property.Name, Read(property.Value));
                    }
                    return map;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(Read).ToArray();
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    var raw = element.GetRawText();
                    if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0)
                    {
                        if (element.TryGetInt64(out var whole)) return whole;
                        return BigInteger.Parse(raw, CultureInfo.InvariantCulture);
                    }
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }

        private static void Write(StringBuilder text, object node)
        {
            var value = PsValue.BaseObject(node);
            switch (value)
            {
                case null:
                    text.Append("null");
                    return;
                case string s:
                    WriteString(text, s);
                    return;
                case bool flag:
                    text.Append(flag ? "true" : "false");
                    return;
                case IDictionary dictionary:
                    // [string[]]@($Value.Keys) sorted ordinally; each key's own canonical text, then its value.
                    var pairs = new List<KeyValuePair<string, object>>();
                    foreach (DictionaryEntry entry in dictionary) pairs.Add(new KeyValuePair<string, object>(PsValue.Str(entry.Key), entry.Value));
                    text.Append('{');
                    var first = true;
                    foreach (var pair in pairs.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        if (!first) text.Append(',');
                        first = false;
                        WriteString(text, pair.Key);
                        text.Append(':');
                        Write(text, pair.Value);
                    }
                    text.Append('}');
                    return;
                case IEnumerable items:
                    text.Append('[');
                    var head = true;
                    foreach (var item in items)
                    {
                        if (!head) text.Append(',');
                        head = false;
                        Write(text, item);
                    }
                    text.Append(']');
                    return;
                case ValueType:
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
            // $Value.PSObject.Properties into @{}: a later property of the same name (ignoring case) replaces an earlier one.
            var properties = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (var property in PsValue.Properties(node)) properties[property.Key] = property.Value;
            Write(text, properties);
        }

        /// <summary>A JSON string as Newtonsoft's default escaping writes it (ConvertTo-Json -Compress -InputObject $text).</summary>
        internal static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\b': text.Append("\\b"); break;
                    case (char)0x0085: text.Append(@"\u0085"); break;
                    case (char)0x2028: text.Append(@"\u2028"); break;
                    case (char)0x2029: text.Append(@"\u2029"); break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
