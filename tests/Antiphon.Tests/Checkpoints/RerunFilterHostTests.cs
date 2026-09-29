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

        var staged = Path.Combine(AppContext.BaseDirectory, "checkpoint-filter-host",
            "Antiphon.Checkpoints.FilterHost.dll");
        File.Exists(staged).ShouldBeTrue("filter host must be staged by the parent build");
        {
            var good = await RunAsync(staged, filters[0]);
            good.ExitCode.ShouldBe(0, good.Text);
            Count(good.Text, "total:").ShouldBe(2);
            Count(good.Text, "succeeded:").ShouldBe(2);

            var bad = await RunAsync(staged, "/*/*/FilterHostTests/alpha|/*/*/FilterHostTests/beta");
            Count(bad.Text, "total:").ShouldBe(0);
        }
    }

    private static async Task<(int ExitCode, string Text)> RunAsync(string staged, string filter)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(staged);
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
