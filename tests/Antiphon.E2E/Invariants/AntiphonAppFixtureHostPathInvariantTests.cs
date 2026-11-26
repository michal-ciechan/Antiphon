using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class AntiphonAppFixtureHostPathInvariantTests
{
    [Test]
    public void Tracked_windows_paths_include_the_fixture_custody_anchors()
    {
        var keys = AntiphonAppFixtureHostPathTests.WindowsPathKeys();
        // The two the fixture inherited before CARD-1795; any new one must be overridden too.
        keys.ShouldContain("Delegation:CheckInterpreterWorkingDirectory");
        keys.ShouldContain("ZombieCensus:SessionLogPath");
    }
}
