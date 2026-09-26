using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RerunPolicyTests
{
    [Test]
    public async Task reruns_only_known_flaky_names_once()
    {
        var name = "Antiphon.Tests.Sample.alpha";
        var driver = new FakeDriver();
        var passes = 0;
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            passes++;
            var outcome = passes == 1 ? "Failed" : "Passed";
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), (name, outcome));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, [name]);
        result.ExitCode.ShouldBe(0);
        result.Reruns.ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(2);
    }

    [Test]
    public async Task unlisted_failure_never_reruns()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Failed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, ["Antiphon.Tests.Other.beta"]);
        result.ExitCode.ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(1);
    }

    [Test]
    public void rerun_filter_is_method_scoped()
    {
        var filters = RerunPolicy.MethodFilters([
            "Antiphon.Tests.Sample.alpha",
            "Antiphon.Tests.Sample.beta",
            "Antiphon.Tests.Other.gamma",
        ]);
        filters.ShouldBe([
            "/*/*/Sample/(alpha*)|(beta*)",
            "/*/*/Other/gamma",
        ]);
    }

    [Test]
    public async Task rerun_invokes_once_per_class()
    {
        var alpha = "Antiphon.Tests.Sample.alpha";
        var beta = "Antiphon.Tests.Sample.beta";
        var gamma = "Antiphon.Tests.Other.gamma";
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var file = CheckpointFixtures.TrxFile(request);
            if (file.Contains("rerun-", StringComparison.Ordinal))
                CheckpointFixtures.WriteResults(file, (alpha, "Passed"), (beta, "Passed"), (gamma, "Passed"));
            else
                CheckpointFixtures.WriteResults(file, (alpha, "Failed"), (beta, "Failed"), (gamma, "Failed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, [alpha, beta, gamma]);
        result.ExitCode.ShouldBe(0);
        result.Reruns.ShouldBe(1);
        var reruns = driver.Calls.Where(CheckpointFixtures.IsRun).Skip(1).ToList();
        reruns.Count.ShouldBe(2);
        reruns[0].Arguments.ShouldContain("/*/*/Sample/(alpha*)|(beta*)");
        reruns[1].Arguments.ShouldContain("/*/*/Other/gamma");
    }

    [Test]
    public async Task row_green_only_when_rerun_passed_and_nothing_else_failed()
    {
        var flaky = "Antiphon.Tests.Sample.alpha";
        var other = "Antiphon.Tests.Sample.beta";
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var file = CheckpointFixtures.TrxFile(request);
            if (file.EndsWith("rerun-1.trx", StringComparison.Ordinal))
                CheckpointFixtures.WriteResults(file, (flaky, "Passed"));
            else
                CheckpointFixtures.WriteResults(file, (flaky, "Failed"), (other, "Failed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var mixed = await Run(driver, [flaky]);
        mixed.ExitCode.ShouldBe(1);

        var only = new FakeDriver();
        var n = 0;
        only.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        only.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            n++;
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), (flaky, n == 1 ? "Failed" : "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var green = await Run(only, [flaky]);
        green.ExitCode.ShouldBe(0);
        green.Reruns.ShouldBe(1);
    }

    [Test]
    public async Task rerun_lines_and_reruns_suffix()
    {
        var name = "Antiphon.Tests.Sample.alpha";
        var driver = new FakeDriver();
        var n = 0;
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            n++;
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), (name, n == 1 ? "Failed" : "Failed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, [name]);
        result.Line.ShouldContain("reruns=1");
        result.RerunLines.ShouldContain($"RERUN {name} first=Failed second=Failed");
        result.ExitCode.ShouldBe(1);
    }

    private static Task<RowRunResult> Run(FakeDriver driver, IReadOnlyList<string> known)
    {
        var runner = new RowRunner(driver, new FakePlatform());
        return runner.RunAsync(new RowRequest
        {
            Name = "CP-2",
            Project = "tests/Antiphon.Tests",
            OutputPath = "bin-ex/",
            Filter = "/*/*/Sample/*",
            ResultsDirectory = Path.Combine(CheckpointFixtures.TempDir(), "row"),
            WorkingDirectory = CheckpointFixtures.TempDir(),
            MinExecuted = 1,
            KnownFlaky = known,
            Commit = new string('d', 40),
        }, TextWriter.Null, CancellationToken.None);
    }
}
