using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class ChannelPreamblePresetEndpointTests(AntiphonWebAppFactory factory)
{
    [Test]
    public async Task Preset_endpoint_returns_each_known_provider_and_404_for_unknown()
    {
        using var client = factory.CreateClient();

        var defaultPreset = await client.GetAsync("/api/agents/preamble-preset");
        defaultPreset.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TemplateAsync(defaultPreset)).ShouldBe(ChannelPreamble.TelegramPresetTemplate);

        var telegram = await client.GetAsync("/api/agents/preamble-preset?provider=telegram");
        telegram.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TemplateAsync(telegram)).ShouldBe(ChannelPreamble.TelegramPresetTemplate);

        var slack = await client.GetAsync("/api/agents/preamble-preset?provider=slack");
        slack.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TemplateAsync(slack)).ShouldBe(ChannelPreamble.SlackPresetTemplate);

        var unknown = await client.GetAsync("/api/agents/preamble-preset?provider=unknown");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string?> TemplateAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("template").GetString();
    }
}
