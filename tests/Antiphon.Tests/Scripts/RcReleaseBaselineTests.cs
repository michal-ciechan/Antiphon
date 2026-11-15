using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcReleaseBaselineTests
{
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-baseline.ps1", "BL", name, 1,
            ["BL " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task AncestorReleaseThenSameLineFallback(CancellationToken token) => Run(nameof(AncestorReleaseThenSameLineFallback), token);
    [Test, Timeout(60_000)] public Task CrossPlatformMovedAndCorruptRefRefuse(CancellationToken token) => Run(nameof(CrossPlatformMovedAndCorruptRefRefuse), token);
    [Test, Timeout(60_000)] public Task MissingBaselineIsFull(CancellationToken token) => Run(nameof(MissingBaselineIsFull), token);
}
