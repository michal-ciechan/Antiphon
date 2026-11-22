// JSON as PowerShell 7's ConvertFrom-Json sees it, restricted to the shapes this CLI can reproduce exactly.
// Objects keep their key order; numbers are long (no fractions or exponents); strings stay strings. Anything
// PowerShell would read differently (a date-like string it turns into a DateTime, an integer it widens to
// BigInteger, keys that differ only in case, an empty key) is NotHandled, so the PowerShell path decides.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Scripts
{
    /// <summary>The command cannot answer this exactly as PowerShell would: the caller keeps its PowerShell path.</summary>
    public sealed class NotHandledException : Exception
    {
        public NotHandledException(string reason) : base(reason) { }
    }

    /// <summary>A JSON object: ordered keys, unique ignoring case.</summary>
    public sealed class PsObject
    {
        public readonly List<KeyValuePair<string, object>> Pairs = new List<KeyValuePair<string, object>>();

        /// <summary>$object.Name on a [pscustomobject]: the key ignoring case, null when absent.</summary>
        public object Get(string name)
        {
            foreach (var pair in Pairs) if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        public bool Has(string name) => Pairs.Any(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>$hashtable.Name / $hashtable[name] on -AsHashtable output: the exact key; a key that differs
        /// only in case is NotHandled because hashtable case sensitivity is not part of the contract here.</summary>
        public object GetExact(string name)
        {
            foreach (var pair in Pairs)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal)) return pair.Value;
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) throw new NotHandledException("key-case:" + name);
            }
            return null;
        }

        public bool HasExact(string name)
        {
            GetExact(name);
            return Pairs.Any(p => string.Equals(p.Key, name, StringComparison.Ordinal));
        }

        public IEnumerable<string> Keys => Pairs.Select(p => p.Key);
    }

    public static class PsJson
    {
        // Newtonsoft (behind ConvertFrom-Json) turns ISO-8601-looking and /Date(...)/ strings into DateTime.
        private static readonly Regex DateLike = new Regex(@"^\s*\d{4}-\d{2}-\d{2}|/Date\(", RegexOptions.CultureInvariant);

        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new NotHandledException("json-empty");
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 256 });
            }
            catch (JsonException) { throw new NotHandledException("json-invalid"); }
            using (document) return Convert(document.RootElement);
        }

        private static object Convert(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new PsObject();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Length == 0) throw new NotHandledException("json-empty-key");
                        if (!seen.Add(property.Name)) throw new NotHandledException("json-duplicate-key");
                        obj.Pairs.Add(new KeyValuePair<string, object>(property.Name, Convert(property.Value)));
                    }
                    return obj;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray()) list.Add(Convert(item));
                    return list;
                case JsonValueKind.String:
                    string s = element.GetString();
                    if (DateLike.IsMatch(s)) throw new NotHandledException("json-date-like");
                    return s;
                case JsonValueKind.Number:
                    string raw = element.GetRawText();
                    if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) throw new NotHandledException("json-fraction");
                    if (!element.TryGetInt64(out long value)) throw new NotHandledException("json-big-integer");
                    return value;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: return null;
                default: throw new NotHandledException("json-kind");
            }
        }

        /// <summary>ConvertTo-NightlyCanonicalJson (NightlyDocuments.CanonicalJson): keys sorted by the current culture
        /// ignoring case (stable), only \ " CR LF TAB escaped, keys written raw.</summary>
        public static string NightlyCanonical(object value)
        {
            var sb = new StringBuilder();
            WriteNightly(sb, value);
            return sb.ToString();
        }

        private static void WriteNightly(StringBuilder sb, object node)
        {
            switch (node)
            {
                case null: sb.Append("null"); return;
                case string s:
                    sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")).Append('"');
                    return;
                case bool flag: sb.Append(flag ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case PsObject obj:
                    var culture = CultureInfo.CurrentCulture;
                    var sorted = obj.Pairs.Where(p => p.Key.Length > 0)
                        .OrderBy(p => p.Key, Comparer<string>.Create((x, y) => string.Compare(x, y, culture, CompareOptions.IgnoreCase))).ToList();
                    sb.Append('{');
                    for (int k = 0; k < sorted.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append('"').Append(sorted[k].Key).Append("\":");
                        WriteNightly(sb, sorted[k].Value);
                    }
                    sb.Append('}');
                    return;
                case System.Collections.IEnumerable items:
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in items)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteNightly(sb, item);
                    }
                    sb.Append(']');
                    return;
                default: throw new NotHandledException("canonical-type:" + node.GetType().Name);
            }
        }

        /// <summary>
        /// A PsObject tree as the core's plain values (TestSelectionJson digests them): an object is an OrderedDictionary
        /// with ordinal keys in order, a list an object[], anything else itself.
        /// </summary>
        public static object ToPlain(object value)
        {
            switch (value)
            {
                case PsObject obj:
                    var map = new System.Collections.Specialized.OrderedDictionary(StringComparer.Ordinal);
                    foreach (var pair in obj.Pairs) map[pair.Key] = ToPlain(pair.Value);
                    return map;
                case string _:
                    return value;
                case System.Collections.IEnumerable items:
                    return items.Cast<object>().Select(ToPlain).ToArray();
                default:
                    return value;
            }
        }

        /// <summary>
        /// The core's plain values (a selection or a contract) as this command's PsObject tree: a dictionary is a PsObject
        /// in key order, any other enumerable a list; keys that differ only in case are not handled.
        /// </summary>
        public static object FromPlain(object value)
        {
            switch (value)
            {
                case System.Collections.IDictionary dictionary:
                    var obj = new PsObject();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.Collections.DictionaryEntry entry in dictionary)
                    {
                        if (!(entry.Key is string key) || key.Length == 0 || !seen.Add(key)) throw new NotHandledException("plain-key");
                        obj.Pairs.Add(new KeyValuePair<string, object>(key, FromPlain(entry.Value)));
                    }
                    return obj;
                case string _:
                    return value;
                case System.Collections.IEnumerable items:
                    return items.Cast<object>().Select(FromPlain).ToList();
                default:
                    return value;
            }
        }

        public static string Sha256Hex(byte[] bytes) =>
            System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        public static string Sha256Text(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));
    }
}
