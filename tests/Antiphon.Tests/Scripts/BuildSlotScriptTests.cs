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
    public Task C589_WrapperUnreachableAtDeadline() => RunCaseAsync("C589_WrapperUnreachableAtDeadline", 4,
        "C589 WrapperUnreachableAtDeadline deadline_unreachable,unreachable fails open after grace",
        "C589 WrapperUnreachableAtDeadline deadline_unreachable,unreachable never runs unleased early",
        "C589 WrapperUnreachableAtDeadline deadline_notfound,notfound fails open after grace",
        "C589 WrapperUnreachableAtDeadline deadline_notfound,notfound never runs unleased early");

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

    [Test]
    public Task Wrapper_renews_a_renew_mode_grant_while_the_command_runs() => RunCaseAsync("C589_WrapperRenews", 2,
        "C589 WrapperRenews renews twice before release",
        "C589 WrapperRenews pid grant has no renewals");

    [Test]
    public Task C800_WrapperPassesWildcardArgvLiterally() => RunC800CaseAsync("C800_WrapperPassesWildcardArgvLiterally", 6,
        "C800 WrapperPassesWildcardArgvLiterally propagates exit 3",
        "C800 WrapperPassesWildcardArgvLiterally runs the command between grant and release",
        "C800 WrapperPassesWildcardArgvLiterally every token arrives literally",
        "C800 WrapperPassesWildcardArgvLiterally the child runs in the caller's directory",
        "C800 WrapperPassesWildcardArgvLiterally an in-process call passes the token literally",
        "C800 WrapperPassesWildcardArgvLiterally an in-process call runs the child at the pushed location");

    [Test]
    public Task C800_WrapperStartsUnitFilterWithinDeadline() => RunC800CaseAsync("C800_WrapperStartsUnitFilterWithinDeadline", 3,
        "C800 WrapperStartsUnitFilterWithinDeadline exits within 30 s",
        "C800 WrapperStartsUnitFilterWithinDeadline passes the Unit filter literally",
        "C800 WrapperStartsUnitFilterWithinDeadline releases the lease");

    [Test]
    public Task C800_WrapperForwardsScriptTokens() => RunC800CaseAsync("C800_WrapperForwardsScriptTokens", 4,
        "C800 WrapperForwardsScriptTokens propagates exit 6 from a script command",
        "C800 WrapperForwardsScriptTokens a script command receives its tokens unchanged",
        "C800 WrapperForwardsScriptTokens an unsplatted array stays separate",
        "C800 WrapperForwardsScriptTokens an empty array element stays separate");

    [Test]
    public Task C800_WrapperLaunchesNativeExecutableLiterally() => RunC800CaseAsync("C800_WrapperLaunchesNativeExecutableLiterally", 3,
        "C800 WrapperLaunchesNativeExecutableLiterally exits zero",
        "C800 WrapperLaunchesNativeExecutableLiterally passes wildcard argv unchanged",
        "C800 WrapperLaunchesNativeExecutableLiterally keeps the caller directory");

    [Test]
    public Task C800_WrapperInterruptKillsChildAndReleasesLease()
    {
        if (!OperatingSystem.IsLinux()) throw new TUnit.Core.Exceptions.SkipTestException("Linux SIGINT behavior only");
        return RunC800CaseAsync("C800_WrapperInterruptKillsChildAndReleasesLease", 3,
            "C800 WrapperInterruptKillsChildAndReleasesLease the wrapper exits within 5 s of SIGINT",
            "C800 WrapperInterruptKillsChildAndReleasesLease the child is gone within 5 s",
            "C800 WrapperInterruptKillsChildAndReleasesLease the lease is released after the command");
    }

    [Test]
    public Task C845_WrapperBindsNamedScriptParameters() => RunC845CaseAsync(
        "WrapperBindsNamedScriptParameters", ["file-true", "file-false", "inprocess-true", "inprocess-false"],
        ["exits zero", "payload parses", "binds typed values", "runs outside the lease holder"]);

    [Test]
    public Task C845_WrapperPropagatesScriptExitCodes() => RunC845CaseAsync(
        "WrapperPropagatesScriptExitCodes", ["exit0", "exit7", "exit23", "normal23", "parent37"],
        ["propagates the process exit", "preserves body and stream sentinels"]);

    [Test]
    public Task C845_WrapperReleasesLeaseAfterScriptFailure() => RunC845CaseAsync(
        "WrapperReleasesLeaseAfterScriptFailure", ["exit9", "throw", "binding", "unresolved"],
        ["returns the failure exit", "observes the body boundary", "releases the granted lease in order"]);

    [Test]
    public Task C845_WrapperPreservesScriptPathsAndValues() => RunC845CaseAsync(
        "WrapperPreservesScriptPathsAndValues", ["absolute", "pushed-relative"],
        ["exits zero", "payload parses", "preserves literal values", "uses caller location", "resolves relative input", "does not evaluate text"]);

    private static Task RunC845CaseAsync(string stem, string[] scenarios, string[] suffixes) =>
        ScriptHarness.RunHarnessCaseAsync("test-build-slot.ps1", "C845", "C845_" + stem,
            scenarios.Length * suffixes.Length,
            scenarios.SelectMany(scenario => suffixes.Select(suffix => $"C845 {stem} {scenario} {suffix}")).ToArray());

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-build-slot.ps1", "C589", caseName, expectedRows, requiredRows);

    private static Task RunC800CaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-build-slot.ps1", "C800", caseName, expectedRows, requiredRows);
}
