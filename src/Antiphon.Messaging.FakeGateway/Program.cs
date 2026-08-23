using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.Messaging;
using Antiphon.Messaging.FakeGateway;
using Antiphon.Messaging.Gateway;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Antiphon FAKE messaging gateway (spec: 2026-07-20-always-on-agents-and-alerting.md, Q9).
// Real Kafka in, no real Telegram out: records every would-be delivery for assertions, injects
// synthetic inbound messages, and simulates outages. Local dev + integration tests ONLY —
// deployed environments run the real Antiphon.Messaging.Service.
//
// Ingress and outbound Kafka loops come from Antiphon.Messaging.Gateway. This process is a
// thin host around FakeChannelAdapter (telegram + slack) plus the HTTP assertion/inject API.
// ─────────────────────────────────────────────────────────────────────────────────────────────

// appsettings.json here exists ONLY to turn Microsoft.AspNetCore down to Warning (CARD-0043).
// With no settings file at all this ran at the framework default of Information, and the AppHost's
// 30-second /health poll (plus the Aspire dashboard's own, much faster, one while it is open) wrote
// three request-lifecycle lines each: measured, 99.6% of the 57 MB logs/fake-gateway.log was those
// three sources and 130,072 health polls. The recorder's own logs stay at Information.
var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.Configuration["AntiphonMessaging:BootstrapServers"] ?? "localhost:19092";
var inboundTopic = builder.Configuration["AntiphonMessaging:InboundTopic"] ?? "channels.inbound";
var outboundTopic = builder.Configuration["AntiphonMessaging:OutboundTopic"] ?? "channels.outbound";
var jsonlPath = builder.Configuration["FakeGateway:DeliveryLog"]
    ?? Path.Combine("logs", "fake-gateway", "outbound.jsonl");

builder.Services.AddSingleton(new DeliveryStore(jsonlPath));
builder.Services.AddSingleton<PauseState>();
builder.Services.AddSingleton(sp =>
{
    var store = sp.GetRequiredService<DeliveryStore>();
    var pause = sp.GetRequiredService<PauseState>();
    var logger = sp.GetRequiredService<ILogger<FakeChannelAdapter>>();
    return new FakeChannelHub(
        new FakeChannelAdapter("telegram", store, pause, logger),
        new FakeChannelAdapter("slack", store, pause, logger));
});
builder.Services.AddSingleton<IChannelAdapter>(sp =>
    sp.GetRequiredService<FakeChannelHub>().Adapters.Single(a => a.Channel == "telegram"));
builder.Services.AddSingleton<IChannelAdapter>(sp =>
    sp.GetRequiredService<FakeChannelHub>().Adapters.Single(a => a.Channel == "slack"));

builder.Services.AddAntiphonGateway(o =>
{
    o.BootstrapServers = bootstrap;
    o.InboundTopic = inboundTopic;
    o.OutboundTopic = outboundTopic;
    o.ConsumerGroup = "antiphon-fake-gateway";
    // Latest: a restart must not replay the outbound topic into /deliveries. Production
    // gateways keep the library default (Earliest) so a new group does not skip replies.
    o.AutoOffsetReset = "Latest";
});

var app = builder.Build();

var assembly = typeof(Program).Assembly;
var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
var revisionMatch = Regex.Match(informationalVersion, @"\+([0-9a-f]{40})(?:$|[.+])", RegexOptions.IgnoreCase);
var build = new
{
    informationalVersion,
    commitSha = revisionMatch.Success ? revisionMatch.Groups[1].Value : null,
    assemblyWriteTimeUtc = File.GetLastWriteTimeUtc(assembly.Location),
    processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
};

app.MapGet("/health", () => Results.Ok(new { ok = true, bootstrap, inboundTopic, outboundTopic }));
app.MapGet("/capabilities", () => Results.Ok(new { build }));

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
app.MapPost("/inbound", async (InjectInboundRequest request, FakeChannelHub hub, ILogger<Program> logger, CancellationToken ct) =>
{
    var channel = request.Channel ?? "telegram";
    if (!hub.TryGet(channel, out var adapter))
        return Results.BadRequest(new { error = $"no fake adapter for channel '{channel}'", known = hub.Channels });

    var message = new ChannelMessage
    {
        Id = Guid.NewGuid().ToString("n"),
        Channel = channel,
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
        Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
    };

    try
    {
        await adapter.InjectAsync(message, ct);
    }
    catch (TimeoutException)
    {
        return Results.Json(new { error = "ingress did not accept the message in time" }, statusCode: 503);
    }

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
