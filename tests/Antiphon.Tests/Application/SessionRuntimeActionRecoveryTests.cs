using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class SessionRuntimeActionRecoveryTests
{
    [Test]
    public async Task C1259_uncertain_direct_roots_cannot_starve_a_later_recoverable_tail()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string prompt = "[Telegram Family] Recoverable later question";
        await h.SeedChannelCorrelationAsync(prompt, "telegram:later-recipient");
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1000, "UserPrompt", prompt), Entry(h, 1001, "AssistantText", "first"), Entry(h, 1002, "TurnEnd", null)]);
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 1002, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first"]);
        await using (var db = h.CreateObserverContext())
        {
            for (var i = 1; i <= SessionRuntimeActionCoordinator.PageSize; i++)
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = i + 1,
                    Body = "historical uncertain request", Status = QueuedMessageStatus.Sent,
                    CreatedAt = h.Now.AddHours(-1), ChannelReplySettledAt = h.Now,
                    DirectReplyPromptSequence = i, DirectReplyThroughSequence = i,
                    DirectReplyPendingThroughSequence = i + 1, DirectReplyProvider = "telegram",
                    DirectReplyConversationId = "uncertain-" + i
                });
            await db.SaveChangesAsync();
        }
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1003, "AssistantText", "recoverable trailing answer"), Entry(h, 1004, "UserPrompt", "next question")]);
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 1003, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first", "recoverable trailing answer"],
            "an uncertain full page must not hide the later recoverable direct root");
        producer.Sent.ShouldAllBe(r => r.ConversationId == "later-recipient");
        await using var verify = h.CreateObserverContext();
        (await verify.SessionQueuedMessages.CountAsync(m => m.DirectReplyPendingThroughSequence != null)).ShouldBe(128);
        (await verify.SessionQueuedMessages.CountAsync(m => m.DirectReplyTailClosedAt != null)).ShouldBe(1);
    }

    [Test]
    [Arguments("complete")]
    [Arguments("incomplete")]
    [Arguments("stale-generation")]
    [Arguments("resume-during-send")]
    public async Task C1259_terminal_direct_tail_catches_up_before_closing(string variant)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string prompt = "[Telegram Family] Final question";
        await h.SeedChannelCorrelationAsync(prompt, "telegram:final-recipient");
        var initial = new[] { Entry(h, 1, "UserPrompt", prompt), Entry(h, 2, "AssistantText", "first"), Entry(h, 3, "TurnEnd", null) };
        await h.Runtime.PersistTranscriptAsync(h.SessionId, initial);
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 3, default);
        DateTime generation;
        await using (var db = h.CreateObserverContext())
        {
            generation = await db.AgentSessions.Where(s => s.Id == h.SessionId).Select(s => s.StartedAt).SingleAsync();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
        }
        var late = Entry(h, 4, "AssistantText", "last answer from terminal snapshot");
        var snapshot = new SessionRunnerTranscriptDto(h.SessionId, [.. initial, late], 4,
            AcceptedStartedAt: generation, TerminalComplete: true);
        h.Runner.SetTranscript(snapshot with
        {
            TerminalComplete = variant != "incomplete",
            AcceptedStartedAt = variant == "stale-generation" ? generation.AddMinutes(-1) : generation
        });
        if (variant is "incomplete" or "stale-generation")
        {
            await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 3, default);
            producer.Sent.Select(r => r.Text).ShouldBe(["first"]);
            await using var db = h.CreateObserverContext();
            (await db.SessionQueuedMessages.SingleAsync()).DirectReplyTailClosedAt.ShouldBeNull();
            h.Runner.SetTranscript(snapshot);
        }
        if (variant == "resume-during-send") producer.OnSend = async () =>
        {
            await using var db = h.CreateObserverContext();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.StartedAt, generation.AddMinutes(1)).SetProperty(s => s.Status, SessionStatus.Running));
        };
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 3, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first", "last answer from terminal snapshot"],
            "terminal catch-up must publish the final suffix before closing its evidence");
        producer.Sent.ShouldAllBe(r => r.ConversationId == "final-recipient");
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 3, default);
        producer.Sent.Count.ShouldBe(2);
        await using var verify = h.CreateObserverContext();
        var root = await verify.SessionQueuedMessages.SingleAsync();
        root.DirectReplyThroughSequence.ShouldBe(4);
        if (variant == "resume-during-send") root.DirectReplyTailClosedAt.ShouldBeNull("closure must recheck terminal generation after send");
        else root.DirectReplyTailClosedAt.ShouldNotBeNull("a proven complete terminal tail must release retention protection");
        (await verify.TranscriptEntries.SingleAsync(t => t.Uuid == late.Uuid)).Text.ShouldBe(late.Text);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1259_admitted_task_attempt_cannot_settle_its_replacement(bool catchup)
        => await VerifyTaskAttemptReplacementAsync(catchup);

    internal static async Task VerifyTaskAttemptReplacementAsync(bool catchup)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton<AgentTaskReplyService>();
                s.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.GetTempPath() });
            }
        });
        var taskId = Guid.NewGuid();
        await using (var db = h.CreateObserverContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, AgentSessionId = h.SessionId,
                Status = AgentTaskStatus.Working, Attempt = 1, Title = "attempt fence", Role = AgentTaskRole.Docs,
                AgentId = h.AgentId, Workspace = WorkspaceMode.Shared, WorkingDirectory = h.TempRoot,
                CreatedAt = h.Now.AddMinutes(-2), DispatchedAt = h.Now.AddMinutes(-1),
                CommitOnSettle = CommitOnSettlePolicy.Never
            });
            await db.SaveChangesAsync();
        }
        var prompt = DelegationReportFormatter.TaskMarker(taskId);
        var report = $"Original attempt needs a decision.\n[antiphon-report:{DelegationReportFormatter.Short(taskId)} blocked]";
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", prompt), Entry(h, 2, "AssistantText", report), Entry(h, 3, "TurnEnd", null)]);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        if (catchup)
        {
            await using var db = h.CreateObserverContext();
            await db.SessionRuntimeActions.ExecuteDeleteAsync();
            await actions.RequestAsync(h.SessionId, "catchup", default);
        }
        await using (var db = h.CreateObserverContext())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Attempt, 2));
        await actions.DrainPageAsync(h.SessionId, default);
        await using var verify = h.CreateObserverContext();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Attempt.ShouldBe(2);
        task.Status.ShouldBe(AgentTaskStatus.Working, "a persisted action belongs to its admitted task attempt");
        task.Result.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId)).ShouldBe(0);
        (await verify.SessionRuntimeActions.CountAsync()).ShouldBe(0, "stale settlement is superseded without retrying another attempt");
    }

    [Test]
    [Arguments("restart")]
    [Arguments("failed-tail")]
    public async Task C1259_direct_tail_recovers_its_original_turn_and_recipient(string variant)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string destination = "original-direct-recipient";
        const string prompt = "[Telegram Family] Original question";
        await h.SeedChannelCorrelationAsync(prompt, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", prompt), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 3, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first answer"]);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "AssistantText", "late answer"), Entry(h, 5, "UserPrompt", "new unrelated turn"),
                Entry(h, 6, "AssistantText", "must not escape"), Entry(h, 7, "TurnEnd", null)]);
        var dispatcher = variant == "restart" ? new ChannelReplyDispatcher(
            h.Provider.GetRequiredService<IServiceScopeFactory>(), producer,
            h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock,
            NullLogger<ChannelReplyDispatcher>.Instance) : h.Dispatcher;
        if (variant == "failed-tail")
        {
            producer.FailNext = true;
            try { await dispatcher.OnTranscriptActionAsync(h.SessionId, 4, default); }
            catch (IOException) { }
            producer.Failures.ShouldBe(1, "the real trailing producer must be reached");
        }
        await dispatcher.OnTranscriptActionAsync(h.SessionId, 4, default);
        await dispatcher.OnTranscriptActionAsync(h.SessionId, 4, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first answer", "late answer"]);
        producer.Sent.ShouldAllBe(r => r.ConversationId == destination);
    }

    // CARD-1422: a failing channel reply becomes its own debt. The turn's other phases
    // finish, the reply is retried after the dispatcher recovers, and replies keep their order.
    [Test]
    public async Task C1422_failed_channel_reply_is_retried_in_order_after_the_dispatcher_recovers()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer { Failing = true };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string destination = "ordered-recipient";
        const string first = "[Telegram Family] First question";
        const string second = "[Telegram Family] Second question";
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await h.SeedChannelCorrelationAsync(first, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", first), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);

        producer.Failures.ShouldBeGreaterThan(0, "the real producer must be reached");
        producer.Sent.ShouldBeEmpty();
        await using (var db = h.CreateObserverContext())
        {
            var pending = await db.SessionRuntimeActions.AsNoTracking().Where(a => a.AgentSessionId == h.SessionId).ToListAsync();
            var debt = pending.Where(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt)
                .OrderBy(a => a.Sequence).ToList();
            debt.ShouldNotBeEmpty();
            debt[0].Attempts.ShouldBeGreaterThan(0);
            debt[0].FailureReason.ShouldNotBeNull();
            pending.Where(a => a.TriggerKind != SessionRuntimeActionCoordinator.ChannelReplyDebt)
                .ShouldAllBe(a => a.Phase > SessionRuntimeActionCoordinator.ChannelReplyPhase,
                    "the failed reply cannot hold the turn's later phases");
            // Hold the first reply's backoff open while the next turn arrives.
            await db.SessionRuntimeActions.Where(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddHours(1)));
        }

        producer.Failing = false;
        await h.SeedChannelCorrelationAsync(second, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "UserPrompt", second), Entry(h, 5, "AssistantText", "second answer"), Entry(h, 6, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.ShouldBeEmpty("a later reply cannot overtake an earlier owed reply");

        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddSeconds(-1)));
        await actions.DrainPageAsync(h.SessionId, default);

        producer.Sent.Select(r => r.Text).ShouldBe(["first answer", "second answer"]);
        producer.Sent.ShouldAllBe(r => r.ConversationId == destination);
        await using var verify = h.CreateObserverContext();
        (await verify.SessionRuntimeActions.AnyAsync(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt))
            .ShouldBeFalse("delivered replies leave no debt");
    }

    // CARD-1435 (CARD-1422 rule): next-message delivery does not wait for channel debt. A failed
    // reply is owed as its own debt row; with no late-confirm in play, the turn end still types
    // the next queued message.
    [Test]
    public async Task C1435_owed_channel_reply_does_not_hold_next_message_delivery()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer { Failing = true };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string prompt = "[Telegram Family] Question whose reply fails";
        const string next = "C1435 next queued message";
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await h.SeedChannelCorrelationAsync(prompt, "telegram:debt-recipient");
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", prompt), Entry(h, 2, "AssistantText", "owed answer"), Entry(h, 3, "TurnEnd", null)]);
        var queued = await h.SeedPendingMessageAsync(next);
        await actions.DrainPageAsync(h.SessionId, default);

        producer.Failures.ShouldBeGreaterThan(0, "the real producer must be reached");
        producer.Sent.ShouldBeEmpty();
        await using var verify = h.CreateObserverContext();
        (await verify.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == h.SessionId
            && a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt))
            .ShouldBeTrue("the failed reply is owed as channel debt");
        h.Adapter.SubmittedBodies.ShouldBe([next], "an owed channel reply must not hold the next queued message");
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == queued)).Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    // CARD-1435 (CARD-1422 rule): a late-confirm publication waits while an earlier channel reply
    // for the session is still owed, then publishes after it, in transcript order.
    [Test]
    public async Task C1435_late_confirm_publication_waits_for_an_earlier_owed_reply()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer { Failing = true };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string destination = "late-confirm-recipient";
        const string first = "[Telegram Family] First question";
        const string second = "[Telegram Family] Second question, typed once";
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await h.SeedChannelCorrelationAsync(first, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", first), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Failures.ShouldBeGreaterThan(0, "the real producer must be reached");
        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.Where(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddHours(1)));

        // The second channel message was typed once and is confirmed late by the next turn end.
        producer.Failing = false;
        var typed = await h.SeedPendingMessageAsync(second, deliveryAttempts: 1, baselineSequence: 3,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "UserPrompt", second), Entry(h, 5, "AssistantText", "second answer"), Entry(h, 6, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);

        producer.Sent.ShouldBeEmpty("a late-confirmed reply cannot publish past an earlier owed reply");
        await using (var db = h.CreateObserverContext())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == typed)).DeliveryVerdict
                .ShouldBe(DeliveryVerdict.LateConfirmed);
            // CARD-1433: the waiting publication is channel debt at its own sequence, not a
            // retained queue-phase action, so later queue actions are not held behind it.
            (await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == h.SessionId
                && a.TriggerKind != SessionRuntimeActionCoordinator.ChannelReplyDebt))
                .ShouldBeFalse("a waiting late-confirm publication is channel debt, not a retained action");
            var waiting = await db.SessionRuntimeActions.AsNoTracking().SingleAsync(a => a.AgentSessionId == h.SessionId
                && a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt && a.Sequence == 6);
            waiting.LateConfirmedChannelMessageIds.ShouldContain(typed);
            waiting.Attempts.ShouldBe(0, "it waits behind the earlier owed reply; it has not failed");
            waiting.ConversationKey.ShouldBe("telegram:" + destination);
            await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddSeconds(-1)));
        }

        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first answer", "second answer"]);
        producer.Sent.ShouldAllBe(r => r.ConversationId == destination);
        await using var verify = h.CreateObserverContext();
        (await verify.SessionRuntimeActions.CountAsync(a => a.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    // CARD-1434: channel debt is ordered per destination. A reply that keeps failing to one chat
    // holds only later replies to that chat; another chat's reply publishes, and the failing
    // reply still publishes first once its destination recovers.
    [Test]
    public async Task C1434_a_failing_reply_to_one_destination_does_not_hold_another_destination()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        const string dead = "dead-recipient";
        const string live = "live-recipient";
        var producer = new RecordingProducer { FailingConversations = { dead } };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string first = "[Telegram Family] First question, to the dead chat";
        const string second = "[Telegram Work] Second question, to the live chat";
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await h.SeedChannelCorrelationAsync(first, "telegram:" + dead);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", first), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Failures.ShouldBeGreaterThan(0, "the real producer must be reached");
        producer.Sent.ShouldBeEmpty();
        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.Where(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddHours(1)));

        await h.SeedChannelCorrelationAsync(second, "telegram:" + live);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "UserPrompt", second), Entry(h, 5, "AssistantText", "second answer"), Entry(h, 6, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["second answer"],
            "a reply that keeps failing to one chat must not hold another chat's reply");
        producer.Sent.ShouldAllBe(r => r.ConversationId == live);
        await using (var db = h.CreateObserverContext())
        {
            var pending = await db.SessionRuntimeActions.AsNoTracking().Where(a => a.AgentSessionId == h.SessionId).ToListAsync();
            var debt = pending.ShouldHaveSingleItem("only the dead chat's reply is owed");
            debt.TriggerKind.ShouldBe(SessionRuntimeActionCoordinator.ChannelReplyDebt);
            debt.Sequence.ShouldBe(3);
            debt.Attempts.ShouldBeGreaterThan(0);
            debt.ConversationKey.ShouldBe("telegram:" + dead, "debt is keyed by the destination it owes");
        }

        producer.FailingConversations.Clear();
        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddSeconds(-1)));
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["second answer", "first answer"]);
        producer.Sent.Select(r => r.ConversationId).ShouldBe([live, dead]);
        await using var verify = h.CreateObserverContext();
        (await verify.SessionRuntimeActions.CountAsync(a => a.AgentSessionId == h.SessionId)).ShouldBe(0, "delivered replies leave no debt");
    }

    // CARD-1433: a late-confirm publication that waits for an earlier owed reply is channel debt
    // at its own sequence, not a retained queue-phase action, so a later turn's queue phase
    // still types the next queued message while the replies wait.
    [Test]
    public async Task C1433_a_waiting_late_confirm_does_not_hold_later_queue_actions()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer { Failing = true };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string destination = "late-confirm-hold-recipient";
        const string first = "[Telegram Family] First question";
        const string second = "[Telegram Family] Second question, typed once";
        const string next = "C1433 next queued message";
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        var owed = await h.SeedChannelCorrelationAsync(first, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", first), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Failures.ShouldBeGreaterThan(0, "the real producer must be reached");
        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.Where(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddHours(1)));

        // The second channel message was typed once and is confirmed late by the turn end at 6.
        producer.Failing = false;
        var typed = await h.SeedPendingMessageAsync(second, deliveryAttempts: 1, baselineSequence: 3,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "UserPrompt", second), Entry(h, 5, "AssistantText", "second answer"), Entry(h, 6, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.ShouldBeEmpty("a late-confirmed reply cannot publish past an earlier owed reply");
        await using (var db = h.CreateObserverContext())
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == typed)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);

        // A plain queued message and a terminal-typed third turn while both replies wait.
        var queued = await h.SeedPendingMessageAsync(next);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 7, "UserPrompt", "typed in the terminal"), Entry(h, 8, "AssistantText", "terminal answer"), Entry(h, 9, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        h.Adapter.SubmittedBodies.ShouldBe([next], "a waiting late-confirm publication must not hold the next queued message");
        producer.Sent.ShouldBeEmpty("the owed replies still wait");
        await using (var db = h.CreateObserverContext())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued)).Status.ShouldBe(QueuedMessageStatus.Sent);
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == owed)).ChannelReplySettledAt.ShouldBeNull("the first reply is still owed");
            var pending = await db.SessionRuntimeActions.AsNoTracking().Where(a => a.AgentSessionId == h.SessionId)
                .OrderBy(a => a.Sequence).ToListAsync();
            pending.ShouldAllBe(a => a.TriggerKind == SessionRuntimeActionCoordinator.ChannelReplyDebt,
                "only channel debt remains; no queue-phase action is retained");
            pending.Select(a => a.Sequence).ShouldBe([3L, 6L, 9L]);
            pending[0].Attempts.ShouldBeGreaterThan(0);
            pending[1].LateConfirmedChannelMessageIds.ShouldContain(typed, "the late-confirmed publication is debt at its own sequence");
            pending[2].Attempts.ShouldBe(0);
            await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow.AddSeconds(-1)));
        }

        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first answer", "second answer"]);
        producer.Sent.ShouldAllBe(r => r.ConversationId == destination);
        await using var verify = h.CreateObserverContext();
        (await verify.SessionRuntimeActions.CountAsync(a => a.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    // CARD-1434: a reply whose publication keeps failing is not retried until the TTL misreports
    // it. At the give-up bound the debt is abandoned: its correlation is settled with a Critical
    // ChannelReplyLost (PublishFailed) incident, the row is removed and later replies to that
    // chat flow.
    [Test]
    public async Task C1434_exhausted_channel_debt_is_reported_lost_and_later_replies_flow()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer { Failing = true };
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string destination = "exhausted-recipient";
        const string first = "[Telegram Family] First question";
        const string second = "[Telegram Family] Second question";
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        var settings = h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>().Value;
        var owed = await h.SeedChannelCorrelationAsync(first, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", first), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Failures.ShouldBeGreaterThan(0, "the real producer must be reached");
        producer.Sent.ShouldBeEmpty();
        await using (var db = h.CreateObserverContext())
        {
            var debt = await db.SessionRuntimeActions.AsNoTracking().Where(a => a.AgentSessionId == h.SessionId).ToListAsync();
            debt.ShouldHaveSingleItem().TriggerKind.ShouldBe(SessionRuntimeActionCoordinator.ChannelReplyDebt);
            // The row has failed as often as the policy allows and its retry is due.
            await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Attempts, settings.ReplyDebtGiveUpAttempts)
                .SetProperty(a => a.DueAt, DateTime.UtcNow.AddSeconds(-1)));
        }

        await actions.DrainPageAsync(h.SessionId, default);
        await using (var db = h.CreateObserverContext())
        {
            var incident = (await db.AgentIncidents
                    .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost).ToListAsync())
                .ShouldHaveSingleItem("an abandoned reply is reported once, never dropped silently");
            incident.FailureReason.ShouldBe("PublishFailed");
            incident.Severity.ShouldBe(AlertSeverity.Critical);
            incident.SessionId.ShouldBe(h.SessionId);
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == owed)).ChannelReplySettledAt
                .ShouldNotBeNull("abandoning settles the correlation so the TTL sweep cannot report it again");
            (await db.SessionRuntimeActions.CountAsync(a => a.AgentSessionId == h.SessionId)).ShouldBe(0, "the debt row is removed");
        }

        producer.Failing = false;
        await h.SeedChannelCorrelationAsync(second, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "UserPrompt", second), Entry(h, 5, "AssistantText", "second answer"), Entry(h, 6, "TurnEnd", null)]);
        await actions.DrainPageAsync(h.SessionId, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["second answer"], "later replies to the chat flow once the dead debt is abandoned");
        producer.Sent.ShouldAllBe(r => r.ConversationId == destination);
        await using var verify = h.CreateObserverContext();
        (await verify.SessionRuntimeActions.CountAsync(a => a.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task C1259_queue_prune_holds_the_transcript_mutation_lease()
    {
        var gate = new QueueDeleteGate();
        await using var f = await SessionStateTestFixture.CreateAsync(gate);
        await using var db = f.Db();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
            Body = "old settled source", Sequence = 1, Status = QueuedMessageStatus.Sent,
            CreatedAt = DateTime.UtcNow.AddDays(-100) });
        await db.SaveChangesAsync();
        await f.Store.ReadAsync(f.SessionId, default);
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db, Options.Create(new RetentionSettings()), audit,
            TimeProvider.System, NullLogger<DataRetentionService>.Instance, new AuditService(db, audit), f.Store);
        var prune = retention.PruneQueuedMessagesAsync(default);
        Task<SessionStateStore.Lease>? writer = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The cached store makes an uncontended lease acquisition synchronous. This
            // observes the actual gate, without inferring ownership from database latency.
            writer = f.Store.BeginWriteAsync(f.SessionId, default);
            writer.IsCompleted.ShouldBeFalse("queue deletion must hold the same lease as action admission");
        }
        finally
        {
            gate.Release.TrySetResult(); await prune;
            if (writer is not null) (await writer).Dispose();
        }
        await f.Runtime.ObserveTranscriptAsync(f.Event(1, "ToolResult", text: "new durable debt"), default);
        await using var verify = f.Db();
        (await verify.TranscriptEntries.SingleAsync()).Text.ShouldBe("new durable debt");
        (await verify.SessionRuntimeActions.SingleAsync()).Sequence.ShouldBe(1);
    }

    [Test]
    public async Task C1259_worker_diagnostics_identify_debt_without_transcript_or_exception_text()
    {
        var log = new RecordingLogger();
        await using var f = await SessionStateTestFixture.CreateAsync(configure: s =>
            s.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log));
        await f.Runtime.ObserveTranscriptAsync(f.Event(1, "ToolResult", text: "private-transcript-body"), default);
        await f.Services.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(f.SessionId, default);
        log.Values.ShouldContain(v => v.ContainsKey("PendingCount") && Equals(v["PendingCount"], 1));
        log.Values.ShouldContain(v => v.ContainsKey("Frontier") && Equals(v["Frontier"], 1L));
        log.Values.ShouldContain(v => v.ContainsKey("AgeSeconds") && v.ContainsKey("Phase") && v.ContainsKey("ActionId"));
        log.Messages.ShouldContain(m => m.Contains("start"));
        log.Messages.ShouldContain(m => m.Contains("end"));
        log.Messages.ShouldAllBe(m => !m.Contains("private-transcript-body"));
        var failure = new PrivateFailureBus();
        await using var failing = await SessionStateTestFixture.CreateAsync(configure: s =>
        {
            s.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log);
            s.AddSingleton<IEventBus>(failure);
        });
        await failing.Runtime.ObserveTranscriptAsync(failing.Event(1, "ToolResult", text: "second-private-body"), default);
        await failing.Services.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(failing.SessionId, default);
        failure.Hits.ShouldBe(1);
        await using var verify = failing.Db();
        var retained = await verify.SessionRuntimeActions.SingleAsync();
        retained.Phase.ShouldBe(0);
        retained.Attempts.ShouldBe(1);
        retained.FailureReason.ShouldBe(nameof(IOException));
        log.Values.ShouldContain(v => v.ContainsKey("FailureType") && Equals(v["FailureType"], nameof(IOException)));
        log.Messages.ShouldAllBe(m => !m.Contains("second-private-body") && !m.Contains("private-exception-detail"));
    }

    private static SessionRunnerTranscriptEvent Entry(BridgeQueueHarness h, long seq, string kind, string? text) =>
        new(h.SessionId, seq, kind, Guid.NewGuid().ToString(), null, h.Clock.GetUtcNow(),
            kind == "UserPrompt" ? "user" : "assistant", text, null, null, null, null,
            kind == "TurnEnd" ? "end_turn" : null);

    private sealed class QueueDeleteGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"SessionQueuedMessages\"", StringComparison.Ordinal))
            { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
    }

    private sealed class RecordingProducer : IAntiphonMessagingProducer
    {
        public List<ChannelReply> Sent { get; } = [];
        public bool FailNext;
        public bool Failing;
        // CARD-1434: fail only the replies addressed to these conversation ids.
        public HashSet<string> FailingConversations { get; } = new(StringComparer.Ordinal);
        public int Failures;
        public Func<Task>? OnSend;
        public async Task SendAsync(ChannelReply reply, CancellationToken ct = default)
        {
            var dead = reply.ConversationId is { } conversation && FailingConversations.Contains(conversation);
            if (FailNext || Failing || dead) { FailNext = false; Failures++; throw new IOException("pre-accept failure"); }
            Sent.Add(reply);
            if (OnSend is not null) await OnSend();
        }
    }

    private sealed class RecordingLogger : ILogger<SessionRuntimeActionCoordinator>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ConcurrentQueue<Dictionary<string, object?>> Values { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> format)
        {
            Messages.Enqueue(format(state, ex));
            if (state is IEnumerable<KeyValuePair<string, object?>> values) Values.Enqueue(values.ToDictionary());
        }
    }

    private sealed class PrivateFailureBus : IEventBus
    {
        public int Hits;
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        { Hits++; throw new IOException("private-exception-detail"); }
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }
}
