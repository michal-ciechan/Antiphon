using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1803. One digest page when a Blocked seat has held past the hour bound and its caller
/// note is still undelivered. Notification only: the seat stays occupied.
/// </summary>
public sealed class BlockedSeatEscalationNotifier
{
    private readonly AppDbContext _db;
    private readonly AttentionService _attention;
    private readonly ChatChannelService _channels;
    private readonly DigestSettings _digest;
    private readonly SeatOccupancyState _seats;
    private readonly AttentionSettings _attentionSettings;
    private readonly TimeProvider _time;
    private readonly ILogger<BlockedSeatEscalationNotifier> _logger;

    public BlockedSeatEscalationNotifier(
        AppDbContext db,
        AttentionService attention,
        ChatChannelService channels,
        IOptions<DigestSettings> digest,
        SeatOccupancyState seats,
        IOptions<AttentionSettings> attentionSettings,
        TimeProvider time,
        ILogger<BlockedSeatEscalationNotifier> logger)
    {
        _db = db;
        _attention = attention;
        _channels = channels;
        _digest = digest.Value;
        _seats = seats;
        _attentionSettings = attentionSettings.Value;
        _time = time;
        _logger = logger;
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        if (!_digest.WakeOnBlocked || !_attentionSettings.SeatWatchEnabled)
            return;
        var channelIds = await _db.ChatChannels.AsNoTracking()
            .Where(c => c.DigestEnabled)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (channelIds.Count == 0)
            return;
        var snapshot = _seats.Current;
        if (snapshot.Hosts.Count == 0)
            return;
        var blocked = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Blocked)
            .Where(AgentTaskRoles.NotSpecialist)
            .ToListAsync(ct);
        if (blocked.Count == 0)
            return;
        var taskIds = blocked.Select(t => t.Id).ToList();
        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => taskIds.Contains(e.AgentTaskId))
            .ToListAsync(ct);
        var attention = await _attention.GetAsync(ct);
        var noted = attention.Items
            .Where(i => i.Kind == AttentionKind.CallerNoteUndelivered && i.TaskId is not null)
            .Select(i => i.TaskId!.Value)
            .ToHashSet();
        var now = _time.GetUtcNow().UtcDateTime;
        var hours = _attentionSettings.BlockedSeatEscalationHours;
        foreach (var task in blocked)
        {
            var latestBlock = events
                .Where(e => e.AgentTaskId == task.Id
                    && (e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Conflicted))
                .OrderByDescending(e => e.At)
                .FirstOrDefault();
            if (latestBlock is null
                || events.Any(e => e.AgentTaskId == task.Id
                    && e.Type == AgentTaskEventType.BlockedSeatEscalated
                    && e.At > latestBlock.At))
                continue;
            if (!BlockedSeatEscalation.Qualifies(
                    task.Status, task.Role, snapshot, task.Id, now, hours,
                    noted.Contains(task.Id), _attentionSettings.SeatWatchEnabled))
                continue;
            var seat = BlockedSeatEscalation.OccupyingIdleBlockedSeat(snapshot, task.Id);
            var evidence = seat is null ? null : $"runner={seat.RunnerId}; idleSince={seat.IdleSince:O}";
            var text = AwayDigestFormatter.FormatBlockedSeatEscalation(task.Id, evidence);
            foreach (var channelId in channelIds)
            {
                try
                {
                    await _channels.SendAsync(channelId, text, ct);
                    _db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = Guid.NewGuid(),
                        AgentTaskId = task.Id,
                        Type = AgentTaskEventType.BlockedSeatEscalated,
                        Detail = channelId.ToString("D"),
                        At = _time.GetUtcNow().UtcDateTime,
                    });
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Blocked seat page for {TaskId} to {ChannelId} failed", task.Id, channelId);
                }
            }
        }
    }
}
