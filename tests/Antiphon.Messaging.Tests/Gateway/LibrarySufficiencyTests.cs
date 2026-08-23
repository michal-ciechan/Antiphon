using Antiphon.Messaging.FakeGateway;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed class LibrarySufficiencyTests
{
    [Test]
    public void Service_has_no_hand_rolled_ingress_or_outbound_loops()
    {
        var serviceDir = Path.Combine(RepoRoot, "src", "Antiphon.Messaging.Service");
        File.Exists(Path.Combine(serviceDir, "ChannelIngressService.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(serviceDir, "OutboundConsumerService.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(serviceDir, "KafkaSettings.cs")).ShouldBeFalse();

        var program = File.ReadAllText(Path.Combine(serviceDir, "Program.cs"));
        program.ShouldContain("AddAntiphonGateway");
        program.ShouldContain("\"Kafka\"");
        program.ShouldNotContain("ChannelIngressService");
        program.ShouldNotContain("OutboundConsumerService");
    }

    [Test]
    public void FakeGateway_has_no_Confluent_Kafka_usage_of_its_own()
    {
        var fakeDir = Path.Combine(RepoRoot, "src", "Antiphon.Messaging.FakeGateway");
        foreach (var file in Directory.GetFiles(fakeDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("using Confluent.Kafka");
            text.ShouldNotContain("ProducerBuilder");
            text.ShouldNotContain("ConsumerBuilder");
            text.ShouldNotContain("ProducerConfig");
            text.ShouldNotContain("ConsumerConfig");
        }

        var csproj = File.ReadAllText(Path.Combine(fakeDir, "Antiphon.Messaging.FakeGateway.csproj"));
        csproj.ShouldNotContain("Confluent.Kafka");
        csproj.ShouldContain("Antiphon.Messaging.Gateway");
    }

    [Test]
    public void EchoGateway_sample_is_a_library_hosted_adapter_not_a_hand_rolled_loop()
    {
        var sampleDir = Path.Combine(RepoRoot, "samples", "EchoGateway");
        Directory.Exists(sampleDir).ShouldBeTrue();

        var program = File.ReadAllText(Path.Combine(sampleDir, "Program.cs"));
        program.ShouldContain("AddAntiphonGateway");
        program.ShouldContain("EchoChannelAdapter");
        program.ShouldNotContain("ProducerBuilder");
        program.ShouldNotContain("ConsumerBuilder");
        program.ShouldNotContain("using Confluent.Kafka");

        var adapter = File.ReadAllText(Path.Combine(sampleDir, "EchoChannelAdapter.cs"));
        adapter.ShouldContain("IChannelAdapter");
        adapter.ShouldContain("ChannelKey = \"echo\"");
        adapter.ShouldContain("IsSelf = false");

        var csproj = File.ReadAllText(Path.Combine(sampleDir, "EchoGateway.csproj"));
        csproj.ShouldContain("Antiphon.Messaging.Gateway");
        csproj.ShouldContain("UsePublishedPackages");
        csproj.ShouldNotContain("Confluent.Kafka");
    }

    [Test]
    public async Task FakeChannelAdapter_inject_yields_on_ReceiveAsync_and_send_records()
    {
        var store = new DeliveryStore(jsonlPath: null);
        var pause = new PauseState();
        var adapter = new FakeChannelAdapter("telegram", store, pause, NullLogger<FakeChannelAdapter>.Instance);
        var message = new ChannelMessage
        {
            Id = "id-1",
            Channel = "telegram",
            ChannelMessageId = "1",
            Conversation = new Conversation { Id = "chat-1", Kind = ConversationKind.Group },
            Author = new Participant { Id = "user-1" },
            Timestamp = DateTimeOffset.UnixEpoch,
            Text = "hello",
            ReplyHandle = "chat-1",
            Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inject = adapter.InjectAsync(message, cts.Token);

        ChannelMessage? received = null;
        await foreach (var item in adapter.ReceiveAsync(cts.Token))
        {
            received = item;
            break;
        }

        await inject;
        received.ShouldBe(message);

        var send = await adapter.SendAsync(new ChannelReply { Channel = "telegram", ConversationId = "chat-1", Text = "pong" }, cts.Token);
        send.Ok.ShouldBeTrue();
        var recorded = store.Query(null, "telegram", "chat-1").ShouldHaveSingleItem();
        recorded.Text.ShouldBe("pong");
    }

    [Test]
    public async Task FakeChannelAdapter_send_waits_while_paused()
    {
        var store = new DeliveryStore(jsonlPath: null);
        var pause = new PauseState();
        var adapter = new FakeChannelAdapter("slack", store, pause, NullLogger<FakeChannelAdapter>.Instance);
        pause.Pause();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var send = adapter.SendAsync(new ChannelReply { Channel = "slack", Text = "held" }, cts.Token);
        await Task.Delay(80);
        send.IsCompleted.ShouldBeFalse();
        store.Query(null, null, null).ShouldBeEmpty();

        pause.Resume();
        var result = await send;
        result.Ok.ShouldBeTrue();
        store.Query(null, "slack", null).ShouldHaveSingleItem().Text.ShouldBe("held");
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
