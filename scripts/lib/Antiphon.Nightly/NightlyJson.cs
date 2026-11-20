// JSON files for the nightly scripts, read and written with System.Text.Json straight from and to UTF-8.
//
// ReadFile gives what ConvertFrom-Json gives for the file: objects are PSCustomObjects of note properties in
// document order, arrays object[], integers Int64 (BigInteger past Int64), other numbers Double, ISO 8601
// date-times DateTime (as ConvertFrom-Json's reader recognises them: 19-40 characters, a digit first, 'T' at index
// 10; "Z" is UTC, an offset is converted to local time, no zone is unspecified), other strings, booleans and nulls.
// It returns null wherever the file is not one strict JSON object (comments, trailing commas, trailing content, an
// empty or repeated property name, a missing or unreadable file) so the caller runs ConvertFrom-Json and raises its
// usual error. A caller may name property paths to omit ("suites[].exclusions": the exclusions of every suites
// element); those values are still parsed but never built, and the property is absent from the result. The file is
// read into one pooled buffer: the parse itself needs about the file's size, the objects built are the real cost.
//
// WriteFile writes the value as indented JSON through Utf8JsonWriter, streaming to the file. It is not
// ConvertTo-Json byte for byte (no reader of these files depends on that): non-ASCII text is written unescaped,
// numbers in their shortest round-trip form, line ends are the platform's. Dictionaries (string or stringified
// keys), lists and PSCustomObjects (their properties, in order) are containers; strings, chars, booleans, numbers,
// enums (as numbers, like ConvertTo-Json), DateTime, DateTimeOffset, Guid and Uri are values. It returns false (the
// file possibly part-written, which the caller deletes before failing) for anything else or for containers
// nested deeper than maxDepth.
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Antiphon.Nightly
{
    public static class NightlyJson
    {
        private sealed class GiveUp : Exception { }

        // ---- read ----

        /// <summary>The ConvertFrom-Json result for the file, or null when the caller must use ConvertFrom-Json.
        /// stripLeadingBom also drops a U+FEFF left after the UTF-8 byte-order mark (Read-NightlyHistoryText).</summary>
        public static PSObject ReadFile(string path, bool stripLeadingBom) => ReadFile(path, stripLeadingBom, null);

        /// <summary>ReadFile without the properties at the omitted paths (dotted names, "[]" for array elements).</summary>
        public static PSObject ReadFile(string path, bool stripLeadingBom, string[] omit)
        {
            var omitted = new HashSet<string>(omit ?? new string[0], StringComparer.Ordinal);
            byte[] buffer = null;
            try
            {
                int length;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                {
                    if (stream.Length > int.MaxValue / 2) return null;
                    buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                    length = 0;
                    int read;
                    while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0) length += read;
                    if (stream.ReadByte() >= 0) return null; // grew while being read
                }
                var start = 0;
                // [IO.File]::ReadAllText drops a UTF-8 byte-order mark; stripLeadingBom drops a second one.
                if (StartsWithBom(buffer, start, length)) start += 3;
                if (stripLeadingBom && StartsWithBom(buffer, start, length)) start += 3;
                // Only UTF-8 (or ASCII) text: a UTF-16 file goes to ConvertFrom-Json.
                if (length - start >= 2 && (buffer[start] == 0 || buffer[start + 1] == 0)) return null;

                var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(buffer, start, length - start),
                    new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 1024 });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
                var root = (PSObject)ReadValue(ref reader, "", omitted, true);
                if (reader.Read()) return null;
                return root;
            }
            catch (GiveUp) { return null; }
            catch (JsonException) { return null; }
            catch (InvalidOperationException) { return null; }
            // A missing or unreadable file: the caller's own read raises its usual error.
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            finally { if (buffer != null) ArrayPool<byte>.Shared.Return(buffer); }
        }

        /// <summary>
        /// Read-NightlyDiscoveryDocument. One JSON object. A UTF-8 BOM is dropped. A UTF-16 document with a
        /// BOM (FF FE or FE FF) is decoded and parsed the same way; FF FE 00 00 is UTF-32 and stays refused.
        /// UTF-16 without a BOM is refused with reason utf-16. Trailing commas are accepted. Comments are
        /// refused. DiscoveryWriter.WriteFile writes UTF-8 with no BOM, commas only between values, and no
        /// comments (Invoke-NightlyProduceDiscovery parses the --list-tests diagnostic, then that writer emits
        /// the discovery document). There is no ConvertFrom-Json fallback. Document is null and Reason is
        /// empty, utf-16, comment, not-object, trailing-content, invalid-json or unreadable.
        /// </summary>
        public static DiscoveryRead ReadDiscovery(string path)
        {
            byte[] buffer = null;
            try
            {
                int length;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                {
                    if (stream.Length > int.MaxValue / 2) return DiscoveryRead.Fail("unreadable");
                    buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                    length = 0;
                    int read;
                    while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0) length += read;
                    if (stream.ReadByte() >= 0) return DiscoveryRead.Fail("unreadable");
                }
                if (TryUtf16Bom(buffer, length, out var utf8, out var utf16Reason))
                {
                    if (utf8 == null) return DiscoveryRead.Fail(utf16Reason);
                    return ParseDiscoveryUtf8(utf8, 0, utf8.Length);
                }
                var start = 0;
                if (StartsWithBom(buffer, start, length)) start += 3;
                if (length - start >= 2 && (buffer[start] == 0 || buffer[start + 1] == 0))
                    return DiscoveryRead.Fail("utf-16");
                return ParseDiscoveryUtf8(buffer, start, length);
            }
            catch (IOException) { return DiscoveryRead.Fail("unreadable"); }
            catch (UnauthorizedAccessException) { return DiscoveryRead.Fail("unreadable"); }
            finally { if (buffer != null) ArrayPool<byte>.Shared.Return(buffer); }
        }

        // True when the buffer starts with a UTF-16 BOM. utf8 is the decoded document, or null when reason is set.
        private static bool TryUtf16Bom(byte[] buffer, int length, out byte[] utf8, out string reason)
        {
            utf8 = null;
            reason = null;
            if (length < 2) return false;
            bool le = buffer[0] == 0xFF && buffer[1] == 0xFE;
            bool be = buffer[0] == 0xFE && buffer[1] == 0xFF;
            if (!le && !be) return false;
            // FF FE 00 00 is a UTF-32 LE BOM, not a UTF-16 document.
            if (le && length >= 4 && buffer[2] == 0x00 && buffer[3] == 0x00)
            {
                reason = "utf-16";
                return true;
            }
            int payload = length - 2;
            if ((payload & 1) != 0)
            {
                reason = "invalid-json";
                return true;
            }
            if (payload == 0)
            {
                utf8 = Array.Empty<byte>();
                return true;
            }
            try
            {
                var text = new UnicodeEncoding(!le, false, true).GetString(buffer, 2, payload);
                utf8 = Encoding.UTF8.GetBytes(text);
                return true;
            }
            catch (DecoderFallbackException)
            {
                reason = "invalid-json";
                return true;
            }
        }

        private static DiscoveryRead ParseDiscoveryUtf8(byte[] buffer, int start, int end)
        {
            if (IsDiscoveryWhitespace(buffer, start, end)) return DiscoveryRead.Fail("empty");
            var parsed = TryParseDiscoveryObject(buffer, start, end - start, false, out var reason);
            if (parsed != null) return DiscoveryRead.Ok(parsed);
            if (reason != "comment")
            {
                var skipped = TryParseDiscoveryObject(buffer, start, end - start, true, out _);
                if (skipped != null) reason = "comment";
            }
            return DiscoveryRead.Fail(reason);
        }

        private static bool IsDiscoveryWhitespace(byte[] buffer, int start, int length)
        {
            for (var i = start; i < length; i++)
            {
                var c = buffer[i];
                if (c != (byte)' ' && c != (byte)'\t' && c != (byte)'\r' && c != (byte)'\n') return false;
            }
            return true;
        }

        // allowComments is only the probe that decides a comment refusal. Callers discard that object.
        private static PSObject TryParseDiscoveryObject(byte[] buffer, int start, int count, bool allowComments, out string reason)
        {
            reason = "invalid-json";
            try
            {
                var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(buffer, start, count),
                    new JsonReaderOptions
                    {
                        CommentHandling = allowComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
                        AllowTrailingCommas = true,
                        MaxDepth = 1024
                    });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    reason = "not-object";
                    return null;
                }
                var root = (PSObject)ReadValue(ref reader, "", new HashSet<string>(StringComparer.Ordinal), true);
                if (reader.Read())
                {
                    reason = "trailing-content";
                    return null;
                }
                reason = "";
                return root;
            }
            catch (GiveUp) { reason = "invalid-json"; return null; }
            catch (JsonException) { reason = "invalid-json"; return null; }
            catch (InvalidOperationException) { reason = "invalid-json"; return null; }
        }

        /// <summary>
        /// The timing sidecar read (QualificationTiming.ReadSidecar): text that is one strict JSON object as
        /// note-property objects, object[] arrays, Int64 (BigInteger past Int64) or Double numbers, strings kept as
        /// strings. Null for anything else (comments, trailing commas or content, a leading U+FEFF, an empty or
        /// case-insensitively repeated property name, a non-finite number, nesting deeper than maxDepth): the
        /// sidecar is then unreadable.
        /// </summary>
        public static PSObject ParseText(string text, int maxDepth) => ParseText(text, maxDepth, false);

        /// <summary>
        /// ParseText with dates: as ConvertFrom-Json reads the text (ISO date-times become DateTime, as ReadFile
        /// describes); null wherever ConvertFrom-Json could read it differently, for the caller to run it.
        /// </summary>
        public static PSObject ParseText(string text, int maxDepth, bool dates)
        {
            if (string.IsNullOrEmpty(text) || text[0] == (char)0xFEFF) return null;
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var reader = new Utf8JsonReader(bytes,
                    new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = maxDepth });
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
                var root = (PSObject)ReadValue(ref reader, "", new HashSet<string>(StringComparer.Ordinal), dates);
                if (reader.Read()) return null;
                return HasNonFinite(root) ? null : root;
            }
            catch (GiveUp) { return null; }
            catch (JsonException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
        }

        private static bool HasNonFinite(object value)
        {
            if (value is double d) return double.IsNaN(d) || double.IsInfinity(d);
            if (value is object[] items) { foreach (var item in items) if (HasNonFinite(item)) return true; return false; }
            if (value is PSObject ps) { foreach (var p in ps.Properties) if (HasNonFinite(p.Value)) return true; }
            return false;
        }

        private static bool StartsWithBom(byte[] b, int at, int length) =>
            length - at >= 3 && b[at] == 0xEF && b[at + 1] == 0xBB && b[at + 2] == 0xBF;

        private static object ReadValue(ref Utf8JsonReader reader, string at, HashSet<string> omitted, bool dates)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    {
                        var result = new PSObject();
                        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        while (true)
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.EndObject) return result;
                            var name = reader.GetString();
                            // ConvertFrom-Json refuses these.
                            if (string.IsNullOrEmpty(name) || !names.Add(name)) throw new GiveUp();
                            reader.Read();
                            var child = at.Length == 0 ? name : at + "." + name;
                            if (omitted.Count != 0 && omitted.Contains(child)) { reader.Skip(); continue; }
                            result.Properties.Add(new PSNoteProperty(name, ReadValue(ref reader, child, omitted, dates)));
                        }
                    }
                case JsonTokenType.StartArray:
                    {
                        var items = new List<object>();
                        var element = at + "[]";
                        while (true)
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.EndArray) return items.ToArray();
                            items.Add(ReadValue(ref reader, element, omitted, dates));
                        }
                    }
                case JsonTokenType.String:
                    {
                        var text = reader.GetString();
                        if (!dates) return text;
                        // ConvertFrom-Json's reader also reads Microsoft "\/Date(ticks)\/" strings as dates.
                        if (text.StartsWith("/Date(", StringComparison.Ordinal)) throw new GiveUp();
                        if (text.Length >= 19 && text.Length <= 40 && text[0] >= '0' && text[0] <= '9' && text[10] == 'T'
                            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
                            && IsIsoShape(text))
                            return date;
                        return text;
                    }
                case JsonTokenType.Number:
                    {
                        var raw = reader.ValueSpan;
                        if (raw.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0) return reader.GetDouble();
                        if (reader.TryGetInt64(out var whole)) return whole;
                        return BigInteger.Parse(System.Text.Encoding.ASCII.GetString(raw), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    }
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Null: return null;
                default: throw new GiveUp();
            }
        }

        // yyyy-MM-ddTHH:mm:ss, then optional .fraction, then optional Z or +HH:mm / -HH:mm.
        private static bool IsIsoShape(string s)
        {
            bool D(int i) => i < s.Length && s[i] >= '0' && s[i] <= '9';
            if (!(D(0) && D(1) && D(2) && D(3) && s[4] == '-' && D(5) && D(6) && s[7] == '-' && D(8) && D(9)
                && D(11) && D(12) && s[13] == ':' && D(14) && D(15) && s[16] == ':' && D(17) && D(18))) return false;
            var i = 19;
            if (i < s.Length && s[i] == '.') { i++; var digits = i; while (D(i)) i++; if (i == digits) return false; }
            if (i == s.Length) return true;
            if (s[i] == 'Z') return i + 1 == s.Length;
            if (s[i] != '+' && s[i] != '-') return false;
            return i + 6 == s.Length && D(i + 1) && D(i + 2) && s[i + 3] == ':' && D(i + 4) && D(i + 5);
        }

        // ---- write ----

        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = 1024,
        };

        /// <summary>Writes the file and returns true, or returns false (the file possibly part-written) for a value
        /// it cannot write. File errors are thrown. The caller fails either way: there is no other writer.</summary>
        public static bool WriteFile(string path, object value, int maxDepth)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536))
                {
                    using (var writer = new Utf8JsonWriter(stream, WriterOptions))
                    {
                        WriteValue(writer, value, 0, maxDepth);
                        writer.Flush();
                    }
                    var end = System.Text.Encoding.ASCII.GetBytes(Environment.NewLine);
                    stream.Write(end, 0, end.Length);
                }
                return true;
            }
            catch (GiveUp) { return false; }
        }

        private static void WriteValue(Utf8JsonWriter writer, object value, int depth, int maxDepth)
        {
            if (value == null || ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value)) { writer.WriteNullValue(); return; }
            var pso = value as PSObject;
            var obj = pso != null ? pso.BaseObject : value;
            switch (obj)
            {
                case null: writer.WriteNullValue(); return;
                case string s: writer.WriteStringValue(s); return;
                case bool b: writer.WriteBooleanValue(b); return;
                case char c: writer.WriteStringValue(c.ToString()); return;
                case int n: writer.WriteNumberValue(n); return;
                case long n: writer.WriteNumberValue(n); return;
                case double n: WriteDouble(writer, n); return;
                case DateTime d: writer.WriteStringValue(d); return;
                case DateTimeOffset d: writer.WriteStringValue(d); return;
                case short n: writer.WriteNumberValue(n); return;
                case byte n: writer.WriteNumberValue(n); return;
                case sbyte n: writer.WriteNumberValue(n); return;
                case ushort n: writer.WriteNumberValue(n); return;
                case uint n: writer.WriteNumberValue(n); return;
                case ulong n: writer.WriteNumberValue(n); return;
                case float n: WriteDouble(writer, double.Parse(n.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)); return;
                case decimal n: writer.WriteNumberValue(n); return;
                case BigInteger n: writer.WriteRawValue(n.ToString(CultureInfo.InvariantCulture), true); return;
                case Guid g: writer.WriteStringValue(g); return;
                case Uri u: writer.WriteStringValue(u.OriginalString); return;
                case Enum e: writer.WriteRawValue(Convert.ToString(Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture), true); return;
            }
            if (obj == DBNull.Value || obj.GetType().FullName == "System.Management.Automation.Language.NullString") { writer.WriteNullValue(); return; }
            if (depth >= maxDepth + 1) throw new GiveUp();
            if (obj is IDictionary dictionary)
            {
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    writer.WritePropertyName(entry.Key as string ?? Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    WriteValue(writer, entry.Value, depth + 1, maxDepth);
                }
                writer.WriteEndObject();
                return;
            }
            if (obj is IEnumerable items)
            {
                writer.WriteStartArray();
                foreach (var item in items) WriteValue(writer, item, depth + 1, maxDepth);
                writer.WriteEndArray();
                return;
            }
            if (obj is PSCustomObject && pso != null)
            {
                writer.WriteStartObject();
                foreach (var property in pso.Properties)
                {
                    if (!property.IsGettable) continue;
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value, depth + 1, maxDepth);
                }
                writer.WriteEndObject();
                return;
            }
            // A core record writes itself.
            if (obj is IJsonWritable record) { record.WriteJson(writer); return; }
            // Arbitrary .NET objects: ConvertTo-Json's reflection walk.
            throw new GiveUp();
        }

        // ConvertTo-Json writes non-finite doubles as strings ("NaN", "Infinity"), and whole ones with ".0" so they
        // read back as doubles.
        private static void WriteDouble(Utf8JsonWriter writer, double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) { writer.WriteStringValue(n.ToString(CultureInfo.InvariantCulture)); return; }
            var text = n.ToString("R", CultureInfo.InvariantCulture);
            if (text.IndexOfAny(new[] { '.', 'E', 'e' }) < 0) text += ".0";
            writer.WriteRawValue(text, true);
        }
    }

    /// <summary>ReadDiscovery's document, or a refusal Reason when Document is null.</summary>
    public sealed class DiscoveryRead
    {
        public PSObject Document { get; private set; }
        public string Reason { get; private set; }

        internal static DiscoveryRead Ok(PSObject document) => new DiscoveryRead { Document = document, Reason = "" };
        internal static DiscoveryRead Fail(string reason) => new DiscoveryRead { Reason = string.IsNullOrEmpty(reason) ? "invalid-json" : reason };
    }
}
