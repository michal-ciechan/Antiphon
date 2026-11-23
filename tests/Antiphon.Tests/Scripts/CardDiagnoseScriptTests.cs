using Antiphon.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0352 S4: the card command's diagnose (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1 in unit 27 of the
/// 2026-11-22 plan) against a stub transport. It posts the forced diagnose route and prints the ledger row, or with
/// <c>--no-wait</c> prints the 202. The poll waits for a row no older than the card, on a fake clock. Nothing leaves the
/// process.
/// </summary>
[Category("Integration")]
public sealed class CardDiagnoseScriptTests
{
    private const string CardId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private const string FreshRow = """
        [{"id":"44444444-4444-4444-4444-444444444444","kind":"Labels","outcome":"Applied",
          "cardId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","cardIdentifier":"CARD-0001",
          "applied":"complexity=medium ui=no","answer":"complexity=medium ui=no",
          "costUsd":0.0091,"waitMs":1200,"forced":true,"createdAt":"2100-01-01T00:00:00Z"}]
        """;

    [Test]
    public async Task diagnose_posts_the_route_and_prints_the_ledger_row()
    {
        var server = new StubApi();
        var run = await RunAsync(server, "diagnose", "CARD-0001");

        run.ExitCode.ShouldBe(0, run.Output);
        server.PostedDiagnose.ShouldBeTrue();
        server.LastDiagnosePath.ShouldContain($"/api/cards/{CardId}/diagnose");
        run.Output.ShouldContain("CARD-0001");
        run.Output.ShouldContain("Applied");
        run.Output.ShouldContain("complexity=medium ui=no");
        run.Output.ShouldContain("cost        0.0091  wait 1200ms");
        server.LastDiagnoseBody.ShouldBe("{}");
    }

    [Test]
    public async Task diagnose_NoWait_prints_the_202_and_does_not_poll()
    {
        var server = new StubApi();
        var run = await RunAsync(server, "diagnose", "CARD-0001", "--no-wait");

        run.ExitCode.ShouldBe(0, run.Output);
        server.PostedDiagnose.ShouldBeTrue();
        server.PolledDiagnoses.ShouldBeFalse();
        run.Output.ShouldContain("202");
        run.Output.ShouldContain("queued");
    }

    [Test]
    public void diagnose_skips_a_row_older_than_the_card_and_times_out_after_120_seconds()
    {
        var stale = new StubApi(FreshRow.Replace("2100-01-01T00:00:00Z", "1999-01-01T00:00:00Z", StringComparison.Ordinal));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        var run = CardCommandRun.Run(stale.Transport, ["diagnose", "CARD-0001"], clock: clock);

        run.Exit.ShouldBe(1, run.All);
        run.Output.ShouldBeEmpty();
        run.Error.Trim().ShouldBe("Timed out waiting for a diagnosis of CARD-0001. Pass --no-wait and poll GET /api/diagnoses?cardId=" + CardId + ".");
        stale.Polls.ShouldBe(60, "one poll every 2 s for 120 s");
        clock.GetUtcNow().ShouldBe(new DateTimeOffset(2026, 11, 23, 9, 2, 0, TimeSpan.Zero));
    }

    private static Task<(int ExitCode, string Output)> RunAsync(StubApi server, params string[] args)
    {
        var run = CardCommandRun.Run(server.Transport, args, taskToken: "");
        return Task.FromResult((run.Exit, run.Output + run.Error));
    }

    private sealed class StubApi(string rows = FreshRow)
    {
        public bool PostedDiagnose { get; private set; }
        public bool PolledDiagnoses => Polls > 0;
        public int Polls { get; private set; }
        public string? LastDiagnosePath { get; private set; }
        public string? LastDiagnoseBody { get; private set; }

        public NightlyReportHttp Transport => (method, uri, _, body) =>
        {
            var address = new Uri(uri);
            var path = address.AbsolutePath;
            if (method == "GET" && path == "/api/cards/limits")
                return new(200, """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":5,"importanceValues":["Low","Normal","High","Critical"],"urgencyValues":["Normal","Soon","Now"]}""");
            if (method == "GET" && path.StartsWith("/api/cards/", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/diagnose", StringComparison.OrdinalIgnoreCase) && !path.Contains("/revisions", StringComparison.OrdinalIgnoreCase))
                return new(200, $$"""
                    {"id":"{{CardId}}","identifier":"CARD-0001","status":"Backlog","title":"Unlabelled",
                     "importance":"Normal","urgency":"Normal","rank":0,"importanceProvenance":"Auto",
                     "concurrencyToken":"11111111-1111-1111-1111-111111111111","revisionCount":0,
                     "boardId":"22222222-2222-2222-2222-222222222222","boardColumnId":"33333333-3333-3333-3333-333333333333",
                     "labels":[],"updatedAt":"2000-01-01T00:00:00Z"}
                    """);
            if (method == "POST" && path.Contains("/diagnose", StringComparison.OrdinalIgnoreCase))
            {
                PostedDiagnose = true;
                LastDiagnosePath = address.PathAndQuery;
                LastDiagnoseBody = body;
                return new(202, """{"queued":true}""");
            }
            if (method == "GET" && path.StartsWith("/api/diagnoses", StringComparison.OrdinalIgnoreCase))
            {
                Polls++;
                return new(200, rows);
            }
            return new(404, """{"detail":"not stubbed"}""");
        };
    }
}
