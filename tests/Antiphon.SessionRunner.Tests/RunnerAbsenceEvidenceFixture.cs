using System.Collections.Concurrent;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S1 fixture: a temp state root, the production store and service, the production
/// artifact probe over real files, and the A-10 seam for runtime entries, adoption and custody.
/// Every service instance is a new runner epoch over the same root.
/// </summary>
internal sealed class RunnerAbsenceEvidenceHarness : IDisposable
{
    public static readonly DateTime Generation = new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc).AddTicks(4567);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1153-" + Guid.NewGuid().ToString("N"));
    public SessionRunnerSettings Settings { get; }
    public FaultingEvidenceFiles Files { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero));

    /// <summary>F2 round 2: the runner's clock. Monotonic time is <see cref="Clock"/>; the wall clock can step.</summary>
    public SteppingWallClock RunnerClock { get; }
    public ConcurrentDictionary<Guid, RunnerAbsenceRuntimeEntry> RuntimeEntries { get; } = new();
    public ConcurrentDictionary<Guid, bool> CustodyReservations { get; } = new();
    public bool AdoptionComplete { get; set; } = true;
    public int RuntimeLookups;
    public Guid StoreId { get; set; } = Guid.NewGuid();
    public string WatermarkDirectory => Path.Combine(Root, "launch-generations");
    public RunnerAbsenceEvidenceStore Store { get; private set; } = null!;
    public RunnerAbsenceEvidenceService Service { get; private set; } = null!;

    /// <summary>The store's file access; defaults to <see cref="Files"/>. F1 crash tests swap in a simulated volume.</summary>
    public IRunnerAbsenceEvidenceFiles StoreFiles { get; set; } = null!;

    /// <summary>The session log path the store lives under; defaults to <see cref="Root"/>.</summary>
    public string StorePath { get; set; } = null!;

    public RunnerAbsenceEvidenceHarness()
    {
        Settings = new SessionRunnerSettings { SessionLogPath = Root };
        RunnerClock = new SteppingWallClock(Clock);
        StoreFiles = Files;
        StorePath = Root;
        Restart();
    }

    /// <summary>A new service instance over the same root: a new random epoch.</summary>
    public RunnerAbsenceEvidenceService Restart()
    {
        Store = new RunnerAbsenceEvidenceStore(StorePath, StoreFiles);
        var inspection = new RunnerAbsenceArtifactInspection(
            Settings,
            id =>
            {
                Interlocked.Increment(ref RuntimeLookups);
                return RuntimeEntries.TryGetValue(id, out var entry) ? entry : RunnerAbsenceRuntimeEntry.None;
            },
            () => AdoptionComplete,
            id => CustodyReservations.ContainsKey(id),
            WatermarkDirectory);
        Service = new RunnerAbsenceEvidenceService(Store, inspection, () => StoreId, RunnerClock);
        // F2: requests issued within the window after an epoch starts are refused as possible replays.
        Clock.Advance(RunnerAbsenceEvidence.RequestFreshness + TimeSpan.FromSeconds(1));
        return Service;
    }

    public RunnerAbsenceRequest Request(Guid id, DateTime? generation = null, Guid? store = null, string? nonce = null) =>
        new(RunnerAbsenceEvidence.Version, id, generation ?? Generation, store ?? StoreId, nonce ?? RunnerAbsenceEvidence.NewNonce(),
            Clock.GetUtcNow().UtcDateTime);

    public RunnerAbsencePrepared PrepareOk(Guid id)
    {
        var prepared = Service.Prepare(Request(id));
        prepared.Refusal.ShouldBeNull();
        return prepared.Value!;
    }

    public string RecordPath(Guid id) => Store.PathFor(id);

    public byte[]? RecordBytes(Guid id) => File.Exists(RecordPath(id)) ? File.ReadAllBytes(RecordPath(id)) : null;

    public RunnerAbsenceRecord Record(Guid id)
    {
        var read = Store.Read(id);
        read.Kind.ShouldBe(RunnerAbsenceReadKind.Record);
        return read.Record!;
    }

    /// <summary>Every file under the root, relative, sorted.</summary>
    public IReadOnlyList<string> AllFiles() => Directory.Exists(Root)
        ? Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Root, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList()
        : [];

    public void SeedManifest(Guid id) => new PtyHostManifest
    {
        SessionId = id, PipeName = "fixture-no-process", HostPid = 1, HostStartTimeUtc = Generation,
        CreatedAtUtc = Generation, AcceptedStartedAt = Generation,
    }.SaveAtomic(PtyHostManifest.PathFor(Settings.PtyHostManifestDir, id));

    public void SeedTranscriptSidecar(Guid id) => new TranscriptSidecar
    {
        SessionId = id, ChildStartUtc = Generation, TranscriptPath = Path.Combine(Root, "t.jsonl"),
        Cwd = Root, UpdatedAtUtc = Generation,
    }.SaveAtomic(TranscriptSidecar.PathFor(Root, id));

    public void SeedMalformedTranscriptSidecar(Guid id)
    {
        var path = TranscriptSidecar.PathFor(Root, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x7b, 0x00, 0xff]);
        TranscriptSidecar.TryLoad(path).ShouldBeNull("the tolerant loader treats this sidecar as absent");
    }

    public void SeedHerdrSidecar(Guid id) => new HerdrPaneSidecar
    {
        SessionId = id, WorkspaceKey = "fixture", WorkspaceId = "fixture", TabId = "fixture", PaneId = "fixture",
        AcceptedStartedAt = Generation,
    }.SaveAtomic(HerdrPaneSidecar.PathFor(Root, id));

    public void SeedWatermark(Guid id) => new PhoneHomeLaunchGenerationStore(WatermarkDirectory).Record(id, Generation);

    public void SeedRecord(Guid id, RunnerAbsenceRecordState state, Guid epoch, DateTime? generation = null, Guid? store = null) =>
        Store.Write(new RunnerAbsenceRecord(state, id, generation ?? Generation, store ?? StoreId, epoch, Generation));

    public void SeedRaw(Guid id, string text)
    {
        Directory.CreateDirectory(Store.Root);
        File.WriteAllText(RecordPath(id), text);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (Exception) { }
    }
}

