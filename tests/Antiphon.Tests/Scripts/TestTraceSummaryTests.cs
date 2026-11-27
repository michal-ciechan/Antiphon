using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The test trace summary (scripts/lib/Antiphon.Nightly/TestTraceSummary.cs, `antiphon-scripts summarize-test-trace`) in
/// this process. The expected texts are what scripts/summarize-test-trace.ps1 printed for the same file before it was
/// deleted (2026-11-23): span counts, roots by duration, span names by self time and the --test trees, numbers in the
/// invariant culture.
/// </summary>
[Category("Unit")]
public sealed class TestTraceSummaryTests
{
    // Two tests (t1, t2) and an orphan whose parent is not in the file (a root). a4 is under one millisecond; b2 outlasts
    // its parent, so b1's self time floors at zero. A blank line and a malformed line are skipped.
    private const string Trace = """
        {"TraceId":"t1","SpanId":"a1","ParentId":null,"Source":"TUnit","Name":"Antiphon.Tests.Alpha.Runs","Start":"2026-11-23T10:00:00.0000000Z","DurationMs":12345.678,"Status":null,"Tags":null,"Pid":7}
        {"TraceId":"t1","SpanId":"a2","ParentId":"a1","Source":"Antiphon.Tests","Name":"harness create","Start":"2026-11-23T10:00:00.0100000Z","DurationMs":400,"Status":null,"Tags":{"slot":"3"},"Pid":7}

        {"TraceId":"t1","SpanId":"a3","ParentId":"a1","Source":"Antiphon.Tests","Name":"git status","Start":"2026-11-23T10:00:00.0050000Z","DurationMs":300.5,"Status":null,"Tags":null,"Pid":7}
        {"TraceId":"t1","SpanId":"a4","ParentId":"a2","Source":"Npgsql","Name":"SELECT","Start":"2026-11-23T10:00:00.0200000Z","DurationMs":0.4,"Status":null,"Tags":null,"Pid":7}
        {not json
        {"TraceId":"t1","SpanId":"a5","ParentId":"a2","Source":"Npgsql","Name":"INSERT","Start":"2026-11-23T10:00:00.0300000Z","DurationMs":120.25,"Status":null,"Tags":{"db.system":"postgresql","code.stack":null},"Pid":7}
        {"TraceId":"t2","SpanId":"b1","ParentId":null,"Source":"TUnit","Name":"Antiphon.Tests.Beta.Fails","Start":"2026-11-23T10:00:01.0000000Z","DurationMs":900,"Status":"error","Tags":null,"Pid":7}
        {"TraceId":"t2","SpanId":"b2","ParentId":"b1","Source":"Antiphon.Tests","Name":"harness create","Start":"2026-11-23T10:00:01.1000000Z","DurationMs":1200,"Status":null,"Tags":null,"Pid":7}
        {"TraceId":"t3","SpanId":"c1","ParentId":"ffff","Source":"Antiphon.Tests","Name":"git fetch","Start":"2026-11-23T10:00:02.0000000Z","DurationMs":50,"Status":null,"Tags":null,"Pid":7}
        """;

    private const string Rankings = """
        8 spans, 3 traces, 3 sources: Antiphon.Tests, Npgsql, TUnit

        == Root spans (tests) by duration ==
          12,345.7 ms  TUnit        Antiphon.Tests.Alpha.Runs
             900.0 ms  TUnit        Antiphon.Tests.Beta.Fails
              50.0 ms  Antiphon.Tests git fetch

        == Span names by self time (all traces) ==
          count      self ms     total ms     max ms  source | span
              1       11,645       12,346     12,346  TUnit | Antiphon.Tests.Alpha.Runs
              2        1,479        1,600      1,200  Antiphon.Tests | harness create
              1          300          300        300  Antiphon.Tests | git status
              1          120          120        120  Npgsql | INSERT
              1           50           50         50  Antiphon.Tests | git fetch
              1            0            0          0  Npgsql | SELECT
              1            0          900        900  TUnit | Antiphon.Tests.Beta.Fails

        """;

    private const string AlphaTree = """

        == Trace: Antiphon.Tests.Alpha.Runs ==
         12,345.7 ms (self 11,645.2)  Antiphon.Tests.Alpha.Runs
              300.5 ms (self    300.5)  git status
              400.0 ms (self    279.4)  harness create {slot=3}
                120.2 ms (self    120.2)  INSERT {db.system=postgresql code.stack=}

        """;

