using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1893 S0. Plan section 7.0 under the Q-8 fallback: a commit that contains
// 9cf3e491705247bc03814e6afed77a6534805dfd is RELEASED only when the base runner
// compose sets SessionRunner__HostStats__HistoryEnabled to "false".
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1893S0RunnerHoldTests
{
    private const string Collector = "9cf3e491705247bc03814e6afed77a6534805dfd";
    private const string ShaWithoutTheLine = "29ba0c5002e642c4f4f91805be7e17660a40906d";
    private const string BaseCompose = "docker-compose.server2-runner.yml";

    private static readonly Regex ReleaseLine = new(
        "^ +SessionRunner__HostStats__HistoryEnabled: \"false\"$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Test]
    public void Base_file_releases_the_s0_hold()
    {
        Git("merge-base", "--is-ancestor", Collector, "HEAD").Exit
            .ShouldBe(0, "HEAD contains the collector commit");
        var text = DockerStackDocuments.Read(BaseCompose).Replace("\r\n", "\n");
        Verdict(text, containsCollector: true).ShouldBe("RELEASED");
        ReleaseLine.IsMatch(text).ShouldBeTrue();
    }

    [Test]
    public void Sha_without_the_line_prints_hold()
    {
        var ancestor = Git("merge-base", "--is-ancestor", Collector, ShaWithoutTheLine);
        ancestor.Exit.ShouldBe(0, ancestor.Error);
        var shown = Git("show", ShaWithoutTheLine + ":" + BaseCompose);
        shown.Exit.ShouldBe(0, shown.Error);
        var text = shown.Output.Replace("\r\n", "\n");
        ReleaseLine.IsMatch(text).ShouldBeFalse();
        Verdict(text, containsCollector: true).ShouldBe("HOLD");
    }

    internal static string Verdict(string composeText, bool containsCollector)
    {
        if (!containsCollector)
            return "NOT-AFFECTED";
        return ReleaseLine.IsMatch(composeText.Replace("\r\n", "\n")) ? "RELEASED" : "HOLD";
    }

    private readonly record struct GitResult(int Exit, string Output, string Error);

    private static GitResult Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = DockerStackDocuments.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("git timed out");
        }

        return new GitResult(process.ExitCode, output, error);
    }
}
