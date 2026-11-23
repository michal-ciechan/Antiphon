using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0658: the hangfire-dashboard command (HangfireDashboard.Run), in process. It replaced scripts/hangfire-dashboard.ps1
/// (unit 25 of the 2026-11-22 plan), which had no test of its own. The operator token file is a mock file, HTTP a recording
/// transport and the browser a recording seam: no browser is ever opened and no request leaves the process. Every case also
/// checks that neither the token nor the login link reaches the output.
/// </summary>
[Category("Unit")]
public sealed class HangfireDashboardCommandTests
{
    private const string Api = "http://127.0.0.1:17202";
    private const string Token = "operator-secret-token";
    private const string LoginPath = "/hangfire-login?nonce=secret-nonce-123";

    private sealed record DashboardRun(int Exit, string Output, string Error, RecordingOperatorHttp Http, List<string> Opened);

    private static DashboardRun Open(Func<RecordingOperatorHttp.Call, NightlyReportHttpResponse> answer, string? token = Token, bool browser = true,
        Action<string>? openBrowser = null)
    {
        var files = new MockFileSystem();
        var tokenFile = files.Path.Combine(files.Path.GetTempPath(), "Antiphon", "operator-token");
        if (token != null) files.AddFile(tokenFile, new MockFileData(token + "\n"));
        var http = new RecordingOperatorHttp(answer);
        var opened = new List<string>();
        var output = new StringWriter();
        var error = new StringWriter();
        var options = HangfireDashboardOptions.Parse([], Api + "/", tokenFile);
        var exit = HangfireDashboard.Run(options, NightlyHost.Production(output, error), files, http.Transport,
            browser ? openBrowser ?? new Action<string>(opened.Add) : null);
        var run = new DashboardRun(exit, output.ToString().Trim(), error.ToString().Trim(), http, opened);
        (run.Output + run.Error).ShouldNotContain(Token);
        (run.Output + run.Error).ShouldNotContain("secret-nonce-123");
        return run;
    }

    [Test]
    public void Opens_the_one_time_login_link_with_the_operator_token()
    {
        var run = Open(_ => new NightlyReportHttpResponse(200, "{\"loginPath\":\"" + LoginPath + "\"}"));

        run.Exit.ShouldBe(0, run.Error);
        run.Output.ShouldBe("Opened the Hangfire dashboard in the default browser (session lasts 12 hours).");
        var call = run.Http.Calls.ShouldHaveSingleItem();
        (call.Method, call.Uri).ShouldBe(("POST", Api + "/api/operator/dashboard-sessions"));
        call.Headers[OperatorApi.OperatorTokenHeader].ShouldBe(Token, "the token file is read and trimmed");
        run.Opened.ShouldBe([Api + LoginPath]);
    }

    [Test]
    public void A_refused_login_is_reported_by_its_problem_code_only()
    {
        var refused = Open(_ => new NightlyReportHttpResponse(401, """{"code":"operator_token_invalid","detail":"secret-nonce-123 rejected"}"""));
        (refused.Exit, refused.Output).ShouldBe((1, "The server refused the dashboard login: operator_token_invalid"));
        refused.Opened.ShouldBeEmpty();

        var plain = Open(_ => new NightlyReportHttpResponse(502, "bad gateway"));
        (plain.Exit, plain.Output).ShouldBe((1, "The server refused the dashboard login: request_failed"));

        var unreachable = Open(_ => throw new HttpRequestException("connection refused (" + Api + ")"));
        (unreachable.Exit, unreachable.Output).ShouldBe((1, "The server refused the dashboard login: request_failed"));
    }

    [Test]
    public void An_answer_without_a_rooted_login_path_is_refused()
    {
        foreach (var body in new[] { "{}", """{"loginPath":"https://elsewhere.test/x"}""", "not json" })
        {
            var run = Open(_ => new NightlyReportHttpResponse(200, body));
            (run.Exit, run.Output).ShouldBe((1, "The server returned no usable login link."), body);
            run.Opened.ShouldBeEmpty();
        }
    }

    [Test]
    public void A_browser_that_cannot_start_is_a_fixed_message_without_the_link()
    {
        var run = Open(_ => new NightlyReportHttpResponse(200, "{\"loginPath\":\"" + LoginPath + "\"}"),
            openBrowser: address => throw new System.ComponentModel.Win32Exception(2, "cannot open " + address));
        (run.Exit, run.Output).ShouldBe((1, "The default browser could not be started."));
    }

    [Test]
    public void A_missing_or_empty_token_file_refuses_before_any_request()
    {
        var missing = Open(_ => throw new InvalidOperationException("no request expected"), token: null);
        missing.Exit.ShouldBe(1);
        missing.Error.ShouldStartWith("Operator token file not found at ");
        missing.Http.Calls.ShouldBeEmpty();

        var empty = Open(_ => throw new InvalidOperationException("no request expected"), token: "   ");
        empty.Exit.ShouldBe(1);
        empty.Error.ShouldEndWith("is empty.");
        empty.Http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Without_a_browser_seam_it_explains_the_curl_route_and_answers_2()
    {
        var run = Open(_ => throw new InvalidOperationException("no request expected"), browser: false);
        run.Exit.ShouldBe(2);
        run.Output.ShouldStartWith("Opening a browser is only supported on Windows.");
        run.Http.Calls.ShouldBeEmpty();
    }

    [Test]
    public void The_server_texts_name_the_command_and_the_source_is_ascii()
    {
        var endpoints = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "server", "Api", "Endpoints", "OperatorEndpoints.cs"));
        endpoints.ShouldContain("The dashboard login requires the operator token (scripts/antiphon hangfire-dashboard sends it).");
        endpoints.ShouldContain("The dashboard login link is invalid or has expired; run scripts/antiphon hangfire-dashboard again.");
        endpoints.ShouldNotContain("hangfire-dashboard.ps1");
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "HangfireDashboard.cs")).Count(b => b > 127).ShouldBe(0);
        File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "hangfire-dashboard.ps1")).ShouldBeFalse("the dashboard login is a command");
    }
}
