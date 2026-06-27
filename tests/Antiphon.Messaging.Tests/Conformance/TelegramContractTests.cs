using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Messaging.Tests.FakeTelegram;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Conformance;

/// <summary>
/// Conformance ("verified fake") tests: each contract is asserted against BOTH the fake and — when a
/// real bot token is supplied via <c>ANTIPHON_TG_TEST_TOKEN</c> — real Telegram. If the fake ever drifts
/// from real Telegram on a faked endpoint, the corresponding test fails, so the offline integration
/// tests can trust the fake.
///
/// These cover the endpoints that need no live chat (getMe / getUpdates-empty / invalid-token /
/// sendMessage-error / deleteWebhook). The sendMessage-*success* round-trip against real Telegram needs
/// a live chat and is driven separately via Playwright (see TelegramLiveChatConformanceTests).
/// </summary>
public sealed class TelegramContractTests
{
    private static readonly HttpClient Http = new();
    private const string RealBaseUrl = "https://api.telegram.org";
    private static string? RealToken => Environment.GetEnvironmentVariable("ANTIPHON_TG_TEST_TOKEN");

    private static string Url(string baseUrl, string token, string method) => $"{baseUrl}/bot{token}/{method}";

    private async Task ForFakeAndReal(Func<string, string, Task> assertContract)
    {
        await using var fake = new FakeTelegramServer();
        await fake.StartAsync();
        await assertContract(fake.BaseUrl, fake.BotToken);          // fake — always

        if (RealToken is { Length: > 0 } token)
            await assertContract(RealBaseUrl, token);               // real — when a token is provided
    }

    [Test]
    public async Task getMe_returns_ok_with_a_bot_identity() =>
        await ForFakeAndReal(async (baseUrl, token) =>
        {
            var r = await GetJson(Url(baseUrl, token, "getMe"));
            r["ok"]!.GetValue<bool>().ShouldBeTrue();
            r["result"]!["is_bot"]!.GetValue<bool>().ShouldBeTrue();
            r["result"]!["username"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        });

    [Test]
    public async Task getUpdates_returns_the_ok_result_array_envelope() =>
        await ForFakeAndReal(async (baseUrl, token) =>
        {
            var r = await GetJson(Url(baseUrl, token, "getUpdates") + "?timeout=0");
            // Envelope conformance: on success ok:true + result is an array; if another consumer is
            // already long-polling this token (real prod bot), Telegram returns ok:false + error_code 409.
            // Both are valid real-Telegram shapes — assert the envelope, not exclusivity.
            if (r["ok"]!.GetValue<bool>())
                r["result"].ShouldBeOfType<JsonArray>();
            else
                r["error_code"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(400);
        });

    [Test]
    public async Task deleteWebhook_returns_ok_true() =>
        await ForFakeAndReal(async (baseUrl, token) =>
        {
            var r = await GetJson(Url(baseUrl, token, "deleteWebhook"));
            r["ok"]!.GetValue<bool>().ShouldBeTrue();
            r["result"]!.GetValue<bool>().ShouldBeTrue();
        });

    [Test]
    public async Task invalid_token_returns_ok_false_401() =>
        await ForFakeAndReal(async (baseUrl, _) =>
        {
            // Wrong token against the same base url — fake and real both reject with 401/ok:false.
            var r = await GetJson(Url(baseUrl, "0000000000:DEFINITELY-NOT-A-REAL-TOKEN-000000000", "getMe"));
            r["ok"]!.GetValue<bool>().ShouldBeFalse();
            r["error_code"]!.GetValue<int>().ShouldBe(401);
            r["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        });

    [Test]
    public async Task sendMessage_to_empty_chat_returns_ok_false_with_error() =>
        await ForFakeAndReal(async (baseUrl, token) =>
        {
            // chat_id 0 is never a valid chat — both fake and real return ok:false with an error_code.
            var r = await PostJson(Url(baseUrl, token, "sendMessage"), new JsonObject { ["chat_id"] = 0, ["text"] = "x" });
            r["ok"]!.GetValue<bool>().ShouldBeFalse();
            r["error_code"]!.GetValue<int>().ShouldBeGreaterThanOrEqualTo(400);
            r["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        });

    private static async Task<JsonNode> GetJson(string url)
    {
        using var resp = await Http.GetAsync(url);
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
    }

    private static async Task<JsonNode> PostJson(string url, JsonObject body)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync(url, content);
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
    }
}
