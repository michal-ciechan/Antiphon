using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

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
/// Every way a strict read can end. Only <see cref="NoRecord"/> (root present, file absent) and
/// <see cref="Record"/> are known facts; every other kind is unknown evidence, never a blank store.
/// </summary>
public enum RunnerAbsenceReadKind { NoRecord, Record, Corrupt, UnknownSchema, Denied, LostRoot, IoError }

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

        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// CARD-1153 D-1: <c>&lt;SessionLogPath&gt;/absence-evidence/&lt;id:N&gt;.json</c>. Records are retained
/// (no pruning, no AuditCleanup ownership). Reads are strict: a missing root after initialization,
/// a denied or failed read, a corrupt record or an unknown schema are distinct unknown kinds.
/// </summary>
public sealed class RunnerAbsenceEvidenceStore
{
    public const int SchemaVersion = 1;
    public const string DirectoryName = "absence-evidence";

    private static readonly string[] Members =
        ["version", "state", "sessionId", "acceptedStartedAt", "runnerStoreId", "runtimeEpoch", "updatedAtUtc"];

    private readonly IRunnerAbsenceEvidenceFiles _files;
    private readonly object _rootGate = new();
    private bool _initialized;

    public string Root { get; }

    /// <summary>
    /// Construction has no disk effect: the root is created by the first write. A root that existed
    /// at construction, or that this instance created, is initialized; its later absence is
    /// <see cref="RunnerAbsenceReadKind.LostRoot"/>, never a blank store.
    /// </summary>
    public RunnerAbsenceEvidenceStore(string sessionLogPath, IRunnerAbsenceEvidenceFiles? files = null)
    {
        _files = files ?? RunnerAbsenceEvidenceFiles.Instance;
        Root = Path.Combine(sessionLogPath, DirectoryName);
        _initialized = RootPresent();
    }

    /// <summary>The root was initialized and is now missing (recycled volume, deletion, IO failure).</summary>
    public bool RootLost
    {
        get { lock (_rootGate) return _initialized && !RootPresent(); }
    }

    public static string DirectoryFor(string sessionLogPath) => Path.Combine(sessionLogPath, DirectoryName);

    public string PathFor(Guid sessionId) => Path.Combine(Root, $"{sessionId:N}.json");

    public bool RootPresent()
    {
        try { return _files.DirectoryExists(Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public RunnerAbsenceRead Read(Guid sessionId)
    {
        if (!RootPresent())
        {
            // Before the first write nothing was ever recorded here: an absent root is no record.
            lock (_rootGate)
                return _initialized
                    ? new(RunnerAbsenceReadKind.LostRoot, null, "evidence root missing after initialization")
                    : new(RunnerAbsenceReadKind.NoRecord, null, null);
        }

        byte[]? bytes;
        try { bytes = _files.ReadIfExists(PathFor(sessionId)); }
        catch (UnauthorizedAccessException ex) { return new(RunnerAbsenceReadKind.Denied, null, ex.GetType().Name); }
        catch (IOException ex) { return new(RunnerAbsenceReadKind.IoError, null, ex.GetType().Name); }
        if (bytes is null)
        {
            // The file is absent, but only a root that is still present makes that a known fact.
            return RootPresent() || !_initialized
                ? new(RunnerAbsenceReadKind.NoRecord, null, null)
                : new(RunnerAbsenceReadKind.LostRoot, null, "evidence root missing after initialization");
        }

        return Parse(sessionId, bytes);
    }

    /// <summary>Durable before return; a failure throws so the caller can latch.</summary>
    public void Write(RunnerAbsenceRecord record)
    {
        lock (_rootGate)
        {
            if (_initialized && !RootPresent())
                throw new IOException("evidence root missing after initialization");
            if (!_initialized)
            {
                _files.CreateDirectory(Root);
                _initialized = true;
            }
        }

        _files.WriteAtomic(PathFor(record.SessionId), Serialize(record));
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