    private const string BetaTree = """

        == Trace: Antiphon.Tests.Beta.Fails ==
            900.0 ms (self      0.0)  Antiphon.Tests.Beta.Fails
            1,200.0 ms (self  1,200.0)  harness create

        """;

    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    private static (int Exit, string Out, string Error) Summarize(NightlyFixture fixture, params string[] arguments)
    {
        var options = SummarizeTestTraceOptions.Parse(arguments, fixture.Root);
        var exit = TestTraceSummary.Run(options, fixture.Host());
        return (exit, fixture.Out.ToString().ReplaceLineEndings("\n"), fixture.Error.ToString());
    }

    private static NightlyFixture WithTrace(string body)
    {
        var fixture = NightlyFixture.Create("trace-summary");
        File.WriteAllText(fixture.PathOf("trace.jsonl"), Lf(body) + "\n");
        return fixture;
    }

    [Test]
    public void Rankings_match_the_retired_script()
    {
        using var fixture = WithTrace(Trace);
        var run = Summarize(fixture, "--path", "trace.jsonl");
        run.Exit.ShouldBe(0, run.Error);
        run.Out.ShouldBe(Lf(Rankings));
        run.Error.ShouldBeEmpty();
    }

    [Test]
    public void Test_prints_the_matching_trees_in_start_order()
    {
        using var fixture = WithTrace(Trace);
        var spans = TestTraceSummary.Read(fixture.PathOf("trace.jsonl"));
        spans.Count.ShouldBe(8);

        // A substring ignoring case; the sub-millisecond SELECT is left out of the tree, not out of the rankings.
        TestTraceSummary.Text(spans, "trace.jsonl", 25, "alpha", 1).ShouldBe(Lf(Rankings + AlphaTree).TrimEnd('\n') + "\n");
        // Wildcards and sets (-like): both tests, in file order.
        TestTraceSummary.Text(spans, "trace.jsonl", 25, "?ntiphon.Tests.[AB]*", 1)
            .ShouldBe(Lf(Rankings + AlphaTree + BetaTree).TrimEnd('\n') + "\n");
        // --min-ms 0 keeps every child; the parent outlasted by its child has zero self time.
        TestTraceSummary.Text(spans, "trace.jsonl", 25, "*beta*", 0).ShouldBe(Lf(Rankings + BetaTree).TrimEnd('\n') + "\n");
        // A backtick escapes a wildcard character, and a '[' with no ']' is literal: neither matches a root.
        TestTraceSummary.Text(spans, "trace.jsonl", 25, "Alpha`*", 1).ShouldBe(Lf(Rankings));
        TestTraceSummary.Text(spans, "trace.jsonl", 25, "Alpha[", 1).ShouldBe(Lf(Rankings));
    }

    [Test]
    public void A_tag_keeps_its_own_line_breaks()
    {
        // Npgsql's SQL text carries CRLF: it is printed as written, and only the summary's own lines take the host's line end.
        using var fixture = WithTrace("""{"TraceId":"q1","SpanId":"s1","ParentId":null,"Source":"Npgsql","Name":"query","Start":"2026-11-23T10:00:00Z","DurationMs":2,"Tags":{"db.statement":"SELECT 1\r\nFROM t"}}""");
        var options = SummarizeTestTraceOptions.Parse(["--path", "trace.jsonl", "--test", "query"], fixture.Root);
        TestTraceSummary.Run(options, fixture.Host()).ShouldBe(0);
        fixture.Out.ToString().ShouldEndWith(Environment.NewLine + "      2.0 ms (self      2.0)  query {db.statement=SELECT 1\r\nFROM t}" + Environment.NewLine, Case.Sensitive);
        TestTraceSummary.Text(TestTraceSummary.Read(fixture.PathOf("trace.jsonl")), "trace.jsonl", 25, "query", 1)
            .ShouldEndWith("\n      2.0 ms (self      2.0)  query {db.statement=SELECT 1\r\nFROM t}\n", Case.Sensitive);
    }

