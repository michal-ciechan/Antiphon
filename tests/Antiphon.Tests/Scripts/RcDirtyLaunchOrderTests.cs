using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcDirtyLaunchOrderTests
{
    private static Task Run(string name, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-dirty-order.ps1", "DO", name, 1,
            ["DO " + name + ".contract"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10)), token);

    [Test, Timeout(60_000)] public Task OnlyDirtyAndSampleLaunch(CancellationToken token) => Run(nameof(OnlyDirtyAndSampleLaunch), token);
    [Test, Timeout(60_000)] public Task OpenFailurePrecedesChanged(CancellationToken token) => Run(nameof(OpenFailurePrecedesChanged), token);
    [Test, Timeout(60_000)] public Task SameTipPassCannotClearFailure(CancellationToken token) => Run(nameof(SameTipPassCannotClearFailure), token);
}
