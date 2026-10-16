using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionDependencyTests
{
    [Test]
    public Task C1313_ProviderAndShared() => Run("C1313_ProviderAndShared", 3, "provider-exact", "shared-all-providers", "full-dominates");
    [Test]
    public Task C1313_RuntimeEdges() => Run("C1313_RuntimeEdges", 4, "linked-consumer", "staged-consumer", "helper-consumer", "bundle-consumer");
    [Test]
    public Task C1313_InvalidGraph() => Run("C1313_InvalidGraph", 5, "invalid-graph", "invalid-rule", "cycle-refused", "regex-timeout-refused", "data-not-code");
    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray());
}
