using System.Globalization;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1514 S3. The existing heartbeat contract stays closed while progress identity and launch admission are added.
// The C1397 contract is NightlyTestsPlanTests.C1397_RcAntiphonLaunchGroups (in-process since W2-nightly S11).
// W2-nightly S10: the heartbeat is compiled (Antiphon.Nightly/QualificationHeartbeat.cs); the seventeen rows of the
// deleted scripts/test-qualification-supervisor-heartbeat.ps1 (CARD-1606: an injected process table and clock, no live
// process) run here in-process under their names.
[Category("Integration")]
public sealed class RcFullWatchdogHarnessTests
{
    private static readonly string[] HeartbeatAssertions =
    [
        "supervisor monitor CPU does not hold idleMinutes at zero",
        "monitor and heartbeat writer inside the chunk tree are excluded",
        "a busy test host is not a hang while the monitor burns CPU",
        "busy test host keeps idleMinutes at zero",
        "trx growth clears a would-be hang",
        "trx growth resets idleMinutes",
        "chunk-log byte growth clears a would-be hang",
        "chunk-log byte growth resets idleMinutes",
        "plannedSeconds below 10 minutes uses the 10 minute floor",
        "idle for the 10 minute floor is a hang",
        "a 30 minute plan does not hang at the 20 minute default",
        "idle for the planned 30 minutes is a hang",
        "a chunk with no test host is the suspect",
        "groups-planned.json exposes plannedSeconds for the current chunk",
        "heartbeat JSON keeps existing names and adds hangReason plus suspectPids",
        "a reread heartbeat continues the idle clock",
        "heartbeat library is ASCII and does not kill",
    ];

