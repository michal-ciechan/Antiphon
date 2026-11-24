using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-10. The delegate command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the
/// 2026-11-22 plan) in process against a loopback capture (the card half is CardPlatformScriptTests).
/// </summary>
[Category("Integration")]
public sealed class DelegatePlatformScriptTests
{
    [Test]
    public async Task Omission_and_explicit_any_have_distinct_payloads()
    {
        using var omitted = new DelegateCreateStubApi(Created("Any", "server2"));
        var without = await DelegateScriptRunner.RunAsync(omitted.BaseUrl, ["create", "--role", "Code", "--goal", "inherit"]);
        without.ExitCode.ShouldBe(0, without.Output);
        omitted.LastBody!.RootElement.TryGetProperty("requiredPlatform", out _).ShouldBeFalse();

        using var any = new DelegateCreateStubApi(Created("Any", "server2"));
        var with = await DelegateScriptRunner.RunAsync(any.BaseUrl, ["create", "--role", "Code", "--goal", "reset", "--platform", "Any"]);
        with.ExitCode.ShouldBe(0, with.Output);
        any.LastBody!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Any");
        with.Output.ShouldContain("platform: Any");
    }

    [Test]
    public async Task Desktop_alias_preserves_workspace()
    {
        using var server = new DelegateCreateStubApi(Created("Any", "desktop"));
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            ["create", "--role", "Code", "--goal", "stay shared", "--shared", "--runner", "desktop"]);
        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody!.RootElement;
        body.GetProperty("runnerId").GetString().ShouldBe("desktop");
        body.GetProperty("workspace").GetString().ShouldBe("Shared");
    }

    [Test]
    public async Task Remote_guards_and_platform_forwarding()
    {
        using var invalid = new DelegateCreateStubApi();
        var refused = await DelegateScriptRunner.RunAsync(invalid.BaseUrl, ["create", "--role", "Code", "--goal", "nope", "--platform", "Mac"]);
        refused.ExitCode.ShouldNotBe(0);
        invalid.RequestCount.ShouldBe(0);

        using var remote = new DelegateCreateStubApi(Created("Linux", "server2"));
        var sent = await DelegateScriptRunner.RunAsync(remote.BaseUrl,
            ["create", "--role", "Code", "--goal", "linux", "--runner", "server2", "--platform", "Linux"]);
        sent.ExitCode.ShouldBe(0, sent.Output);
        var body = remote.LastBody!.RootElement;
        body.GetProperty("runnerId").GetString().ShouldBe("server2");
        body.GetProperty("workspace").GetString().ShouldBe("Worktree");
        body.GetProperty("requiredPlatform").GetString().ShouldBe("Linux");
    }

    [Test]
    public async Task Server_mismatch_is_reported_without_retry()
    {
        using var server = new DelegateCreateStubApi(
            """{"title":"runner_platform_mismatch","status":409,"detail":"Runner 'server2' is linux and cannot run a Windows task."}""",
            statusCode: 409);
        var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
            ["create", "--role", "Code", "--goal", "windows on linux", "--runner", "server2", "--platform", "Windows"]);
        run.ExitCode.ShouldNotBe(0, run.Output);
        run.Output.ShouldContain("runner_platform_mismatch");
        server.RequestCount.ShouldBe(1);
    }

    private static string Created(string platform, string runner) =>
        $$"""{"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111","status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode","requiredPlatform":"{{platform}}","runnerId":"{{runner}}"}""";
}

/// <summary>
/// CARD-0710 V-10: the card command (Antiphon.Nightly.CardCli, which replaced scripts/card.ps1 in unit 27 of the 2026-11-22
/// plan) forwards --platform on new and edit, Any included, in process against a stub transport. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class CardPlatformScriptTests
{
    [Test]
    public void New_edit_and_reset_forward_platform()
    {
        var created = new CardPlatformStub();
        var board = "c7100000-aaaa-bbbb-cccc-0000000000bb";
        var fresh = Run(created, ["new", "--board", board, "--title", "platform card", "--platform", "Windows"]);
        fresh.ExitCode.ShouldBe(0, fresh.Output);
        created.LastCreate!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Windows");
        fresh.Output.ShouldContain("platform    Windows");

        var edited = new CardPlatformStub();
        var edit = Run(edited, ["edit", "CARD-0710", "--platform", "Linux", "--reason", "need linux"]);
        edit.ExitCode.ShouldBe(0, edit.Output);
        edited.LastPatch!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Linux");

        var reset = new CardPlatformStub();
        var any = Run(reset, ["edit", "CARD-0710", "--platform", "any", "--reason", "reset"]);
        any.ExitCode.ShouldBe(0, any.Output);
        reset.LastPatch!.RootElement.GetProperty("requiredPlatform").GetString().ShouldBe("Any");
    }

    private static (int ExitCode, string Output) Run(CardPlatformStub api, string[] args)
    {
        var run = Antiphon.Tests.Scripts.CardCommandRun.Run(api.Transport, args);
        return (run.Exit, run.Output + run.Error);
    }

    private sealed class CardPlatformStub
    {
        private const string CardId = "c7100000-aaaa-bbbb-cccc-0000000000cc";
        private const string Token = "c7100000-aaaa-bbbb-cccc-000000000001";

        public JsonDocument? LastCreate { get; private set; }
        public JsonDocument? LastPatch { get; private set; }

        public Antiphon.Nightly.NightlyReportHttp Transport => (method, uri, _, body) =>
        {
            var path = new Uri(uri).AbsolutePath;
            var response = Card();
            if (method == "GET" && path == "/api/cards/limits")
                response = """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxPrivateNotesLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":6}""";
            else if (method == "POST" && path.Contains("/cards", StringComparison.Ordinal))
            {
                LastCreate = JsonDocument.Parse(body!);
                response = Card("Windows");
            }
            else if (method == "PATCH")
            {
                LastPatch = JsonDocument.Parse(body!);
                var platform = LastPatch.RootElement.TryGetProperty("requiredPlatform", out var value) ? value.GetString() : "Any";
                response = Card(platform);
            }
            return new Antiphon.Nightly.NightlyReportHttpResponse(200, response);
        };

        private static string Card(string? platform = "Any") =>
            $$"""{"id":"{{CardId}}","boardId":"c7100000-aaaa-bbbb-cccc-0000000000bb","identifier":"CARD-0710","title":"platform card","status":"Backlog","importance":"Normal","urgency":"Normal","rank":1,"importanceProvenance":"Auto","labels":[],"concurrencyToken":"{{Token}}","revisionCount":2,"requiredPlatform":"{{platform}}"}""";
    }
}