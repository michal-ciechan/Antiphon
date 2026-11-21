using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S18: one state root for the run-lock cases (NightlyDeadOwnerLockTests, NightlyReclaimRaceTests): a
/// NightlyFixture, a FakeProcessTable with the boot and host ids of Write-C1386Seams, and the NightlyRunLock the run
/// core takes. Plant writes run.lock as Write-C1386Plant did; the readers are the C1386 helpers (Get-C1386LockText,
/// Get-C1386ReclaimReason, Test-C1386RunIdGone/Kept, Test-C1386NoReclaim).
/// </summary>
internal sealed class NightlyLockFixture : IDisposable
{
    private NightlyLockFixture(string boot, string host)
    {
        Fixture = NightlyFixture.Create("nightly-lock");
        Processes = new FakeProcessTable { Boot = boot, Host = host };
        State = Fixture.PathOf("state");
        Directory.CreateDirectory(State);
        Host = Fixture.Host(processes: Processes);
        Lock = new NightlyRunLock(Host);
    }

    /// <summary>A state root whose host reports this boot and host id; every pid is alive until Dead.</summary>
    public static NightlyLockFixture Create(string boot = "boot-current", string host = "host-current") => new(boot, host);

    public NightlyFixture Fixture { get; }

    public FakeProcessTable Processes { get; }

    public NightlyHost Host { get; }

    /// <summary>The attempt under test (the run core's lock).</summary>
    public NightlyRunLock Lock { get; }

    public string State { get; }

    public string LockPath => NightlyRunLock.LockPath(State);

    public string ReclaimPath => Path.Combine(State, NightlyRunLock.ReclaimRecordName);

    /// <summary>Marks the pids gone (the IsProcessAlive seam answers false for them).</summary>
    public NightlyLockFixture Dead(params int[] pids)
    {
        foreach (var pid in pids) Processes.Exit(pid);
        return this;
    }

    /// <summary>Write-C1386Plant: run.lock as ConvertTo-Json -Compress of the planted owner.</summary>
    public void Plant(int ownerPid, string runId, int parentPid = 0, bool omitParent = false, string? bootId = null, string? hostId = null,
        string processStartedAt = "2020-01-01T00:00:00.0000000Z") =>
        File.WriteAllText(LockPath, PlantJson(ownerPid, runId, parentPid, omitParent, bootId, hostId, processStartedAt));

    /// <summary>The planted record's JSON.</summary>
    public static string PlantJson(int ownerPid, string runId, int parentPid = 0, bool omitParent = false, string? bootId = null, string? hostId = null,
        string processStartedAt = "2020-01-01T00:00:00.0000000Z")
    {
        var record = new JsonObject
        {
            ["pid"] = ownerPid,
            ["processStartedAt"] = processStartedAt,
            ["startedAt"] = "2020-01-01T00:00:00.0000000Z",
            ["runId"] = runId,
        };
        if (!omitParent)
        {
            record["parentPid"] = parentPid;
            record["parentStartedAt"] = "2020-01-01T00:00:00.0000000Z";
        }
        if (bootId is not null) record["bootId"] = bootId;
        if (hostId is not null) record["hostId"] = hostId;
        return record.ToJsonString();
    }

    /// <summary>A new attempt in the run core with a fresh run id (Invoke-FxRun).</summary>
    public NightlyLockResult Enter(string? runId = null) => Lock.Enter(State, runId ?? Guid.NewGuid().ToString("N"));

    /// <summary>Get-C1386LockText: run.lock's text, empty when it is gone.</summary>
    public string LockText() => File.Exists(LockPath) ? Encoding.UTF8.GetString(ReadShared(LockPath)) : "";

    /// <summary>
    /// A file's bytes read as the lock readers read them (FileShare.ReadWrite): File.ReadAllBytes asks for FileShare.Read,
    /// which Windows refuses while the owner holds its write handle.
    /// </summary>
    public static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The run id run.lock names, empty when it is gone or not JSON.</summary>
    public string LockRunId()
    {
        var text = LockText();
        if (!text.TrimStart().StartsWith('{')) return "";
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("runId", out var runId) ? runId.GetString() ?? "" : "";
    }

    /// <summary>Get-C1386ReclaimReason.</summary>
    public string ReclaimReason() => ReclaimField("reason");

    /// <summary>Get-C1389ReclaimNewRunId.</summary>
    public string ReclaimNewRunId() => ReclaimField("newRunId");

    private string ReclaimField(string name)
    {
        if (!File.Exists(ReclaimPath)) return "";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ReclaimPath));
            return document.RootElement.TryGetProperty(name, out var value) ? value.ToString() : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Test-C1386RunIdGone.</summary>
    public bool RunIdGone(string runId)
    {
        var text = LockText();
        return text.Length == 0 || !text.Contains(runId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Test-C1386RunIdKept.</summary>
    public bool RunIdKept(string runId)
    {
        var text = LockText();
        return text.Length > 0 && text.Contains(runId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Test-C1386NoReclaim.</summary>
    public bool NoReclaim() => !File.Exists(ReclaimPath);

    /// <summary>A result's reason and ownership for an assertion message.</summary>
    public static string Detail(NightlyLockResult? result) =>
        result is null ? "result=null" : "ok=" + result.Ok + " reason=" + result.Reason + " owns=" + result.OwnsLock;

    public void Dispose()
    {
        Lock.Dispose();
        Fixture.Dispose();
    }
}
