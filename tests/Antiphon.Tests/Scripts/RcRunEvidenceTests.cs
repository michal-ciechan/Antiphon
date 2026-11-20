using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcRunEvidenceTests
{
    // CARD-1836 S6b. Fixture git and output files only. No qualification process.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-run-evidence.ps1", "RE", name, 1,
            ["RE " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task BuildSourceAndLaunchOutputMustJoin(CancellationToken token) => Run(nameof(BuildSourceAndLaunchOutputMustJoin), token);
    [Test, Timeout(60_000)] public Task ChangedInputsDuringRunRefuse(CancellationToken token) => Run(nameof(ChangedInputsDuringRunRefuse), token);
    [Test, Timeout(60_000)] public Task InterruptedWriterLeavesNotRun(CancellationToken token) => Run(nameof(InterruptedWriterLeavesNotRun), token);
    [Test, Timeout(60_000)] public Task OneBuildAndDiscoveryPerSuite(CancellationToken token) => Run(nameof(OneBuildAndDiscoveryPerSuite), token);

    // CARD-1836 S6b follow-up. The real nightly-run path, twice, on one log root.
    [Test, Timeout(300_000)]
    public Task OneEvidenceRootPerRun(CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-run-evidence.ps1", "RE", nameof(OneEvidenceRootPerRun), 1,
            ["RE OneEvidenceRootPerRun.contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(20)), token);
}
