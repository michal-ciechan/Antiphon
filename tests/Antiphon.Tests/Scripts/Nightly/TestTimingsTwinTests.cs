using System.Collections.Specialized;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// The antiphon-test-timing-v1 journal of TestTimings.cs (CARD-1313). From W2-nightly S03 until 2026-11-22 this class held
/// the compiled writer and reader to their PowerShell twin, scripts/lib/test-timings.ps1, byte for byte; the ps1 is deleted
/// (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 2); its last writer, run-checkpoint.ps1, ran the compiled one through
/// `antiphon-scripts test-timing-event` until unit 20 made it a launcher of the row verb, which records its own. The outcomes, lines, layout and reads the twins agreed on are stated here.
/// One difference from the ps1 stays recorded: an identical replay of an event whose ISO timestamp carries trailing zero
/// fraction digits is a no-op (the ps1 re-serialized the stored line through a DateTime and reported event-conflict).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingsTwinTests
{
    private sealed record Step(string Name, bool Rooted, (string Key, object? Value)[] Fields, string Expected = "ok");

    private const string Filter = "/*/Antiphon.Tests/Class/(One*)|(Two*)";
    private const string Stamp = "2026-11-21T05:06:07.1234567+00:00";
    private const string Padded = "2026-11-21T05:06:07.1200000+00:00";

    private static (string, object?)[] Nightly(string eventId, string kind, string stamp, string producer = "nightly", params (string, object?)[] more) =>
    [
        ("schemaVersion", TestTimings.Schema), ("eventId", eventId), ("eventKind", kind), ("runId", "nightly"), ("attempt", 1),
        ("producer", producer), ("setKind", "chunk"), ("setId", Filter), ("outcome", kind == "run-started" ? "started" : "passed"),
        ("startedAt", stamp), .. more, ("timingPersistence", "complete"),
    ];

    private static OrderedDictionary Map(IEnumerable<(string Key, object? Value)> fields)
    {
        var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields) map[key] = value;
        return map;
    }

    private const string Text = "quote\" back\\ tab\t nl\n cr\r ff\f bs\b ctl\u001f nel\u0085 ls\u2028 ps\u2029 del\u007f html<>&' slash/ e\u00e9 omega\u03a9 face\ud83d\ude00";

    private static (string, object?)[] ReportShape(double wall) =>
    [
        ("eventId", "report-1"), ("eventKind", "set-completed"), ("runId", "wrapper"), ("attempt", "2"), ("producer", "wrapper"),
        ("setKind", "class"), ("setId", "Antiphon.Tests.Sample"), ("startedAt", "2026-11-20 10:00:00Z"), ("caseWindowSeconds", 1.5),
        ("wallSeconds", wall), ("buildSeconds", 1e-7), ("concurrency", 8), ("sequence", 3L), ("big", long.MaxValue), ("negative", -0.25),
        ("purposeKnown", true), ("retry", false), ("unavailableReason", null), ("args", new object?[] { 1, "x", null }),
        ("exclusive", Map([("db", 1), ("host", 0.5)])), ("text", Text),
    ];

    [Test]
    public void The_writer_appends_each_event_once_and_refuses_conflicts_and_bad_components()
    {
        using var fixture = NightlyFixture.Create("test-timings-journal");
        var root = fixture.PathOf("journal");
        var steps = new List<Step>
        {
            new("nightly run-started", true, Nightly("nightly-start-1", "run-started", Stamp)),
            new("nightly set-completed", true, Nightly("nightly-done-1", "set-completed", Stamp, more: [("exitCode", 0), ("endedAt", Stamp), ("category", "unavailable")])),
            new("run-checkpoint wrapper", true,
            [
                ("schemaVersion", TestTimings.Schema), ("eventId", "0f8fad5bd9cb469fa165708679a2c4e1"), ("eventKind", "set-completed"), ("runId", "wrapper"),
                ("attempt", 1), ("producer", "wrapper"), ("setKind", "checkpoint-row"), ("setId", "CP-1"), ("outcome", "failed"), ("exitCode", 3),
                ("startedAt", Stamp), ("endedAt", Stamp), ("category", "unavailable"), ("timingPersistence", "complete"), ("durabilityClass", "configured-root"),
            ]),
            new("report shape with numbers, nesting and escapes", true, ReportShape(2.0)),
            new("identical replay without an ISO timestamp", true, ReportShape(2.0)),
            new("ISO timestamp with trailing zero digits", true, Nightly("padded-1", "run-started", Padded)),
            new("identical replay of that timestamp", true, Nightly("padded-1", "run-started", Padded)),
            new("same eventId, another payload", true, ReportShape(3.0), "event-conflict"),
            new("set completed by another producer", true, Nightly("other-done-1", "set-completed", Stamp, "other")),
            new("run-started by another producer", true, Nightly("other-start-1", "run-started", Stamp, "other")),
            new("set completed again by its producer", true, Nightly("nightly-done-2", "set-completed", Stamp)),
            new("offset moves the date", true, Nightly("late-1", "run-started", "2026-11-21T23:30:00.0000000-05:00")),
            new("runId ..", true, [("eventId", "bad-1"), ("runId", ".."), ("attempt", 1), ("startedAt", Stamp)], "path-component-refused"),
            new("attempt a/b", true, [("eventId", "bad-2"), ("runId", "r"), ("attempt", "a/b"), ("startedAt", Stamp)], "path-component-refused"),
            new("empty eventId", true, [("eventId", ""), ("runId", "r"), ("attempt", 1), ("startedAt", Stamp)], "path-component-refused"),
            new("eventId with a backslash", true, [("eventId", "x\\y"), ("runId", "r"), ("attempt", 1), ("startedAt", Stamp)], "path-component-refused"),
            new("no root writes nothing", false, Nightly("unrooted-1", "run-started", Stamp)),
        };
        foreach (var step in steps)
        {
            string outcome;
            try { TestTimings.WriteEvent(step.Rooted ? root : "", Map(step.Fields)); outcome = "ok"; }
            catch (InvalidOperationException e) { outcome = e.Message; }
            outcome.ShouldBe(step.Expected, step.Name);
        }

        // One journal per UTC date, run and attempt; each event once, in the order written.
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal).ToArray();
        files.ShouldBe(new[]
        {
            Path.Combine("runs", "2026-11-20", "wrapper", "2", "events.jsonl"), Path.Combine("runs", "2026-11-21", "nightly", "1", "events.jsonl"),
            Path.Combine("runs", "2026-11-21", "wrapper", "1", "events.jsonl"), Path.Combine("runs", "2026-11-22", "nightly", "1", "events.jsonl"),
        });
        string[] Lines(params string[] parts) => File.ReadAllText(Path.Combine([root, "runs", .. parts, "events.jsonl"])).Split('\n');
        var nightly = Lines("2026-11-21", "nightly", "1");
        nightly[^1].ShouldBe("", "every line ends with a newline");
        nightly[..^1].Select(line => JsonNode.Parse(line)!["eventId"]!.GetValue<string>())
            .ShouldBe(new[] { "nightly-start-1", "nightly-done-1", "padded-1", "other-start-1", "nightly-done-2" });
        nightly[0].ShouldBe("{\"schemaVersion\":\"antiphon-test-timing-v1\",\"eventId\":\"nightly-start-1\",\"eventKind\":\"run-started\",\"runId\":\"nightly\",\"attempt\":1,"
            + "\"producer\":\"nightly\",\"setKind\":\"chunk\",\"setId\":\"/*/Antiphon.Tests/Class/(One*)|(Two*)\",\"outcome\":\"started\",\"startedAt\":\"" + Stamp + "\",\"timingPersistence\":\"complete\"}");
        nightly[2].ShouldContain("\"startedAt\":\"" + Padded + "\"", customMessage: "the timestamp text is kept as given");
        Lines("2026-11-22", "nightly", "1")[0].ShouldContain("\"eventId\":\"late-1\"", customMessage: "the UTC date names the directory");

        // The line ConvertTo-Json -Compress -Depth 8 wrote: member order, integers as digits, floating values in round-trip
        // form with ".0" when integral, Newtonsoft's escapes (control characters, quote, backslash, U+0085, U+2028, U+2029),
        // everything else as it is.
        var report = Lines("2026-11-20", "wrapper", "2");
        report.Length.ShouldBe(2);
        var line = report[0];
        line.ShouldStartWith("{\"eventId\":\"report-1\",\"eventKind\":\"set-completed\",\"runId\":\"wrapper\",\"attempt\":\"2\",\"producer\":\"wrapper\",\"setKind\":\"class\","
            + "\"setId\":\"Antiphon.Tests.Sample\",\"startedAt\":\"2026-11-20 10:00:00Z\",\"caseWindowSeconds\":1.5,\"wallSeconds\":2.0,\"buildSeconds\":1E-07,\"concurrency\":8,"
            + "\"sequence\":3,\"big\":9223372036854775807,\"negative\":-0.25,\"purposeKnown\":true,\"retry\":false,\"unavailableReason\":null,\"args\":[1,\"x\",null],"
            + "\"exclusive\":{\"db\":1,\"host\":0.5},\"text\":\"quote\\\" back\\\\ tab\\t nl\\n cr\\r ff\\f bs\\b ctl\\u001f nel\\u0085 ls\\u2028 ps\\u2029 del\u007f html<>&' slash/ "
            + "e\u00e9 omega\u03a9 face\ud83d\ude00\"}");
        line.Length.ShouldBe(line.IndexOf("face", StringComparison.Ordinal) + "face\ud83d\ude00\"}".Length);
        JsonNode.Parse(line)!["text"]!.GetValue<string>().ShouldBe(Text);
        TestTimings.NodeJson(JsonNode.Parse(line)).ShouldBe(line, "a read line serializes back to its own bytes");
    }

    [Test]
    public void The_reader_skips_corrupt_and_duplicate_lines_and_warns_on_a_torn_tail()
    {
        using var fixture = NightlyFixture.Create("test-timings-read");
        var crafted = fixture.PathOf("crafted");
        var first = Path.Combine(crafted, "runs", "2026-11-19", "r", "1");
        Directory.CreateDirectory(first);
        File.WriteAllText(Path.Combine(first, "events.jsonl"), string.Join("\n",
        [
            "{\"schemaVersion\":\"antiphon-test-timing-v1\",\"eventId\":\"crafted-1\",\"eventKind\":\"set-completed\",\"runId\":\"r\",\"attempt\":1,\"startedAt\":\"2026-11-19T01:02:03.0000000+00:00\",\"wallSeconds\":2.0,\"nested\":{\"a\":[1,\"b\\u001f\",null]}}",
            "{not json",
            "",
            "   ",
            "{\"eventId\":\"CRAFTED-1\",\"eventKind\":\"run-started\"}",
            "{\"eventKind\":\"set-completed\",\"setId\":\"no-id\"}",
            "5",
            "{\"eventId\":\"nightly-start-1\",\"eventKind\":\"run-started\",\"startedAt\":\"2026-11-19T00:00:00Z\"}",
            "{\"eventId\":\"tail\",\"eventKind\":\"run-started\"",
        ]), new UTF8Encoding(false));
        var second = Path.Combine(crafted, "runs", "2026-11-18", "r", "1");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "events.jsonl"), "{\"eventId\":\"crlf-1\",\"n\":1.5}\r\n{\"eventId\":\"crlf-2\",\"s\":\"a\\u001fb\"}\r\n", new UTF8Encoding(false));

        string Shape(JsonNode? node) => node switch
        {
            null => "null",
            JsonObject row => row["eventId"]?.GetValue<string>() ?? "no-id:" + row["setId"]?.GetValue<string>(),
            _ => "value:" + node.ToJsonString(),
        };
        // Files in path order (2026-11-18 first), lines in file order; an eventId seen before (ignoring case) is skipped, a
        // blank line is skipped, a whitespace line is a null record, a line that is not JSON is counted, never returned.
        var read = TestTimings.ReadEvents([crafted]);
        read.Events.Select(Shape).ShouldBe(new[] { "crlf-1", "crlf-2", "crafted-1", "null", "no-id:no-id", "value:5", "nightly-start-1" });
        read.CorruptCount.ShouldBe(1);
        read.Warnings.ShouldBe(new[] { "torn-tail" });
        TestTimings.NodeJson(read.Events[2]).ShouldBe("{\"schemaVersion\":\"antiphon-test-timing-v1\",\"eventId\":\"crafted-1\",\"eventKind\":\"set-completed\",\"runId\":\"r\",\"attempt\":1,"
            + "\"startedAt\":\"2026-11-19T01:02:03.0000000+00:00\",\"wallSeconds\":2.0,\"nested\":{\"a\":[1,\"b\\u001f\",null]}}");
        read.Events[1]!["s"]!.GetValue<string>().ShouldBe("a\u001fb");

        // Roots in order, an eventId once across all of them, and a missing root is no root.
        var written = fixture.PathOf("written");
        TestTimings.WriteEvent(written, Map(Nightly("nightly-start-1", "run-started", Stamp)));
        TestTimings.WriteEvent(written, Map(Nightly("written-2", "run-started", Stamp)));
        var both = TestTimings.ReadEvents([written, crafted, fixture.PathOf("missing")]);
        both.Events.Select(Shape).ShouldBe(new[] { "nightly-start-1", "written-2", "crlf-1", "crlf-2", "crafted-1", "null", "no-id:no-id", "value:5" });
        (both.CorruptCount, both.Warnings.Count).ShouldBe((1, 1));
        TestTimings.ReadEvents([fixture.PathOf("missing"), ""]).Events.ShouldBeEmpty();
    }

    [Test]
    public void Join_path_and_the_mutex_name_of_a_journal_directory()
    {
        using var fixture = NightlyFixture.Create("test-timings-join");
        string Mutex(string path) => "antiphon-timing-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32];
        var cases = OperatingSystem.IsWindows()
            ? new[]
            {
                ("C:/x", "runs", @"C:\x\runs"), (@"C:\x\", "runs", @"C:\x\runs"), (@"C:\x\\", "runs", @"C:\x\\runs"), ("rel/dir", "runs", @"rel\dir\runs"),
                (@"\\server\share", "runs", @"\\server\share\runs"), ("C:", "runs", @"C:\runs"), (@"C:\x", @"\runs", @"C:\x\runs"), (fixture.Root, "runs", fixture.Root + @"\runs"),
            }
            : new[] { ("/x", "runs", "/x/runs"), ("/x/", "runs", "/x/runs"), ("rel/dir", "runs", "rel/dir/runs"), (@"C:\x\", "runs", @"C:\x\/runs"), (fixture.Root, "runs", fixture.Root + "/runs") };
        foreach (var (parent, child, expected) in cases)
        {
            TestTimings.JoinPath(parent, child).ShouldBe(expected, parent);
            TestTimings.MutexName(expected).ShouldBe(Mutex(expected), parent);
        }
        TestTimings.MutexName(null!).ShouldBe(Mutex(""));
    }

    [Test]
    public void Components_are_refused_before_anything_is_written()
    {
        using var fixture = NightlyFixture.Create("test-timings-twin");
        foreach (var bad in new[] { "", " ", ".", "..", "a..b", "a/b", "a\\b", Path.GetPathRoot(fixture.Root)! })
            Should.Throw<InvalidOperationException>(() => TestTimings.RequireComponent(bad)).Message.ShouldBe("path-component-refused", bad);
        TestTimings.RequireComponent("nightly-start-1");
        var map = new OrderedDictionary { ["eventId"] = "e", ["runId"] = "..", ["attempt"] = 1, ["startedAt"] = "2026-11-21T00:00:00Z" };
        Should.Throw<InvalidOperationException>(() => TestTimings.WriteEvent(fixture.Root, map)).Message.ShouldBe("path-component-refused");
        Directory.EnumerateFileSystemEntries(fixture.Root).ShouldBeEmpty();
        TestTimings.WriteEvent(null!, map);
    }
}
