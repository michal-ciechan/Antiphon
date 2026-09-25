using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0679 review 914a96fd D1. A phone-home connection that closes under a request is two
// different facts for the queue. A request that was never written reached nobody: that is the
// unreachable defer, no attempt charged. A request that was written may have been acted on: the
// attempt and its transcript floor must survive, so the next flush asks the transcript before it
// types anything again. Both are driven through the real transport: every terminal write the fake
// adapter sees is first carried over a real phone-home connection to a scripted runner.
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueuePhoneHomeDropTests
{
    private const string Body =
        "CARD-0679 in-flight drop probe: please summarise the phone-home reconnect evidence";

    [Test]
    public async Task Drop_under_an_in_flight_Enter_keeps_the_attempt_and_late_confirms_without_retyping()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peerA = await host.ConnectPeerAsync();
        var liveA = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(liveA);
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await h.InsertTurnAsync("prior-observable-turn", "prior-answer");
        var client = new RunnerScopedSessionRunnerClient(host.Directory, host.AllowedRunnerId);

        var dropped = false;
        h.Adapter.BeforeInput = async (input, ct) =>
        {
            if (input != "\r" || dropped)
            {
                await client.SendInputAsync(h.SessionId, input, ct);
                return;
            }

            // The submitting Enter reaches the runner, which submits the prompt; the socket drops
            // before its reply, so the desktop cannot know that it did.
            dropped = true;
            peerA.SilentFor(PhoneHomeOperation.Input);
            var enter = client.SendInputAsync(h.SessionId, input, ct);
            await WaitUntilAsync(() => peerA.RequestCount(PhoneHomeOperation.Input) == 2);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, Body, timestamp: DateTime.UtcNow);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            h.Adapter.PrimeComposer("");
            peerA.Socket.Abort();
            await enter.WaitAsync(TimeSpan.FromSeconds(5));
        };

        await h.Queue.EnqueueAsync(h.SessionId, Body, MessageSendMode.WhenIdle, CancellationToken.None);
        dropped.ShouldBeTrue("the first delivery's Enter was carried over the connection that dropped");
        peerA.RequestCount(PhoneHomeOperation.Input).ShouldBe(2);

        SessionQueuedMessage afterDrop;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            afterDrop = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == h.SessionId);

        // The runner is back on a new connection before the next flush.
        await WaitUntilAsync(() => !liveA.SocketOpen);
        await using var peerB = await host.ConnectPeerAsync();
        await WaitUntilAsync(() => host.Directory.SnapshotLive() is { } l && !ReferenceEquals(l, liveA));
        host.Directory.MarkRecovered(host.Directory.SnapshotLive()!);

        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);

        peerB.RequestCount(PhoneHomeOperation.Input).ShouldBe(0,
            "a body the runner may already have submitted is never typed again before the transcript is asked");
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        // What made that possible: the in-flight loss charged the attempt and kept its floor.
        afterDrop.Status.ShouldBe(QueuedMessageStatus.Pending);
        afterDrop.DeliveryAttempts.ShouldBe(1);
        afterDrop.LastDeliveryStartedAt.ShouldNotBeNull();
        afterDrop.LastDeliveryBaselineSequence.ShouldNotBeNull();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task Drop_before_the_body_is_sent_defers_with_no_attempt_charged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await h.InsertTurnAsync("prior-observable-turn", "prior-answer");
        // A client still holding the connection that has since closed: its next write never leaves.
        var stale = new PhoneHomeRunnerClient(live);
        peer.Socket.Abort();
        await WaitUntilAsync(() => !live.SocketOpen);
        h.Adapter.BeforeInput = (input, ct) => stale.SendInputAsync(h.SessionId, input, ct);

        await h.Queue.EnqueueAsync(h.SessionId, Body, MessageSendMode.WhenIdle, CancellationToken.None);

        peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0, "a write that never left is the unreachable defer, not an attempt");
        row.LastDeliveryStartedAt.ShouldBeNull();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
                && i.Kind == AgentIncidentKind.DeliveryTransportFailed))
            .ShouldBe(0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        condition().ShouldBeTrue();
    }
}