/// <summary>Real files with injectable faults on read, write and root presence.</summary>
internal sealed class FaultingEvidenceFiles : IRunnerAbsenceEvidenceFiles
{
    public Func<string, Exception?>? ReadFault { get; set; }
    public Func<string, Exception?>? WriteFault { get; set; }
    public int Writes;

    public bool DirectoryExists(string path) => RunnerAbsenceEvidenceFiles.Instance.DirectoryExists(path);

    public void CreateDirectory(string path) => RunnerAbsenceEvidenceFiles.Instance.CreateDirectory(path);

    public byte[]? ReadIfExists(string path)
    {
        if (ReadFault?.Invoke(path) is { } fault) throw fault;
        return RunnerAbsenceEvidenceFiles.Instance.ReadIfExists(path);
    }

    public void WriteAtomic(string path, byte[] bytes)
    {
        if (WriteFault?.Invoke(path) is { } fault) throw fault;
        Interlocked.Increment(ref Writes);
        RunnerAbsenceEvidenceFiles.Instance.WriteAtomic(path, bytes);
    }

    public void AppendDurable(string path, byte[] bytes)
    {
        if (WriteFault?.Invoke(path) is { } fault) throw fault;
        Interlocked.Increment(ref Writes);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public IReadOnlyList<string> FileNames(string path) =>
        Directory.EnumerateFiles(path).Select(p => Path.GetFileName(p)).ToList();

    public Func<string, Exception?>? SyncFault { get; set; }

    public void SyncDirectory(string path)
    {
        if (SyncFault?.Invoke(path) is { } fault) throw fault;
        RunnerAbsenceEvidenceFiles.Instance.SyncDirectory(path);
    }
}

/// <summary>
/// CARD-1153 F2 round 2: the runner's clock in the replay tests. The monotonic timestamp is the
/// shared <see cref="FakeTimeProvider"/>'s; the wall clock is that clock plus <see cref="WallOffset"/>,
/// so a test can step the runner's wall clock backwards or forwards (or skew it from the server's)
/// without monotonic time moving. Zero offset is exactly the shared clock.
/// </summary>
internal sealed class SteppingWallClock(FakeTimeProvider monotonic) : TimeProvider
{
    public TimeSpan WallOffset { get; set; }

    public override DateTimeOffset GetUtcNow() => monotonic.GetUtcNow() + WallOffset;

    public override long GetTimestamp() => monotonic.GetTimestamp();

    public override long TimestampFrequency => monotonic.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => monotonic.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        monotonic.CreateTimer(callback, state, dueTime, period);
}

/// <summary>Power failed: the simulated process stops here.</summary>
internal sealed class SimulatedPowerLoss(string operation) : Exception("power lost after " + operation);

/// <summary>Which of the not-yet-synced names the disk happened to write back before power failed.</summary>
internal enum Writeback { None, All, Latest }

/// <summary>
/// CARD-1153 F1 round 2 (Review a086fe80): an in-memory volume that separates what the process
/// sees from what survives power loss. File contents are durable once written (the production
/// writer flushes before its rename; the append flushes). A NAME (a created directory, a renamed
/// file) is durable only after <see cref="SyncDirectory"/> of the directory that holds it.
/// <see cref="Crash"/> keeps the durable names plus a chosen subset of the pending ones (none,
/// all, or only the latest, modelling arbitrary writeback order) and then drops every name whose
/// directory did not survive. Paths outside the volume always exist.
/// </summary>
internal sealed class PowerLossEvidenceFiles : IRunnerAbsenceEvidenceFiles
{
    private sealed class Node(bool directory)
    {
        public bool IsDirectory { get; } = directory;
        public byte[] Content { get; set; } = [];
    }

