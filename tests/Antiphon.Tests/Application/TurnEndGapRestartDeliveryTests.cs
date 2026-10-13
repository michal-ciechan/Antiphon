using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), Category("Slow")]
[NotInParallel("MessageQueue"), ParallelLimiter<ProcessSpawnLimit>]
public sealed class TurnEndGapRestartDeliveryTests
{
    // T37 / V-20 / R-5 -> PC-84 suppresses Sync's recovered-boundary wakeup.
    // Red oracle: five complete recipient prompts and Confirmed notifications,
    // after the finite captured-gap / keepalive / queue-timer schedule.
    [Test, Arguments(true), Arguments(false)]
    public async Task C1205_Restart_delivers_five_gap_held_completions_once(bool cache)
    {
        await using var h = await TurnEndGapDeliveryHarness.CreateAsync(cache);
        await h.ProduceFiveAsync();
        await h.AssertHeldAsync();
        var notes = await h.NotesAsync();
        var rows = await h.RowsAsync();
        await h.RestartAsync();
        await h.AssertHeldAsync();
        await h.StartGapAsync();
        h.ReleaseGapWithEnd();
        await h.JoinDeliveryAsync(h.PumpDrained);
        await h.AssertReceiptsAsync(notes, rows);
        await h.RestartAsync();
        await h.AssertReceiptsAsync(notes, rows);
    }
}
