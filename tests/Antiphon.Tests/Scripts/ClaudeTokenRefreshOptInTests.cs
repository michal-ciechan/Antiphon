using System.Diagnostics;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0737. deploy-parent must not touch bw unless a refresh was asked for, and it must not
// claim the runner logged out while an existing token file is left in place.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ClaudeTokenRefreshOptInTests
{
    private const string KeepLine =
        "Claude token: keeping the existing server2 file (no vault refresh requested)";

    private const string RefreshSkipped = "refresh skipped, previous file unchanged";

    [Test]
    public async Task Default_path_does_not_call_bw_or_warn_logged_out_when_the_file_is_present()
    {
        var run = await ProbeAsync("default");
        run.ExitCode.ShouldBe(0, run.Output);
        run.BwCalled.ShouldBeFalse(run.Output);
        run.Output.ShouldContain(KeepLine);
        run.Output.ShouldNotContain("claudeAuth=logged-out");
        run.Output.ShouldNotContain("ClaudeOAuthTokenUnavailable");
    }

    [Test]
    public async Task Locked_vault_on_an_explicit_refresh_skips_and_keeps_the_previous_file()
    {
        var run = await ProbeAsync("locked");
        run.ExitCode.ShouldBe(0, run.Output);
        run.BwCalled.ShouldBeFalse(run.Output);
        run.Output.ShouldContain(RefreshSkipped);
        run.Output.ShouldNotContain("claudeAuth=logged-out");
    }

    [Test]
    public async Task Manifest_refresh_calls_bw_and_skips_when_the_item_is_unreadable()
    {
        var run = await ProbeAsync("manifest");
        run.ExitCode.ShouldBe(0, run.Output);
        run.BwCalled.ShouldBeTrue(run.Output);
        run.Output.ShouldContain(RefreshSkipped);
        run.Output.ShouldNotContain("claudeAuth=logged-out");
        run.Output.ShouldNotContain(KeepLine);
    }

    [Test]
    public void Deploy_parent_calls_the_gate_and_the_switch_is_threaded()
    {
        var bridge = DockerStackDocuments.Read("scripts/c590-real.ps1");
        var verify = DockerStackDocuments.Read("scripts/verify-docker-stack.ps1");
        var remote = DockerStackDocuments.Read("scripts/c590-remote.sh");
        var credentials = DockerStackDocuments.Read("docs/agent-credentials.md");
        var stack = DockerStackDocuments.Read("docs/docker-stack.md");

        var start = bridge.IndexOf("if ($Case -eq 'deploy-parent')", StringComparison.Ordinal);
        var end = bridge.IndexOf("if ($Case -eq 'git-credential-smoke')", StringComparison.Ordinal);
        (start >= 0 && end > start).ShouldBeTrue("deploy-parent block was not found");
        var block = bridge[start..end];
        block.ShouldContain("Invoke-C628ClaudeTokenOnDeploy");
        block.ShouldNotContain("Send-C628ClaudeOAuthToken");
        block.ShouldNotContain("bw ");

        bridge.ShouldContain(KeepLine);
        bridge.ShouldContain(RefreshSkipped);
        bridge.Contains("claudeAuth=logged-out", StringComparison.Ordinal)
            .ShouldBeFalse("the desktop bridge must not claim the runner logged out");
        bridge.ShouldContain("[switch]$RefreshClaudeToken");
        verify.ShouldContain("[switch]$RefreshClaudeToken");
        verify.ShouldContain("$script:C628RefreshClaudeToken");

        var absent = remote.Split('\n').Single(line => line.Contains("WARN ClaudeOAuthTokenAbsent", StringComparison.Ordinal));
        absent.ShouldContain("-RefreshClaudeToken");
        absent.ShouldContain("claudeAuth=logged-out");

        credentials.ShouldContain("-RefreshClaudeToken");
        credentials.ShouldContain("does not need `BW_SESSION`");
        credentials.Contains("at deploy, `scripts/c590-real.ps1` reads it through the relay session", StringComparison.Ordinal)
            .ShouldBeFalse("a deploy is no longer described as always reading the vault");
        stack.ShouldContain("-RefreshClaudeToken");
        stack.Contains("delivers from the vault", StringComparison.Ordinal)
            .ShouldBeFalse("the stack runbook no longer says every deploy delivers the token from the vault");
    }

    private static async Task<Probe> ProbeAsync(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "c737-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home");
        var bwDir = Path.Combine(root, "bw");
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(bwDir);
        Directory.CreateDirectory(empty);
        var marker = Path.Combine(root, "bw-called.txt");
        var bw = Path.Combine(bwDir, "bw");
        await File.WriteAllTextAsync(bw, "#!/bin/sh\nprintf 'called\\n' >> \"$C737_MARKER\"\nexit 2\n");
        if (OperatingSystem.IsWindows())
            throw new InvalidOperationException("the bw stand-in is a shell script");
        File.SetUnixFileMode(bw, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var probe = Path.Combine(root, "probe.ps1");
        await File.WriteAllTextAsync(probe, """
            $ErrorActionPreference = 'Continue'
            Remove-Item Env:ANTIPHON_REFRESH_CLAUDE_TOKEN -ErrorAction SilentlyContinue
            Remove-Item Env:BW_SESSION -ErrorAction SilentlyContinue
            $env:HOME = $env:C737_HOME
            $env:USERPROFILE = $env:C737_HOME
            if ($env:C737_MODE -eq 'default' -or $env:C737_MODE -eq 'manifest') {
                $env:BW_SESSION = 'c737-not-a-vault-session'
                $env:PATH = $env:C737_BWDIR + [IO.Path]::PathSeparator + $env:PATH
            }
            if ($env:C737_MODE -eq 'locked') {
                $env:PATH = $env:C737_EMPTY
            }
            . $env:C737_SCRIPT
            $manifest = [pscustomobject]@{}
            if ($env:C737_MODE -eq 'manifest') {
                $manifest = [pscustomobject]@{ refreshClaudeToken = $true }
            }
            if (Get-Command Invoke-C628ClaudeTokenOnDeploy -ErrorAction SilentlyContinue) {
                if ($env:C737_MODE -eq 'locked') {
                    Invoke-C628ClaudeTokenOnDeploy -Refresh -Manifest $manifest
                } else {
                    Invoke-C628ClaudeTokenOnDeploy -Manifest $manifest
                }
            } else {
                [void](Send-C628ClaudeOAuthToken)
            }
            exit 0
            """);

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["C737_MODE"] = mode;
        psi.Environment["C737_HOME"] = home;
        psi.Environment["C737_BWDIR"] = bwDir;
        psi.Environment["C737_EMPTY"] = empty;
        psi.Environment["C737_MARKER"] = marker;
        psi.Environment["C737_SCRIPT"] = Path.Combine(C590Harness.RepoRoot, "scripts", "c590-real.ps1");
        psi.Environment.Remove("BW_SESSION");
        psi.Environment.Remove("ANTIPHON_REFRESH_CLAUDE_TOKEN");
        foreach (var arg in new[] { "-NoProfile", "-File", probe })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new Probe(process.ExitCode, stdout + stderr, File.Exists(marker));
    }

    private sealed record Probe(int ExitCode, string Output, bool BwCalled);
}
