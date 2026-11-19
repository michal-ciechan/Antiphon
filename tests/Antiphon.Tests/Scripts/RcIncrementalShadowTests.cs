using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcIncrementalShadowTests
{
    // CARD-1836 S8b provisional process-only budget; collect timings in CP-11.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-shadow-sample.ps1", "RS", name, 1,
            ["RS " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task SampleSizeStrataAndReplay(CancellationToken token) => Run(nameof(SampleSizeStrataAndReplay), token);
    [Test, Timeout(60_000)] public Task MismatchInvalidatesCarries(CancellationToken token) => Run(nameof(MismatchInvalidatesCarries), token);
    [Test, Timeout(60_000)] public Task NoCarryAndOnePilotDoNotGrantTrust(CancellationToken token) => Run(nameof(NoCarryAndOnePilotDoNotGrantTrust), token);
}
