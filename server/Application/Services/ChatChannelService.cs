using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

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
    private readonly IAntiphonMessagingProducer _producer;
    private readonly ChannelOutboundSettings _outbound;
    private readonly ChannelOutboundService? _outboundSender;

    public ChatChannelService(AppDbContext db, TimeProvider timeProvider, IAntiphonMessagingProducer producer,
        IOptions<ChannelOutboundSettings>? outbound = null, ChannelOutboundService? outboundSender = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _producer = producer;
        _outbound = outbound?.Value ?? new ChannelOutboundSettings();
        _outboundSender = outboundSender;
    }

    public async Task<IReadOnlyList<ChatChannelDto>> GetAllAsync(CancellationToken ct)
    {
        var channels = await _db.ChatChannels
            .AsNoTracking()
            .Include(c => c.Agent)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(ct);
        return channels.Select(ToDto).ToList();
    }

    public IReadOnlyList<ChannelOutboundProfileDto> GetOutboundProfiles() =>
        _outbound.Profiles.Keys.OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => Preview(n)!).ToList();

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
            channel.OutboundAgentProfile = null;
        }
        else if (request.AgentId is Guid agentId)
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct)
                ?? throw new NotFoundException(nameof(Agent), agentId);
            // Kind refusal still runs here so a herdr×OpenCode/Raw pair cannot be created by bind.
            // AlwaysOn / channel-bound arms were lifted (CARD-0186); Grok/Codex by CARD-0187.
            AgentService.ValidateSessionBackendPairing(agent.SessionBackend, agent.Kind);

            var bindingChanged = channel.AgentId != agent.Id;
            channel.AgentId = agent.Id;
            channel.Agent = agent;
            if (bindingChanged)
                channel.OutboundAgentProfile = null;
        }

        if (request.ClearOutboundAgentProfile && request.OutboundAgentProfile is not null)
            throw new ValidationException(nameof(request.OutboundAgentProfile),
                "Set or clear the outbound profile, not both.");
        if (request.ClearOutboundAgentProfile)
            channel.OutboundAgentProfile = null;
        else if (request.OutboundAgentProfile is { } profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ValidationException(nameof(request.OutboundAgentProfile), "Profile name is required.");
            channel.OutboundAgentProfile = profileName;
        }

        if (request.Enabled is bool enabled)
            channel.Enabled = enabled;

        if (channel.OutboundAgentProfile is not null &&
            (request.OutboundAgentProfile is not null || request.AgentId is not null))
            await ValidateOutboundBindingAsync(_db, _outbound, channel, ct);

        if (request.ClearAlertMinSeverity)
            channel.AlertMinSeverity = null;
        else if (request.AlertMinSeverity is { } alertMinSeverity)
            channel.AlertMinSeverity = alertMinSeverity;

        if (request.DigestEnabled is bool digestEnabled)
            channel.DigestEnabled = digestEnabled;

        channel.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        return ToDto(channel);
    }

    /// <summary>
    /// Sends a proactive, out-of-band message to a channel — the caller (a scheduled job, an
    /// operator script) initiates it, not an inbound message. This bypasses the alert
    /// throttle/digest path entirely (<see cref="ChannelAlertRouter"/>/<c>AlertDigestFlusher</c>):
    /// no alert row, no severity gate, one send per call. A disabled channel refuses
    /// - Enabled=false means routing is deliberately off, and this must respect that the same way
    /// inbound routing already does, not offer a side door around it.
    /// </summary>
    public async Task SendAsync(Guid id, string text, CancellationToken ct)
        => await SendAsync(id, text, options: null, ct);

    /// <summary>Targeted send options for server-composed channel messages.</summary>
    public async Task SendAsync(Guid id, string text, ChannelSendOptions? options, CancellationToken ct)
    {
        var channel = await _db.ChatChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(ChatChannel), id);

        if (!channel.Enabled)
        {
            throw new ConflictException(
                $"Channel {id} is disabled; enable it before sending.",
                "channel_disabled");
        }

        JsonElement? rawOverrides = options?.Silent == true
            ? JsonDocument.Parse("{\"disable_notification\":true}").RootElement.Clone()
            : null;
        var reply = new ChannelReply
            {
                Channel = channel.Provider,
                ConversationId = channel.ExternalId,
                ReplyHandle = options?.ReplyHandle,
                Text = text,
                ReplyToMessageId = options?.ReplyToMessageId,
                RawOverrides = rawOverrides,
            };
        if (_outboundSender is null)
            await _producer.SendAsync(reply, ct);
        else
            await _outboundSender.SendAsync(reply, ChannelOutboundOrigin.Control, source: null, ct);
        await StampLastReplyAsync(channel.Provider, channel.ExternalId, text, ct);
    }

    /// <summary>
    /// CARD-0338 S4: stamp the last outbound reply without touching inbound
    /// <see cref="ChatChannel.LastMessageAt"/> / <see cref="ChatChannel.LastAuthor"/> /
    /// <see cref="ChatChannel.LastChannelMessageId"/>.
    /// </summary>
    public Task StampLastReplyAsync(string provider, string conversationId, string? preview, CancellationToken ct)
    {
        var now = UtcNow();
        var truncated = Truncate(preview);
        return _db.ChatChannels
            .Where(c => c.Provider == provider && c.ExternalId == conversationId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.LastReplyAt, now)
                .SetProperty(c => c.LastReplyPreview, truncated)
                .SetProperty(c => c.UpdatedAt, now), ct);
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

    private ChatChannelDto ToDto(ChatChannel c) => new(
        c.Id, c.Provider, c.ExternalId, c.Kind, c.Title,
        c.AgentId, c.Agent?.Name, c.Enabled,
        c.LastMessageAt, c.LastMessagePreview, c.LastAuthor,
        c.LastReplyAt, c.LastReplyPreview, c.MessageCount, c.CreatedAt,
        c.AlertMinSeverity, c.DigestEnabled, c.DigestLastSentAt,
        c.OutboundAgentProfile, Preview(c.OutboundAgentProfile));

    private ChannelOutboundProfileDto? Preview(string? name)
    {
        if (name is null || !_outbound.Profiles.TryGetValue(name, out var profile))
            return null;
        var agent = _db.Agents.AsNoTracking().FirstOrDefault(a => a.Id == profile.AgentId);
        string revision = "unavailable";
        if (agent is not null && TryGetPromptPath(agent, profile.PromptFile, out var path)
            && File.Exists(path))
        {
            try { revision = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new ChannelOutboundProfileDto(name, profile.ProjectId, profile.AgentId,
            agent?.Name ?? "unavailable", revision, profile.Trigger.ToString(),
            profile.TimeoutSeconds, profile.MaxPending,
            "One metered worker invocation per matching agent reply");
    }

    internal static async Task ValidateOutboundBindingAsync(
        AppDbContext db, ChannelOutboundSettings outbound, ChatChannel channel, CancellationToken ct)
    {
        var name = channel.OutboundAgentProfile!;
        if (!outbound.Profiles.TryGetValue(name, out var profile))
            throw new ValidationException(nameof(channel.OutboundAgentProfile), $"Unknown profile '{name}'.");
        if (!channel.Enabled || channel.AgentId is null)
            throw new ValidationException(nameof(channel.OutboundAgentProfile),
                "An outbound profile requires an enabled channel bound to an agent.");
        var inbound = await db.Agents.Include(a => a.Board)
            .SingleOrDefaultAsync(a => a.Id == channel.AgentId, ct);
        var converter = await db.Agents.Include(a => a.Board)
            .SingleOrDefaultAsync(a => a.Id == profile.AgentId, ct);
        if (inbound?.Board?.ProjectId != profile.ProjectId || converter?.Board?.ProjectId != profile.ProjectId
            || converter.Id == inbound.Id || converter.IsPoolDelegate || converter.AlwaysOn
            || !AgentTaskService.DelegatableKinds.Contains(converter.Kind))
            throw new ValidationException(nameof(channel.OutboundAgentProfile),
                "Inbound and conversion agents must be distinct, delegatable agents in the profile project.");
        if (await db.ChatChannels.AnyAsync(c => c.AgentId == converter.Id, ct))
            throw new ValidationException(nameof(channel.OutboundAgentProfile),
                "The conversion agent is bound to an inbound channel.");
        if (!TryGetPromptPath(converter, profile.PromptFile, out var promptPath) || !File.Exists(promptPath))
            throw new ValidationException(nameof(channel.OutboundAgentProfile),
                "The conversion agent needs an existing prompt inside its dedicated workspace.");
    }

    internal static bool TryGetPromptPath(Agent agent, string relative, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(agent.WorkingDirectory) || Path.IsPathRooted(relative))
            return false;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(agent.WorkingDirectory));
            var candidate = Path.GetFullPath(Path.Combine(root, relative));
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, pathComparison))
                return false;
            if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
                return false;
            for (var parent = Path.GetDirectoryName(candidate); parent is not null && parent.Length > root.Length;
                 parent = Path.GetDirectoryName(parent))
                if (Directory.Exists(parent) && File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
                    return false;
            if (File.Exists(candidate) && File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint))
                return false;
            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record ChannelSendOptions(
    bool Silent = false,
    string? ReplyToMessageId = null,
    string? ReplyHandle = null);