    private readonly string _volume;
    private readonly HashSet<string> _initial;
    private Dictionary<string, Node> _live = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Node> _durable = new(StringComparer.Ordinal);
    private List<(string Path, Node Node)> _pending = [];

    /// <param name="volume">Everything outside this directory always exists.</param>
    /// <param name="durableDirectories">Directories that already exist durably inside the volume.</param>
    public PowerLossEvidenceFiles(string volume, params string[] durableDirectories)
    {
        _volume = Path.TrimEndingDirectorySeparator(volume);
        _initial = new HashSet<string>(durableDirectories, StringComparer.Ordinal);
        foreach (var dir in durableDirectories)
            _live[dir] = _durable[dir] = new Node(true);
    }

    /// <summary>Every mutating call in order: <c>mkdir:</c>, <c>write:</c>, <c>append:</c>, <c>sync:</c> plus the name.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>Power fails immediately after the first operation this matches.</summary>
    public Func<string, bool>? CrashAfter { get; set; }

    private bool Ambient(string path) => !path.StartsWith(_volume + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    public bool DirectoryExists(string path) => Ambient(path) || _live.TryGetValue(path, out var node) && node.IsDirectory;

    public void CreateDirectory(string path)
    {
        var missing = new List<string>();
        for (var dir = path; !DirectoryExists(dir); dir = Path.GetDirectoryName(dir)!)
            missing.Insert(0, dir);
        foreach (var dir in missing)
            Bind(dir, new Node(true), "mkdir:");
    }

    public byte[]? ReadIfExists(string path)
    {
        if (!_live.TryGetValue(path, out var node))
            return null;
        if (node.IsDirectory)
            throw new UnauthorizedAccessException("is a directory");
        return node.Content.ToArray();
    }

    public void WriteAtomic(string path, byte[] bytes)
    {
        if (!DirectoryExists(Path.GetDirectoryName(path)!))
            throw new DirectoryNotFoundException(path);
        Bind(path, new Node(false) { Content = bytes.ToArray() }, "write:");
    }

    public void AppendDurable(string path, byte[] bytes)
    {
        if (!_live.TryGetValue(path, out var node) || node.IsDirectory)
            throw new FileNotFoundException(path);
        node.Content = [.. node.Content, .. bytes];
        After("append:" + Path.GetFileName(path));
    }

    public IReadOnlyList<string> FileNames(string path) => _live
        .Where(e => !e.Value.IsDirectory && Path.GetDirectoryName(e.Key) == path)
        .Select(e => Path.GetFileName(e.Key)).ToList();

    public void SyncDirectory(string path)
    {
        if (!DirectoryExists(path))
            throw new DirectoryNotFoundException(path);
        foreach (var (name, node) in _pending.Where(c => Path.GetDirectoryName(c.Path) == path))
            _durable[name] = node;
        _pending = _pending.Where(c => Path.GetDirectoryName(c.Path) != path).ToList();
        After("sync:" + Path.GetFileName(path));
    }

    /// <summary>
    /// The process restarted without losing power after an earlier process (or build) created
    /// these names but never synced them: everything inside the volume is pending again.
    /// </summary>
    public void ForgetDurability()
    {
        _pending = _live.Where(e => !_initial.Contains(e.Key)).OrderBy(e => e.Key.Length).Select(e => (e.Key, e.Value)).ToList();
        foreach (var (name, _) in _pending)
            _durable.Remove(name);
    }

    /// <summary>Power loss: the durable names plus the chosen writeback survive.</summary>
    public void Crash(Writeback writeback)
    {
        var survivors = writeback switch
        {
            Writeback.All => _pending,
            Writeback.Latest => _pending.TakeLast(1).ToList(),
            _ => [],
        };
        foreach (var (name, node) in survivors)
            _durable[name] = node;
        _pending = [];
        bool dropped;
        do
        {
            dropped = false;
            foreach (var name in _durable.Keys.ToList())
            {
                var parent = Path.GetDirectoryName(name)!;
                if (Ambient(parent) || _durable.TryGetValue(parent, out var dir) && dir.IsDirectory)
                    continue;
                _durable.Remove(name);
                dropped = true;
            }
        }
        while (dropped);
        _live = new Dictionary<string, Node>(_durable, StringComparer.Ordinal);
        CrashAfter = null;
    }

    private void Bind(string path, Node node, string verb)
    {
        _live[path] = node;
        _pending.Add((path, node));
        After(verb + Path.GetFileName(path));
    }

    private void After(string operation)
    {
        Operations.Add(operation);
        if (CrashAfter?.Invoke(operation) != true)
            return;
        CrashAfter = null;
        throw new SimulatedPowerLoss(operation);
    }
}
