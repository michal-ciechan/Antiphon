using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcForcedFullTests
{
    // CARD-1836 S8a provisional process-only budget; collect timings in CP-4.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-forced-full.ps1", "RF", name, 1,
            ["RF " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task ContractOrUnknownForcesFull(CancellationToken token) => Run(nameof(ContractOrUnknownForcesFull), token);
    [Test, Timeout(60_000)] public Task AnchorAgeCannotRenewThroughCarry(CancellationToken token) => Run(nameof(AnchorAgeCannotRenewThroughCarry), token);
    [Test, Timeout(60_000)] public Task ExecutedFullCanCloseWithReasons(CancellationToken token) => Run(nameof(ExecutedFullCanCloseWithReasons), token);
}
