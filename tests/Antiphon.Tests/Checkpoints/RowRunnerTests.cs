using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RowRunnerTests
{
    [Test]
    public async Task green_row_is_exit_0()
    {
        var result = await Run(passed: true, min: 1);
        result.ExitCode.ShouldBe(0);
        result.Line.ShouldContain("build=ok");
        result.Line.ShouldContain("executed=1");
    }

    [Test]
    public async Task red_row_is_exit_1()
    {
        var result = await Run(passed: false, min: 1);
        result.ExitCode.ShouldBe(1);
        result.Output.ShouldContain("FAILED ");
    }

    [Test]
    public async Task zero_executed_is_exit_3()
    {
        var result = await Run(passed: true, min: 1, write: true, zero: true);
        result.ExitCode.ShouldBe(3);
        result.Output.ShouldContain("MIN EXECUTED");
    }

    [Test]
    public async Task roster_miss_is_exit_3()
    {
        var result = await Run(passed: true, min: 1, expect: ["MissingClass"]);
        result.ExitCode.ShouldBe(3);
        result.Output.ShouldContain("ROSTER MISS MissingClass");
    }

    [Test]
    public async Task skipped_class_still_meets_the_roster()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(
                CheckpointFixtures.TrxFile(request),
                ("Antiphon.Tests.Sample.alpha", "Passed"),
                ("Antiphon.Tests.Checkpoints.DetachedLauncherTests.executor_survives_its_starter", "NotExecuted"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, passed: true, min: 1, expect: ["Sample", "DetachedLauncherTests"]);
        result.ExitCode.ShouldBe(0);
        result.Output.ShouldNotContain("ROSTER MISS");
    }

    [Test]
    public async Task build_failed_is_exit_2()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(1, "", "")));
        var result = await Run(driver, passed: true, min: 1);
        result.ExitCode.ShouldBe(2);
        result.Output.ShouldContain("build=failed");
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
    }

    [Test]
    public async Task no_trx_is_exit_2()
    {
        var result = await Run(passed: true, min: 1, write: false);
        result.ExitCode.ShouldBe(2);
        result.Output.ShouldContain("no TRX");
    }

    [Test]
    public async Task malformed_trx_is_exit_2()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var path = CheckpointFixtures.TrxFile(request);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not xml");
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, passed: true, min: 1);
        result.ExitCode.ShouldBe(2);
        result.Output.ShouldContain("malformed TRX");
    }

    [Test]
    public async Task no_build_reports_reused()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        var result = await Run(driver, passed: true, min: 1, noBuild: true);
        result.ExitCode.ShouldBe(0);
        result.Line.ShouldContain("build=reused");
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(0);
    }

    [Test]
    public async Task properties_are_forwarded_to_build_and_run()
    {
        var driver = DriverThatWrites();
        await Run(driver, passed: true, min: 1, properties: [new KeyValuePair<string, string>("Foo", "bar")], windows: false);
        driver.Calls.ShouldAllBe(call =>
            call.Arguments.Contains("--property:Foo=bar") && call.Arguments.Contains("--property:UseAppHost=false"));
        driver.Calls.ShouldContain(call => call.Arguments.Contains("-nodeReuse:false"));
    }

    [Test]
    public async Task linux_defaults_use_app_host_false()
    {
        var driver = DriverThatWrites();
        await Run(driver, passed: true, min: 1, windows: false);
        driver.Calls.ShouldAllBe(call => call.Arguments.Contains("--property:UseAppHost=false"));
    }

    [Test]
    public async Task windows_leaves_use_app_host_unset()
    {
        var driver = DriverThatWrites();
        await Run(driver, passed: true, min: 1, windows: true);
        driver.Calls.ShouldAllBe(call => !call.Arguments.Any(arg => arg.StartsWith("--property:UseAppHost", StringComparison.Ordinal)));
    }

    [Test]
    public async Task explicit_use_app_host_wins()
    {
        var driver = DriverThatWrites();
        await Run(driver, passed: true, min: 1, windows: false,
            properties: [new KeyValuePair<string, string>("UseAppHost", "true")]);
        driver.Calls.ShouldAllBe(call => call.Arguments.Contains("--property:UseAppHost=true"));
        driver.Calls.ShouldAllBe(call => !call.Arguments.Contains("--property:UseAppHost=false"));
    }

    private static FakeDriver DriverThatWrites()
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return driver;
    }

    private static Task<RowRunResult> Run(bool passed, int min, bool write = true, bool zero = false, bool noBuild = false,
        List<string>? expect = null, List<KeyValuePair<string, string>>? properties = null, bool windows = false)
    {
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            if (write)
            {
                if (zero)
                    CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request));
                else if (passed)
                    CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Passed"));
                else
                    CheckpointFixtures.WriteResults(CheckpointFixtures.TrxFile(request), ("Antiphon.Tests.Sample.alpha", "Failed"));
            }

            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return Run(driver, passed, min, noBuild, expect, properties, windows);
    }

    private static Task<RowRunResult> Run(FakeDriver driver, bool passed, int min, bool noBuild = false,
        List<string>? expect = null, List<KeyValuePair<string, string>>? properties = null, bool windows = false)
    {
        var runner = new RowRunner(driver, new FakePlatform { IsWindows = windows });
        return runner.RunAsync(new RowRequest
        {
            Name = "CP-1",
            Project = "tests/Antiphon.Tests",
            OutputPath = "bin-ex/",
            Filter = "/*/*/Sample/*",
            ResultsDirectory = Path.Combine(CheckpointFixtures.TempDir(), "row"),
            WorkingDirectory = CheckpointFixtures.TempDir(),
            NoBuild = noBuild,
            MinExecuted = min,
            Expect = expect ?? [],
            Properties = properties ?? [],
            Commit = new string('a', 40),
        }, TextWriter.Null, CancellationToken.None);
    }
}
