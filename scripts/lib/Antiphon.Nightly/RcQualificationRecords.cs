// CARD-1500, CARD-1667 A-1: the read-only records of an rc qualification. It replaced scripts/lib/rc-qualification-status.ps1
// (Get-RcQualificationPathStamp, Open-RcQualificationSharedRead, Read-RcQualificationRecord, ConvertTo-RcQualificationTime,
// Select-RcQualificationFields, Get-RcQualificationOwnerObservation) and Get-QualificationProcessObservation of
// scripts/lib/qualification-supervisor.ps1, both deleted 2026-11-23 (unit 7 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The status (RcQualificationStatus), the supervisor set
// (QualificationSupervisor, QualificationStop) and the status snapshot read every shared record through Read.
//
// One record is one JSON object of at most 1 MiB. It is opened with FileShare.ReadWrite | FileShare.Delete, so the writer's
// File.Replace of last-run.json (nightly-run) is never refused while it is read (Review 230d1f5d A-1). A path, or any of its
// ancestors, that is a reparse point, and a path that is not a regular file, is unsupported-path. The file identity (Windows:
// the creation time; Linux: device and inode), length and write time are read before and after the bytes: a change is
// "changed". Keys are matched exactly (one canonical spelling); a key repeated in one object is malformed. Process
// observation goes through the host's INightlyProcessTable. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>One record read: its literal path, the outcome (ok, missing, malformed, unreadable, changed, unsupported-path) and the object when ok.</summary>
    public sealed class RcQualificationRead
    {
        /// <summary>The path read.</summary>
        public string Path { get; init; }

        /// <summary>ok, missing, malformed, unreadable (a too-large record included), changed, unsupported-path, or not-requested.</summary>
        public string Status { get; init; }

        /// <summary>Null when ok; not-found, invalid-json-object, read-error, too-large, record-changed or unsupported-path otherwise.</summary>
        public string Reason { get; init; }

        /// <summary>The JSON object when Status is ok; null otherwise.</summary>
        public JsonElement? Record { get; init; }

        /// <summary>The read is ok.</summary>
        public bool Ok => Status == "ok";

        /// <summary>The outcome without the record: { path, status, reason }. Raw records never leave the readers.</summary>
        public JsonObject Outcome() => new JsonObject { ["path"] = Path, ["status"] = Status, ["reason"] = Reason };
    }

    /// <summary>An owner observation: alive, absent, reused or unknown, and why.</summary>
    public sealed record RcQualificationObservation(string Status, string Reason)
    {
        /// <summary>{ status, reason }.</summary>
        public JsonObject Json() => new JsonObject { ["status"] = Status, ["reason"] = Reason };
    }

    /// <summary>The bounded shared read of one record and the typed reads of its fields.</summary>
    public static class RcQualificationRecords
    {
        /// <summary>The largest record read (1 MiB).</summary>
        public const int MaxBytes = 1048576;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

        private sealed class UnsupportedPathException : IOException
        {
            public UnsupportedPathException() : base("unsupported-path") { }
        }

        /// <summary>
        /// Open-RcQualificationSharedRead: the file for reading, sharing ReadWrite and Delete, so the writer's handle and its
        /// File.Replace both stay possible while it is open.
        /// </summary>
        public static FileSystemStream OpenSharedRead(IFileSystem files, string path) =>
            files.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        /// <summary>
        /// Get-RcQualificationPathStamp: "identity:length:writeTicks" of a regular file. Throws IOException("unsupported-path")
        /// when the path or an ancestor is a reparse point, or the path is not a regular file; FileNotFoundException when it
        /// is missing.
        /// </summary>
        public static string PathStamp(IFileSystem files, string path)
        {
            var full = files.Path.GetFullPath(path);
            for (var cursor = full; !string.IsNullOrEmpty(cursor); cursor = files.Path.GetDirectoryName(cursor))
            {
                FileAttributes attributes;
                try
                {
                    attributes = files.File.GetAttributes(cursor);
                }
                catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
                {
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new UnsupportedPathException();
            }
            if (files.Directory.Exists(full)) throw new UnsupportedPathException();
            if (!files.File.Exists(full)) throw new FileNotFoundException("not found", full);
            var info = files.FileInfo.New(full);
            string identity;
            if (OperatingSystem.IsLinux() && files is FileSystem)
            {
                // The real file's device and inode (the reparse walk above already refused a link).
                if (Statx(-100, full, 0x100, 0x7ff, out var stat) != 0) throw new IOException("stat failed: " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture));
                if ((stat.Mode & 0xf000) != 0x8000) throw new UnsupportedPathException();
                identity = (((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor).ToString(CultureInfo.InvariantCulture) + ":" + stat.Inode.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                identity = info.CreationTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            return identity + ":" + info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read-RcQualificationRecord: the path's record, bounded and shared, with its stamp compared before and after the
        /// read. afterRead (a test seam) runs between the read and the second stamp. Never throws for the file's state.
        /// </summary>
        public static RcQualificationRead Read(IFileSystem files, string path, Action<string> afterRead = null)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            Stream stream = null;
            try
            {
                var before = PathStamp(files, path);
                stream = OpenSharedRead(files, path);
                if (stream.Length > MaxBytes) return Outcome(path, "unreadable", "too-large");
                var buffer = new byte[MaxBytes + 1];
                var count = 0;
                while (count < buffer.Length)
                {
                    var got = stream.Read(buffer, count, buffer.Length - count);
                    if (got == 0) break;
                    count += got;
                }
                if (count > MaxBytes) return Outcome(path, "unreadable", "too-large");
                afterRead?.Invoke(path);
                string after;
                try { after = PathStamp(files, path); }
                catch (Exception) { after = null; }
                if (!string.Equals(before, after, StringComparison.Ordinal)) return Outcome(path, "changed", "record-changed");
                JsonElement record;
                try
                {
                    record = ParseObject(StrictUtf8.GetString(buffer, 0, count));
                }
                catch (Exception e) when (e is JsonException || e is DecoderFallbackException || e is InvalidDataException)
                {
                    return Outcome(path, "malformed", "invalid-json-object");
                }
                return new RcQualificationRead { Path = path, Status = "ok", Reason = null, Record = record };
            }
            catch (UnsupportedPathException)
            {
                return Outcome(path, "unsupported-path", "unsupported-path");
            }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
            {
                return Outcome(path, "missing", "not-found");
            }
            catch (Exception)
            {
                return Outcome(path, "unreadable", "read-error");
            }
            finally
            {
                stream?.Dispose();
            }
        }

        /// <summary>The outcome of a path that was not asked for (an omitted journal root): no path, not-requested.</summary>
        public static RcQualificationRead NotRequested() => new RcQualificationRead { Path = null, Status = "not-requested", Reason = null };

        private static RcQualificationRead Outcome(string path, string status, string reason) => new RcQualificationRead { Path = path, Status = status, Reason = reason };

        /// <summary>
        /// The JSON object in the text (a leading byte-order mark dropped): InvalidDataException when it is not an object or a
        /// key repeats within one object, JsonException when it does not parse. The element does not need its document.
        /// </summary>
        public static JsonElement ParseObject(string text)
        {
            var raw = (text ?? "").TrimStart((char)0xFEFF);
            if (!raw.TrimStart().StartsWith("{", StringComparison.Ordinal)) throw new InvalidDataException("object-required");
            using var document = JsonDocument.Parse(raw, ParseOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("object-required");
            NoRepeatedKeys(document.RootElement);
            return document.RootElement.Clone();
        }

        private static void NoRepeatedKeys(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("repeated-key");
                    NoRepeatedKeys(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) NoRepeatedKeys(item);
            }
        }

        /// <summary>The member of an object by its exact name; null for a missing member, a non-object or no record.</summary>
        public static JsonElement? Member(JsonElement? record, string name)
        {
            if (record == null || record.Value.ValueKind != JsonValueKind.Object) return null;
            return record.Value.TryGetProperty(name, out var value) ? value : (JsonElement?)null;
        }

        /// <summary>The value is present and not JSON null.</summary>
        public static bool Present(JsonElement? value) => value != null && value.Value.ValueKind != JsonValueKind.Null && value.Value.ValueKind != JsonValueKind.Undefined;

        /// <summary>A JSON string.</summary>
        public static bool IsString(JsonElement? value) => value?.ValueKind == JsonValueKind.String;

        /// <summary>A JSON true or false.</summary>
        public static bool IsBool(JsonElement? value) => value?.ValueKind == JsonValueKind.True || value?.ValueKind == JsonValueKind.False;

        /// <summary>A whole JSON number within a long ([int] or [long] as ConvertFrom-Json reads it).</summary>
        public static bool IsInteger(JsonElement? value) => value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt64(out _);

        /// <summary>A JSON string's value, else null.</summary>
        public static string Str(JsonElement? value) => IsString(value) ? value.Value.GetString() : null;

        /// <summary>
        /// The value as [string] reads it: a string as is, a number as its text, True or False, the raw text of an object or
        /// array, empty for JSON null or no value.
        /// </summary>
        public static string Text(JsonElement? value)
        {
            if (!Present(value)) return "";
            switch (value.Value.ValueKind)
            {
                case JsonValueKind.String: return value.Value.GetString();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "False";
                default: return value.Value.GetRawText();
            }
        }

        /// <summary>PowerShell's truth of a value: a non-empty string, true, a non-zero number, an object, a non-empty array.</summary>
        public static bool Truthy(JsonElement? value)
        {
            if (!Present(value)) return false;
            switch (value.Value.ValueKind)
            {
                case JsonValueKind.String: return value.Value.GetString().Length > 0;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return value.Value.TryGetDouble(out var number) && number != 0;
                case JsonValueKind.Array: return value.Value.GetArrayLength() > 0;
                default: return true;
            }
        }

        /// <summary>ConvertTo-RcQualificationTime: a non-blank string read as an invariant time (UTC when it has no zone), in UTC; null otherwise.</summary>
        public static DateTime? Time(JsonElement? value)
        {
            var text = Str(value);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
                ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                : (DateTime?)null;
        }

        /// <summary>
        /// Select-RcQualificationFields: an object with exactly these names, each the record's string, boolean or whole
        /// number, null for anything else (never a nested body or an unrecognised field).
        /// </summary>
        public static JsonObject Select(JsonElement? record, params string[] names)
        {
            var selected = new JsonObject();
            foreach (var name in names)
            {
                var value = Member(record, name);
                JsonNode node = null;
                if (IsString(value)) node = JsonValue.Create(value.Value.GetString());
                else if (IsBool(value)) node = JsonValue.Create(value.Value.GetBoolean());
                else if (IsInteger(value)) node = JsonValue.Create(value.Value.GetInt64());
                selected[name] = node;
            }
            return selected;
        }

        /// <summary>The compact JSON of a node with the relaxed escaping ConvertTo-Json uses (non-ASCII text kept).</summary>
        public static string Compact(JsonNode node) => node == null ? "null" : node.ToJsonString(CompactOptions);

        /// <summary>The indented JSON of a node (ConvertTo-Json without -Compress).</summary>
        public static string Indented(JsonNode node) => node == null ? "null" : node.ToJsonString(IndentedOptions);

        private static readonly JsonSerializerOptions CompactOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static readonly JsonSerializerOptions IndentedOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        // statx(2) as RcHistoricalEvidence reads it (struct statx, 256 bytes).
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxStatx
        {
            [FieldOffset(0)] public uint Mask;
            [FieldOffset(28)] public ushort Mode;
            [FieldOffset(32)] public ulong Inode;
            [FieldOffset(136)] public uint DeviceMajor;
            [FieldOffset(140)] public uint DeviceMinor;
        }

        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    }

    /// <summary>Get-RcQualificationOwnerObservation and Get-QualificationProcessObservation: one recorded owner against the process table.</summary>
    public static class RcQualificationOwner
    {
        /// <summary>
        /// The start-time tolerance of the platform: Windows start times are exact FILETIME ticks (0 ms); Linux derives them
        /// from the boot clock, which differs by milliseconds between readers (1000 ms). platform is the host probe's answer.
        /// </summary>
        public static double ToleranceMilliseconds(string platform) => platform == "windows" ? 0 : 1000;

        /// <summary>
        /// The owner a record names (pid and processStartedAt): no-record without one; unknown invalid-owner-identity for a
        /// pid that is not a whole number in 1..int.MaxValue or a start that is not a time; absent pid-not-found when no such
        /// process runs; reused start-mismatch when its start differs by more than the tolerance; unknown process-probe-error
        /// when the start cannot be read or the probe throws; alive pid-start-match otherwise.
        /// </summary>
        public static RcQualificationObservation Observe(JsonElement? record, INightlyProcessTable processes, double toleranceMilliseconds)
        {
            if (processes == null) throw new ArgumentNullException(nameof(processes));
            if (record == null) return new RcQualificationObservation("unknown", "no-record");
            var unknown = new RcQualificationObservation("unknown", "invalid-owner-identity");
            var pidValue = RcQualificationRecords.Member(record, "pid");
            if (!RcQualificationRecords.IsInteger(pidValue)) return unknown;
            var pid = pidValue.Value.GetInt64();
            if (pid <= 0 || pid > int.MaxValue) return unknown;
            var expected = RcQualificationRecords.Time(RcQualificationRecords.Member(record, "processStartedAt"));
            if (expected == null) return unknown;
            try
            {
                if (!processes.IsAlive((int)pid)) return new RcQualificationObservation("absent", "pid-not-found");
                var actual = processes.StartTimeUtc((int)pid);
                if (actual == null) return new RcQualificationObservation("unknown", "process-probe-error");
                if (Math.Abs((actual.Value - expected.Value).TotalMilliseconds) > toleranceMilliseconds)
                    return new RcQualificationObservation("reused", "start-mismatch");
                return new RcQualificationObservation("alive", "pid-start-match");
            }
            catch (Exception)
            {
                return new RcQualificationObservation("unknown", "process-probe-error");
            }
        }
    }
}
