using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Service;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Telegram;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection(TelegramSettings.SectionName));
builder.Services.Configure<SlackSettings>(builder.Configuration.GetSection(SlackSettings.SectionName));

builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Messaging")));

// Shared JSON options: camelCase + string enums. Used for both Kafka payloads and the HTTP API.
builder.Services.AddSingleton(MessagingJson.Options);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new TolerantStringEnumConverterFactory());
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
});

builder.Services.AddHttpClient();

// One adapter per configured channel. A single image may serve Telegram, Slack, or both.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Telegram:BotToken"]))
{
    builder.Services.AddSingleton<IChannelAdapter>(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram");
        var settings = sp.GetRequiredService<IOptions<TelegramSettings>>().Value;
        var logger = sp.GetRequiredService<ILogger<TelegramChannelAdapter>>();
        return new TelegramChannelAdapter(http, settings, logger);
    });
}

if (!string.IsNullOrWhiteSpace(builder.Configuration["Slack:BotToken"]))
{
    builder.Services.AddSingleton<IChannelAdapter>(sp =>
    {
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("slack");
        var settings = sp.GetRequiredService<IOptions<SlackSettings>>().Value;
        var logger = sp.GetRequiredService<ILogger<SlackChannelAdapter>>();
        return new SlackChannelAdapter(http, settings, logger);
    });
}

// Ingress + outbound loops live in Antiphon.Messaging.Gateway. Section name "Kafka" keeps the
// deployed env vars (Kafka__BootstrapServers, Kafka__ConsumerGroup, …) working.
builder.Services.AddAntiphonGateway(builder.Configuration, "Kafka");
builder.Services.AddHostedService<InboxConsumerService>();

var app = builder.Build();

var registeredChannels = app.Services.GetServices<IChannelAdapter>().Select(adapter => adapter.Channel).ToArray();
if (registeredChannels.Length == 0)
    app.Logger.LogWarning("No messaging channel adapters are registered; configure Telegram:BotToken and/or Slack:BotToken.");
else
    // string.Join, NOT the array: LogInformation takes `params object?[]`, and a string[] binds AS
    // that params array — so {Channels} would render only registeredChannels[0] and silently drop
    // the rest. Measured live 2026-08-21: a gateway running BOTH adapters logged "registered: telegram",
    // which reads exactly like Slack failed to register (it hadn't — see docs/slack-bot-ops.md).
    app.Logger.LogInformation("Messaging channel adapters registered: {Channels}", string.Join(", ", registeredChannels));

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<MessagingDbContext>().Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Connected channels and what each supports.
app.MapGet("/api/channels", (IEnumerable<IChannelAdapter> adapters) =>
    Results.Ok(adapters.Select(a => a.Capabilities)));

// The "things we can reply to" list.
app.MapGet("/api/channels/messages", async (
    MessagingDbContext db, string? status, string? channel, int? limit, CancellationToken ct) =>
{
    IQueryable<InboxMessage> query = db.Inbox;
    if (Enum.TryParse<InboxStatus>(status, ignoreCase: true, out var parsed))
        query = query.Where(x => x.Status == parsed);
    if (!string.IsNullOrWhiteSpace(channel))
        query = query.Where(x => x.Channel == channel);

    var items = await query
        .OrderByDescending(x => x.ReceivedAt)
        .Take(Math.Clamp(limit ?? 50, 1, 500))
        .Select(x => new InboxSummary(
            x.Id, x.Channel, x.ConversationId, x.ConversationTitle, x.AuthorDisplay,
            x.Text, x.MentionsMe, x.HasAttachments, x.Status.ToString(), x.ReceivedAt))
        .ToListAsync(ct);

    return Results.Ok(items);
});

// One message, including the full envelope (raw payload included).
app.MapGet("/api/channels/messages/{id:guid}", async (
    Guid id, MessagingDbContext db, JsonSerializerOptions json, CancellationToken ct) =>
{
    var item = await db.Inbox.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (item is null)
        return Results.NotFound();

    var envelope = JsonSerializer.Deserialize<JsonElement>(item.EnvelopeJson, json);
    return Results.Ok(new
    {
        item.Id,
        item.Channel,
        status = item.Status.ToString(),
        item.ReceivedAt,
        item.AnsweredAt,
        envelope,
    });
});

// Reply to a message: enqueue on the outbound topic and mark answered.
app.MapPost("/api/channels/messages/{id:guid}/reply", async (
    Guid id, ReplyRequest body, MessagingDbContext db, IProducer<string, string> producer,
    IOptions<AntiphonGatewayOptions> kafka, JsonSerializerOptions json, CancellationToken ct) =>
{
    var item = await db.Inbox.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (item is null)
        return Results.NotFound();

    var reply = new ChannelReply
    {
        Channel = item.Channel,
        ReplyHandle = item.ReplyHandle,
        ConversationId = item.ConversationId,
        ReplyToMessageId = body.ReplyToMessageId,
        Text = body.Text,
        RawOverrides = body.RawOverrides,
    };

    await producer.ProduceAsync(
        kafka.Value.ResolveOutboundTopic(),
        new Message<string, string> { Key = item.ConversationId, Value = JsonSerializer.Serialize(reply, json) },
        ct);

    item.Status = InboxStatus.Answered;
    item.AnsweredAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);

    return Results.Accepted();
});

// Mark a message handled without replying.
app.MapPost("/api/channels/messages/{id:guid}/ack", async (Guid id, MessagingDbContext db, CancellationToken ct) =>
{
    var item = await db.Inbox.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (item is null)
        return Results.NotFound();

    item.Status = InboxStatus.Answered;
    item.AnsweredAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);

    return Results.NoContent();
});

app.Run();

internal sealed record InboxSummary(
    Guid Id, string Channel, string ConversationId, string? ConversationTitle, string? AuthorDisplay,
    string? Text, bool MentionsMe, bool HasAttachments, string Status, DateTimeOffset ReceivedAt);

internal sealed record ReplyRequest(string? Text, string? ReplyToMessageId, JsonElement? RawOverrides);
