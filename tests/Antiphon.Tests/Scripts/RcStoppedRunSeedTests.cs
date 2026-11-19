using System.Reflection;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcStoppedRunSeedTests
{
    // Provisional bounded portable fixture cost, CARD-1514; no DB/provider/native child.
    // Every case runs in one pwsh process: test-nightly-stopped-seed.ps1 given the comma-separated case list runs each
    // case as its own invocation of the script, with its own results directory and a fresh script scope. One process
    // start and one library parse serve all of them (a process per case spent about 1 s each before its case began).
    // Each test validates its own case's block of that run.
    private static readonly string[] Cases = typeof(RcStoppedRunSeedTests)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => method.GetCustomAttribute<TestAttribute>() is not null)
        .Select(method => method.Name)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static readonly Lazy<Task<ScriptHarnessResult>> Batch = new(() => ScriptHarnessProcess.RunAsync(
        "test-nightly-stopped-seed.ps1", "SS", string.Join(",", Cases),
        Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-stopped-seed.ps1"),
        new ScriptHarnessOptions(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(20)), CancellationToken.None));

    private static async Task Run(string name, CancellationToken token)
    {
        var result = await Batch.Value.WaitAsync(token);
        ScriptHarness.ValidateBatchCase(result, "SS", name, 1, ["SS " + name + ".contract"]);
    }

    [Test, Timeout(300_000)] public Task BuilderReadOnly(CancellationToken token) => Run(nameof(BuilderReadOnly), token);
    [Test, Timeout(300_000)] public Task FailedFirstMapping(CancellationToken token) => Run(nameof(FailedFirstMapping), token);
    [Test, Timeout(300_000)] public Task AcknowledgementRequired(CancellationToken token) => Run(nameof(AcknowledgementRequired), token);
    [Test, Timeout(300_000)] public Task HashMustVerify(CancellationToken token) => Run(nameof(HashMustVerify), token);
    [Test, Timeout(300_000)] public Task TimeoutIsFailure(CancellationToken token) => Run(nameof(TimeoutIsFailure), token);
    [Test, Timeout(300_000)] public Task ScheduleConservesAllChunks(CancellationToken token) => Run(nameof(ScheduleConservesAllChunks), token);
    [Test, Timeout(300_000)] public Task PublicHopsForwardAcknowledgement(CancellationToken token) => Run(nameof(PublicHopsForwardAcknowledgement), token);
    [Test, Timeout(300_000)] public Task InvalidSeedFallsBack(CancellationToken token) => Run(nameof(InvalidSeedFallsBack), token);
    [Test, Timeout(300_000)] public Task SiblingTimingFromSummary(CancellationToken token) => Run(nameof(SiblingTimingFromSummary), token);
    [Test, Timeout(300_000)] public Task StoppedTimingRootAndRefusals(CancellationToken token) => Run(nameof(StoppedTimingRootAndRefusals), token);
    [Test, Timeout(300_000)] public Task ChunkUnfinishedTolerance(CancellationToken token) => Run(nameof(ChunkUnfinishedTolerance), token);
    [Test, Timeout(300_000)] public Task LinkCacheRefusesReplacement(CancellationToken token) => Run(nameof(LinkCacheRefusesReplacement), token);
    [Test, Timeout(300_000)] public Task DigestGuardLeavesSeedOrder(CancellationToken token) => Run(nameof(DigestGuardLeavesSeedOrder), token);
    [Test, Timeout(300_000)] public Task RealPackerRegroupKeepsFailedFirst(CancellationToken token) => Run(nameof(RealPackerRegroupKeepsFailedFirst), token);
    [Test, Timeout(300_000)] public Task FailedMethodBands(CancellationToken token) => Run(nameof(FailedMethodBands), token);
    [Test, Timeout(300_000)] public Task RealPackerFailedMethodFollows(CancellationToken token) => Run(nameof(RealPackerFailedMethodFollows), token);
    [Test, Timeout(300_000)] public Task RealShapeFailedMethodBand(CancellationToken token) => Run(nameof(RealShapeFailedMethodBand), token);
    [Test, Timeout(300_000)] public Task SummaryPrefixTamper(CancellationToken token) => Run(nameof(SummaryPrefixTamper), token);
    [Test, Timeout(300_000)] public Task UnfinishedRunLaneResume(CancellationToken token) => Run(nameof(UnfinishedRunLaneResume), token);
    [Test, Timeout(300_000)] public Task SkipRowsCountAsCompleted(CancellationToken token) => Run(nameof(SkipRowsCountAsCompleted), token);
    [Test, Timeout(300_000)] public Task WindowsAncestorSwapRefused(CancellationToken token) => Run(nameof(WindowsAncestorSwapRefused), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeUnion(CancellationToken token) => Run(nameof(CumulativeMergeUnion), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeProvenDrop(CancellationToken token) => Run(nameof(CumulativeMergeProvenDrop), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeUnfixedGreen(CancellationToken token) => Run(nameof(CumulativeMergeUnfixedGreen), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeCherryPick(CancellationToken token) => Run(nameof(CumulativeMergeCherryPick), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeExplicitProven(CancellationToken token) => Run(nameof(CumulativeMergeExplicitProven), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeDigestStable(CancellationToken token) => Run(nameof(CumulativeMergeDigestStable), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeLaunchDefaults(CancellationToken token) => Run(nameof(CumulativeMergeLaunchDefaults), token);
    [Test, Timeout(300_000)] public Task CumulativeMergeFixPresentAtFailure(CancellationToken token) => Run(nameof(CumulativeMergeFixPresentAtFailure), token);
    [Test, Timeout(300_000)] public Task CumulativeMergePrintsDigest(CancellationToken token) => Run(nameof(CumulativeMergePrintsDigest), token);

    // Provisional: one measured export of a 3,300 + 1,700 file layout (see the harness detail).
    [Test, Timeout(300_000)] public Task ScaleExportBound(CancellationToken token) => Run(nameof(ScaleExportBound), token);
}