    [Test]
    public void Top_and_min_ms_cut_the_output()
    {
        using var fixture = WithTrace(Trace);
        var run = Summarize(fixture, "--path", fixture.PathOf("trace.jsonl"), "--test", "Alpha", "--min-ms", "200", "--top", "2");
        run.Exit.ShouldBe(0, run.Error);
        run.Out.ShouldBe(Lf("""
            8 spans, 3 traces, 3 sources: Antiphon.Tests, Npgsql, TUnit

            == Root spans (tests) by duration ==
              12,345.7 ms  TUnit        Antiphon.Tests.Alpha.Runs
                 900.0 ms  TUnit        Antiphon.Tests.Beta.Fails

            == Span names by self time (all traces) ==
              count      self ms     total ms     max ms  source | span
                  1       11,645       12,346     12,346  TUnit | Antiphon.Tests.Alpha.Runs
                  2        1,479        1,600      1,200  Antiphon.Tests | harness create

            == Trace: Antiphon.Tests.Alpha.Runs ==
             12,345.7 ms (self 11,645.2)  Antiphon.Tests.Alpha.Runs
                  300.5 ms (self    300.5)  git status
                  400.0 ms (self    279.4)  harness create {slot=3}

            """));
    }

    [Test]
    public void Chunk_summary_subtracts_overlapping_intervals_from_wall_time_once()
    {
        using var fixture = WithTrace("""
            {"TraceId":"t","SpanId":"p","Source":"Antiphon.Tests","Name":"process lifetime","Start":"2026-11-23T10:00:00.000Z","DurationMs":100}
            {"TraceId":"t","SpanId":"s","Source":"Antiphon.Tests","Name":"process startup","Start":"2026-11-23T10:00:00.000Z","DurationMs":10}
            {"TraceId":"t","SpanId":"d","Source":"TUnit.Lifecycle","Name":"test discovery","Start":"2026-11-23T10:00:00.010Z","DurationMs":5}
            {"TraceId":"t","SpanId":"a","Source":"TUnit.Lifecycle","Name":"test assembly","Start":"2026-11-23T10:00:00.020Z","DurationMs":65}
            {"TraceId":"t","SpanId":"h1","ParentId":"a","Source":"TUnit.Lifecycle","Name":"hook: Open()","Start":"2026-11-23T10:00:00.020Z","DurationMs":10}
            {"TraceId":"t","SpanId":"f","ParentId":"h1","Source":"Antiphon.Tests","Name":"fixture initialize","Start":"2026-11-23T10:00:00.021Z","DurationMs":8,"Tags":{"antiphon.fixture":"shared"}}
            {"TraceId":"t","SpanId":"g","ParentId":"a","Source":"TUnit.Lifecycle","Name":"test suite","Start":"2026-11-23T10:00:00.030Z","DurationMs":47,"Tags":{"test.suite.name":"Alpha"}}
            {"TraceId":"tx","SpanId":"x","Source":"TUnit","Name":"test case","Start":"2026-11-23T10:00:00.030Z","DurationMs":35,"Tags":{"tunit.test.class":"Alpha","test.suite.name":"Alpha","tunit.test.id":"1"}}
            {"TraceId":"tx","SpanId":"x1","ParentId":"x","Source":"TUnit","Name":"hook: Prepare()","Start":"2026-11-23T10:00:00.033Z","DurationMs":3}
            {"TraceId":"tx","SpanId":"x2","ParentId":"x","Source":"TUnit","Name":"test body","Start":"2026-11-23T10:00:00.040Z","DurationMs":20}
            {"TraceId":"t","SpanId":"x3","ParentId":"g","Source":"TUnit","Name":"dispose Alpha","Start":"2026-11-23T10:00:00.060Z","DurationMs":5,"Tags":{"tunit.trace.scope":"test","tunit.test.id":"1"}}
            {"TraceId":"ty","SpanId":"y","Source":"TUnit","Name":"test case","Start":"2026-11-23T10:00:00.055Z","DurationMs":20,"Tags":{"tunit.test.class":"Beta"}}
            {"TraceId":"ty","SpanId":"y2","ParentId":"y","Source":"TUnit","Name":"test body","Start":"2026-11-23T10:00:00.055Z","DurationMs":20}
            {"TraceId":"t","SpanId":"c1","ParentId":"g","Source":"TUnit.Lifecycle","Name":"hook: CloseClass()","Start":"2026-11-23T10:00:00.075Z","DurationMs":2}
            {"TraceId":"t","SpanId":"h2","ParentId":"a","Source":"TUnit.Lifecycle","Name":"hook: Close()","Start":"2026-11-23T10:00:00.080Z","DurationMs":5}
            """);
        var path = fixture.PathOf("trace.jsonl");
        TestTraceBreakdown.Run(path, fixture.Out, fixture.Error).ShouldBe(0);
        var output = Regex.Replace(fixture.Out.ToString(), " +", " ");
        output.ShouldContain("Wall 100.000 ms");
        output.ShouldContain("Startup 10.000");
        output.ShouldContain("Discovery 5.000");
        output.ShouldContain("One-time setup 10.000");
        output.ShouldContain("One-time teardown 7.000");
        output.ShouldContain("Per-test setup 10.000");
        output.ShouldContain("Per-test body 40.000");
        output.ShouldContain("Per-test teardown 5.000");
        output.ShouldContain("Limiter wait -");
        output.ShouldContain("Process exit/teardown 15.000");
        output.ShouldContain("Idle/unaccounted 8.000");
        output.ShouldContain("35.000 Alpha");
        output.ShouldContain("20.000 Beta");
    }

