using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcIncrementalLedgerTests
{
    // Provisional harness budget, CARD-1836; no DB, provider, or native child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-ledger.ps1", "RL", name, 1,
            ["RL " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task MissingDigestIsDirty(CancellationToken token) => Run(nameof(MissingDigestIsDirty), token);
    [Test, Timeout(120_000)] public Task UnexpectedSkipNeverCarries(CancellationToken token) => Run(nameof(UnexpectedSkipNeverCarries), token);
    [Test, Timeout(120_000)] public Task CrossPlatformNeverCarries(CancellationToken token) => Run(nameof(CrossPlatformNeverCarries), token);
    [Test, Timeout(120_000)] public Task SameDigestCarriesProvenance(CancellationToken token) => Run(nameof(SameDigestCarriesProvenance), token);
    [Test, Timeout(120_000)] public Task DuplicateOutcomeNeverCarries(CancellationToken token) => Run(nameof(DuplicateOutcomeNeverCarries), token);
    [Test, Timeout(120_000)] public Task NestedDuplicateIsInvalid(CancellationToken token) => Run(nameof(NestedDuplicateIsInvalid), token);
    [Test, Timeout(120_000)] public Task CaseDuplicateIsInvalid(CancellationToken token) => Run(nameof(CaseDuplicateIsInvalid), token);
    [Test, Timeout(120_000)] public Task UnknownPropertyIsInvalid(CancellationToken token) => Run(nameof(UnknownPropertyIsInvalid), token);
    [Test, Timeout(120_000)] public Task WrongTypeIsInvalid(CancellationToken token) => Run(nameof(WrongTypeIsInvalid), token);
    [Test, Timeout(120_000)] public Task MissingFieldIsInvalid(CancellationToken token) => Run(nameof(MissingFieldIsInvalid), token);
    [Test, Timeout(120_000)] public Task BomAndCrlfStillRead(CancellationToken token) => Run(nameof(BomAndCrlfStillRead), token);
    [Test, Timeout(120_000)] public Task LargeLedgerStaysLinear(CancellationToken token) => Run(nameof(LargeLedgerStaysLinear), token);
    [Test, Timeout(120_000)] public Task WellFormedRowStillCarries(CancellationToken token) => Run(nameof(WellFormedRowStillCarries), token);
}
