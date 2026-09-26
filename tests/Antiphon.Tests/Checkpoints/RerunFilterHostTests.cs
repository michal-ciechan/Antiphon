using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RerunFilterHostTests
{
    [Test]
    [Timeout(180_000)]
    public async Task parenthesized_class_filter_runs_both_methods_on_a_real_host()
    {
        var filters = RerunPolicy.MethodFilters([
            "Antiphon.Tests.FilterHost.FilterHostTests.alpha",
            "Antiphon.Tests.FilterHost.FilterHostTests.beta",
        ]);
        filters.ShouldBe(["/*/*/FilterHostTests/(alpha*)|(beta*)"]);

        var projectDir = Path.Combine(CheckpointFixtures.RepoRoot, "tests", "Antiphon.Checkpoints.FilterHost");
        var project = Path.Combine(projectDir, "Antiphon.Checkpoints.FilterHost.csproj");
        var output = Path.Combine(projectDir, "bin-c723filter");
        try
        {
            var good = await RunAsync(project, filters[0]);
            good.ExitCode.ShouldBe(0, good.Text);
            Count(good.Text, "total:").ShouldBe(2);
            Count(good.Text, "succeeded:").ShouldBe(2);

            var bad = await RunAsync(project, "/*/*/FilterHostTests/alpha|/*/*/FilterHostTests/beta");
            Count(bad.Text, "total:").ShouldBe(0);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Text)> RunAsync(string project, string filter)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(project);
        psi.ArgumentList.Add("--property:OutputPath=bin-c723filter/");
        psi.ArgumentList.Add("--disable-build-servers");
        psi.ArgumentList.Add("-p:UseAppHost=false");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--treenode-filter");
        psi.ArgumentList.Add(filter);
        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout + "\n" + stderr);
    }

    private static int Count(string text, string label)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(label, StringComparison.Ordinal))
                continue;
            var rest = trimmed[label.Length..].Trim();
            if (int.TryParse(rest, out var value))
                return value;
        }

        return -1;
    }
}
