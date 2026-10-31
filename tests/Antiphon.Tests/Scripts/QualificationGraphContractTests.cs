using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class QualificationGraphContractTests
{
    [Test]
    [Timeout(210_000)]
    public Task SchemaAndIdentity() => ScriptHarness.RunHarnessCaseAsync(
        "test-qualification-graph.ps1", "QGRAPH", "SchemaAndIdentity", 1,
        ["QGRAPH SchemaAndIdentity/missing-platform"],
        ScriptHarnessOptions.Default with
        {
            ExecutionBudget = TimeSpan.FromSeconds(180),
            CleanupBudget = TimeSpan.FromSeconds(10),
            PostExitDrainBudget = TimeSpan.FromSeconds(10)
        }, CancellationToken.None);
}
