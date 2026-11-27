using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1842 S4 witnesses, in-process. The qualification-eta command (QualificationEta.Run, QualificationEta.Read)
/// replaced scripts/qualification-eta.ps1 and these cases replaced its driver scripts/test-qualification-eta.ps1 (plan
/// 2026-11-22-remaining-ps1-to-csharp.md, unit 6). The timing roots are in a MockFileSystem; the clock is a
/// FakeTimeProvider, or --now-utc as the driver passed it.
/// </summary>
[Category("Unit")]
public sealed class QualificationEtaTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Scheduled_trx_history_gives_the_hand_calculated_point_and_percentiles()
    {
        var files = new MockFileSystem();
        var releases = NewRoot(files);
        var logs = files.Path.Combine(releases, "candidates", "rc-2026-11-10-0123456789ab", "logs");
        const string runId = "01234567-89ab-4cde-8f01-23456789abcd";
        var root = files.Path.Combine(logs, "timing-" + runId);
        var live = files.Path.Combine(logs, "2026-11-10-1100-01234567");
        var previous = files.Path.Combine(logs, "2026-11-09-1100-76543210");
        var finished = Enumerable.Range(0, 20).Select(index => "a" + index).ToArray();
        files.Directory.CreateDirectory(root);
        WritePlan(files, root, "suite", """{"chunk":"b","plannedSeconds":10000},{"chunk":"unknown","plannedSeconds":10000}""");
        for (var index = 0; index < finished.Length; index++)
        {
            WriteFixtureTrx(files, previous, finished[index], 10);
            WriteFixtureTrx(files, live, finished[index], index < 10 ? 10 : 30);
        }
        WriteFixtureTrx(files, previous, "b", 100);
        WriteFixtureTrx(files, previous, "removed", 900);
        WriteFixtureSchedule(files, previous, "previous", [.. finished, "b", "removed"]);
        WriteFixtureSchedule(files, live, runId, [.. finished, "b", "unknown"]);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = QualificationEtaOptions.Parse(["--timing-root", root]);
        QualificationEta.Run(options, Host(output, error, new FakeTimeProvider(NowUtc)), files).ShouldBe(0, error.ToString());
        error.ToString().ShouldBeEmpty();
        var lines = output.ToString().Trim().Split(Environment.NewLine);
        // Raw scale 400/200 = 2. With 20 matches and 20 prior matches at scale 1, the scale is 1.5.
        // The standard error of the 20 equal-weight ratios (ten 1s and ten 3s) is 1/sqrt(19).
        // The known 100 seconds therefore project 150 seconds and 150 + 164.5/sqrt(19) = 187.739 seconds.
        lines[0].ShouldBe("ETA outstanding=2 running=0 completed=20 unpriced=1 pointSeconds=150 "
            + "p50=2026-11-10T12:02:30.000Z p95=2026-11-10T12:03:07.739Z confidence=matched lowerBound=true");
        lines[1].ShouldBe("UNVERIFIED rc ETA is a lower bound: no previous timing for 1 remaining chunk(s): suite-unknown.");
    }

    internal static void WriteFixtureSchedule(MockFileSystem files, string directory, string runId, string[] chunks)
    {
        files.Directory.CreateDirectory(directory);
        files.File.WriteAllText(files.Path.Combine(directory, "dispatch-schedule.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            runId,
            preparationComplete = true,
            entries = chunks.Select(chunk => new { suite = "suite", chunk }),
        }));
    }

    internal static void WriteFixtureTrx(MockFileSystem files, string directory, string chunk, int duration)
    {
        files.Directory.CreateDirectory(directory);
        var start = NowUtc.AddHours(-1);
        files.File.WriteAllText(files.Path.Combine(directory, "suite-" + chunk + ".trx"), $"""
            <TestRun xmlns="{WatchQual.TrxNamespace}">
              <Times start="{start:O}" finish="{start.AddSeconds(duration):O}" />
              <ResultSummary outcome="Completed" />
            </TestRun>
            """);
    }

    private static readonly Regex EtaPattern = new(
        @"^ETA outstanding=(\d+) running=(\d+) completed=(\d+) unpriced=(\d+) pointSeconds=([0-9]+(?:\.[0-9]+)?) p50=(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z) p95=(unavailable|\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)$");

    /// <summary>One parsed ETA line.</summary>
    internal sealed record EtaLine(int Outstanding, int Running, int Completed, int Unpriced, double PointSeconds, string P50, string P95, string Line);

    /// <summary>A host over fakes with these writers and clock; a sleep it was not given fails the test.</summary>
    internal static NightlyHost Host(StringWriter output, StringWriter error, TimeProvider clock, Action<TimeSpan>? sleep = null) => new()
    {
        Clock = clock,
        Git = new FakeGit().Run,
        Launcher = new RecordingLauncher(),
        Processes = new FakeProcessTable(),
        Probe = new FakeHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Sleep = sleep ?? (_ => throw new InvalidOperationException("no sleep expected")),
    };

    /// <summary>The ETA line's fields; fails the test when the line does not have the pinned shape.</summary>
    internal static EtaLine Parse(string line)
    {
        var match = EtaPattern.Match(line);
        match.Success.ShouldBeTrue("unparsed " + line);
        return new EtaLine(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value),
            double.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture), match.Groups[6].Value, match.Groups[7].Value, line);
    }

    /// <summary>A fresh timing root in the file system.</summary>
    internal static string NewRoot(MockFileSystem files)
    {
        var root = files.Path.Combine(files.Path.GetTempPath(), "eta-" + Guid.NewGuid().ToString("N"));
        files.Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>groups-planned-&lt;suite&gt;.json with these group objects.</summary>
    internal static void WritePlan(MockFileSystem files, string root, string suite, string groups) =>
        files.File.WriteAllText(files.Path.Combine(root, "groups-planned-" + suite + ".json"),
            "{\"version\":1,\"suite\":\"" + suite + "\",\"runId\":\"fixture\",\"attempt\":\"1\",\"groups\":[" + groups + "]}");

    /// <summary>&lt;suite&gt;-&lt;chunk&gt;.timing.json; an empty started, completed or wall is left out.</summary>
    internal static void WriteTiming(MockFileSystem files, string root, string suite, string chunk, string phase, string started, string completed, string wall)
    {
        var parts = new List<string> { "\"suite\":\"" + suite + "\"", "\"chunk\":\"" + chunk + "\"", "\"lastPhase\":\"" + phase + "\"" };
        if (started.Length > 0) parts.Add("\"startedAt\":\"" + started + "\"");
        if (completed.Length > 0) parts.Add("\"completedAt\":\"" + completed + "\"");
        if (wall.Length > 0) parts.Add("\"runWallSeconds\":" + wall);
        files.File.WriteAllText(files.Path.Combine(root, suite + "-" + chunk + ".timing.json"), "{" + string.Join(",", parts) + "}");
    }

    private static void WriteActive(MockFileSystem files, string root, string state, string suite, string chunk) =>
        files.File.WriteAllText(files.Path.Combine(root, "active-chunk.json"),
            "{\"format\":\"antiphon-qualification-active-chunk-v1\",\"state\":\"" + state + "\",\"generation\":1,\"runId\":\"fixture\",\"attempt\":\"1\",\"suite\":\""
            + suite + "\",\"chunk\":\"" + chunk + "\",\"planFile\":\"groups-planned-" + suite + ".json\"}");

    /// <summary>Every file under the root with its bytes and write time: the tree a read must leave as it was.</summary>
    internal static string TreeStamp(MockFileSystem files, string root) =>
        string.Join("\n", files.Directory.GetFiles(root).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => files.Path.GetFileName(path) + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(files.File.ReadAllBytes(path)))
                + ":" + files.File.GetLastWriteTimeUtc(path).Ticks));

    // The command as the driver ran it (-NowUtc 2026-11-10T12:00:00Z): exactly one stdout line, nothing on stderr.
    private static EtaLine Eta(MockFileSystem files, string root, string costPath = "")
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = new List<string> { "--timing-root", root, "--now-utc", "2026-11-10T12:00:00Z" };
        if (costPath.Length > 0) arguments.AddRange(new[] { "--class-cost-path", costPath });
        var options = QualificationEtaOptions.Parse(arguments);
        QualificationEta.Run(options, Host(output, error, new FakeTimeProvider(DateTimeOffset.UnixEpoch)), files).ShouldBe(0, error.ToString());
        error.ToString().ShouldBeEmpty();
        var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
        lines.Length.ShouldBe(1, string.Join(" | ", lines));
        return Parse(lines[0]);
    }

    private static void Same(EtaLine eta, int outstanding, int running, int completed, int unpriced, double point, string p50, string p95, string label)
    {
        var ok = eta.Outstanding == outstanding && eta.Running == running && eta.Completed == completed && eta.Unpriced == unpriced
            && Math.Abs(eta.PointSeconds - point) < 0.001 && eta.P50 == p50 && eta.P95 == p95;
        ok.ShouldBeTrue(label + " " + eta.Line);
    }

    [Test]
    public void OutstandingSumsPlannedSeconds()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        // First plan row wins. The repeated chunk, the singleton copy, and the index must not be summed again.
        WritePlan(files, root, "antiphon", """{"chunk":"a","plannedSeconds":100},{"chunk":"a","plannedSeconds":99999},{"chunk":"b","plannedSeconds":200},{"chunk":"c","plannedSeconds":300},{"chunk":"d","classes":["Alpha","Beta"]},{"chunk":"e","classes":["Ghost"]}""");
        files.File.WriteAllText(files.Path.Combine(root, "groups-planned.json"), """{"version":1,"suite":"antiphon","groups":[{"chunk":"trap","plannedSeconds":999999}]}""");
        files.File.WriteAllText(files.Path.Combine(root, "groups-planned-index.json"), """{"version":1,"groups":[{"chunk":"trap","plannedSeconds":999999}]}""");
        var costs = files.Path.Combine(root, "class-costs.json");
        files.File.WriteAllText(costs, """{"format":"antiphon-nightly-class-costs-v1","classes":{"Alpha":{"nodes":2,"seconds":10},"Beta":{"nodes":1,"seconds":15}}}""");
        WriteActive(files, root, "Idle", "antiphon", "a");
        var eta = Eta(files, root, costs);
        // 100+200+300 plus class seconds 10+15. nodes is not the divisor. Ghost stays unpriced, not zero.
        Same(eta, 5, 0, 0, 1, 625, "2026-11-10T12:10:25.000Z", "unavailable", "outstanding sum");
    }

    [Test]
    public void RunningSubtractsElapsed()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        WritePlan(files, root, "antiphon", """{"chunk":"run-1","plannedSeconds":1000},{"chunk":"later","plannedSeconds":100}""");
        WriteTiming(files, root, "antiphon", "run-1", "run", "2026-11-10T11:58:20.000Z", "", "1");
        WriteActive(files, root, "Active", "antiphon", "run-1");
        Same(Eta(files, root), 1, 1, 0, 0, 1000, "2026-11-10T12:16:40.000Z", "unavailable", "elapsed subtracted");

        var missing = NewRoot(files);
        WritePlan(files, missing, "antiphon", """{"chunk":"run-1","plannedSeconds":400}""");
        WriteTiming(files, missing, "antiphon", "run-1", "run", "", "", "1");
        WriteActive(files, missing, "Active", "antiphon", "run-1");
        Same(Eta(files, missing), 0, 1, 0, 0, 400, "2026-11-10T12:06:40.000Z", "unavailable", "missing start keeps the plan");

        var over = NewRoot(files);
        WritePlan(files, over, "antiphon", """{"chunk":"run-1","plannedSeconds":100},{"chunk":"later","plannedSeconds":50}""");
        WriteTiming(files, over, "antiphon", "run-1", "run", "2026-11-10T11:56:40.000Z", "", "1");
        WriteActive(files, over, "Active", "antiphon", "run-1");
        Same(Eta(files, over), 1, 1, 0, 0, 50, "2026-11-10T12:00:50.000Z", "unavailable", "overrun stays at zero");
    }

    [Test]
    public void CompletedDoesNotAdd()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        WritePlan(files, root, "antiphon", """{"chunk":"stay","plannedSeconds":100},{"chunk":"stale","plannedSeconds":30},{"chunk":"timed","plannedSeconds":500},{"chunk":"done","plannedSeconds":80},{"chunk":"stopped","plannedSeconds":70},{"chunk":"torn","plannedSeconds":60},{"chunk":"marked","plannedSeconds":40}""");
        WriteTiming(files, root, "antiphon", "stale", "run", "2026-11-10T11:00:00.000Z", "", "30");
        WriteTiming(files, root, "antiphon", "timed", "timeout", "2026-11-10T11:00:00.000Z", "2026-11-10T11:30:00.000Z", "500");
        WriteTiming(files, root, "antiphon", "done", "complete", "2026-11-10T11:00:00.000Z", "2026-11-10T11:10:00.000Z", "80");
        WriteTiming(files, root, "antiphon", "stopped", "cancelled", "2026-11-10T11:00:00.000Z", "2026-11-10T11:10:00.000Z", "70");
        WriteTiming(files, root, "antiphon", "torn", "teardown", "2026-11-10T11:00:00.000Z", "2026-11-10T11:10:00.000Z", "60");
        WriteTiming(files, root, "antiphon", "marked", "run", "2026-11-10T11:00:00.000Z", "2026-11-10T11:10:00.000Z", "40");
        // The active file still names the timeout chunk. Settlement wins over Active.
        WriteActive(files, root, "Active", "antiphon", "timed");
        Same(Eta(files, root), 2, 0, 5, 0, 130, "2026-11-10T12:02:10.000Z", "unavailable", "settled chunks add nothing");
    }

    [Test]
    public void FewWallsLeaveP95Unavailable()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        var groups = """{"chunk":"left","plannedSeconds":610}""";
        for (var i = 1; i <= 6; i++) groups += ",{\"chunk\":\"w" + i + "\",\"plannedSeconds\":100}";
        groups += """,{"chunk":"short","plannedSeconds":10}""";
        WritePlan(files, root, "antiphon", groups);
        for (var i = 1; i <= 6; i++)
            WriteTiming(files, root, "antiphon", "w" + i, "complete", "2026-11-10T11:00:00.000Z", "2026-11-10T11:02:00.000Z", "100");
        WriteTiming(files, root, "antiphon", "short", "complete", "2026-11-10T11:00:00.000Z", "2026-11-10T11:02:00.000Z", "100");
        var eta = Eta(files, root);
        // Seven walls. p50 stays the unscaled remainder. The headline is 700/610 of that remainder, not the unweighted mean.
        Same(eta, 1, 0, 7, 0, 700, "2026-11-10T12:10:10.000Z", "unavailable", "seven walls");
        eta.P95.ShouldBe("unavailable", "p95 token");
    }

    [Test]
    public void EightWallsSetP50AndP95()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        // Ratios sorted: 0.5, 1, 1, 1, 2, 2, 4, 8. N=8 indexes 3 and 7. Sum wall 1400 / sum planned 800 = 1.75, not the unweighted mean 2.4375.
        WritePlan(files, root, "antiphon", """{"chunk":"left","plannedSeconds":800},{"chunk":"r1","plannedSeconds":200},{"chunk":"r2","plannedSeconds":100},{"chunk":"r3","plannedSeconds":100},{"chunk":"r4","plannedSeconds":100},{"chunk":"r5","plannedSeconds":100},{"chunk":"r6","plannedSeconds":100},{"chunk":"r7","plannedSeconds":50},{"chunk":"r8","plannedSeconds":50}""");
        var walls = new[] { "100", "100", "100", "100", "200", "200", "200", "400" };
        for (var i = 0; i < walls.Length; i++)
            WriteTiming(files, root, "antiphon", "r" + (i + 1), "complete", "2026-11-10T11:00:00.000Z", "2026-11-10T11:02:00.000Z", walls[i]);
        var eta = Eta(files, root);
        Same(eta, 1, 0, 8, 0, 1400, "2026-11-10T12:13:20.000Z", "2026-11-10T13:46:40.000Z", "eight walls");
        eta.PointSeconds.ShouldNotBe(1950, "headline must not be the unweighted mean");
    }

    [Test]
    public void RefusesTimingRootWrites()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        WritePlan(files, root, "antiphon", """{"chunk":"only","plannedSeconds":10}""");
        var before = TreeStamp(files, root);
        var eta = Eta(files, root);
        Same(eta, 1, 0, 0, 0, 10, "2026-11-10T12:00:10.000Z", "unavailable", "read line");
        TreeStamp(files, root).ShouldBe(before, "timing root changed");
        files.File.Exists(files.Path.Combine(root, "eta.json")).ShouldBeFalse("eta.json was written");
    }

    // The command's own contract: the host clock without --now-utc, exit 1 with one stderr line for a refusal, usage errors.
    [Test]
    public void CommandUsesTheHostClockAndRefusesWithExitOne()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        WritePlan(files, root, "antiphon", """{"chunk":"only","plannedSeconds":10},{"chunk":"priced-by-class","classes":["Alpha"]}""");
        var output = new StringWriter();
        var error = new StringWriter();
        QualificationEta.Run(QualificationEtaOptions.Parse(["--timing-root", root]), Host(output, error, new FakeTimeProvider(new DateTimeOffset(NowUtc))), files).ShouldBe(0);
        Parse(output.ToString().Trim()).P50.ShouldBe("2026-11-10T12:00:10.000Z", "the host clock is the default now");
        error.ToString().ShouldBeEmpty();

        string Refusal(string timingRoot, string costPath = "")
        {
            var refusedOut = new StringWriter();
            var refusedError = new StringWriter();
            var options = new QualificationEtaOptions { TimingRoot = timingRoot, NowUtc = NowUtc, ClassCostPath = costPath };
            QualificationEta.Run(options, Host(refusedOut, refusedError, new FakeTimeProvider(new DateTimeOffset(NowUtc))), files).ShouldBe(1, timingRoot);
            refusedOut.ToString().ShouldBeEmpty("a refusal prints no ETA");
            var lines = refusedError.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();
            lines.Length.ShouldBe(1, "one stderr line");
            lines[0].ShouldStartWith("qualification-eta: ");
            return lines[0];
        }

        Refusal(files.Path.Combine(root, "absent")).ShouldContain("timing-root-missing");
        var costs = files.Path.Combine(root, "costs.json");
        files.File.WriteAllText(costs, """{"format":"another-format","classes":{}}""");
        Refusal(root, costs).ShouldBe("qualification-eta: class-cost-format", "the cost file is read when a row needs it");
        var noSuite = NewRoot(files);
        files.File.WriteAllText(files.Path.Combine(noSuite, "groups-planned-x.json"), """{"groups":[{"chunk":"a","plannedSeconds":1}]}""");
        Refusal(noSuite).ShouldBe("qualification-eta: plan-suite groups-planned-x.json");
        var twice = NewRoot(files);
        WriteTiming(files, twice, "antiphon", "a", "run", "", "", "1");
        files.File.WriteAllText(files.Path.Combine(twice, "copy.timing.json"), """{"suite":"antiphon","chunk":"a","lastPhase":"run"}""");
        Refusal(twice).ShouldStartWith("qualification-eta: duplicate-timing ");

        Should.Throw<NightlyUsageException>(() => QualificationEtaOptions.Parse([])).Message.ShouldBe("missing --timing-root");
        Should.Throw<NightlyUsageException>(() => QualificationEtaOptions.Parse(["--timing-root", root, "--now-utc", "noon"])).Message.ShouldContain("--now-utc");
        Should.Throw<NightlyUsageException>(() => QualificationEtaOptions.Parse(["--TimingRoot", root]));
    }
}
