// The discovery document writer (Write-NightlyDiscoveryDocument, nightly-coverage.ps1), compiled: the text of
// "$Document | ConvertTo-Json -Depth 12" for the document ConvertTo-NightlyDiscoveryDocument builds, written to the
// atomic writer's temporary file as [IO.File]::WriteAllText with UTF-8 without a byte-order mark wrote it. A full
// suite's document (19,012 nodes, 6.7 MB) took ConvertTo-Json about 1.5 s, and the text then crossed into .NET as a
// method argument (an AMSI scan); here it never leaves .NET.
//
// PowerShell 7's ConvertTo-Json is Newtonsoft.Json, Formatting.Indented, StringEscapeHandling.Default: two-space
// indentation, "name": value, Environment.NewLine between lines, [] and {} when empty; in strings \" \\ \b \f \n
// \r \t, a lower-case hex escape for the other C0 controls and for U+0085, U+2028 and U+2029, all else literal.
// Only the values that document holds are written: dictionaries with string keys, arrays and lists, strings,
// booleans and nulls, a few levels deep. Anything else (a PSObject, a number, a deeper value) is not claimed, and
// Write-NightlyAtomicJson refuses the document without writing anything (no ConvertTo-Json fallback).
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace Antiphon.Nightly
{
    public static class DiscoveryWriter
    {
        // The document is three containers deep (document, nodes, a node's categories); -Depth 12 never cuts one.
        private const int MaxDepth = 8;

        /// <summary>
        /// ConvertTo-Json -Depth 12 of <paramref name="document"/> (an IDictionary), or null when it holds a value
        /// this writer does not claim.
        /// </summary>
        public static string Json(object document)
        {
            if (!(document is IDictionary)) return null;
            var sb = new StringBuilder(4096);
            return Append(sb, document, 0) ? sb.ToString() : null;
        }

        /// <summary>
        /// <see cref="Json"/> of the document held for <see cref="WriteFile"/>, so the text is not passed back into
        /// .NET as a string argument; null when the document holds a value this writer does not claim.
        /// </summary>
        public static DiscoveryText Prepare(object document)
        {
            var json = Json(document);
            return json == null ? null : new DiscoveryText(json);
        }

        /// <summary>[IO.File]::WriteAllText($path, $json, UTF-8 without a byte-order mark).</summary>
        public static void WriteFile(string path, DiscoveryText text) =>
            File.WriteAllText(path, text.Json, new UTF8Encoding(false));

        private static bool Append(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("null"); return true; }
            if (value is PSObject) return false;
            if (value is string text) { AppendString(sb, text); return true; }
            if (value is bool flag) { sb.Append(flag ? "true" : "false"); return true; }
            if (depth >= MaxDepth) return false;
            if (value is IDictionary dictionary)
            {
                if (dictionary.Count == 0) { sb.Append("{}"); return true; }
                sb.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!(entry.Key is string key)) return false;
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, depth + 1);
                    AppendString(sb, key);
                    sb.Append(": ");
                    if (!Append(sb, entry.Value, depth + 1)) return false;
                }
                NewLine(sb, depth);
                sb.Append('}');
                return true;
            }
            if (value is IList list)
            {
                if (list.Count == 0) { sb.Append("[]"); return true; }
                sb.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, depth + 1);
                    if (!Append(sb, list[i], depth + 1)) return false;
                }
                NewLine(sb, depth);
                sb.Append(']');
                return true;
            }
            return false;
        }

        private static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append(Environment.NewLine);
            sb.Append(' ', depth * 2);
        }

        private static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            var start = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                string escaped;
                switch (ch)
                {
                    case '"': escaped = "\\\""; break;
                    case '\\': escaped = "\\\\"; break;
                    case '\b': escaped = "\\b"; break;
                    case '\f': escaped = "\\f"; break;
                    case '\n': escaped = "\\n"; break;
                    case '\r': escaped = "\\r"; break;
                    case '\t': escaped = "\\t"; break;
                    default:
                        if (ch >= ' ' && ch != (char)0x85 && ch != (char)0x2028 && ch != (char)0x2029) continue;
                        escaped = "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture);
                        break;
                }
                sb.Append(value, start, i - start).Append(escaped);
                start = i + 1;
            }
            sb.Append(value, start, value.Length - start);
            sb.Append('"');
        }
    }

    /// <summary>A prepared discovery document text (<see cref="DiscoveryWriter.Prepare"/>).</summary>
    public sealed class DiscoveryText
    {
        internal DiscoveryText(string json) { Json = json; }

        internal string Json { get; }

        /// <summary>The text's length in UTF-16 code units.</summary>
        public int Length => Json.Length;
    }
}
