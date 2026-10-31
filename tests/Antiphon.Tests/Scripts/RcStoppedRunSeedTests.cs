using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcStoppedRunSeedTests
{
    // Provisional bounded portable fixture cost, CARD-1514; no DB/provider/native child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-stopped-seed.ps1", "SS", name, 1,
            ["SS " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task BuilderReadOnly(CancellationToken token) => Run(nameof(BuilderReadOnly), token);
    [Test, Timeout(120_000)] public Task FailedFirstMapping(CancellationToken token) => Run(nameof(FailedFirstMapping), token);
    [Test, Timeout(120_000)] public Task AcknowledgementRequired(CancellationToken token) => Run(nameof(AcknowledgementRequired), token);
    [Test, Timeout(120_000)] public Task HashMustVerify(CancellationToken token) => Run(nameof(HashMustVerify), token);
    [Test, Timeout(120_000)] public Task TimeoutIsFailure(CancellationToken token) => Run(nameof(TimeoutIsFailure), token);
    [Test, Timeout(120_000)] public Task ScheduleConservesAllChunks(CancellationToken token) => Run(nameof(ScheduleConservesAllChunks), token);
    [Test, Timeout(120_000)] public Task PublicHopsForwardAcknowledgement(CancellationToken token) => Run(nameof(PublicHopsForwardAcknowledgement), token);
    [Test, Timeout(120_000)] public Task InvalidSeedFallsBack(CancellationToken token) => Run(nameof(InvalidSeedFallsBack), token);
}
