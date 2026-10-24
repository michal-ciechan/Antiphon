using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Explicit supported-Claude fixture setup; the default fake still supplies no observation.</summary>
internal static class ClaudeDialogFixture
{
    /// <summary>Delivery fixtures model a supported Claude session using its persisted generation.</summary>
    public static Task<BridgeQueueHarness> CreateBridgeAsync(BridgeQueueHarness.HarnessOptions? options = null) =>
        BridgeQueueHarness.CreateAsync((options ?? new()) with { ClaudeDialogObservation = true });

    /// <summary>Newly launched fakes bind their generation in StartAsync before accepting input.</summary>
    public static FakeAgentProtocolAdapter WithClaudeDialogObservation(this FakeAgentProtocolAdapter adapter, bool recordPromptAttempts = false)
    {
        if (recordPromptAttempts) adapter.RecordGuardedPromptAttempts = true;
        adapter.DialogGuardEnabled = true;
        adapter.ClaudeComposerChrome = true;
        return adapter;
    }

    /// <summary>For synchronous fixture attachment to an already persisted session.</summary>
    public static void EnableClaudeDialogObservation(
        this FakeAgentProtocolAdapter adapter, string connectionString, Guid sessionId)
    {
        using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        adapter.AcceptedStartedAt = db.AgentSessions.Where(s => s.Id == sessionId)
            .Select(s => s.StartedAt).Single();
        adapter.WithClaudeDialogObservation();
    }

    public static async Task EnableClaudeDialogObservationAsync(
        this FakeAgentProtocolAdapter adapter, string connectionString, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        adapter.AcceptedStartedAt = await db.AgentSessions.Where(s => s.Id == sessionId)
            .Select(s => s.StartedAt).SingleAsync();
        adapter.WithClaudeDialogObservation();
    }
}
