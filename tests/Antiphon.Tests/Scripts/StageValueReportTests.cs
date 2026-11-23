using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0272 S4: the stage-value-report command (StageValueReport.Run), in process. It replaced scripts/stage-value-report.ps1
/// and its driver scripts/test-stage-value-report.ps1 (unit 25 of the 2026-11-22 plan); T1 to T5 are the driver's rows, the
/// HTTP a recording transport answering as the API (the driver's -HttpShim) and git a FakeGit, so nothing leaves the process.
/// Not ported: the -PassThru return value (a PowerShell in-process seam; T3 reads the printed JSON instead).
/// </summary>
[Category("Unit")]
public sealed class StageValueReportTests
{
    private const string Api = "http://stage-value-report.test";

    private sealed record StageReportRun(int Exit, string Output, string Error, RecordingOperatorHttp Http, FakeGit Git);

    private static string Row(string stage, int runs, int found, int clean, int skipped = 0, int failed = 0, int unreported = 0, string hitPercent = "null",
        string usdSpent = "0", string usdPerFinding = "null", int serverSecs = 0) =>
        "{\"stage\":\"" + stage + "\",\"runs\":" + runs + ",\"found\":" + found + ",\"clean\":" + clean + ",\"skipped\":" + skipped + ",\"failed\":" + failed
        + ",\"unreported\":" + unreported + ",\"hitPercent\":" + hitPercent + ",\"usdSpent\":" + usdSpent + ",\"usdPerFinding\":" + usdPerFinding
        + ",\"serverSecs\":" + serverSecs + "}";

