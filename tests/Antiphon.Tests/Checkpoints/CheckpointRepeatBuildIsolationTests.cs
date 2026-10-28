using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointRepeatBuildIsolationTests : CheckpointTestBase
{
    [Test]
    [Timeout(300_000)]
    public async Task repeat_two_then_default_then_three_rebuilds_the_same_project(CancellationToken cancellationToken)
    {
        var root = TempDir();

        await AssertRunAsync(root, 2, 4, cancellationToken);
        await AssertRunAsync(root, 1, 2, cancellationToken);
        await AssertRunAsync(root, 3, 6, cancellationToken);
    }

    // Windows MAX_PATH is 260 including the terminating NUL, so a path string must stay
    // within 259 characters. The deepest file in a UseArtifactsOutput FilterHost build is
    // the generated testing-platform cache under the isolated output directory.
    [Test]
    [Timeout(30_000)]
    public void repeat_output_directory_stays_within_windows_max_path(CancellationToken cancellationToken)
    {
        const string longestRelative =
            @"artifacts\obj\Antiphon.Checkpoints.FilterHost\debug\Antiphon.Checkpoints.FilterHost.gentestingplatformentrypointinputcache.cache";
        var tempName = "c723-" + new string('a', 32);
        var output = CheckpointRepeatFixture.OutputName(tempName) + "-sequence-2";
        foreach (var prefix in new[]
        {
            @"C:\Antiphon\releases\checkout-1873094e\tests\Antiphon.Checkpoints.FilterHost",
            @"C:\Antiphon\worktrees\card-task-0a352e05\tests\Antiphon.Checkpoints.FilterHost",
        })
        {
            var full = prefix + "\\" + output + "\\" + longestRelative;
            full.Length.ShouldBeLessThanOrEqualTo(259, full);
        }
    }

    private async Task AssertRunAsync(string root, int repeat, int expectedExecuted,
        CancellationToken cancellationToken)
    {
        var results = Path.Combine(root, $"results-{repeat}");
        var outputName = CheckpointRepeatFixture.OutputName(root) + $"-sequence-{repeat}";
        var outputDirectory = Path.Combine(CheckpointFixtures.RepoRoot, "tests", "Antiphon.Checkpoints.FilterHost", outputName);
        Directory.Exists(outputDirectory).ShouldBeFalse("the invocation must own a fresh output path");
        var psi = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1"),
            "-Name", $"CP-repeat-{repeat}", "-Project", "tests/Antiphon.Checkpoints.FilterHost",
            "-OutputPath", outputName + "/", "-Filter", "/*/*/FilterHostTests*/*",
            "-MinExecuted", "2", "-ResultsRoot", results, "-Repeat", repeat.ToString(), "-NoSlot",
        })
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout + "\n" + await stderr;
        process.ExitCode.ShouldBe(0, $"sequence-repeat-{repeat}: {output}");

        var directory = Directory.GetDirectories(results, $"CP-repeat-{repeat}-*").Single();
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "source.json")));
        receipt.RootElement.GetProperty("executed").GetInt32()
            .ShouldBe(expectedExecuted, $"sequence-repeat-{repeat}-executed: {output}");
        receipt.RootElement.GetProperty("passed").GetInt32()
            .ShouldBe(expectedExecuted, $"sequence-repeat-{repeat}-passed: {output}");

        var hasRepeat = receipt.RootElement.TryGetProperty("repeat", out var proof);
        hasRepeat.ShouldBe(repeat > 1, $"sequence-repeat-{repeat}-receipt-token");
        if (repeat > 1)
        {
            proof.GetProperty("requested").GetInt32().ShouldBe(repeat);
            output.ShouldContain($"repeat={repeat} repetitions={repeat}/{repeat} hostInvocations=1",
                Case.Sensitive, $"sequence-repeat-{repeat}-trailer-token");
        }
        else
            output.ShouldNotContain(" repetitions=", Case.Sensitive, "sequence-default-trailer-token");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
