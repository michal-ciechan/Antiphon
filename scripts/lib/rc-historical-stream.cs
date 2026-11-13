#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Xml;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.Rc;

// No qualification caller: these bytes are evidence, not admitted passes. The
// caller must retain the returned inventory digest in its later admission envelope.
public static class HistoricalEvidence
{
    public const long FileLimit = 4L * 1024 * 1024 * 1024;
    public const int ScalarLimit = 1024 * 1024, EntryLimit = 1_000_000;
    private const int BufferSize = 65536, FileCountLimit = 10000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly HashSet<string> Scalars = new(StringComparer.Ordinal)
    {
        "runId", "candidateSha", "candidateId", "sourceSha", "platform", "complete", "outcome",
        "suite", "chunk", "runWallSeconds", "lastPhase", "exitCode", "attempt", "format"
    };
    public sealed class Entry
    {
        [JsonRequired] public string relativePath { get; set; } = "";
        [JsonRequired] public string bundlePath { get; set; } = "";
        [JsonRequired] public string sha256 { get; set; } = "";
        [JsonRequired] public long length { get; set; }
    }
    public sealed class Inventory
    {
        [JsonRequired] public string format { get; set; } = "antiphon-rc-evidence-v1";
        [JsonRequired] public List<Entry> files { get; set; } = new();
        [JsonRequired] public Entry rows { get; set; } = new();
        [JsonRequired] public int maxBufferedBytes { get; set; }
    }
    public sealed class Result
    {
        public bool valid { get; set; }
        public string evidenceRoot { get; set; } = "";
        public string inventorySha256 { get; set; } = "";
        public List<Entry> files { get; set; } = new();
        public int maxBufferedBytes { get; set; }
    }
    private static InvalidDataException Refuse(string reason) => new(reason);
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool HandleInside(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw Refuse("evidence-path");
        return path;
    }
    // Inspect every component, including ancestors of the supplied roots. Native
    // handle checks below additionally bind the file that was actually opened.
    private static void NoLinks(string path)
    {
        for (var p = Full(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw Refuse("evidence-link");
    }
    private static void Enumerate(string root, string directory, List<string> paths)
    {
        if (Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar).Length > 64) throw Refuse("evidence-depth");
        NoLinks(directory);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(x => x, StringComparer.Ordinal))
        {
            NoLinks(path);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Enumerate(root, path, paths);
            else paths.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
            if (paths.Count > FileCountLimit) throw Refuse("evidence-file-count");
        }
    }
    private static string[] Paths(string root, string[]? selected)
    {
        var paths = new List<string>();
        if (selected == null || selected.Length == 0) Enumerate(root, root, paths);
        else paths.AddRange(selected);
        if (paths.Count == 0 || paths.Count > FileCountLimit) throw Refuse("evidence-file-count");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var components = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!unique.Add(Relative(path))) throw Refuse("evidence-path");
            var prefix = "";
            foreach (var part in path.Split('/'))
            {
                prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                if (components.TryGetValue(prefix, out var spelling) && spelling != prefix) throw Refuse("evidence-path");
                components[prefix] = prefix;
            }
        }
        return paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
    public static Result Export(string sourceRoot, string evidenceRoot, string repositoryRoot, string checkoutRoot,
        string[]? selected = null, Action? afterOpen = null)
    {
        var watch = Stopwatch.StartNew();
        sourceRoot = Full(sourceRoot); evidenceRoot = Full(evidenceRoot);
        if (Inside(evidenceRoot, sourceRoot) || Inside(sourceRoot, evidenceRoot) ||
            Inside(evidenceRoot, Full(repositoryRoot)) || Inside(evidenceRoot, Full(checkoutRoot)) ||
            Directory.Exists(evidenceRoot) || File.Exists(evidenceRoot)) throw Refuse("evidence-output");
        NoLinks(sourceRoot);
        // A new leaf under an existing, non-linked parent is the ownership boundary.
        NoLinks(Path.GetDirectoryName(evidenceRoot)!);
        var paths = Paths(sourceRoot, selected);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(evidenceRoot);
        else Directory.CreateDirectory(evidenceRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var owner = new FileStream(Path.Combine(evidenceRoot, ".writer"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "objects"));
        var inventory = new Inventory();
        var rowsPath = Path.Combine(evidenceRoot, "rows.jsonl");
        long rowCount = 0;
        using (var rows = new StreamWriter(new FileStream(rowsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None), Utf8, BufferSize))
        {
            foreach (var path in paths)
            {
                Deadline(watch);
                var pending = Path.Combine(evidenceRoot, "objects", Guid.NewGuid().ToString("N"));
                var entry = Freeze(sourceRoot, path, pending, afterOpen, watch);
                entry.bundlePath = "objects/" + entry.sha256;
                var target = Path.Combine(evidenceRoot, entry.bundlePath);
                if (File.Exists(target))
                {
                    var existing = Digest(evidenceRoot, entry.bundlePath, watch);
                    if (existing.sha256 != entry.sha256 || existing.length != entry.length) throw Refuse("evidence-digest");
                    // Keep the duplicate pending bytes; no cleanup authority is inferred.
                }
                else File.Move(pending, target);
                void Emit(object row)
                {
                    if (++rowCount > EntryLimit) throw Refuse("evidence-entry-count");
                    rows.WriteLine(JsonSerializer.Serialize(row));
                }
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = Reader(target);
                    var parser = new JsonScanner(reader, (field, value) => Emit(new { path, kind = "scalar", field, value }), watch);
                    parser.Read();
                    inventory.maxBufferedBytes = Math.Max(inventory.maxBufferedBytes, parser.MaxBufferedBytes);
                }
                else if (path.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)) ReadTrx(target, path, Emit, watch);
                // Other retained files are opaque. Their bytes remain bound for later adapters.
                inventory.files.Add(entry);
            }
            rows.Flush();
            ((FileStream)rows.BaseStream).Flush(true);
        }
        if ((selected == null || selected.Length == 0) && !paths.SequenceEqual(Paths(sourceRoot, null))) throw Refuse("evidence-changing");
        inventory.rows = Digest(evidenceRoot, "rows.jsonl", watch);
        inventory.rows.bundlePath = "rows.jsonl";
        inventory.maxBufferedBytes = Math.Max(inventory.maxBufferedBytes, BufferSize * 4);
        var inventoryPath = Path.Combine(evidenceRoot, "inventory.json");
        // This terminal inventory is written only once, after all bytes are durable.
        // An interrupted export has no terminal inventory and cannot verify.
        var temporary = Path.Combine(evidenceRoot, "inventory.pending");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, inventory, Json);
            file.Flush(true);
        }
        if (new FileInfo(temporary).Length > 16 * 1024 * 1024) throw Refuse("evidence-inventory-limit");
        File.Move(temporary, inventoryPath);
        return Verify(evidenceRoot, null);
    }
    public static Result Verify(string evidenceRoot, string? expectedInventorySha256)
    {
        evidenceRoot = Full(evidenceRoot); NoLinks(evidenceRoot);
        var watch = Stopwatch.StartNew();
        var seal = Digest(evidenceRoot, "inventory.json", watch);
        if (seal.length > 16 * 1024 * 1024) throw Refuse("evidence-inventory-limit");
        if (!string.IsNullOrEmpty(expectedInventorySha256) && seal.sha256 != expectedInventorySha256) throw Refuse("evidence-digest");
        using (var text = Reader(Path.Combine(evidenceRoot, "inventory.json"))) new JsonScanner(text, null, watch).Read();
        Inventory inventory;
        try
        {
            using var file = File.OpenRead(Path.Combine(evidenceRoot, "inventory.json"));
            inventory = JsonSerializer.Deserialize<Inventory>(file, Json) ?? throw Refuse("evidence-inventory");
        }
        catch (JsonException) { throw Refuse("evidence-inventory"); }
        if (inventory.format != "antiphon-rc-evidence-v1" || inventory.files == null || inventory.rows == null ||
            inventory.files.Count == 0 || inventory.files.Count > FileCountLimit) throw Refuse("evidence-inventory");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in inventory.files)
        {
            if (entry == null || !unique.Add(Relative(entry.relativePath)) || entry.sha256.Length != 64 ||
                entry.sha256.Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)) || entry.bundlePath != "objects/" + entry.sha256)
                throw Refuse("evidence-inventory");
            Check(entry);
        }
        if (inventory.rows.bundlePath != "rows.jsonl") throw Refuse("evidence-inventory");
        Check(inventory.rows);
        // Recheck the seal after consuming references. No trust is cached by mtime/path.
        if (Digest(evidenceRoot, "inventory.json", watch).sha256 != seal.sha256) throw Refuse("evidence-changing");
        return new Result { valid = true, evidenceRoot = evidenceRoot, inventorySha256 = seal.sha256,
            files = inventory.files, maxBufferedBytes = inventory.maxBufferedBytes };
        void Check(Entry entry)
        {
            var actual = Digest(evidenceRoot, Relative(entry.bundlePath), watch);
            if (actual.sha256 != entry.sha256 || actual.length != entry.length) throw Refuse("evidence-digest");
        }
    }
    private static StreamReader Reader(string path) => new(path, Utf8, false, BufferSize);
    private static void Deadline(Stopwatch watch)
    {
        if (watch.Elapsed > TimeSpan.FromMinutes(20)) throw Refuse("evidence-deadline");
    }
    private static Entry Digest(string root, string path, Stopwatch watch) => Freeze(root, path, null, null, watch);
    private static Entry Freeze(string root, string relative, string? destination, Action? afterOpen, Stopwatch watch)
    {
        var path = Path.Combine(root, Relative(relative)); NoLinks(path);
        using var handle = OpenRegular(path);
        var before = Inspect(handle, root, path);
        if (before.Size > (ulong)FileLimit) throw Refuse("evidence-file-limit");
        using var stream = new FileStream(handle, FileAccess.Read, BufferSize, false);
        try { afterOpen?.Invoke(); }
        catch (Exception ex) { throw new InvalidDataException("evidence-changing", ex); }
        using var output = destination == null ? null : new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize]; long length = 0;
        for (;;)
        {
            Deadline(watch);
            var readWatch = Stopwatch.StartNew();
            // Only verified regular local handles are read; no pipes/devices/network executor.
            var n = stream.Read(buffer, 0, buffer.Length);
            if (readWatch.Elapsed > TimeSpan.FromSeconds(60)) throw Refuse("evidence-io-deadline");
            if (n == 0) break;
            length += n;
            if (length > FileLimit) throw Refuse("evidence-file-limit");
            hash.AppendData(buffer, 0, n); output?.Write(buffer, 0, n);
        }
        output?.Flush(true);
        var after = Inspect(handle, root, path);
        using var current = OpenRegular(path);
        if (before != after || before != Inspect(current, root, path) || length != (long)before.Size)
            throw Refuse("evidence-changing");
        return new Entry { relativePath = relative, length = length, sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
    }
    private static void ReadTrx(string file, string path, Action<object> emit, Stopwatch watch)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = FileLimit, MaxCharactersFromEntities = 0, IgnoreComments = true, IgnoreWhitespace = true };
        try
        {
            using var reader = XmlReader.Create(file, settings);
            string owner = "";
            while (reader.Read())
            {
                Deadline(watch);
                if (reader.Depth > 64) throw Refuse("evidence-depth");
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "UnitTest") owner = reader.GetAttribute("id") ?? "";
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "UnitTest") owner = "";
                if (reader.NodeType != XmlNodeType.Element || (reader.LocalName != "UnitTestResult" && reader.LocalName != "TestMethod")) continue;
                var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (reader.MoveToFirstAttribute()) do
                {
                    if (reader.Value.Length > ScalarLimit) throw Refuse("evidence-scalar");
                    attributes.Add(reader.Name, reader.Value);
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
                emit(new { path, kind = reader.LocalName, owner, attributes });
            }
        }
        catch (XmlException) { throw Refuse("evidence-xml"); }
    }

    // Streaming JSON grammar, including strings longer than the scalar cap when
    // ignored. Utf8JsonReader requires an entire string token in its buffer; this
    // scanner validates ignored strings/escapes without retaining their bytes.
    private sealed class JsonScanner
    {
        private readonly TextReader reader;
        private readonly Action<string, object?>? emit;
        private readonly Stopwatch watch;
        private readonly char[] buffer = new char[32768];
        private int offset, count, entries, retainedNames;
        public int MaxBufferedBytes { get; private set; } = BufferSize * 4;
        public JsonScanner(TextReader reader, Action<string, object?>? emit, Stopwatch watch)
        { this.reader = reader; this.emit = emit; this.watch = watch; }
        private int Peek()
        {
            if (offset == count) { Deadline(watch); count = reader.Read(buffer, 0, buffer.Length); offset = 0; }
            return count == 0 ? -1 : buffer[offset];
        }
        private int Take() { var c = Peek(); if (c >= 0) offset++; return c; }
        private void White() { while (Peek() is 32 or 9 or 10 or 13) Take(); }
        private void Want(int c) { if (Take() != c) throw Refuse("evidence-json"); }
        public void Read()
        {
            try { White(); Value(0, null); White(); if (Peek() != -1) throw Refuse("evidence-json"); }
            catch (DecoderFallbackException) { throw Refuse("evidence-json: utf8"); }
        }
        private void Value(int depth, string? field)
        {
            if (depth > 64) throw Refuse("evidence-depth");
            if (++entries > EntryLimit) throw Refuse("evidence-entry-count");
            White(); var c = Peek();
            if (c == '{') { Object(depth); return; }
            if (c == '[')
            {
                Take(); White(); if (Peek() == ']') { Take(); return; }
                for (;;) { Value(depth + 1, null); White(); if (Peek() == ']') { Take(); return; } Want(','); }
            }
            object? value;
            if (c == '"') value = String(field != null);
            else if (c == 't') { Literal("true"); value = true; }
            else if (c == 'f') { Literal("false"); value = false; }
            else if (c == 'n') { Literal("null"); value = null; }
            else value = Number();
            if (field != null) emit?.Invoke(field, value);
        }
        private void Object(int depth)
        {
            Want('{'); White(); if (Peek() == '}') { Take(); return; }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localNames = 0;
            for (;;)
            {
                White(); var name = String(true)!;
                if (!names.Add(name)) throw Refuse("evidence-json: duplicate");
                localNames += name.Length; retainedNames += name.Length;
                if (retainedNames > 8 * 1024 * 1024) throw Refuse("evidence-entry-count");
                White(); Want(':');
                Value(depth + 1, depth == 0 && Scalars.Contains(name) ? name : null);
                White(); if (Peek() == '}') { Take(); retainedNames -= localNames; return; } Want(',');
            }
        }
        private void Literal(string value) { foreach (var c in value) Want(c); }
        private double Number()
        {
            var s = new StringBuilder();
            void Add() { if (s.Length >= ScalarLimit) throw Refuse("evidence-scalar"); s.Append((char)Take()); }
            bool Digit() => Peek() >= '0' && Peek() <= '9';
            if (Peek() == '-') Add();
            if (Peek() == '0') Add();
            else { if (Peek() < '1' || Peek() > '9') throw Refuse("evidence-json"); while (Digit()) Add(); }
            if (Peek() == '.') { Add(); if (!Digit()) throw Refuse("evidence-json"); while (Digit()) Add(); }
            if (Peek() is 'e' or 'E') { Add(); if (Peek() is '+' or '-') Add(); if (!Digit()) throw Refuse("evidence-json"); while (Digit()) Add(); }
            if (!double.TryParse(s.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
                throw Refuse("evidence-json");
            MaxBufferedBytes = Math.Max(MaxBufferedBytes, BufferSize * 4 + s.Length * 4);
            return n;
        }
        private int Hex()
        {
            var n = 0;
            for (var i = 0; i < 4; i++)
            {
                var c = Take(); var v = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (v < 0) throw Refuse("evidence-json"); n = n * 16 + v;
            }
            return n;
        }
        private string? String(bool capture)
        {
            Want('"'); var s = capture ? new StringBuilder() : null;
            void Add(int ch)
            {
                if (s == null) return;
                if (s.Length >= ScalarLimit / 2) throw Refuse("evidence-scalar");
                s.Append((char)ch); MaxBufferedBytes = Math.Max(MaxBufferedBytes, BufferSize * 4 + s.Length * 4);
            }
            for (;;)
            {
                var c = Take(); if (c == '"') return s?.ToString();
                if (c < 32) throw Refuse("evidence-json");
                if (c == '\\')
                {
                    c = Take();
                    if (c == 'u')
                    {
                        c = Hex();
                        if (char.IsHighSurrogate((char)c))
                        {
                            Add(c); Want('\\'); Want('u'); var low = Hex();
                            if (!char.IsLowSurrogate((char)low)) throw Refuse("evidence-json"); Add(low); continue;
                        }
                        if (char.IsLowSurrogate((char)c)) throw Refuse("evidence-json");
                    }
                    else c = c switch { '"' => '"', '\\' => '\\', '/' => '/', 'b' => 8, 'f' => 12, 'n' => 10, 'r' => 13, 't' => 9, _ => throw Refuse("evidence-json") };
                }
                else if (char.IsHighSurrogate((char)c))
                {
                    Add(c); c = Take(); if (c < 0 || !char.IsLowSurrogate((char)c)) throw Refuse("evidence-json");
                }
                else if (char.IsLowSurrogate((char)c)) throw Refuse("evidence-json");
                Add(c);
            }
        }
    }

    private readonly record struct Stamp(ulong Device, ulong Inode, ulong Size, long Write, long Change);
    private static SafeFileHandle OpenRegular(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            if (Statx(-100, path, 0x100, 0x7ff, out var stat) != 0 || (stat.Mode & 0xf000) != 0x8000) throw Refuse("evidence-link: not regular");
            var fd = Open(path, 0x80000 | 0x20000 | 0x800);
            if (fd < 0) throw Refuse("evidence-open");
            return new SafeFileHandle((IntPtr)fd, true);
        }
        if (OperatingSystem.IsWindows())
        {
            var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000 | 0x08000000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw Refuse("evidence-open"); }
            return handle;
        }
        throw Refuse("evidence-platform-unknown");
    }
    private static Stamp Inspect(SafeFileHandle handle, string root, string path)
    {
        NoLinks(path);
        if (OperatingSystem.IsLinux())
        {
            if (Statx(handle.DangerousGetHandle().ToInt32(), "", 0x1000, 0x7ff, out var s) != 0 || (s.Mask & 0x7ff) != 0x7ff) throw Refuse("evidence-stat");
            if ((s.Mode & 0xf000) != 0x8000 || s.Links != 1) throw Refuse("evidence-link");
            var buffer = new byte[32768];
            var length = ReadLink("/proc/self/fd/" + handle.DangerousGetHandle().ToInt32(), buffer, (nuint)buffer.Length);
            if (length <= 0 || length >= buffer.Length || !HandleInside(Utf8.GetString(buffer, 0, (int)length), root)) throw Refuse("evidence-path");
            return new Stamp(((ulong)s.DeviceMajor << 32) | s.DeviceMinor, s.Inode, s.Size, s.MtimeSeconds * 1000000000 + s.MtimeNanos, s.CtimeSeconds * 1000000000 + s.CtimeNanos);
        }
        if (!GetFileInformationByHandle(handle, out var w) || !GetFileInformationByHandleEx(handle, 0, out var basic, (uint)Marshal.SizeOf<WindowsBasic>())) throw Refuse("evidence-stat");
        if (GetFileType(handle) != 1 || (w.Attributes & (0x10u | 0x400u)) != 0 || w.Links != 1) throw Refuse("evidence-link");
        var final = new StringBuilder(32768);
        var n = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
        if (n == 0 || n >= final.Capacity) throw Refuse("evidence-path");
        var resolved = final.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        if (!HandleInside(resolved, root)) throw Refuse("evidence-path");
        return new Stamp(w.Volume, ((ulong)w.IndexHigh << 32) | w.IndexLow, ((ulong)w.SizeHigh << 32) | w.SizeLow, basic.Write, basic.Change);
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long CtimeSeconds;
        [FieldOffset(104)] public uint CtimeNanos;
        [FieldOffset(112)] public long MtimeSeconds;
        [FieldOffset(120)] public uint MtimeNanos;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowsInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowsBasic { public long Creation, Access, Write, Change; public uint Attributes; }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern nint ReadLink(string path, byte[] buffer, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out WindowsBasic info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    private static uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags) => GetFinalPathNameByHandleW(handle, path, capacity, flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
}
