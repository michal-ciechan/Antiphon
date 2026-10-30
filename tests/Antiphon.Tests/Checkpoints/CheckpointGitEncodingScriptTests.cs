using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1608. The production tamper check must name a non-ASCII path after a legacy code page
/// decodes git output, and invalid UTF-8 from git must be refused rather than replaced.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointGitEncodingScriptTests : CheckpointTestBase
{
    [Test]
    public Task C1608_legacy_code_page_still_reports_non_ascii_tamper() => Run("tamper");

    [Test]
    public Task C1608_invalid_utf8_git_output_is_refused() => Run("encoding");

    [Test]
    public Task C1608_invalid_utf8_comes_from_the_configured_executable() => Run("encoding", verifyConfiguredGit: true);

    private async Task Run(string mode, bool verifyConfiguredGit = false)
    {
        var root = TempDir();
        var invocationLog = Path.Combine(root, "git-invocations.txt");
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot, "scripts/test-checkpoint-git-encoding.ps1"),
            "-Root", root, "-Repository", CheckpointFixtures.RepoRoot, "-Mode", mode,
        })
            psi.ArgumentList.Add(arg);
        if (verifyConfiguredGit)
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "checkpoint-git-fixture",
                "Antiphon.Checkpoints.GitFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.Exists(executable).ShouldBeTrue("native git fixture must be staged by the build");
            psi.ArgumentList.Add("-GitExecutable");
            psi.ArgumentList.Add(executable);
            psi.Environment["C1608_GIT_LOG"] = invocationLog;
        }
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new ShouldAssertException("checkpoint git encoding harness exceeded 120 seconds");
        }
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        (await stdout).ShouldContain("C1608 " + mode + " PASS");
        if (verifyConfiguredGit)
        {
            File.Exists(invocationLog).ShouldBeTrue("the configured executable must serve the actual git queries");
            File.ReadAllLines(invocationLog).ShouldBe(["config", "status", "config", "ls-files"]);
        }
    }
}
