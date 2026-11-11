using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class QualificationEtaTests
{
    // Provisional harness budget, CARD-1842; no DB, provider, or native child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-qualification-eta.ps1", "QE", name, 1,
            ["QE " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task OutstandingSumsPlannedSeconds(CancellationToken token) => Run(nameof(OutstandingSumsPlannedSeconds), token);
    [Test, Timeout(120_000)] public Task RunningSubtractsElapsed(CancellationToken token) => Run(nameof(RunningSubtractsElapsed), token);
    [Test, Timeout(120_000)] public Task CompletedDoesNotAdd(CancellationToken token) => Run(nameof(CompletedDoesNotAdd), token);
    [Test, Timeout(120_000)] public Task FewWallsLeaveP95Unavailable(CancellationToken token) => Run(nameof(FewWallsLeaveP95Unavailable), token);
    [Test, Timeout(120_000)] public Task EightWallsSetP50AndP95(CancellationToken token) => Run(nameof(EightWallsSetP50AndP95), token);
    [Test, Timeout(120_000)] public Task RefusesTimingRootWrites(CancellationToken token) => Run(nameof(RefusesTimingRootWrites), token);
}
