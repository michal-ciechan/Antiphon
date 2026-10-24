using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionRuntimeActionDeferralTests
{
    [Test]
    [Arguments("boundary-delivery")]
    [Arguments("status-delivery")]
    [Arguments("boundary-finished")]
    public async Task C1308_new_wakeup_rechecks_review_handoff_before_due(string variant)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString, AlwaysOn = false,
            ConfigureServices = services =>
            {
                services.AddSingleton<ReviewReplyDispatcher>();
                services.AddScoped<ReviewThreadService>();
                services.AddScoped<AgentFilesService>();
            }
        });
        var (source, original) = await DeferReviewAsync(h);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        var finishedOnly = variant == "boundary-finished";
        if (finishedOnly)
        {
            var cap = h.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification.MaxDeliveryAttempts;
            await using var db = h.CreateObserverContext();
            await db.SessionQueuedMessages.Where(m => m.Id == source.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.DeliveryAttempts, cap));
        }
        h.Runtime.SetTestPending(h.SessionId, null);
        if (variant == "status-delivery") await actions.RequestAsync(h.SessionId, "status", default);
        else await h.Runtime.ObserveTranscriptAsync(Entry(h, 3, TranscriptKinds.TurnEnd), default);
        var receipt = Entry(h, 4, TranscriptKinds.UserPrompt, source.Body);
        h.Adapter.OnSubmitted = body =>
        {
            body.ShouldBe(source.Body);
            return h.Runtime.ObserveTranscriptAsync(receipt, default);
        };
        await actions.DrainPageAsync(h.SessionId, default);
        h.Now.ShouldBeLessThan(original.DueAt, "the newer wake must make progress before the capped retry");
        if (finishedOnly) Finished(h).ShouldBe(1, "the later boundary must finish before the handoff retry is due");
        else h.Adapter.SubmittedBodies.ShouldBe([source.Body], "the original typed handoff must execute before its retry is due");
        await using (var db = h.CreateObserverContext())
        {
            (await db.SessionRuntimeActions.AnyAsync(a => a.Id == original.Id)).ShouldBeFalse("acknowledge the original handoff");
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == source.Id);
            row.ReviewThreadId.ShouldBe(source.ReviewThreadId);
            row.ConversationKey.ShouldBe(source.ConversationKey);
            if (!finishedOnly)
            {
                row.Status.ShouldBe(QueuedMessageStatus.Sent);
                row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
                row.DeliveryAttempts.ShouldBe(1);
                (await db.TranscriptEntries.SingleAsync(t => t.Uuid == receipt.Uuid)).Text.ShouldBe(source.Body);
            }
        }
        if (variant == "status-delivery") Finished(h).ShouldBe(0, "typed handoffs and status requests are not turn boundaries");
        await actions.DrainPageAsync(h.SessionId, default);
        h.Adapter.SubmittedBodies.Count.ShouldBe(finishedOnly ? 0 : 1);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task C1308_still_blocked_handoff_recheck_yields_and_preserves_debt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services =>
            {
                services.AddSingleton<ReviewReplyDispatcher>();
                services.AddScoped<ReviewThreadService>();
                services.AddScoped<AgentFilesService>();
            }
        });
        var (source, original) = await DeferReviewAsync(h);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        await using (var db = h.CreateObserverContext())
            (await db.SessionRuntimeActions.SingleAsync(a => a.Id == original.Id)).Attempts.ShouldBe(original.Attempts,
                "a hint without a newer request cannot bypass DueAt");

        h.Runtime.SetTestPending(h.SessionId, null);
        // This unrouted prompt must take precedence over the idle request. Its stored
        // Working state must still prevent input when the handoff executes early.
        await h.Runtime.ObserveTranscriptAsync(Entry(h, 3, TranscriptKinds.UserPrompt, "new work"), default);
        await actions.RequestAsync(h.SessionId, "status", default);
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse("a failed early recheck must not request another page");
        await using (var db = h.CreateObserverContext())
        {
            var retained = await db.SessionRuntimeActions.SingleAsync(a => a.Id == original.Id);
            retained.Attempts.ShouldBe(original.Attempts + 1, "one queue recheck per drain, not 128 bypasses of the new DueAt");
            retained.TriggerKind.ShouldBe($"review:{source.Id:N}");
            retained.AcceptedGeneration.ShouldBe(original.AcceptedGeneration);
            retained.Phase.ShouldBe(SessionRuntimeActionCoordinator.QueuePhase);
            retained.FailureReason.ShouldBe("Deferred");
            retained.DueAt.ShouldBeGreaterThan(h.Now.AddSeconds(20));
            (await db.SessionRuntimeActions.CountAsync()).ShouldBe(2, "retain the handoff and newer status debt after routing");
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == source.Id);
            row.ReviewThreadId.ShouldBe(source.ReviewThreadId);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBe(0);
            row.DeliveryVerdict.ShouldBeNull();
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, original.AcceptedGeneration!.Value.AddMinutes(1)));
        }
        // The early path must use the existing generation check, never rebind a stale handoff.
        await actions.DrainPageAsync(h.SessionId, default);
        await using (var db = h.CreateObserverContext()) (await db.SessionRuntimeActions.CountAsync()).ShouldBe(0);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();
        Finished(h).ShouldBe(0);
    }

    private static async Task<(SessionQueuedMessage Source, SessionRuntimeAction Action)> DeferReviewAsync(BridgeQueueHarness h)
    {
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "earlier completed work");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        using var scope = h.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ReviewThreadService>();
        var thread = await service.CreateAsync(h.AgentId,
            new CreateReviewThreadRequest("notes/report.md", 12, "quoted line", "Check the complete figure.", false), default);
        thread.ShouldNotBeNull();
        (await service.DispatchAsync(thread.Id, default)).ShouldBeTrue();
        SessionQueuedMessage source;
        Guid originalId;
        await using (var db = h.CreateObserverContext())
        {
            source = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
            source.ReviewThreadId.ShouldBe(thread.Id);
            var action = await db.SessionRuntimeActions.SingleAsync();
            originalId = action.Id;
            action.TriggerKind.ShouldBe($"review:{source.Id:N}");
            action.Attempts = 5; // Exercise the existing capped retry without waiting thirty seconds.
            await db.SaveChangesAsync();
        }
        h.Runtime.SetTestPending(h.SessionId, HerdrPendingReasons.Unreachable);
        await h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(h.SessionId, default);
        await using var verify = h.CreateObserverContext();
        var retained = await verify.SessionRuntimeActions.AsNoTracking().SingleAsync(a => a.Id == originalId);
        retained.TriggerKind.ShouldBe($"review:{source.Id:N}");
        retained.Phase.ShouldBe(SessionRuntimeActionCoordinator.QueuePhase);
        retained.FailureReason.ShouldBe("Deferred");
        retained.Attempts.ShouldBe(6);
        retained.DueAt.ShouldBeGreaterThan(h.Now.AddSeconds(20));
        h.Adapter.Inputs.ShouldBeEmpty();
        Finished(h).ShouldBe(0);
        return (source, retained);
    }

    [Test]
    [Arguments("parked")]
    [Arguments("stopped")]
    [Arguments("failed")]
    [Arguments("failed-rules")]
    [Arguments("accumulated-turns")]
    public async Task C1259_ineligible_pending_rows_finish_and_release_retention(string variant)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        { ConnectionString = schema.ConnectionString, TimeProvider = clock, AlwaysOn = false });
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        var cap = h.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification.MaxDeliveryAttempts;
        var attempts = variant is "parked" or "accumulated-turns" ? cap : 0;
        var pending = await h.SeedPendingMessageAsync("parked original body must never be retyped", deliveryAttempts: attempts);
        var turns = variant == "accumulated-turns" ? 6 : 1;
        long sequence = 0;
        for (var i = 0; i < turns; i++)
        {
            await h.Runtime.ObserveTranscriptAsync(Entry(h, ++sequence, TranscriptKinds.UserPrompt, $"unrelated prompt {i}"), default);
            await h.Runtime.ObserveTranscriptAsync(Entry(h, ++sequence, TranscriptKinds.TurnEnd), default);
        }
        await using (var db = h.CreateObserverContext())
        {
            if (variant is "stopped" or "failed")
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u =>
                    u.SetProperty(s => s.Status, variant == "stopped" ? SessionStatus.Stopped : SessionStatus.Failed));
            if (variant == "failed-rules")
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.GrokRulesState, GrokRulesState.Failed));
        }
        h.EventBus.Clear();
        await actions.DrainPageAsync(h.SessionId, default);
        h.Adapter.Inputs.ShouldBeEmpty();
        await using (var db = h.CreateObserverContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == pending);
            row.Status.ShouldBe(QueuedMessageStatus.Pending); row.DeliveryAttempts.ShouldBe(attempts);
            (await db.SessionRuntimeActions.CountAsync()).ShouldBe(0, "permanently ineligible input must not retain turn actions");
        }
        Finished(h).ShouldBe(turns, "each completed boundary must publish once without waiting for parked input");
        clock.Advance(TimeSpan.FromMinutes(1));
        await actions.DrainPageAsync(h.SessionId, default);
        Finished(h).ShouldBe(turns);
        await VerifyRetentionAsync(h, turns * 2);
    }

    [Test]
    public async Task C1259_transient_deferrals_back_off_without_charging_input()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        { ConnectionString = schema.ConnectionString, TimeProvider = clock });
        await h.Runtime.ObserveTranscriptAsync(Entry(h, 1, TranscriptKinds.UserPrompt, "still working"), default);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await actions.RequestAsync(h.SessionId, "status", default);
        var count = 0;
        foreach (var seconds in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await actions.DrainPageAsync(h.SessionId, default);
            await using var db = h.CreateObserverContext();
            var action = await db.SessionRuntimeActions.SingleAsync();
            action.DueAt.ShouldBe(now.AddSeconds(seconds));
            action.Attempts.ShouldBe(++count);
            await actions.DrainPageAsync(h.SessionId, default);
            (await db.SessionRuntimeActions.AsNoTracking().SingleAsync()).Attempts.ShouldBe(count, "a not-due hint cannot repeat the flush");
            clock.Advance(TimeSpan.FromSeconds(seconds));
        }
        h.Adapter.Inputs.ShouldBeEmpty();
        Finished(h).ShouldBe(0);
    }

    [Test]
    [Arguments("boundary")]
    [Arguments("status")]
    public async Task C1259_new_requests_replace_backed_off_queue_work(string replacement)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        { ConnectionString = schema.ConnectionString, TimeProvider = clock, AlwaysOn = false });
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        var cap = h.Provider.GetRequiredService<IOptions<SupervisionSettings>>().Value.DeliveryVerification.MaxDeliveryAttempts;
        await h.SeedPendingMessageAsync("never type this parked row", deliveryAttempts: cap);
        await h.Runtime.ObserveTranscriptAsync(Entry(h, 1, TranscriptKinds.UserPrompt, "first"), default);
        await h.Runtime.ObserveTranscriptAsync(Entry(h, 2, TranscriptKinds.TurnEnd), default);
        await h.Runtime.ObserveTranscriptAsync(Entry(h, 3, TranscriptKinds.UserPrompt, "working again"), default);
        await actions.DrainPageAsync(h.SessionId, default);
        Guid original;
        await using (var db = h.CreateObserverContext())
        {
            var deferred = await db.SessionRuntimeActions.SingleAsync();
            original = deferred.Id;
            deferred.DueAt.ShouldBeGreaterThan(clock.GetUtcNow().UtcDateTime);
        }
        var boundaries = 1;
        long sequence = 3;
        // New arrivals must replace old deferred work even before its due time. Repeated
        // Working turns otherwise accumulate an unbounded tail behind the oldest action.
        for (var i = 0; i < 5; i++)
        {
            if (replacement == "boundary")
            {
                await h.Runtime.ObserveTranscriptAsync(Entry(h, ++sequence, TranscriptKinds.TurnEnd), default);
                boundaries++;
            }
            await h.Runtime.ObserveTranscriptAsync(Entry(h, ++sequence, TranscriptKinds.UserPrompt, $"next working {i}"), default);
            if (replacement == "status") await actions.RequestAsync(h.SessionId, "status", default);
            await actions.DrainPageAsync(h.SessionId, default);
            await using var db = h.CreateObserverContext();
            (await db.SessionRuntimeActions.CountAsync()).ShouldBe(1, "only the newest queue wakeup may remain deferred");
            (await db.SessionRuntimeActions.AnyAsync(a => a.Id == original)).ShouldBeFalse();
            Finished(h).ShouldBe(replacement == "boundary" ? boundaries - 1 : boundaries);
        }
        await h.Runtime.ObserveTranscriptAsync(Entry(h, ++sequence, TranscriptKinds.TurnEnd), default);
        boundaries++;
        await actions.DrainPageAsync(h.SessionId, default);
        await using (var db = h.CreateObserverContext()) (await db.SessionRuntimeActions.CountAsync()).ShouldBe(0);
        Finished(h).ShouldBe(boundaries);
        h.Adapter.Inputs.ShouldBeEmpty();
        await VerifyRetentionAsync(h, (int)sequence);
    }

    private static int Finished(BridgeQueueHarness h) => h.EventBus.PublishedEvents.Count(e => e.EventName == "SessionFinished");

    private static FakeTimeProvider Clock()
    {
        var now = DateTimeOffset.UtcNow;
        return new(new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero));
    }

    private static SessionRunnerTranscriptEvent Entry(BridgeQueueHarness h, long sequence, string kind, string? text = null) =>
        new(h.SessionId, sequence, kind, Guid.NewGuid().ToString(), null, h.Clock.GetUtcNow(),
            kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text, null, null, null, null,
            kind == TranscriptKinds.TurnEnd ? "end_turn" : null);

    private static async Task VerifyRetentionAsync(BridgeQueueHarness h, int transcriptCount)
    {
        await using var db = h.CreateObserverContext();
        var old = h.Now.AddDays(-100);
        await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u =>
            u.SetProperty(s => s.Status, SessionStatus.Stopped).SetProperty(s => s.LastSeenAt, old));
        await db.TranscriptEntries.ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, old));
        await db.SessionQueuedMessages.ExecuteUpdateAsync(u => u.SetProperty(m => m.CreatedAt, old)
            .SetProperty(m => m.Status, QueuedMessageStatus.Canceled).SetProperty(m => m.CanceledAt, old));
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db, Options.Create(new RetentionSettings()), audit, h.Clock,
            NullLogger<DataRetentionService>.Instance, new AuditService(db, audit), h.Provider.GetRequiredService<SessionStateStore>());
        (await retention.PruneTranscriptsAsync(default)).ShouldBe(transcriptCount);
        (await retention.PruneQueuedMessagesAsync(default)).ShouldBe(1);
    }
}
