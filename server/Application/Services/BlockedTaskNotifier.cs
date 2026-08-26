using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Sends one loud ping each time a task enters Blocked, then records that the human was told.</summary>
public sealed class BlockedTaskNotifier
{
    private readonly AppDbContext _db;
    private readonly AttentionService _attention;
    private readonly ChatChannelService _channels;
    private readonly DigestSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<BlockedTaskNotifier> _logger;

    public BlockedTaskNotifier(AppDbContext db, AttentionService attention, ChatChannelService channels,
        IOptions<DigestSettings> settings, TimeProvider time, ILogger<BlockedTaskNotifier> logger)
    { _db = db; _attention = attention; _channels = channels; _settings = settings.Value; _time = time; _logger = logger; }

    public async Task SweepAsync(CancellationToken ct)
    {
        if (!_settings.WakeOnBlocked) return;
        var channelIds = await _db.ChatChannels.AsNoTracking().Where(c => c.DigestEnabled).Select(c => c.Id).ToListAsync(ct);
        if (channelIds.Count == 0) return;
        var blocked = await _db.AgentTasks.AsNoTracking().Where(t => t.Status == AgentTaskStatus.Blocked).ToListAsync(ct);
        if (blocked.Count == 0) return;
        var taskIds = blocked.Select(t => t.Id).ToList();
        var events = await _db.AgentTaskEvents.AsNoTracking().Where(e => taskIds.Contains(e.AgentTaskId)).ToListAsync(ct);
        var attention = await _attention.GetAsync(ct);
        var byId = attention.Items.Where(i => i.TaskId is not null).ToDictionary(i => i.TaskId!.Value);
        foreach (var task in blocked)
        {
            var latestBlock = events.Where(e => e.AgentTaskId == task.Id && (e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Conflicted))
                .OrderByDescending(e => e.At).FirstOrDefault();
            if (latestBlock is null || events.Any(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.HumanNotified && e.At > latestBlock.At)) continue;
            if (!byId.TryGetValue(task.Id, out var item)) continue;
            foreach (var channelId in channelIds)
            {
                try
                {
                    await _channels.SendAsync(channelId, AwayDigestFormatter.FormatPing(item), ct);
                    _db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.HumanNotified,
                        Detail = channelId.ToString("D"), At = _time.GetUtcNow().UtcDateTime,
                    });
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger.LogWarning(ex, "Blocked task ping for {TaskId} to {ChannelId} failed", task.Id, channelId); }
            }
        }
    }
}
