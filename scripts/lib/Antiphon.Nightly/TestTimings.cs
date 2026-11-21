// W2-nightly S03: the C# twin of the event journal of scripts/lib/test-timings.ps1 (CARD-1313, schema
// antiphon-test-timing-v1; plan 2026-11-21-w2-nightly-port-slices.md): Test-TestTimingComponent, Write-TestTimingEvent
// and Read-TestTimingEvents. The ps1 stays for run-checkpoint.ps1 (W2-checkpoint) and select-test-rosters.ps1, which also
// read its report (Get-TestTimingReport, Add-TestTimingEstimates); nightly-tests-impl.ps1 writes its chunk events here.
// Both writers produce the same line for the same event (ConvertTo-Json -Compress -Depth 8: Newtonsoft's default
// escaping and number forms) under the same named mutex, so they share a journal; TestTimingsTwinTests holds the two
// to the same bytes and records. Persistence failure is not a test result: callers catch. Missing configuration writes
// nothing. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>Read-TestTimingEvents: the events in file order, first eventId wins; torn-tail warnings; the count of lines that are not JSON.</summary>
    public sealed record TestTimingEvents(IReadOnlyList<JsonNode> Events, IReadOnlyList<string> Warnings, int CorruptCount);

    /// <summary>The antiphon-test-timing-v1 event journal: runs/&lt;startedAt UTC date&gt;/&lt;runId&gt;/&lt;attempt&gt;/events.jsonl under a root.</summary>
    public static class TestTimings
    {
        /// <summary>The journal's schema version.</summary>
        public const string Schema = "antiphon-test-timing-v1";

        private const int Depth = 8;
        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        /// <summary>Test-TestTimingComponent: refuses (path-component-refused) a blank value, ".", "..", one containing "..", a separator, or a rooted path.</summary>
        public static void RequireComponent(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".." || value.Contains("..", StringComparison.Ordinal)
                || value.Contains('/') || value.Contains('\\') || Path.IsPathRooted(value))
                throw new InvalidOperationException("path-component-refused");
        }

        /// <summary>
        /// Write-TestTimingEvent: nothing without a root. Refuses a runId, attempt or eventId that is not one path component,
        /// then, holding the directory's named mutex, appends the event's line unless the journal already holds that eventId
        /// (the same line: nothing; another: event-conflict) or it is a set-completed whose set another producer completed.
        /// </summary>
        public static void WriteEvent(string root, IDictionary timingEvent)
        {
            if (string.IsNullOrEmpty(root)) return;
            if (timingEvent == null) throw new ArgumentNullException(nameof(timingEvent));
            RequireComponent(Text(Field(timingEvent, "runId")));
            RequireComponent(Text(Field(timingEvent, "attempt")));
            RequireComponent(Text(Field(timingEvent, "eventId")));
            var date = Instant(Field(timingEvent, "startedAt")).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dir = JoinPath(JoinPath(JoinPath(JoinPath(root, "runs"), date), Text(Field(timingEvent, "runId"))), Text(Field(timingEvent, "attempt")));
            Directory.CreateDirectory(dir);
            var path = JoinPath(dir, "events.jsonl");
            using var mutex = new Mutex(false, MutexName(dir));
            mutex.WaitOne();
            try
            {
                var existing = new List<JsonObject>();
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        // ConvertFrom-Json of a blank line is null: it can match nothing. Any other line that is not JSON throws.
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (JsonNode.Parse(line, null, ReadOptions) is JsonObject item) existing.Add(item);
                    }
                }
                var eventId = Text(Field(timingEvent, "eventId"));
                var payload = EventJson(timingEvent);
                var match = existing.FirstOrDefault(item => Same(item["eventId"], eventId));
                if (match != null)
                {
                    if (!string.Equals(NodeJson(match), payload, StringComparison.Ordinal)) throw new InvalidOperationException("event-conflict");
                    return;
                }
                var owned = existing.Any(item => Same(item["eventKind"], "set-completed") && Same(item["setId"], Field(timingEvent, "setId"))
                    && Same(item["setKind"], Field(timingEvent, "setKind")) && !Same(item["producer"], Field(timingEvent, "producer")));
                if (string.Equals(Field(timingEvent, "eventKind") as string, "set-completed", StringComparison.Ordinal) && owned) return;
                File.AppendAllText(path, payload + "\n");
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Read-TestTimingEvents: every events.jsonl under each existing root (recursive, sorted by full path as
        /// Sort-Object sorts), each complete line parsed; a line that is not JSON is counted, never returned; an event whose
        /// eventId (ignoring case) was already read is skipped; a last line without its newline is a torn-tail warning.
        /// </summary>
        public static TestTimingEvents ReadEvents(IEnumerable<string> roots)
        {
            var events = new List<JsonNode>();
            var warnings = new List<string>();
            var corrupt = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var root in roots ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                var files = Directory.EnumerateFiles(root, "events.jsonl", walk).Select(Path.GetFullPath).ToList();
                files.Sort(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true));
                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    if (text.Length == 0) continue;
                    var torn = !text.EndsWith('\n');
                    var lines = text.Split('\n');
                    var last = lines.Length - 1;
                    for (var i = 0; i < last; i++)
                    {
                        if (lines[i].Length == 0) continue;
                        JsonNode item;
                        if (string.IsNullOrWhiteSpace(lines[i])) item = null;
                        else
                        {
                            try { item = JsonNode.Parse(lines[i], null, ReadOptions); }
                            catch (JsonException) { corrupt++; continue; }
                        }
                        var id = item is JsonObject row ? Text(row["eventId"]) : "";
                        if (id.Length > 0 && !seen.Add(id)) continue;
                        events.Add(item);
                    }
                    if (torn && lines[last].Length > 0) warnings.Add("torn-tail");
                }
            }
            return new TestTimingEvents(events, warnings, corrupt);
        }

        /// <summary>
        /// The event's journal line: ConvertTo-Json -Compress -Depth 8 of it. Keys in the dictionary's order; strings with
        /// Newtonsoft's default escaping (control characters, quote, backslash, U+0085, U+2028, U+2029; lower-case hex);
        /// integers as digits; floating values in round-trip form with ".0" when integral; dates in ISO 8601 with the
        /// fraction trimmed; enums as numbers; nested dictionaries and lists as objects and arrays.
        /// </summary>
        public static string EventJson(IDictionary timingEvent)
        {
            var text = new StringBuilder();
            WriteValue(text, timingEvent, 0);
            return text.ToString();
        }

        /// <summary>A read event (or any node) in the same form as EventJson: what Write compares a replayed eventId with.</summary>
        public static string NodeJson(JsonNode node)
        {
            var text = new StringBuilder();
            WriteNode(text, node);
            return text.ToString();
        }

        /// <summary>The named mutex both writers hold for one journal directory: antiphon-timing- and 32 hex digits of SHA-256(UTF-8 path).</summary>
        public static string MutexName(string directory) =>
            "antiphon-timing-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory ?? ""))).Substring(0, 32);

        /// <summary>
        /// Join-Path's text for two parts: on Windows every '/' becomes '\'; one separator joins them unless the first
        /// already ends with one. The mutex name is a hash of this text, so it must match the ps1's.
        /// </summary>
        public static string JoinPath(string parent, string child)
        {
            var separator = Path.DirectorySeparatorChar;
            parent ??= "";
            child ??= "";
            if (separator == '\\')
            {
                parent = parent.Replace('/', '\\');
                child = child.Replace('/', '\\');
            }
            child = child.TrimStart(separator);
            if (parent.Length == 0) return child;
            return parent[parent.Length - 1] == separator ? parent + child : parent + separator + child;
        }

        // $Event.<name>: the dictionary's own lookup ([ordered]@{} ignores case).
        private static object Field(IDictionary map, string name) => map.Contains(name) ? map[name] : null;

        // [string]$value: empty for null, invariant text for numbers and dates, True/False for booleans.
        private static string Text(object value)
        {
            switch (value)
            {
                case null: return "";
                case string s: return s;
                case JsonValue v when v.TryGetValue(out JsonElement e):
                    return e.ValueKind switch
                    {
                        JsonValueKind.String => e.GetString() ?? "",
                        JsonValueKind.True => "True",
                        JsonValueKind.False => "False",
                        JsonValueKind.Null => "",
                        _ => e.GetRawText(),
                    };
                case JsonValue v when v.TryGetValue(out string s): return s;
                case JsonNode n: return n.ToJsonString();
                case bool b: return b ? "True" : "False";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? "";
            }
        }

        // -ceq between a read value and the event's: null only equals null; otherwise the text forms, ordinal.
        private static bool Same(JsonNode read, object value)
        {
            var readNull = read == null || (read is JsonValue v && v.TryGetValue(out JsonElement e) && e.ValueKind == JsonValueKind.Null);
            if (readNull || value == null) return readNull && value == null;
            return string.Equals(Text(read), Text(value), StringComparison.Ordinal);
        }

        // [DateTimeOffset]$Event.startedAt: a DateTimeOffset, a DateTime, or text parsed as the invariant culture does.
        private static DateTimeOffset Instant(object value)
        {
            switch (value)
            {
                case DateTimeOffset offset: return offset;
                case DateTime time: return new DateTimeOffset(time);
                case null: throw new InvalidCastException("Cannot convert null to type System.DateTimeOffset.");
                default: return DateTimeOffset.Parse(Text(value), CultureInfo.InvariantCulture);
            }
        }

        private static void WriteValue(StringBuilder text, object value, int depth)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    text.Append("null");
                    return;
                case string s:
                    WriteString(text, s);
                    return;
                case char c:
                    WriteString(text, c.ToString());
                    return;
                case bool b:
                    text.Append(b ? "true" : "false");
                    return;
                case JsonNode node:
                    WriteNode(text, node);
                    return;
                case JsonElement element:
                    WriteElement(text, element);
                    return;
                case Enum e:
                    text.Append(Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return;
                case sbyte or byte or short or ushort or int or uint or long or ulong or BigInteger:
                    text.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case double d:
                    WriteFloat(text, d, d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case float f:
                    WriteFloat(text, f, f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal m:
                    text.Append(DecimalPlace(m.ToString(CultureInfo.InvariantCulture)));
                    return;
                case DateTime time:
                    WriteString(text, time.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK", CultureInfo.InvariantCulture));
                    return;
                case DateTimeOffset offset:
                    WriteString(text, offset.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFzzz", CultureInfo.InvariantCulture));
                    return;
                case IDictionary map:
                    if (depth > Depth) { WriteString(text, map.ToString()); return; }
                    text.Append('{');
                    var first = true;
                    foreach (DictionaryEntry entry in map)
                    {
                        if (!first) text.Append(',');
                        first = false;
                        WriteString(text, Text(entry.Key));
                        text.Append(':');
                        WriteValue(text, entry.Value, depth + 1);
                    }
                    text.Append('}');
                    return;
                case IEnumerable items:
                    if (depth > Depth) { WriteString(text, items.ToString()); return; }
                    text.Append('[');
                    var head = true;
                    foreach (var item in items)
                    {
                        if (!head) text.Append(',');
                        head = false;
                        WriteValue(text, item, depth + 1);
                    }
                    text.Append(']');
                    return;
                default:
                    WriteString(text, Text(value));
                    return;
            }
        }

        private static void WriteNode(StringBuilder text, JsonNode node)
        {
            switch (node)
            {
                case null:
                    text.Append("null");
                    return;
                case JsonObject map:
                    text.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first) text.Append(',');
                        first = false;
                        WriteString(text, pair.Key);
                        text.Append(':');
                        WriteNode(text, pair.Value);
                    }
                    text.Append('}');
                    return;
                case JsonArray items:
                    text.Append('[');
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        WriteNode(text, items[i]);
                    }
                    text.Append(']');
                    return;
                case JsonValue value when value.TryGetValue(out JsonElement element):
                    WriteElement(text, element);
                    return;
                default:
                    WriteValue(text, node.GetValue<object>(), 0);
                    return;
            }
        }

        private static void WriteElement(StringBuilder text, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String: WriteString(text, element.GetString()); return;
                case JsonValueKind.Number: text.Append(element.GetRawText()); return;
                case JsonValueKind.True: text.Append("true"); return;
                case JsonValueKind.False: text.Append("false"); return;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    WriteNode(text, JsonNode.Parse(element.GetRawText()));
                    return;
                default: text.Append("null"); return;
            }
        }

        private static void WriteFloat(StringBuilder text, double value, string roundTrip)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { WriteString(text, roundTrip); return; }
            text.Append(DecimalPlace(roundTrip));
        }

        // Newtonsoft's EnsureDecimalPlace: an integral number keeps a ".0".
        private static string DecimalPlace(string number) =>
            number.IndexOf('.') >= 0 || number.IndexOf('E') >= 0 || number.IndexOf('e') >= 0 ? number : number + ".0";

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\u0085':
                    case '\u2028':
                    case '\u2029':
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        break;
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
