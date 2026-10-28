using System.Collections.Concurrent;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests.TestHelpers;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class PhoneHomeBackpressureTests
{
    private static async Task OwnershipAndFailureAsync(PhoneHomeHostedRunner h)
    {
        var foreign = await h.AddSessionAsync();
        await using (var db = h.Db())
            await db.AgentSessions.Where(s => s.Id == foreign).ExecuteUpdateAsync(s => s.SetProperty(x => x.RunnerId, "foreign-owner"));
        var before = h.Live.ReceivedEvents;
        h.Transcript(foreign, 1, "foreign-rejected", "foreign");
        h.Exit(foreign, 2, h.Generation);
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.ReceivedEvents == before + 2 && h.Live.PendingEvents == 0,
            "foreign events are consumed and released");
        await using (var db = h.Db())
        {
            (await db.TranscriptEntries.AnyAsync(e => e.Uuid == "foreign-rejected")).ShouldBeFalse();
            (await db.AgentSessions.SingleAsync(s => s.Id == foreign)).Status.ShouldBe(SessionStatus.Running);
        }
        var ours = await h.AddSessionAsync();
        before = h.Live.ReceivedEvents;
        h.Exit(ours, 1, h.Generation.AddSeconds(-1));
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.ReceivedEvents == before + 1 && h.Live.PendingEvents == 0, "stale exit observed");
        await using (var db = h.Db()) (await db.AgentSessions.SingleAsync(s => s.Id == ours)).Status.ShouldBe(SessionStatus.Running);
        h.Bus.Recipient.ThrowOnceOnEvent = "AgentTextDelta";
        h.Output(ours, 2, "throw once");
        h.Transcript(ours, 3, "after-observer-failure", "persist despite preceding publication failure");
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.ReceivedEvents == before + 3 && h.Live.PendingEvents == 0,
            "observer failure releases exactly once and the next event drains");
        h.Pump.EventFailures(h.Live).ShouldBe(1);
        await using (var db = h.Db()) (await db.TranscriptEntries.CountAsync(e => e.Uuid == "after-observer-failure")).ShouldBe(1);
        h.Live.PendingEventBytes.ShouldBe(0);
    }

    private static async Task WorkerBoundAsync()
    {
        foreach (var invalid in new[] { 0, 17 })
            PhoneHomeRunnerSettingsRules.Validate(new PhoneHomeRunnerSettings { Enabled = true, EventConsumerConcurrency = invalid })
                .ShouldContain(message => message.Contains("EventConsumerConcurrency"));
        using var socket = new PhoneHomeTestWebSocket();
        await using var live = new PhoneHomeLiveConnection("workers", Guid.NewGuid(), Guid.NewGuid(), 2, socket,
            new PhoneHomeLimits(MaxPendingEvents: 32), TimeProvider.System);
        using var stop = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var peak = 0;
        var perSession = new ConcurrentDictionary<Guid, int>();
        var order = new ConcurrentDictionary<Guid, ConcurrentQueue<long>>();
        var errors = new ConcurrentQueue<string>();
        var scheduler = new PhoneHomeEventScheduler(live, 4, async (_, item, parsed, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            int old;
            do { old = Volatile.Read(ref peak); } while (count > old && Interlocked.CompareExchange(ref peak, count, old) != old);
            if (perSession.AddOrUpdate(parsed.SessionId, 1, (_, n) => n + 1) != 1) errors.Enqueue("overlapping session observers");
            if (count == 4) entered.TrySetResult();
            try
            {
                await release.Task.WaitAsync(ct);
                order.GetOrAdd(parsed.SessionId, _ => new()).Enqueue(parsed.Output!.Sequence);
            }
            finally { perSession.AddOrUpdate(parsed.SessionId, 0, (_, n) => n - 1); Interlocked.Decrement(ref active); }
        }, (_, ex) => errors.Enqueue(ex.GetType().Name));
        var receiving = live.ReceiveLoopAsync(stop.Token);
        var pumping = scheduler.RunAsync(stop.Token);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        try
        {
            foreach (var id in ids) Emit(id, 1);
            Emit(ids[0], 2); Emit(ids[0], 3);
            await entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
            await PhoneHomeHostedRunner.UntilAsync(() => live.Diagnostics.Snapshot().Sessions.Sum(s => s.Events) == 7,
                "all seven frames reached the scheduler while four observers are held");
            live.PendingEvents.ShouldBe(7, "active plus every internal queue retain the global reservation");
            release.TrySetResult();
            await PhoneHomeHostedRunner.UntilAsync(() => live.PendingEvents == 0, "all scheduled observers finish");
            peak.ShouldBe(4);
            errors.ShouldBeEmpty();
            order[ids[0]].ShouldBe(new long[] { 1, 2, 3 });
            order.Values.Sum(q => q.Count).ShouldBe(7);
            for (var i = 0; i < 20; i++) Emit(Guid.NewGuid(), 1);
            await PhoneHomeHostedRunner.UntilAsync(() => live.ReceivedEvents == 27 && live.PendingEvents == 0,
                "more producers than the diagnostic sample capacity drain normally");
            var diagnostics = live.Diagnostics.Snapshot();
            diagnostics.Sessions.Length.ShouldBe(16);
            diagnostics.OtherSessionEvents.ShouldBe(9);
            diagnostics.Kinds.Single().ReceivedEvents.ShouldBe(27);
            diagnostics.Kinds.Single().ReleasedEvents.ShouldBe(27);
        }
        finally
        {
            release.TrySetResult(); stop.Cancel();
            try { await pumping; } catch (OperationCanceledException) { }
            try { await receiving; } catch (OperationCanceledException) { }
        }
        void Emit(Guid id, long sequence) => socket.Enqueue(new(PhoneHomeFrameKind.Event, 2, Guid.Empty,
            EventName: SessionRunnerEventNames.SessionOutput,
            Payload: JsonSerializer.SerializeToElement(new RunnerOutputEvent(id, sequence, "ordered"), PhoneHomeFraming.Json)));
    }

    private static async Task ReplacementWaitsForObserverAsync()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync(observeCompletion: false);
        h.Bus.IgnoreCancellation = true;
        h.Output(h.SessionId, 1, "uncooperative publication");
        await h.Bus.Entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
        var old = h.Live;
        await old.DisposeAsync("test-replacement");
        var reconnect = h.ReconnectAsync();
        try
        {
            await PhoneHomeHostedRunner.UntilAsync(() => h.Host.Directory.SnapshotLive() is { } next && next.Epoch > old.Epoch
                && h.Logs.Entries.Any(e => e.Message.Contains("observer teardown is still running")),
                "supervision observes the outstanding teardown while a replacement socket is accepted");
            h.Host.Directory.SnapshotLive()!.DispatchEligible.ShouldBeFalse();
            old.PendingEvents.ShouldBe(1, "uncooperative observer keeps its reservation until it actually returns");
            h.Bus.Release.TrySetResult();
            await reconnect.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
            h.Live.DispatchEligible.ShouldBeTrue();
            old.PendingEvents.ShouldBe(0);
        }
        finally { h.Bus.Release.TrySetResult(); await reconnect; }
    }
}
