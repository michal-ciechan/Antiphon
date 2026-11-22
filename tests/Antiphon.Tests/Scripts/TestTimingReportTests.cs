using System.Collections;
using System.Collections.Specialized;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1313 S5. The report reads durable history and never changes a selection. In-process since 2026-11-22: the
// test-timing-report command (TestTimings.Run) replaced scripts/test-timing-report.ps1, and the select-test-rosters command
// (TestSelection.Run, unit 3) calls TestTimings.Report and AddEstimates (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 2).
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingReportTests : CheckpointTestBase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Test]
    public void C1313_ComparableHistory()
    {
        var root = Fresh("history");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var events = new List<object>();
        for (var value = 1; value <= 10; value++)
            events.Add(Sample("main", value, started.AddMinutes(value), value, candidateSha: "sha-" + value));
        events.Add(Sample("main", 1, started, 1, membership: "other-mem"));
        events.Add(Sample("main", 1, started, 2, os: "windows"));
        events.Add(Sample("main", 1, started, 3, runtime: "8.0.0"));
        events.Add(Sample("main", 1, started, 4, concurrency: 8));
        events.Add(Sample("main", 1, started, 5, host: "host-b"));
        for (var value = 1; value <= 9; value++)
            events.Add(Sample("short", value, started.AddMinutes(value), value));
        foreach (var value in new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1 })
            events.Add(Sample("zeros", value, started.AddMinutes(events.Count), events.Count));
        WriteEvents(root, events);

        using var doc = Report(root);
        doc.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("antiphon-test-timing-report-v1");
        var main = One(doc, "main", "mem", host: "host-a", os: "linux", runtime: "9.0.0", concurrency: 1);
        main.GetProperty("sampleCount").GetInt32().ShouldBe(10, "comparable-series");
        main.GetProperty("median").GetDouble().ShouldBe(5.5);
        main.GetProperty("p90").GetDouble().ShouldBe(9);
        main.GetProperty("latest").GetDouble().ShouldBe(10);
        main.GetProperty("priorFiveMedian").GetDouble().ShouldBe(3);
        main.GetProperty("latestFiveMedian").GetDouble().ShouldBe(8);
        main.GetProperty("absoluteChange").GetDouble().ShouldBe(5);
        Math.Abs(main.GetProperty("percentageChange").GetDouble() - (500d / 3d)).ShouldBeLessThan(0.001);
        main.GetProperty("insufficientHistory").GetBoolean().ShouldBeFalse();
        main.GetProperty("metric").GetString().ShouldBe("inclusive-case-window");
        Groups(doc).Count(item => item.GetProperty("setId").GetString() == "main" && item.GetProperty("sampleCount").GetInt32() == 1)
            .ShouldBe(5, "comparable-series");

        var brief = One(doc, "short", "mem");
        brief.GetProperty("insufficientHistory").GetBoolean().ShouldBeTrue();
        Missing(brief, "absoluteChange").ShouldBeTrue();
        Missing(brief, "percentageChange").ShouldBeTrue();

        var zeros = One(doc, "zeros", "mem");
        zeros.GetProperty("priorFiveMedian").GetDouble().ShouldBe(0);
        Missing(zeros, "percentageChange").ShouldBeTrue();
    }

    [Test]
    public void C1313_AttemptCost()
    {
        var root = Fresh("cost");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        WriteEvents(root,
        [
            Cost("e1", 10, "passed", "ordinary", "build-a", 5, started, 1),
            Cost("e1", 10, "passed", "ordinary", "build-a", 5, started, 1),
            Cost("e2", 20, "passed", "ordinary", "build-a", 5, started.AddMinutes(1), 2),
            Cost("e3", 30, "passed", "ordinary", "build-b", 4, started.AddMinutes(2), 3),
            Cost("e4", 100, "failed", "ordinary", "build-a", 5, started.AddMinutes(3), 4),
            Cost("e5", 50, "passed", "retry", "build-a", 5, started.AddMinutes(4), 5),
        ]);
        using var doc = Report(root);
        var group = One(doc, "cost", "mem");
        group.GetProperty("median").GetDouble().ShouldBe(20, "attempt-cost");
        group.GetProperty("p90").GetDouble().ShouldBe(30, "attempt-cost");
        group.GetProperty("sampleCount").GetInt32().ShouldBe(3);
        group.GetProperty("totalAttemptedCost").GetDouble().ShouldBe(219, "attempt-cost");
    }

    [Test]
    public void C1313_EstimatesNeverSelect()
    {
        var root = Fresh("estimates");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var events = new List<object>();
        for (var value = 1; value <= 10; value++)
            events.Add(Sample("A", value, started.AddMinutes(value), value));
        for (var value = 1; value <= 10; value++)
            events.Add(Sample("B", 100000, started.AddMinutes(value), value));
        for (var value = 1; value <= 4; value++)
            events.Add(Sample("D", value, started.AddMinutes(value), value));
        WriteEvents(root, events);
        var selection = Path.Combine(Fresh("selection"), "selection.json");
        File.WriteAllText(selection, """
            {"digest":"fixed-digest","activation":"unqualified","executionEvidence":false,"selectedSets":[{"id":"A"},{"id":"B"},{"id":"C"},{"id":"D"}]}
            """);

        using var doc = Report(root, selection);
        doc.RootElement.GetProperty("digest").GetString().ShouldBe("fixed-digest");
        doc.RootElement.GetProperty("activation").GetString().ShouldBe("unqualified");
        doc.RootElement.GetProperty("executionEvidence").GetBoolean().ShouldBeFalse();
        var ids = doc.RootElement.GetProperty("selectedSets").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        ids.ShouldBe(new[] { "A", "B", "C", "D" }, "budget-never-omits");
        doc.RootElement.GetProperty("budgetObservations").EnumerateArray().Select(item => item.GetString()).ShouldContain("budget-observed");
        Estimate(doc, "A").GetProperty("unbudgeted").GetBoolean().ShouldBeFalse();
        Estimate(doc, "A").GetProperty("provisional").GetBoolean().ShouldBeFalse();
        Estimate(doc, "C").GetProperty("unbudgeted").GetBoolean().ShouldBeTrue();
        Estimate(doc, "D").GetProperty("provisional").GetBoolean().ShouldBeTrue();
        Estimate(doc, "D").GetProperty("sampleCount").GetInt32().ShouldBe(4);
        // The selection's own members keep their order; the estimates follow them.
        doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray()
            .ShouldBe(new[] { "digest", "activation", "executionEvidence", "selectedSets", "estimates", "budgetObservations" });
    }

    /// <summary>
    /// The select-test-rosters command's call (TestTimings.Report by roster, then AddEstimates on its ordered selection): a roster
    /// series is host wall time over roster, run and chunk sets; the copy keeps every entry, drops stale estimates and
    /// observations, and never changes a set or the digest.
    /// </summary>
    [Test]
    public void C1313_RosterEstimatesForTheSelector()
    {
        var root = Fresh("roster");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var events = new List<object>();
        for (var value = 1; value <= 6; value++)
            events.Add(Sample("R", value, started.AddMinutes(value), value, setKind: "roster", wall: value * 10));
        events.Add(Sample("K", 1, started, 1, setKind: "class"));
        WriteEvents(root, events);

        var report = TestTimings.Report([root], "roster");
        var group = report.Groups.ShouldHaveSingleItem();
        (group.SetId, group.SetKind, group.Metric, group.SampleCount).ShouldBe(("R", "roster", "host-wall", 6));
        (group.Median, group.P90, group.Latest, group.Provisional, group.InsufficientHistory).ShouldBe((35d, 60d, 60d, false, true));
        TestTimings.Report([root], "category").Groups.ShouldBeEmpty();

        var sets = new object[] { new Hashtable { ["id"] = "R" }, new Hashtable { ["id"] = "Q" } };
        var selection = new OrderedDictionary
        {
            ["format"] = "antiphon-test-selection-v1", ["selectedSets"] = sets, ["estimates"] = "stale", ["budgetObservations"] = "stale", ["digest"] = "d",
        };
        var copy = TestTimings.AddEstimates(selection, report);
        copy.Keys.Cast<string>().ToArray().ShouldBe(new[] { "format", "selectedSets", "digest", "estimates", "budgetObservations" });
        copy["selectedSets"].ShouldBe(sets);
        copy["digest"].ShouldBe("d");
        ((object[])copy["budgetObservations"]!).ShouldBe(new object[] { "budget-observed" });
        var estimates = ((object[])copy["estimates"]!).Cast<OrderedDictionary>().ToArray();
        estimates.Select(e => string.Join(",", e.Keys.Cast<string>())).Distinct().ShouldBe(new[] { "setId,median,p90,sampleCount,provisional,unbudgeted,last" });
        (estimates[0]["setId"], estimates[0]["median"], estimates[0]["p90"], estimates[0]["sampleCount"], estimates[0]["provisional"], estimates[0]["unbudgeted"], estimates[0]["last"])
            .ShouldBe(("R", (object)35d, (object)60d, (object)6, (object)false, (object)false, (object)60d));
        (estimates[1]["setId"], estimates[1]["median"], estimates[1]["sampleCount"], estimates[1]["unbudgeted"]).ShouldBe(("Q", (object?)null, (object)0, (object)true));
        selection["estimates"].ShouldBe("stale", "the selection itself is not changed");
    }

    /// <summary>The report command ranks by median, cuts to --top and refuses an unreadable selection with one stderr line.</summary>
    [Test]
    public void C1313_ReportRanksAndRefuses()
    {
        var root = Fresh("ranked");
        var started = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        WriteEvents(root,
        [
            Sample("small", 1, started, 1), Sample("large", 9, started, 1), Sample("middle", 5, started, 1),
            Sample("failed", 50, started, 1, outcome: "failed"),
        ]);
        using (var all = Report(root, top: 0))
            Groups(all).Select(item => item.GetProperty("setId").GetString()).ShouldBe(new[] { "large", "middle", "small", "failed" });
        using (var two = Report(root, top: 2))
            Groups(two).Select(item => item.GetProperty("setId").GetString()).ShouldBe(new[] { "large", "middle" });

        using var fixture = NightlyFixture.Create("c1313-report");
        var missing = Path.Combine(root, "absent.json");
        TestTimings.Run(TestTimingReportOptions.Parse(["--roots", root, "--selection-path", missing]), fixture.Host()).ShouldBe(1);
        fixture.Out.ToString().ShouldBeEmpty();
        fixture.Error.ToString().Trim().ShouldStartWith("test-timing-report: ");

        var defaults = TestTimingReportOptions.Parse([]);
        (defaults.Roots.Count, defaults.GroupBy, defaults.Top, defaults.SelectionPath).ShouldBe((0, "class", 20, ""));
        TestTimingReportOptions.Parse(["--roots", "a,b", "--group-by", "roster", "--top", "3"]).Roots.ShouldBe(new[] { "a", "b" });
        Should.Throw<NightlyUsageException>(() => TestTimingReportOptions.Parse(["--group-by", "Roster"])).Message
            .ShouldBe("--group-by expects roster, class or category, not 'Roster'");
        Should.Throw<NightlyUsageException>(() => TestTimingReportOptions.Parse(["--json"])).Message.ShouldBe("unknown flag --json");
    }

    /// <summary>
    /// run-checkpoint.ps1's Write-WrapperTiming runs `test-timing-event --root --event`: the event's members in order on one
    /// journal line, a replay of it a no-op, another payload under its eventId refused, and nothing on stdout.
    /// </summary>
    [Test]
    public void C1313_WrapperEventCommand()
    {
        var root = Fresh("wrapper");
        using var fixture = NightlyFixture.Create("c1313-event");
        const string Event = """{"schemaVersion":"antiphon-test-timing-v1","eventId":"0f8fad5bd9cb469fa165708679a2c4e1","eventKind":"set-completed","runId":"wrapper","attempt":1,"producer":"wrapper","setKind":"checkpoint-row","setId":"CP-1","outcome":"failed","exitCode":3,"startedAt":"2026-11-21T05:06:07.1234567+00:00","endedAt":"2026-11-21T05:06:07.1234567+00:00","category":"unavailable","timingPersistence":"complete","durabilityClass":"configured-root"}""";
        var options = TestTimingEventOptions.Parse(["--root", root, "--event", Event]);
        TestTimings.Run(options, fixture.Host()).ShouldBe(0);
        TestTimings.Run(options, fixture.Host()).ShouldBe(0, "an identical replay is a no-op");
        var journal = Path.Combine(root, "runs", "2026-11-21", "wrapper", "1", "events.jsonl");
        File.ReadAllText(journal).ShouldBe(Event + "\n");
        TestTimings.Run(TestTimingEventOptions.Parse(["--root", root, "--event", Event.Replace("\"exitCode\":3", "\"exitCode\":4")]), fixture.Host()).ShouldBe(1);
        TestTimings.Run(TestTimingEventOptions.Parse(["--root", root, "--event", "[1]"]), fixture.Host()).ShouldBe(1);
        TestTimings.Run(TestTimingEventOptions.Parse(["--root", root, "--event", Event.Replace("\"runId\":\"wrapper\"", "\"runId\":\"..\"")]), fixture.Host()).ShouldBe(1);
        fixture.Error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ShouldBe(new[] { "test-timing-event: event-conflict", "test-timing-event: event-not-an-object", "test-timing-event: path-component-refused" });
        fixture.Out.ToString().ShouldBeEmpty();
        File.ReadAllText(journal).ShouldBe(Event + "\n");
        TestTimings.ReadEvents([root]).Events.ShouldHaveSingleItem()!["exitCode"]!.GetValue<int>().ShouldBe(3);
        Should.Throw<NightlyUsageException>(() => TestTimingEventOptions.Parse(["--root", root])).Message.ShouldBe("missing --event");
        Should.Throw<NightlyUsageException>(() => TestTimingEventOptions.Parse(["--event", Event])).Message.ShouldBe("missing --root");
    }

    private static object Sample(string setId, double window, DateTimeOffset started, int sequence, string membership = "mem",
        string os = "linux", string runtime = "9.0.0", int concurrency = 1, string host = "host-a", string candidateSha = "sha",
        string setKind = "class", double? wall = null, string outcome = "passed") => new
    {
        eventKind = "set-completed",
        setKind,
        setId,
        membershipDigest = membership,
        measurementKind = "inclusive-case-window",
        os,
        architecture = "x64",
        runtimeVersion = runtime,
        concurrency,
        profile = "rc",
        hostId = host,
        repositoryIdentity = "repo",
        candidateSha,
        outcome,
        purpose = "ordinary",
        caseWindowSeconds = window,
        wallSeconds = wall ?? window,
        startedAt = started,
        sequence,
    };

    private static object Cost(string eventId, double wall, string outcome, string purpose, string buildId, double buildSeconds,
        DateTimeOffset started, int sequence) => new
    {
        eventId,
        eventKind = "set-completed",
        setKind = "class",
        setId = "cost",
        membershipDigest = "mem",
        measurementKind = "inclusive-case-window",
        os = "linux",
        architecture = "x64",
        runtimeVersion = "9.0.0",
        concurrency = 1,
        profile = "rc",
        hostId = "host-a",
        repositoryIdentity = "repo",
        outcome,
        purpose,
        caseWindowSeconds = wall,
        wallSeconds = wall,
        producingBuildId = buildId,
        buildSeconds,
        startedAt = started,
        sequence,
    };

    // The test-timing-report command in-process: its one JSON line on the host's stdout, nothing on stderr.
    private static JsonDocument Report(string root, string? selection = null, int top = 20)
    {
        using var fixture = NightlyFixture.Create("c1313-report");
        var arguments = new List<string> { "--roots", root, "--group-by", "class", "--top", top.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (selection is not null) arguments.AddRange(["--selection-path", selection]);
        TestTimings.Run(TestTimingReportOptions.Parse(arguments), fixture.Host()).ShouldBe(0, fixture.Error.ToString());
        fixture.Error.ToString().ShouldBeEmpty();
        var lines = fixture.Out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return JsonDocument.Parse(lines.ShouldHaveSingleItem());
    }

    private string Fresh(string name)
    {
        var path = Path.Combine(TempDir(), name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteEvents(string root, IEnumerable<object> events)
    {
        var lines = events.Select(item => JsonSerializer.Serialize(item, Json));
        File.WriteAllText(Path.Combine(root, "events.jsonl"), string.Join('\n', lines) + "\n");
    }

    private static JsonElement One(JsonDocument doc, string setId, string membership, string? host = null,
        string? os = null, string? runtime = null, int? concurrency = null) =>
        Groups(doc).Single(item => item.GetProperty("setId").GetString() == setId
            && item.GetProperty("membershipDigest").GetString() == membership
            && (host is null || item.GetProperty("hostId").GetString() == host)
            && (os is null || item.GetProperty("os").GetString() == os)
            && (runtime is null || item.GetProperty("runtimeVersion").GetString() == runtime)
            && (concurrency is null || item.GetProperty("concurrency").GetDouble() == concurrency));

    private static JsonElement Estimate(JsonDocument doc, string setId) =>
        doc.RootElement.GetProperty("estimates").EnumerateArray().Single(item => item.GetProperty("setId").GetString() == setId);

    private static IEnumerable<JsonElement> Groups(JsonDocument doc) => doc.RootElement.GetProperty("groups").EnumerateArray().ToArray();

    private static bool Missing(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
}
