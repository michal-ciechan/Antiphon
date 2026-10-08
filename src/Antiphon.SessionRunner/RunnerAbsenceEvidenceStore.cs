using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1153 D-1: one small record per observed id.</summary>
public enum RunnerAbsenceRecordState { Prepared, Attempted, ClosedUnused }

public sealed record RunnerAbsenceRecord(
    RunnerAbsenceRecordState State,
    Guid SessionId,
    DateTime? AcceptedStartedAt,
    Guid? RunnerStoreId,
    Guid RuntimeEpoch,
    DateTime UpdatedAtUtc);

/// <summary>
/// Every way a strict read can end. Only <see cref="NoRecord"/> (a healthy or never-initialized
/// store without a record or closure for the id) and <see cref="Record"/> are known facts; every
/// other kind is unknown evidence, never a blank store.
/// </summary>
public enum RunnerAbsenceReadKind
{
    NoRecord, Record, Corrupt, UnknownSchema, Denied, LostRoot, IoError,
    /// <summary>Store anchor, header or closure log missing, changed or malformed (CARD-1153 F1).</summary>
    StoreUnknown,
    /// <summary>The closure log names the id but its record is missing or no longer ClosedUnused (F1).</summary>
    ClosedRecordLost,
    /// <summary>
    /// A ClosedUnused record without a closure-log entry, or one that differs from the record its
    /// entry hashed (F3 round 2): the closure metadata disagrees, so nothing about the id is known.
    /// </summary>
    ClosureMismatch,
}

public readonly record struct RunnerAbsenceRead(RunnerAbsenceReadKind Kind, RunnerAbsenceRecord? Record, string? Detail)
{
    public bool IsKnown => Kind is RunnerAbsenceReadKind.NoRecord or RunnerAbsenceReadKind.Record;
}

/// <summary>File access for the evidence store; tests inject faults through this seam.</summary>
public interface IRunnerAbsenceEvidenceFiles
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);

    /// <summary>Null only when the file (or its directory) does not exist; every other failure throws.</summary>
    byte[]? ReadIfExists(string path);

    /// <summary>Temp file, write, flush to disk, rename over the target.</summary>
    void WriteAtomic(string path, byte[] bytes);

    /// <summary>Append and flush to disk before returning (the closure log).</summary>
    void AppendDurable(string path, byte[] bytes);

    /// <summary>File names directly under <paramref name="path"/>.</summary>
    IReadOnlyList<string> FileNames(string path);

    /// <summary>
    /// CARD-1153 F1 (round 2): make the names directly under <paramref name="path"/> durable.
    /// Flushing a file never persists its directory entry, so every created or renamed name is
    /// followed by this. Throws when the platform cannot establish it; the caller fails closed.
    /// </summary>
    void SyncDirectory(string path);
}

