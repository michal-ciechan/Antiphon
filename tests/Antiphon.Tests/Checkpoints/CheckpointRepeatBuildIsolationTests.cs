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

    private static async Task AssertRunAsync(string root, int repeat, int expectedExecuted,
        CancellationToken cancellationToken)
    {
        var results = Path.Combine(root, $"results-{repeat}");
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
            "-OutputPath", $"bin-c885-sequence-{repeat}/", "-Filter", "/*/*/FilterHostTests*/*",
            "-MinExecuted", "2", "-ResultsRoot", results, "-Repeat", repeat.ToString(), "-NoSlot",
        })
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
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
}
