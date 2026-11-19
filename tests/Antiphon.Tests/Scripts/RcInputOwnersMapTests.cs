using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcInputOwnersMapTests
{
    // CARD-1820 S3: tiny local Git fixture, no DB or qualification process.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-input-owners.ps1", "RIO", name, 1,
            ["RIO " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task UnknownPathDirtiesAll(CancellationToken token) => Run(nameof(UnknownPathDirtiesAll), token);
    [Test, Timeout(60_000)] public Task ServerChangeDirtiesAntiphonAndE2E(CancellationToken token) => Run(nameof(ServerChangeDirtiesAntiphonAndE2E), token);
    [Test, Timeout(60_000)] public Task DocsChangeDirtiesRepoReaders(CancellationToken token) => Run(nameof(DocsChangeDirtiesRepoReaders), token);
    [Test, Timeout(60_000)] public Task BuildGraphChangeForcesFull(CancellationToken token) => Run(nameof(BuildGraphChangeForcesFull), token);
    [Test, Timeout(60_000)] public Task LinkedSourceSelectsIncludingProject(CancellationToken token) => Run(nameof(LinkedSourceSelectsIncludingProject), token);
    [Test, Timeout(60_000)] public Task UnrelatedFileSelectsNothing(CancellationToken token) => Run(nameof(UnrelatedFileSelectsNothing), token);
}