public sealed class RunnerAbsenceEvidenceFiles : IRunnerAbsenceEvidenceFiles
{
    public static readonly RunnerAbsenceEvidenceFiles Instance = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public byte[]? ReadIfExists(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows has no directory fsync for a rename; MOVEFILE_WRITE_THROUGH returns only once
            // the move is on disk.
            if (!MoveFileExW(tmp, path, MoveFileReplaceExisting | MoveFileWriteThrough))
                throw new IOException("write-through rename failed", new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        File.Move(tmp, path, overwrite: true);
    }

    public void AppendDurable(string path, byte[] bytes)
    {
        // Open, never create: a missing log was already refused by the caller as an unknown store.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public IReadOnlyList<string> FileNames(string path) =>
        Directory.EnumerateFiles(path).Select(p => Path.GetFileName(p)).ToList();

    /// <summary>
    /// Unix: open the directory read-only and fsync it (the POSIX directory-entry persistence
    /// primitive). Windows: FlushFileBuffers on a directory handle (FILE_FLAG_BACKUP_SEMANTICS);
    /// renames are additionally write-through. Any failure throws, so no certificate is issued.
    /// </summary>
    public void SyncDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var handle = CreateFileW(path, GenericRead | GenericWrite, ShareAll, IntPtr.Zero, OpenExisting,
                FileFlagBackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid || !FlushFileBuffers(handle))
                throw new IOException("directory flush failed", new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        var fd = Open(path, ReadOnly);
        if (fd < 0)
            throw new IOException($"directory open for fsync failed: errno {Marshal.GetLastPInvokeError()}");
        try
        {
            if (Fsync(fd) != 0)
                throw new IOException($"directory fsync failed: errno {Marshal.GetLastPInvokeError()}");
        }
        finally { Close(fd); }
    }

    private const int ReadOnly = 0;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareAll = 7, OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint MoveFileReplaceExisting = 0x1, MoveFileWriteThrough = 0x8;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existing, string replacement, uint flags);
}

/// <summary>
/// CARD-1153 D-1: <c>&lt;SessionLogPath&gt;/absence-evidence/&lt;id:N&gt;.json</c>. Records are retained
/// (no pruning, no AuditCleanup ownership). Reads are strict: a missing root after initialization,
/// a denied or failed read, a corrupt record or an unknown schema are distinct unknown kinds.
/// <para>
/// F1 (closure survives storage failure). The store is initialized by its first write, in this
/// order: root, empty closure log <c>closed.log</c>, header <c>store.json</c>, and last the anchor
/// <c>absence-evidence.identity.json</c> beside the root, both naming one random incarnation. A
/// closure appends the id to the closure log (flushed) before its ClosedUnused record is written.
/// Only two states are positive: never initialized (no anchor, and no record or closure under the
/// root) and healthy (anchor, header and remembered incarnation agree; closure log present and
/// well formed). Anything else (anchor or header missing or changed, root lost, closure log
/// missing or malformed, a logged closure whose record is missing, unreadable or not ClosedUnused)
/// is unknown, so a wiped, replaced or damaged store is never read as "nothing was certified".
/// F3 (round 2): each closure-log line also carries the SHA-256 of the exact ClosedUnused record
/// bytes and of the line before it (a chain seeded by the incarnation). A ClosedUnused record
/// without an entry or differing from its entry is unknown for that id
/// (<see cref="RunnerAbsenceReadKind.ClosureMismatch"/>); a broken chain (duplicated, reordered,
/// removed or foreign line) makes the whole store unknown. Either refuses creation and certification.
/// Namespace durability (F1, round 2): flushing a file does not persist its directory entry, so
/// every created directory, created file and rename is followed by a sync of the directory that
/// holds the new name (<see cref="IRunnerAbsenceEvidenceFiles.SyncDirectory"/>): the parents of
/// the root and of any ancestor it created, the root after the closure log and after the header,
/// the anchor's directory after the anchor, the root after every record. A store this process did
/// not initialize has its root and anchor directory synced once before its first write. A write,
/// and so a certificate or a ClosedUnused record, returns only after its closure-log append is
/// flushed and every name it depends on is durable; any sync failure throws and certification
/// fails closed. Platforms: Linux and other Unix fsync the directory itself (the POSIX primitive);
/// Windows uses MOVEFILE_WRITE_THROUGH for renames and FlushFileBuffers on a directory handle,
/// failing closed (no certificate, evidence latched) when NTFS or the host refuses either.
/// A record rename lost before its sync leaves the flushed closure-log entry, which reads as
/// <see cref="RunnerAbsenceReadKind.ClosedRecordLost"/>. Residual (runner-store tampering, H-24):
/// deleting both a closed record and its closure log line, or the anchor together with the whole
/// root, is indistinguishable from an id that was never closed.
/// </para>
/// </summary>
public sealed class RunnerAbsenceEvidenceStore
{
    public const int SchemaVersion = 1;
    public const string DirectoryName = "absence-evidence";
    public const string HeaderName = "store.json";
    public const string ClosureLogName = "closed.log";
    // "<id:N> <SHA-256 of the closed record> <SHA-256 of the previous line, or the chain seed>\n"
    private const int ClosureLineBytes = 32 + 1 + 64 + 1 + 64 + 1;

