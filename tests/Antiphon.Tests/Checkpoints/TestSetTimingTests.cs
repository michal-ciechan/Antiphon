using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// CARD-1313 S4. The owned child can pass while no durable per-set completion exists.
[Category("Unit")]
[NotInParallel]
public sealed class TestSetTimingTests : CheckpointTestBase
{
    [Test]
    public async Task C1313_ProducerReceipts()
    {
        var root = Path.Combine(TempDir(), "timing-root");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT");
        Environment.SetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT", root);
        try
        {
            var result = await RunOwnedChild();
            result.ExitCode.ShouldBe(0);
            result.State.ShouldBe("green");
            var events = Directory.GetFiles(root, "events.jsonl", SearchOption.AllDirectories);
            var receipt = string.Concat(events.Select(File.ReadAllText));
            receipt.Contains("\"eventKind\":\"set-completed\"", StringComparison.Ordinal).ShouldBeTrue("producer-receipt");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIPHON_TEST_TIMING_ROOT", previous);
        }
    }

    private async Task<RowRunResult> RunOwnedChild()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var workingDirectory = TempDir();
        Directory.CreateDirectory(Path.Combine(workingDirectory, "tests/Antiphon.Tests"));
        var runner = new RowRunner(driver, new FakePlatform());
        return await runner.RunAsync(new RowRequest
        {
            Name = "CP-9",
            Project = "tests/Antiphon.Tests",
            OutputPath = "bin-ex/",
            Filter = "/*/*/Sample/*",
            ResultsDirectory = Path.Combine(TempDir(), "row"),
            WorkingDirectory = workingDirectory,
            MinExecuted = 1,
            Commit = new string('a', 40),
        }, TextWriter.Null, CancellationToken.None);
    }
}
