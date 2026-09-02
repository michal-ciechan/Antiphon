using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0324: one Critical incident per credential store, not one per corpse.
/// Episode key is the resolved <c>GROK_HOME</c>. Closed when a later registry-path
/// Grok session (no TUI profile revision) reaches <see cref="SessionStatus.Running"/>.
/// </summary>
internal static class GrokSignInIncident
{
    public const string EpisodeKeyPrefix = "grok-home:";

    public static string EpisodeKey(string grokHome) => EpisodeKeyPrefix + grokHome;

    public static async Task RecordAsync(
        AppDbContext db,
        AgentSupervisorService? supervisor,
        Guid? agentId,
        Guid? sessionId,
        string grokHome,
        string reason,
        CancellationToken ct)
    {
        if (await HasOpenEpisodeAsync(db, grokHome, ct))
            return;

        if (supervisor is not null)
        {
            await supervisor.RecordIncidentAsync(
                agentId,
                sessionId,
                AgentIncidentKind.ProviderSignInRequired,
                AlertSeverity.Critical,
                reason,
                failureReason: EpisodeKey(grokHome),
                ct: ct);
            return;
        }

        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = AgentIncidentKind.ProviderSignInRequired,
            Severity = AlertSeverity.Critical,
            Message = ColumnText.Clip(reason, AgentIncident.MessageMaxLength),
            FailureReason = ColumnText.ClipOrNull(EpisodeKey(grokHome), AgentIncident.FailureReasonMaxLength),
            CreatedAt = DateTime.UtcNow,
        });
    }

    public static async Task<bool> HasOpenEpisodeAsync(
        AppDbContext db, string grokHome, CancellationToken ct)
    {
        var key = EpisodeKey(grokHome);
        var latest = await db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.ProviderSignInRequired
                && i.FailureReason == key)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => (DateTime?)i.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (latest is null)
            return false;
        return !await IsClosedAsync(db, latest.Value, ct);
    }

    /// <summary>
    /// A later registry-path Grok launch reported ready. Profile-path sessions
    /// (the standing <c>gkp</c> case) have a TUI profile revision and must not
    /// close a pool-home episode.
    /// </summary>
    public static Task<bool> IsClosedAsync(
        AppDbContext db, DateTime incidentCreatedAt, CancellationToken ct) =>
        db.AgentSessions.AsNoTracking().AnyAsync(
            s => s.AgentKind == AgentKind.Grok
                && s.TuiProfileRevisionId == null
                && s.Status == SessionStatus.Running
                && s.CreatedAt > incidentCreatedAt,
            ct);

    public static SessionLaunchBlock ToSessionBlock(AgentLaunchBlockKind kind) => kind switch
    {
        AgentLaunchBlockKind.ProviderSignInRequired => SessionLaunchBlock.ProviderSignInRequired,
        AgentLaunchBlockKind.TrustDialogNotCleared => SessionLaunchBlock.TrustDialogNotCleared,
        _ => SessionLaunchBlock.None,
    };
}
