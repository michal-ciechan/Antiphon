using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests.TestHelpers;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class PhoneHomeBackpressureTests
{
    private static async Task ByteWindowDuringCatchupAsync()
    {
        const string text = "é😀日本語";
        var bytes = System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new RunnerOutputEvent(Guid.Empty, 1, text)));
        await using var h = await PhoneHomeHostedRunner.CreateAsync(count: 8, bytes: 2 * bytes - 1, holdCatchup: true);
        h.Live.DispatchEligible.ShouldBeFalse();
        h.Output(h.SessionId, 1, text);
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 1, "first byte-window event admitted during catch-up");
        h.Output(h.SessionId, 2, text);
        await PhoneHomeHostedRunner.UntilAsync(() => h.Service.WaitingForEventCredit, "bytes bind even with seven count credits available");
        h.Live.PendingEventBytes.ShouldBe(bytes);
        h.RecoveryHold.TrySetResult();
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.DispatchEligible, "catch-up List/transcript RPCs finish with exhausted event credit");
        h.Live.PendingEvents.ShouldBe(1);
        (await new PhoneHomeRunnerClient(h.Live).ListAsync(CancellationToken.None)).Count.ShouldBe(1);
        h.Bus.Release.TrySetResult();
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.ReceivedEvents == 2 && h.Live.PendingEvents == 0
            && h.Service.EventPressure.PendingEvents == 0, "byte-window sender and observer both drain");
        h.Live.AccountingSnapshot().ReceivedPayloadBytes.ShouldBe(2L * bytes);
        h.Live.PendingEventBytes.ShouldBe(0);
        h.Service.WaitingForEventCredit.ShouldBeFalse();
        h.Bus.Recipient.PublishedEvents.Where(e => e.EventName == "AgentTextDelta")
            .Select(e => JsonSerializer.SerializeToElement(e.Payload).GetProperty("text").GetString()).ShouldBe(new[] { text, text });
    }

    private static async Task AccountingBoundsAsync()
    {
        using var socket = new PhoneHomeTestWebSocket();
        await using var live = new PhoneHomeLiveConnection("test", Guid.NewGuid(), Guid.NewGuid(), 3, socket,
            new PhoneHomeLimits(MaxPendingEvents: 10), TimeProvider.System);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        using var stop = new CancellationTokenSource();
        var receive = live.ReceiveLoopAsync(stop.Token, factory.CreateLogger("accounting"));
        var raw = "{\"kind\":4,\"epoch\":3,\"requestId\":\"00000000-0000-0000-0000-000000000000\",\"eventName\":\"unknown\",\"payload\":{\"text\":\"é😀\"}}";
        try
        {
            socket.EnqueueJson(raw.Replace("\"epoch\":3", "\"epoch\":2"));
            socket.EnqueueJson(raw);
            var item = await live.Events.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            item.PayloadBytes.ShouldBe(17, "raw non-ASCII JSON has 17 UTF-8 bytes, not 14 UTF-16 units");
            live.PendingEventBytes.ShouldBe(17);
            item.Release(); item.Release();
            live.PendingEvents.ShouldBe(0);
            live.PendingEventBytes.ShouldBe(0);
            live.AccountingSnapshot().ReleasedEvents.ShouldBe(1);
            for (var i = 0; i < 5; i++) socket.EnqueueJson(raw);
            await PhoneHomeHostedRunner.UntilAsync(() => live.PendingEvents == 5 && logs.Entries.Count(e => Equals(e["Percent"], 50)) == 1,
                "exactly 50 percent warns once");
            for (var i = 0; i < 4; i++) socket.EnqueueJson(raw);
            await PhoneHomeHostedRunner.UntilAsync(() => live.PendingEvents == 9 && logs.Entries.Count(e => Equals(e["Percent"], 90)) == 1,
                "exactly 90 percent warns once");
            for (var i = 0; i < 9; i++) (await live.Events.ReadAsync()).Release();
            for (var i = 0; i < 11; i++) socket.EnqueueJson(raw);
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => receive.WaitAsync(TimeSpan.FromSeconds(5))))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
            live.OverflowAt!.Dimension.ShouldBe("count");
            live.OverflowAt.At.PendingEvents.ShouldBe(10);
            live.OverflowAt.IncomingPayloadBytes.ShouldBe(17);
            for (var i = 0; i < 10; i++) (await live.Events.ReadAsync()).Release();
            live.AccountingSnapshot().PendingEvents.ShouldBe(0);
            live.OverflowAt.At.PendingEvents.ShouldBe(10, "the trigger snapshot survives a later drain");
            live.AccountingSnapshot().ReceivedEvents.ShouldBe(20, "rejected overflow frames do not enter lifetime totals");
            live.AccountingSnapshot().ReceivedPayloadBytes.ShouldBe(340);
            logs.Entries.Count(e => Equals(e["Percent"], 50)).ShouldBe(1);
            logs.Entries.Count(e => Equals(e["Percent"], 90)).ShouldBe(1);
        }
        finally
        {
            stop.Cancel();
            try { await receive; } catch (OperationCanceledException) { } catch (PhoneHomeTransportException) { }
        }
    }

    private static async Task LegacyAndTicketBindingAsync(RunnerCapabilitiesDto capabilities)
    {
        PhoneHomeRunnerSettings settings = null!;
        await using (var completion = new PhoneHomeReceiveControl(PhoneHomeReceiveMode.ObserveOnly))
        await using (var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxPendingEvents: 2),
            configureRunnerSettings: s => settings = s, receiveControl: completion))
        {
            var ticket = await host.RegisterAsync(capabilities: capabilities);
            ticket.EventWindow!.MaxPendingEvents.ShouldBe(2);
            settings.Limits = new PhoneHomeLimits(MaxPendingEvents: 99);
            await using var peer = new PhoneHomeScriptedPeer { Epoch = ticket.Epoch };
            peer.Socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
            await peer.Socket.ConnectAsync(host.ConnectUri, CancellationToken.None);
            peer.Start();
            var live = await host.WaitLiveAsync();
            live.EventWindow.ShouldBe(ticket.EventWindow, "accepted connection is bound to its registration response");
            for (var i = 0; i < 3; i++) await peer.EmitAsync(new(PhoneHomeFrameKind.Event, live.Epoch, Guid.Empty,
                Payload: JsonSerializer.SerializeToElement(new { i }), EventName: "unknown"));
            await completion.WaitForCompletionAsync();
            live.LastDisconnectReason.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
            live.OverflowAt!.At.PendingEvents.ShouldBe(2, "later settings cannot enlarge the ticket's admitted bounds");
        }
        await using (var completion = new PhoneHomeReceiveControl(PhoneHomeReceiveMode.ObserveOnly))
        await using (var host = await PhoneHomeTestHost.StartAsync(limits: new PhoneHomeLimits(MaxPendingEvents: 1), receiveControl: completion))
        {
            await using var peer = await host.ConnectPeerAsync();
            var live = await host.WaitLiveAsync();
            live.EventWindow.ShouldBeNull("old runner has no capability");
            for (var i = 0; i < 2; i++) await peer.EmitAsync(new(PhoneHomeFrameKind.Event, live.Epoch, Guid.Empty,
                Payload: JsonSerializer.SerializeToElement(new { i }), EventName: "unknown"));
            await completion.WaitForCompletionAsync();
            live.LastDisconnectReason.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
        }
    }

    private static async Task NewRunnerLegacyServerAsync()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync(count: 2, legacyServer: true);
        h.Commands.Capabilities().Features.ShouldContain(PhoneHomeEventWindowOptions.Capability);
        h.Live.EventWindow.ShouldBeNull();
        h.Output(h.SessionId, 1, "held");
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Output(h.SessionId, 2, "full");
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 2, "legacy server budget full");
        h.Output(h.SessionId, 3, "legacy overflow");
        await h.Completion.WaitForCompletionAsync();
        h.Live.LastDisconnectReason.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
        h.Service.WaitingForEventCredit.ShouldBeFalse("an absent registration field keeps the actual new runner in legacy mode");
    }

    [Test]
    public async Task Sustained_overload_recovers_durable_transcript_once()
    {
        await using var h = await PhoneHomeHostedRunner.CreateAsync(observeCompletion: false);
        h.Output(h.SessionId, 1, "held");
        await h.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 2; i <= 4; i++) h.Output(h.SessionId, i, "fill");
        await PhoneHomeHostedRunner.UntilAsync(() => h.Live.PendingEvents == 4, "desktop window full");
        h.Commands.HoldList = true;
        var rpc = new PhoneHomeRunnerClient(h.Live).ListAsync(CancellationToken.None);
        try
        {
            await h.Commands.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            h.Transcript(h.SessionId, 5, "durable-overload", "retained UserPrompt");
            await PhoneHomeHostedRunner.UntilAsync(() => h.Service.WaitingForEventCredit, "durable event waits at sender");
            for (var i = 6; i <= 12; i++) h.Output(h.SessionId, i, "bounded");
            h.Service.EventPressure.PendingEvents.ShouldBe(8);
            h.Output(h.SessionId, 13, "one beyond the runner budget");
            (await Should.ThrowAsync<PhoneHomeTransportException>(async () => await rpc.WaitAsync(TimeSpan.FromSeconds(5))))
                .Code.ShouldBe(PhoneHomeProblemTypes.ConnectionClosedInFlight);
            h.Commands.ReleaseList.Set();
            (await Should.ThrowAsync<PhoneHomeTransportException>(() => h.Connection.WaitAsync(TimeSpan.FromSeconds(5))))
                .Code.ShouldBe(PhoneHomeProblemTypes.EventOverflow);
            h.Logs.Entries.ShouldContain(e => Equals(e["Loop"], "events") && Equals(e["Overflow"], "true"));
            h.Live.DispatchEligible.ShouldBeFalse();
            h.Bus.Release.TrySetResult();
            h.Commands.HoldList = false;
            h.Pump.CatchUpHold = h.RecoveryHold;
            var reconnect = h.ReconnectAsync();
            await PhoneHomeHostedRunner.UntilAsync(() => h.Host.Directory.SnapshotLive() is { } next && next.Epoch > 1,
                "replacement accepted before recovery");
            h.Host.Directory.SnapshotLive()!.DispatchEligible.ShouldBeFalse();
            h.RecoveryHold.TrySetResult();
            await reconnect;
            await using var db = h.Db();
            (await db.TranscriptEntries.CountAsync(e => e.Uuid == "durable-overload")).ShouldBe(1);
            // Replay the same durable UUID through the new live path: still one committed row.
            h.Transcript(h.SessionId, 5, "durable-overload", "retained UserPrompt");
            await PhoneHomeHostedRunner.UntilAsync(() => h.Live.ReceivedEvents == 1 && h.Live.PendingEvents == 0, "replayed live row is observed");
            (await db.TranscriptEntries.CountAsync(e => e.Uuid == "durable-overload")).ShouldBe(1);
            h.Live.DispatchEligible.ShouldBeTrue();
        }
        finally
        {
            h.Commands.ReleaseList.Set(); h.Bus.Release.TrySetResult(); h.RecoveryHold.TrySetResult();
            try { await rpc; } catch (PhoneHomeTransportException) { }
        }
    }
}
