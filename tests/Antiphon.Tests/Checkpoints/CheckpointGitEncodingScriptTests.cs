using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1608. The checkpoint tool's confinement (the row verb's, which scripts/run-checkpoint.ps1 launches since unit 20)
/// must name a non-ASCII path whatever the console code page, and invalid UTF-8 from git must be refused rather than
/// replaced. Each case runs the tool's own readers in the staged script harness host (checkpoint-git-encoding), a process
/// of its own because each sets what is process-wide: git answers every read (ANTIPHON_GIT_INPROCESS=off), and for the
/// encoding cases a stub git first on PATH writes a byte that is not UTF-8.
/// </summary>
[Category("Unit")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
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
        var host = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { host, "checkpoint-git-encoding", typeof(Antiphon.Checkpoints.Program).Assembly.Location, mode, Path.Combine(root, "fixture") })
            psi.ArgumentList.Add(arg);
        psi.Environment["ANTIPHON_GIT_INPROCESS"] = "off";
        if (mode == "encoding")
        {
            // The staged native fixture under the name git, first on the probe's PATH.
            var staged = Path.Combine(AppContext.BaseDirectory, "checkpoint-git-fixture");
            var executable = Path.Combine(staged, "Antiphon.Checkpoints.GitFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.Exists(executable).ShouldBeTrue("native git fixture must be staged by the build");
            var bin = Path.Combine(root, "fake-git");
            Directory.CreateDirectory(bin);
            foreach (var file in Directory.GetFiles(staged))
                File.Copy(file, Path.Combine(bin, Path.GetFileName(file)));
            var git = Path.Combine(bin, "git" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.Move(Path.Combine(bin, Path.GetFileName(executable)), git);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            psi.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
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
            throw new ShouldAssertException("checkpoint git encoding probe exceeded 120 seconds");
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
