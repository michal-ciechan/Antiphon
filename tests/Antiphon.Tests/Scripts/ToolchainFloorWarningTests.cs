using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1698 slice 1. Each method runs one Test-C1698_* case of scripts/test-toolchain-floor.ps1
/// (Get-ToolchainFloorWarnings with an injected version source, and the restart entry points with
/// a fake git), or of the nightly run/tests harnesses for the summary and run wiring.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ToolchainFloorWarningTests
{
    [Test]
    public Task C1698_GitBelowFloor() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_GitBelowFloor), 8,
            [
                "C1698 Below names the tool",
                "C1698 Below names the found version",
                "C1698 Below names the floor",
                "C1698 Below names what stops working",
                "C1698 Below names the fix command",
                "C1698 Below message carries all five fields"
            ]);

    [Test]
    public Task C1698_GitAtOrAboveFloor() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_GitAtOrAboveFloor), 5,
            [
                "C1698 AtOrAbove no warning git version 2.43",
                "C1698 AtOrAbove no warning git version 2.43.0",
                "C1698 AtOrAbove no warning git version 2.100.1"
            ]);

    [Test]
    public Task C1698_UnreadableVersion() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_UnreadableVersion), 6,
            [
                "C1698 Unreadable warns, never throws: garbage",
                "C1698 Unreadable warns, never throws: empty",
                "C1698 Unreadable warns, never throws: throwing source",
                "C1698 Unreadable message carries floor, impact and fix: garbage",
                "C1698 Unreadable message carries floor, impact and fix: empty",
                "C1698 Unreadable message carries floor, impact and fix: throwing source"
            ]);

    [Test]
    public Task C1698_SdkFloor() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_SdkFloor), 4,
            [
                "C1698 Sdk 11.0.100 [/x]",
                "C1698 Sdk 9.0.300 [/x] 10.0.204 [/x]"
            ]);

    [Test]
    public Task C1698_ManifestSources() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_ManifestSources), 5,
            [
                "C1698 Manifest git floor is the image guard declaration",
                "C1698 Manifest SDK floor is global.json",
                "C1698 Manifest pwsh node docker are listed, not enforced",
                "C1698 Manifest missing floor sources warn, not throw"
            ]);

    [Test]
    public Task C1698_RestartServerUnchanged() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartServerUnchanged), 5,
            [
                "C1698 RestartServer prints the warning",
                "C1698 RestartServer exit 0 with a warning",
                "C1698 RestartServer same stop build start with a warning",
                "C1698 RestartServer build failure still exit 1 with a warning"
            ]);

    [Test]
    public Task C1698_RestartRunnerUnchanged() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartRunnerUnchanged), 4,
            [
                "C1698 RestartRunner prints the warning",
                "C1698 RestartRunner same result with a warning",
                "C1698 RestartRunner same exit code with a warning"
            ]);

    /// <summary>A toolchain library that throws or cannot be parsed when dot-sourced (the only failure
    /// that reaches the entry wrapper) leaves restart-server at exit 0 with the same calls.</summary>
    [Test]
    public Task C1698_RestartServerLibraryLoadFailure() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartServerLibraryLoadFailure), 2,
            [
                "C1698 RestartServer survives a library that fails to load: throws",
                "C1698 RestartServer survives a library that fails to load: unparsable"
            ]);

    [Test]
    public Task C1698_RestartRunnerLibraryLoadFailure() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_RestartRunnerLibraryLoadFailure), 2,
            [
                "C1698 RestartRunner survives a library that fails to load: throws",
                "C1698 RestartRunner survives a library that fails to load: unparsable"
            ]);

    /// <summary>A pwsh 7 only construct (ternary, ??, ?., pipeline chains, -Parallel) in the library
    /// or its entry callers; the controls prove the scan flags each one. A real powershell.exe 5.1
    /// parse on Windows is the fuller proof.</summary>
    [Test]
    public Task C1698_WindowsPowerShellSyntax() =>
        ScriptHarness.RunHarnessCaseAsync("test-toolchain-floor.ps1", "C1698",
            nameof(C1698_WindowsPowerShellSyntax), 18,
            [
                "C1698 Syntax scan flags ternary",
                "C1698 Syntax scan flags null-coalescing",
                "C1698 Syntax scan flags null-conditional member",
                "C1698 Syntax scan flags pipeline chain and",
                "C1698 Syntax scan flags ForEach-Object -Parallel",
                "C1698 Syntax scan passes the 5.1 spellings",
                "C1698 Windows PowerShell 5.1 syntax: scripts/lib/toolchain-floor.ps1",
                "C1698 Windows PowerShell 5.1 syntax: restart-server.ps1",
                "C1698 Windows PowerShell 5.1 syntax: scripts/session-runner-restart-health.ps1",
                "C1698 Windows PowerShell 5.1 syntax: scripts/lib/nightly-run-impl.ps1",
                "C1698 Windows PowerShell 5.1 syntax: scripts/lib/nightly-tests-impl.ps1"
            ]);

    [Test]
    public Task C1698_RunPrintsToolchainWarning() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1698",
            nameof(C1698_RunPrintsToolchainWarning), 4,
            [
                "C1698 Run prints the git floor warning",
                "C1698 Run prints none at the floor",
                "C1698 Run warns before the tests hop",
                "C1698 Run exit code unchanged by the warning"
            ]);

    [Test]
    public Task C1698_SummaryRecordsToolchain() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1698",
            nameof(C1698_SummaryRecordsToolchain), 4,
            [
                "C1698 Summary records the git version and floor",
                "C1698 Summary records the SDK version",
                "C1698 Summary records the warning",
                "C1698 Summary preflight block records the toolchain"
            ]);

    [Test]
    public Task C1698_RunLibraryLoadFailure() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1698",
            nameof(C1698_RunLibraryLoadFailure), 2,
            [
                "C1698 Run survives a library that fails to load: throws",
                "C1698 Run survives a library that fails to load: unparsable"
            ]);

    /// <summary>summary.json as a real Invoke-AntiphonNightlyTests run writes it to disk.</summary>
    [Test]
    public Task C1698_SummaryJsonRecordsToolchain() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1698",
            nameof(C1698_SummaryJsonRecordsToolchain), 2,
            [
                "C1698 Summary json on disk records the versions and floors",
                "C1698 Summary json on disk records the warning"
            ]);
}
