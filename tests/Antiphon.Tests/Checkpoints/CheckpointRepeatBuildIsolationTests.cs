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
    // One real FilterHost build, not three. A loaded Windows repeat-2 invocation
    // took 251 s and the 300 s limit cancelled the next build. The 2/1/3 sequence
    // keeps the receipt and fresh-output assertions against a stub dotnet.
    [Test]
    [Timeout(600_000)]
    public async Task repeat_two_then_default_then_three_rebuilds_the_same_project(CancellationToken cancellationToken)
    {
        var root = TempDir();
        var shim = CheckpointRepeatFixture.WriteShim(root);

        await AssertRunAsync(root, 2, 4, cancellationToken, shim);
        await AssertRunAsync(root, 1, 2, cancellationToken, shim);
        await AssertRunAsync(root, 3, 6, cancellationToken, shim);
        await AssertRealRepeatAsync(root, cancellationToken);
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
        CancellationToken cancellationToken, string shim)
    {
        var template = CheckpointRepeatFixture.WriteTemplate(root, repeat, methods: ["alpha", "beta"]);
        var calls = Path.Combine(root, $"calls-{repeat}.log");
        var results = Path.Combine(root, $"results-{repeat}");
        var outputName = CheckpointRepeatFixture.OutputName(root) + $"-sequence-{repeat}";
        var outputDirectory = FilterHostOutput(outputName);
        Directory.Exists(outputDirectory).ShouldBeFalse("the invocation must own a fresh output path");
        var psi = CheckpointProcess();
        foreach (var argument in CheckpointArguments($"CP-repeat-{repeat}", outputName, results, repeat))
            psi.ArgumentList.Add(argument);
        psi.ArgumentList.Add("-DotnetShim");
        psi.ArgumentList.Add(shim);
        psi.Environment["C885_SHIM_LOG"] = calls;
        psi.Environment["C885_TRX_TEMPLATE"] = template;

        var (exitCode, output) = await RunCheckpointAsync(psi, outputDirectory, cancellationToken, expectAssembly: false);
        exitCode.ShouldBe(0, $"sequence-repeat-{repeat}: {output}");
        AssertReceipt(output, results, $"CP-repeat-{repeat}", repeat, expectedExecuted, $"sequence-repeat-{repeat}");
        output.ShouldNotContain("Build succeeded", Case.Sensitive, $"sequence-repeat-{repeat}-stub-build");
        var lines = File.ReadAllLines(calls);
        lines.Count(line => line.StartsWith("build ", StringComparison.Ordinal)).ShouldBe(1, $"sequence-repeat-{repeat}-one-build");
        lines.Count(line => line.StartsWith("run ", StringComparison.Ordinal)).ShouldBe(1, $"sequence-repeat-{repeat}-one-run");
    }

    private async Task AssertRealRepeatAsync(string root, CancellationToken cancellationToken)
    {
        const int repeat = 2;
        var results = Path.Combine(root, "results-real");
        var outputName = CheckpointRepeatFixture.OutputName(root) + "-real";
        var outputDirectory = FilterHostOutput(outputName);
        Directory.Exists(outputDirectory).ShouldBeFalse("the real build must own a fresh output path");
        var psi = CheckpointProcess();
        foreach (var argument in CheckpointArguments("CP-repeat-real", outputName, results, repeat))
            psi.ArgumentList.Add(argument);

        var (exitCode, output) = await RunCheckpointAsync(psi, outputDirectory, cancellationToken, expectAssembly: true);
        exitCode.ShouldBe(0, $"real-repeat-2: {output}");
        AssertReceipt(output, results, "CP-repeat-real", repeat, 4, "real-repeat-2");
        output.ShouldContain("Build succeeded", Case.Sensitive, "real-repeat-2-build");
    }

    private static ProcessStartInfo CheckpointProcess() => new("pwsh")
    {
        WorkingDirectory = CheckpointFixtures.RepoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    private static IEnumerable<string> CheckpointArguments(string name, string outputName, string results, int repeat)
    {
        return
        [
            "-NoProfile", "-File", Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1"),
            "-Name", name, "-Project", "tests/Antiphon.Checkpoints.FilterHost",
            "-OutputPath", outputName + "/", "-Filter", "/*/*/FilterHostTests*/*",
            "-MinExecuted", "2", "-ResultsRoot", results, "-Repeat", repeat.ToString(), "-NoSlot",
        ];
    }

    private static string FilterHostOutput(string outputName) =>
        Path.Combine(CheckpointFixtures.RepoRoot, "tests", "Antiphon.Checkpoints.FilterHost", outputName);

    private static string FilterHostAssembly(string outputDirectory) =>
        Path.Combine(outputDirectory, "Antiphon.Checkpoints.FilterHost.dll");

    private static void AssertReceipt(string output, string results, string name, int repeat, int expectedExecuted, string label)
    {
        var directory = Directory.GetDirectories(results, $"{name}-*").Single();
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "source.json")));
        receipt.RootElement.GetProperty("executed").GetInt32()
            .ShouldBe(expectedExecuted, $"{label}-executed: {output}");
        receipt.RootElement.GetProperty("passed").GetInt32()
            .ShouldBe(expectedExecuted, $"{label}-passed: {output}");

        var hasRepeat = receipt.RootElement.TryGetProperty("repeat", out var proof);
        hasRepeat.ShouldBe(repeat > 1, $"{label}-receipt-token");
        if (repeat > 1)
        {
            proof.GetProperty("requested").GetInt32().ShouldBe(repeat);
            output.ShouldContain($"repeat={repeat} repetitions={repeat}/{repeat} hostInvocations=1",
                Case.Sensitive, $"{label}-trailer-token");
        }
        else
            output.ShouldNotContain(" repetitions=", Case.Sensitive, "sequence-default-trailer-token");
    }

    private async Task<(int ExitCode, string Output)> RunCheckpointAsync(ProcessStartInfo psi, string outputDirectory,
        CancellationToken cancellationToken, bool? expectAssembly = null)
    {
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var output = await stdout + "\n" + await stderr;
            if (expectAssembly is bool expected)
                File.Exists(FilterHostAssembly(outputDirectory)).ShouldBe(expected, $"assembly witness: {output}");
            return (process.ExitCode, output);
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
