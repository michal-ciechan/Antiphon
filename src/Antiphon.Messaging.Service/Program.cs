using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Messaging;
using Antiphon.Messaging.Service;
using Antiphon.Messaging.Telegram;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection(KafkaSettings.SectionName));
builder.Services.Configure<TelegramSettings>(builder.Configuration.GetSection(TelegramSettings.SectionName));

builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Messaging")));

// Shared JSON options: camelCase + string enums. Used for both Kafka payloads and the HTTP API.
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() },
};
builder.Services.AddSingleton(jsonOptions);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHttpClient();

// One adapter per channel. Telegram for now; WhatsApp/Teams register the same way.
builder.Services.AddSingleton<IChannelAdapter>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("telegram");
    var settings = sp.GetRequiredService<IOptions<TelegramSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<TelegramChannelAdapter>>();
    return new TelegramChannelAdapter(http, settings, logger);
});

builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var kafka = sp.GetRequiredService<IOptions<KafkaSettings>>().Value;
    return new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
});

builder.Services.AddHostedService<TelegramIngressService>();
builder.Services.AddHostedService<InboxConsumerService>();
builder.Services.AddHostedService<OutboundConsumerService>();

var app = builder.Build();

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
    IOptions<KafkaSettings> kafka, JsonSerializerOptions json, CancellationToken ct) =>
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
        kafka.Value.OutboundTopic,
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