    private static readonly DateTime Origin = DateTime.Parse("2026-10-28T12:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    /// <summary>The compiled heartbeat's source, which the ASCII and no-kill rows read.</summary>
    internal static string HeartbeatSource => Path.Combine(C1514Support.RepoRoot(), "scripts", "lib", "Antiphon.Nightly", "QualificationHeartbeat.cs");

    /// <summary>The process-stopping calls the heartbeat never makes.</summary>
    internal static readonly string[] KillTokens = ["Stop-Process", "taskkill", "TerminateProcess", ".Kill("];

    [Test]
    public void HeartbeatExistingContract()
    {
        var rows = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail) => rows.Add((name, ok, detail));
        static string Describe(QualificationHeartbeatRecord b) =>
            $"hangSuspected={b.HangSuspected} idleMinutes={b.IdleMinutes} cpuSeconds={b.CpuSeconds} budget={b.IdleBudgetMinutes} source={b.IdleBudgetSource} reason={b.HangReason}";

        var witness = MonitorIdleWitness();
        Check(HeartbeatAssertions[0], witness.HangSuspected && witness.IdleMinutes >= 20 && witness.CpuSeconds < 200 && witness.IdleBudgetSource == "default-unknown-group"
            && witness.IdleBudgetMinutes == 20 && witness.SuspectPids.Contains(178996) && !string.IsNullOrWhiteSpace(witness.HangReason), Describe(witness));

        var nested = Stepped([(400, 100), (200, 400), (300, 400), (178996, 400)], new() { [400] = 2.0, [200] = 5400.0, [300] = 5.0, [178996] = 93.0 },
            new() { [400] = 0.01, [200] = 120.0, [300] = 50.0, [178996] = 0.05 }, 20, 400, [200, 300], null);
        Check(HeartbeatAssertions[1], nested.HangSuspected && nested.CpuSeconds < 200 && nested.SuspectPids.Contains(178996) && !nested.SuspectPids.Contains(200)
            && !nested.SuspectPids.Contains(300), $"hang={nested.HangSuspected} cpu={nested.CpuSeconds} pids={string.Join(",", nested.SuspectPids)}");

        var busy = Stepped([(400, 100), (200, 100), (178996, 400)], new() { [400] = 2.0, [200] = 5400.0, [178996] = 93.0 },
            new() { [400] = 0.01, [200] = 120.0, [178996] = 30.0 }, 41, 400, [100, 200], null);
        Check(HeartbeatAssertions[2], !busy.HangSuspected, Describe(busy));
        Check(HeartbeatAssertions[3], busy.IdleMinutes == 0, "idleMinutes=" + busy.IdleMinutes);

        (int, int)[] host = [(178996, 0)];
        var trxBeat = Stepped(host, new() { [178996] = 93.0 }, new() { [178996] = 0.01 }, 19, 178996, [], 120, bumpMinute: 19, bumpTrx: 4);
        Check(HeartbeatAssertions[4], !trxBeat.HangSuspected, Describe(trxBeat));
        Check(HeartbeatAssertions[5], trxBeat.IdleMinutes < 0.001 && trxBeat.Trx == 4, $"idle={trxBeat.IdleMinutes} trx={trxBeat.Trx}");

        var byteBeat = Stepped(host, new() { [178996] = 93.0 }, new() { [178996] = 0.01 }, 19, 178996, [], 120, bumpMinute: 19, bumpTrx: 0, bumpBytes: 2500);
        Check(HeartbeatAssertions[6], !byteBeat.HangSuspected, Describe(byteBeat));
        Check(HeartbeatAssertions[7], byteBeat.IdleMinutes < 0.001 && byteBeat.Bytes == 2500, $"idle={byteBeat.IdleMinutes} bytes={byteBeat.Bytes}");

        var floorEarly = Stepped(host, new() { [178996] = 1.0 }, new() { [178996] = 0.01 }, 9, 178996, [], 120);
        var floorLate = Stepped(host, new() { [178996] = 1.0 }, new() { [178996] = 0.01 }, 10, 178996, [], 120);
        Check(HeartbeatAssertions[8], !floorEarly.HangSuspected && floorEarly.IdleBudgetMinutes == 10 && floorEarly.IdleBudgetSource == "plannedSeconds", Describe(floorEarly));
        Check(HeartbeatAssertions[9], floorLate.HangSuspected, Describe(floorLate));

        var longEarly = Stepped(host, new() { [178996] = 1.0 }, new() { [178996] = 0.01 }, 20, 178996, [], 1800);
        var longLate = Stepped(host, new() { [178996] = 1.0 }, new() { [178996] = 0.01 }, 30, 178996, [], 1800);
        Check(HeartbeatAssertions[10], !longEarly.HangSuspected && longEarly.IdleBudgetMinutes == 30 && longEarly.IdleBudgetSource == "plannedSeconds", Describe(longEarly));
        Check(HeartbeatAssertions[11], longLate.HangSuspected, Describe(longLate));

        var absent = Stepped([(100, 0)], new() { [100] = 10.0 }, new() { [100] = 5.0 }, 20, 400, [100], null);
        Check(HeartbeatAssertions[12], absent.HangSuspected && absent.HangReason.Contains("no test-host", StringComparison.OrdinalIgnoreCase) && absent.SuspectPids.Contains(400),
            $"hang={absent.HangSuspected} reason={absent.HangReason} pids={string.Join(",", absent.SuspectPids)}");

        var temp = Path.Combine(Path.GetTempPath(), "qhb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var plannedPath = Path.Combine(temp, "groups-planned.json");
            var plan = QualificationHeartbeatTests.PlanJson("antiphon", "standalone-heartbeat", "1", ("all-group-105", 600), ("other", 1800));
            File.WriteAllText(plannedPath, plan);
            File.WriteAllText(Path.Combine(temp, "groups-planned-antiphon.json"), plan);
            File.WriteAllText(Path.Combine(temp, "groups-planned-index.json"), QualificationHeartbeatTests.IndexJson("standalone-heartbeat", "1", "antiphon"));
            QualificationHeartbeat.WriteActiveChunkIdentity(temp, "antiphon", "all-group-105", "standalone-heartbeat", "1").Written.ShouldBeTrue();
            var found = QualificationHeartbeat.PlannedCost(plannedPath, "all-group-105").Seconds;
            var missingChunk = QualificationHeartbeat.PlannedCost(plannedPath, "missing-group").Seconds;
            var wrongCase = QualificationHeartbeat.PlannedCost(plannedPath, "All-Group-105").Seconds;
            var missingFile = QualificationHeartbeat.PlannedCost(Path.Combine(temp, "absent.json"), "all-group-105").Seconds;
            File.WriteAllText(Path.Combine(temp, "bad.json"), "{");
            var malformed = QualificationHeartbeat.PlannedCost(Path.Combine(temp, "bad.json"), "all-group-105").Seconds;
            Check(HeartbeatAssertions[13], found == 600 && missingChunk == null && wrongCase == null && missingFile == null && malformed == null,
                $"found={found} missing={missingChunk} case={wrongCase} file={missingFile} bad={malformed}");

            var heartbeatPath = Path.Combine(temp, "supervisor-heartbeat.json");
            QualificationHeartbeat.Write(witness, heartbeatPath);
            var raw = File.ReadAllText(heartbeatPath);
            var missingNames = new[] { "cpuSeconds", "bytes", "idleMinutes", "trx", "newest", "hangSuspected", "hangReason", "suspectPids" }
                .Where(name => !raw.Contains("\"" + name + "\"", StringComparison.Ordinal)).ToArray();
            var round = QualificationHeartbeat.Read(heartbeatPath);
            Check(HeartbeatAssertions[14], missingNames.Length == 0 && round.HangSuspected && round.Newest == "log.diag" && round.SuspectPids.Contains(178996),
                $"missing={string.Join(",", missingNames)} hang={round.HangSuspected} newest={round.Newest}");

            var subjectCpu = round.HostCpu.ToDictionary(s => s.Pid, s => s.CpuSeconds);
            QualificationProcess[] continuedTable = [new(100, 0, 1200), new(200, 100, 7800), new(300, 100, 9), new(400, 100, subjectCpu[400]), new(178996, 400, subjectCpu[178996])];
            var continuedAt = DateTime.Parse(round.ObservedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).AddMinutes(1);
            var continued = QualificationHeartbeat.Update(new QualificationHeartbeatInput
            {
                NowUtc = continuedAt, Processes = continuedTable, ChunkRootPid = 400, ExcludedPids = [100, 200, 300], Previous = round, Trx = 0, Bytes = 1000, Newest = "log.diag",
            });
            Check(HeartbeatAssertions[15], continued.HangSuspected && continued.IdleMinutes >= 21, $"hang={continued.HangSuspected} idle={continued.IdleMinutes}");
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        var bytes = File.ReadAllBytes(HeartbeatSource);
        var text = Encoding.UTF8.GetString(bytes);
        var nonAscii = bytes.Count(b => b > 127);
        var forbidden = KillTokens.Where(token => text.Contains(token, StringComparison.Ordinal)).ToArray();
        Check(HeartbeatAssertions[16], nonAscii == 0 && forbidden.Length == 0, $"nonascii={nonAscii} forbidden={string.Join(",", forbidden)}");

        var report = string.Join("\n", rows.Select(r => (r.Ok ? "PASS " : "FAIL ") + r.Name + (r.Ok ? "" : " - " + r.Detail)));
        string.Join("\n", rows.Select(r => r.Name)).ShouldBe(string.Join("\n", HeartbeatAssertions), report);
        rows.Count(r => !r.Ok).ShouldBe(0, report);
    }

    // Invoke-MonitorIdleWitness: the supervisor (100) and its heartbeat writer (200, 300) burn CPU beside a quiet chunk
    // root (400) whose one test host (178996) is idle, for twenty one-minute polls.
    internal static QualificationHeartbeatRecord MonitorIdleWitness()
    {
        var cpu = new Dictionary<int, double> { [100] = 1000.0, [200] = 5400.0, [300] = 5.0, [400] = 2.0, [178996] = 93.0 };
        QualificationHeartbeatRecord? previous = null;
        QualificationHeartbeatRecord beat = null!;
        for (var minute = 0; minute <= 20; minute++)
        {
            QualificationProcess[] table = [new(100, 0, cpu[100]), new(200, 100, cpu[200]), new(300, 100, cpu[300]), new(400, 100, cpu[400]), new(178996, 400, cpu[178996])];
            beat = QualificationHeartbeat.Update(new QualificationHeartbeatInput
            {
                NowUtc = Origin.AddMinutes(minute), Processes = table, ChunkRootPid = 400, ExcludedPids = [100, 200, 300], Previous = previous, Trx = 0, Bytes = 1000,
                Newest = "log.diag", PlannedSeconds = null,
            });
            previous = beat;
            if (minute < 20)
            {
                cpu[100] += 10;
                cpu[200] += 120;
                cpu[300] += 0.2;
                cpu[400] += 0.01;
                cpu[178996] += 0.05;
            }
        }
        return beat;
    }

    // Invoke-SteppedHeartbeat: one poll a minute for minutes + 1 polls, each process gaining its CPU between polls; at
    // bumpMinute the TRX count (and, when not 0, the chunk-log bytes) change.
    internal static QualificationHeartbeatRecord Stepped((int Id, int Parent)[] rows, Dictionary<int, double> cpu, Dictionary<int, double> gain, int minutes,
        int chunkRootPid, int[] excludedPids, double? plannedSeconds, double initialTrx = 0, long initialBytes = 1000, int bumpMinute = -1, double bumpTrx = 0, long bumpBytes = 0)
    {
        var trx = initialTrx;
        var bytes = initialBytes;
        QualificationHeartbeatRecord? previous = null;
        QualificationHeartbeatRecord beat = null!;
        for (var minute = 0; minute <= minutes; minute++)
        {
            if (minute == bumpMinute)
            {
                trx = bumpTrx;
                if (bumpBytes != 0) bytes = bumpBytes;
            }
            var table = rows.Select(r => new QualificationProcess(r.Id, r.Parent, cpu[r.Id])).ToArray();
            beat = QualificationHeartbeat.Update(new QualificationHeartbeatInput
            {
                NowUtc = Origin.AddMinutes(minute), Processes = table, ChunkRootPid = chunkRootPid, ExcludedPids = excludedPids, Previous = previous, Trx = trx,
                Bytes = bytes, Newest = "log.diag", PlannedSeconds = plannedSeconds,
            });
            previous = beat;
            if (minute < minutes)
                foreach (var row in rows) cpu[row.Id] += gain[row.Id];
        }
        return beat;
    }
}
