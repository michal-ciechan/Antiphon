using System.Diagnostics;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Hardening tests for the Socket Mode integration, driven via <see cref="FakeSlackServer"/> fault
/// injection — the Slack twin of <see cref="TelegramResilienceTests"/>, and the same class of defect.
///
/// Socket Mode adds a failure surface long-polling does not have: a long-lived connection that Slack
/// recycles on purpose (<c>disconnect</c>), that can die mid-stream, and that is preceded by an HTTP
/// handshake of its own. Every one of those is transient — the receive stream must never end.
/// </summary>
public sealed class SlackResilienceTests
{
    private static SlackSettings Settings(FakeSlackServer fake, int errorBackoffSeconds = 0, int sendRetries = 2) => new()
    {
        ApiBaseUrl = fake.ApiBaseUrl,
        BotToken = fake.BotToken,
        AppToken = fake.AppToken,
        BotUserId = fake.BotUserId,
        ErrorBackoffSeconds = errorBackoffSeconds,   // 0 → fast recovery in tests; Retry-After still honored
        SendRetryAttempts = sendRetries,
    };

    private static SlackChannelAdapter Adapter(FakeSlackServer fake, SlackSettings settings, TimeSpan? httpTimeout = null) =>
        new(
            httpTimeout is { } t ? new HttpClient { Timeout = t } : new HttpClient(),
            settings,
            NullLogger<SlackChannelAdapter>.Instance);

