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

    [Test]
    public Task C1323_PlatformExecution() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1323_PlatformExecution", 12,
            "C1323 linux-launches-only-portable-cases", "C1323 windows-retains-delivery-cases",
            "C1323 linux-summary-is-diagnostic", "C1323 linux-state-is-diagnostic",
            "C1323 linux-leaves-green-files-identical", "C1323 unknown-platform-launches-nothing");

    [Test]
    public Task C1415_SkipExclusions() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "C1415_SkipExclusions", 10,
            "C1323 c1415-two-method-scoped-rows", "C1323 c1415-excluded-methods-exist",
            "C1323 c1415-windows-excludes-exactly", "C1323 c1415-windows-launches-only-required",
            "C1323 c1415-windows-census-complete", "C1323 c1415-linux-excludes-exactly",
            "C1323 c1425-pack-methods-required", "C1323 c1415-launched-skips-stay-incomplete");

    [Test]
    public Task LinuxOnlyWindowsExclusionPolicy() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-platform.ps1", "C1323", "LinuxOnlyWindowsExclusionPolicy", 6,
            "C1323 every-new-row-has-linux-gate", "C1323 document-classes-are-excluded-or-decided",
            "C1323 decision-classes-are-not-class-excluded", "C1323 linux-only-rows-leave-census-complete",
            "C1323 decision-gaps-stay-incomplete", "C1323 linux-still-requires-the-rows");
}
