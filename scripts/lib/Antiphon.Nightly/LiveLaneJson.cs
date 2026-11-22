// The JSON of the live-lane manifest files (CARD-1545 S1a), compiled in W2-nightly S23-S24: the text
// "ConvertTo-Json -Depth 8" wrote in PowerShell 7 and the readings the PowerShell made of ConvertFrom-Json values.
//
// Written text: two-space indents, "name": value, the platform's line ends, Newtonsoft's default escaping, [] and {}
// for an empty list or object, no byte-order mark. Set-Content added a final line end; [IO.File]::WriteAllText did
// not. Values: null, text, booleans, integers, LiveLaneJsonObject (a [pscustomobject] or [ordered]@{} in its order),
// any other list, and a JsonElement written as ConvertFrom-Json | ConvertTo-Json wrote it back (dates aside: the
// element keeps its text, where ConvertFrom-Json had turned a date-like string into a date first; a number other
// than an integer refuses). A container nested 8 deep refuses rather than being written as its type name.
//
// Read values: Get-LiveLaneProp is the first property whose name matches ignoring case; [string] of a value is its
// text (a list's items joined by spaces, True/False, a number's text, null and an empty object blank); [bool] is
// PowerShell's truth of the value; @(ConvertTo-LiveLaneList ...) is none for a missing or null value, a list's
// items, else the value itself. PowerShell's -eq and -ne on text compare with the invariant culture, ignoring case.
// ASCII-only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Antiphon.Nightly
{
    /// <summary>An ordered JSON object: a [pscustomobject] or [ordered]@{} as ConvertTo-Json writes it.</summary>
    public sealed class LiveLaneJsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> _fields = new List<KeyValuePair<string, object>>();

        /// <summary>Adds a field after the others.</summary>
        public void Add(string name, object value) => _fields.Add(new KeyValuePair<string, object>(name, value));

        public int Count => _fields.Count;

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _fields.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static class LiveLaneJson
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // ---- write --------------------------------------------------------------------------------------------

        /// <summary>ConvertTo-Json -Depth 8 of the value: its indented text, without a final line end.</summary>
        internal static string Text(object value)
        {
            var sb = new StringBuilder(4096);
            Write(sb, value, 0);
            return sb.ToString();
        }

        /// <summary>
        /// The value's text to path, UTF-8 without a byte-order mark: with a final line end ("| Set-Content -Encoding
        /// utf8") or without one ([IO.File]::WriteAllText).
        /// </summary>
        internal static void WriteFile(string path, object value, bool finalNewLine)
        {
            var text = Text(value);
            File.WriteAllText(path, finalNewLine ? text + Environment.NewLine : text, Utf8NoBom);
        }

        internal static void Write(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case JsonElement element: WriteElement(sb, element, depth); return;
                case LiveLaneJsonObject obj:
                    if (depth >= 8) throw new InvalidOperationException("live-lane-json-depth");
                    if (obj.Count == 0) { sb.Append("{}"); return; }
                    sb.Append('{');
                    var first = true;
                    foreach (var field in obj)
                    {
                        Name(sb, depth + 1, field.Key, first);
                        first = false;
                        Write(sb, field.Value, depth + 1);
                    }
                    NewLine(sb, depth);
                    sb.Append('}');
                    return;
                case IEnumerable list:
                    if (depth >= 8) throw new InvalidOperationException("live-lane-json-depth");
                    var any = false;
                    foreach (var item in list)
                    {
                        sb.Append(any ? "," : "[");
                        any = true;
                        NewLine(sb, depth + 1);
                        Write(sb, item, depth + 1);
                    }
                    if (!any) { sb.Append("[]"); return; }
                    NewLine(sb, depth);
                    sb.Append(']');
                    return;
                default:
                    throw new InvalidOperationException("live-lane-json-value " + value.GetType().FullName);
            }
        }

        // A value ConvertFrom-Json read, as ConvertTo-Json -Depth 8 wrote it back (dates aside).
        private static void WriteElement(StringBuilder sb, JsonElement value, int depth)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String: WriteString(sb, value.GetString()); return;
                case JsonValueKind.True: sb.Append("true"); return;
                case JsonValueKind.False: sb.Append("false"); return;
                case JsonValueKind.Null: sb.Append("null"); return;
                case JsonValueKind.Number:
                    if (!IsInteger(value.GetRawText()) || !value.TryGetInt64(out var number)) throw new InvalidOperationException("live-lanes-number " + value.GetRawText());
                    sb.Append(number.ToString(CultureInfo.InvariantCulture));
                    return;
            }
            if (depth >= 8) throw new InvalidOperationException("live-lanes-depth");
            if (value.ValueKind == JsonValueKind.Array)
            {
                var any = false;
                foreach (var item in value.EnumerateArray())
                {
                    sb.Append(any ? "," : "[");
                    any = true;
                    NewLine(sb, depth + 1);
                    WriteElement(sb, item, depth + 1);
                }
                if (!any) { sb.Append("[]"); return; }
                NewLine(sb, depth);
                sb.Append(']');
                return;
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = true;
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidOperationException("live-lanes-duplicate-key " + property.Name);
                if (first) sb.Append('{');
                Name(sb, depth + 1, property.Name, first);
                first = false;
                WriteElement(sb, property.Value, depth + 1);
            }
            if (first) { sb.Append("{}"); return; }
            NewLine(sb, depth);
            sb.Append('}');
        }

        private static bool IsInteger(string raw)
        {
            if (raw.Length == 0) return false;
            for (var i = raw[0] == '-' ? 1 : 0; i < raw.Length; i++)
                if (raw[i] < '0' || raw[i] > '9') return false;
            return raw != "-";
        }

        internal static void Name(StringBuilder sb, int depth, string name, bool first)
        {
            if (!first) sb.Append(',');
            NewLine(sb, depth);
            WriteString(sb, name);
            sb.Append(": ");
        }

        internal static void NewLine(StringBuilder sb, int depth) => sb.Append(Environment.NewLine).Append(' ', depth * 2);

        // Newtonsoft's default escaping: \" \\ \b \f \n \r \t, lower-case \u00XX for the other C0 controls and for
        // U+0085, U+2028 and U+2029, everything else literal.
        internal static void WriteString(StringBuilder sb, string value)
        {
            value ??= "";
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

        // ---- read ---------------------------------------------------------------------------------------------

        /// <summary>[IO.File]::ReadAllText($path) | ConvertFrom-Json, as a document (comments and trailing commas read).</summary>
        internal static JsonDocument ReadFile(string path) => Parse(File.ReadAllText(path));

        internal static JsonDocument Parse(string text) => JsonDocument.Parse(text, ReadOptions);

        /// <summary>Get-LiveLaneProp: the first property whose name matches, ignoring case (ordinal).</summary>
        internal static bool TryProp(JsonElement row, string name, out JsonElement value)
        {
            if (row.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in row.EnumerateObject())
                {
                    if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>The named property, or null when it is missing or JSON null.</summary>
        internal static JsonElement? Prop(JsonElement? row, string name)
        {
            if (row == null || !TryProp(row.Value, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            return value;
        }

        /// <summary>[string](Get-LiveLaneProp $row name).</summary>
        internal static string Field(JsonElement? row, string name) => row != null && TryProp(row.Value, name, out var value) ? Str(value) : "";

        /// <summary>[string] of a ConvertFrom-Json value: a list's items joined by spaces, an empty object blank.</summary>
        internal static string Str(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.Array:
                    var parts = new List<string>();
                    foreach (var item in value.EnumerateArray()) parts.Add(Str(item));
                    return string.Join(" ", parts);
                case JsonValueKind.Object:
                    foreach (var _ in value.EnumerateObject()) return value.GetRawText();
                    return "";
                default: return "";
            }
        }

        /// <summary>[bool] of a ConvertFrom-Json value (LanguagePrimitives.IsTrue).</summary>
        internal static bool IsTrue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null: case JsonValueKind.Undefined: return false;
                case JsonValueKind.String: return value.GetString().Length != 0;
                case JsonValueKind.Number: return value.GetDouble() != 0;
                case JsonValueKind.Array:
                    var count = value.GetArrayLength();
                    if (count == 0) return false;
                    if (count > 1) return true;
                    var only = value[0];
                    if (only.ValueKind == JsonValueKind.Array) return only.GetArrayLength() > 0;
                    return IsTrue(only);
                default: return true;
            }
        }

        /// <summary>@(ConvertTo-LiveLaneList (Get-LiveLaneProp $doc name)): none, a list's items, else the value.</summary>
        internal static List<JsonElement> Rows(JsonElement? document, string name)
        {
            var rows = new List<JsonElement>();
            var value = Prop(document, name);
            if (value == null) return rows;
            if (value.Value.ValueKind == JsonValueKind.Array) rows.AddRange(value.Value.EnumerateArray());
            else rows.Add(value.Value);
            return rows;
        }

        /// <summary>@(ConvertTo-LiveLaneList (Get-LiveLaneProp $row name) | ForEach-Object { [string]$_ }).</summary>
        internal static string[] Strings(JsonElement? row, string name)
        {
            var rows = Rows(row, name);
            var values = new string[rows.Count];
            for (var i = 0; i < rows.Count; i++) values[i] = Str(rows[i]);
            return values;
        }

        /// <summary>PowerShell's -eq of two strings: the invariant culture, ignoring case.</summary>
        internal static bool Same(string a, string b) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(a ?? "", b ?? "", CompareOptions.IgnoreCase) == 0;
    }
}
