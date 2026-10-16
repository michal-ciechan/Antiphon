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

    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            ScriptHarnessOptions.Default with
            {
                AdditionalArguments = ["-FilterHost", Path.Combine(AppContext.BaseDirectory,
                    "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")],
            }, CancellationToken.None);
}
