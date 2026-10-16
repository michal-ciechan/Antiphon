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
            "C1313_ProjectAndCatalogUniverse", 5, "C1313 project-universe", "C1313 unclassified-project");

    [Test]
    public Task C1313_LiveEligibility() =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313",
            "C1313_LiveEligibility", 4, "C1313 live-obligations-visible", "C1313 unknown-resource");

    [Test]
    public Task C1313_MixedDeliveryInventory() =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313",
            "C1313_MixedDeliveryInventory", 2, "C1313 fake-required-real-excluded");
}
