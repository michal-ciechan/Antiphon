using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1514LandingSplitBudgetTests
{
    [Test]
    public Task C1404_Group10MeasuredBudget() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1404", "C1404_Group10MeasuredBudget", 1,
            "C1404 group 10 uses evidence costs, eight isolated launches and the unchanged watchdog");

    [Test]
    public Task C1410_MeasuredCostGroups() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1410", "C1410_MeasuredCostGroups", 1,
            "C1410 rc launch groups pack by measured cost under the watchdog and assign every node once");
}
