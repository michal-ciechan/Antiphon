using System.Text.Json;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Explicit supported-Claude fixture setup; the default fake still supplies no observation.</summary>
internal static class ClaudeDialogFixture
{
    public static AgentLaunchSpec WithNativeClaudeDialog(this AgentLaunchSpec spec) => spec with
    {
        Env = new Dictionary<string, string>(spec.Env) { ["ANTIPHON_FAKE_CLAUDE_COMPOSER"] = "1" },
        AcceptedStartedAt = spec.AcceptedStartedAt ?? SessionGeneration.Normalize(DateTime.UtcNow),
    };

    public static void ConfigurePhoneHomePeer(
        PhoneHomeScriptedPeer peer, FakeAgentProtocolAdapter terminal, Guid sessionId)
    {
        terminal.WithClaudeDialogObservation();
        peer.Reply = frame =>
        {
            object? payload = frame.Operation switch
            {
                PhoneHomeOperation.Capabilities => new RunnerCapabilitiesDto(
                    "InboxConhost", "inbox", "test", false,
                    Features: [ProviderDialogObservation.Capability]),
                PhoneHomeOperation.Get => new RunnerSessionDto(sessionId, 1,
                    terminal.AcceptedStartedAt!.Value, "Running", null, "", terminal.SnapshotSequence,
                    AcceptedStartedAt: terminal.AcceptedStartedAt),
                PhoneHomeOperation.Snapshot => new RunnerSnapshotDto(
                    sessionId, terminal.SnapshotRawOutput(), terminal.SnapshotRenderedScreen(),
                    terminal.SnapshotSequence, terminal.AcceptedStartedAt!.Value,
                    terminal.AcceptedStartedAt,
                    terminal.ObserveDialogAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult()),
                PhoneHomeOperation.Buffer => new RunnerBufferDto(
                    sessionId, terminal.SnapshotRawOutput(), terminal.SnapshotSequence),
                PhoneHomeOperation.ConditionalInput => terminal.SendConditionalInputAsync(
                    frame.Payload!.Value.Deserialize<RunnerConditionalInputRequest>(PhoneHomeFraming.Json)!,
                    CancellationToken.None).GetAwaiter().GetResult() with { SessionId = sessionId },
                _ => null,
            };
            if (frame.Operation == PhoneHomeOperation.Input)
            {
                terminal.SendInputAsync(frame.Payload!.Value.GetProperty("input").GetString()!,
                    CancellationToken.None).GetAwaiter().GetResult();
                terminal.SnapshotSequence++;
            }
            return payload is null ? null : new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));
        };
    }

    /// <summary>Conversation history above a current readable Claude composer.</summary>
    public static string ComposerScreen(string history = "", string draft = "") =>
        history + "\n" + FakeAgentProtocolAdapter.ClaudeRule + "\n❯ " + draft
        + "\n" + FakeAgentProtocolAdapter.ClaudeRule + "\n" + FakeAgentProtocolAdapter.ClaudeHintBar;

    /// <summary>Delivery fixtures model a supported Claude session using its persisted generation.</summary>
    public static Task<BridgeQueueHarness> CreateBridgeAsync(BridgeQueueHarness.HarnessOptions? options = null) =>
        BridgeQueueHarness.CreateAsync((options ?? new()) with { ClaudeDialogObservation = true });

    /// <summary>Newly launched fakes bind their generation in StartAsync before accepting input.</summary>
    public static FakeAgentProtocolAdapter WithClaudeDialogObservation(this FakeAgentProtocolAdapter adapter)
    {
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
