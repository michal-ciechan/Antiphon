using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1389. Each method runs one Test-C1389_* case of scripts/test-nightly-run.ps1.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyReclaimRaceTests
{
    [Test]
    public Task C1389_LoserReclaimRecord() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1389",
            nameof(C1389_LoserReclaimRecord), 2,
            [
                "C1389 Record loser does not own the lock",
                "C1389 Record winner newRunId remains"
            ]);

    [Test]
    public Task C1389_DoubleReclaim() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1389",
            nameof(C1389_DoubleReclaim), 2,
            [
                "C1389 Race one attempt owns the lock",
                "C1389 Race lock run id is that owner"
            ]);

    [Test]
    public Task C1389_BootSourcePinned() =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1389",
            nameof(C1389_BootSourcePinned), 3,
            [
                "C1389 Boot registry failure keeps a single source",
                "C1389 Boot live owner stays live-owner",
                "C1389 Boot live owner writes no reclaim record"
            ]);
}
