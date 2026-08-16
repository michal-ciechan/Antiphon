using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

[Category("Unit")]
public sealed class AgentTuiSmokeScriptTests
{
    [Test]
    public async Task Smoke_script_is_valid_PowerShell()
    {
        var scriptPath = Path.Combine(FindRepoRoot(), "scripts", "verify-agent-tui-profile.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("& { param([string] $scriptPath) [void] [scriptblock]::Create([IO.File]::ReadAllText($scriptPath)) }");
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo);
        process.ShouldNotBeNull();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.ShouldBe(0, $"PowerShell parse failed:{Environment.NewLine}{await standardOutput}{await standardError}");
    }

    [Test]
    public async Task Smoke_script_waits_for_the_runner_process_before_reading_launch_arguments()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "scripts", "verify-agent-tui-profile.ps1"));

        script.ShouldContain("function Wait-RunnerProcess");
        script.ShouldContain("$runnerProcess = Wait-RunnerProcess $sessionId");
    }

    [Test]
    public async Task Smoke_script_waits_for_the_OpenCode_composer_before_queueing_the_prompt()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "scripts", "verify-agent-tui-profile.ps1"));

        script.ShouldContain("function Wait-ForOpenCodeComposer");
        script.ShouldContain("$readySnapshot = Wait-ForOpenCodeComposer $sessionId");
        script.ShouldContain("Ask anything...");
    }

    [Test]
    public async Task OpenCode_smoke_uses_the_queued_message_contract_and_checks_a_rendered_reply()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "scripts", "verify-agent-tui-profile.ps1"));

        script.ShouldContain("@{ body = $prompt; mode = \"WhenIdle\" }");
        script.ShouldNotContain("/api/sessions/$sessionId/input");
        script.ShouldContain("Get-RunnerSnapshot");
        script.ShouldContain("renderedScreen");
        script.ShouldContain("$lines -contains $ExpectedReply");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }
}
