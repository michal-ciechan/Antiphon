using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0589 V-5. Each method runs the matching <c>Test-C589_*</c> case of
/// <c>scripts/test-build-slot.ps1</c> — an offline harness whose slot shim stands in for the
/// runner's <c>/build-slots</c> and whose command shim stands in for the wrapped build/test
/// driver, so no runner is contacted and nothing is built. Exit 0 plus the case's named PASS
/// inventory is the verdict.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BuildSlotScriptTests
{
    [Test]
    public Task C589_WrapperRunsUnderLease() => RunCaseAsync("C589_WrapperRunsUnderLease", 7,
        "C589 WrapperRunsUnderLease propagates the command exit code",
        "C589 WrapperRunsUnderLease runs the command between grant and release",
        "C589 WrapperRunsUnderLease applies the grant -maxcpucount to dotnet build",
        "C589 WrapperRunsUnderLease prints the granted and released lines",
        "C589 WrapperRunsUnderLease asks under its -Label",
        "C589 WrapperRunsUnderLease -NoSlot runs the command unchanged without asking",
        "C589 WrapperRunsUnderLease an in-process call leases, runs and returns the exit code");

    [Test]
    public Task C589_WrapperMaxCpuCountRules() => RunCaseAsync("C589_WrapperMaxCpuCountRules", 12,
        "C589 WrapperMaxCpuCountRules dotnet build gets the count",
        "C589 WrapperMaxCpuCountRules dotnet test gets it before --",
        "C589 WrapperMaxCpuCountRules dotnet run is left alone because it forwards the switch to the program",
        "C589 WrapperMaxCpuCountRules dotnet run --no-build is left alone",
        "C589 WrapperMaxCpuCountRules an explicit -m:2 wins",
        "C589 WrapperMaxCpuCountRules an explicit bare -maxcpucount wins",
        "C589 WrapperMaxCpuCountRules a -m after -- belongs to the program",
        "C589 WrapperMaxCpuCountRules a wrapped dotnet run is leased, unchanged and says why");

    [Test]
    public Task C589_WrapperReleasesOnFailure() => RunCaseAsync("C589_WrapperReleasesOnFailure", 2,
        "C589 WrapperReleasesOnFailure exit code 1",
        "C589 WrapperReleasesOnFailure releases the lease after a failing command");

    [Test]
    public Task C589_WrapperTimeout() => RunCaseAsync("C589_WrapperTimeout", 3,
        "C589 WrapperTimeout exit code 4",
        "C589 WrapperTimeout runs nothing and releases nothing",
        "C589 WrapperTimeout prints the waiting and timeout lines");

    [Test]
    public Task C589_WrapperUnreachableAtDeadline() => RunCaseAsync("C589_WrapperUnreachableAtDeadline", 2,
        "C589 WrapperUnreachableAtDeadline exits 4 when the first unreachable answer arrives at the wait deadline",
        "C589 WrapperUnreachableAtDeadline never starts a build before the full grace");

    [Test]
    public Task C589_WrapperUnreachable() => RunCaseAsync("C589_WrapperUnreachable", 4,
        "C589 WrapperUnreachable prints the unleased line",
        "C589 WrapperUnreachable runs with the fallback -maxcpucount 4 and releases nothing",
        "C589 WrapperUnreachable a runner that stops answering mid-wait falls back unleased",
        "C589 WrapperUnreachable a disabled broker answers unlimited with its cpu count and holds nothing");

    [Test]
    public Task C589_WrapperAsciiOnly() => RunCaseAsync("C589_WrapperAsciiOnly", 5,
        "C589 WrapperAsciiOnly build-slot.ps1 is ASCII-only",
        "C589 WrapperAsciiOnly test-build-slot.ps1 is ASCII-only",
        "C589 WrapperAsciiOnly c589-slot-shim.ps1 is ASCII-only",
        "C589 WrapperAsciiOnly c589-command-shim.ps1 is ASCII-only");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-build-slot.ps1", "C589", caseName, expectedRows, requiredRows);
}
