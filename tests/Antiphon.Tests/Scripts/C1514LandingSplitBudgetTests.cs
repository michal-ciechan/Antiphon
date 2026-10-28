using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1514LandingSplitBudgetTests
{
    [Test]
    public Task C1584_E2EExpandedBudget() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1584", "C1584_E2EExpandedBudget", 1,
            "C1584 E2E expanded cases fit conservative launch bounds with every required node once");

    [Test]
    public Task C1577_Group71MeasuredBudget() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1577", "C1577_Group71MeasuredBudget", 1,
            "C1577 group 71 uses observed bounds within half the unchanged watchdog with every node once");

    [Test]
    public Task C1404_Group10MeasuredBudget() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1404", "C1404_Group10MeasuredBudget", 1,
            "C1404 group 10 uses evidence costs, eight isolated launches and the unchanged watchdog");

    [Test]
    public Task C1410_MeasuredCostGroups() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1410", "C1410_MeasuredCostGroups", 1,
            "C1410 rc launch groups pack by measured cost under the watchdog and assign every node once");

    [Test]
    public Task G2865_Group28Group65MeasuredBudget() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "G2865", "G2865_Group28Group65MeasuredBudget", 1,
            "G2865 group 28 and 65 measured class costs fit inside chunk prices under the watchdog");
}
