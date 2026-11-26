using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0330 S4. The distiller command (Antiphon.Nightly.DistillerLedger, which replaced scripts/distiller.ps1 in unit 29 of the
/// 2026-11-22 plan) in process, on a recording transport and a fake clock: the stats and their sections, the ledger rows and the
/// flagged filter, the since window (a day count against the clock, any other value as given), the failures and the command
/// line. Nothing leaves the process.
/// </summary>
[Category("Unit")]
public sealed class DistillerCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Stats_print_the_totals_the_cost_and_each_section_in_the_servers_order()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """
            {"since":"2026-11-16T09:00:00Z","total":12,"byOutcome":{"Applied":9,"Timeout":3},"byFeedback":{"None":10,"LostInformation":2},
             "byBundleStamp":{"v3":12},"medianRatio":0.31,"p90Ratio":0.5,"topMissingAnchorClasses":["sha","path"],"fullReadRate":0.25,"costUsd":0.0123}
            """));

        var run = Run(http, "--stats", "--since", " 7D ");

        run.Exit.ShouldBe(0, run.All);
        run.Error.ShouldBeEmpty();
        Lines(run.Output).ShouldBe([
            "total=12 cost=$0.0123 fullReadRate=0.25",
            "byOutcome:", "  Applied 9", "  Timeout 3",
            "byFeedback:", "  None 10", "  LostInformation 2",
            "topMissing:", "  sha", "  path",
        ]);
        var call = http.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe("GET");
        call.Uri.ShouldBe("http://127.0.0.1:17202/api/distillations/stats?since=2026-11-16T09%3A00%3A00.0000000Z");
        call.Headers.ShouldBeEmpty();
        call.Body.ShouldBeNull();
    }

    [Test]
    public void Stats_keep_an_empty_section_heading_and_leave_out_what_the_answer_lacks()
    {
        // PowerShell's truth: an empty object is true, an empty list and a missing value are false.
        var http = new RecordingOperatorHttp(_ => new(200, """{"total":0,"byOutcome":{},"topMissingAnchorClasses":[],"fullReadRate":null,"costUsd":0}"""));

        var run = Run(http, "--stats", "--since", "2026-11-01T00:00:00+01:00");

        run.Exit.ShouldBe(0, run.All);
        Lines(run.Output).ShouldBe(["total=0 cost=$0 fullReadRate=", "byOutcome:"]);
        http.Calls.Single().Uri.ShouldBe("http://127.0.0.1:17202/api/distillations/stats?since=2026-11-01T00%3A00%3A00%2B01%3A00");

        var empty = new RecordingOperatorHttp(_ => new(200, ""));
        var nothing = Run(empty, "--stats");
        nothing.Exit.ShouldBe(0, nothing.All);
        Lines(nothing.Output).ShouldBe(["total= cost=$ fullReadRate="]);
        empty.Calls.Single().Uri.ShouldBe("http://127.0.0.1:17202/api/distillations/stats");
    }

    [Test]
    public void List_prints_one_line_per_row_and_flagged_keeps_lost_and_noisy_rows()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """
            [{"taskShortId":"aaaa1111","outcome":"Applied","rawChars":9000,"distilledChars":1200,"bundleStamp":"v3","feedback":"None"},
             {"taskShortId":"bbbb2222","outcome":"Applied","rawChars":8000,"distilledChars":900,"bundleStamp":"v3","feedback":"LostInformation"},
             {"taskShortId":"cccc3333","outcome":"Timeout","rawChars":5000,"distilledChars":0,"bundleStamp":null,"feedback":"noisy"},
             {"taskShortId":"dddd4444","outcome":"Applied","rawChars":4500,"distilledChars":700,"bundleStamp":"v2","feedback":"Good"},
             {"taskShortId":"eeee5555","outcome":"Shadow","rawChars":4100,"distilledChars":600,"bundleStamp":"v2"}]
            """));

        var all = Run(http, "--list");
        all.Exit.ShouldBe(0, all.All);
        Lines(all.Output).ShouldBe([
            "aaaa1111  Applied  9000->1200  v3  flag=-",
            "bbbb2222  Applied  8000->900  v3  flag=LostInformation",
            "cccc3333  Timeout  5000->0    flag=noisy",
            "dddd4444  Applied  4500->700  v2  flag=Good",
            "eeee5555  Shadow  4100->600  v2  flag=-",
        ]);
        http.Calls[^1].Uri.ShouldBe("http://127.0.0.1:17202/api/distillations");

        var flagged = Run(http, "--list", "--flagged", "--since", "1d");
        flagged.Exit.ShouldBe(0, flagged.All);
        Lines(flagged.Output).ShouldBe(["bbbb2222  Applied  8000->900  v3  flag=LostInformation", "cccc3333  Timeout  5000->0    flag=noisy"]);
        http.Calls[^1].Uri.ShouldBe("http://127.0.0.1:17202/api/distillations?since=2026-11-22T09%3A00%3A00.0000000Z");
    }

    [Test]
    public void A_failed_request_is_one_line_and_exit_1()
    {
        var refused = Run(new RecordingOperatorHttp(_ => new(400, """{"title":"Bad since"}""")), "--list", "--since", "yesterday");
        refused.Exit.ShouldBe(1);
        refused.Output.ShouldBeEmpty();
        refused.Error.Trim().ShouldBe("""distiller failed: HTTP 400 {"title":"Bad since"}""");

        var down = Run(new RecordingOperatorHttp(_ => throw new HttpRequestException("No connection could be made (127.0.0.1:17202)")), "--stats");
        down.Exit.ShouldBe(1);
        down.Error.Trim().ShouldBe("distiller failed: No connection could be made (127.0.0.1:17202)");

        var html = Run(new RecordingOperatorHttp(_ => new(200, "<html>")), "--list");
        html.Exit.ShouldBe(1);
        html.Error.ShouldStartWith("distiller failed: GET /api/distillations answered with a body that is not JSON: ");

        var none = new RecordingOperatorHttp(_ => throw new InvalidOperationException("no request is sent"));
        var range = Run(none, "--stats", "--since", "99999999999d");
        range.Exit.ShouldBe(1);
        range.Error.Trim().ShouldBe("distiller failed: --since '99999999999d' is out of range");
        Run(none, "--stats", "--since", "2147483647d").Exit.ShouldBe(1);
        none.Calls.ShouldBeEmpty();
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => DistillerLedgerOptions.Parse([], null)).Message.ShouldBe("one of --stats or --list is required");
        Should.Throw<NightlyUsageException>(() => DistillerLedgerOptions.Parse(["--stats", "--list"], null)).Message.ShouldBe("--stats and --list are exclusive");
        Should.Throw<NightlyUsageException>(() => DistillerLedgerOptions.Parse(["--stats", "--flagged"], null)).Message.ShouldBe("--flagged applies to --list only");
        Should.Throw<NightlyUsageException>(() => DistillerLedgerOptions.Parse(["-Stats"], null));
        Should.Throw<NightlyUsageException>(() => DistillerLedgerOptions.Parse(["--list", "--since"], null)).Message.ShouldBe("missing value for --since");
        var options = DistillerLedgerOptions.Parse(["--list", "--flagged", "--since", "7d"], "http://api.test/");
        (options.Stats, options.Flagged, options.Since, options.Api).ShouldBe((false, true, "7d", "http://api.test"));
        DistillerLedgerOptions.Parse(["--stats"], " ").Api.ShouldBe("http://localhost:17202");
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var options = DistillerLedgerOptions.Parse(args, BoardCommandRun.Api);
            var exit = DistillerLedger.Run(options, CardCommandRun.Host(output, error, new FakeTimeProvider(Now)), http.Transport);
            return new BoardCommandRun.Result(exit, output.ToString(), error.ToString());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine("usage: " + e.Message);
            return new BoardCommandRun.Result(2, output.ToString(), error.ToString());
        }
    }
}
