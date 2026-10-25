using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingResumeReceiptReadinessTests
{
    private const string NoteBody = "Restart obligation for this launch: resume the standing work and retain its delivery identity.";
    private static string NoteKey(DateTime generation) => $"standing-resume-note:{SessionGeneration.Normalize(generation).Ticks}";

    [Test]
    public async Task C1574_Resume_note_waits_then_receipts_once()
    {
        await using var h = await AgentSystemPromptLaunchTests.CreateHarnessAsync(true);
        await AgentSystemPromptLaunchTests.SetPreambleAsync(h, "Standing agent {{agent.name}}");
        await using (var db = h.CreateObserverContext())
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
        await h.Runtime.DisposeSessionAsync(h.SessionId);
        h.Runner.TranscriptPull = async (id, _) =>
        {
            await using var db = h.CreateObserverContext();
            var generation = (await db.AgentSessions.SingleAsync(s => s.Id == id)).StartedAt;
            return new(id, [], 0, AcceptedStartedAt: generation,
                Readiness: new("Behind", "launch-binding", 0, 100));
        };
        var started = await AgentSystemPromptLaunchTests.StartAsync(h, false);
        started.PersistentSessionId.ShouldBe(h.SessionId.ToString("D"));
        var adapter = AgentSystemPromptLaunchTests.Factory(h).Created.ShouldHaveSingleItem();
        adapter.Inputs.ShouldBeEmpty("launch completes without typing into unread history");
        await using var verify = h.CreateObserverContext();
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        var row = (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldHaveSingleItem();
        row.RuntimeActionKey.ShouldBe(NoteKey(session.StartedAt));
        row.Origin.ShouldBe(QueuedMessageOrigin.System);
        row.DeliveryAttempts.ShouldBe(0);
        // An old identical prompt cannot satisfy the new attempt's floor.
        await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt, row.Body,
            timestamp: session.StartedAt.AddMinutes(-10));
        await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.TurnEnd);
        h.Runner.TranscriptPull = (id, _) => Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0,
            AcceptedStartedAt: session.StartedAt, Readiness: new("Ready", "launch-binding", 100, 100)));
        await h.Queue.FlushSessionAsync(h.SessionId, default);
        adapter.SubmittedBodies.ShouldBe([row.Body]);
        await verify.Entry(row).ReloadAsync();
        row.DeliveryAttempts.ShouldBe(1);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
        adapter.KillGenerationCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task C1574_Accepted_note_outlives_grace_without_retyping()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString, TimeProvider = new ScaledTimeProvider(1),
            ConfigureDeliveryVerification = v => { v.TranscriptConfirmTimeoutSeconds = 1; v.PostFailureConfirmGraceSeconds = 1; }
        });
        var generation = await OwnAsync(h);
        var progress = new SessionRunnerTranscriptReadiness("Ready", "binding", 100, 100);
        var entries = new List<SessionRunnerTranscriptEvent>();
        h.Runner.TranscriptPull = (id, _) => Task.FromResult(new SessionRunnerTranscriptDto(id, entries.ToArray(),
            entries.Count, AcceptedStartedAt: generation, Readiness: progress));
        h.Adapter.OnSubmitted = _ => { progress = progress with { Status = "Behind", ParsedThrough = 50 }; return Task.CompletedTask; };
        var id = await EnqueueNoteAsync(h, generation);
        await h.Queue.FlushSessionAsync(h.SessionId, default);
        h.Adapter.SubmittedBodies.ShouldBe([NoteBody]);
        h.Adapter.KillGenerationCalls.ShouldBeEmpty("absence during replay cannot kill");
        var original = await RowAsync(h, id);
        original.DeliveryAttempts.ShouldBe(1);
        original.Status.ShouldBe(QueuedMessageStatus.Pending);
        var inputCount = h.Adapter.Inputs.Count;
        ((ScaledTimeProvider)h.Clock).Advance(TimeSpan.FromMinutes(24));
        foreach (var status in new[] { "Behind", "Unknown" })
        {
            progress = progress with { Status = status };
            await h.Queue.FlushSessionAsync(h.SessionId, default);
            try { await h.Queue.EnqueueAsync(h.SessionId, "Automated input must wait for readiness", MessageSendMode.Now,
                default, origin: QueuedMessageOrigin.System); } catch (ConflictException) { }
            await h.Queue.SendNowAsync(h.SessionId, id, default);
            h.Adapter.Inputs.Count.ShouldBe(inputCount, status);
            var current = await RowAsync(h, id);
            current.DeliveryAttempts.ShouldBe(original.DeliveryAttempts);
            current.LastDeliveryStartedAt.ShouldBe(original.LastDeliveryStartedAt);
            current.LastDeliveryBaselineSequence.ShouldBe(original.LastDeliveryBaselineSequence);
            h.Adapter.KillGenerationCalls.ShouldBeEmpty(status);
        }
        entries.Add(new(h.SessionId, 1, TranscriptKinds.UserPrompt, "late-receipt", null,
            new DateTimeOffset(generation), "user", NoteBody, null, null, null, null, null));
        await h.Queue.FlushSessionAsync(h.SessionId, default);
        var final = await RowAsync(h, id);
        final.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        final.DeliveryAttempts.ShouldBe(1);
        h.Adapter.Inputs.Count.ShouldBe(inputCount);
    }

    [Test]
    public async Task C1574_Recreated_services_recover_the_original_note()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid sessionId, agentId, id; DateTime generation; SessionQueuedMessage original;
        await using (var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        { ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true }))
        {
            generation = await OwnAsync(h); sessionId = h.SessionId; agentId = h.AgentId;
            id = await EnqueueNoteAsync(h, generation);
            await using var db = h.CreateObserverContext();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Status = QueuedMessageStatus.Sent; row.SentAt = row.LastDeliveryStartedAt = h.Now.AddMinutes(-124);
            row.LastDeliveryGeneration = generation; row.LastDeliveryBaselineSequence = 0; row.DeliveryAttempts = 1;
            await db.SaveChangesAsync(); original = await RowAsync(h, id);
        }
        await using var rebuilt = await ClaudeDialogFixture.CreateBridgeAsync(new()
        { ConnectionString = schema.ConnectionString, AttachSessionId = sessionId, AttachAgentId = agentId });
        rebuilt.Runner.TranscriptPull = (sid, _) => Task.FromResult(new SessionRunnerTranscriptDto(sid, [], 0,
            AcceptedStartedAt: generation, Readiness: new("Behind", "binding", 0, 100)));
        await rebuilt.Queue.TryPollLocalCommandAsync(sessionId,
            new LocalCommandPoll(AgentKind.ClaudeCode, "/usage", [], OpensOverlay: true,
                OverlaySettleMs: 0, PanelTimeoutSeconds: 1), default);
        rebuilt.Adapter.Inputs.ShouldBeEmpty("local-command Esc is automatic input too");
        await rebuilt.Queue.FlushSessionAsync(sessionId, default);
        rebuilt.Adapter.Inputs.ShouldBeEmpty("interrupted claims cannot re-Enter or retype while Behind");
        var after = await RowAsync(rebuilt, id);
        after.Status.ShouldBe(original.Status); after.DeliveryAttempts.ShouldBe(1);
        after.LastDeliveryStartedAt.ShouldBe(original.LastDeliveryStartedAt);
        after.LastDeliveryGeneration.ShouldBe(original.LastDeliveryGeneration);
        after.LastDeliveryBaselineSequence.ShouldBe(0);
        await EnqueueNoteAsync(rebuilt, generation);
        await using (var db = rebuilt.CreateObserverContext())
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId)).ShouldBe(1);
        rebuilt.Runner.TranscriptPull = (sid, _) => Task.FromResult(new SessionRunnerTranscriptDto(sid,
            [new(sid, 1, TranscriptKinds.UserPrompt, "reconstructed-receipt", null, new DateTimeOffset(generation),
                "user", NoteBody, null, null, null, null, null)], 1, AcceptedStartedAt: generation,
            Readiness: new("Behind", "binding", 50, 100)));
        await rebuilt.Queue.FlushStrandedQueuesAsync(default);
        (await RowAsync(rebuilt, id)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        rebuilt.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C1574_Ready_absence_keeps_existing_recovery_rules()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var variant in new[] { "idle", "working", "interactive", "generation", "final-receipt" })
        {
            await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
            { ConnectionString = schema.ConnectionString, AlwaysOn = variant != "interactive",
                ConfigureDeliveryVerification = v => v.PostFailureConfirmGraceSeconds = 1 });
            var generation = await OwnAsync(h);
            var id = await EnqueueNoteAsync(h, generation);
            await using (var db = h.CreateObserverContext())
            {
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
                row.Status = QueuedMessageStatus.Sent; row.SentAt = row.LastDeliveryStartedAt = h.Now;
                row.DeliveryAttempts = 1; row.LastDeliveryGeneration = generation; row.LastDeliveryBaselineSequence = 0;
                if (variant == "generation")
                    await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, generation.AddSeconds(1)));
                await db.SaveChangesAsync();
            }
            if (variant == "working") await h.MarkWorkingAsync();
            h.Runner.TranscriptPull = (sid, _) => Task.FromResult(new SessionRunnerTranscriptDto(sid,
                variant == "final-receipt" ? [new(sid, 1, TranscriptKinds.UserPrompt, "final", null,
                    new DateTimeOffset(generation), "user", NoteBody, null, null, null, null, null)] : [],
                variant == "final-receipt" ? 1 : 0, AcceptedStartedAt: generation,
                Readiness: new("Ready", "binding", 100, 100)));
            await h.Queue.HandleDeliveryFailureAsync(h.SessionId, [id], DeliveryVerdict.NoTranscriptRecord,
                default, generation);
            h.Adapter.KillGenerationCalls.Count.ShouldBe(variant == "idle" ? 1 : 0, variant);
            if (variant == "generation") h.Adapter.Killed.ShouldBeFalse();
            Console.WriteLine($"C1574 genuine-absence variant={variant} conditionalCalls={h.Adapter.KillGenerationCalls.Count}");
        }
    }

    [Test]
    public async Task C1574_Ordinary_repeated_note_body_keeps_direct_send_contract()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString, AlwaysOn = false,
            ConfigureDeliveryVerification = v => { v.TranscriptConfirmEnabled = false; v.TranscriptConfirmTimeoutSeconds = 1; }
        });
        var generation = await OwnAsync(h);
        var id = await EnqueueNoteAsync(h, generation);
        await using (var db = h.CreateObserverContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.Status = QueuedMessageStatus.Sent;
            row.DeliveryAttempts = 1;
            row.LastDeliveryStartedAt = row.DeliveryVerdictAt = row.SentAt = h.Now;
            row.LastDeliveryGeneration = generation; row.LastDeliveryBaselineSequence = 0;
            row.DeliveryVerdict = DeliveryVerdict.Delivered;
            await db.SaveChangesAsync();
        }
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        // Identical text is an ordinary unkeyed Now send after the original obligation settled.
        // It retains the explicit generic-confirmation setting and does not become a resume note.
        var result = await h.Queue.EnqueueAsync(h.SessionId, NoteBody, MessageSendMode.Now, default);
        result.LastDelivery!.Verdict.ShouldBe(nameof(DeliveryVerdict.Delivered));
        h.Adapter.SubmittedBodies.ShouldBe([NoteBody]);
    }

    private static async Task<DateTime> OwnAsync(BridgeQueueHarness h)
    {
        await using var db = h.CreateObserverContext();
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StandingAgentId, h.AgentId));
        return (await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).StartedAt;
    }
    private static async Task<Guid> EnqueueNoteAsync(BridgeQueueHarness h, DateTime generation)
    {
        await h.Queue.EnqueueAsync(h.SessionId, NoteBody, MessageSendMode.WhenIdle, default,
            origin: QueuedMessageOrigin.System, deliverIfIdle: false, runtimeActionKey: NoteKey(generation));
        await using var db = h.CreateObserverContext();
        return (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.RuntimeActionKey == NoteKey(generation))).Id;
    }
    private static async Task<SessionQueuedMessage> RowAsync(BridgeQueueHarness h, Guid id)
    {
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }
}
