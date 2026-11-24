using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// The row verb (scripts/run-checkpoint.ps1's body since unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)
// builds each repeat count into its own fresh output. The 2/1/3 sequence runs against a stand-in dotnet over a temporary
// repository; the one real leg builds and runs tests/Antiphon.Checkpoints.FilterHost in the shared checkout.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[SharedCheckoutChild]
public sealed class CheckpointRepeatBuildIsolationTests : CheckpointTestBase
{
    // One real FilterHost build, not three. A loaded Windows repeat-2 invocation
    // took 251 s and the 300 s limit cancelled the next build. The 2/1/3 sequence
    // keeps the receipt and fresh-output assertions against a stub dotnet.
    // R5 (CARD-1638): 2x the one measured sample, 355.6s on Windows rc run d47848cc.
    // Provisional: n=1.
    [Test]
    [Timeout(712_000)]
    public async Task repeat_two_then_default_then_three_rebuilds_the_same_project(CancellationToken cancellationToken)
    {
        var root = TempDir();
        await AssertRunAsync(root, 2, 4, cancellationToken);
        await AssertRunAsync(root, 1, 2, cancellationToken);
        await AssertRunAsync(root, 3, 6, cancellationToken);
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

    private async Task AssertRunAsync(string root, int repeat, int expectedExecuted, CancellationToken cancellationToken)
    {
        var template = CheckpointRepeatFixture.WriteTemplate(root, repeat, methods: ["alpha", "beta"]);
        var repo = Path.Combine(root, "repo");
        if (!Directory.Exists(repo)) CheckpointRepeatFixture.WriteRepository(root);
        var results = Path.Combine(root, $"results-{repeat}");
        var outputName = CheckpointRepeatFixture.OutputName(root) + $"-sequence-{repeat}";
        Directory.Exists(Path.Combine(repo, CheckpointRepeatFixture.Project, outputName)).ShouldBeFalse("the invocation must own a fresh output path");
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            File.WriteAllText(CheckpointFixtures.TrxFile(request), File.ReadAllText(template)
                .Replace("__NONCE__", request.Environment?.GetValueOrDefault("ANTIPHON_CHECKPOINT_NONCE") ?? "", StringComparison.Ordinal));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var output = new StringWriter();
        var exitCode = await Antiphon.Checkpoints.Program.RunAsync(
            [.. Arguments($"CP-repeat-{repeat}", repo, CheckpointRepeatFixture.Project, outputName, results, repeat, "/*/*/SampleTests*/*")],
            new CheckpointApp.Runtime
            {
                Output = output, Driver = driver, Platform = new FakePlatform { IsWindows = true },
                SourceCapture = _ => CheckpointFixtures.CleanSource(), EnvironmentLookup = _ => null, CancellationToken = cancellationToken,
            });
        exitCode.ShouldBe(0, $"sequence-repeat-{repeat}: {output}");
        AssertReceipt(output.ToString(), results, $"CP-repeat-{repeat}", repeat, expectedExecuted, $"sequence-repeat-{repeat}");
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(1, $"sequence-repeat-{repeat}-one-build");
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1, $"sequence-repeat-{repeat}-one-run");
        Directory.Exists(Path.Combine(repo, CheckpointRepeatFixture.Project, outputName)).ShouldBeTrue($"sequence-repeat-{repeat}-own-output");
    }

    private async Task AssertRealRepeatAsync(string root, CancellationToken cancellationToken)
    {
        const int repeat = 2;
        var results = Path.Combine(root, "results-real");
        var outputName = CheckpointRepeatFixture.OutputName(root) + "-real";
        var outputDirectory = Path.Combine(CheckpointFixtures.RepoRoot, "tests", "Antiphon.Checkpoints.FilterHost", outputName);
        Directory.Exists(outputDirectory).ShouldBeFalse("the real build must own a fresh output path");
        var output = new StringWriter();
        try
        {
            var exitCode = await Antiphon.Checkpoints.Program.RunAsync(
                [.. Arguments("CP-repeat-real", CheckpointFixtures.RepoRoot, "tests/Antiphon.Checkpoints.FilterHost", outputName, results, repeat, "/*/*/FilterHostTests*/*")],
                new CheckpointApp.Runtime { Output = output, EnvironmentLookup = _ => null, CancellationToken = cancellationToken });
            File.Exists(Path.Combine(outputDirectory, "Antiphon.Checkpoints.FilterHost.dll")).ShouldBeTrue($"assembly witness: {output}");
            exitCode.ShouldBe(0, $"real-repeat-2: {output}");
            AssertReceipt(output.ToString(), results, "CP-repeat-real", repeat, 4, "real-repeat-2");
            output.ToString().ShouldContain("Build succeeded", Case.Sensitive, "real-repeat-2-build");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static IEnumerable<string> Arguments(string name, string repo, string project, string outputName, string results, int repeat, string filter) =>
    [
        "row", "--repo-root", repo, "--name", name, "--project", project, "--output-path", outputName + "/", "--filter", filter,
        "--min-executed", "2", "--results-root", results, "--repeat", repeat.ToString(), "--no-slot",
    ];

    private static void AssertReceipt(string output, string results, string name, int repeat, int expectedExecuted, string label)
    {
        var directory = Directory.GetDirectories(results, $"{name}-*").Single();
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "report.json")));
        var row = receipt.RootElement.GetProperty("rows")[0];
        row.GetProperty("executed").GetInt32().ShouldBe(expectedExecuted, $"{label}-executed: {output}");
        row.GetProperty("passed").GetInt32().ShouldBe(expectedExecuted, $"{label}-passed: {output}");
        var proof = row.GetProperty("repeat");
        (proof.ValueKind == JsonValueKind.Object).ShouldBe(repeat > 1, $"{label}-receipt-token");
        if (repeat > 1)
        {
            proof.GetProperty("requested").GetInt32().ShouldBe(repeat);
            output.ShouldContain($"repeat={repeat} repetitions={repeat}/{repeat} hostInvocations=1", Case.Sensitive, $"{label}-trailer-token");
        }
        else
            output.ShouldNotContain(" repetitions=", Case.Sensitive, "sequence-default-trailer-token");
    }
}
