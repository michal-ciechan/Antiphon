using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcQualificationStatusTests
{
    [Test]
    public Task C1500_StatusIdentityAndObservation() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1500", "C1500_StatusIdentityAndObservation", 25,
            "C1500 foreign-state-run", "C1500 changed-record", "C1500 unknown-owner");

    [Test]
    public Task C1500_StatusDoesNotMutate() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1500", "C1500_StatusDoesNotMutate", 3,
            "C1500 input-bytes-and-times-preserved", "C1500 no-action-commands");

    [Test]
    public Task C1500_WindowsSharedReadAndPs51() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1500", "C1500_WindowsSharedReadAndPs51", 4,
            "C1500 powershell-shared-read", "C1500 pwsh-shared-read");
}
