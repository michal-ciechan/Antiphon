using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class RunStateStoreTests
{
    [Test]
    public async Task readers_never_see_a_partial_file()
    {
        var path = Path.Combine(CheckpointFixtures.TempDir(), "state.json");
        var store = new RunStateStore();
        var failures = 0;
        var seen = 0;
        var stop = false;
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var state = store.TryRead(path);
                if (state is null)
                    continue;
                if (!int.TryParse(state.RunId, out var nonce) || state.Marker.Length != nonce)
                    Interlocked.Increment(ref failures);
                else
                    Interlocked.Increment(ref seen);
            }
        });
        for (var i = 1; i <= 80; i++)
            store.Write(path, new RunState { RunId = i.ToString(), Marker = new string('x', i), Phase = "running" });
        Volatile.Write(ref stop, true);
        await reader;
        failures.ShouldBe(0);
        seen.ShouldBeGreaterThan(0);
    }
}
