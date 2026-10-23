using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Explicit supported-Claude fixture setup; the default fake still supplies no observation.</summary>
internal static class ClaudeDialogFixture
{
    public static async Task EnableClaudeDialogObservationAsync(
        this FakeAgentProtocolAdapter adapter, string connectionString, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        adapter.AcceptedStartedAt = await db.AgentSessions.Where(s => s.Id == sessionId)
            .Select(s => s.StartedAt).SingleAsync();
        adapter.DialogGuardEnabled = true;
        adapter.ClaudeComposerChrome = true;
    }
}
