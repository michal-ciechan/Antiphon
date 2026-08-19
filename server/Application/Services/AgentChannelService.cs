using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AgentChannelService
{
    private static readonly SessionStatus[] SourceStatuses = [SessionStatus.Starting, SessionStatus.Running];
    private static readonly SessionStatus[] TargetStatuses = [SessionStatus.Running];

    private readonly AppDbContext _db;
    private readonly AgentSessionRuntime _runtime;
    private readonly CardService _cardService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AgentChannelService> _logger;
    private readonly MentionRouteDiagnostics? _diagnostics;

    public AgentChannelService(
        AppDbContext db,
        AgentSessionRuntime runtime,
        CardService cardService,
        IEventBus eventBus,
        ILogger<AgentChannelService> logger,
        MentionRouteDiagnostics? diagnostics = null)
    {
        _db = db;
        _runtime = runtime;
        _cardService = cardService;
        _eventBus = eventBus;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task SendToSessionAsync(
        Guid? sourceSessionId,
        Guid targetSessionId,
        string message,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ValidationException(nameof(message), "Channel message must not be empty.");

        var target = await LoadLiveTargetSessionAsync(targetSessionId, ct);
        AgentSession? source = null;
        if (sourceSessionId is Guid sourceId)
            source = await LoadSourceSessionAsync(sourceId, ct);

        var input = FormatInput(source, message);
        await _runtime.SendInputAsync(target.Id, input, ct);
        await PublishMessageAsync(source, target, message.Trim(), routedByMention: false, ct);
    }

    public async Task<bool> RouteMentionAsync(
        Guid sourceSessionId,
        AgentMention mention,
        CancellationToken ct)
    {
        Record(sourceSessionId, MentionRouteDiagnostics.RouteStarted, $"target={mention.Target}");
        var source = await _db.AgentSessions
            .AsNoTracking()
            .Include(s => s.Card)
            .FirstOrDefaultAsync(s => s.Id == sourceSessionId && SourceStatuses.Contains(s.Status), ct);
        if (source is null)
        {
            Record(sourceSessionId, MentionRouteDiagnostics.SourceQueryReturned, "found=false");
            Record(sourceSessionId, MentionRouteDiagnostics.RouteReturned, "ok=false reason=source-missing");
            return false;
        }
        // Cardless interactive sessions don't participate in card-scoped channel routing.
        if (source.CardId is not Guid sourceCardId)
        {
            Record(sourceSessionId, MentionRouteDiagnostics.SourceQueryReturned, "found=true cardId=no");
            Record(sourceSessionId, MentionRouteDiagnostics.RouteReturned, "ok=false reason=cardless");
            return false;
        }

        Record(sourceSessionId, MentionRouteDiagnostics.SourceQueryReturned, $"found=true cardId={sourceCardId:N}");
        var matches = await FindMentionTargetsAsync(source, mention.Target, ct);
        if (matches.Count != 1)
        {
            await _eventBus.PublishToGroupAsync(
                AgentChannelGroups.Card(sourceCardId),
                "ChannelMentionIgnored",
                new
                {
                    sourceSessionId,
                    target = mention.Target,
                    reason = matches.Count == 0 ? "No live target matched." : "Mention target was ambiguous."
                },
                ct);
            Record(sourceSessionId, MentionRouteDiagnostics.EventPublished, "ChannelMentionIgnored");
            Record(
                sourceSessionId,
                MentionRouteDiagnostics.RouteReturned,
                matches.Count == 0 ? "ok=false reason=no-target" : "ok=false reason=ambiguous");
            return false;
        }

        var target = matches.Single();
        if (target.Id == source.Id)
        {
            Record(sourceSessionId, MentionRouteDiagnostics.RouteReturned, "ok=false reason=self");
            return false;
        }

        var message = string.IsNullOrWhiteSpace(mention.Message)
            ? $"@{mention.Target}"
            : mention.Message.Trim();
        await _runtime.SendInputAsync(target.Id, FormatInput(source, message), ct);
        Record(sourceSessionId, MentionRouteDiagnostics.InputSent, $"target={target.Id:N}");
        await PublishMessageAsync(source, target, message, routedByMention: true, ct);
        Record(sourceSessionId, MentionRouteDiagnostics.RouteReturned, "ok=true");
        return true;
    }

    public async Task<SpawnCardResult> DelegateCardAsync(
        ChannelDelegateCardRequest request,
        CancellationToken ct)
    {
        if (request.ConcurrencyToken == Guid.Empty)
            throw new ValidationException(nameof(request.ConcurrencyToken), "Card concurrency token is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ValidationException(nameof(request.Message), "Delegation message must not be empty.");

        var prompt = $"""
            Delegated channel message:
            {request.Message.Trim()}
            """;
        var result = await _cardService.SpawnAsync(
            request.CardId,
            new SpawnCardRequest(
                request.DefinitionName,
                request.Cols,
                request.Rows,
                Prompt: prompt,
                ConcurrencyToken: request.ConcurrencyToken),
            ct);
        await _eventBus.PublishToAllAsync(
            "ChannelDelegated",
            new { cardId = request.CardId, result.SessionId },
            ct);
        return result;
    }

    private async Task<AgentSession> LoadLiveTargetSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var liveSessionIds = _runtime.ListLiveSessions().ToHashSet();
        if (!liveSessionIds.Contains(sessionId))
            throw new NotFoundException(nameof(AgentSession), sessionId);

        return await _db.AgentSessions
            .AsNoTracking()
            .Include(s => s.Card)
            .FirstOrDefaultAsync(s => s.Id == sessionId && TargetStatuses.Contains(s.Status), ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);
    }

    private async Task<AgentSession> LoadSourceSessionAsync(Guid sessionId, CancellationToken ct)
    {
        return await _db.AgentSessions
            .AsNoTracking()
            .Include(s => s.Card)
            .FirstOrDefaultAsync(s => s.Id == sessionId && SourceStatuses.Contains(s.Status), ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);
    }

    private async Task<IReadOnlyList<AgentSession>> FindMentionTargetsAsync(
        AgentSession source,
        string target,
        CancellationToken ct)
    {
        var normalized = target.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            Record(source.Id, MentionRouteDiagnostics.TargetQueryReturned, "matches=0 reason=empty-target");
            return [];
        }

        var liveSessionIds = _runtime.ListLiveSessions().ToHashSet();
        var sessions = await _db.AgentSessions
            .AsNoTracking()
            .Include(s => s.Card)
            .Where(s => s.Id != source.Id
                && liveSessionIds.Contains(s.Id)
                && TargetStatuses.Contains(s.Status)
                && s.Card.BoardId == source.Card.BoardId)
            .ToListAsync(ct);

        var matches = sessions
            .Where(s => s.DefinitionName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0 && normalized.Length >= 8)
        {
            matches = sessions
                .Where(s => s.Id.ToString("N").StartsWith(normalized.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Record(
            source.Id,
            MentionRouteDiagnostics.TargetQueryReturned,
            $"matches={matches.Count} live={liveSessionIds.Count} scanned={sessions.Count}");
        return matches;
    }

    private void Record(Guid sourceSessionId, string stage, string? detail = null)
    {
        if (_diagnostics is not null)
            _diagnostics.Record(sourceSessionId, stage, detail);
    }

    private async Task PublishMessageAsync(
        AgentSession? source,
        AgentSession target,
        string message,
        bool routedByMention,
        CancellationToken ct)
    {
        // Cardless interactive targets have no card channel to broadcast on.
        if (target.CardId is not Guid targetCardId)
        {
            if (routedByMention && source is not null)
                Record(source.Id, MentionRouteDiagnostics.EventPublished, "skipped-cardless");
            return;
        }

        try
        {
            await _eventBus.PublishToGroupAsync(
                AgentChannelGroups.Card(targetCardId),
                "ChannelMessage",
                new
                {
                    sourceSessionId = source?.Id,
                    targetSessionId = target.Id,
                    targetCardId,
                    message,
                    routedByMention
                },
                ct);
            if (routedByMention && source is not null)
                Record(source.Id, MentionRouteDiagnostics.EventPublished, "ChannelMessage");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (routedByMention && source is not null)
                Record(source.Id, MentionRouteDiagnostics.EventPublished, $"failed {ex.GetType().Name}");
            _logger.LogWarning(ex, "Failed to publish channel message for target session {SessionId}", target.Id);
        }
    }

    private static string FormatInput(AgentSession? source, string message)
    {
        var prefix = source is null
            ? "[channel]"
            : $"[channel from {source.DefinitionName}]";
        return $"{prefix} {message.Trim()}{Environment.NewLine}";
    }
}
