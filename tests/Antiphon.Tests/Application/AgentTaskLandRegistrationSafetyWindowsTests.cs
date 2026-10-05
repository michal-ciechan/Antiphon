using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRegistrationSafetyWindowsTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C975_SpacedBacklinkCorruptionBeforeMoveRefuses(bool adoption) =>
        AssertSpacedBoundaryAsync(adoption, afterMove: false);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C975_SpacedBacklinkCorruptionAfterMoveRefuses(bool adoption) =>
        AssertSpacedBoundaryAsync(adoption, afterMove: true);

    private static Task AssertSpacedBoundaryAsync(bool adoption, bool afterMove)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("CP-5 requires native Windows Git and path semantics");
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N") + " with spaces");
        // The shared scenario uses Git-written .git/admin paths and asserts the boundary,
        // exact CAS/reset command counts, captured source/observer bytes and remote refs,
        // and durable recovery intent/pins before restoring the backlink at disposal.
        return AgentTaskLandRegistrationSafetyTests.AssertBoundaryCorruptionAsync(adoption, afterMove, root);
    }
}
