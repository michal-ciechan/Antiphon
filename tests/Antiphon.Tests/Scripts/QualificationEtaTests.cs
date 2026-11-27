using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-2093: the shared ETA against scheduled TRX fixtures with a frozen observation time.
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

    internal static void WriteFixtureTrx(MockFileSystem files, string directory, string chunk, double duration)
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

    /// <summary>Every file under the root with its bytes and write time: the tree a read must leave as it was.</summary>
    internal static string TreeStamp(MockFileSystem files, string root) =>
        string.Join("\n", files.Directory.GetFiles(root).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => files.Path.GetFileName(path) + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(files.File.ReadAllBytes(path)))
                + ":" + files.File.GetLastWriteTimeUtc(path).Ticks));


    [Test]
    public void First_match_is_damped_and_active_chunk_counts_as_unfinished()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        var previous = files.Path.Combine(root, "previous");
        WriteFixtureSchedule(files, root, "fixture", ["a", "b"]);
        WriteFixtureSchedule(files, previous, "earlier", ["a", "b"]);
        WriteFixtureTrx(files, previous, "a", 20);
        WriteFixtureTrx(files, previous, "b", 40);
        var active = files.Path.Combine(root, "active-chunk.json");
        files.File.WriteAllText(active, """{"runId":"fixture","state":"Active","suite":"suite","chunk":"a"}""");

        Observe(files, ["--timing-root", root, "--previous-run-log-dir", previous]).ShouldBe([
            "ETA outstanding=1 running=1 completed=0 unpriced=0 pointSeconds=60 p50=2026-11-10T12:01:00.000Z p95=unavailable confidence=provisional lowerBound=false",
        ]);

        WriteFixtureTrx(files, root, "a", 30);
        var before = TreeStamp(files, root);
        // A raw ratio of 1.5 receives 1/21 of the weight. The stale active record refers to a finished chunk.
        Observe(files, ["--timing-root", root, "--previous-run-log-dir", previous]).ShouldBe([
            "ETA outstanding=1 running=0 completed=1 unpriced=0 pointSeconds=40.952 p50=2026-11-10T12:00:40.952Z p95=unavailable confidence=provisional lowerBound=false",
        ]);
        TreeStamp(files, root).ShouldBe(before);
    }

    [Test]
    public void One_dominant_chunk_keeps_the_sample_provisional()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        var previous = files.Path.Combine(root, "previous");
        var shortChunks = Enumerable.Range(0, 20).Select(index => "a" + index).ToArray();
        foreach (var chunk in shortChunks)
        {
            WriteFixtureTrx(files, root, chunk, 1);
            WriteFixtureTrx(files, previous, chunk, 1);
        }
        WriteFixtureTrx(files, root, "dominant", 1000);
        WriteFixtureTrx(files, previous, "dominant", 1000);
        WriteFixtureTrx(files, previous, "b", 60);
        WriteFixtureSchedule(files, root, "fixture", [.. shortChunks, "dominant", "b"]);
        WriteFixtureSchedule(files, previous, "earlier", [.. shortChunks, "dominant", "b"]);

        Observe(files, ["--timing-root", root, "--previous-run-log-dir", previous]).ShouldBe([
            "ETA outstanding=1 running=0 completed=21 unpriced=0 pointSeconds=60 p50=2026-11-10T12:01:00.000Z p95=unavailable confidence=provisional lowerBound=false",
        ]);
    }

    [Test]
    public void A_tiny_historical_duration_does_not_blow_up_the_finish_interval()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        var previous = files.Path.Combine(root, "previous");
        var equal = Enumerable.Range(0, 20).Select(index => "a" + index).ToArray();
        foreach (var chunk in equal)
        {
            WriteFixtureTrx(files, root, chunk, 10);
            WriteFixtureTrx(files, previous, chunk, 10);
        }
        WriteFixtureTrx(files, root, "tiny", 20);
        WriteFixtureTrx(files, previous, "tiny", 0.01);
        WriteFixtureTrx(files, previous, "b", 100);
        WriteFixtureSchedule(files, root, "fixture", [.. equal, "tiny", "b"]);
        WriteFixtureSchedule(files, previous, "earlier", [.. equal, "tiny", "b"]);

        Observe(files, ["--timing-root", root, "--previous-run-log-dir", previous]).ShouldBe([
            "ETA outstanding=1 running=0 completed=21 unpriced=0 pointSeconds=104.997 p50=2026-11-10T12:01:44.997Z "
            + "p95=2026-11-10T12:02:02.281Z confidence=matched lowerBound=false",
        ]);
    }

    [Test]
    public void No_usable_remaining_history_reports_unavailable_and_names_the_unknowns()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        WriteFixtureSchedule(files, root, "fixture", ["a"]);
        var lines = Observe(files, ["--timing-root", root]);
        lines[0].ShouldBe("ETA outstanding=1 running=0 completed=0 unpriced=1 pointSeconds=unavailable p50=unavailable "
            + "p95=unavailable confidence=unavailable lowerBound=true");
        lines[1].ShouldStartWith("UNVERIFIED Previous rc run not found;");
        lines[2].ShouldBe("UNVERIFIED rc ETA is a lower bound: no previous timing for 1 remaining chunk(s): suite-a.");
        lines.Length.ShouldBe(3);
    }

    [Test]
    public void Command_refuses_missing_or_invalid_inputs()
    {
        var files = new MockFileSystem();
        var root = NewRoot(files);
        var log = files.Path.Combine(root, "copied-logs");
        files.Directory.CreateDirectory(log);
        Refused(files, ["--timing-root", files.Path.Combine(root, "absent")]).ShouldStartWith("qualification-eta: timing-root-missing ");
        Refused(files, ["--timing-root", root]).ShouldContain("supply --run-log-dir");
        files.File.WriteAllText(files.Path.Combine(log, "dispatch-schedule.json"), "{");
        Refused(files, ["--timing-root", root, "--run-log-dir", log]).ShouldContain("invalid-json-object");
        WriteFixtureSchedule(files, log, "fixture", ["a", "a"]);
        Refused(files, ["--timing-root", root, "--run-log-dir", log]).ShouldContain("invalid chunk identities");

        Should.Throw<NightlyUsageException>(() => QualificationEtaOptions.Parse([])).Message.ShouldBe("missing --timing-root");
        Should.Throw<NightlyUsageException>(() => QualificationEtaOptions.Parse(["--timing-root", root, "--now-utc", "noon"])).Message.ShouldContain("--now-utc");
        Should.Throw<NightlyUsageException>(() => QualificationEtaOptions.Parse(["--TimingRoot", root]));
    }

    private static string[] Observe(MockFileSystem files, string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var host = Host(output, error, new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        QualificationEta.Run(QualificationEtaOptions.Parse([..args, "--now-utc", "2026-11-10T12:00:00Z"]), host, files).ShouldBe(0, error.ToString());
        error.ToString().ShouldBeEmpty();
        return output.ToString().Trim().Split(Environment.NewLine);
    }

    private static string Refused(MockFileSystem files, string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        QualificationEta.Run(QualificationEtaOptions.Parse(args), Host(output, error, new FakeTimeProvider(NowUtc)), files).ShouldBe(1);
        output.ToString().ShouldBeEmpty();
        return error.ToString().Trim().Split(Environment.NewLine).ShouldHaveSingleItem();
    }
}