    [Test]
    public void Exporter_has_no_destination_without_opt_in()
    {
        TestTrace.HasFileDestination(null, null).ShouldBeFalse();
        TestTrace.HasFileDestination(" ", "").ShouldBeFalse();
        TestTrace.HasFileDestination("traces", null).ShouldBeTrue();
        TestTrace.HasFileDestination(null, "trace.jsonl").ShouldBeTrue();
    }

    [Test]
    public void Chunk_helper_spans_require_the_trace_directory_even_with_a_listener()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, TestTrace.Source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var span = TestTrace.ChunkSpan("chunk helper opt-in test");
        var enabled = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestTrace.DirectoryVariable));
        (span is not null).ShouldBe(enabled);
    }

    [Test]
    public void A_file_without_spans_is_refused()
    {
        // JSON that is not a span object is skipped like a malformed line.
        using var fixture = WithTrace("\n{not json\n{}\n[]\n{\"TraceId\":\"t1\",\"SpanId\":\"a1\"}\n");
        TestTraceSummary.Read(fixture.PathOf("trace.jsonl")).ShouldBeEmpty();
        var run = Summarize(fixture, "--path", "trace.jsonl");
        run.Exit.ShouldBe(1);
        run.Out.ShouldBeEmpty();
        run.Error.ShouldBe("summarize-test-trace: No spans in " + fixture.PathOf("trace.jsonl") + "." + Environment.NewLine);

        using var missing = NightlyFixture.Create("trace-summary");
        var absent = Summarize(missing, "--path", "absent.jsonl");
        absent.Exit.ShouldBe(1);
        absent.Error.ShouldStartWith("summarize-test-trace: ", Case.Sensitive);
    }

    [Test]
    public void The_reader_reads_what_TestTrace_writes()
    {
        // The writer is tests/Shared/TestTrace.cs (compiled into test assemblies that do not reference the core): its
        // record's members, in order, are the names the reader reads.
        var writer = Regex.Replace(File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "tests/Shared/TestTrace.cs")), @"\s+", " ");
        writer.ShouldContain("private sealed record SpanRecord( string TraceId, string SpanId, string? ParentId, string Source, string Name,"
            + " DateTime Start, double DurationMs, string? Status, Dictionary<string, string?>? Tags, int Pid);", Case.Sensitive);
        writer.ShouldContain("scripts/antiphon summarize-test-trace", Case.Sensitive);
        File.Exists(Path.Combine(NightlyPlanCases.Repo, "scripts/lib/Antiphon.Nightly/TestTraceSummary.cs")).ShouldBeTrue();
    }

    [Test]
    public void The_command_line_is_checked()
    {
        Should.Throw<NightlyUsageException>(() => SummarizeTestTraceOptions.Parse([], Path.GetTempPath()));
        Should.Throw<NightlyUsageException>(() => SummarizeTestTraceOptions.Parse(["--path", "t.jsonl", "--min-ms", "ten"], Path.GetTempPath()));
        Should.Throw<NightlyUsageException>(() => SummarizeTestTraceOptions.Parse(["--path", "t.jsonl", "--top", "x"], Path.GetTempPath()));
        Should.Throw<NightlyUsageException>(() => SummarizeTestTraceOptions.Parse(["--path", "t.jsonl", "-Test", "x"], Path.GetTempPath()));
        var options = SummarizeTestTraceOptions.Parse(["--path", "t.jsonl", "--min-ms", "2.5"], Path.GetTempPath());
        options.Path.ShouldBe(Path.Combine(Path.GetTempPath(), "t.jsonl"));
        options.MinMs.ShouldBe(2.5);
        options.Top.ShouldBe(25);
        options.Test.ShouldBeEmpty();
    }
}