    private static readonly string[] Members =
        ["version", "state", "sessionId", "acceptedStartedAt", "runnerStoreId", "runtimeEpoch", "updatedAtUtc"];

    private IRunnerAbsenceEvidenceFiles _files;
    private readonly object _rootGate = new();
    private Guid? _incarnation;
    private bool _namespaceDurable;

    public string Root { get; }

    public string AnchorPath => Path.TrimEndingDirectorySeparator(Root) + ".identity.json";
    public string HeaderPath => Path.Combine(Root, HeaderName);
    public string ClosureLogPath => Path.Combine(Root, ClosureLogName);

    /// <summary>
    /// Construction has no disk effect: the store is initialized by the first write. An anchor
    /// readable at construction pins the incarnation this process will accept.
    /// </summary>
    public RunnerAbsenceEvidenceStore(string sessionLogPath, IRunnerAbsenceEvidenceFiles? files = null)
    {
        _files = files ?? RunnerAbsenceEvidenceFiles.Instance;
        Root = Path.Combine(sessionLogPath, DirectoryName);
        try
        {
            if (_files.ReadIfExists(AnchorPath) is { } anchor && TryIdentity(anchor, out var incarnation))
                _incarnation = incarnation;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>CARD-1153 F1 test seam: swap the file access after construction (runtime tests).</summary>
    internal IRunnerAbsenceEvidenceFiles Files { get => _files; set => _files = value; }

    /// <summary>Null while the store is healthy or never initialized; otherwise why it is unknown.</summary>
    public string? UnknownReason
    {
        get
        {
            lock (_rootGate)
            {
                var state = Inspect();
                return state.Unknown is { } unknown ? Describe(unknown) : null;
            }
        }
    }

    public static string DirectoryFor(string sessionLogPath) => Path.Combine(sessionLogPath, DirectoryName);

    public string PathFor(Guid sessionId) => Path.Combine(Root, $"{sessionId:N}.json");

    public RunnerAbsenceRead Read(Guid sessionId)
    {
        lock (_rootGate)
        {
            var state = Inspect();
            if (state.Unknown is { } unknown)
                return unknown;
            if (!state.Initialized)
                return new(RunnerAbsenceReadKind.NoRecord, null, null);

            var closed = state.Closed!.TryGetValue(sessionId, out var loggedHash);
            byte[]? bytes;
            try { bytes = _files.ReadIfExists(PathFor(sessionId)); }
            catch (UnauthorizedAccessException ex) { return new(RunnerAbsenceReadKind.Denied, null, ex.GetType().Name); }
            catch (IOException ex) { return new(RunnerAbsenceReadKind.IoError, null, ex.GetType().Name); }
            if (bytes is null)
            {
                if (!SafeDirectoryExists(Root))
                    return new(RunnerAbsenceReadKind.LostRoot, null, "evidence root missing after initialization");
                return closed
                    ? new(RunnerAbsenceReadKind.ClosedRecordLost, null, "closed record missing")
                    : new(RunnerAbsenceReadKind.NoRecord, null, null);
            }

            var parsed = Parse(sessionId, bytes);
            if (closed && parsed.Record is not { State: RunnerAbsenceRecordState.ClosedUnused })
                return new(RunnerAbsenceReadKind.ClosedRecordLost, null,
                    "closed record reads as " + (parsed.Record?.State.ToString() ?? Describe(parsed)));
            // F3 round 2: a closure is trusted only when its record and its log entry agree exactly.
            if (closed && RecordHash(bytes) != loggedHash)
                return new(RunnerAbsenceReadKind.ClosureMismatch, null, "closed record differs from its closure-log entry");
            if (!closed && parsed.Record is { State: RunnerAbsenceRecordState.ClosedUnused })
                return new(RunnerAbsenceReadKind.ClosureMismatch, null, "closed record without a closure-log entry");
            return parsed;
        }
    }

    /// <summary>
    /// Durable before return; a failure throws so the caller can latch. A store that is not healthy
    /// or never initialized throws <see cref="RunnerAbsenceStoreUnknownException"/> and writes nothing.
    /// </summary>
    public void Write(RunnerAbsenceRecord record)
    {
        lock (_rootGate)
        {
            var state = Inspect();
            if (state.Unknown is { } unknown)
                throw new RunnerAbsenceStoreUnknownException(Describe(unknown));
            if (!state.Initialized)
                state = Initialize();
            else
                EnsureNamespaceDurable();

            var bytes = Serialize(record);
            if (record.State == RunnerAbsenceRecordState.ClosedUnused && !state.Closed!.ContainsKey(record.SessionId))
                _files.AppendDurable(ClosureLogPath,
                    Encoding.ASCII.GetBytes($"{record.SessionId:N} {RecordHash(bytes)} {state.Tail}\n"));
            _files.WriteAtomic(PathFor(record.SessionId), bytes);
            _files.SyncDirectory(Root);
        }
    }

    // Closed maps each logged id to the SHA-256 of its closed record; Tail is the chain value the next line names.
    private readonly record struct StoreState(bool Initialized, IReadOnlyDictionary<Guid, string>? Closed, string? Tail, RunnerAbsenceRead? Unknown);

    private static StoreState UnknownState(RunnerAbsenceReadKind kind, string detail) =>
        new(false, null, null, new RunnerAbsenceRead(kind, null, detail));

    // Caller holds _rootGate.
    private StoreState Inspect()
    {
        byte[]? anchor;
        bool rootPresent;
        try
        {
            anchor = _files.ReadIfExists(AnchorPath);
            rootPresent = _files.DirectoryExists(Root);
        }
        catch (UnauthorizedAccessException ex) { return UnknownState(RunnerAbsenceReadKind.Denied, "store anchor: " + ex.GetType().Name); }
        catch (IOException ex) { return UnknownState(RunnerAbsenceReadKind.IoError, "store anchor: " + ex.GetType().Name); }

        if (anchor is null)
        {
            if (_incarnation is not null)
                return rootPresent
                    ? UnknownState(RunnerAbsenceReadKind.StoreUnknown, "store anchor missing after initialization")
                    : UnknownState(RunnerAbsenceReadKind.LostRoot, "evidence root missing after initialization");
            if (!rootPresent)
                return new(false, null, null, null);
            // An interrupted first initialization leaves at most the header and an empty log; a
            // record or a logged closure without an anchor means the anchor was lost.
            try
            {
                foreach (var name in _files.FileNames(Root))
                {
                    if (name is HeaderName || name.EndsWith(".tmp", StringComparison.Ordinal))
                        continue;
                    if (name == ClosureLogName && _files.ReadIfExists(ClosureLogPath) is { Length: 0 })
                        continue;
                    return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "evidence without a store anchor");
                }
            }
            catch (UnauthorizedAccessException ex) { return UnknownState(RunnerAbsenceReadKind.Denied, "store root: " + ex.GetType().Name); }
            catch (IOException ex) { return UnknownState(RunnerAbsenceReadKind.IoError, "store root: " + ex.GetType().Name); }
            return new(false, null, null, null);
        }

        if (!TryIdentity(anchor, out var incarnation))
            return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "store anchor malformed");
        if (_incarnation is { } known && known != incarnation)
            return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "store incarnation changed");
        if (!rootPresent)
            return UnknownState(RunnerAbsenceReadKind.LostRoot, "evidence root missing after initialization");

