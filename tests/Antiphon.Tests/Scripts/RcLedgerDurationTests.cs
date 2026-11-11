using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcLedgerDurationTests
{
    // Provisional harness budget, CARD-1842; no DB, provider, or native child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-ledger-duration.ps1", "RD", name, 1,
            ["RD " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task V1DurationFromSidecarAndTrx(CancellationToken token) => Run(nameof(V1DurationFromSidecarAndTrx), token);
    [Test, Timeout(120_000)] public Task V1RejectsHostBeforeWrite(CancellationToken token) => Run(nameof(V1RejectsHostBeforeWrite), token);
    [Test, Timeout(120_000)] public Task V2AcceptsExplicitHost(CancellationToken token) => Run(nameof(V2AcceptsExplicitHost), token);
    [Test, Timeout(120_000)] public Task V2OmitsHostWhenSwitchOff(CancellationToken token) => Run(nameof(V2OmitsHostWhenSwitchOff), token);
}
