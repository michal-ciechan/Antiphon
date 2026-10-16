using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointOwnedOutputScriptTests
{
    [Test]
    public Task C987_StandaloneNativeSource() =>
        ScriptHarness.RunHarnessCaseAsync("test-checkpoint-owned-output.ps1", "C987", nameof(C987_StandaloneNativeSource), 8,
            new[] { "compile", "create", "collision", "sentinel", "identity", "missing-parent", "permissions", "no-library" }
                .Select(boundary => "C987 C987.C987_StandaloneNativeSource.native." + boundary).ToArray());

    [Test]
    public Task C987_OwnedOutputHelper() =>
        ScriptHarness.RunHarnessCaseAsync("test-checkpoint-owned-output.ps1", "C987", nameof(C987_OwnedOutputHelper), 8,
            new[] { "source-load", "create", "marker", "collision", "marker-unchanged", "sentinel", "identity", "obsolete-source-absent" }
                .Select(boundary => "C987 C987.C987_OwnedOutputHelper.helper." + boundary).ToArray());
}
