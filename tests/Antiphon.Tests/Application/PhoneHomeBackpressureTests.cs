using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public partial class PhoneHomeBackpressureTests
{
    [Test]
    public async Task Finite_stall_preserves_connection_order_and_delivery()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync();
        await h.PrepareMentionAsync();
        var chunks = new[] { "first\n", "@fa", "ke hello", "\n" };
        h.Output(h.SessionId, 1, chunks[0]);
        await h.Bus.Entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
        for (var i = 1; i < chunks.Length; i++) h.Output(h.SessionId, i + 1, chunks[i]);
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 4, "the first four include the held observer");
        h.Commands.HoldList = true;
        var rpc = new PhoneHomeRunnerClient(h.Live).ListAsync(CancellationToken.None);
        await h.Commands.ListEntered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
        var nonce = "delivery-" + Guid.NewGuid().ToString("N");
        h.Transcript(h.SessionId, 5, nonce, nonce);
        h.Transcript(h.SessionId, 6, nonce + "-end", "", TranscriptKinds.TurnEnd);
        h.Output(h.SessionId, 7, "last");
        h.Exit(h.SessionId, 8, h.Generation);
        try
        {
            await PhoneHomeHostedRunner.UntilAsync(() => !h.Live.SocketOpen || WaitingForCredit(h),
                "the sender either reaches credit pressure or the legacy endpoint closes");
            if (!h.Live.SocketOpen)
            {
                await h.Completion.WaitForCompletionAsync();
                Console.WriteLine($"C1523 RED endpoint reason={h.Live.LastDisconnectReason} eligible={h.Live.DispatchEligible}");
                h.Live.LastDisconnectReason.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
                await Should.ThrowAsync<PhoneHomeTransportException>(async () => await rpc);
            }
            h.Live.SocketOpen.ShouldBeTrue("a finite eight-event burst must keep the real connection open");
            h.Live.DispatchEligible.ShouldBeTrue();
            h.Live.PendingEvents.ShouldBe(4);
            var heartbeat = h.Live.LastHeartbeatUtc;
            h.Clock.Advance(TimeSpan.FromSeconds(2));
            h.Commands.ReleaseList.Set();
            (await rpc.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait)).Count.ShouldBe(1);
            await PhoneHomeHostedRunner.UntilAsync(() => h.Live.LastHeartbeatUtc > heartbeat,
                "heartbeats bypass the event credit wait");
            h.Live.PendingEvents.ShouldBe(4);
            h.Bus.Release.TrySetResult();
            await PhoneHomeHostedRunner.UntilAsync(async () =>
            {
                await using var db = h.Db();
                return await db.TranscriptEntries.CountAsync(t => t.Uuid == nonce) == 1
                    && await db.AgentSessions.AnyAsync(s => s.Id == h.SessionId && s.Status == SessionStatus.Stopped);
            }, "durable prompt and matching generation exit are observed after the hold");
            await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 0 && !WaitingForCredit(h), "all reservations drain");
            h.Live.PendingEventBytes.ShouldBe(0);
            var deltas = h.Bus.Recipient.PublishedEvents.Where(e => e.EventName == "AgentTextDelta")
                .Select(e => JsonSerializer.SerializeToElement(e.Payload)).ToArray();
            deltas.Select(e => e.GetProperty("sequence").GetInt64()).ShouldBe(new long[] { 1, 2, 3, 4, 7 });
            deltas.Select(e => e.GetProperty("text").GetString()).ShouldBe(chunks.Concat(new[] { "last" }));
            await PhoneHomeHostedRunner.UntilAsync(async () =>
            {
                await using var db = h.Db();
                return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.Bridge.SessionId
                    && m.Origin == QueuedMessageOrigin.Mention && m.Body.Contains("hello")) == 1;
            }, "the split mention reaches the real recipient queue exactly once");
        }
        finally
        {
            h.Commands.ReleaseList.Set();
            h.Bus.Release.TrySetResult();
            try { await rpc; } catch (PhoneHomeTransportException) { }
        }
        await ByteWindowDuringCatchupAsync();
    }

    [Test]
    public async Task Exit_after_split_mention_still_queues_the_line_once()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync();
        await h.PrepareMentionAsync();
        var router = h.Bridge.Provider.GetRequiredService<AgentMentionRouter>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        router.RouteGateEntered = entered;
        router.BeforeRouteGate = gate;
        // Release the publication hold so the split line is observed and the route gate is the stall.
        h.Bus.Release.TrySetResult();
        try
        {
            h.Output(h.SessionId, 1, "@fa");
            h.Output(h.SessionId, 2, "ke hello");
            h.Output(h.SessionId, 3, "\n");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            h.Exit(h.SessionId, 4, h.Generation);
            using var stopWait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!stopWait.IsCancellationRequested)
            {
                await using var db = h.Db();
                if (await db.AgentSessions.AnyAsync(s => s.Id == h.SessionId && s.Status == SessionStatus.Stopped))
                    break;
                try { await Task.Delay(10, stopWait.Token); } catch (OperationCanceledException) { }
            }

            gate.TrySetResult();
            await PhoneHomeHostedRunner.UntilAsync(async () =>
            {
                await using var db = h.Db();
                return await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.Bridge.SessionId
                    && m.Origin == QueuedMessageOrigin.Mention && m.Body.Contains("hello")) == 1;
            }, "the split mention reaches the real recipient queue exactly once");
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Test]
    public async Task Slow_session_does_not_block_another_session()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync(count: 16);
        var other = await h.AddSessionAsync();
        h.Output(h.SessionId, 1, "held-session-A");
        await h.Bus.Entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
        h.Transcript(h.SessionId, 2, "a-behind-output", "A");
        h.Exit(h.SessionId, 3, h.Generation);
        h.Transcript(other, 1, "b-progress", "B");
        h.Exit(other, 2, h.Generation);
        await PhoneHomeHostedRunner.UntilAsync(async () =>
        {
            await using var db = h.Db();
            return await db.TranscriptEntries.AnyAsync(e => e.Uuid == "b-progress")
                && await db.AgentSessions.AnyAsync(s => s.Id == other && s.Status == SessionStatus.Stopped);
        }, "session B must commit and exit while A's publication remains held");
        await using (var db = h.Db())
        {
            (await db.TranscriptEntries.AnyAsync(e => e.Uuid == "a-behind-output")).ShouldBeFalse();
            (await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Running);
        }
        h.Bus.Release.TrySetResult();
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 0, "per-session FIFO eventually drains");
        await using var final = h.Db();
        (await final.TranscriptEntries.CountAsync(e => e.Uuid == "a-behind-output")).ShouldBe(1);
        (await final.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Stopped);
        await OwnershipAndFailureAsync(h);
        await WorkerBoundAsync();
        await ReplacementWaitsForObserverAsync();
    }

    [Test]
    public async Task End_snapshot_distinguishes_pending_from_received()
    {
        await using var completion = new PhoneHomeReceiveControl(PhoneHomeReceiveMode.ObserveOnly);
        await using var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxPendingEvents: 10), receiveControl: completion);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        for (var i = 0; i < 12; i++)
        {
            await peer.EmitAsync(new(PhoneHomeFrameKind.Event, live.Epoch, Guid.Empty,
                Payload: JsonSerializer.SerializeToElement(new { text = "non-ASCII 😀 é" }), EventName: "unknown"));
            await PhoneHomeHostedRunner.UntilAsync(() => live.PendingEvents == 1, "one arrival at a time");
            var frame = await live.Events.ReadAsync();
            // Before the queued lease exists the base has a bare frame; inspect it as data so
            // the witness builds before production changes.
            frame.Release();
            live.PendingEvents.ShouldBe(0);
            live.PendingEventBytes.ShouldBe(0);
        }
        host.Logs.Entries.Where(e => e.Properties.ContainsKey("Percent")).ShouldBeEmpty("lifetime totals never cause high water");
        peer.Socket.Abort();
        await completion.WaitForCompletionAsync();
        var end = host.Logs.Entries.Single(e => e.Properties.ContainsKey("Reason") && e.Message.Contains(" ended:"));
        end.Properties.ContainsKey("ReceivedEvents").ShouldBeTrue("cumulative arrivals need an explicit total name");
        Convert.ToInt64(end["ReceivedEvents"]).ShouldBe(12);
        Convert.ToInt32(end["PendingEvents"]).ShouldBe(0);
        Convert.ToInt32(end["PendingEventBytes"]).ShouldBe(0);
        end.Properties.ContainsKey("LiveBufferEvents").ShouldBeFalse();
        end.Message.ShouldContain("received total 12 events");
        end.Message.ShouldContain("pending 0 events / 0 bytes");
        end.Message.ShouldNotContain("live buffer");
        await AccountingBoundsAsync();
    }

    [Test]
    public async Task Slow_observer_is_attributed_without_payload()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync();
        var secretText = "never-log-payload-" + Guid.NewGuid().ToString("N");
        h.Output(h.SessionId, 1, secretText);
        await h.Bus.Entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        await PhoneHomeHostedRunner.UntilAsync(() => h.Logs.Entries.Any(e => Equals(e["Phase"], "output.publish")),
            "a still-held output.publish phase must be attributed by supervision");
        var warning = h.Logs.Entries.Single(e => Equals(e["Phase"], "output.publish"));
        warning["SessionId"].ShouldBe(h.SessionId);
        warning["EventName"].ShouldBe(SessionRunnerEventNames.SessionOutput);
        Convert.ToDouble(warning["ElapsedMs"]).ShouldBeGreaterThanOrEqualTo(1000);
        h.Clock.Advance(TimeSpan.FromSeconds(9));
        await h.Pump.RunCycleAsync(CancellationToken.None);
        h.Logs.Entries.Count(e => Equals(e["Phase"], "output.publish")).ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await h.Pump.RunCycleAsync(CancellationToken.None);
        h.Logs.Entries.Count(e => Equals(e["Phase"], "output.publish")).ShouldBe(2);
        string.Join("\n", h.Logs.Entries.Concat(h.Host.Logs.Entries).Select(e => e.Message)).ShouldNotContain(secretText);
        h.Bus.Release.TrySetResult();
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 0, "publication scenario drains");
        var other = await h.AddSessionAsync();
        h.Activity.Hold = true;
        h.Output(other, 1, secretText);
        await h.Activity.Entered.Task.WaitAsync(LoadTimeoutBounds.PhoneHomeSignalWait);
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        await PhoneHomeHostedRunner.UntilAsync(() => h.Logs.Entries.Any(e => Equals(e["Phase"], "output.activity")),
            "the independently held activity save is named while still running");
        h.Logs.Entries.Single(e => Equals(e["Phase"], "output.activity"))["SessionId"].ShouldBe(other);
        h.Activity.Release.TrySetResult();
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 0, "activity scenario drains");
        string.Join("\n", h.Logs.Entries.Select(e => e.Message)).ShouldNotContain(secretText);
    }

    [Test]
    public async Task Window_limits_epoch_and_legacy_peers()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync();
        h.Commands.Capabilities().Features.ShouldContain("phoneHomeEventWindowV1",
            "the shipping adapter, not the fake command surface, must advertise support");
        var window = JsonSerializer.SerializeToElement(h.Live.EventWindow);
        window.GetProperty("Version").GetInt32().ShouldBe(1);
        window.GetProperty("MaxPendingEvents").GetInt32().ShouldBe(4);
        await using var legacy = await PhoneHomeTestHost.StartAsync();
        var oldTicket = JsonSerializer.SerializeToElement(await legacy.RegisterAsync(), PhoneHomeFraming.Json);
        if (oldTicket.TryGetProperty("eventWindow", out var oldWindow)) oldWindow.ValueKind.ShouldBe(JsonValueKind.Null);
        await ProtocolGuardsAsync();
        await LegacyAndTicketBindingAsync(h.Commands.Capabilities());
        await NewRunnerLegacyServerAsync();
    }

    private static bool WaitingForCredit(PhoneHomeHostedRunner h) =>
        h.Service.GetType().GetProperty("WaitingForEventCredit")?.GetValue(h.Service) is true;

}
