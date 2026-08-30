using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0245 S1: read the independent watchdog-state observer document and raise one Critical
/// <see cref="AgentIncidentKind.AppHostWatchdogDisabled"/> per eligible AlwaysOn + bound-channel
/// agent per episode. Detection only — never re-enables the Scheduled Task and never restarts
/// AppHost. The document is the handoff across an AppHost outage; this service can only create
/// the in-app row after recovery. Incidents go through the same
/// <see cref="AgentSupervisorService.RecordIncidentAsync"/> path (row + alert) so they reuse
/// RecentCriticalIncident attention rather than a parallel store.
/// </summary>
public sealed class AppHostWatchdogStateAttentionService
{
    public const string EpisodeReasonPrefix = "episode=";

    private readonly AppDbContext _db;
    private readonly IAgentIncidentRecorder _incidents;
    private readonly AppHostWatchdogStateSettings _settings;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AppHostWatchdogStateAttentionService> _logger;

    public AppHostWatchdogStateAttentionService(
        AppDbContext db,
        IAgentIncidentRecorder incidents,
        IOptions<SupervisionSettings> settings,
        IHostEnvironment environment,
        ILogger<AppHostWatchdogStateAttentionService> logger)
    {
        _db = db;
        _incidents = incidents;
        _settings = settings.Value.AppHostWatchdogState;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>Returns how many NEW incidents this pass raised (not a global count).</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        var path = AppHostWatchdogStateDocument.ResolveDocumentPath(
            _settings.StateDocumentPath, _environment.ContentRootPath);
        if (!File.Exists(path))
            return 0;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Watchdog-state document unreadable at {Path}", path);
            return 0;
        }

        if (!AppHostWatchdogStateDocument.TryParse(json, out var document) || document is null)
        {
            _logger.LogWarning("Watchdog-state document at {Path} did not parse", path);
            return 0;
        }

        if (!document.IsUnhealthy || document.Maintenance || document.EpisodeId is null)
            return 0;

        var episodeKey = EpisodeReasonPrefix + document.EpisodeId.Value.ToString("D");
        var eligible = await _db.Agents.AsNoTracking()
            .Where(a => a.AlwaysOn)
            .Where(a => _db.ChatChannels.Any(c => c.AgentId == a.Id && c.Enabled))
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        if (eligible.Count == 0)
            return 0;

        var already = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.AppHostWatchdogDisabled
                && i.FailureReason != null
                && i.FailureReason.StartsWith(episodeKey))
            .Select(i => i.AgentId)
            .ToListAsync(ct);
        var known = already.Where(id => id is not null).Select(id => id!.Value).ToHashSet();

        var raised = 0;
        var since = document.DisabledSinceUtc?.UtcDateTime.ToString("o") ?? document.ObservedAtUtc.UtcDateTime.ToString("o");
        foreach (var agent in eligible)
        {
            if (known.Contains(agent.Id))
                continue;

            var message =
                $"AppHost watchdog is {document.State} since {since} (episode {document.EpisodeId:D}). "
                + "Recovery will not run until the task is re-enabled. Detection only.";
            await _incidents.RecordIncidentAsync(
                agent.Id,
                sessionId: null,
                AgentIncidentKind.AppHostWatchdogDisabled,
                AlertSeverity.Critical,
                message,
                failureReason: episodeKey,
                ct: ct);
            raised++;
            _logger.LogWarning(
                "AppHost watchdog {State} (episode {EpisodeId}) — Critical incident for agent {AgentId} {AgentName}",
                document.State, document.EpisodeId, agent.Id, agent.Name);
        }

        if (raised > 0)
            await _db.SaveChangesAsync(ct);
        return raised;
    }
}
