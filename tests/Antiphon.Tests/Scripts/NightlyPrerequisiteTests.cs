using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1350. The prerequisite cases run from the harness default loop and from these wrappers.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyPrerequisiteTests
{
    [Test]
    public Task C487_PrerequisiteGate() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C487",
            nameof(C487_PrerequisiteGate), 1,
            ["C487 prerequisite npm build stops the pipeline and summary testsPassed is false"]);

    [Test]
    public Task C487_PrerequisiteSettle() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C487",
            nameof(C487_PrerequisiteSettle), 1,
            ["C487 prerequisite settle summary testsPassed matches last-run"]);
}
