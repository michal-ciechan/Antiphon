using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcChunkInputDigestTests
{
    // Provisional CARD-1836 budget: local Git and PowerShell only, no services.
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-chunk-digest.ps1", "RCD", name, 1,
            ["RCD " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task TransitiveEditAndDeletionChangeDigest(CancellationToken token) => Run(nameof(TransitiveEditAndDeletionChangeDigest), token);
    [Test, Timeout(60_000)] public Task ProvenUnrelatedProjectKeepsDigest(CancellationToken token) => Run(nameof(ProvenUnrelatedProjectKeepsDigest), token);
    [Test, Timeout(60_000)] public Task UnknownAndCaseSetChangesAreDirty(CancellationToken token) => Run(nameof(UnknownAndCaseSetChangesAreDirty), token);
    [Test, Timeout(60_000)] public Task RenameSymlinkAndCaseCollision(CancellationToken token) => Run(nameof(RenameSymlinkAndCaseCollision), token);
    [Test, Timeout(60_000)] public Task CaseKeyedMultipleCasesMatchMembership(CancellationToken token) => Run(nameof(CaseKeyedMultipleCasesMatchMembership), token);
    [Test, Timeout(60_000)] public Task CaseKeyedSingletonsMatchMembership(CancellationToken token) => Run(nameof(CaseKeyedSingletonsMatchMembership), token);
    [Test, Timeout(60_000)] public Task CaseOnlyMembershipChangesDigest(CancellationToken token) => Run(nameof(CaseOnlyMembershipChangesDigest), token);
    [Test, Timeout(60_000)] public Task CanonicalCaseOrderAndEncoding(CancellationToken token) => Run(nameof(CanonicalCaseOrderAndEncoding), token);
    [Test, Timeout(60_000)] public Task LoneSurrogatesFailClosed(CancellationToken token) => Run(nameof(LoneSurrogatesFailClosed), token);
    [Test, Timeout(60_000)] public Task DependencyEdgesAffectDigest(CancellationToken token) => Run(nameof(DependencyEdgesAffectDigest), token);
}
