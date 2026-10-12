using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1205 S2. Iterator cases read SessionRunnerHttpClient. Pump cases run the real event pump
/// against an isolated store. Exact ": keepalive" or the first parsed supported event is readiness.
/// ": connected", other comments, an empty frame, and an unsupported event are not.
/// </summary>
[Category("Integration")]
public class SessionRunnerStreamReadinessTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(90_000)]
    public async Task C1205_Connected_comment_is_not_readiness(CancellationToken ct)
    {
        // V-3. Case: response headers, then exact ": connected", with registration still closed.
        // Oracle: no readiness marker and no second snapshot. PC-3 emits readiness on that comment;
        // markerCount.ShouldBe(0) fails. The next read is not held on the second GET.
        await using var harness = await TurnEndGapReadiness.CreatePumpAsync();
        harness.OnConnection = (connection, token) =>
            connection.WriteFrameAsync(": connected\n\n", armNextRead: true, token, "connected-consumed");
        await harness.StartPumpAsync(ct);
        await harness.WaitNextReadAsync(ct);

        var markerCount = harness.MarkerCount;
        markerCount.ShouldBe(0);
        harness.CallerGets.ShouldBe(1);
        harness.SubscribeCount.ShouldBe(0);
        harness.HeadersRecorded.ShouldBeTrue();
        harness.HeaderStatus.ShouldBe(200);
        harness.HeaderContentType.ShouldBe("text/event-stream");
    }

    [Test]
    [Timeout(90_000)]
    [Arguments(": Keepalive\n\n", "capital-keepalive")]
    [Arguments(": keepalive extra\n\n", "keepalive-extra")]
    [Arguments(": unknown\n\n", "unknown")]
    [Arguments("\n", "empty")]
    [Arguments("event: SessionHeartbeat\ndata: {}\n\n", "unsupported")]
    public async Task C1205_Unknown_comment_is_not_readiness(string frame, string label, CancellationToken ct)
    {
        // V-3. Case: a comment, empty frame, or unsupported event is not subscription proof.
        // PC-4 changes the keepalive check to StartsWith(":"); markerCount.ShouldBe(0) fails
        // on the unknown comment row.
        await using var harness = TurnEndGapReadiness.CreateStream();
        harness.OnConnection = (connection, token) =>
            connection.WriteFrameAsync(frame, armNextRead: true, token, label);
        var events = await harness.CollectUntilBarrierAsync(ct);
        var markerCount = events.Count(evt => evt.SubscriptionReady);
        markerCount.ShouldBe(0, label);
    }

    [Test]
    [Timeout(90_000)]
    public async Task C1205_Keepalive_emits_readiness_once(CancellationToken ct)
    {
        // V-4. Case: the hub subscriber is registered, then three exact keepalive comments.
        // Oracle: one internal marker and no transcript row or publication carrying it.
        // PC-5 emits on every keepalive. PC-10 dispatches the marker as output.
        await using var harness = await TurnEndGapReadiness.CreatePumpAsync();
        harness.OnConnection = async (connection, token) =>
        {
            connection.Subscribe();
            await connection.WriteFrameAsync(": connected\n\n", armNextRead: false, token, "connected-consumed");
            await connection.WriteFrameAsync(": keepalive\n\n", armNextRead: false, token, "keepalive-1");
            await connection.WriteFrameAsync(": keepalive\n\n", armNextRead: false, token, "keepalive-2");
            await connection.WriteFrameAsync(": keepalive\n\n", armNextRead: true, token, "keepalive-3");
        };
        await harness.StartPumpAsync(ct);
        await harness.WaitNextReadAsync(ct);

        var markerCount = harness.MarkerCount;
        markerCount.ShouldBe(1);
        var publicationsWithMarker = harness.PublicationsWithMarker();
        publicationsWithMarker.ShouldBeEmpty();
        (await harness.MarkerRowCountAsync()).ShouldBe(0);
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("transcript")]
    [Arguments("output")]
    [Arguments("exit")]
    [Arguments("fault")]
    [Arguments("bound")]
    [Arguments("adopted")]
    [Arguments("status")]
    [Arguments("started")]
    public async Task C1205_First_event_follows_readiness_without_loss(string variant, CancellationToken ct)
    {
        // V-5. Case: the first supported event is itself the subscription proof.
        // Oracle: the iterator yields the marker and then that same event.
        // PC-6 drops the held event; events.Count.ShouldBe(2) fails.
        var expected = ExpectedEvent(variant, out var frame);
        await using var harness = TurnEndGapReadiness.CreateStream();
        harness.OnConnection = (connection, token) =>
            connection.WriteFrameAsync(frame, armNextRead: true, token, variant);
        var events = await harness.CollectUntilBarrierAsync(ct);
        events.Count.ShouldBe(2, variant);
        events[0].SubscriptionReady.ShouldBeTrue(variant);
        events[0].EventName.ShouldBe(SessionRunnerEvent.SubscriptionReadyName, variant);
        events[1].ShouldBe(expected, variant);
    }

    [Test]
    [Timeout(90_000)]
    public async Task C1205_Reconnect_rearms_readiness(CancellationToken ct)
    {
        // V-6. Case: the first stream ends, a second gap TurnEnd is published with no subscriber,
        // then the second connection's keepalive is the new proof.
        // PC-7 keeps the latch across iterator calls; secondConnectionMarkers.ShouldBe(1) fails.
        const string secondEnd = "c1205-second-end";
        var secondConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await TurnEndGapReadiness.CreatePumpAsync(reconnectDelayMs: 1);
        harness.OnConnection = async (connection, token) =>
        {
            if (connection.Number == 1)
            {
                connection.Subscribe();
                await connection.WriteFrameAsync(": keepalive\n\n", armNextRead: false, token, "keepalive-1");
                connection.End();
                return;
            }

            await connection.WriteFrameAsync(": connected\n\n", armNextRead: false, token, "connected-2");
            secondConnected.TrySetResult();
            await hold.Task.WaitAsync(token);
            connection.Subscribe();
            await connection.WriteFrameAsync(": keepalive\n\n", armNextRead: true, token, "keepalive-2");
        };
        try
        {
            await harness.StartPumpAsync(ct);
            await secondConnected.Task.WaitAsync(ct);
            harness.SubscribeCount.ShouldBe(0);
            harness.PublishGapEnd(secondEnd);
            hold.TrySetResult();
            await harness.WaitNextReadAsync(ct);
            var secondConnectionMarkers = harness.MarkersOn(2);
            secondConnectionMarkers.ShouldBe(1);
            harness.MarkersOn(1).ShouldBe(1);
            (await harness.CountAsync(secondEnd, TranscriptKinds.TurnEnd)).ShouldBe(1);
        }
        finally
        {
            hold.TrySetResult();
        }
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("read-fault")]
    [Arguments("cancellation")]
    public async Task C1205_Stream_failure_and_cancellation_release_owned_resources(string mode, CancellationToken ct)
    {
        // V-7. Case: a pending read faults or the caller cancels.
        // Oracle: response, content, and stream are disposed, the subscriber is closed, and the
        // producer is joined. Count those flags before this test's teardown dispose.
        // PC-8 removes the response, stream, and reader disposal; streamDisposed.ShouldBeTrue()
        // fails in both rows.
        await using var harness = TurnEndGapReadiness.CreateStream();
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnConnection = async (connection, token) =>
        {
            connection.Subscribe();
            subscribed.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var enumerator = harness.Stream(cts.Token).GetAsyncEnumerator(cts.Token);
        var pending = enumerator.MoveNextAsync();
        await harness.ReadEntered.WaitAsync(ct);
        await subscribed.Task.WaitAsync(ct);
        if (mode == "read-fault")
            harness.FaultRead();
        else
            await cts.CancelAsync();
        try
        {
            await pending;
        }
        catch (Exception)
        {
        }

        var streamDisposed = harness.StreamDisposed;
        streamDisposed.ShouldBeTrue(mode);
        harness.ResponseDisposed.ShouldBeTrue(mode);
        harness.ContentDisposed.ShouldBeTrue(mode);
        harness.SubscriberClosed.ShouldBeTrue(mode);
        harness.CancelWorkers();
        if (harness.Producer is { } producer)
        {
            try
            {
                await producer.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                producer.IsCompleted.ShouldBeTrue($"{mode} producer did not join");
            }
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task C1205_Catchup_finishes_before_live_dispatch(CancellationToken ct)
    {
        // V-5/R-1. Case: the missing end is in the snapshot, and the first live output is held
        // until the post-proof snapshot commits. PC-9 discards that catch-up task;
        // liveCallbacksAtSaveBarrier.ShouldBeEmpty() fails.
        await using var harness = await TurnEndGapReadiness.CreatePumpAsync(holdSecondSave: true);
        harness.OnConnection = async (connection, token) =>
        {
            harness.PublishGapEnd(harness.MissingEndUuid);
            await connection.WriteFrameAsync(": connected\n\n", armNextRead: true, token, "connected-consumed");
            connection.Subscribe();
            var output = new RunnerOutputEvent(harness.SessionId, 40, "c1205-live-output");
            var json = JsonSerializer.Serialize(output, Web);
            await connection.WriteFrameAsync(
                $"event: {SessionRunnerEventNames.SessionOutput}\ndata: {json}\n\n",
                armNextRead: false,
                token,
                "proof-consumed");
        };
        await harness.StartPumpAsync(ct);
        await harness.WaitNextReadAsync(ct);
        harness.MarkerCount.ShouldBe(0);
        harness.CallerGets.ShouldBe(1);
        harness.ReleaseRead();
        await Task.WhenAny(harness.Gate!.Reached.Task, harness.Bus!.Seen.Task).WaitAsync(ct);
        var liveCallbacksAtSaveBarrier = harness.Bus.Deltas();
        liveCallbacksAtSaveBarrier.ShouldBeEmpty();
        harness.Note("c2-save");
        harness.Gate.Release.TrySetResult();
        await harness.Bus.Seen.Task.WaitAsync(ct);
        harness.Note("live-callback");
        AssertChronological(harness.Order,
            "caller-transcript",
            "hub-subscribed",
            "proof-consumed",
            "c2-save",
            "live-callback");
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("success")]
    [Arguments("timeout")]
    [Arguments("throw")]
    [Arguments("cancellation")]
    public async Task C1205_Held_event_survives_failed_second_catchup(string mode, CancellationToken ct)
    {
        // V-5 repair. The first parsed output is held across the readiness yield while the
        // second /sessions list runs. The successful catch-up is the control: one callback,
        // after that list returns. A list timeout, a throwing list, and host cancellation
        // must deliver that same output once. PC-111 omits the common held offer on exceptional catch-up unwind:
        // the timeout row's deltas.Count.ShouldBe(1) fails. PC-112 skips the common offer on host
        // cancellation. PC-113 rethrows the list fault: the throw row disconnects.
        // PC-114 yields a payload-free marker: the timeout row loses the only delivered copy.
        const string text = "c1205-held-output";
        await using var harness = await TurnEndGapReadiness.CreatePumpAsync(
            reconnectDelayMs: 60_000,
            listTimeoutSeconds: mode == "timeout" ? 1 : 30);
        harness.SecondListFault = mode switch
        {
            "timeout" => SecondListFault.Timeout,
            "throw" => SecondListFault.Throw,
            "cancellation" => SecondListFault.Cancel,
            _ => SecondListFault.None,
        };
        harness.OnConnection = async (connection, token) =>
        {
            connection.Subscribe();
            var output = new RunnerOutputEvent(harness.SessionId, 4, text);
            var json = JsonSerializer.Serialize(output, Web);
            await connection.WriteFrameAsync(
                $"event: {SessionRunnerEventNames.SessionOutput}\ndata: {json}\n\n",
                armNextRead: false,
                token,
                "proof-consumed");
        };
        await harness.StartPumpAsync(ct);
        if (mode == "cancellation")
        {
            await harness.SecondListEntered.WaitAsync(ct);
            harness.CancelPump();
            await harness.StopPumpAsync();
        }
        else
        {
            await Task.WhenAny(harness.Bus!.Seen.Task, harness.Disconnected).WaitAsync(ct);
            if (mode == "timeout")
                await harness.Disconnected.WaitAsync(ct);
        }

        var deltas = harness.Bus!.Deltas();
        deltas.Count.ShouldBe(1, mode);
        JsonSerializer.Serialize(deltas[0].Payload).ShouldContain(text);
        AssertChronological(harness.Order, "proof-consumed", "live-callback");
        harness.ListCalls.ShouldBe(2, mode);
        var disconnected = harness.Disconnected.IsCompleted;
        disconnected.ShouldBe(mode == "timeout", mode);
        if (mode == "success")
            AssertChronological(harness.Order, "list-2-ok", "live-callback");
        if (mode is "timeout" or "cancellation")
            AssertChronological(harness.Order, "live-callback", "iterator-disposed");
        if (mode == "timeout")
            harness.PumpWarnings.ShouldContain(line => line.Contains("without snapshot coverage", StringComparison.Ordinal));
    }

    private static SessionRunnerEvent ExpectedEvent(string variant, out string frame)
    {
        var id = Guid.Parse("12051205-0006-4000-8000-000000000006");
        var accepted = new DateTime(2026, 10, 12, 11, 19, 22, DateTimeKind.Utc);
        var observed = new DateTime(2026, 10, 12, 11, 19, 23, DateTimeKind.Utc);
        var startedAt = new DateTime(2026, 10, 12, 11, 19, 24, DateTimeKind.Utc);
        var timestamp = new DateTimeOffset(2026, 10, 12, 11, 19, 21, TimeSpan.Zero);
        switch (variant)
        {
            case "transcript":
            {
                var wire = new RunnerTranscriptEvent(
                    id, 8, TranscriptKinds.AssistantText, "c1205-t06-uuid", "parent-1", timestamp,
                    "assistant", "c1205-transcript-row", null, null, null, null, null);
                frame = Frame(SessionRunnerEventNames.SessionTranscript, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionTranscript,
                    id,
                    Transcript: new SessionRunnerTranscriptEvent(
                        id, 8, TranscriptKinds.AssistantText, "c1205-t06-uuid", "parent-1", timestamp,
                        "assistant", "c1205-transcript-row", null, null, null, null, null));
            }
            case "output":
            {
                var wire = new RunnerOutputEvent(id, 4, "c1205-output-text");
                frame = Frame(SessionRunnerEventNames.SessionOutput, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionOutput,
                    id,
                    Output: new SessionRunnerOutputEvent(id, 4, "c1205-output-text"));
            }
            case "exit":
            {
                var wire = new RunnerSessionExitedEvent(id, 9, "Unknown", 15, accepted);
                frame = Frame(SessionRunnerEventNames.SessionExited, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionExited,
                    id,
                    Exited: new SessionRunnerExitedEvent(id, 9, AgentExitReason.Unknown, 15, accepted));
            }
            case "fault":
            {
                var wire = new RunnerTranscriptFaultEvent(
                    id, TranscriptFaultKinds.AdoptionRefused, "c1205-fault", "candidate.jsonl", 3.5, 2);
                frame = Frame(SessionRunnerEventNames.SessionTranscriptFault, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionTranscriptFault,
                    id,
                    TranscriptFault: new SessionRunnerTranscriptFaultEvent(
                        id, TranscriptFaultKinds.AdoptionRefused, "c1205-fault", "candidate.jsonl", 3.5, 2));
            }
            case "bound":
            {
                var wire = new RunnerTranscriptBoundEvent(id, "transcript.jsonl", TranscriptBindMethods.Discovery);
                frame = Frame(SessionRunnerEventNames.SessionTranscriptBound, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionTranscriptBound,
                    id,
                    TranscriptBound: new SessionRunnerTranscriptBoundEvent(
                        id, "transcript.jsonl", TranscriptBindMethods.Discovery));
            }
            case "adopted":
            {
                var wire = new RunnerSessionAdoptedEvent(id, 7, startedAt, 11, accepted);
                frame = Frame(SessionRunnerEventNames.SessionAdopted, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionAdopted,
                    id,
                    Adopted: new SessionRunnerAdoptedEvent(id, 7, 11, accepted));
            }
            case "status":
            {
                var wire = new RunnerAgentStatusEvent(id, "blocked", "running", observed);
                frame = Frame(SessionRunnerEventNames.SessionAgentStatus, wire);
                return new SessionRunnerEvent(
                    SessionRunnerEventNames.SessionAgentStatus,
                    id,
                    AgentStatus: new SessionRunnerAgentStatusEvent(id, "blocked", "running", observed));
            }
            case "started":
            {
                var wire = new RunnerSessionStartedEvent(id, 9, startedAt, accepted);
                frame = Frame(SessionRunnerEventNames.SessionStarted, wire);
                return new SessionRunnerEvent(SessionRunnerEventNames.SessionStarted, id);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown T06 variant.");
        }
    }

    private static string Frame<T>(string name, T body) =>
        $"event: {name}\ndata: {JsonSerializer.Serialize(body, Web)}\n\n";

    private static void AssertChronological(IReadOnlyList<string> order, params string[] markers)
    {
        var cursor = -1;
        foreach (var marker in markers)
        {
            var index = -1;
            for (var i = cursor + 1; i < order.Count; i++)
            {
                if (order[i] == marker)
                {
                    index = i;
                    break;
                }
            }

            index.ShouldBeGreaterThan(cursor, $"'{marker}' missing or out of order in [{string.Join(" > ", order)}]");
            cursor = index;
        }
    }
}
