using System.Diagnostics;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0124 S1 — pin the nightly scripts: ASCII-only, no git mutations or
/// alternate OutputPath in the runner, isolated clone named by the bootstrap,
/// shared-tree guard refuses <c>C:\src\Antiphon</c>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyScriptsTests
{
    [Test]
    public void The_three_scripts_are_ascii_only()
    {
        foreach (var name in new[] { "nightly-tests.ps1", "nightly-run.ps1", "nightly-report.ps1" })
        {
            var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", name);
            File.Exists(path).ShouldBeTrue(path);
            var bytes = File.ReadAllBytes(path);
            var firstNonAscii = Array.FindIndex(bytes, static b => b > 127);
            firstNonAscii.ShouldBe(-1, $"{name} has a non-ASCII byte at offset {firstNonAscii}");
        }
    }

    [Test]
    public void Nightly_tests_script_has_no_git_mutations_or_alternate_output_path()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-tests.ps1");
        var text = File.ReadAllText(path, Encoding.UTF8);
        text.ShouldNotContain("git pull");
        text.ShouldNotContain("git checkout");
        text.ShouldNotContain("git stash");
        text.ShouldNotContain("OutputPath=");
    }

    [Test]
    public void Nightly_run_script_names_the_isolated_clone_and_origin_master()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1");
        var text = File.ReadAllText(path, Encoding.UTF8);
        text.ShouldContain(@"C:\Antiphon\nightly\checkout");
        text.ShouldContain("origin/master");
    }

    [Test]
    public async Task Shared_tree_WhatIf_exits_3_naming_the_guard()
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-tests.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-RepoRoot");
        startInfo.ArgumentList.Add(@"C:\src\Antiphon");
        startInfo.ArgumentList.Add("-Suites");
        startInfo.ArgumentList.Add("client");
        startInfo.ArgumentList.Add("-WhatIf");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + await stderr;

        process.ExitCode.ShouldBe(3, output);
        output.ShouldContain("REFUSED");
        output.ShouldContain(@"C:\src\Antiphon");
        output.ShouldContain("AllowSharedTree");
        output.ShouldNotContain("WhatIf: would run");
    }
}