        byte[]? header;
        byte[]? log;
        try
        {
            header = _files.ReadIfExists(HeaderPath);
            log = _files.ReadIfExists(ClosureLogPath);
        }
        catch (UnauthorizedAccessException ex) { return UnknownState(RunnerAbsenceReadKind.Denied, "store header: " + ex.GetType().Name); }
        catch (IOException ex) { return UnknownState(RunnerAbsenceReadKind.IoError, "store header: " + ex.GetType().Name); }
        if (header is null)
            return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "store header missing");
        if (!TryIdentity(header, out var headerIncarnation) || headerIncarnation != incarnation)
            return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "store header changed");
        if (log is null)
            return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "closure log missing");
        if (ParseClosures(log, incarnation) is not { } closures)
            return UnknownState(RunnerAbsenceReadKind.StoreUnknown, "closure log malformed");

        _incarnation ??= incarnation;
        return new(true, closures.Closed, closures.Tail, null);
    }

    // Caller holds _rootGate; the store is never initialized. Each created or renamed name is
    // made durable (its directory synced) before the next step depends on it, and the anchor is
    // written last: a crash at any point reads as never initialized (completed by the next write)
    // or healthy, and a durable anchor implies a durable root, header and closure log.
    private StoreState Initialize()
    {
        var incarnation = Guid.NewGuid();
        var identity = Identity(incarnation);
        var created = new List<string> { Root };
        for (var dir = ParentOf(Root); dir is not null && !_files.DirectoryExists(dir); dir = ParentOf(dir))
            created.Insert(0, dir);
        _files.CreateDirectory(Root);
        foreach (var dir in created)
            _files.SyncDirectory(ParentOf(dir)!);
        _files.WriteAtomic(ClosureLogPath, []);
        _files.SyncDirectory(Root);
        _files.WriteAtomic(HeaderPath, identity);
        _files.SyncDirectory(Root);
        _files.WriteAtomic(AnchorPath, identity);
        _files.SyncDirectory(ParentOf(Root)!);
        _incarnation = incarnation;
        _namespaceDurable = true;
        return new(true, new Dictionary<Guid, string>(), ChainSeed(incarnation), null);
    }

    // Caller holds _rootGate. A healthy store this process did not initialize may hold names an
    // earlier process created but never synced (it died before the OS wrote them); sync once
    // before any write this process will rely on.
    private void EnsureNamespaceDurable()
    {
        if (_namespaceDurable)
            return;
        _files.SyncDirectory(Root);
        _files.SyncDirectory(ParentOf(Root)!);
        _namespaceDurable = true;
    }

    private static string? ParentOf(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));

    private bool SafeDirectoryExists(string path)
    {
        try { return _files.DirectoryExists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // F3 round 2: each line names its id, the hash of the closed record and the hash of the line
    // before it (the first names a seed bound to the incarnation), so a duplicated, reordered,
    // removed-from-the-middle or foreign line breaks the chain and the whole log reads unknown.
    // Losing only trailing lines leaves closed records without an entry (ClosureMismatch).
    private static (Dictionary<Guid, string> Closed, string Tail)? ParseClosures(byte[] log, Guid incarnation)
    {
        if (log.Length % ClosureLineBytes != 0)
            return null;
        var closed = new Dictionary<Guid, string>();
        var previous = ChainSeed(incarnation);
        for (var offset = 0; offset < log.Length; offset += ClosureLineBytes)
        {
            // A malformed record hash is not checked here: it never equals a record's hash, so that
            // id reads ClosureMismatch.
            var line = Encoding.ASCII.GetString(log, offset, ClosureLineBytes);
            if (line[^1] != '\n'
                || !Guid.TryParseExact(line[..32], "N", out var id) || id == Guid.Empty
                || line.Substring(98, 64) != previous
                || !closed.TryAdd(id, line.Substring(33, 64)))
                return null;
            previous = Convert.ToHexString(SHA256.HashData(log.AsSpan(offset, ClosureLineBytes)));
        }

        return (closed, previous);
    }

    private static string ChainSeed(Guid incarnation) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"absence-evidence closure log {incarnation:D}")));

    private static string RecordHash(byte[] record) => Convert.ToHexString(SHA256.HashData(record));

    private static byte[] Identity(Guid incarnation) =>
        Encoding.UTF8.GetBytes($"{{\"version\":{SchemaVersion},\"incarnation\":\"{incarnation:D}\"}}");

    private static bool TryIdentity(byte[] bytes, out Guid incarnation)
    {
        incarnation = Guid.Empty;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().Count() == 2
                && root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var v) && v == SchemaVersion
                && root.TryGetProperty("incarnation", out var value) && value.ValueKind == JsonValueKind.String
                && Guid.TryParseExact(value.GetString(), "D", out incarnation) && incarnation != Guid.Empty;
        }
        catch (JsonException) { return false; }
    }

    public static byte[] Serialize(RunnerAbsenceRecord record)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("version", SchemaVersion);
            json.WriteString("state", record.State.ToString());
            json.WriteString("sessionId", record.SessionId.ToString("D"));
            if (record.AcceptedStartedAt is { } generation)
                json.WriteString("acceptedStartedAt", Stamp(SessionGeneration.Normalize(generation)));
            else
                json.WriteNull("acceptedStartedAt");
            if (record.RunnerStoreId is { } store)
                json.WriteString("runnerStoreId", store.ToString("D"));
            else
                json.WriteNull("runnerStoreId");
            json.WriteString("runtimeEpoch", record.RuntimeEpoch.ToString("D"));
            json.WriteString("updatedAtUtc", Stamp(record.UpdatedAtUtc));
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static RunnerAbsenceRead Parse(Guid sessionId, byte[] bytes)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { return Corrupt("not json"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Corrupt("not an object");
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != SchemaVersion))
                return new(RunnerAbsenceReadKind.UnknownSchema, null, "unknown record schema");
            var names = root.EnumerateObject().Select(p => p.Name).ToList();
            if (names.Count != Members.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Count
                || !names.All(n => Members.Contains(n, StringComparer.Ordinal)))
                return Corrupt("member set");
            if (!Enum.TryParse<RunnerAbsenceRecordState>(String(root, "state"), ignoreCase: false, out var state)
                || !Enum.IsDefined(state) || String(root, "state") != state.ToString())
                return Corrupt("state");
            if (!Guid.TryParseExact(String(root, "sessionId"), "D", out var id) || id != sessionId)
                return Corrupt("session id");
            if (!Guid.TryParseExact(String(root, "runtimeEpoch"), "D", out var epoch) || epoch == Guid.Empty)
                return Corrupt("epoch");
            if (!TryStamp(root, "updatedAtUtc", nullable: false, out var updated))
                return Corrupt("updatedAtUtc");
            if (!TryStamp(root, "acceptedStartedAt", nullable: state == RunnerAbsenceRecordState.Attempted, out var generation))
                return Corrupt("acceptedStartedAt");
            Guid? store = null;
            var storeElement = root.GetProperty("runnerStoreId");
            if (storeElement.ValueKind == JsonValueKind.Null)
            {
                if (state != RunnerAbsenceRecordState.Attempted)
                    return Corrupt("runnerStoreId");
            }
            else if (storeElement.ValueKind == JsonValueKind.String
                && Guid.TryParseExact(storeElement.GetString(), "D", out var parsedStore) && parsedStore != Guid.Empty)
                store = parsedStore;
            else
                return Corrupt("runnerStoreId");
            return new(RunnerAbsenceReadKind.Record,
                new RunnerAbsenceRecord(state, id, generation, store, epoch, updated!.Value), null);
        }
    }

    private static RunnerAbsenceRead Corrupt(string detail) => new(RunnerAbsenceReadKind.Corrupt, null, detail);

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryStamp(JsonElement root, string name, bool nullable, out DateTime? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element))
            return false;
        if (element.ValueKind == JsonValueKind.Null)
            return nullable;
        if (element.ValueKind != JsonValueKind.String
            || !DateTime.TryParseExact(element.GetString(), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Kind != DateTimeKind.Utc)
            return false;
        value = parsed;
        return true;
    }

    internal static string Stamp(DateTime value) =>
        DateTime.SpecifyKind(value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(), DateTimeKind.Utc)
            .ToString("O", CultureInfo.InvariantCulture);

    internal static string Describe(RunnerAbsenceRead read) =>
        new StringBuilder(read.Kind.ToString()).Append(read.Detail is null ? "" : ": " + read.Detail).ToString();
}

/// <summary>CARD-1153 F1: the store is not healthy; nothing was written.</summary>
public sealed class RunnerAbsenceStoreUnknownException(string reason)
    : IOException("absence evidence store unknown: " + reason);
