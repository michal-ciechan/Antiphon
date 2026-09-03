using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0338 S3: pages every DigestEnabled channel once per Critical incident kind in
/// <see cref="DigestSettings.WakeOnIncidentKinds"/> on an AlwaysOn channel-bound agent.
/// Deduped by <c>AgentIncident.HumanNotifiedAt</c> (not an ack). ProviderCapacity is never
/// paged — CARD-0281 already sent the capacity notice.
/// </summary>
public sealed class IncidentPageNotifier
{
    internal const string ProviderCapacityReason = "ProviderCapacity";

    private readonly AppDbContext _db;
    private readonly ChatChannelService _channels;
    private readonly DigestSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<IncidentPageNotifier> _logger;

    public IncidentPageNotifier(
        AppDbContext db,
        ChatChannelService channels,
        IOptions<DigestSettings> settings,
        TimeProvider time,
        ILogger<IncidentPageNotifier> logger)
    {
        _db = db;
        _channels = channels;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var kinds = _settings.WakeOnIncidentKinds;
        if (kinds is null || kinds.Count == 0)
            return;

        var channelIds = await _db.ChatChannels.AsNoTracking()
            .Where(c => c.DigestEnabled)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (channelIds.Count == 0)
            return;

        var cutoff = _time.GetUtcNow().UtcDateTime.AddHours(-24);
        var candidates = await _db.AgentIncidents
            .Where(i => kinds.Contains(i.Kind)
                && i.Severity == AlertSeverity.Critical
                && i.HumanNotifiedAt == null
                && i.CreatedAt >= cutoff
                && i.AgentId != null
                && i.FailureReason != ProviderCapacityReason)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var agentIds = candidates.Select(i => i.AgentId!.Value).Distinct().ToList();
        var alwaysOn = await _db.Agents.AsNoTracking()
            .Where(a => agentIds.Contains(a.Id) && a.AlwaysOn)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        if (alwaysOn.Count == 0)
            return;

        var alwaysOnIds = alwaysOn.Select(a => a.Id).ToList();
        var boundIds = await _db.ChatChannels.AsNoTracking()
            .Where(c => c.AgentId != null && c.Enabled && alwaysOnIds.Contains(c.AgentId.Value))
            .Select(c => c.AgentId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (boundIds.Count == 0)
            return;

        var names = alwaysOn.ToDictionary(a => a.Id, a => a.Name);
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var incident in candidates.Where(i => boundIds.Contains(i.AgentId!.Value)))
        {
            var agentName = names.GetValueOrDefault(incident.AgentId!.Value, "agent");
            var ping = AwayDigestFormatter.FormatIncidentPing(
                agentName,
                incident.Kind,
                incident.Message,
                incident.FailureReason,
                incident.CreatedAt,
                _settings,
                incident.AgentId);
            try
            {
                foreach (var channelId in channelIds)
                    await _channels.SendAsync(channelId, ping, ct);
                incident.HumanNotifiedAt = now;
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Incident ping for {IncidentId} ({Kind}) on agent {AgentId} failed",
                    incident.Id, incident.Kind, incident.AgentId);
            }
        }
    }
}
