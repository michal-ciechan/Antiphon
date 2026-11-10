using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// scripts/lib/Antiphon.Scripts select-release (the compiled selection command) against the PowerShell
// selection worker: from the same state each attempt leaves byte-identical selection files and returns the
// same value or refusal. The command answers every admitted attempt, with or without a baseline (a baseline
// commit, a receipt, an archive choice), and leaves every refusal to the worker.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseSelectionCliTests
{
    [Test]
    public Task CliMatchesWorker(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-selection-cli.ps1", "RS", nameof(CliMatchesWorker), 16,
            ["RS CliMatchesWorker.cliBuilt", "RS CliMatchesWorker.full", "RS CliMatchesWorker.changeAware",
             "RS CliMatchesWorker.inputs", "RS CliMatchesWorker.absentInputs", "RS CliMatchesWorker.resume",
             "RS CliMatchesWorker.secondAttempt", "RS CliMatchesWorker.baseline", "RS CliMatchesWorker.baselineArchive",
             "RS CliMatchesWorker.conflict", "RS CliMatchesWorker.notCommit", "RS CliMatchesWorker.stalePolicy",
             "RS CliMatchesWorker.qualificationArray", "RS CliMatchesWorker.trainDirectory",
             "RS CliMatchesWorker.outsideState", "RS CliMatchesWorker.badRunId"],
            ScriptHarnessOptions.Default, cancellationToken);

    // Baseline shapes: project add, remove and rename, glob and import edges, policy and catalog changes, a
    // receipt (compatible or not, missing), archive choices; and the refusals (corrupt receipt, a baseline that is
    // not an ancestor, a stale baseline policy) that stay the worker's.
    [Test]
    public Task BaselineMatchesWorker(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-selection-cli.ps1", "RS", nameof(BaselineMatchesWorker), 17,
            ["RS BaselineMatchesWorker.projectAdd", "RS BaselineMatchesWorker.projectRemove", "RS BaselineMatchesWorker.projectRename",
             "RS BaselineMatchesWorker.globEdges", "RS BaselineMatchesWorker.fullMode", "RS BaselineMatchesWorker.policyChange",
             "RS BaselineMatchesWorker.catalogChange", "RS BaselineMatchesWorker.sameCommit", "RS BaselineMatchesWorker.receipt",
             "RS BaselineMatchesWorker.receiptIncompatible", "RS BaselineMatchesWorker.receiptMissing", "RS BaselineMatchesWorker.archiveChoice",
             "RS BaselineMatchesWorker.archiveFiltered", "RS BaselineMatchesWorker.archiveIncompatible", "RS BaselineMatchesWorker.receiptCorrupt",
             "RS BaselineMatchesWorker.notAncestor", "RS BaselineMatchesWorker.staleBaseline"],
            ScriptHarnessOptions.Default, cancellationToken);
}
