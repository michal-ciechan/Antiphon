using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class BaselineComparerTests
{
    [Test]
    public async Task classifies_inherited_introduced_new()
    {
        var driver = Script("""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="1" outcome="Failed" duration="00:00:01" />
                <UnitTestResult testId="2" outcome="Passed" duration="00:00:01" />
              </Results>
              <TestDefinitions>
                <UnitTest id="1"><TestMethod className="N.Kept" name="bad" /></UnitTest>
                <UnitTest id="2"><TestMethod className="N.Fresh" name="good" /></UnitTest>
              </TestDefinitions>
              <ResultSummary><Counters total="2" executed="2" passed="1" failed="1" /></ResultSummary>
            </TestRun>
            """);
        var rows = new List<ReportRow>
        {
            Failures("CP-1", "bin-a", "N.Kept.bad", "N.Fresh.good", "N.Missing.gone"),
        };
        var kinds = await new BaselineComparer(driver).CompareAsync(
            CheckpointFixtures.TempDir(), CheckpointFixtures.TempDir(), "origin/master", rows,
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
            CancellationToken.None);
        kinds.Single(item => item.Name == "N.Kept.bad").Kind.ShouldBe("INHERITED");
        kinds.Single(item => item.Name == "N.Fresh.good").Kind.ShouldBe("INTRODUCED");
        kinds.Single(item => item.Name == "N.Missing.gone").Kind.ShouldBe("NEW");
    }

    [Test]
    public async Task adds_and_removes_the_detached_worktree_once_per_build()
    {
        var driver = Script(GreenTrx());
        var rows = new List<ReportRow>
        {
            Failures("CP-1", "bin-a", "N.A.one"),
            Failures("CP-2", "bin-a", "N.A.two"),
            Failures("CP-3", "bin-b", "N.B.three"),
        };
        await new BaselineComparer(driver).CompareAsync(
            CheckpointFixtures.TempDir(), CheckpointFixtures.TempDir(), "origin/master", rows,
            [
                new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" },
                new BuildSpec { Id = "bin-b", Project = "tests/Other.Tests", OutputPath = "bin-b/" },
            ],
            CancellationToken.None);
        driver.Count(request => request.Arguments.Contains("add") && request.Arguments.Contains("--detach")).ShouldBe(2);
        driver.Count(request => request.Arguments.Contains("remove") && request.Arguments.Contains("--force")).ShouldBe(2);
    }

    [Test]
    public async Task removes_worktree_when_build_fails()
    {
        var driver = new FakeDriver();
        driver.When(request => request.FileName == "git" && request.Arguments.Contains("rev-parse"),
            (_, _) => Task.FromResult(new DriverResult(0, new string('a', 40) + "\n", "")));
        driver.When(request => request.FileName == "git" && request.Arguments.Contains("fetch"),
            (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(request => request.Arguments.Contains("add"),
            (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(1, "", "fail")));
        driver.When(request => request.Arguments.Contains("remove"),
            (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        await new BaselineComparer(driver).CompareAsync(
            CheckpointFixtures.TempDir(), CheckpointFixtures.TempDir(), "origin/master",
            [Failures("CP-1", "bin-a", "N.A.one")],
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
            CancellationToken.None);
        driver.Count(request => request.Arguments.Contains("remove")).ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
    }

    private static FakeDriver Script(string trx)
    {
        var driver = new FakeDriver();
        driver.When(request => request.FileName == "git", (request, _) =>
        {
            var stdout = request.Arguments.Contains("rev-parse") ? new string('a', 40) + "\n" : "";
            return Task.FromResult(new DriverResult(0, stdout, ""));
        });
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            var path = CheckpointFixtures.TrxFile(request);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, trx);
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return driver;
    }

    private static ReportRow Failures(string id, string build, params string[] names) => new()
    {
        Id = id,
        Build = build,
        Filter = "/*/*/X/*",
        Failures = names.Select(name => new ReportFailure { Name = name, Message = "bad" }).ToList(),
    };

    private static string GreenTrx() => """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="1" outcome="Passed" duration="00:00:01" />
          </Results>
          <TestDefinitions>
            <UnitTest id="1"><TestMethod className="N.A" name="one" /></UnitTest>
          </TestDefinitions>
          <ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>
        </TestRun>
        """;
}
