using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionCatalogTests
{
    [Test]
    public Task C1313_ProjectAndCatalogUniverse() =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313",
            "C1313_ProjectAndCatalogUniverse", 1, "C1313 project-universe");
}