    private static async Task<ChannelMessage?> FirstMessageAsync(SlackChannelAdapter adapter, int seconds = 25)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
            return msg;
        return null;
    }

    // ---- receive: the handshake ----

    [Test]
    public async Task Receive_backs_off_and_recovers_after_a_failed_connections_open()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueConnectionsOpenError("invalid_auth");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "after a failed handshake");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake)));

        msg.ShouldNotBeNull();
        msg!.Text.ShouldBe("after a failed handshake");
        fake.ConnectionsOpenCalls.ShouldBeGreaterThanOrEqualTo(2, "it retried past the failure rather than giving up");
    }

    [Test]
    public async Task Receive_recovers_after_a_5xx_non_json_handshake_body()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueConnectionsOpenServerError();
        fake.EnqueueMessage("C0ENG", "U0ALICE", "after 5xx");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake)));

        msg!.Text.ShouldBe("after 5xx");
    }

    // The silent-ingress-death incident (2026-07-31, AZ Care) in its Slack shape: a stalled
    // apps.connections.open trips HttpClient.Timeout, which throws TaskCanceledException — an
    // OperationCanceledException with the ambient token NOT cancelled. Treating that as shutdown
    // ends the receive stream forever with zero log output.
    [Test]
    public async Task Receive_survives_an_http_client_timeout_on_the_handshake()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueConnectionsOpenHang(TimeSpan.FromSeconds(5));   // longer than the client timeout below
        fake.EnqueueMessage("C0ENG", "U0ALICE", "after the timeout");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake), httpTimeout: TimeSpan.FromMilliseconds(500)));

        msg.ShouldNotBeNull("the stream must keep reconnecting past the timeout, not silently end");
        msg!.Text.ShouldBe("after the timeout");
        fake.ConnectionsOpenCalls.ShouldBeGreaterThanOrEqualTo(2);
    }

    // ---- receive: the socket ----

    [Test]
    public async Task A_disconnect_envelope_reopens_the_connection_cleanly()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        // Slack recycles Socket Mode connections routinely — this is housekeeping, not an error.
        fake.EnqueueDisconnect("refresh_requested");
        fake.EnqueueMessage("C0ENG", "U0ALICE", "on the new connection");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake)));

        msg!.Text.ShouldBe("on the new connection");
        fake.SocketConnections.ShouldBeGreaterThanOrEqualTo(2, "the disconnect must be answered with a fresh socket");
        fake.ConnectionsOpenCalls.ShouldBeGreaterThanOrEqualTo(2, "a reopen needs a NEW single-use wss url");
    }

    [Test]
    public async Task A_socket_that_dies_mid_stream_reconnects_and_keeps_delivering()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueMessage("C0ENG", "U0ALICE", "before the death");
        fake.AbortSocket();                                          // no close handshake at all
        fake.EnqueueMessage("C0ENG", "U0ALICE", "after the death");

        var adapter = Adapter(fake, Settings(fake));
        var received = new List<string?>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
        {
            received.Add(msg.Text);
            if (received.Count == 2)
                break;
        }

        received.ShouldBe(["before the death", "after the death"]);
        fake.SocketConnections.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task A_junk_frame_is_ignored_without_killing_the_stream()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueueEnvelope(new System.Text.Json.Nodes.JsonObject { ["type"] = "slash_commands", ["envelope_id"] = "ignored-1" });
        fake.EnqueueMessage("C0ENG", "U0ALICE", "still flowing");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake)));

        msg!.Text.ShouldBe("still flowing");
        // An unhandled envelope type is still acked, or Slack redelivers it forever.
        fake.Acks.ShouldContain("ignored-1");
    }

    // ---- send ----

    [Test]
    public async Task Send_retries_after_an_http_client_timeout()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueuePostMessageHang(TimeSpan.FromSeconds(5));   // first send stalls past the client timeout

        var result = await Adapter(fake, Settings(fake), httpTimeout: TimeSpan.FromMilliseconds(500)).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "timeout retry" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue("a send timeout is transient — retried, not rethrown as a cancellation");
        fake.PostMessageCalls.ShouldBe(2);
    }

    [Test]
    public async Task Send_honors_retry_after_on_a_429_then_succeeds()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueuePostMessageRateLimit(retryAfterSeconds: 1);

        var sw = Stopwatch.StartNew();
        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "retry me" },
            CancellationToken.None);
        sw.Stop();

        result.Ok.ShouldBeTrue();
        fake.PostMessageCalls.ShouldBe(2);
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("retry me");
        sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900), "waited ~Retry-After, not a tight loop");
    }

    [Test]
    public async Task Send_caps_a_hostile_retry_after()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.MaxRetryAfterSeconds = 0;   // a huge Retry-After must not stall the outbound path
        fake.EnqueuePostMessageRateLimit(retryAfterSeconds: 86_400);

        var sw = Stopwatch.StartNew();
        var result = await Adapter(fake, settings).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "capped" },
            CancellationToken.None);
        sw.Stop();

        result.Ok.ShouldBeTrue();
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Send_recovers_after_a_5xx_non_json_body()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueuePostMessageServerError();

        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "ok eventually" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.PostMessageCalls.ShouldBe(2);
    }

    [Test]
    public async Task Send_retries_slacks_own_transient_error()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        // Slack reports most failures as HTTP 200 + ok:false; only a few of those are retryable.
        fake.EnqueuePostMessageError("service_unavailable");

        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "transient" },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.PostMessageCalls.ShouldBe(2);
    }

    [Test]
    public async Task Send_fails_fast_on_a_permanent_error()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        fake.EnqueuePostMessageError("channel_not_found");

        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0GONE", Text = "nope" },
            CancellationToken.None);

        result.Ok.ShouldBeFalse();
        result.Error!.ShouldContain("channel_not_found");
        fake.PostMessageCalls.ShouldBe(1, "a permanent error is not worth the retry budget");
    }

    [Test]
    public async Task Send_gives_up_after_the_bounded_retry_budget()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake, sendRetries: 2);
        fake.EnqueuePostMessageServerError();
        fake.EnqueuePostMessageServerError();
        fake.EnqueuePostMessageServerError();
        fake.EnqueuePostMessageServerError();   // one more than the budget allows

        var result = await Adapter(fake, settings).SendAsync(
            new ChannelReply { Channel = "slack", ConversationId = "C0ENG", Text = "doomed" },
            CancellationToken.None);

        result.Ok.ShouldBeFalse();
        fake.PostMessageCalls.ShouldBe(3, "1 attempt + SendRetryAttempts, then it reports the failure");
    }

    [Test]
    public async Task An_upload_retries_the_whole_flow_past_a_transient_failure()
    {
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        var settings = Settings(fake);
        settings.SendRetryAttempts = 1;
        var bytes = "attachment bytes"u8.ToArray();
        // The first leg (reserving an upload URL) fails; a fresh URL must be reserved on the retry,
        // because the one-shot URL from a failed attempt is worthless.
        fake.EnqueueUploadUrlServerError();

        var result = await Adapter(fake, settings).SendAsync(
            new ChannelReply
            {
                Channel = "slack", ConversationId = "C0ENG",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File, Content = bytes, Name = "a.bin" }],
            },
            CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.UploadedFiles.ShouldHaveSingleItem().Bytes.ShouldBe(bytes);
        fake.UploadUrlCalls.ShouldBe(2);
    }
}
