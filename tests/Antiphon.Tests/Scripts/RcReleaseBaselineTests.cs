using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcReleaseBaselineTests
{
    // CARD-1947 / Debug 4a63f1cf: AncestorReleaseThenSameLineFallback timed out at 45 s in Windows rc run 160200cb.
    // After the git memo fix the case measured 24.0-33.2 s natively on the loaded desktop (p95 about 33 s; the
    // script alone 27.4-29.1 s, before the fix 54.9-59.9 s), but two of five runs under live-run CPU load still
    // passed 45 s. Budget about 2x the measured p95: 70 s execution, 10 s joined cleanup, 90 s method deadline.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-baseline.ps1", "BL", name, 1,
            ["BL " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(90_000)] public Task AncestorReleaseThenSameLineFallback(CancellationToken token) => Run(nameof(AncestorReleaseThenSameLineFallback), token);
    [Test, Timeout(90_000)] public Task CrossPlatformMovedAndCorruptRefRefuse(CancellationToken token) => Run(nameof(CrossPlatformMovedAndCorruptRefRefuse), token);
    [Test, Timeout(90_000)] public Task MissingBaselineIsFull(CancellationToken token) => Run(nameof(MissingBaselineIsFull), token);
}
