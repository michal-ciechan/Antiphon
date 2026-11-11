using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WatchQualEtaLineTests
{
    // Provisional harness budget, CARD-1842; no DB, provider, or native child.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-watch-qual-eta.ps1", "WQ", name, 1,
            ["WQ " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(120_000)] public Task OncePrintsOneEtaLine(CancellationToken token) => Run(nameof(OncePrintsOneEtaLine), token);
    [Test, Timeout(120_000)] public Task OmittedTimingRootStaysSilent(CancellationToken token) => Run(nameof(OmittedTimingRootStaysSilent), token);
    [Test, Timeout(120_000)] public Task StatusObjectHasNoEta(CancellationToken token) => Run(nameof(StatusObjectHasNoEta), token);
    [Test, Timeout(120_000)] public Task ReleaseGatesKeepsLastActivityRefusal(CancellationToken token) => Run(nameof(ReleaseGatesKeepsLastActivityRefusal), token);
}
