using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Messaging;
using Antiphon.Messaging.FakeGateway;
using Confluent.Kafka;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Antiphon FAKE messaging gateway (spec: 2026-07-20-always-on-agents-and-alerting.md, Q9).
// Real Kafka in, no real Telegram out: records every would-be delivery for assertions, injects
// synthetic inbound messages, and simulates outages. Local dev + integration tests ONLY —
// deployed environments run the real Antiphon.Messaging.Service.
// ─────────────────────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.Configuration["AntiphonMessaging:BootstrapServers"] ?? "localhost:19092";
var inboundTopic = builder.Configuration["AntiphonMessaging:InboundTopic"] ?? "channels.inbound";
var outboundTopic = builder.Configuration["AntiphonMessaging:OutboundTopic"] ?? "channels.outbound";
var jsonlPath = builder.Configuration["FakeGateway:DeliveryLog"]
    ?? Path.Combine("logs", "fake-gateway", "outbound.jsonl");

// Same wire shape the real client/gateway use: camelCase + string enums.
var wireJson = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
};

builder.Services.AddSingleton(new DeliveryStore(jsonlPath));
builder.Services.AddSingleton<PauseState>();
builder.Services.AddSingleton(wireJson);
builder.Services.AddHostedService(sp => new OutboundRecorderService(
    sp.GetRequiredService<DeliveryStore>(),
    sp.GetRequiredService<PauseState>(),
    wireJson,
    bootstrap,
    outboundTopic,
    sp.GetRequiredService<ILogger<OutboundRecorderService>>()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { ok = true, bootstrap, inboundTopic, outboundTopic }));

// ── Assertions ───────────────────────────────────────────────────────────────────────────────
app.MapGet("/deliveries", (DeliveryStore store, long? since, string? channel, string? conversationId) =>
    Results.Ok(store.Query(since, channel, conversationId)));

app.MapDelete("/deliveries", (DeliveryStore store) => Results.Ok(new { cleared = store.Reset() }));

// ── Outage simulation ────────────────────────────────────────────────────────────────────────
app.MapPost("/pause", (PauseState pause) =>
{
    pause.Pause();
    return Results.Ok(new { paused = true });
});
app.MapPost("/resume", (PauseState pause) =>
{
    pause.Resume();
    return Results.Ok(new { paused = false });
});

// ── Inbound injection: drive the whole bridge path without any external service ─────────────
app.MapPost("/inbound", async (InjectInboundRequest request, ILogger<Program> logger) =>
{
    var message = new ChannelMessage
    {
        Id = Guid.NewGuid().ToString("n"),
        Channel = request.Channel ?? "telegram",
        ChannelMessageId = DateTime.UtcNow.Ticks.ToString(),
        Conversation = new Conversation
        {
            Id = request.ChatId,
            Kind = request.Kind ?? ConversationKind.Group,
            Title = request.Title,
        },
        Author = new Participant
        {
            Id = request.AuthorId ?? "1001",
            Username = request.Username,
            DisplayName = request.Username ?? "fake-user",
        },
        Timestamp = DateTimeOffset.UtcNow,
        Text = request.Text,
        ReplyHandle = request.ChatId,
        Raw = JsonDocument.Parse("{}").RootElement.Clone(),
    };

    var config = new ProducerConfig { BootstrapServers = bootstrap };
    using var producer = new ProducerBuilder<string, string>(config).Build();
    await producer.ProduceAsync(inboundTopic, new Message<string, string>
    {
        Key = request.ChatId,
        Value = JsonSerializer.Serialize(message, wireJson),
    });

    logger.LogInformation("Injected inbound {Channel} message into {Topic} for chat {ChatId}",
        message.Channel, inboundTopic, request.ChatId);
    return Results.Ok(new { injected = message.Id, chatId = request.ChatId });
});

app.Run();

internal sealed record InjectInboundRequest(
    string ChatId,
    string Text,
    string? Channel = null,
    ConversationKind? Kind = null,
    string? Username = null,
    string? AuthorId = null,
    string? Title = null);

/// <summary>Consumes channels.outbound and records every reply (honoring the pause switch).</summary>
internal sealed class OutboundRecorderService(
    DeliveryStore store,
    PauseState pause,
    JsonSerializerOptions wireJson,
    string bootstrap,
    string outboundTopic,
    ILogger<OutboundRecorderService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private void ConsumeLoop(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "antiphon-fake-gateway",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var consumer = new ConsumerBuilder<string, string>(config).Build();
                consumer.Subscribe(outboundTopic);
                logger.LogInformation("Fake gateway consuming {Topic} at {Bootstrap}", outboundTopic, bootstrap);

                while (!ct.IsCancellationRequested)
                {
                    if (pause.Paused)
                    {
                        // Simulated outage: stop polling entirely, like a dead gateway process.
                        Thread.Sleep(250);
                        continue;
                    }

                    var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (result?.Message?.Value is null)
                        continue;

                    try
                    {
                        var reply = JsonSerializer.Deserialize<ChannelReply>(result.Message.Value, wireJson);
                        if (reply is not null)
                        {
                            var recorded = store.Record(reply, DateTime.UtcNow);
                            logger.LogInformation(
                                "Recorded delivery #{Seq}: [{Channel}/{Conversation}] {Text}",
                                recorded.Seq, recorded.Channel, recorded.ConversationId,
                                Truncate(recorded.Text, 120));
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Unparseable outbound message skipped");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Broker down: keep retrying quietly — the fake must be as forgiving as dev needs.
                logger.LogWarning(ex, "Fake gateway consume loop failed; retrying in 5s");
                for (var i = 0; i < 10 && !ct.IsCancellationRequested; i++)
                    Thread.Sleep(500);
            }
        }
    }

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max] + "…";
}
