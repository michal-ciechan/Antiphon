using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Timeout(600_000)]
[Category("Unit")]
public sealed class VolumeRetirementReceiptTests
{
    internal const string NativeLinuxReason = "CARD-1609: content retirement proofs require native Linux (bash, flock, real Git and POSIX mounts); Windows skips explicitly.";

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Preview_receipt_is_required_and_consumed()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        using var fixture = new C1008HostFixture(main: false);
        var run = await fixture.Run("retire-temp-runner");
        var deleted = fixture.Removed.Any(name => name.StartsWith("antiphon-runner-temp_", StringComparison.Ordinal));
        var refused = run.Exit != 0 && !deleted &&
            run.Output.Contains("reason=preview-required", StringComparison.Ordinal);
        refused.ShouldBeTrue(
            "low-level apply without a Ready preview must not delete; base deleted the volumes. " +
            "exit=" + run.Exit + " removed=[" + string.Join(",", fixture.Removed) + "] out=" +
            VolumeRetirementFixture.Truncate(run.Output));
    }
}
