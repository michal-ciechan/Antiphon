using System.Diagnostics;
using System.Text.Json;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[Category("Integration")]
public class ChannelConsumerIdentityEndpointTests
{
    [Test]
    public async Task Returns_effective_overrides_and_only_allowlisted_fields()
    {
        await using var factory = new ChannelConsumerIdentityWebAppFactory();
        using var client = factory.CreateClient();
        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync("/api/channels/consumer");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        var root = body.RootElement;
        root.EnumerateObject().Select(p => p.Name).Order().ShouldBe(
            new[] { "brokers", "consumerGroup", "enabled", "inboundTopic" });
        root.GetProperty("consumerGroup").GetString().ShouldBe("c0410-override-group");
        root.GetProperty("inboundTopic").GetString().ShouldBe("channels.inbound");
        root.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        var brokers = root.GetProperty("brokers");
        brokers.GetArrayLength().ShouldBe(3);
        brokers[0].GetProperty("host").GetString().ShouldBe("c0410-a.invalid");
        brokers[0].GetProperty("port").GetInt32().ShouldBe(19092);
        brokers[1].GetProperty("host").GetString().ShouldBe("c0410-b.invalid");
        brokers[1].GetProperty("port").GetInt32().ShouldBe(19093);
        brokers[2].GetProperty("host").GetString().ShouldBe("noport.invalid");
        brokers[2].GetProperty("port").ValueKind.ShouldBe(JsonValueKind.Null);
        factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
    }

    [Test]
    public void Dto_reflects_enabled_flag_without_touching_kafka()
    {
        foreach (var enabled in new[] { true, false })
            ChannelConsumerIdentityDto.From(new(), new() { Enabled = enabled }).Enabled.ShouldBe(enabled);
    }
}

public sealed class ChannelConsumerIdentityWebAppFactory : AntiphonWebAppFactory
{
    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.PostConfigure<AntiphonMessagingOptions>(o =>
        {
            o.ConsumerGroup = "c0410-override-group";
            o.BootstrapServers = "c0410-a.invalid:19092, kafka://c0410-b.invalid:19093,noport.invalid";
        });
    }
}
