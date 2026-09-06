using Antiphon.Messaging.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed class GatewayMonitorValidationTests
{
    [Test]
    [Arguments("blank_group", "AntiphonConsumerGroup")]
    [Arguments("blank_topic", "InboundTopic")]
    [Arguments("self_group", "AntiphonConsumerGroup")]
    [Arguments("mismatch", "ExpectedAntiphonConsumerGroup")]
    [Arguments("case", "ExpectedAntiphonConsumerGroup")]
    [Arguments("space", "ExpectedAntiphonConsumerGroup")]
    [Arguments("required", "ExpectedAntiphonConsumerGroup")]
    [Arguments("budget", "ObservationBudgetSeconds")]
    public void Validator_rejects(string variant, string property)
    {
        var o = new AntiphonGatewayOptions { AntiphonConsumerGroup = "antiphon-server-bridge" };
        switch (variant)
        {
            case "blank_group": o.AntiphonConsumerGroup = " "; break;
            case "blank_topic": o.InboundTopic = " "; break;
            case "self_group": o.ConsumerGroup = o.AntiphonConsumerGroup; break;
            case "mismatch": o.ExpectedAntiphonConsumerGroup = "antiphon-consumer"; break;
            case "case": o.ExpectedAntiphonConsumerGroup = "Antiphon-Server-Bridge"; break;
            case "space": o.ExpectedAntiphonConsumerGroup = " antiphon-server-bridge"; break;
            case "required": o.RequireExpectedAntiphonConsumerGroup = true; break;
            case "budget": o.ObservationBudgetSeconds = 0; break;
        }
        var result = new AntiphonGatewayOptionsValidator().Validate(null, o);
        result.Failed.ShouldBeTrue(); result.FailureMessage.ShouldContain(property);
    }

    [Test]
    [Arguments("portable")]
    [Arguments("disabled")]
    [Arguments("matching")]
    public void Validator_accepts(string variant)
    {
        var o = new AntiphonGatewayOptions();
        if (variant == "disabled") { o.RequireExpectedAntiphonConsumerGroup = true; o.InboundUnconsumedMonitorEnabled = false; }
        if (variant == "matching") o.ExpectedAntiphonConsumerGroup = o.AntiphonConsumerGroup;
        new AntiphonGatewayOptionsValidator().Validate(null, o).Succeeded.ShouldBeTrue();
    }

    [Test]
    public Task Startup_fails_before_any_hosted_service_when_expected_group_mismatches() => StartupFailureAsync(false);
    [Test]
    public Task Configuration_overload_validates_the_same_rules() => StartupFailureAsync(true);

    private async Task StartupFailureAsync(bool config)
    {
        var builder = Host.CreateApplicationBuilder();
        var sentinel = new Sentinel(); builder.Services.AddSingleton<IHostedService>(sentinel);
        if (config)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:ExpectedAntiphonConsumerGroup"] = "wrong", ["Kafka:BootstrapServers"] = "127.0.0.1:1" });
            builder.Services.AddAntiphonGateway(builder.Configuration, "Kafka");
        }
        else builder.Services.AddAntiphonGateway(o => { o.ExpectedAntiphonConsumerGroup = "wrong"; o.BootstrapServers = "127.0.0.1:1"; });
        using var host = builder.Build();
        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());
        sentinel.Started.ShouldBeFalse();
    }

    [Test]
    public Task Disabled_monitor_host_starts_without_broker_and_reports_disabled() => DisabledAsync(false);
    [Test]
    public Task Enabled_monitor_without_store_is_disabled_not_ready() => DisabledAsync(true);

    private async Task DisabledAsync(bool enabled)
    {
        var builder = Host.CreateApplicationBuilder();
        var reader = new InboundUnconsumedMonitorTests.FakeOffsets();
        builder.Services.AddSingleton<IConsumerGroupObservationReader>(reader);
        builder.Services.AddAntiphonGateway(o => { o.BootstrapServers = "127.0.0.1:1"; o.InboundUnconsumedMonitorEnabled = enabled; });
        using var host = builder.Build();
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        reader.Calls.ShouldBeEmpty();
        var snapshot = host.Services.GetRequiredService<IInboundUnconsumedMonitorStatus>().GetSnapshot();
        snapshot.State.ShouldBe(MonitorState.Disabled); snapshot.HttpStatusCode.ShouldBe(200);
        if (enabled) snapshot.ReasonCode.ShouldBe("no_inbox_store");
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Sentinel : IHostedService
    {
        public bool Started { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
