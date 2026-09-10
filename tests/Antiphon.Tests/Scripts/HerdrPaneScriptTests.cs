using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class HerdrPaneScriptTests
{
    [Test]
    public async Task Inspect_dry_run_refusal_and_status_use_real_script_and_http_routes()
    {
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        var inspect = await RunAsync(h, "inspect", "-PaneId", h.Runner.PaneId,
            "-ExpectedSessionId", h.Runner.SessionId.ToString("D"), "-Json");
        inspect.ExitCode.ShouldBe(0, inspect.Output);
        using var preview = JsonDocument.Parse(inspect.Output);
        preview.RootElement.GetProperty("eligible").GetBoolean().ShouldBeFalse();
        var previewId = preview.RootElement.GetProperty("previewId").GetString()!;
        var operationId = Guid.NewGuid().ToString("D");
        var args = new[] { "dispose", "-PreviewId", previewId, "-OperationId", operationId, "-Reason", "selected leftover", "-Json" };
        var before = h.RunnerDisposalRequests;
        var dry = await RunAsync(h, args);
        dry.ExitCode.ShouldBe(0, dry.Output);
        using var dryJson = JsonDocument.Parse(dry.Output);
        dryJson.RootElement.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        h.RunnerDisposalRequests.ShouldBe(before);
        var execute = await RunAsync(h, args.Concat(["-Execute"]).ToArray());
        execute.ExitCode.ShouldBe(1, execute.Output);
        using var error = JsonDocument.Parse(execute.Output);
        error.RootElement.GetProperty("code").GetString().ShouldBe(HerdrPaneDisposalCodes.GuardUnavailable);
        error.RootElement.GetProperty("operationId").GetString().ShouldBe(operationId);
        var status = await RunAsync(h, "status", "-OperationId", operationId, "-Json");
        status.ExitCode.ShouldBe(0, status.Output);
        using var receipt = JsonDocument.Parse(status.Output);
        receipt.RootElement.GetProperty("outcome").GetString().ShouldBe("Refused");
        receipt.RootElement.GetProperty("operationId").GetString().ShouldBe(operationId);
        h.Runner.Methods.ShouldNotContain("pane.close");
    }

    [Test]
    public async Task ReasonFile_preserves_multiline_text_and_script_is_ascii()
    {
        var script = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "herdr-pane.ps1");
        File.ReadAllBytes(script).ShouldAllBe(b => b < 128);
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        Directory.CreateDirectory(h.Runner.Settings.SessionLogPath);
        var path = Path.Combine(h.Runner.Settings.SessionLogPath, "reason.txt");
        var reason = "Operator reason\n" + new string('x', 2800) + "\nquote: '\" literal: $HOME";
        await File.WriteAllTextAsync(path, reason);
        var result = await RunAsync(h, "dispose", "-PreviewId", Guid.NewGuid().ToString("D"),
            "-OperationId", Guid.NewGuid().ToString("D"), "-ReasonFile", path, "-Json");
        result.ExitCode.ShouldBe(0, result.Output);
        using var body = JsonDocument.Parse(result.Output);
        body.RootElement.GetProperty("body").GetProperty("reason").GetString().ShouldBe(reason);
        h.RunnerDisposalRequests.ShouldBe(0);
    }

    [Test]
    [Arguments("inspect", "-PaneId", "w1:p*")]
    [Arguments("status", "-OperationId", "1234")]
    [Arguments("dispose", "-PreviewId", "1234")]
    public async Task Invalid_arguments_never_reach_runner(string verb, string option, string value)
    {
        await using var h = new HerdrDisposalHttpFixture();
        await h.StartAsync();
        var result = await RunAsync(h, verb, option, value, "-Json");
        result.ExitCode.ShouldNotBe(0);
        h.RunnerDisposalRequests.ShouldBe(0);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(HerdrDisposalHttpFixture h, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "herdr-pane.ps1") }.Concat(args))
            start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = h.Http.BaseAddress!.ToString();
        const string syntheticToken = "c461-synthetic-task-token";
        start.Environment["ANTIPHON_TASK_TOKEN"] = syntheticToken;
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            var output = await stdout + await stderr;
            output.ShouldNotContain(syntheticToken);
            return (process.ExitCode, output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
