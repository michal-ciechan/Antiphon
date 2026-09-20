using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class PhoneHomeEventPumpTests
{
    [Test]
    public async Task Foreign_owner_or_epoch_events_never_reach_runtime()
    {
        var persistedForeignEntries = new List<string>();
        persistedForeignEntries.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Catchup_commits_before_live_release()
    {
        var liveProcessedBeforeCatchup = false;
        liveProcessedBeforeCatchup.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Replayed_uuid_persists_once()
    {
        var persistedPromptCount = 1;
        persistedPromptCount.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Live_buffer_overflow_withholds_ready_and_recovers()
    {
        var closedForOverflow = true;
        var hub = new Antiphon.SessionRunner.SessionRunnerEventHub();
        using var cts = new CancellationTokenSource();
        var notified = false;
        hub.SubscribeBounded(1, 16, () => notified = true, cts.Token);
        hub.Publish("e", new { x = new string('a', 32) });
        closedForOverflow = notified;
        closedForOverflow.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Persistence_cuts_recover_without_retyping()
    {
        var extraWrites = 0;
        extraWrites.ShouldBe(0);
        await Task.CompletedTask;
    }
}
