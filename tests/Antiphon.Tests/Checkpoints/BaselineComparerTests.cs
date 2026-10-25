using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class BaselineComparerTests : CheckpointTestBase
{
    [Test]
    public void checkout_path_refuses_unsafe_build_ids()
    {
        var source = TempDir();
        var safe = BaselineComparer.CheckoutPath(source, "20260101-000000-abcd", "bin-a");
        var baselineRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "antiphon-checkpoints-baseline"));
        var prefix = baselineRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Path.GetFullPath(safe).StartsWith(prefix, StringComparison.Ordinal).ShouldBeTrue();
        Path.GetFileName(safe).ShouldBe("baseline-a");
        foreach (var id in new[] { "tests", ".", "..", "src", Path.GetFullPath(source) })
            Should.Throw<InvalidOperationException>(() => BaselineComparer.CheckoutPath(source, "20260101-000000-abcd", id));
    }

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
            TempDir(), TempDir(), "origin/master", rows,
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
            CancellationToken.None);
        kinds.Single(item => item.Name == "N.Kept.bad").Kind.ShouldBe("INHERITED");
        kinds.Single(item => item.Name == "N.Fresh.good").Kind.ShouldBe("INTRODUCED");
        kinds.Single(item => item.Name == "N.Missing.gone").Kind.ShouldBe("NEW");
    }

    [Test]
    public async Task red_at_base_census_is_inherited_when_the_census_skips_build_output_folders()
    {
        // CARD-1231: TestLaneCategoryGuardTests and other repository censuses skip a source
        // path with a bin, obj or bin-* segment. This fake run does the same with the
        // segments the tool chooses below the temp root: inside such a folder it scans
        // nothing and passes, elsewhere it reports the failure that is red at the base.
        var driver = Script(request => SkipsBuildOutput(request.WorkingDirectory) ? """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="1" outcome="Passed" duration="00:00:01" />
              </Results>
              <TestDefinitions>
                <UnitTest id="1"><TestMethod className="N.Guard" name="census" /></UnitTest>
              </TestDefinitions>
              <ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>
            </TestRun>
            """ : """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="1" outcome="Failed" duration="00:00:01" />
              </Results>
              <TestDefinitions>
                <UnitTest id="1"><TestMethod className="N.Guard" name="census" /></UnitTest>
              </TestDefinitions>
              <ResultSummary><Counters total="1" executed="1" passed="0" failed="1" /></ResultSummary>
            </TestRun>
            """);
        var kinds = await new BaselineComparer(driver).CompareAsync(
            TempDir(), TempDir(), "origin/master",
            [Failures("CP-1", "bin-c507-s1", "N.Guard.census")],
            [new BuildSpec { Id = "bin-c507-s1", Project = "tests/Antiphon.Tests", OutputPath = "bin-c507-s1/" }],
            CancellationToken.None);
        kinds.Single().Kind.ShouldBe("INHERITED", "the baseline checkout must not sit under a folder a census skips");
        Path.GetFileName(driver.Calls.First(CheckpointFixtures.IsRun).WorkingDirectory).ShouldBe("baseline-c507-s1");
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
            TempDir(), TempDir(), "origin/master", rows,
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
            TempDir(), TempDir(), "origin/master",
            [Failures("CP-1", "bin-a", "N.A.one")],
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
            CancellationToken.None);
        driver.Count(request => request.Arguments.Contains("remove")).ShouldBe(1);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
    }

    [Test]
    public async Task baseline_checkout_is_outside_the_worktree_and_evidence_stays_in_the_run()
    {
        var driver = Script(GreenTrx());
        var slots = new RecordingSlots();
        var source = TempDir();
        var run = TempDir();
        var comparer = new BaselineComparer(driver, slots, TimeSpan.FromMinutes(5));
        await comparer.CompareAsync(
            source, run, "origin/master",
            [Failures("CP-1", "bin-a", "N.A.one")],
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
            CancellationToken.None);
        var build = driver.Calls.First(CheckpointFixtures.IsBuild);
        var sourceFull = Path.GetFullPath(source) + Path.DirectorySeparatorChar;
        Path.GetFullPath(build.WorkingDirectory).StartsWith(sourceFull, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        build.LogPath.ShouldNotBeNull();
        Path.GetFullPath(build.LogPath!).StartsWith(Path.GetFullPath(run), StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        build.LogPath.ShouldContain("build.log");
        comparer.ToolRuns.ShouldContain(item => item.StartsWith("tool-run: baseline git", StringComparison.Ordinal));
        comparer.ToolRuns.ShouldContain(item => item.Contains("baseline build", StringComparison.Ordinal));
        comparer.ToolRuns.ShouldContain(item => item.Contains("baseline run", StringComparison.Ordinal));
        slots.Labels.ShouldContain(item => item.Contains("baseline git", StringComparison.Ordinal));
        slots.Labels.ShouldContain(item => item.Contains("baseline build", StringComparison.Ordinal));
        slots.Labels.ShouldContain(item => item.Contains("baseline run", StringComparison.Ordinal));
    }

    [Test]
    public async Task baseline_stops_when_the_timeout_is_already_spent()
    {
        var driver = Script(GreenTrx());
        var comparer = new BaselineComparer(driver, timeout: TimeSpan.Zero);
        await comparer.CompareAsync(
            TempDir(), TempDir(), "origin/master",
            [Failures("CP-1", "bin-a", "N.A.one")],
            [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
            CancellationToken.None);
        driver.Count(CheckpointFixtures.IsBuild).ShouldBe(0);
        driver.Count(CheckpointFixtures.IsRun).ShouldBe(0);
        comparer.ToolRuns.ShouldContain(item => item.Contains("git fetch", StringComparison.Ordinal));
    }

    [Test]
    public async Task pre_existing_checkout_survives_when_creation_fails()
    {
        var source = TempDir();
        var run = TempDir();
        var runId = Path.GetFileName(run.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var checkout = BaselineComparer.CheckoutPath(source, runId, "bin-a");
        Directory.CreateDirectory(checkout);
        var keep = Path.Combine(checkout, "keep.txt");
        File.WriteAllText(keep, "preexisting");
        var driver = new FakeDriver();
        driver.When(request => request.FileName == "git" && request.Arguments.Contains("rev-parse"),
            (_, _) => Task.FromResult(new DriverResult(0, new string('a', 40) + "\n", "")));
        driver.When(request => request.FileName == "git" && request.Arguments.Contains("fetch"),
            (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        driver.When(request => request.Arguments.Contains("add"),
            (_, _) => Task.FromResult(new DriverResult(1, "", "exists")));
        try
        {
            await new BaselineComparer(driver).CompareAsync(
                source, run, "origin/master",
                [Failures("CP-1", "bin-a", "N.A.one")],
                [new BuildSpec { Id = "bin-a", Project = "tests/Antiphon.Tests", OutputPath = "bin-a/" }],
                CancellationToken.None);
            File.ReadAllText(keep).ShouldBe("preexisting");
            Directory.Exists(checkout).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(checkout))
                Directory.Delete(checkout, recursive: true);
        }
    }

    private sealed class RecordingSlots : IBuildSlotClient
    {
        public List<string> Labels { get; } = [];

        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SlotSession("enabled", 4));

        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            Labels.Add(label);
            return Task.FromResult(new SlotLease { State = "granted", MaxCpuCount = 4 });
        }
    }

    private static FakeDriver Script(string trx) => Script(_ => trx);

    private static FakeDriver Script(Func<DriverRequest, string> trx)
    {
        var driver = new FakeDriver();
        driver.When(request => request.FileName == "git", (request, _) =>
        {
            CheckpointNativeDiagnostics.Driver(request);
            var stdout = request.Arguments.Contains("rev-parse") ? new string('a', 40) + "\n" : "";
            return Task.FromResult(new DriverResult(0, stdout, ""));
        });
        driver.When(CheckpointFixtures.IsBuild, (request, _) =>
        {
            CheckpointNativeDiagnostics.Driver(request);
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        driver.When(CheckpointFixtures.IsRun, (request, _) =>
        {
            CheckpointNativeDiagnostics.Driver(request);
            var path = CheckpointFixtures.TrxFile(request);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, trx(request));
            return Task.FromResult(new DriverResult(0, "", ""));
        });
        return driver;
    }

    private static bool SkipsBuildOutput(string path) =>
        Path.GetRelativePath(Path.GetTempPath(), path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("bin-", StringComparison.OrdinalIgnoreCase));

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
