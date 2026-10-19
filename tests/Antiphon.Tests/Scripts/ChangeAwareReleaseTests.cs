using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChangeAwareReleaseTests
{
    [Test]
    public Task C1313_ModeCredit() => Run("C1313_ModeCredit", 1, "unqualified-no-credit");

    [Test]
    public Task C1313_BaselineAuthority() => Run("C1313_BaselineAuthority", 1, "baseline-absent-full");

    [Test]
    public Task C1313_ParameterHops() => Run("C1313_ParameterHops", 1, "hop-baseline-bound");

    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-change-aware-release.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            ScriptHarnessOptions.Default with
            {
                AdditionalArguments = ["-FilterHost", Path.Combine(AppContext.BaseDirectory,
                    "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")],
            }, CancellationToken.None);
}
