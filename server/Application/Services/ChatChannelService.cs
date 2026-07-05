using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CRUD + inbound-upsert for <see cref="ChatChannel"/> rows — the catalog of external conversations
/// (Telegram today; WhatsApp/Discord later) and their channel → agent routing. Channels are never
/// created by hand: they appear when the bridge sees their first inbound message.
/// </summary>
public sealed class ChatChannelService
{
    private const int PreviewMaxChars = 200;

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ChatChannelService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ChatChannelDto>> GetAllAsync(CancellationToken ct)
    {
        return await _db.ChatChannels
            .AsNoTracking()
            .Include(c => c.Agent)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => ToDto(c))
            .ToListAsync(ct);
    }

    public async Task<ChatChannelDto> UpdateAsync(Guid id, UpdateChatChannelRequest request, CancellationToken ct)
    {
        var channel = await _db.ChatChannels
            .Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(ChatChannel), id);

        if (request.UnbindAgent)
        {
            channel.AgentId = null;
            channel.Agent = null;
        }
        else if (request.AgentId is Guid agentId)
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct)
                ?? throw new NotFoundException(nameof(Agent), agentId);
            channel.AgentId = agent.Id;
            channel.Agent = agent;
        }

        if (request.Enabled is bool enabled)
            channel.Enabled = enabled;

        channel.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        return ToDto(channel);
    }

    /// <summary>
    /// Record an inbound message against its channel, creating the row on first sight.
    /// Returns the tracked entity (with <see cref="ChatChannel.Agent"/> loaded) plus whether this exact
    /// message was already recorded (Kafka redelivery) — duplicates must not be routed twice.
    /// </summary>
    public async Task<(ChatChannel Channel, bool IsDuplicate)> UpsertFromInboundAsync(
        ChannelMessage message, CancellationToken ct)
    {
        var now = UtcNow();
        var channel = await _db.ChatChannels
            .Include(c => c.Agent)
            .FirstOrDefaultAsync(
                c => c.Provider == message.Channel && c.ExternalId == message.Conversation.Id, ct);

        if (channel is not null && channel.LastChannelMessageId == message.ChannelMessageId)
            return (channel, true);

        if (channel is null)
        {
            channel = new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = message.Channel,
                ExternalId = message.Conversation.Id,
                CreatedAt = now,
            };
            _db.ChatChannels.Add(channel);
        }

        channel.Kind = MapKind(message.Conversation.Kind);
        if (!string.IsNullOrWhiteSpace(message.Conversation.Title))
            channel.Title = message.Conversation.Title;
        channel.ReplyHandle = message.ReplyHandle;
        channel.LastChannelMessageId = message.ChannelMessageId;
        channel.LastMessageAt = message.Timestamp.UtcDateTime;
        channel.LastMessagePreview = Truncate(message.Text);
        channel.LastAuthor = message.Author.DisplayName ?? message.Author.Username ?? message.Author.Id;
        channel.MessageCount++;
        channel.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return (channel, false);
    }

    private static ChatChannelKind MapKind(ConversationKind kind) => kind switch
    {
        ConversationKind.Direct => ChatChannelKind.Direct,
        ConversationKind.Group => ChatChannelKind.Group,
        _ => ChatChannelKind.Broadcast,
    };

    private static string? Truncate(string? text) =>
        text is { Length: > PreviewMaxChars } ? text[..PreviewMaxChars] : text;

    private static ChatChannelDto ToDto(ChatChannel c) => new(
        c.Id, c.Provider, c.ExternalId, c.Kind, c.Title,
        c.AgentId, c.Agent?.Name, c.Enabled,
        c.LastMessageAt, c.LastMessagePreview, c.LastAuthor, c.MessageCount, c.CreatedAt);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
