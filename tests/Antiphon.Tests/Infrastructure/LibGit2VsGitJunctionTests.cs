using Antiphon.GitInProcess;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// The path answers of <see cref="InProcessGitReads"/> through a junction: git prints the resolved
/// real path, so libgit2's answer must too. Starts <c>cmd /c mklink /J</c>, so it is on the process
/// lane rather than the git lane (the rest of the comparison is LibGit2VsGitDifferentialTests).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LibGit2VsGitJunctionTests
{
    [Test]
    public async Task Paths_through_a_junction_print_the_resolved_real_path()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var link = Path.Combine(Path.GetTempPath(), "antiphon-lg2j-" + Guid.NewGuid().ToString("N"));
        await RunAsync("cmd.exe", Path.GetTempPath(), "/d", "/c", "mklink", "/J", link, fixture.Root);
        try
        {
            foreach (var leaf in new[] { "canonical", Path.Combine("trees", "source") })
            {
                var through = Path.Combine(link, leaf);
                string[][] shapes =
                [
                    ["rev-parse", "--absolute-git-dir"],
                    ["rev-parse", "--path-format=absolute", "--git-common-dir"],
                    ["rev-parse", "--path-format=absolute", "--git-path", "index.lock"],
                ];
                foreach (var args in shapes)
                {
                    var answer = InProcessGitReads.TryAnswer(through, args);
                    answer.ShouldNotBeNull(string.Join(' ', args) + " @ " + through);
                    var git = await RunAsync("git", through, args);
                    answer.Output.ShouldBe(git.Output, string.Join(' ', args) + " @ " + through);
                    answer.Output.ShouldNotContain(Path.GetFileName(link), Case.Insensitive, "the junction is resolved");
                }
            }
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link); // removes the junction only
        }
    }

    private static async Task<LandingGitResult> RunAsync(string exe, string cwd, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        p.ExitCode.ShouldBe(0, exe + " " + string.Join(' ', args));
        return new LandingGitResult(p.ExitCode, output, "");
    }
}
