using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Messaging.Tests.FakeTelegram;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Conformance;

/// <summary>
/// The one contract that needs a real live chat: a *successful* sendMessage. A bot may only message a
/// user who has started it, so this discovers a chat from getUpdates (the fake's enqueued /start, or —
/// when <c>ANTIPHON_TG_TEST_TOKEN</c> is set — a real /start sent to @antiphon_test_bot) and delivers to it.
///
/// The fake leg always runs. The real leg runs only when a token is set AND a live chat exists; if no one
/// has messaged the test bot yet it logs and skips (so CI stays green without a prepared chat). When both
/// run, identical assertions prove the fake's sendMessage-success shape matches real Telegram's.
/// </summary>
public sealed class TelegramLiveChatConformanceTests
{
    private static readonly HttpClient Http = new();
    private const string RealBaseUrl = "https://api.telegram.org";
    private static string? RealToken => Environment.GetEnvironmentVariable("ANTIPHON_TG_TEST_TOKEN");

    [Test]
    public async Task sendMessage_to_a_live_chat_succeeds_on_fake_and_real()
    {
        // ---- FAKE: an inbound /start establishes a chat; sending back to it succeeds. ----
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        fake.EnqueueTextMessage(chatId: 4242, fromId: 4242, text: "/start");
        var fakeChat = await DiscoverChatIdAsync(fake.BaseUrl, fake.BotToken);
        fakeChat.ShouldBe("4242");
        AssertSent(await SendAsync(fake.BaseUrl, fake.BotToken, fakeChat!, "conformance: hello (fake)"), fakeChat!);

        // ---- REAL (gated): discover the live chat and deliver for real. ----
        if (RealToken is not { Length: > 0 } token) return;     // no token → fake-only
        var realChat = await DiscoverChatIdAsync(RealBaseUrl, token);
        if (realChat is null)
        {
            Console.WriteLine("[live-chat] no pending chat for the test bot — send /start to @antiphon_test_bot, then re-run; skipping the real send.");
            return;
        }
        AssertSent(
            await SendAsync(RealBaseUrl, token, realChat, "Antiphon conformance ✅ — fake == real (sendMessage success)"),
            realChat);
    }

    private static void AssertSent(JsonNode r, string chatId)
    {
        r["ok"]!.GetValue<bool>().ShouldBeTrue();
        r["result"]!["message_id"].ShouldNotBeNull();
        r["result"]!["chat"]!["id"]!.GetValue<long>().ToString().ShouldBe(chatId);
    }

    /// <summary>Most recent update's message.chat.id, or null if there are no pending message updates.</summary>
    private static async Task<string?> DiscoverChatIdAsync(string baseUrl, string token)
    {
        using var resp = await Http.GetAsync($"{baseUrl}/bot{token}/getUpdates?timeout=0");
        var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        if (!root["ok"]!.GetValue<bool>()) return null;
        var updates = root["result"]!.AsArray();
        for (var i = updates.Count - 1; i >= 0; i--)
            if (updates[i]?["message"]?["chat"]?["id"] is { } id)
                return id.GetValue<long>().ToString();
        return null;
    }

    private static async Task<JsonNode> SendAsync(string baseUrl, string token, string chatId, string text)
    {
        var body = new JsonObject { ["chat_id"] = long.Parse(chatId), ["text"] = text };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync($"{baseUrl}/bot{token}/sendMessage", content);
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
    }
}
