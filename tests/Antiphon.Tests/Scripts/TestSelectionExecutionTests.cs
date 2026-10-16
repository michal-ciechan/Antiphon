using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionExecutionTests
{
    [Test]
    public Task C1313_ExactExpandedExecution() => Run("C1313_ExactExpandedExecution", 2,
        "exact-uid-union", "forbidden-not-launched");

    [Test]
    public Task C1313_CoverageRefusal() => Run("C1313_CoverageRefusal", 6,
        "coverage-control", "missing-uid", "extra-uid", "duplicate-uid", "required-skip", "zero-required");

    [Test]
    public Task C1313_NewUidRemainder() => Run("C1313_NewUidRemainder", 1, "new-uid-required");

    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            ScriptHarnessOptions.Default with
            {
                AdditionalArguments = ["-FilterHost", Path.Combine(AppContext.BaseDirectory,
                    "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")],
            }, CancellationToken.None);
}
