using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChangeAwareReleaseTests
{
    [Test]
    public Task C1313_ModeCredit() => Run("C1313_ModeCredit", 7, "unqualified-no-credit", "default-full", "explicit-full", "shadow-full", "unqualified-full", "qualification-root-unchanged", "selector-fallback-full");

    [Test]
    public Task C1313_BaselineAuthority() => Run("C1313_BaselineAuthority", 14, "baseline-absent-full", "baseline-full-bound", "historical-baseline-accepted", "baseline-full-only", "baseline-readiness-refused", "baseline-legacy-refused", "baseline-foreign-refused", "baseline-platform-refused", "baseline-corrupt-refused", "contract-hash-bound", "git-object-refused", "baseline-ancestor-required", "baseline-incompatible-full", "baseline-publication-required");

    [Test]
    public Task C1313_ParameterHops() => Run("C1313_ParameterHops", 10, "hop-baseline-bound", "hop-executed-full", "attempt-identical-resume", "attempt-conflict-refused", "prior-attempt-kept", "hop-digest-refused", "hop-sha-refused", "pwsh-required-before-work", "full-without-pwsh", "attempt-survives-cleanup");

    // CARD-1590: a direct run must not leave its nested fixture repositories behind,
    // on the failure path (no -FilterHost: fixture-discovery-failed after New-Fx) or on success.
    [Test]
    public Task C1590_FailedRunRemovesFixtureRoots() => RunOwnedRoot("C1313_ModeCredit", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldNotBe(0, output);
        output.ShouldContain("fixture-discovery-failed", Case.Sensitive, output);
        Directory.Exists(Path.Combine(root, "discovery")).ShouldBeTrue(output);
        AssertNoFixtureRepository(root, output);
    });

    // Budget: 2x the 358 s direct run on loaded server2 (2026-11-03, n=1; provisional).
    [Test, Timeout(780_000)]
    public Task C1590_PassedRunRemovesFixtureRoots() => RunOwnedRoot("C1313_BaselineAuthority", FilterHostArguments, TimeSpan.FromSeconds(720), (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
        AssertNoFixtureRepository(root, output);
    });

    private static readonly string[] FilterHostArguments =
        ["-FilterHost", Path.Combine(AppContext.BaseDirectory, "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")];

    private static void AssertNoFixtureRepository(string root, string output)
    {
        Directory.GetDirectories(root, "space *").ShouldBeEmpty(output);
        Directory.EnumerateFileSystemEntries(root, ".git", SearchOption.AllDirectories).ShouldBeEmpty(output);
    }

    // The caller-owned root is inspected after the harness exits and before ScriptHarness removes it.
    private static Task RunOwnedRoot(string name, string[] arguments, TimeSpan budget, Action<ScriptHarnessResult, string> validate)
    {
        string? root = null;
        var options = ScriptHarnessOptions.Default with
        {
            ExecutionBudget = budget,
            AdditionalArguments = arguments,
            OwnerFactory = request =>
            {
                root = request.ResultsDirectory;
                return OperatingSystem.IsWindows() ? new WindowsScriptHarnessProcess(request) :
                    OperatingSystem.IsLinux() ? new LinuxScriptHarnessProcess(request) :
                    throw new PlatformNotSupportedException("ScriptHarness requires Windows or Linux process ownership.");
            },
        };
        return ScriptHarnessProcess.RunAsync("test-change-aware-release.ps1", "C1313", name,
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-change-aware-release.ps1"), options,
            CancellationToken.None, result => validate(result, root!));
    }

    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-change-aware-release.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            ScriptHarnessOptions.Default with
            {
                AdditionalArguments = FilterHostArguments,
            }, CancellationToken.None);
}
