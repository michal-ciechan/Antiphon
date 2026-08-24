using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record ChatChannelDto(
    Guid Id,
    string Provider,
    string ExternalId,
    ChatChannelKind Kind,
    string? Title,
    Guid? AgentId,
    string? AgentName,
    bool Enabled,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    string? LastAuthor,
    long MessageCount,
    DateTime CreatedAt,
    // Non-null = this channel is an alert sink for severities >= the value.
    AlertSeverity? AlertMinSeverity = null);

/// <summary>
/// Partial update. <paramref name="AgentId"/> binds the channel to an agent; <paramref name="UnbindAgent"/>
/// clears the binding (JSON can't distinguish "agentId absent" from "agentId: null", hence the flag).
/// <paramref name="Enabled"/> toggles routing when provided.
/// </summary>
public sealed record UpdateChatChannelRequest(
    Guid? AgentId = null,
    bool UnbindAgent = false,
    bool? Enabled = null,
    // Set the alert-sink threshold; ClearAlertMinSeverity turns alerting off for this channel
    // (same JSON absent-vs-null dance as the agent binding).
    AlertSeverity? AlertMinSeverity = null,
    bool ClearAlertMinSeverity = false);
