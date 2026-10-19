using Antiphon.Tests.TestHelpers;
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

    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-change-aware-release.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            ScriptHarnessOptions.Default with
            {
                AdditionalArguments = ["-FilterHost", Path.Combine(AppContext.BaseDirectory,
                    "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")],
            }, CancellationToken.None);
}
