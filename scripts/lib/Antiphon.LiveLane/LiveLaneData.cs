// The live lane's JSON (W2 slice S22): the document reader the deleted live-lane-attestation/-fingerprint/-receipt.ps1 used
// (ConvertFrom-Json -AsHashtable), and the canonical writer, digest, ordinal order and line-ending normalisation they called
// on the nightly core (Antiphon.Nightly.TestSelectionJson, the ported PsJson). This project references nothing, so these
// are copies; LiveLaneParityTests pins the writer byte for byte to the nightly one on the same values.
//
// The value model is the hashtable model the PowerShell read: an object is a LiveLaneMap (ordinal keys, document
// order, a repeated key refused), an array a List<object>, an integer a long (a BigInteger beyond it), another number a
// double, a string, a bool or null. One difference, as in the nightly reader: an ISO-8601-looking string stays a string,
// where ConvertFrom-Json made it a DateTime. Comments and trailing commas are read (Newtonsoft read them); single quotes and
// unquoted names are not. Plain C#, framework only. ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.LiveLane
{
    /// <summary>A live-lane refusal: the message is the fixed reason token (declaration-invalid, manifest-missing, ...).</summary>
    public sealed class LiveLaneRefusal : Exception
    {
        public LiveLaneRefusal(string reason) : base(reason) { }
    }

    /// <summary>A JSON object as ConvertFrom-Json -AsHashtable read it: ordinal keys in document order.</summary>
    public sealed class LiveLaneMap : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        public LiveLaneMap() { }

        public LiveLaneMap(params (string Key, object Value)[] pairs)
        {
            foreach (var (key, value) in pairs) this[key] = value;
        }

        /// <summary>$map[$key]: the value, or null when the key is absent.</summary>
        public object this[string key]
        {
            get => key != null && _values.TryGetValue(key, out var value) ? value : null;
            set
            {
                if (!_values.ContainsKey(key)) _keys.Add(key);
                _values[key] = value;
            }
        }

        public IReadOnlyList<string> Keys => _keys;

        public int Count => _keys.Count;

        /// <summary>$map.Contains($key): an exact key (a null value counts).</summary>
        public bool Contains(string key) => key != null && _values.ContainsKey(key);

        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            _keys.Remove(key);
            return true;
        }

        internal void AddNew(string key, object value)
        {
            if (_values.ContainsKey(key)) throw new LiveLaneRefusal("json-duplicate-key");
            _keys.Add(key);
            _values[key] = value;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var key in _keys) yield return new KeyValuePair<string, object>(key, _values[key]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The reader, the canonical writer and the value helpers.</summary>
    public static class LiveLaneData
    {
        /// <summary>
        /// ConvertFrom-Json -AsHashtable: null for an empty or blank text (ConvertFrom-Json wrote nothing), otherwise the
        /// value; any malformed text throws. maxDepth is -Depth (1024 when the PowerShell gave none).
        /// </summary>
        public static object Parse(string text, int maxDepth = 1024)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var options = new JsonDocumentOptions { MaxDepth = maxDepth, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var document = JsonDocument.Parse(text, options);
            return Read(document.RootElement);
        }

        private static object Read(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new LiveLaneMap();
                    foreach (var property in element.EnumerateObject()) map.AddNew(property.Name, Read(property.Value));
                    return map;
                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(Read).ToList();
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

        /// <summary>
        /// The canonical JSON (ConvertTo-TestSelectionCanonicalJson): null; a string as ConvertTo-Json -Compress writes it; a
        /// bool in lower case; an object or dictionary with its keys in ordinal order; any other enumerable as an array;
        /// another value type in the invariant culture, unquoted.
        /// </summary>
        public static string Canonical(object value)
        {
            var text = new StringBuilder();
            Write(text, value);
            return text.ToString();
        }

        /// <summary>Get-TestSelectionDigest: lower-case hex SHA-256 of the canonical JSON's UTF-8 bytes.</summary>
        public static string Digest(object value) => Sha256Text(Canonical(value));

        /// <summary>Lower-case hex SHA-256 of the text's UTF-8 bytes.</summary>
        public static string Sha256Text(string text) => Sha256(Encoding.UTF8.GetBytes(text ?? ""));

        /// <summary>Lower-case hex SHA-256 of the bytes.</summary>
        public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes ?? Array.Empty<byte>())).ToLowerInvariant();

        /// <summary>ConvertTo-TestSelectionLfText: CR LF and CR become LF. Null is empty.</summary>
        public static string LfText(string text) => text == null ? "" : text.Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>Get-TestSelectionSorted: the distinct non-null values (each as text) in ordinal order.</summary>
        public static string[] Sorted(IEnumerable<string> values)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<string>()) if (value != null) set.Add(value);
            return set.ToArray();
        }

        private static void Write(StringBuilder text, object value)
        {
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
                case LiveLaneMap map:
                    WriteObject(text, map.Keys, key => map[key]);
                    return;
                case IDictionary<string, object> generic:
                    WriteObject(text, generic.Keys, key => generic[key]);
                    return;
                case IDictionary<string, string> strings:
                    WriteObject(text, strings.Keys, key => strings[key]);
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
                case ValueType _:
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                default:
                    throw new ArgumentException("not a JSON value: " + value.GetType().Name);
            }
        }

        private static void WriteObject(StringBuilder text, IEnumerable<string> keys, Func<string, object> value)
        {
            text.Append('{');
            var first = true;
            foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!first) text.Append(',');
                first = false;
                WriteString(text, key);
                text.Append(':');
                Write(text, value(key));
            }
            text.Append('}');
        }

        /// <summary>A JSON string as Newtonsoft's default escaping writes it (ConvertTo-Json -Compress).</summary>
        private static void WriteString(StringBuilder text, string value)
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
                    case (char)0x0085: text.Append("\\u0085"); break;
                    case (char)0x2028: text.Append("\\u2028"); break;
                    case (char)0x2029: text.Append("\\u2029"); break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }

        // Typed reads of the hashtable model (-is [string], -is [long], -is [bool], -is [IDictionary], -is [IList]).

        public static bool IsString(object value, out string text)
        {
            text = value as string;
            return text != null;
        }

        public static bool IsLong(object value, out long number)
        {
            switch (value)
            {
                case long l: number = l; return true;
                case int i: number = i; return true;
                default: number = 0; return false;
            }
        }

        public static bool IsBool(object value, out bool flag)
        {
            if (value is bool b) { flag = b; return true; }
            flag = false;
            return false;
        }

        public static LiveLaneMap AsObject(object value) => value as LiveLaneMap;

        /// <summary>-is [Collections.IList]: an array (a string is not one).</summary>
        public static IList<object> AsList(object value) => value as IList<object>;

        /// <summary>PowerShell truth: null, false, 0, "" and an empty list are false.</summary>
        public static bool Truthy(object value)
        {
            switch (value)
            {
                case null: return false;
                case bool b: return b;
                case string s: return s.Length > 0;
                case long l: return l != 0;
                case int i: return i != 0;
                case double d: return d != 0;
                case BigInteger n: return !n.IsZero;
                case IList<object> list: return list.Count > 0 && (list.Count > 1 || Truthy(list[0]));
                default: return true;
            }
        }

        /// <summary>
        /// -ceq between two values of the hashtable model: null equals only null; otherwise the right side as the left
        /// side's text (a string compares ordinally, a number in the invariant culture, a bool as True or False).
        /// </summary>
        public static bool Same(object left, object right)
        {
            if (left == null || right == null) return left == null && right == null;
            return string.Equals(Text(left), Text(right), StringComparison.Ordinal);
        }

        private static string Text(object value)
        {
            switch (value)
            {
                case string s: return s;
                case bool b: return b ? "True" : "False";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }
    }
}
