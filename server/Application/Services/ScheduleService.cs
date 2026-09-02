using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Claim, fire, and CRUD for <see cref="Schedule"/> rows (CARD-0057). The sweep claims due rows
/// with one conditional UPDATE, writes a fire row, and hands a <see cref="FireClaim"/> to
/// <see cref="ScheduleFireQueue"/> before this class runs the prompt arm.
/// </summary>
public sealed class ScheduleService
{
    public const int MaxNameLength = 200;
    public const int MaxCreatedByLength = 200;

    internal const string SchedulerActor = CardService.SchedulerActor;

    private static readonly CardStatus[] CardActionTargets =
        [CardStatus.Backlog, CardStatus.InProgress, CardStatus.Review];

    private readonly AppDbContext _db;
    private readonly ScheduleFireQueue _queue;
    private readonly SessionMessageQueueService _messages;
    private readonly AgentSessionRuntime _runtime;
    private readonly IScheduledCardActions _cards;
    private readonly IEventBus _eventBus;
    private readonly OrchestratorControlState _orchestrator;
    private readonly TimeProvider _time;
    private readonly ScheduleSettings _settings;
    private readonly DigestSettings _digest;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(
        AppDbContext db,
        ScheduleFireQueue queue,
        SessionMessageQueueService messages,
        AgentSessionRuntime runtime,
        IScheduledCardActions cards,
        IEventBus eventBus,
        OrchestratorControlState orchestrator,
        TimeProvider time,
        IOptions<ScheduleSettings> settings,
        IOptions<DigestSettings> digest,
        ILogger<ScheduleService> logger)
    {
        _db = db;
        _queue = queue;
        _messages = messages;
        _runtime = runtime;
        _cards = cards;
        _eventBus = eventBus;
        _orchestrator = orchestrator;
        _time = time;
        _settings = settings.Value;
        _digest = digest.Value;
        _logger = logger;
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    public string ResolveDefaultTimeZone()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultTimeZone)
            && ScheduleRecurrence.TryGetTimeZone(_settings.DefaultTimeZone, out _))
        {
            return _settings.DefaultTimeZone.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_digest.TimeZone)
            && ScheduleRecurrence.TryGetTimeZone(_digest.TimeZone, out _))
        {
            return _digest.TimeZone;
        }

        return TimeZoneInfo.Local.Id;
    }

    public async Task<int> ClaimDueAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        var now = UtcNow();
        var due = await _db.Schedules.AsNoTracking()
            .Where(s => s.Enabled && s.NextFireAt != null && s.NextFireAt <= now)
            .OrderBy(s => s.NextFireAt)
            .ToListAsync(ct);

        var claimed = 0;
        foreach (var row in due)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryClaimOneAsync(row, now, manual: false, ct))
                claimed++;
        }

        return claimed;
    }

    public async Task FireNowAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.Schedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(Schedule), id);

        var now = UtcNow();
        if (!await TryClaimOneAsync(row, now, manual: true, ct))
            throw new ConflictException("The schedule could not be claimed for a manual fire; retry.");
    }

    private async Task<bool> TryClaimOneAsync(Schedule row, DateTime now, bool manual, CancellationToken ct)
    {
        var seenNext = row.NextFireAt;
        var seenCount = row.FireCount;
        var fireNumber = seenCount + 1;
        DateTime? next;
        DateTime dueAt;
        if (manual)
        {
            next = seenNext;
            dueAt = seenNext ?? now;
        }
        else
        {
            dueAt = seenNext ?? now;
            next = row.Repeat == ScheduleRepeat.Once
                ? null
                : ScheduleRecurrence.NextAfter(row, now);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var update = _db.Schedules.Where(s => s.Id == row.Id && s.FireCount == seenCount);
        if (!manual)
            update = update.Where(s => s.NextFireAt == seenNext && s.Enabled);

        var rows = manual
            ? await update.ExecuteUpdateAsync(
                s => s.SetProperty(t => t.FireCount, fireNumber)
                      .SetProperty(t => t.LastFiredAt, now)
                      .SetProperty(t => t.UpdatedAt, now)
                      .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()),
                ct)
            : await update.ExecuteUpdateAsync(
                s => s.SetProperty(t => t.NextFireAt, next)
                      .SetProperty(t => t.FireCount, fireNumber)
                      .SetProperty(t => t.LastFiredAt, now)
                      .SetProperty(t => t.UpdatedAt, now)
                      .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()),
                ct);
        if (rows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        _db.ScheduleFires.Add(new ScheduleFire
        {
            Id = Guid.NewGuid(),
            ScheduleId = row.Id,
            FireNumber = fireNumber,
            DueAt = dueAt,
            ClaimedAt = now,
            Outcome = ScheduleFireOutcome.Claimed,
            Manual = manual,
        });
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _queue.TryEnqueue(new FireClaim(row.Id, dueAt, fireNumber, manual));
        return true;
    }

    public async Task FireAsync(FireClaim claim, CancellationToken ct)
    {
        try
        {
            await FireCoreAsync(claim, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Schedule fire {ScheduleId} #{FireNumber} failed", claim.ScheduleId, claim.FireNumber);
            await CompleteFireAsync(
                claim.ScheduleId,
                claim.FireNumber,
                ScheduleFireOutcome.Failed,
                ex.Message,
                queuedMessageId: null,
                spawnedSessionId: null,
                ct);
        }
    }

    private async Task FireCoreAsync(FireClaim claim, CancellationToken ct)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == claim.ScheduleId, ct);
        if (schedule is null)
            return;

        var now = UtcNow();
        if (!claim.Manual && schedule.MissedGraceMinutes is int grace)
        {
            var late = now - claim.DueAt;
            if (late > TimeSpan.FromMinutes(grace))
            {
                await CompleteFireAsync(
                    claim.ScheduleId,
                    claim.FireNumber,
                    ScheduleFireOutcome.SkippedLate,
                    $"due {claim.DueAt:o}, skipped {FormatLate(late)} late (grace {grace} min)",
                    null,
                    null,
                    ct);
                return;
            }
        }

        if (schedule.Kind == ScheduleKind.Card)
        {
            await FireCardAsync(schedule, claim, now, ct);
            return;
        }

        if (schedule.Kind != ScheduleKind.Prompt)
        {
            await CompleteFireAsync(
                claim.ScheduleId, claim.FireNumber, ScheduleFireOutcome.Failed,
                $"unknown schedule kind '{schedule.Kind}'", null, null, ct);
            return;
        }

        if (schedule.AgentId is not Guid agentId)
        {
            await CompleteFireAsync(
                claim.ScheduleId, claim.FireNumber, ScheduleFireOutcome.SkippedNoSession,
                "schedule has no agent", null, null, ct);
            return;
        }

        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null)
        {
            await CompleteFireAsync(
                claim.ScheduleId, claim.FireNumber, ScheduleFireOutcome.SkippedNoSession,
                "agent is gone", null, null, ct);
            return;
        }

        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
        {
            await CompleteFireAsync(
                claim.ScheduleId, claim.FireNumber, ScheduleFireOutcome.SkippedNoSession,
                "agent has never launched", null, null, ct);
            return;
        }

        var session = await _db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            await CompleteFireAsync(
                claim.ScheduleId, claim.FireNumber, ScheduleFireOutcome.SkippedNoSession,
                "persistent session row is gone", null, null, ct);
            return;
        }

        var live = _runtime.ListLiveSessions().Contains(sessionId);
        if (!live)
        {
            var queueAnyway = schedule.WhenTargetDown == ScheduleWhenTargetDown.Queue;
            if (!queueAnyway)
            {
                await CompleteFireAsync(
                    claim.ScheduleId, claim.FireNumber, ScheduleFireOutcome.SkippedNoSession,
                    agent.AlwaysOn
                        ? "agent is down (WhenTargetDown=Skip)"
                        : "agent is not always-on and has no live session",
                    null,
                    null,
                    ct);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(schedule.PromptText))
            throw new InvalidOperationException("Prompt text is empty.");

        var superseded = await _messages.CancelPendingBySourceScheduleAsync(schedule.Id, ct);
        var (header, body) = BuildPromptBody(schedule, claim, now);
        Guid? queuedId = null;
        await _messages.EnqueueAsync(
            sessionId,
            body,
            MessageSendMode.WhenIdle,
            ct,
            origin: QueuedMessageOrigin.Scheduled,
            noteHeader: header,
            sourceScheduleId: schedule.Id,
            onCreated: id => queuedId = id);

        var row = queuedId is Guid qid
            ? await _db.SessionQueuedMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == qid, ct)
            : null;

        ScheduleFireOutcome outcome;
        string? detail;
        if (!live)
        {
            outcome = ScheduleFireOutcome.QueuedForRelaunch;
            detail = superseded.Count > 0
                ? $"superseded {string.Join(",", superseded)}"
                : null;
        }
        else if (row is { Status: QueuedMessageStatus.Sent })
        {
            outcome = ScheduleFireOutcome.Delivered;
            detail = superseded.Count > 0
                ? $"superseded {string.Join(",", superseded)}"
                : null;
        }
        else
        {
            outcome = ScheduleFireOutcome.Enqueued;
            var working = await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct);
            detail = working
                ? "session is mid-turn"
                : session.Status == SessionStatus.Starting
                    ? "session is starting"
                    : "queued WhenIdle";
            if (superseded.Count > 0)
                detail += $"; superseded {string.Join(",", superseded)}";
        }

        if (claim.Manual && now - claim.DueAt > TimeSpan.FromMinutes(1))
        {
            var late = FormatLate(now - claim.DueAt);
            detail = string.IsNullOrEmpty(detail) ? $"fired {late} late" : $"{detail}; fired {late} late";
        }
        else if (!claim.Manual && schedule.Repeat == ScheduleRepeat.Once && now - claim.DueAt > TimeSpan.FromMinutes(1))
        {
            var late = FormatLate(now - claim.DueAt);
            detail = string.IsNullOrEmpty(detail) ? $"fired {late} late" : $"{detail}; fired {late} late";
        }

        await CompleteFireAsync(claim.ScheduleId, claim.FireNumber, outcome, detail, queuedId, null, ct);
    }

    private async Task FireCardAsync(Schedule schedule, FireClaim claim, DateTime now, CancellationToken ct)
    {
        if (schedule.CardId is not Guid cardId)
        {
            await SkipTargetGoneAsync(schedule, claim, "schedule has no card", ct);
            return;
        }

        var card = await _db.Cards
            .AsNoTracking()
            .Include(c => c.BoardColumn)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null)
        {
            await SkipTargetGoneAsync(schedule, claim, "card is gone", ct);
            return;
        }

        var skipReason = TargetGoneReason(card);
        if (skipReason is not null)
        {
            await SkipTargetGoneAsync(schedule, claim, skipReason, ct);
            return;
        }

        var target = schedule.TargetStatus
            ?? throw new InvalidOperationException("Card schedule has no target status.");
        var reason =
            $"Scheduled · {schedule.Name} · fire #{claim.FireNumber}";

        try
        {
            switch (schedule.Start)
            {
                case ScheduleStart.None:
                {
                    var moved = await _cards.ApplyAutomatedMoveAsync(
                        cardId, target, reason, SchedulerActor, ct);
                    var detail = moved
                        ? $"moved {card.Status} → {target}"
                        : $"already {target}";
                    await CompleteFireAsync(
                        schedule.Id, claim.FireNumber, ScheduleFireOutcome.Moved, detail, null, null, ct);
                    return;
                }
                case ScheduleStart.Release:
                {
                    var moved = await _cards.ApplyAutomatedMoveAsync(
                        cardId, target, reason, SchedulerActor, ct);
                    var released = await _cards.ReleaseAutoDispatchHoldAsync(
                        cardId, $"{reason} — auto-dispatch hold released", SchedulerActor, ct);
                    var detail = moved
                        ? $"moved {card.Status} → {target}"
                        : $"already {target}";
                    detail += released ? "; auto-dispatch hold released" : "; hold already clear";
                    await CompleteFireAsync(
                        schedule.Id, claim.FireNumber, ScheduleFireOutcome.Released, detail, null, null, ct);
                    return;
                }
                case ScheduleStart.Spawn:
                {
                    var spawned = await _cards.SpawnAsync(cardId, new SpawnCardRequest(), ct);
                    await CompleteFireAsync(
                        schedule.Id,
                        claim.FireNumber,
                        ScheduleFireOutcome.Spawned,
                        $"spawned session {spawned.SessionId:D}",
                        null,
                        spawned.SessionId,
                        ct);
                    return;
                }
                default:
                    await CompleteFireAsync(
                        schedule.Id, claim.FireNumber, ScheduleFireOutcome.Failed,
                        $"unknown start mode '{schedule.Start}'", null, null, ct);
                    return;
            }
        }
        catch (HttpException ex) when (ex.StatusCode == 409)
        {
            // Quota / model-hold 409 is a refusal. Never reroute (AGENTS.md).
            var code = ex.Code ?? "conflict";
            await CompleteFireAsync(
                schedule.Id,
                claim.FireNumber,
                ScheduleFireOutcome.Refused,
                $"{code}: {ex.Message}",
                null,
                null,
                ct);
        }
    }

    private static string? TargetGoneReason(Card card)
    {
        if (card.ArchivedAt is not null)
            return "card is archived";
        if (card.OwnerSessionId is not null)
            return "card is owned by a live session";
        if (card.Status is CardStatus.Done or CardStatus.Canceled)
            return $"card is terminal ({card.Status})";
        if (card.Status is CardStatus.NeedsDecision)
            return "card is parked on NeedsDecision";
        return null;
    }

    private async Task SkipTargetGoneAsync(
        Schedule schedule, FireClaim claim, string reason, CancellationToken ct)
    {
        if (schedule.Repeat == ScheduleRepeat.Once)
        {
            schedule.Enabled = false;
            schedule.NextFireAt = null;
            schedule.UpdatedAt = UtcNow();
            schedule.ConcurrencyToken = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);
        }

        await CompleteFireAsync(
            schedule.Id, claim.FireNumber, ScheduleFireOutcome.SkippedTargetGone, reason, null, null, ct);
    }

    internal static (string Header, string Body) BuildPromptBody(
        Schedule schedule, FireClaim claim, DateTime firedAt)
    {
        var describe = ScheduleRecurrence.Describe(schedule);
        var late = firedAt - claim.DueAt;
        var lateStamp = late > TimeSpan.FromMinutes(1)
            ? $", fired {firedAt:HH:mm:ss} ({FormatLate(late)} late)"
            : $", fired {firedAt:HH:mm:ss}";
        var banner =
            $"[scheduled: {schedule.Name} · {describe} · fire #{claim.FireNumber} · due {claim.DueAt:HH:mm}{lateStamp}]";
        var header = $"Scheduled · {schedule.Name}";
        var prompt = schedule.PromptText ?? "";
        return (header, $"{banner}\n{prompt}");
    }

    internal static string FormatLate(TimeSpan late)
    {
        if (late < TimeSpan.Zero)
            late = TimeSpan.Zero;
        var hours = (int)late.TotalHours;
        var minutes = late.Minutes;
        if (hours > 0)
            return $"{hours}h{minutes:D2}m";
        if (minutes > 0)
            return $"{minutes}m";
        return $"{Math.Max(1, (int)late.TotalSeconds)}s";
    }

    private async Task CompleteFireAsync(
        Guid scheduleId,
        int fireNumber,
        ScheduleFireOutcome outcome,
        string? detail,
        Guid? queuedMessageId,
        Guid? spawnedSessionId,
        CancellationToken ct)
    {
        var now = UtcNow();
        var fire = await _db.ScheduleFires
            .FirstOrDefaultAsync(f => f.ScheduleId == scheduleId && f.FireNumber == fireNumber, ct);
        if (fire is not null)
        {
            fire.Outcome = outcome;
            fire.Detail = detail;
            fire.CompletedAt = now;
            fire.QueuedMessageId = queuedMessageId;
            fire.SpawnedSessionId = spawnedSessionId;
        }

        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is not null)
        {
            schedule.LastOutcome = outcome;
            schedule.LastOutcomeDetail = detail is { Length: > 2000 } ? detail[..2000] : detail;
            schedule.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        if (schedule is not null)
            await PublishChangedAsync(schedule, ct);
    }

    public async Task PruneFiresAsync(CancellationToken ct)
    {
        var keep = Math.Max(1, _settings.FireHistoryKeep);
        var scheduleIds = await _db.Schedules.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
        foreach (var id in scheduleIds)
        {
            var extra = await _db.ScheduleFires
                .Where(f => f.ScheduleId == id)
                .OrderByDescending(f => f.FireNumber)
                .Skip(keep)
                .Select(f => f.Id)
                .ToListAsync(ct);
            if (extra.Count == 0)
                continue;
            await _db.ScheduleFires.Where(f => extra.Contains(f.Id)).ExecuteDeleteAsync(ct);
        }
    }

    public async Task<IReadOnlyList<ScheduleDto>> ListAsync(
        Guid? agentId, Guid? cardId, Guid? boardId, bool? enabled, CancellationToken ct)
    {
        var query = _db.Schedules.AsNoTracking()
            .Include(s => s.Agent)
            .Include(s => s.Card)
            .AsQueryable();
        if (agentId is Guid a)
            query = query.Where(s => s.AgentId == a);
        if (cardId is Guid c)
            query = query.Where(s => s.CardId == c);
        if (boardId is Guid b)
            query = query.Where(s => s.Card != null && s.Card.BoardId == b);
        if (enabled is bool e)
            query = query.Where(s => s.Enabled == e);

        var rows = await query.OrderBy(s => s.NextFireAt).ThenBy(s => s.Name).ToListAsync(ct);
        return rows.Select(s => ToDto(s)).ToList();
    }

    public async Task<ScheduleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.Schedules.AsNoTracking()
            .Include(s => s.Agent)
            .Include(s => s.Card)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(Schedule), id);
        var fires = await _db.ScheduleFires.AsNoTracking()
            .Where(f => f.ScheduleId == id)
            .OrderByDescending(f => f.FireNumber)
            .Take(10)
            .ToListAsync(ct);
        return ToDto(row, fires);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var existing = await _db.Schedules.AsNoTracking()
            .Select(s => new { s.Id, s.AgentId, s.CardId })
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(Schedule), id);
        var rows = await _db.Schedules.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        if (rows == 0)
            throw new NotFoundException(nameof(Schedule), id);
        await _eventBus.PublishToAllAsync(
            "ScheduleChanged",
            new { scheduleId = existing.Id, agentId = existing.AgentId, cardId = existing.CardId },
            ct);
    }

    public async Task<SchedulePreviewDto> PreviewRequestAsync(CreateScheduleRequest request, CancellationToken ct)
    {
        var now = UtcNow();
        var draft = await BuildFromRequestAsync(request, now, persist: false, ct);
        return await BuildPreviewAsync(draft, ct);
    }

    public async Task<SchedulePreviewDto> PreviewExistingAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.Schedules.AsNoTracking()
            .Include(s => s.Agent)
            .Include(s => s.Card)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(Schedule), id);
        return await BuildPreviewAsync(row, ct);
    }

    public async Task<ScheduleDto> CreateAsync(CreateScheduleRequest request, CancellationToken ct)
    {
        var now = UtcNow();
        var row = await BuildFromRequestAsync(request, now, persist: true, ct);
        _db.Schedules.Add(row);
        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(row, ct);
        return ToDto(row);
    }

    public async Task<ScheduleDto> PatchAsync(Guid id, PatchScheduleRequest request, CancellationToken ct)
    {
        var row = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(Schedule), id);
        if (row.ConcurrencyToken != request.ConcurrencyToken)
            throw new ConflictException("The schedule was modified; refresh and retry.");

        var now = UtcNow();
        var recurrenceChanged = false;
        if (request.Name is not null)
            row.Name = RequireName(request.Name);
        if (request.PromptText is not null)
        {
            row.PromptText = RequirePrompt(request.PromptText);
            if (row.AgentId is Guid agentId)
                await RefuseForbiddenBodyAsync(agentId, row.PromptText, ct);
        }

        if (request.WhenTargetDown is { } down)
            row.WhenTargetDown = down;
        if (request.TimeZoneId is not null)
        {
            row.TimeZoneId = RequireZone(request.TimeZoneId);
            recurrenceChanged = true;
        }

        if (request.Repeat is { } repeat)
        {
            row.Repeat = repeat;
            recurrenceChanged = true;
        }

        if (request.FireAt is { } fireAt)
        {
            row.FireAt = AsUtc(fireAt);
            recurrenceChanged = true;
        }

        if (request.EveryMinutes is { } every)
        {
            row.EveryMinutes = RequireEvery(every);
            recurrenceChanged = true;
        }

        if (request.AnchorAt is { } anchor)
        {
            row.AnchorAt = AsUtc(anchor);
            recurrenceChanged = true;
        }

        if (request.AtLocal is not null)
        {
            row.AtLocal = RequireAtLocal(request.AtLocal);
            recurrenceChanged = true;
        }

        if (request.DaysOfWeek is { } days)
        {
            row.DaysOfWeek = days;
            recurrenceChanged = true;
        }

        if (request.MissedGraceMinutes is { } grace)
            row.MissedGraceMinutes = grace < 0 ? throw new ValidationException(
                nameof(PatchScheduleRequest.MissedGraceMinutes), "MissedGraceMinutes cannot be negative.") : grace;

        var enabling = request.Enabled == true && !row.Enabled;
        if (request.Enabled is { } enabled)
            row.Enabled = enabled;

        if (enabling || recurrenceChanged)
            row.NextFireAt = ScheduleRecurrence.InitialNextFireAt(row, now);

        row.UpdatedAt = now;
        row.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await PublishChangedAsync(row, ct);
        return ToDto(row);
    }

    private async Task<Schedule> BuildFromRequestAsync(
        CreateScheduleRequest request, DateTime now, bool persist, CancellationToken ct)
    {
        var name = RequireName(request.Name);
        var zoneId = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? ResolveDefaultTimeZone()
            : RequireZone(request.TimeZoneId);
        var repeat = request.Repeat;

        var row = new Schedule
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = request.Kind,
            Repeat = repeat,
            TimeZoneId = zoneId,
            Enabled = true,
            CreatedBy = TrimTo(request.CreatedBy, MaxCreatedByLength),
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
            DaysOfWeek = request.DaysOfWeek ?? 0,
            Start = ScheduleStart.None,
        };

        switch (request.Kind)
        {
            case ScheduleKind.Prompt:
                await ApplyPromptFieldsAsync(row, request, persist, ct);
                break;
            case ScheduleKind.Card:
                await ApplyCardFieldsAsync(row, request, persist, ct);
                break;
            default:
                throw new ValidationException(
                    nameof(CreateScheduleRequest.Kind),
                    $"Unknown schedule kind '{request.Kind}'.");
        }

        switch (repeat)
        {
            case ScheduleRepeat.Once:
                row.FireAt = request.FireAt is DateTime fireAt
                    ? AsUtc(fireAt)
                    : throw new ValidationException(nameof(CreateScheduleRequest.FireAt), "Once requires fireAt (UTC).");
                break;
            case ScheduleRepeat.Interval:
                row.EveryMinutes = RequireEvery(request.EveryMinutes);
                row.AnchorAt = request.AnchorAt is DateTime anchor ? AsUtc(anchor) : now;
                break;
            case ScheduleRepeat.Daily:
                row.AtLocal = RequireAtLocal(request.AtLocal);
                break;
            default:
                throw new ValidationException(nameof(CreateScheduleRequest.Repeat), $"Unknown repeat '{repeat}'.");
        }

        row.MissedGraceMinutes = request.MissedGraceMinutes
            ?? ScheduleRecurrence.DefaultMissedGraceMinutes(repeat, row.EveryMinutes);
        if (row.MissedGraceMinutes is < 0)
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.MissedGraceMinutes),
                "MissedGraceMinutes cannot be negative.");
        }

        row.NextFireAt = ScheduleRecurrence.InitialNextFireAt(row, now);

        if (persist
            && row.Kind == ScheduleKind.Card
            && row.Start is ScheduleStart.Release or ScheduleStart.Spawn
            && !request.AcceptSpend)
        {
            throw new SpendUnacknowledgedException(await BuildPreviewAsync(row, ct));
        }

        return row;
    }

    private async Task ApplyPromptFieldsAsync(
        Schedule row, CreateScheduleRequest request, bool persist, CancellationToken ct)
    {
        var prompt = RequirePrompt(request.PromptText);
        if (string.IsNullOrWhiteSpace(request.Agent))
            throw new ValidationException(nameof(CreateScheduleRequest.Agent), "Agent is required for a prompt schedule.");

        var agent = await StandingAgentResolver.ResolveAsync(
            _db, request.Agent, nameof(CreateScheduleRequest.Agent), ct);
        await RefuseForbiddenBodyAsync(agent.Id, prompt, ct);

        row.AgentId = agent.Id;
        row.Agent = persist ? null : agent;
        row.PromptText = prompt;
        row.WhenTargetDown = request.WhenTargetDown
            ?? (agent.AlwaysOn ? ScheduleWhenTargetDown.Queue : ScheduleWhenTargetDown.Skip);
        row.Start = ScheduleStart.None;
    }

    private async Task ApplyCardFieldsAsync(
        Schedule row, CreateScheduleRequest request, bool persist, CancellationToken ct)
    {
        if (request.CardId is not Guid cardId)
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.CardId),
                "CardId is required for a card schedule.");
        }

        var target = request.TargetStatus
            ?? throw new ValidationException(
                nameof(CreateScheduleRequest.TargetStatus),
                "TargetStatus is required for a card schedule (Backlog, InProgress, or Review).");
        if (!CardActionTargets.Contains(target))
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.TargetStatus),
                "A card action cannot close a card or park it on NeedsDecision. TargetStatus must be Backlog, InProgress, or Review.");
        }

        var card = await _db.Cards.AsNoTracking()
            .Include(c => c.Board).ThenInclude(b => b.Columns)
            .Include(c => c.Board).ThenInclude(b => b.WorkflowDefinitions)
            .Include(c => c.BoardColumn)
            .Include(c => c.AssignedAgent)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new ValidationException(
                nameof(CreateScheduleRequest.CardId),
                $"No card matches '{cardId}'.");

        row.CardId = card.Id;
        row.Card = persist ? null : card;
        row.TargetStatus = target;
        row.Start = request.Start;
        row.WhenTargetDown = ScheduleWhenTargetDown.Skip;
        if (request.Start is ScheduleStart.Release or ScheduleStart.Spawn && request.AcceptSpend)
        {
            row.SpendAcceptedAt = UtcNow();
            row.SpendAcceptedBy = TrimTo(request.CreatedBy, MaxCreatedByLength) ?? "operator";
        }
    }

    private async Task RefuseForbiddenBodyAsync(Guid agentId, string prompt, CancellationToken ct)
    {
        var kind = await _db.Agents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => (AgentKind?)a.Kind)
            .FirstOrDefaultAsync(ct);
        if (kind is AgentKind k
            && SessionMessageQueueService.TryGetForbiddenReason(k, prompt, out var reason))
        {
            throw new ValidationException(nameof(CreateScheduleRequest.PromptText), reason);
        }
    }

    private async Task<SchedulePreviewDto> BuildPreviewAsync(Schedule schedule, CancellationToken ct)
    {
        var now = UtcNow();
        var next = ScheduleRecurrence.NextOccurrences(schedule, now, 3)
            .Select(utc => new ScheduleOccurrenceDto(utc, ScheduleRecurrence.ToLocal(utc, schedule.TimeZoneId)))
            .ToList();

        Agent? agent = schedule.Agent;
        if (agent is null && schedule.AgentId is Guid agentId)
            agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct);

        Card? card = schedule.Card;
        if (card is null && schedule.CardId is Guid cardId)
        {
            card = await _db.Cards.AsNoTracking()
                .Include(c => c.Board).ThenInclude(b => b.WorkflowDefinitions)
                .Include(c => c.BoardColumn)
                .Include(c => c.AssignedAgent)
                .FirstOrDefaultAsync(c => c.Id == cardId, ct);
        }

        Guid? sessionId = null;
        AgentSession? session = null;
        var live = false;
        if (agent is not null && Guid.TryParse(agent.PersistentSessionId, out var parsed))
        {
            sessionId = parsed;
            session = await _db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == parsed, ct);
            live = _runtime.ListLiveSessions().Contains(parsed);
        }

        var warnings = new List<string>();
        string effect;
        string spend;
        var willStart = false;
        string? willMove = null;
        SchedulePreviewEnvironmentDto? environment = null;

        if (schedule.Kind == ScheduleKind.Card)
        {
            (effect, spend, willStart, willMove, environment) =
                await DescribeCardPreviewAsync(schedule, card, warnings, ct);
        }
        else
        {
            spend = "none";
            if (agent is null)
            {
                warnings.Add("No agent is attached.");
                effect = "no target";
            }
            else if (sessionId is null)
            {
                warnings.Add("The agent has never launched; a fire will skip.");
                effect = $"will enqueue WhenIdle onto {agent.Name}";
            }
            else if (!live && schedule.WhenTargetDown == ScheduleWhenTargetDown.Skip)
            {
                warnings.Add("The agent is down; WhenTargetDown=Skip will record SkippedNoSession.");
                effect = $"will enqueue WhenIdle onto {agent.Name}";
            }
            else if (!live)
            {
                warnings.Add("The agent is down; the prompt will wait on the persistent session for relaunch.");
                effect = $"will enqueue WhenIdle onto {agent.Name}";
            }
            else
            {
                effect = $"will enqueue WhenIdle onto {agent.Name}";
            }
        }

        return new SchedulePreviewDto(
            next,
            new ScheduleTargetDto(
                agent?.Id,
                agent?.Name,
                agent is null ? null : live,
                agent?.AlwaysOn,
                session?.Status.ToString(),
                card?.Id ?? schedule.CardId,
                card?.Identifier,
                card?.Status,
                card?.ArchivedAt is not null,
                card?.BoardColumn?.Name,
                card?.OwnerSessionId),
            effect,
            spend,
            warnings,
            willStart,
            willMove,
            environment);
    }

    private async Task<(
        string Effect,
        string Spend,
        bool WillStartSession,
        string? WillMove,
        SchedulePreviewEnvironmentDto Environment)> DescribeCardPreviewAsync(
        Schedule schedule,
        Card? card,
        List<string> warnings,
        CancellationToken ct)
    {
        var spend = schedule.Start switch
        {
            ScheduleStart.Release => "orchestrator-under-cap",
            ScheduleStart.Spawn => "immediate-session",
            _ => "none",
        };
        var willStart = schedule.Start is ScheduleStart.Release or ScheduleStart.Spawn;
        string? willMove = null;
        if (card is not null && schedule.TargetStatus is { } target)
        {
            willMove = card.Status == target
                ? $"already {DescribeStatus(target)}"
                : $"{DescribeStatus(card.Status)} → {DescribeStatus(target)}";
        }

        if (card is null)
            warnings.Add("No card is attached.");
        else if (TargetGoneReason(card) is { } gone)
            warnings.Add($"A fire will skip: {gone}.");
        else if (schedule.Start == ScheduleStart.Spawn)
            warnings.Add("Spawn bypasses board and column caps, the same as card.ps1 move -Spawn.");
        else if (schedule.Start == ScheduleStart.Release)
            warnings.Add("Release lets the orchestrator start a session under the board and column caps.");

        int? activeCount = null;
        int? cap = null;
        string? hold = null;
        string? assigned = card?.AssignedAgent?.Name;
        string? definition = card?.Board.WorkflowDefinitions
            .Where(d => d.IsActive)
            .OrderByDescending(d => d.Version)
            .Select(d => d.Name)
            .FirstOrDefault();

        if (card is not null)
        {
            cap = card.Board.MaxConcurrentSessions;
            var activeStatuses = new[] { SessionStatus.Starting, SessionStatus.Running };
            activeCount = await _db.AgentSessions.AsNoTracking()
                .CountAsync(
                    s => s.CardId != null
                        && activeStatuses.Contains(s.Status)
                        && _db.Cards.Any(c => c.Id == s.CardId && c.BoardId == card.BoardId),
                    ct);

            var kind = card.AssignedAgent?.Kind;
            if (kind is AgentKind k)
            {
                var now = UtcNow();
                var activeHold = await _db.ModelAvailabilityHolds.AsNoTracking()
                    .Where(h => h.Kind == k && h.ClearedAt == null
                        && (h.DisabledUntil == null || h.DisabledUntil > now))
                    .OrderByDescending(h => h.HitAt)
                    .FirstOrDefaultAsync(ct);
                if (activeHold is not null)
                    hold = $"{k}/{activeHold.ModelAlias} ({activeHold.Source})";
            }
        }

        var environment = new SchedulePreviewEnvironmentDto(
            _orchestrator.IsPaused,
            activeCount,
            cap,
            hold,
            assigned,
            definition);

        var startBit = willStart
            ? schedule.Start == ScheduleStart.Spawn
                ? "willStartSession: true (immediate, bypassing caps)"
                : "willStartSession: true (orchestrator, under cap)"
            : "willStartSession: false";
        var moveBit = willMove is null ? "willMove: none" : $"willMove: {willMove}";
        var effect = $"{moveBit}; {startBit}; spend: {spend}";
        return (effect, spend, willStart, willMove, environment);
    }

    private static string DescribeStatus(CardStatus status) => status switch
    {
        CardStatus.InProgress => "In Progress",
        CardStatus.NeedsDecision => "Needs decision",
        _ => status.ToString(),
    };

    private Task PublishChangedAsync(Schedule schedule, CancellationToken ct) =>
        _eventBus.PublishToAllAsync(
            "ScheduleChanged",
            new { scheduleId = schedule.Id, agentId = schedule.AgentId, cardId = schedule.CardId },
            ct);

    private ScheduleDto ToDto(Schedule s, IReadOnlyList<ScheduleFire>? fires = null) =>
        new(
            s.Id,
            s.Name,
            s.Kind,
            s.Repeat,
            ScheduleRecurrence.Describe(s),
            s.TimeZoneId,
            s.NextFireAt,
            s.NextFireAt is DateTime next
                ? ScheduleRecurrence.ToLocal(next, s.TimeZoneId)
                : null,
            s.Enabled,
            s.MissedGraceMinutes,
            s.FireCount,
            s.LastFiredAt,
            s.LastOutcome,
            s.LastOutcomeDetail,
            s.CreatedBy,
            s.CreatedAt,
            s.UpdatedAt,
            s.ConcurrencyToken,
            s.AgentId,
            s.Agent?.Name,
            s.Agent?.Slug,
            s.PromptText,
            s.WhenTargetDown,
            s.CardId,
            s.Card?.Identifier,
            s.TargetStatus,
            s.Start,
            s.SpendAcceptedAt,
            s.SpendAcceptedBy,
            s.FireAt,
            s.EveryMinutes,
            s.AnchorAt,
            s.AtLocal,
            s.DaysOfWeek,
            fires?.Select(f => new ScheduleFireDto(
                f.Id, f.FireNumber, f.DueAt, f.ClaimedAt, f.CompletedAt,
                f.Outcome, f.Detail, f.QueuedMessageId, f.SpawnedSessionId, f.Manual)).ToList());

    private string RequireName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(CreateScheduleRequest.Name), "Name is required.");
        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.Name),
                $"Name is {trimmed.Length} characters; the limit is {MaxNameLength}.");
        }

        return trimmed;
    }

    private string RequirePrompt(string? prompt)
    {
        var trimmed = (prompt ?? "").Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(CreateScheduleRequest.PromptText), "Prompt text is required.");
        var max = Math.Max(1, _settings.MaxPromptLength);
        if (trimmed.Length > max)
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.PromptText),
                $"Prompt is {trimmed.Length} characters; the limit is {max}.");
        }

        return trimmed;
    }

    private static string RequireZone(string? timeZoneId)
    {
        try
        {
            return ScheduleRecurrence.RequireTimeZone(timeZoneId).Id;
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(nameof(CreateScheduleRequest.TimeZoneId), ex.Message);
        }
    }

    private static int RequireEvery(int? every)
    {
        if (every is not int value
            || value < ScheduleRecurrence.MinEveryMinutes
            || value > ScheduleRecurrence.MaxEveryMinutes)
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.EveryMinutes),
                $"EveryMinutes must be {ScheduleRecurrence.MinEveryMinutes}..{ScheduleRecurrence.MaxEveryMinutes}.");
        }

        return value;
    }

    private static string RequireAtLocal(string? atLocal)
    {
        if (string.IsNullOrWhiteSpace(atLocal) || !TimeOnly.TryParse(atLocal, out var time))
        {
            throw new ValidationException(
                nameof(CreateScheduleRequest.AtLocal),
                "Daily requires atLocal as HH:mm.");
        }

        return time.ToString("HH:mm");
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static string? TrimTo(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
