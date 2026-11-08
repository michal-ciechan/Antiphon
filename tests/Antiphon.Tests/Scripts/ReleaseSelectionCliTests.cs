using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// scripts/lib/Antiphon.Scripts select-release (the compiled selection command) against the PowerShell
// selection worker: from the same state each attempt leaves byte-identical selection files and returns the
// same value or refusal, and every baseline or refusal case is left to the worker.
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
}
