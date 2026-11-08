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

        /// <summary>ConvertTo-TestSelectionCanonicalJson: keys sorted ordinally, strings (keys too) as ConvertTo-Json
        /// -Compress writes them. Only the characters whose escaping is certain are accepted.</summary>
        public static string SelectionCanonical(object value)
        {
            var sb = new StringBuilder();
            WriteSelection(sb, value);
            return sb.ToString();
        }

        private static void WriteSelection(StringBuilder sb, object node)
        {
            switch (node)
            {
                case null: sb.Append("null"); return;
                case string s: AppendJsonString(sb, s); return;
                case bool flag: sb.Append(flag ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case PsObject obj:
                    var keys = obj.Pairs.Select(p => p.Key).ToArray();
                    Array.Sort(keys, StringComparer.Ordinal);
                    sb.Append('{');
                    for (int k = 0; k < keys.Length; k++)
                    {
                        if (k > 0) sb.Append(',');
                        AppendJsonString(sb, keys[k]);
                        sb.Append(':');
                        WriteSelection(sb, obj.GetExact(keys[k]));
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
                        WriteSelection(sb, item);
                    }
                    sb.Append(']');
                    return;
                default: throw new NotHandledException("canonical-type:" + node.GetType().Name);
            }
        }

        private static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Other control characters and the separators Newtonsoft also escapes: not reproduced here.
                        if (c < 0x20 || c == '\u007f' || c == '\u0085' || c == '\u2028' || c == '\u2029') throw new NotHandledException("json-string-escape");
                        sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Sha256Hex(byte[] bytes) =>
            System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        public static string Sha256Text(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));
    }
}
