using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcLedgerStatusTests
{
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-ledger-status.ps1", "RS", name, 1,
            ["RS " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task PartialDecisionsCannotCloseUniverse(CancellationToken token) => Run(nameof(PartialDecisionsCannotCloseUniverse), token);
    [Test, Timeout(60_000)] public Task ParameterizedMethodsAndNonTunitStayRequired(CancellationToken token) => Run(nameof(ParameterizedMethodsAndNonTunitStayRequired), token);
    [Test, Timeout(60_000)] public Task StatusCountsOriginsWithoutDoubleCharging(CancellationToken token) => Run(nameof(StatusCountsOriginsWithoutDoubleCharging), token);
    [Test, Timeout(60_000)] public Task CrashRevisionCannotPublishClosure(CancellationToken token) => Run(nameof(CrashRevisionCannotPublishClosure), token);
}
