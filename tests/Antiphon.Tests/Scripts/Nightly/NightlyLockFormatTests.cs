using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S18: the lock records the compiled locks write (NightlyRunLock.cs, ReleaseGateNativeLock.cs) are read
/// unchanged by every reader: NightlyLockRecord (the live lane's run, C# since W2 slice S21, reads the native lock through it)
/// and the C# readers of the qualification set, RcQualificationRecords.Read and RcQualificationStatus (unit 7 of
/// 2026-11-22-remaining-ps1-to-csharp.md; they replaced lib/rc-qualification-status.ps1). Each test writes the records here
/// with the production process table and clock and keeps the owner's handle open as a run does; the readers read them in
/// this process. The bytes are the compact JSON of the same values in field order, the ones
/// "[ordered]@{...} | ConvertTo-Json -Compress" gave. Unit 62 deleted the PowerShell reader, Read-NightlyLockRecord
/// (scripts/lib/nightly-common.ps1), and with it the pwsh and Windows PowerShell 5.1 children that read through it.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyLockFormatTests
{
    private static readonly string[] RunFields = ["pid", "processStartedAt", "startedAt", "runId", "parentPid", "parentStartedAt", "bootId", "hostId"];
    private static readonly string[] NativeFields = ["pid", "processStartedAt", "runId", "lane", "continuationId", "acquiredAt"];
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Test]
    [Timeout(180_000)]
    public void Core_written_locks_read_back_through_the_readers()
    {
        using var fixture = NightlyFixture.Create("nightly-lock-format");
        var processes = new NightlyProcessTable();
        var runId = Guid.NewGuid().ToString("N");
        var state = fixture.PathOf("state");
        var reclaimState = fixture.PathOf("state-reclaim");
        var coord = fixture.PathOf("coord");
        using var runLock = new NightlyRunLock(TimeProvider.System, processes, null, null);
        using var reclaimLock = new NightlyRunLock(TimeProvider.System, processes, null, null);
        var native = new ReleaseGateNativeLock(TimeProvider.System, processes);

        var fresh = runLock.Enter(state, runId);
        fresh.Reason.ShouldBe("acquired");
        runLock.HoldsHandle.ShouldBeTrue();
        var shared = native.Enter(coord, runId, "rc", "continuation-1");
        shared.Reason.ShouldBe("acquired");

        // A dead legacy owner (no boot or host id, its parent gone too) is reclaimed in place; the new owner keeps the
        // shared handle.
        Directory.CreateDirectory(reclaimState);
        const int deadOwner = 2147483000, deadParent = 2147483001;
        processes.IsAlive(deadOwner).ShouldBeFalse();
        processes.IsAlive(deadParent).ShouldBeFalse();
        File.WriteAllText(NightlyRunLock.LockPath(reclaimState), NightlyLockFixture.PlantJson(deadOwner, "dead-run", parentPid: deadParent));
        var reclaimRunId = Guid.NewGuid().ToString("N");
        var reclaimed = reclaimLock.Enter(reclaimState, reclaimRunId);
        reclaimed.Reason.ShouldBe("reclaimed");
        reclaimLock.HoldsHandle.ShouldBeTrue();

        var paths = new Dictionary<string, string>
        {
            ["run"] = NightlyRunLock.LockPath(state),
            ["native"] = ReleaseGateNativeLock.LockPath(coord),
            ["reclaimed"] = NightlyRunLock.LockPath(reclaimState),
        };
        var expected = new Dictionary<string, NightlyLockRecord>
        {
            ["run"] = fresh.Record!,
            ["native"] = shared.Record!,
            ["reclaimed"] = reclaimed.Record!,
        };
        // The record written equals the record read back here.
        foreach (var (name, path) in paths)
            NightlyLockRecord.Read(path)!.RunId.ShouldBe(expected[name].RunId, name);

        // The C# readers, with the production process table: the record's pid and start are this process, matched to the tick.
        var files = new System.IO.Abstractions.FileSystem();
        foreach (var (name, record) in expected)
        {
            var read = RcQualificationRecords.Read(files, Path.GetFullPath(paths[name]));
            read.Status.ShouldBe("ok", "RcQualificationRecords.Read " + name);
            foreach (var field in name == "native" ? NativeFields : RunFields)
            {
                var value = RcQualificationRecords.Member(read.Record, field);
                (RcQualificationRecords.Present(value) ? RcQualificationRecords.Text(value) : null).ShouldBe(record.Value(field), "RcQualificationRecords.Read " + name + "." + field);
            }
            RcQualificationRecords.IsInteger(RcQualificationRecords.Member(read.Record, "pid")).ShouldBeTrue("the pid is a JSON integer " + name);
            var owner = RcQualificationOwner.Observe(read.Record, processes, RcQualificationOwner.ToleranceMilliseconds(new NightlyHostProbe().Platform()));
            (owner.Status + "/" + owner.Reason).ShouldBe("alive/pid-start-match", "RcQualificationOwner.Observe " + name);
        }
        var status = RcQualificationStatus.Observe(new RcQualificationStatusOptions
        {
            CandidateId = "format-candidate", RunId = runId, StateRoot = state, CoordinationRoot = coord,
        }, NightlyHost.Production(TextWriter.Null, TextWriter.Null), files);
        var statusLocks = status["locks"]!;
        ((string)statusLocks["run"]!["relationship"]! + "/" + (string)statusLocks["run"]!["ownerObservation"]!["status"]!).ShouldBe("this-run/alive", "RcQualificationStatus run lock");
        ((string)statusLocks["native"]!["relationship"]! + "/" + (string)statusLocks["native"]!["ownerObservation"]!["status"]!).ShouldBe("this-run/alive", "RcQualificationStatus native lock");
        ((string)statusLocks["native"]!["record"]!["lane"]!).ShouldBe("rc", "RcQualificationStatus native lane");
        ((string)statusLocks["native"]!["record"]!["continuationId"]!).ShouldBe("continuation-1", "RcQualificationStatus native continuation");

        // The bytes are the compact JSON of the record's values in field order, pids as integers, as
        // "[ordered]@{...} | ConvertTo-Json -Compress" gave them for the PowerShell readers (gone in unit 62).
        foreach (var (name, record) in expected)
            Encoding.ASCII.GetString(NightlyLockFixture.ReadShared(paths[name])).ShouldBe(CompactRecord(record, name == "native" ? NativeFields : RunFields),
                "the compact JSON of the same values gives the same bytes: " + name);

        // The reclaim record names the reclaim, both run ids and the dead owner's pid as an integer.
        using (var reclaim = JsonDocument.Parse(File.ReadAllText(Path.Combine(reclaimState, NightlyRunLock.ReclaimRecordName))))
        {
            var root = reclaim.RootElement;
            root.GetProperty("reason").GetString().ShouldBe("dead-owner-legacy");
            root.GetProperty("newRunId").GetString().ShouldBe(reclaimRunId);
            root.GetProperty("previousRunId").GetString().ShouldBe("dead-run");
            var previousPid = root.GetProperty("previousPid");
            previousPid.ValueKind.ShouldBe(JsonValueKind.Number, "the previous pid is a JSON integer");
            previousPid.GetInt64().ShouldBe(deadOwner);
        }

        runLock.Exit(state, runId);
        reclaimLock.Exit(reclaimState, reclaimRunId);
        native.Exit(coord, runId);
        foreach (var path in paths.Values) File.Exists(path).ShouldBeFalse("exit removes " + path);
        File.Exists(Path.Combine(reclaimState, NightlyRunLock.ReclaimRecordName)).ShouldBeTrue("exit keeps lock-reclaim.json");
    }

    /// <summary>
    /// CARD-1396: the owner's handle mode (FileAccess.Write, FileShare.Read) still lets the record be read, and still refuses
    /// a FileShare.None open (the reclaim compare). Share modes hold per handle, so this process's read is any second
    /// reader's; the second-process read through Read-NightlyLockRecord went with that PowerShell reader (unit 62).
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public void C1396_WriteHeldLockReadable()
    {
        using var fixture = NightlyFixture.Create("nightly-lock-held");
        var lockPath = fixture.PathOf("run.lock");
        File.WriteAllText(lockPath, """{"pid":1,"runId":"held-run","parentPid":0}""");
        using var holder = new FileStream(lockPath, FileMode.Open, FileAccess.Write, FileShare.Read);

        var seen = NightlyLockRecord.Read(lockPath)?.RunId ?? "";
        seen.ShouldBe("held-run", "C1396 Write-held lock reads in-process " + seen);
        var noneRefused = false;
        try
        {
            using var exclusive = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            noneRefused = true;
        }
        noneRefused.ShouldBeTrue("C1396 Write-held lock still refuses FileShare.None refused=" + noneRefused);
    }

    // "[ordered]@{ ... } | ConvertTo-Json -Compress" of a record's values: the fields in order, the pids as integers, a
    // missing value as null, the strings escaped only where JSON needs it.
    private static string CompactRecord(NightlyLockRecord record, string[] fields) =>
        "{" + string.Join(",", fields.Select(field =>
        {
            var value = record.Value(field);
            var json = field is "pid" or "parentPid"
                ? int.Parse(value ?? "0", CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                : value is null ? "null" : JsonSerializer.Serialize(value, Relaxed);
            return "\"" + field + "\":" + json;
        })) + "}";
}