using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionDependencyTests
{
    [Test]
    public Task C1313_ProviderAndShared() =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313",
            "C1313_ProviderAndShared", 1, "C1313 provider-exact");
}
