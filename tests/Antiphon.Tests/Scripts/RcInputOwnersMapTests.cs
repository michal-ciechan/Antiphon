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

    // UnknownPathDirtiesAll required unmatched-path-fallback to select every suite.
    // An unknown path now selects nothing and is flagged.
    [Test, Timeout(60_000)] public Task UnknownPathSelectsNothing(CancellationToken token) => Run(nameof(UnknownPathSelectsNothing), token);
    [Test, Timeout(60_000)] public Task NewCsprojNobodyReferencesIsFlagged(CancellationToken token) => Run(nameof(NewCsprojNobodyReferencesIsFlagged), token);
    [Test, Timeout(60_000)] public Task NoOpPathSelectsNothingAndIsSilent(CancellationToken token) => Run(nameof(NoOpPathSelectsNothingAndIsSilent), token);
    [Test, Timeout(60_000)] public Task DirectoryWalkReadersSelectSuites(CancellationToken token) => Run(nameof(DirectoryWalkReadersSelectSuites), token);
    [Test, Timeout(60_000)] public Task RepoWalkingProjectSelectedForAnyPath(CancellationToken token) => Run(nameof(RepoWalkingProjectSelectedForAnyPath), token);
    [Test, Timeout(60_000)] public Task UnownedPathBlocksCarryCredit(CancellationToken token) => Run(nameof(UnownedPathBlocksCarryCredit), token);
    [Test, Timeout(60_000)] public Task NamedDocSelectsReadingSuite(CancellationToken token) => Run(nameof(NamedDocSelectsReadingSuite), token);
    [Test, Timeout(60_000)] public Task ServerChangeDirtiesAntiphonAndE2E(CancellationToken token) => Run(nameof(ServerChangeDirtiesAntiphonAndE2E), token);
    [Test, Timeout(60_000)] public Task DocsChangeDirtiesRepoReaders(CancellationToken token) => Run(nameof(DocsChangeDirtiesRepoReaders), token);
    [Test, Timeout(60_000)] public Task BuildGraphChangeForcesFull(CancellationToken token) => Run(nameof(BuildGraphChangeForcesFull), token);
    [Test, Timeout(60_000)] public Task LinkedSourceSelectsIncludingProject(CancellationToken token) => Run(nameof(LinkedSourceSelectsIncludingProject), token);
    [Test, Timeout(60_000)] public Task UnrelatedFileSelectsNothing(CancellationToken token) => Run(nameof(UnrelatedFileSelectsNothing), token);
    [Test, Timeout(60_000)] public Task RuntimeReaderCensus(CancellationToken token) => Run(nameof(RuntimeReaderCensus), token);
    [Test, Timeout(60_000)] public Task ScriptChangeSelectsSessionRunner(CancellationToken token) => Run(nameof(ScriptChangeSelectsSessionRunner), token);
    [Test, Timeout(60_000)] public Task DockerAndComposeSelectRuntimeSuites(CancellationToken token) => Run(nameof(DockerAndComposeSelectRuntimeSuites), token);
    [Test, Timeout(60_000)] public Task ExplicitForcedRootsNotFallback(CancellationToken token) => Run(nameof(ExplicitForcedRootsNotFallback), token);
    [Test, Timeout(60_000)] public Task NugetConfigIgnoreCaseForcesFull(CancellationToken token) => Run(nameof(NugetConfigIgnoreCaseForcesFull), token);
    [Test, Timeout(60_000)] public Task ReaderRootsAreEverySuite(CancellationToken token) => Run(nameof(ReaderRootsAreEverySuite), token);
    [Test, Timeout(60_000)] public Task TestOnlyMethodEditSelectsNothing(CancellationToken token) => Run(nameof(TestOnlyMethodEditSelectsNothing), token);
    [Test, Timeout(60_000)] public Task AddedTestClassSelectsWalkers(CancellationToken token) => Run(nameof(AddedTestClassSelectsWalkers), token);
    [Test, Timeout(60_000)] public Task ProductEdgeStillReachesSuite(CancellationToken token) => Run(nameof(ProductEdgeStillReachesSuite), token);
    [Test, Timeout(60_000)] public Task NoOpOwnerConflictFails(CancellationToken token) => Run(nameof(NoOpOwnerConflictFails), token);
    [Test, Timeout(60_000)] public Task UnownedListAboveFortyIsVisible(CancellationToken token) => Run(nameof(UnownedListAboveFortyIsVisible), token);
    [Test, Timeout(60_000)] public Task ExistingCsprojIsNotANewCsproj(CancellationToken token) => Run(nameof(ExistingCsprojIsNotANewCsproj), token);
    [Test, Timeout(60_000)] public Task HelperProjectBodySelectsReferencers(CancellationToken token) => Run(nameof(HelperProjectBodySelectsReferencers), token);
    [Test, Timeout(90_000)] public Task E2EAddedTestSelectsSuites(CancellationToken token) => Run(nameof(E2EAddedTestSelectsSuites), token);
    // A net-zero rename, a moved test and an added [Arguments] row must select. The [Test] count does not.
    [Test, Timeout(90_000)] public Task E2EIdentityChangeSelectsSuites(CancellationToken token) => Run(nameof(E2EIdentityChangeSelectsSuites), token);
    // A renamed literal owner path must fail closed. Absence of the census is that miss.
    [Test, Timeout(60_000)] public Task OwnerLiteralPathsFailClosed(CancellationToken token) => Run(nameof(OwnerLiteralPathsFailClosed), token);
    // The committed owner map is checked against this tree. Renaming the canary must fail that check.
    [Test, Timeout(90_000)] public Task RealOwnerMapLiteralPathsExist(CancellationToken token) => Run(nameof(RealOwnerMapLiteralPathsExist), token);
    [Test, Timeout(60_000)] public Task CanaryEditSelectsPtySuite(CancellationToken token) => Run(nameof(CanaryEditSelectsPtySuite), token);
    [Test, Timeout(120_000)] public Task EnabledFalseMatchesBaseSelection(CancellationToken token) => Run(nameof(EnabledFalseMatchesBaseSelection), token);
}
