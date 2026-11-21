using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcInputManifestTests
{
    // CARD-1836 provisional: tiny local Git fixture, no DB or qualification process.
    private static Task Run(string name, CancellationToken token, IReadOnlyList<string>? arguments = null) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-input-manifest.ps1", "RIM", name, 1,
            ["RIM " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10), AdditionalArguments: arguments), token);

    [Test, Timeout(60_000)] public Task ProductAndHelperFanout(CancellationToken token) => Run(nameof(ProductAndHelperFanout), token);
    [Test, Timeout(60_000)] public Task DynamicAndUnownedAreDirty(CancellationToken token) => Run(nameof(DynamicAndUnownedAreDirty), token);
    [Test, Timeout(60_000)]
    [Arguments("ConfigAndFixtures")]
    [Arguments("ScriptAndBuildInputs")]
    [Arguments("ClientContainerAndDocs")]
    public Task ConfigFixtureScriptAndLockInputs(string family, CancellationToken token) =>
        Run(nameof(ConfigFixtureScriptAndLockInputs), token, ["-FixtureFamily", family]);

    [Test, Timeout(60_000)] public Task PartialIndexNeverSubtracts(CancellationToken token) => Run(nameof(PartialIndexNeverSubtracts), token);
}
