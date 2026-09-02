using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0324: one Critical incident per GROK_HOME; a later registry-path Grok ready launch
/// closes the episode. A standing profile (TuiProfileRevisionId set) must not close it.
/// </summary>
[Category("Integration")]
public class GrokSignInIncidentTests
{
    [Test]
    public async Task An_open_episode_stays_open_until_a_registry_Grok_session_is_Running()
    {
        var home = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var now = DateTime.UtcNow;
        var incidentId = Guid.NewGuid();
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = incidentId,
            Kind = AgentIncidentKind.ProviderSignInRequired,
            Severity = AlertSeverity.Critical,
            Message = "sign in",
            FailureReason = GrokSignInIncident.EpisodeKey(home),
            CreatedAt = now.AddMinutes(-5),
        });
        await db.SaveChangesAsync();

        var sessionIds = new List<Guid>();
        try
        {
            (await GrokSignInIncident.HasOpenEpisodeAsync(db, home, CancellationToken.None))
                .ShouldBeTrue("no later ready launch yet");

            var failedId = Guid.NewGuid();
            sessionIds.Add(failedId);
            db.AgentSessions.Add(Session(failedId, AgentKind.Grok, SessionStatus.Failed, now.AddMinutes(-1)));
            await db.SaveChangesAsync();
            (await GrokSignInIncident.HasOpenEpisodeAsync(db, home, CancellationToken.None))
                .ShouldBeTrue("a Failed Grok corpse is not a ready launch");

            var claudeId = Guid.NewGuid();
            sessionIds.Add(claudeId);
            db.AgentSessions.Add(Session(claudeId, AgentKind.ClaudeCode, SessionStatus.Running, now));
            await db.SaveChangesAsync();
            (await GrokSignInIncident.HasOpenEpisodeAsync(db, home, CancellationToken.None))
                .ShouldBeTrue("a Claude session is a different provider");

            var readyId = Guid.NewGuid();
            sessionIds.Add(readyId);
            db.AgentSessions.Add(Session(readyId, AgentKind.Grok, SessionStatus.Running, now));
            await db.SaveChangesAsync();
            (await GrokSignInIncident.HasOpenEpisodeAsync(db, home, CancellationToken.None))
                .ShouldBeFalse("a later registry-path Grok Running session closes the episode");
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.AgentIncidents.Where(i => i.Id == incidentId).ExecuteDeleteAsync();
            await cleanup.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        }
    }

    private static AgentSession Session(
        Guid id, AgentKind kind, SessionStatus status, DateTime created) => new()
    {
        Id = id,
        DefinitionName = kind == AgentKind.Grok ? "grok" : "claude",
        AgentKind = kind,
        Status = status,
        Cwd = Path.GetTempPath(),
        Cols = 120,
        Rows = 30,
        CreatedAt = created,
        StartedAt = created,
        LastSeenAt = created,
    };
}
