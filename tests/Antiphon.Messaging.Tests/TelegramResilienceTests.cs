using System.Diagnostics;
using Antiphon.Messaging.Telegram;
using Antiphon.Messaging.Tests.FakeTelegram;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Hardening tests for the long-poll integration, driven via <see cref="FakeTelegramServer"/> fault injection:
/// the adapter must back off (not tight-loop) on getUpdates errors, honor <c>retry_after</c>, recover after
/// transient faults, and retry transient outbound failures while failing fast on permanent (4xx) ones.
/// </summary>
public sealed class TelegramResilienceTests
{
    private static TelegramSettings Settings(FakeTelegramServer fake, int errorBackoffSeconds = 0, int sendRetries = 2) => new()
    {
        ApiBaseUrl = fake.BaseUrl,
        BotToken = fake.BotToken,
        BotUsername = "school_revision_bot",
        LongPollTimeoutSeconds = 0,
        ErrorBackoffSeconds = errorBackoffSeconds,   // 0 → fast recovery in tests; retry_after still honored
        SendRetryAttempts = sendRetries,
    };

    private static TelegramChannelAdapter Adapter(FakeTelegramServer fake, TelegramSettings settings) =>
        new(new HttpClient(), settings, NullLogger<TelegramChannelAdapter>.Instance);

    private static async Task<ChannelMessage?> FirstMessageAsync(TelegramChannelAdapter adapter)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var msg in adapter.ReceiveAsync(cts.Token))
            return msg;
        return null;
    }

    // ---- receive ----

    [Test]
    public async Task Receive_backs_off_and_recovers_after_409_conflict()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueGetUpdatesConflict();
        fake.EnqueueTextMessage(chatId: 100, fromId: 100, text: "after conflict");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake)));

        msg.ShouldNotBeNull();
        msg!.Text.ShouldBe("after conflict");
        fake.GetUpdatesCalls.ShouldBeGreaterThanOrEqualTo(2);   // it retried past the 409 rather than giving up
    }

    [Test]
    public async Task Receive_recovers_after_5xx_non_json_body()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueGetUpdatesServerError();
        fake.EnqueueTextMessage(chatId: 101, fromId: 101, text: "after 5xx");

        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake)));

        msg.ShouldNotBeNull();
        msg!.Text.ShouldBe("after 5xx");
    }

    [Test]
    public async Task Receive_honors_retry_after_on_429()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueGetUpdatesRateLimit(retryAfterSeconds: 1);
        fake.EnqueueTextMessage(chatId: 102, fromId: 102, text: "after 429");

        var sw = Stopwatch.StartNew();
        var msg = await FirstMessageAsync(Adapter(fake, Settings(fake, errorBackoffSeconds: 0)));
        sw.Stop();

        msg.ShouldNotBeNull();
        msg!.Text.ShouldBe("after 429");
        sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));   // waited ~retry_after, not a tight loop
    }

    // ---- send ----

    [Test]
    public async Task Send_retries_on_429_then_succeeds()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueSendRateLimit(retryAfterSeconds: 0);   // transient; retried immediately

        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "100", Text = "retry me" }, CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SendCalls.ShouldBe(2);
        fake.SentMessages.ShouldHaveSingleItem().Text.ShouldBe("retry me");
    }

    [Test]
    public async Task Send_recovers_after_5xx_non_json_body()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueSendServerError();

        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "100", Text = "ok eventually" }, CancellationToken.None);

        result.Ok.ShouldBeTrue();
        fake.SendCalls.ShouldBe(2);
    }

    [Test]
    public async Task Send_fails_fast_on_400_bad_request()
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueSendBadRequest();

        var result = await Adapter(fake, Settings(fake)).SendAsync(
            new ChannelReply { Channel = "telegram", ConversationId = "100", Text = "nope" }, CancellationToken.None);

        result.Ok.ShouldBeFalse();
        fake.SendCalls.ShouldBe(1);   // 4xx is permanent — no retry
    }
}
