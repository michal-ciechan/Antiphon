using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcRosterRepairTests
{
    [Test]
    public Task Mirror() => ScriptHarness.RunHarnessCaseAsync("test-rc-roster-repairs.ps1", "C1561", "Mirror", 2,
        "C1561 platform required UID sets", "C1561 all excluded UIDs have Linux obligation");

    [Test]
    public Task Explicit() => ScriptHarness.RunHarnessCaseAsync("test-rc-roster-repairs.ps1", "C1566", "Explicit", 4,
        "C1566 owned manual disposition windows", "C1566 only offline sibling launched windows",
        "C1566 owned manual disposition linux", "C1566 only offline sibling launched linux");

    [Test]
    public Task Costs() => ScriptHarness.RunHarnessCaseAsync("test-rc-roster-repairs.ps1", "C1567", "Costs", 4,
        "C1567 measured heavy prices retained", "C1567 excessive launch refused",
        "C1567 ceiling boundary accepted", "C1567 watchdog unchanged");
}
