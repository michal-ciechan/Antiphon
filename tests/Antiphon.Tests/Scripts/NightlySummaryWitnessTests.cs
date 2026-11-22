using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-2005. The production summary writer streams a 1,274-row document, keeps each suite's exclusion list once, and
/// never converts the whole chunk list to text. Since W2-nightly S12 the writer is NightlyRunSummary (the run's
/// WriteRunSummary, once Write-NightlyRunSummary, is a wrapper over it) and the witness runs in-process (it was scripts/test-nightly-summary-witness.ps1
/// under a 768 MiB DOTNET_GCHeapHardLimit): the bound is the writer's own allocation. The report reads the summary
/// in-process too (NightlyReport since W2-nightly S06).
/// </summary>
[Category("Integration")]
public sealed class NightlySummaryWitnessTests
{
    private static string Repo => DelegateScriptRunner.RepoRoot;

    [Test]
    public void C2005_SummaryBuildDoesNotConvertTheChunkList()
    {
        // W2-nightly S17: the run is NightlyTests.Run (nightly-tests-impl.ps1 is deleted); its Write-NightlyRunSummary is WriteRunSummary.
        var impl = File.ReadAllText(Path.Combine(Repo, "scripts", "lib", "Antiphon.Nightly", "NightlyTests.cs")).Replace("\r\n", "\n");
        var start = impl.IndexOf("public static void WriteRunSummary(", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the run's summary writer");
        var body = impl[start..impl.IndexOf("\n        }\n", start, StringComparison.Ordinal)];
        body.ShouldContain("NightlyRunSummary.Write(summary, suiteResults, dispatchSchedule");
        body.ShouldNotContain("NightlyJson.Text");
        body.ShouldNotContain("suites=@(");
        body.ShouldNotContain("suites = @(");
        Regex.Matches(impl, @"WriteRunSummary\(summary, suiteResults\.ToArray\(\), dispatchSchedule").Count
            .ShouldBe(1, "NightlyTests.Run must call the production summary writer");

        var writer = File.ReadAllText(Path.Combine(Repo, "scripts", "lib", "Antiphon.Nightly", "NightlyRunSummary.cs"));
        Regex.Matches(writer, @"WriteJson\(summaryPath, summary, Depth\)").Count.ShouldBe(1, "one streamed write of the summary");
        writer.ShouldContain("{ \"suitesFromSummary\", true }");
        foreach (Match text in Regex.Matches(writer, @"NightlyJson\.Text\((\w+),"))
            (text.Groups[1].Value is "firstList" or "list").ShouldBeTrue("summary build path hands more than an exclusion list to the text writer: " + text.Value);
    }

    [Test]
    public void C2005_SummaryWriterStreamsWithinABoundedHeap()
    {
        var shape = new (string Id, int Rows, int Exclusions)[]
        {
            ("antiphon", 1100, 500), ("session-runner", 40, 8), ("pty-host", 20, 8), ("agents-pty", 30, 8), ("messaging", 20, 8), ("e2e", 40, 8), ("client", 12, 8), ("scripts", 12, 8),
        };
        var expectedUids = new List<string>();
        var suiteRows = new List<object>();
        foreach (var suite in shape)
        {
            var excluded = new object[suite.Exclusions];
            for (var j = 0; j < excluded.Length; j++)
            {
                var uid = suite.Id + "-u" + j.ToString("D4", CultureInfo.InvariantCulture);
                expectedUids.Add(uid);
                excluded[j] = Ordered(("uid", uid), ("class", "Antiphon.Tests.Sample"), ("method", "Run"), ("categories", new object[] { "Slow", "Integration" }),
                    ("disposition", "Excluded"), ("reason", "policy"), ("owner", "nightly"));
            }
            for (var i = 0; i < suite.Rows; i++)
                suiteRows.Add(Ordered(("id", suite.Id), ("chunk", suite.Id + "-c" + i.ToString("D4", CultureInfo.InvariantCulture)), ("name", suite.Id), ("log", ""), ("exitCode", 0),
                    ("timedOut", false), ("skipped", false), ("result", "pass"), ("trx", "chunk.trx"), ("evidenceReasons", new object[] { "kept-on-suite" }), ("requiredUidCount", 1),
                    ("excludedUidCount", excluded.Length), ("censusDigest", "d5"), ("exclusions", excluded), ("coverageComplete", true)));
        }
        suiteRows.Count.ShouldBe(1274, "generator rows");

        using var fixture = NightlyFixture.Create("c2005");
        var dir = fixture.Root;
        var summaryPath = Path.Combine(dir, "summary.json");
        var sha = new string('a', 40);
        var completedAt = DateTime.SpecifyKind(new DateTime(2026, 11, 19, 1, 3, 3, 4), DateTimeKind.Utc);
        var summary = Ordered(("startedAt", "2026-11-19T01:02:03.0040000Z"), ("completedAt", completedAt.ToString("o", CultureInfo.InvariantCulture)), ("durationSeconds", 60.0),
            ("sha", sha), ("gitRef", "origin/master"), ("trigger", "scheduled"), ("profile", "nightly"), ("expectedSha", sha), ("requiredSuites", new object[] { "antiphon" }),
            ("runId", "c2005-synthetic-1274"), ("clone", @"C:\Antiphon\nightly\checkout"), ("logDir", dir), ("selectedSuites", new object[] { "antiphon" }),
            ("selection", Ordered(("attempt", "1"))), ("timingRoot", @"C:\timing"), ("timingChunks", Array.Empty<object>()), ("suiteTimeline", Array.Empty<object>()),
            ("platform", "linux"), ("coverageScope", "Full"), ("outcome", "green"), ("succeeded", true), ("coverageComplete", true), ("testsPassed", true),
            ("policyHash", new string('b', 64)), ("reasons", Array.Empty<object>()), ("warnings", Array.Empty<object>()), ("expectedSkipRosterDigest", ""),
            ("toleratedSkips", Array.Empty<object>()), ("builds", Array.Empty<object>()), ("suites", suiteRows.ToArray()), ("invocationId", "c2005-synthetic-1274"));
        var schedule = new PlainObject(("preparationComplete", true), ("canonicalPlanDigest", new string('c', 64)));

        var phaseRuns = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        NightlyRunSummary.Write(summary, suiteRows.ToArray(), schedule, sha, completedAt, summaryPath, dir, Path.Combine(dir, "timing"), _ => phaseRuns++);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // The streamed writer and the reference dedupe allocate a few MB; building the document as text (or one list per
        // row) is hundreds. The witness's old 768 MiB process limit becomes this bound on the writer itself.
        allocated.ShouldBeLessThan(64L * 1024 * 1024, "summary writer allocated " + allocated + " bytes");
        phaseRuns.ShouldBe(0, "no phase plan, no phase summary");

        File.Exists(summaryPath).ShouldBeTrue("summary missing");
        var text = File.ReadAllText(summaryPath);
        using var parsed = JsonDocument.Parse(text);
        var root = parsed.RootElement;
        var suites = root.GetProperty("suites").EnumerateArray().ToArray();
        suites.Length.ShouldBe(1274);
        // The reader adds suites in memory (PriorFailureHistory.SummaryDispatchProgress: the suites rows that have a chunk);
        // the file itself must not already carry them.
        var progress = root.GetProperty("dispatchProgress");
        progress.TryGetProperty("suites", out _).ShouldBeFalse("dispatchProgress embeds a second suites array");
        progress.GetProperty("suitesFromSummary").GetBoolean().ShouldBeTrue();
        suites.Count(row => row.TryGetProperty("chunk", out var chunk) && chunk.GetString() is { Length: > 0 }).ShouldBe(1274, "dispatchProgress rows");
        var counts = Regex.Matches(text, "\"uid\"\\s*:\\s*\"([^\"]+)\"").GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.Count());
        counts.Count.ShouldBe(expectedUids.Count, "uid keys");
        foreach (var uid in expectedUids) counts.GetValueOrDefault(uid).ShouldBe(1, "uid " + uid);
        suites.Any(row => row.TryGetProperty("exclusions", out _)).ShouldBeFalse("a row kept its exclusions");
        root.GetProperty("runId").GetString().ShouldBe("c2005-synthetic-1274");
        root.GetProperty("coverageComplete").GetBoolean().ShouldBeTrue();
        progress.GetProperty("liveness").GetString().ShouldBe("closed");
        Console.WriteLine("WROTE bytes=" + new FileInfo(summaryPath).Length + " suites=1274 allocated=" + allocated);

        // The report reads the streamed summary (NightlyReport, the code behind antiphon-scripts nightly-report since
        // W2-nightly S06) in-process over a fake board; its reading allocates within the old witness's 768 MiB process bound.
        var calls = new List<string>();
        NightlyReportHttpResponse Board(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            calls.Add(method + " " + uri);
            if (uri.EndsWith("/columns", StringComparison.Ordinal)) return new(200, "[{\"id\":\"col\",\"name\":\"Done\",\"isTerminal\":true,\"columnOrder\":1}]");
            if (uri.EndsWith("/api/boards", StringComparison.Ordinal)) return new(200, "[{\"id\":\"8988ca03-7414-47ad-b0b6-51556c701703\",\"name\":\"Antiphon\"}]");
            if (uri.Contains("/api/cards", StringComparison.Ordinal)) return new(200, "{\"cards\":[],\"truncated\":false}");
            throw new InvalidOperationException("unexpected " + uri);
        }
        var output = new StringWriter();
        var reportBefore = GC.GetAllocatedBytesForCurrentThread();
        var report = NightlyReport.Run(new NightlyReportOptions { Summary = summaryPath, DryRun = true, Board = "Antiphon", Api = "http://nightly.test" },
            new NightlyReportSeams { Clock = TimeProvider.System, Http = Board, Environment = _ => null!, Out = output, Error = output });
        var reportAllocated = GC.GetAllocatedBytesForCurrentThread() - reportBefore;
        report.ExitCode.ShouldBe(0, output.ToString());
        report.Action.ShouldBe("none", output.ToString());
        report.Title.StartsWith("Nightly green", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(report.Title);
        reportAllocated.ShouldBeLessThan(768L * 1024 * 1024, "report allocated " + reportAllocated + " bytes");
        calls.ShouldNotBeEmpty();
        File.Exists(Path.Combine(dir, "card.md")).ShouldBeFalse("report wrote card.md");
    }

    private static OrderedDictionary Ordered(params (string Key, object? Value)[] entries)
    {
        var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries) map[key] = value;
        return map;
    }

    private static string FunctionBody(string text, string name)
    {
        var marker = "function " + name + " {";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, name);
        var depth = 0;
        for (var i = text.IndexOf('{', at); i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(at, i - at + 1);
            }
        }

        throw new InvalidOperationException("unclosed " + name);
    }
}
