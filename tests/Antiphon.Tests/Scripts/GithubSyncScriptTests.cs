using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0171 S2: the github-sync command (Antiphon.Nightly.GithubSync), in process on a recording transport that answers as
/// the Antiphon API. It replaced the body of scripts/github-sync.ps1 (unit 25 of the 2026-11-22 plan), which stays as the
/// thin launcher Windmill's u/lndcobra/antiphon_github_sync starts by path (its parameters are covered in
/// AntiphonCliLauncherTests). Nothing here talks to GitHub or a server.
/// </summary>
[Category("Unit")]
public sealed class GithubSyncScriptTests
{
    private const string BaseUrl = "http://127.0.0.1:17202";

    private static (BoardCommandRun.Result Run, RecordingOperatorHttp Http) Sync(string body, int status = 200, params string[] args)
    {
        var http = new RecordingOperatorHttp(_ => new NightlyReportHttpResponse(status, body));
        var run = BoardCommandRun.Run(host => GithubSync.Run(GithubSyncOptions.Parse(["--base-url", BaseUrl + "/", .. args]), host, http.Transport));
        return (run, http);
    }

    [Test]
    public void Notify_forwards_the_query_parameter()
    {
        var (run, http) = Sync(CleanBody(), 200, "--notify");

        run.Exit.ShouldBe(0, run.All);
        var call = http.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe("POST");
        call.Path.ShouldBe("/api/tracker-sync/run?notify=true");
        call.Headers.ShouldBeEmpty("the server holds the GitHub token; no credential is sent");
        run.Output.ShouldContain("POST " + BaseUrl + "/api/tracker-sync/run?notify=true");
    }

    [Test]
    public void Without_Notify_the_query_is_empty()
    {
        var (run, http) = Sync(CleanBody());

        run.Exit.ShouldBe(0, run.All);
        new Uri(http.Calls.ShouldHaveSingleItem().Uri).Query.ShouldBeEmpty();
    }

    [Test]
    public void A_board_error_exits_1()
    {
        // Before CARD-0171 this exited 0, so a failed sync was a green Windmill job.
        const string body = """
            {"boards":[{"boardId":"11111111-1111-1111-1111-111111111111","boardName":"Antiphon board",
            "issuesPulled":0,"commentsIn":0,"commentsOut":0,"labelsChanged":0,"stateChanges":0,
            "creates":0,"externalReopens":0,"skips":[],"changes":[],
            "error":"GitHub returned 401 Unauthorized"}],"concurrentRunSkipped":false,"notifications":[]}
            """;
        var (run, _) = Sync(body);

        run.Exit.ShouldBe(1, run.All);
        run.Output.ShouldContain("error=GitHub returned 401 Unauthorized");
        run.Output.ShouldContain("1 board(s) reported an error.");
    }

    [Test]
    public void A_clean_body_exits_0_and_prints_the_notification_lines()
    {
        const string body = """
            {"boards":[{"boardId":"11111111-1111-1111-1111-111111111111","boardName":"Antiphon board",
            "issuesPulled":12,"commentsIn":2,"commentsOut":1,"labelsChanged":3,"stateChanges":1,
            "creates":0,"externalReopens":1,"skips":["closed_pr","label_conflict"],
            "changes":[{"kind":"CommentIn","cardIdentifier":"CARD-0170","externalKey":"#10","url":null}]}],
            "concurrentRunSkipped":true,
            "notifications":[
              {"boardId":"11111111-1111-1111-1111-111111111111","sent":true,
               "channelId":"caee9d25-b751-4401-a295-3b7e242842aa","reason":null},
              {"boardId":"22222222-2222-2222-2222-222222222222","sent":false,
               "channelId":null,"reason":"notify_channel_unset"}]}
            """;
        var (run, _) = Sync(body, 200, "--notify");

        run.Exit.ShouldBe(0, run.All);
        run.Output.ShouldContain("HTTP 200");
        run.Output.ShouldContain("  Antiphon board: pulled=12 commentsIn=2 commentsOut=1 labels=3 state=1 creates=0 reopens=1 changes=1 skips=[closed_pr, label_conflict]"
            + Environment.NewLine);
        run.Output.ShouldContain("  (concurrent run skipped for at least one board)");
        run.Output.ShouldContain("notified board 11111111-1111-1111-1111-111111111111 -> channel caee9d25-b751-4401-a295-3b7e242842aa");
        run.Output.ShouldContain("NOT notified board 22222222-2222-2222-2222-222222222222: notify_channel_unset");
    }

    [Test]
    public void A_board_id_targets_the_per_board_endpoint()
    {
        var (run, http) = Sync(CleanBody(), args: ["--board-id", "8988CA03-7414-47AD-B0B6-51556C701703", "--notify"]);

        run.Exit.ShouldBe(0, run.All);
        http.Calls.ShouldHaveSingleItem().Path.ShouldBe("/api/boards/8988ca03-7414-47ad-b0b6-51556c701703/tracker/sync?notify=true");
        Should.Throw<NightlyUsageException>(() => GithubSyncOptions.Parse(["--board-id", "not-a-guid"]));
    }

    [Test]
    public void A_non_success_response_exits_1()
    {
        var (run, _) = Sync("""{"title":"boom"}""", status: 500);

        run.Exit.ShouldBe(1, run.All);
        run.Error.ShouldContain("HTTP 500 from " + BaseUrl + "/api/tracker-sync/run");
        run.Error.ShouldContain("""{"title":"boom"}""");
        run.Output.ShouldNotContain("HTTP 500", Case.Sensitive, "a refusal is on stderr only");
    }

    [Test]
    public void A_body_that_is_not_json_prints_as_is_and_exits_0()
    {
        var (run, _) = Sync("sync queued");

        run.Exit.ShouldBe(0, run.All);
        run.Output.ShouldContain("sync queued");
    }

    [Test]
    public void The_default_base_url_is_the_apphost_port_and_the_source_is_ascii()
    {
        var options = GithubSyncOptions.Parse([]);
        (options.BaseUrl, options.BoardId, options.Notify).ShouldBe(("http://localhost:17202", (Guid?)null, false));
        foreach (var path in new[] { "scripts/lib/Antiphon.Nightly/GithubSync.cs", "scripts/github-sync.ps1" })
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, path)).Count(b => b > 127).ShouldBe(0, path + " is ASCII-only");
    }

    private static string CleanBody() =>
        """
        {"boards":[{"boardId":"11111111-1111-1111-1111-111111111111","boardName":"Antiphon board",
        "issuesPulled":0,"commentsIn":0,"commentsOut":0,"labelsChanged":0,"stateChanges":0,
        "creates":0,"externalReopens":0,"skips":[],"changes":[]}],
        "concurrentRunSkipped":false,"notifications":[]}
        """;
}
