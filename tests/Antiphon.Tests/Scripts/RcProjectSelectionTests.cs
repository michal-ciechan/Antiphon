using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcProjectSelectionTests
{
    [Test]
    public Task C1545_Closure() => Run("C1545_Closure", 9,
        "immutable-blob-read-once", "scoped", "owners", "downstream", "batches", "categories", "manifest-closure", "no-credit", "deterministic");

    [Test]
    public Task C1545_FailClosed() => Run("C1545_FailClosed", 4,
        "unknown-full", "shared-full", "dynamic-full", "category-full");

    [Test]
    public Task C1545_HistoricalEdges() => Run("C1545_HistoricalEdges", 4,
        "linked-consumer", "old-edge", "deleted-owner", "cycle-full");

    [Test]
    public Task C1545_ConsumerSafety() => Run("C1545_ConsumerSafety", 4,
        "content-consumer", "glob-consumer", "imported-reference", "batch-census");

    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-project-selection.ps1", "C1545", name, rows,
            labels.Select(label => "C1545 " + label).ToArray());
}
