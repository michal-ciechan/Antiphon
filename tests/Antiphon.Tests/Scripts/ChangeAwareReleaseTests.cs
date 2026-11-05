using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChangeAwareReleaseTests
{
    private readonly ScriptHarnessOptions _options;

    public ChangeAwareReleaseTests() : this(ScriptHarnessOptions.Default) { }

    internal ChangeAwareReleaseTests(ScriptHarnessOptions options) => _options = options;

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

    // CARD-1691 C8: a case that fails before New-Fx reaches the cleanup with no fixture root;
    // the caller's results directory and its contents must survive and the original error stays.
    [Test]
    public Task C1590_UnknownCaseKeepsResultsDirectory() => RunOwnedRoot("Bogus", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldNotBe(0, output);
        output.ShouldContain("Test-Bogus", Case.Sensitive, output);
        Directory.Exists(root).ShouldBeTrue(output);
        File.ReadAllText(Path.Combine(root, SentinelName)).ShouldBe(SentinelName, output);
    }, WriteSentinel);

    // CARD-1691 C4/C12: the first of two fixture roots is gone before the finally runs. The
    // passed run still exits 0, the failed run still reports its own error, and both remove the
    // second root.
    [Test]
    public Task C1590_PassedRunToleratesGoneFixtureRoot() => RunOwnedRoot("C1590_GoneRootPassed", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
        output.ShouldContain("PASS C1313 gone-root-fixture", Case.Sensitive, output);
        AssertNoFixtureRepository(root, output);
    });

    [Test]
    public Task C1590_FailedRunToleratesGoneFixtureRoot() => RunOwnedRoot("C1590_GoneRootFailed", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldNotBe(0, output);
        output.ShouldContain("c1590-original-failure", Case.Sensitive, output);
        AssertNoFixtureRepository(root, output);
    });

    private static readonly string[] FilterHostArguments =
        ["-FilterHost", Path.Combine(AppContext.BaseDirectory, "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")];

    private const string SentinelName = "c1590-caller-sentinel.txt";

    // The caller owns the results directory before the harness starts, so a pre-existing file must survive.
    private static void WriteSentinel(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SentinelName), SentinelName);
    }

    private static void AssertNoFixtureRepository(string root, string output)
    {
        Directory.GetDirectories(root, "space *").ShouldBeEmpty(output);
        Directory.EnumerateFileSystemEntries(root, ".git", SearchOption.AllDirectories).ShouldBeEmpty(output);
    }

    // The caller-owned root is inspected after the harness exits and before ScriptHarness removes it.
    private static Task RunOwnedRoot(string name, string[] arguments, TimeSpan budget, Action<ScriptHarnessResult, string> validate,
        Action<string>? prepare = null)
    {
        string? root = null;
        var options = ScriptHarnessOptions.Default with
        {
            ExecutionBudget = budget,
            AdditionalArguments = arguments,
            OwnerFactory = request =>
            {
                root = request.ResultsDirectory;
                prepare?.Invoke(root);
                return OperatingSystem.IsWindows() ? new WindowsScriptHarnessProcess(request) :
                    OperatingSystem.IsLinux() ? new LinuxScriptHarnessProcess(request) :
                    throw new PlatformNotSupportedException("ScriptHarness requires Windows or Linux process ownership.");
            },
        };
        return ScriptHarnessProcess.RunAsync("test-change-aware-release.ps1", "C1313", name,
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-change-aware-release.ps1"), options,
            CancellationToken.None, result => validate(result, root!));
    }

    private Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-change-aware-release.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            _options with
            {
                AdditionalArguments = FilterHostArguments,
            }, CancellationToken.None);
}
