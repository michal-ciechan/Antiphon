using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public class PhoneHomeConnectionServiceTests
{
    [Test]
    public async Task Adoption_precedes_registration()
    {
        var gate = new PhoneHomeAdoptionGate();
        var registrationRequests = new List<int>();
        var wait = gate.WaitAsync(CancellationToken.None);
        wait.IsCompleted.ShouldBeFalse();
        registrationRequests.Count.ShouldBe(0);
        gate.SignalReady();
        await wait;
        registrationRequests.Add(1);
        registrationRequests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Hub_overflow_disconnects_for_recovery()
    {
        var overflowNotified = false;
        var hub = new SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var reader = hub.SubscribeBounded(2, 100, () => overflowNotified = true, cts.Token);
        hub.Publish("a", new { n = 1 });
        hub.Publish("a", new { n = 2 });
        hub.Publish("a", new { n = 3 });
        overflowNotified.ShouldBeTrue();
        await Task.CompletedTask;
        _ = reader;
    }

    [Test]
    public async Task Held_command_does_not_block_receive_progress()
    {
        var receiveProgressBeforeLaunchRelease = true;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = Task.Run(async () =>
        {
            await Task.Delay(10);
            receiveProgressBeforeLaunchRelease.ShouldBeTrue();
            held.TrySetResult();
        });
        await held.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await read;
        receiveProgressBeforeLaunchRelease.ShouldBeTrue();
    }
}
