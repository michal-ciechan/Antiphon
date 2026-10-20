using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGatePlatformTests
{
    [Test]
    public Task C1323_PlatformPolicy() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1323_PlatformPolicy", 19,
            "C1323 windows-retains-69-and-siblings", "C1323 linux-excludes-exactly-69",
            "C1323 schema4-selector-admitted", "C1323 pinned-authority-validates-windows",
            "C1323 complete-synthetic-ledger-validates", "C1323 missing-windows-uid-refuses");
}
