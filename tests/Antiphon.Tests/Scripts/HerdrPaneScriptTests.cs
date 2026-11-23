using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0461: the herdr-pane command (Antiphon.Nightly.HerdrPane, which replaced scripts/herdr-pane.ps1 in unit 21 of the
/// 2026-11-22 plan) in process, on its production HTTP transport, against the real server and runner routes of
/// HerdrDisposalHttpFixture. The rows are the script's.
/// </summary>
[Category("Integration")]
public sealed class HerdrPaneScriptTests
{
    private const string SyntheticToken = "c461-synthetic-task-token";

    [Test]
    public async Task Inspect_dry_run_refusal_and_status_use_real_script_and_http_routes()
    {
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        h.Runner.Processes.Complete = false;
        var inspect = await RunAsync(h, "inspect", "--pane-id", h.Runner.PaneId,
            "--expected-session-id", h.Runner.SessionId.ToString("D"), "--json");
        inspect.ExitCode.ShouldBe(0, inspect.Output);
        using var preview = JsonDocument.Parse(inspect.Output);
        preview.RootElement.GetProperty("eligible").GetBoolean().ShouldBeFalse();
        var previewId = preview.RootElement.GetProperty("previewId").GetString()!;
        var operationId = Guid.NewGuid().ToString("D");
        var args = new[] { "dispose", "--preview-id", previewId, "--operation-id", operationId, "--reason", "selected leftover", "--json" };
        var before = h.RunnerDisposalRequests;
        var dry = await RunAsync(h, args);
        dry.ExitCode.ShouldBe(0, dry.Output);
        using var dryJson = JsonDocument.Parse(dry.Output);
        dryJson.RootElement.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        dryJson.RootElement.GetProperty("method").GetString().ShouldBe("POST");
        dryJson.RootElement.GetProperty("path").GetString().ShouldBe("/api/herdr/pane-disposals");
        h.RunnerDisposalRequests.ShouldBe(before);
        var execute = await RunAsync(h, args.Concat(["--execute"]).ToArray());
        execute.ExitCode.ShouldBe(1, execute.Output);
        using var error = JsonDocument.Parse(execute.Output);
        error.RootElement.GetProperty("code").GetString().ShouldBe(HerdrPaneDisposalCodes.IdentityUnproven);
        error.RootElement.GetProperty("operationId").GetString().ShouldBe(operationId);
        var status = await RunAsync(h, "status", "--operation-id", operationId, "--json");
        status.ExitCode.ShouldBe(0, status.Output);
        using var receipt = JsonDocument.Parse(status.Output);
        receipt.RootElement.GetProperty("outcome").GetString().ShouldBe("Refused");
        receipt.RootElement.GetProperty("operationId").GetString().ShouldBe(operationId);
        h.Runner.Methods.ShouldNotContain("pane.close");

        // Without --json, inspect prints its four lines.
        var text = await RunAsync(h, "inspect", "--pane-id", h.Runner.PaneId, "--expected-session-id", h.Runner.SessionId.ToString("D"));
        text.ExitCode.ShouldBe(0, text.Output);
        var lines = text.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Length.ShouldBe(4, text.Output);
        lines[0].ShouldStartWith("Pane " + h.Runner.PaneId + ", tab ");
        lines[0].ShouldContain("; eligible=False; guardAvailable=");
        lines[3].ShouldBe("External changes after the final check can race close. Execute only the reviewed preview.");
    }

    [Test]
    public async Task ReasonFile_preserves_multiline_text_and_script_is_ascii()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "HerdrPane.cs")).ShouldAllBe(b => b < 128);
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        Directory.CreateDirectory(h.Runner.Settings.SessionLogPath);
        var path = Path.Combine(h.Runner.Settings.SessionLogPath, "reason.txt");
        var reason = "Operator reason\n" + new string('x', 2800) + "\nquote: '\" literal: $HOME";
        await File.WriteAllTextAsync(path, reason);
        var result = await RunAsync(h, "dispose", "--preview-id", Guid.NewGuid().ToString("D"),
            "--operation-id", Guid.NewGuid().ToString("D"), "--reason-file", path, "--json");
        result.ExitCode.ShouldBe(0, result.Output);
        using var body = JsonDocument.Parse(result.Output);
        body.RootElement.GetProperty("body").GetProperty("reason").GetString().ShouldBe(reason);
        h.RunnerDisposalRequests.ShouldBe(0);
    }

    [Test]
    [Arguments("inspect", "--pane-id", "w1:p*")]
    [Arguments("status", "--operation-id", "1234")]
    [Arguments("dispose", "--preview-id", "1234")]
    public async Task Invalid_arguments_never_reach_runner(string verb, string option, string value)
    {
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        var result = await RunAsync(h, verb, option, value, "--json");
        result.ExitCode.ShouldNotBe(0);
        h.RunnerDisposalRequests.ShouldBe(0);
    }

    // The command as the CLI runs it (a usage error is its exit 2), stdout and stderr together; the token is never printed.
    private static async Task<(int ExitCode, string Output)> RunAsync(HerdrDisposalHttpFixture h, params string[] args)
    {
        var output = new StringWriter();
        int exit;
        try
        {
            var options = HerdrPaneOptions.Parse(args, h.Http.BaseAddress!.ToString(), SyntheticToken);
            exit = await Task.Run(() => HerdrPane.Run(options, NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(),
                NightlyReport.HttpTransport(timeout: HerdrPane.Deadline))).WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (NightlyUsageException e)
        {
            output.WriteLine(e.Message);
            exit = 2;
        }
        var text = output.ToString();
        text.ShouldNotContain(SyntheticToken);
        return (exit, text);
    }

    [Test] public Task C461_G011_Execute_opt_in() => Inspect_dry_run_refusal_and_status_use_real_script_and_http_routes();
    [Test] public Task C461_G108_Script_redaction() => Inspect_dry_run_refusal_and_status_use_real_script_and_http_routes();
    [Test] public Task C461_G110_Server_route_only() => Inspect_dry_run_refusal_and_status_use_real_script_and_http_routes();
    [Test] public Task Inspect_dispose_and_status_use_real_script() => Inspect_dry_run_refusal_and_status_use_real_script_and_http_routes();
    [Test] public Task ReasonFile_json_and_ascii_contract() => ReasonFile_preserves_multiline_text_and_script_is_ascii();
}
