using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// PR 7 of the Telegram bot agents plan: a CompactBoundary transcript event produces an Info-level
/// ContextCompacted incident (timeline row, NO alert), a persisted watermark that survives the
/// tailer's replay-from-offset-0 behaviour AND incident pruning, and — only for agents with a
/// channel preamble — one verified recovery note. Driven through the REAL
/// AgentSessionRuntime.ObserveTranscriptAsync path (persist + lazy dispatch), not by calling the
/// service directly.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class CompactionRecoveryTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureServices = services => services.AddSingleton<CompactionRecoveryService>(),
        });

    private static async Task SetPreambleAsync(BridgeQueueHarness h)
    {
        await using var db = CreateContext();
        await db.Agents.Where(a => a.Id == h.AgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.SystemPromptAppend, "You are {agentName}."));
    }

    private static SessionRunnerTranscriptEvent Boundary(Guid sessionId, long sequence) => new(
        sessionId, sequence, TranscriptKinds.CompactBoundary,
        Guid.NewGuid().ToString(), null, DateTimeOffset.UtcNow, null,
        "Context compacted (manual)", null, null, null, null, null);

    [Test]
    public async Task Compact_boundary_records_info_incident_and_enqueues_one_recovery_note()
    {
        await using var h = await CreateHarnessAsync();
        await SetPreambleAsync(h);

        await h.Runtime.ObserveTranscriptAsync(Boundary(h.SessionId, 5), CancellationToken.None);

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ContextCompacted);
        incident.Severity.ShouldBe(AlertSeverity.Info);

        // Idle session + the PR 6 exclusion → the WhenIdle note takes the idle fast-path,
        // delivered VERIFIED (composer echo + submit ack) right away.
        h.Adapter.SubmittedBodies.ShouldBe([ChannelPreamble.RecoveryNoteBody]);

        (await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId))
            .CompactionRecoveryWatermark.ShouldBe(5);
    }

    [Test]
    public async Task Compaction_incident_does_not_raise_alert()
    {
        await using var h = await CreateHarnessAsync();
        await SetPreambleAsync(h);

        await h.Runtime.ObserveTranscriptAsync(Boundary(h.SessionId, 3), CancellationToken.None);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ContextCompacted)).ShouldBeTrue();
        (await db.Alerts.AnyAsync(a => a.AgentId == h.AgentId))
            .ShouldBeFalse("compaction is normal operation — an incident row, never an alert");
    }

    [Test]
    public async Task Duplicate_boundary_events_are_deduped_after_simulated_replay()
    {
        await using var h = await CreateHarnessAsync();
        await SetPreambleAsync(h);

        await h.Runtime.ObserveTranscriptAsync(Boundary(h.SessionId, 7), CancellationToken.None);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);

        // Prove the dedupe is independent of incident rows (PruneIncidentsAsync would delete them).
        await using (var db = CreateContext())
        {
            await db.AgentIncidents.Where(i => i.AgentId == h.AgentId).ExecuteDeleteAsync();
        }

        // A runner restart replays the tail from offset 0 through a FRESH service instance (the
        // in-memory latch is gone) — the persisted watermark must still hold.
        var fresh = new CompactionRecoveryService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Queue,
            NullLogger<CompactionRecoveryService>.Instance);
        await fresh.OnCompactBoundaryAsync(h.SessionId, 7, CancellationToken.None);
        await fresh.OnCompactBoundaryAsync(h.SessionId, 3, CancellationToken.None);

        h.Adapter.SubmittedBodies.Count.ShouldBe(1, "replayed boundaries must not re-fire the note");
        await using var verify = CreateContext();
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId))
            .ShouldBe(0, "replayed boundaries must not re-record incidents either");
    }

    [Test]
    public async Task Agent_without_preamble_gets_incident_but_no_note()
    {
        await using var h = await CreateHarnessAsync();

        await h.Runtime.ObserveTranscriptAsync(Boundary(h.SessionId, 4), CancellationToken.None);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ContextCompacted)).ShouldBeTrue();
        h.Adapter.SubmittedBodies.ShouldBeEmpty("a plain dev agent never gets bot-flavored notes typed at it");
        (await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == h.SessionId)).ShouldBeFalse();
    }

    [Test]
    public async Task Mid_work_compaction_defers_the_note_to_the_next_turn_end()
    {
        await using var h = await CreateHarnessAsync();
        await SetPreambleAsync(h);
        await h.MarkWorkingAsync(); // activity after the (nonexistent) last turn end → working

        await h.Runtime.ObserveTranscriptAsync(Boundary(h.SessionId, 9), CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBeEmpty("a working session must not be interrupted");
        await using (var db = CreateContext())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
                .Status.ShouldBe(QueuedMessageStatus.Pending);
        }

        // The turn ends → the queued note flushes through the normal idle path.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldBe([ChannelPreamble.RecoveryNoteBody]);
    }
}