    private static StageReportRun Report(string list, string? card, params string[] arguments)
    {
        var http = new RecordingOperatorHttp(call =>
        {
            if (call.Method == "GET" && call.Uri.Contains("/api/cards/", StringComparison.Ordinal))
                return card == null ? throw new InvalidOperationException("unexpected card lookup: " + call.Uri) : new NightlyReportHttpResponse(200, card);
            if (call.Method == "GET" && call.Uri.Contains("/api/stage-outcomes", StringComparison.Ordinal)) return new NightlyReportHttpResponse(200, list);
            throw new InvalidOperationException("unhandled " + call.Method + " " + call.Uri);
        });
        var git = new FakeGit("/work/antiphon" + Environment.NewLine);
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(),
            Git = git.Run,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
        };
        var exit = StageValueReport.Run(StageValueReportOptions.Parse(["--api", Api, .. arguments], null, null, "/work/antiphon/sub"), host, http.Transport);
        return new StageReportRun(exit, output.ToString(), error.ToString(), http, git);
    }

    private static string[] Outcomes(StageReportRun run) => run.Http.Calls.Where(call => call.Uri.Contains("/api/stage-outcomes", StringComparison.Ordinal)).Select(call => call.Uri).ToArray();

    [Test]
    public void T1_table_shape_header_rule_one_row_per_stage_and_no_query()
    {
        var list = "{\"rows\":[],\"summary\":[" + string.Join(",",
            Row("Rebase", 47, 1, 46, hitPercent: "2.1", usdSpent: "2.51", usdPerFinding: "2.51"),
            Row("Verify", 47, 0, 13, skipped: 34, hitPercent: "0.0", serverSecs: 5200),
            Row("Cleanup", 47, 0, 38, failed: 9),
            Row("Deploy", 3, 3, 0, hitPercent: "100.0", usdSpent: "4.50", usdPerFinding: "1.50")) + "]}";
        var run = Report(list, null);

        run.Exit.ShouldBe(0, run.Error);
        var lines = run.Output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        lines[0].ShouldMatch(@"^Stage\s+Runs\s+Found\s+Clean\s+Skipped\s+Failed\s+Unreported\s+Hit%\s+UsdSpent\s+Usd/Finding\s+ServerSecs\s*$");
        lines[1].ShouldMatch(@"^-+(  -+){10}$");
        lines.Length.ShouldBe(6, "header, rule and one line per stage");
        lines[2].ShouldStartWith("Rebase ");
        lines[5].ShouldStartWith("Deploy ");
        run.Output.ShouldContain("100.0%");
        run.Output.ShouldContain("$1.50");
        run.Output.ShouldContain("$2.51");
        lines[4].ShouldContain(" - ", Case.Sensitive, "a null hit percent and USD per finding print as -");
        // Every column is padded to its widest cell.
        lines.Select(line => line.Length).Distinct().ShouldHaveSingleItem();
        Outcomes(run).ShouldHaveSingleItem().ShouldBe(Api + "/api/stage-outcomes");
        run.Git.Calls.ShouldBeEmpty("no card, no checkout lookup");
    }

    [Test]
    public void T2_filters_build_the_query_and_the_card_resolves_to_its_guid()
    {
        var list = "{\"rows\":[],\"summary\":[" + Row("Review", 2, 1, 1, hitPercent: "50.0") + "]}";
        const string card = """{"id":"aaaaaaaa-1111-2222-3333-444444444444","identifier":"CARD-0272"}""";
        var run = Report(list, card, "--since", "2026-09-01T00:00:00Z", "--until", "2026-09-04T00:00:00Z", "--stage", "review", "--card", "CARD-0272");

        run.Exit.ShouldBe(0, run.Error);
        var cards = run.Http.Calls.Where(call => call.Uri.Contains("/api/cards/", StringComparison.Ordinal)).ToArray();
        cards.ShouldHaveSingleItem().Path.ShouldBe("/api/cards/CARD-0272?cwd=" + Uri.EscapeDataString("/work/antiphon"));
        run.Git.Calls.ShouldHaveSingleItem().Arguments.ShouldBe(["rev-parse", "--show-toplevel"]);
        run.Git.Calls[0].WorkingDirectory.ShouldBe("/work/antiphon/sub");
        var uri = Outcomes(run).ShouldHaveSingleItem();
        uri.ShouldContain("since=2026-09-01");
        uri.ShouldContain("until=2026-09-04");
        uri.ShouldContain("stage=Review", Case.Sensitive, "the stage is sent in its listed spelling");
        uri.ShouldContain("cardId=aaaaaaaa-1111-2222-3333-444444444444");
    }

    [Test]
    public void T3_json_prints_the_raw_dto()
    {
        var list = "{\"rows\":[{\"id\":\"r1\",\"stage\":\"Deploy\"}],\"summary\":[" + Row("Deploy", 1, 1, 0) + "]}";
        var run = Report(list, null, "--json");

        run.Exit.ShouldBe(0, run.Error);
        run.Output.ShouldContain("\"stage\"");
        run.Output.ShouldContain("\"r1\"");
        run.Output.ShouldNotContain("Usd/Finding");
    }

    [Test]
    public void T4_empty_summary_prints_the_friendly_message_and_no_table()
    {
        var run = Report("""{"rows":[],"summary":[]}""", null);
        run.Exit.ShouldBe(0, run.Error);
        run.Output.Trim().ShouldBe("No stage outcomes in range.");
    }

    [Test]
    public void T5_include_superseded_adds_latestOnly_false()
    {
        var run = Report("{\"rows\":[],\"summary\":[" + Row("Rebase", 1, 0, 1) + "]}", null, "--include-superseded");
        Outcomes(run).ShouldHaveSingleItem().ShouldContain("latestOnly=false");
    }

    [Test]
    public void A_failed_request_is_one_line_and_exit_1()
    {
        var http = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(500, """{"detail":"database unavailable"}"""));
        var output = new StringWriter();
        var error = new StringWriter();
        var host = NightlyHost.Production(output, error);
        StageValueReport.Run(StageValueReportOptions.Parse([], Api, null, "."), host, http.Transport).ShouldBe(1);
        error.ToString().Trim().ShouldBe("Antiphon GET /api/stage-outcomes failed: database unavailable");
        output.ToString().ShouldBeEmpty();
    }

    [Test]
    public void Options_take_the_cli_api_and_token_and_refuse_an_unknown_stage()
    {
        var options = StageValueReportOptions.Parse([], "http://api.test/", "tok", ".");
        (options.Api, options.TaskToken).ShouldBe(("http://api.test", "tok"));
        StageValueReportOptions.Parse(["--api", "http://other.test"], "http://api.test/", "tok", ".").Api.ShouldBe("http://other.test");
        Should.Throw<NightlyUsageException>(() => StageValueReportOptions.Parse(["--stage", "Land"], null, null, ".")).Message.ShouldContain("--stage");
        var http = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(200, """{"rows":[],"summary":[]}"""));
        StageValueReport.Run(options, NightlyHost.Production(new StringWriter(), new StringWriter()), http.Transport).ShouldBe(0);
        http.Calls.ShouldHaveSingleItem().Headers[OperatorApi.TaskTokenHeader].ShouldBe("tok");
    }

    [Test]
    public void Ported_source_is_ascii_and_the_scripts_are_gone()
    {
        foreach (var path in new[] { "scripts/lib/Antiphon.Nightly/StageValueReport.cs" })
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, path)).Count(b => b > 127).ShouldBe(0, path + " is ASCII-only");
        foreach (var path in new[] { "scripts/stage-value-report.ps1", "scripts/test-stage-value-report.ps1" })
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, path)).ShouldBeFalse(path + " is ported to C#");
    }
}
