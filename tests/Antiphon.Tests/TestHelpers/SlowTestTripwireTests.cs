using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class SlowTestTripwireTests
{
    [Test]
    public void allowlist_file_exists_and_has_spawn_lane_entries()
    {
        var path = Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "slow-tests-allowlist.txt");
        File.Exists(path).ShouldBeTrue();
        var entries = SlowTestTripwire.LoadAllowlist(path);
        entries.ShouldContain("SessionMessageQueuePtyIntegrationTests");
        entries.ShouldContain("RunnerProcessProbeTests");
    }

    [Test]
    public void unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Antiphon.Tests.Application.NewSlowThing.Foo" duration="00:00:06.5000000" outcome="Passed" />
                <UnitTestResult testName="Antiphon.Tests.AgentTui.RunnerProcessProbeTests.Bar" duration="00:00:08.0000000" outcome="Passed" />
                <UnitTestResult testName="Antiphon.Tests.Application.ColumnTextTests.Baz" duration="00:00:00.2000000" outcome="Passed" />
              </Results>
            </TestRun>
            """;
        var hits = SlowTestTripwire.FindUnlisted(trx, ["RunnerProcessProbeTests"]);
        hits.Count.ShouldBe(1);
        hits[0].TestName.ShouldContain("NewSlowThing");
        hits[0].Duration.ShouldBe(TimeSpan.FromMilliseconds(6500));
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}
