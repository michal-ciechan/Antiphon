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
    public ConcurrentDictionary<Guid, RunnerAbsenceRuntimeEntry> RuntimeEntries { get; } = new();
    public ConcurrentDictionary<Guid, bool> CustodyReservations { get; } = new();
    public bool AdoptionComplete { get; set; } = true;
    public int RuntimeLookups;
    public Guid StoreId { get; set; } = Guid.NewGuid();
    public string WatermarkDirectory => Path.Combine(Root, "launch-generations");
    public RunnerAbsenceEvidenceStore Store { get; private set; } = null!;
    public RunnerAbsenceEvidenceService Service { get; private set; } = null!;

    public RunnerAbsenceEvidenceHarness()
    {
        Settings = new SessionRunnerSettings { SessionLogPath = Root };
        Restart();
    }

    /// <summary>A new service instance over the same root: a new random epoch.</summary>
    public RunnerAbsenceEvidenceService Restart()
    {
        Store = new RunnerAbsenceEvidenceStore(Root, Files);
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
        Service = new RunnerAbsenceEvidenceService(Store, inspection, () => StoreId, Clock);
        return Service;
    }

    public RunnerAbsenceRequest Request(Guid id, DateTime? generation = null, Guid? store = null, string? nonce = null) =>
        new(RunnerAbsenceEvidence.Version, id, generation ?? Generation, store ?? StoreId, nonce ?? RunnerAbsenceEvidence.NewNonce());

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
}
