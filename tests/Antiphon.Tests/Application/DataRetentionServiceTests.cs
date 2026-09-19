using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0044 slices 1-4: transcript deletion is per-session all-or-nothing, settled queue rows
/// are pruned independently of session liveness, terminal AgentSession rows past 90d are
/// deleted when nothing still names them, AgentTask trees past 180d are deleted whole or
/// not at all, and audit FullContent is archived past <c>AuditSettings.RetentionDays</c>. The
/// sweep is global, so this class is
/// <see cref="NotInParallelAttribute"/> with no group key (serialise against everything, not just
/// itself) and every assertion is scoped to ids this test created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class DataRetentionServiceTests
{
    [Test]
    public async Task A_running_sessions_rows_survive_at_any_age()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, stale);
            var oldId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var olderId = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.TurnEnd, stale.AddDays(-5));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(oldId)).ShouldBeTrue();
            (await ExistsAsync(olderId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_terminal_session_whose_newest_row_is_recent_keeps_every_row()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(1));
            var ancientId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, DaysAgo(90));
            var oldId = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.AssistantText, DaysAgo(40));
            var recentId = await SeedTranscriptAsync(sessionId, 3, TranscriptKinds.TurnEnd, DaysAgo(1));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(ancientId)).ShouldBeTrue("a partial trim of old rows is forbidden");
            (await ExistsAsync(oldId)).ShouldBeTrue("a partial trim of old rows is forbidden");
            (await ExistsAsync(recentId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_terminal_session_with_recent_LastSeenAt_keeps_every_row_even_when_all_transcripts_are_stale()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, lastSeenAt: DaysAgo(1));
            var a = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, DaysAgo(40));
            var b = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.TurnEnd, DaysAgo(40));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(a)).ShouldBeTrue();
            (await ExistsAsync(b)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_fully_stale_terminal_session_loses_all_transcript_rows_and_reads_idle()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, stale);
            // Activity without an end marker: before the prune this session reads working.
            var activityId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var moreId = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.AssistantText, stale.AddHours(1));

            await using (var before = CreateContext())
            {
                (await SessionMessageQueueService.IsWorkingAsync(before, sessionId, CancellationToken.None))
                    .ShouldBeTrue("fixture: leftover activity with no end marker is working");
            }

            await using var db = CreateContext();
            var removed = await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);
            removed.ShouldBeGreaterThanOrEqualTo(2);

            (await ExistsAsync(activityId)).ShouldBeFalse();
            (await ExistsAsync(moreId)).ShouldBeFalse();

            await using var after = CreateContext();
            (await after.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId)).ShouldBe(0);
            (await SessionMessageQueueService.IsWorkingAsync(after, sessionId, CancellationToken.None))
                .ShouldBeFalse("zero rows is idle — the launch invariant");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_stale_stopped_session_that_is_an_agents_PersistentSessionId_is_excluded()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var rowId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            await SeedAgentPointingAtAsync(marker, sessionId);

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(rowId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Queue_keeps_Pending_and_unsettled_channel_rows_and_deletes_settled_old_ones()
    {
        var marker = NewMarker();
        try
        {
            // Queue prune is independent of session liveness — a live always-on session is the
            // interesting case (this is what keeps its queue bounded).
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DateTime.UtcNow);
            var pendingOld = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Pending, QueuedMessageOrigin.Ui, DaysAgo(40));
            var sentOld = await SeedQueuedAsync(
                sessionId, 2, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, DaysAgo(40));
            var sentRecent = await SeedQueuedAsync(
                sessionId, 3, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, DaysAgo(1));
            var canceledOld = await SeedQueuedAsync(
                sessionId, 4, QueuedMessageStatus.Canceled, QueuedMessageOrigin.System, DaysAgo(40));
            var channelUnsettled = await SeedQueuedAsync(
                sessionId, 5, QueuedMessageStatus.Sent, QueuedMessageOrigin.Channel, DaysAgo(40),
                settledAt: null);
            var channelSettled = await SeedQueuedAsync(
                sessionId, 6, QueuedMessageStatus.Sent, QueuedMessageOrigin.Channel, DaysAgo(40),
                settledAt: DaysAgo(39));

            await using var db = CreateContext();
            await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None);

            (await QueueExistsAsync(pendingOld)).ShouldBeTrue("Pending survives at any age");
            (await QueueExistsAsync(sentOld)).ShouldBeFalse();
            (await QueueExistsAsync(sentRecent)).ShouldBeTrue();
            (await QueueExistsAsync(canceledOld)).ShouldBeFalse();
            (await QueueExistsAsync(channelUnsettled)).ShouldBeTrue("owed channel reply is never deleted");
            (await QueueExistsAsync(channelSettled)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_swept_parked_row_is_pruned_after_the_queued_message_retention_window()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, DaysAgo(40));
            var swept = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Canceled, QueuedMessageOrigin.Delegation, DaysAgo(40),
                deliveryAttempts: 3);

            await using var db = CreateContext();
            (await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);

            (await QueueExistsAsync(swept)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_transcript_window_skips_transcripts_and_still_prunes_queued_messages()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var transcriptId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var sentOld = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, stale);

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                TranscriptRetentionDays = 0,
                QueuedMessageRetentionDays = 30,
            }).RunOnceAsync(CancellationToken.None);

            result.Transcripts.ShouldBe(0);
            (await ExistsAsync(transcriptId)).ShouldBeTrue();
            (await QueueExistsAsync(sentOld)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_queued_window_skips_queued_messages_and_still_prunes_transcripts()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var transcriptId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var sentOld = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, stale);

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                TranscriptRetentionDays = 30,
                QueuedMessageRetentionDays = 0,
            }).RunOnceAsync(CancellationToken.None);

            result.QueuedMessages.ShouldBe(0);
            (await ExistsAsync(transcriptId)).ShouldBeFalse();
            (await QueueExistsAsync(sentOld)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_terminal_session_past_the_window_with_no_referencers_is_deleted()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));

            await using var db = CreateContext();
            var removed = await CreateService(db).PruneSessionsAsync(CancellationToken.None);
            removed.ShouldBeGreaterThanOrEqualTo(1);

            (await SessionExistsAsync(sessionId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_session_referenced_via_AgentTask_AgentSessionId_survives_past_the_window()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, DaysAgo(100));
            await SeedTaskAsync(marker, agentSessionId: sessionId, parentSessionId: null);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_session_referenced_via_AgentTask_ParentSessionId_survives_past_the_window()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            await SeedTaskAsync(marker, agentSessionId: null, parentSessionId: sessionId);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_session_that_is_an_agents_PersistentSessionId_survives_session_prune()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            await SeedAgentPointingAtAsync(marker, sessionId);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_non_terminal_session_survives_session_prune_regardless_of_age()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DaysAgo(100));

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Deleting_a_session_cascades_its_transcripts_and_queued_messages()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(100);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, stale);
            var transcriptId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var queuedId = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, stale);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeFalse();
            (await ExistsAsync(transcriptId)).ShouldBeFalse();
            (await QueueExistsAsync(queuedId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_session_window_skips_sessions()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                SessionRetentionDays = 0,
            }).RunOnceAsync(CancellationToken.None);

            result.Sessions.ShouldBe(0);
            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_fully_terminal_stale_tree_loses_every_row_and_its_events()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var parentId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, parentId, rootId, rootId, depth: 1,
                AgentTaskStatus.Failed, stale, stale.AddHours(2));
            await SeedTaskRowAsync(marker, childId, rootId, parentId, depth: 2,
                AgentTaskStatus.Canceled, stale, stale.AddHours(3));
            var rootEvent = await SeedTaskEventAsync(rootId, stale.AddHours(1));
            var parentEvent = await SeedTaskEventAsync(parentId, stale.AddHours(2));
            var childEvent = await SeedTaskEventAsync(childId, stale.AddHours(3));

            await using var db = CreateContext();
            var removed = await CreateService(db).PruneTasksAsync(CancellationToken.None);
            removed.ShouldBeGreaterThanOrEqualTo(3);

            (await TaskExistsAsync(rootId)).ShouldBeFalse();
            (await TaskExistsAsync(parentId)).ShouldBeFalse();
            (await TaskExistsAsync(childId)).ShouldBeFalse();
            (await TaskEventExistsAsync(rootEvent)).ShouldBeFalse();
            (await TaskEventExistsAsync(parentEvent)).ShouldBeFalse();
            (await TaskEventExistsAsync(childEvent)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_tree_with_one_live_member_survives_entirely()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Working, stale, completedAt: null);

            await using var db = CreateContext();
            await CreateService(db).PruneTasksAsync(CancellationToken.None);

            (await TaskExistsAsync(rootId)).ShouldBeTrue("a partial tree delete is forbidden");
            (await TaskExistsAsync(childId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_tree_whose_newest_row_is_within_the_window_survives_entirely()
    {
        var marker = NewMarker();
        try
        {
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, DaysAgo(200), DaysAgo(199));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Succeeded, DaysAgo(2), DaysAgo(1));

            await using var db = CreateContext();
            await CreateService(db).PruneTasksAsync(CancellationToken.None);

            (await TaskExistsAsync(rootId)).ShouldBeTrue("a stale leaf of a fresh tree survives");
            (await TaskExistsAsync(childId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_task_window_skips_tasks()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale);

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                TaskRetentionDays = 0,
            }).RunOnceAsync(CancellationToken.None);

            result.Tasks.ShouldBe(0);
            (await TaskExistsAsync(rootId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task RunOnce_archives_old_audit_FullContent_using_the_configured_window()
    {
        var marker = NewMarker();
        try
        {
            // 20d is younger than the hardcoded 90 the endpoint used to default to, so this
            // only goes if RunOnceAsync actually reads AuditSettings.RetentionDays = 14.
            var oldId = await SeedAuditAsync(marker, DaysAgo(20), """{"prompt":"old"}""", " old");
            var youngId = await SeedAuditAsync(marker, DaysAgo(7), """{"prompt":"young"}""", " young");

            await using var db = CreateContext();
            var result = await CreateService(db, auditSettings: new AuditSettings { RetentionDays = 14 })
                .RunOnceAsync(CancellationToken.None);

            result.AuditRecords.ShouldBeGreaterThanOrEqualTo(1);

            var old = await GetAuditAsync(oldId);
            old.ShouldNotBeNull();
            old.FullContent.ShouldBeNull();
            old.Summary.ShouldBe($"{marker} old");
            old.ModelName.ShouldBe("test-model");
            old.TokensIn.ShouldBe(10);
            old.TokensOut.ShouldBe(20);
            old.CostUsd.ShouldBe(0.001m);

            var young = await GetAuditAsync(youngId);
            young.ShouldNotBeNull();
            young.FullContent.ShouldNotBeNull("a 7-day-old record is inside the 14-day window");
            young.FullContent.ShouldContain("young");
            young.Summary.ShouldBe($"{marker} young");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_audit_window_skips_the_archive_pass()
    {
        var marker = NewMarker();
        try
        {
            var oldId = await SeedAuditAsync(marker, DaysAgo(200), """{"prompt":"keep"}""");

            await using var db = CreateContext();
            var result = await CreateService(db, auditSettings: new AuditSettings { RetentionDays = 0 })
                .RunOnceAsync(CancellationToken.None);

            result.AuditRecords.ShouldBe(0);
            var kept = await GetAuditAsync(oldId);
            kept.ShouldNotBeNull();
            kept.FullContent.ShouldNotBeNull("RetentionDays <= 0 must skip the archive pass");
            kept.FullContent.ShouldContain("keep");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    // ---------- helpers ----------

    [Test]
    public async Task C514_Retention_keeps_ambiguous_arm_veto()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DateTime.UtcNow);
            var id = Guid.NewGuid();
            await using (var db = CreateContext())
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = id,
                    AgentSessionId = sessionId,
                    Body = "/remote-control",
                    Status = QueuedMessageStatus.Sent,
                    Sequence = 1,
                    Origin = QueuedMessageOrigin.Supervision,
                    CreatedAt = DaysAgo(40),
                    SentAt = DaysAgo(40),
                    MaintenanceKind = RemoteControlMaintenanceKind.AutomaticArm,
                    MaintenanceResult = RemoteControlArmResult.ArmUnconfirmed,
                    MaintenanceAcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneQueuedMessagesAsync(CancellationToken.None);
            (await QueueExistsAsync(id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task C514_Open_modal_survives_episode_retention()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DateTime.UtcNow);
            var openId = Guid.NewGuid();
            var resolvedId = Guid.NewGuid();
            var generation = SessionGeneration.Normalize(DateTime.UtcNow);
            await using (var db = CreateContext())
            {
                db.RemoteControlModalEpisodes.Add(new RemoteControlModalEpisode
                {
                    Id = openId,
                    SessionId = sessionId,
                    AcceptedStartedAt = generation,
                    FirstObservedAt = DaysAgo(40),
                    LastObservedAt = DaysAgo(40),
                });
                db.RemoteControlModalEpisodes.Add(new RemoteControlModalEpisode
                {
                    Id = resolvedId,
                    SessionId = sessionId,
                    AcceptedStartedAt = SessionGeneration.Next(generation, DateTime.UtcNow.AddMinutes(-1)),
                    FirstObservedAt = DaysAgo(40),
                    LastObservedAt = DaysAgo(40),
                    ResolvedAt = DaysAgo(40),
                    Resolution = RemoteControlEpisodeResolution.ObservedClear,
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneResolvedModalEpisodesAsync(CancellationToken.None);
            await using var verify = CreateContext();
            (await verify.RemoteControlModalEpisodes.AnyAsync(e => e.Id == openId)).ShouldBeTrue();
            (await verify.RemoteControlModalEpisodes.AnyAsync(e => e.Id == resolvedId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task C514_Deferred_work_retains_its_attempt_and_session()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            await using (var db = CreateContext())
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Body = "deferred original work body c514",
                    Status = QueuedMessageStatus.Pending,
                    Sequence = 1,
                    Origin = QueuedMessageOrigin.System,
                    CreatedAt = DaysAgo(100),
                    DeferredFromRunAttemptId = Guid.NewGuid(),
                    MaintenanceAcceptedStartedAt = SessionGeneration.Normalize(DaysAgo(100)),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneSessionsAsync(CancellationToken.None);
            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-91: a pending dispatch-warning intent is the ONLY thing naming its original
    /// destination — no agent PersistentSessionId, no task AgentSessionId/ParentSessionId, no
    /// queue row and no note — and the session survives the sweep anyway. Materializing it, or
    /// capturing it with ReplyTo None, releases the session.
    /// </summary>
    [Test]
    public async Task C508_IntentSessionRetention()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var loose = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var notRequired = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var taskId = await SeedDetachedTaskAsync(marker, DaysAgo(200));
            await SeedIntentAsync(taskId, sessionId, AgentTaskReplyTo.Session, DaysAgo(100), materialized: false);
            await SeedIntentAsync(taskId, loose, AgentTaskReplyTo.Session, DaysAgo(100), materialized: true);
            await SeedIntentAsync(taskId, notRequired, AgentTaskReplyTo.None, DaysAgo(100), materialized: false);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue(
                "a pending dispatch-warning intent still owes this destination a prompt");
            (await SessionExistsAsync(loose)).ShouldBeFalse("a materialized intent owes the session nothing");
            (await SessionExistsAsync(notRequired)).ShouldBeFalse("ReplyTo None never owed a prompt");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-92: transcript pruning is per-session all-or-nothing, and the protected
    /// destination keeps EVERY row — including rows far older than the window — while an
    /// otherwise identical unprotected session in the same sweep loses all of its.
    /// </summary>
    [Test]
    public async Task C508_IntentTranscriptRetention()
    {
        var marker = NewMarker();
        try
        {
            var protectedSession = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var control = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var taskId = await SeedDetachedTaskAsync(marker, DaysAgo(200));
            await SeedIntentAsync(taskId, protectedSession, AgentTaskReplyTo.Session, DaysAgo(100), materialized: false);

            var oldest = await SeedTranscriptAsync(protectedSession, 1, TranscriptKinds.UserPrompt, DaysAgo(120));
            var middle = await SeedTranscriptAsync(protectedSession, 2, TranscriptKinds.AssistantText, DaysAgo(110));
            var newest = await SeedTranscriptAsync(protectedSession, 3, TranscriptKinds.TurnEnd, DaysAgo(100));
            var controlRow = await SeedTranscriptAsync(control, 1, TranscriptKinds.UserPrompt, DaysAgo(120));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(oldest)).ShouldBeTrue();
            (await ExistsAsync(middle)).ShouldBeTrue();
            (await ExistsAsync(newest)).ShouldBeTrue();
            (await ExistsAsync(controlRow)).ShouldBeFalse(
                "the unprotected session in the same sweep must still make progress");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-93: any intent on ANY member of a stale terminal tree — pending or materialized —
    /// protects the whole tree, and an unrelated eligible tree still prunes in the same sweep.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C508_IntentTaskTreeRetention(bool materialized)
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(2));
            // The intent hangs off the CHILD; the whole tree must be retained.
            await SeedIntentAsync(childId, parentSessionId: null, AgentTaskReplyTo.Session,
                stale.AddHours(2), materialized);

            var otherRoot = Guid.NewGuid();
            await SeedTaskRowAsync(marker, otherRoot, otherRoot, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            var otherEvent = await SeedTaskEventAsync(otherRoot, stale.AddHours(1));

            await using var db = CreateContext();
            Exception? caught = null;
            try { await CreateService(db).PruneTasksAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }

            caught.ShouldBeNull();
            (await TaskExistsAsync(rootId)).ShouldBeTrue("a partial tree delete is forbidden");
            (await TaskExistsAsync(childId)).ShouldBeTrue();
            (await TaskExistsAsync(otherRoot)).ShouldBeFalse(
                "an unrelated eligible tree still prunes in the same sweep");
            (await TaskEventExistsAsync(otherEvent)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-94: the same protection for a notification on any member, including a CONFIRMED
    /// note and a NotRequired one — the row is history the tree delete would silently destroy.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C508_NotificationTaskTreeRetention(bool confirmed)
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(2));
            await SeedDispatchNoteAsync(childId, stale.AddHours(2), confirmed);

            var otherRoot = Guid.NewGuid();
            await SeedTaskRowAsync(marker, otherRoot, otherRoot, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));

            await using var db = CreateContext();
            Exception? caught = null;
            try { await CreateService(db).PruneTasksAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }

            caught.ShouldBeNull();
            (await TaskExistsAsync(rootId)).ShouldBeTrue();
            (await TaskExistsAsync(childId)).ShouldBeTrue();
            (await TaskExistsAsync(otherRoot)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// A task that names no session at all, so only the intent under test can protect anything.
    /// </summary>
    private static async Task<Guid> SeedDetachedTaskAsync(string marker, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = marker,
            Goal = "retention intent custody",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            AgentSessionId = null,
            ParentSessionId = null,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = createdAt,
            CompletedAt = createdAt.AddHours(1),
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// CARD-0544 PC-118 / R-11: a stale keyed row whose Completion obligation is unconfirmed is receipt
    /// evidence still owed and survives the queue window; a confirmed obligation's row prunes while the
    /// notification keeps ConfirmedAt/ConfirmingPromptSequence, and the next reconcile never re-enqueues.
    /// </summary>
    [Test]
    public async Task C544_CompletionObligationRetention()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var caller = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var old = DaysAgo(40);
        (Guid NoteId, Guid RowId) confirmed, owed;
        await using (var db = new AppDbContext(options))
        {
            confirmed = SeedCompletionObligation(db, caller.SessionId, old, confirmed: true, sequence: 1);
            owed = SeedCompletionObligation(db, caller.SessionId, old, confirmed: false, sequence: 2);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None);

        await using (var db = new AppDbContext(options))
        {
            (await db.SessionQueuedMessages.AnyAsync(m => m.Id == owed.RowId)).ShouldBeTrue("unconfirmed Completion evidence survives");
            (await db.SessionQueuedMessages.AnyAsync(m => m.Id == confirmed.RowId)).ShouldBeFalse("confirmed obligation row prunes");
            var kept = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == confirmed.NoteId);
            kept.State.ShouldBe(LandNotificationState.Confirmed);
            kept.ConfirmedAt.ShouldNotBeNull("receipt identity retained");
            kept.ConfirmingPromptSequence.ShouldBe(41);

            await new AgentTaskLandNotificationService(db, caller.Queue, new CompletionNoteFlushQueue(), caller.Runtime, TimeProvider.System)
                .ReconcileAsync(confirmed.NoteId, CancellationToken.None);
        }

        await using (var verify = new AppDbContext(options))
        {
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == confirmed.NoteId))
                .ShouldBe(0, "a confirmed obligation is never re-enqueued");
            var after = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == confirmed.NoteId);
            after.State.ShouldBe(LandNotificationState.Confirmed);
            after.ConfirmingPromptSequence.ShouldBe(41);
            caller.Adapter.SubmittedBodies.ShouldBeEmpty();
        }
    }

    private static (Guid NoteId, Guid RowId) SeedCompletionObligation(AppDbContext db, Guid session, DateTime at, bool confirmed, long sequence)
    {
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        const string body = "[task retention] verification=Final; scope=Full; final-review=none\nreport";
        var digest = DelegationNoteDigest.Compute(body);
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C544 retention", Goal = "retention", Role = AgentTaskRole.Review,
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = session, VerificationProfileVersion = 1, VerificationRound = VerificationRound.Final,
            CreatedAt = at, CompletedAt = at,
        });
        db.AgentTaskEvents.Add(new AgentTaskEvent { Id = eventId, AgentTaskId = taskId, Type = AgentTaskEventType.Completed, Detail = "settled", At = at });
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId, TaskId = taskId, SourceEventId = eventId, Kind = LandNotificationKind.TaskCompletion,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = session, Body = body, ContentDigest = digest,
            CreatedAt = at, NextAttemptAt = at, EnqueuedAt = at, QueueMessageId = rowId,
            State = confirmed ? LandNotificationState.Confirmed : LandNotificationState.AwaitingReceipt,
            ConfirmedAt = confirmed ? at : null, ConfirmingPromptSequence = confirmed ? 41 : null,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = rowId, AgentSessionId = session, Sequence = sequence, Body = body,
            Status = QueuedMessageStatus.Sent, Origin = QueuedMessageOrigin.Delegation, SourceTaskId = taskId,
            ContentDigest = digest, SourceLandNotificationId = noteId, ConversationKey = $"task:{taskId:N}",
            DeliveryAttempts = 1, DeliveryVerdict = confirmed ? DeliveryVerdict.Delivered : null,
            CreatedAt = at, SentAt = at,
        });
        return (noteId, rowId);
    }

    private static async Task<Guid> SeedIntentAsync(
        Guid taskId, Guid? parentSessionId, AgentTaskReplyTo replyTo, DateTime createdAt, bool materialized)
    {
        var intentId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await using var db = CreateContext();
        var dispatchEventId = Guid.NewGuid();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = dispatchEventId,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Dispatched,
            Detail = "retention dispatch",
            At = createdAt,
        });
        var payload = DispatchBaseNotificationPayload.Capture(
            intentId, notificationId, taskId, dispatchEventId, 0,
            DispatchBaseNotificationPayload.MismatchKey, replyTo, parentSessionId,
            "retention dispatch-base warning", createdAt);
        db.AgentTaskDispatchWarningIntents.Add(new AgentTaskDispatchWarningIntent
        {
            Id = payload.WarningEventId,
            DispatchEventId = payload.DispatchEventId,
            TaskId = payload.TaskId,
            Attempt = payload.Attempt,
            WarningKey = payload.WarningKey,
            NotificationId = payload.NotificationId,
            ReplyTo = payload.ReplyTo,
            ParentSessionId = payload.ParentSessionId,
            Detail = payload.Detail,
            Body = payload.Body,
            ContentDigest = payload.ContentDigest,
            CreatedAt = payload.CreatedAt,
            InitialState = payload.InitialState,
            NextAttemptAt = payload.CreatedAt,
            MaterializedAt = materialized ? createdAt : null,
        });
        await db.SaveChangesAsync();
        return intentId;
    }

    private static async Task<Guid> SeedDispatchNoteAsync(Guid taskId, DateTime createdAt, bool confirmed)
    {
        var noteId = Guid.NewGuid();
        var warningId = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = warningId,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Warning,
            Detail = "retention dispatch-base warning",
            At = createdAt,
        });
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId,
            RequestId = null,
            TaskId = taskId,
            SourceEventId = warningId,
            Kind = LandNotificationKind.DispatchBase,
            ReplyTo = confirmed ? AgentTaskReplyTo.Session : AgentTaskReplyTo.None,
            ParentSessionId = null,
            Body = "retention dispatch-base note",
            ContentDigest = "digest",
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
            State = confirmed ? LandNotificationState.Confirmed : LandNotificationState.NotRequired,
            ConfirmedAt = confirmed ? createdAt : null,
        });
        await db.SaveChangesAsync();
        return noteId;
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static DataRetentionService CreateService(
        AppDbContext db,
        RetentionSettings? settings = null,
        AuditSettings? auditSettings = null)
    {
        var audit = auditSettings ?? new AuditSettings();
        return new DataRetentionService(
            db,
            Options.Create(settings ?? new RetentionSettings()),
            Options.Create(audit),
            TimeProvider.System,
            NullLogger<DataRetentionService>.Instance,
            new AuditService(db, Options.Create(audit)));
    }

    private static string NewMarker() => $"ret-{Guid.NewGuid():N}";

    private static DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days);

    private static async Task<Guid> SeedSessionAsync(string marker, SessionStatus status, DateTime lastSeenAt)
    {
        var sessionId = Guid.NewGuid();
        var startedAt = lastSeenAt.AddDays(-1);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = Path.Combine(Path.GetTempPath(), marker),
            Cols = 120,
            Rows = 30,
            CreatedAt = startedAt,
            StartedAt = startedAt,
            LastSeenAt = lastSeenAt,
            EndedAt = status is SessionStatus.Stopped or SessionStatus.Failed ? lastSeenAt : null,
            ExitCode = status is SessionStatus.Failed ? 1 : status is SessionStatus.Stopped ? 0 : null,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static async Task SeedAgentPointingAtAsync(string marker, Guid sessionId)
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.Agents.Add(new Agent
        {
            Id = Guid.NewGuid(),
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = AgentStatus.Idle,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddDays(-40),
            UpdatedAt = now.AddDays(-40),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedTaskAsync(string marker, Guid? agentSessionId, Guid? parentSessionId)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = marker,
            Goal = "retention session-ref",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            AgentSessionId = agentSessionId,
            ParentSessionId = parentSessionId,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DaysAgo(100),
            CompletedAt = DaysAgo(99),
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task C459_IncompleteRetirementRetainsEvidence()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var taskId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, taskId, taskId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            var other = Guid.NewGuid();
            await SeedTaskRowAsync(marker, other, other, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await using (var db = CreateContext())
            {
                db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskId,
                    TaskAttempt = 1,
                    TerminalStatus = AgentTaskStatus.Succeeded,
                    TaskCompletedAt = stale.AddHours(1),
                    ReleasedTaskRevision = Guid.NewGuid(),
                    ReleasedAt = stale.AddHours(2),
                    State = WorktreeRetirementState.Claimed,
                    Active = true,
                    UpdatedAt = stale.AddHours(2),
                    SourceSha = new string('a', 40),
                    SourceFullRef = "refs/heads/feat/card-task-aaaaaaaa",
                    WorktreePath = Path.Combine(Path.GetTempPath(), marker, "tree"),
                    RepositoryPath = Path.Combine(Path.GetTempPath(), marker),
                    CommonDirectory = Path.Combine(Path.GetTempPath(), marker),
                    GitDirectory = Path.Combine(Path.GetTempPath(), marker, ".git"),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneTasksAsync(CancellationToken.None);
            var retainedTask = await TaskExistsAsync(taskId);
            retainedTask.ShouldBeTrue();
            (await TaskExistsAsync(other)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task C459_CompletedRetirementExpiresInDependencyOrder()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var taskId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, taskId, taskId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await using (var db = CreateContext())
            {
                db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskId,
                    TaskAttempt = 1,
                    TerminalStatus = AgentTaskStatus.Succeeded,
                    TaskCompletedAt = stale.AddHours(1),
                    ReleasedTaskRevision = Guid.NewGuid(),
                    ReleasedAt = stale.AddHours(2),
                    State = WorktreeRetirementState.Complete,
                    Active = false,
                    UpdatedAt = stale.AddHours(3),
                    SourceSha = new string('a', 40),
                    SourceFullRef = "refs/heads/feat/card-task-aaaaaaaa",
                    WorktreePath = Path.Combine(Path.GetTempPath(), marker, "tree"),
                    RepositoryPath = Path.Combine(Path.GetTempPath(), marker),
                    CommonDirectory = Path.Combine(Path.GetTempPath(), marker),
                    GitDirectory = Path.Combine(Path.GetTempPath(), marker, ".git"),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneTasksAsync(CancellationToken.None);
            (await TaskExistsAsync(taskId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    private static async Task<Guid> SeedTaskRowAsync(
        string marker,
        Guid id,
        Guid rootTaskId,
        Guid? parentTaskId,
        int depth,
        AgentTaskStatus status,
        DateTime createdAt,
        DateTime? completedAt)
    {
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = rootTaskId,
            ParentTaskId = parentTaskId,
            Depth = depth,
            Title = marker,
            Goal = "retention task-tree",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = status,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTaskEventAsync(Guid taskId, DateTime at)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = id,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Completed,
            Detail = "retention event",
            At = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTranscriptAsync(
        Guid sessionId, long sequence, string kind, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = id,
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = id.ToString("N"),
            Text = kind == TranscriptKinds.UserPrompt ? $"prompt {sequence}" : $"body {sequence}",
            CreatedAt = createdAt,
            Timestamp = createdAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedQueuedAsync(
        Guid sessionId,
        long sequence,
        QueuedMessageStatus status,
        QueuedMessageOrigin origin,
        DateTime createdAt,
        DateTime? settledAt = null,
        int deliveryAttempts = 0)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id,
            AgentSessionId = sessionId,
            Body = $"msg {sequence}",
            Status = status,
            Sequence = sequence,
            Origin = origin,
            ConversationKey = origin == QueuedMessageOrigin.Channel ? "telegram:-1001" : null,
            CreatedAt = createdAt,
            SentAt = status == QueuedMessageStatus.Sent ? createdAt : null,
            CanceledAt = status == QueuedMessageStatus.Canceled ? createdAt : null,
            ChannelReplySettledAt = settledAt,
            DeliveryAttempts = deliveryAttempts,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<bool> SessionExistsAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.AgentSessions.AnyAsync(s => s.Id == sessionId);
    }

    private static async Task<bool> ExistsAsync(Guid transcriptId)
    {
        await using var db = CreateContext();
        return await db.TranscriptEntries.AnyAsync(t => t.Id == transcriptId);
    }

    private static async Task<bool> QueueExistsAsync(Guid messageId)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AnyAsync(m => m.Id == messageId);
    }

    private static async Task<bool> TaskExistsAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AnyAsync(t => t.Id == taskId);
    }

    private static async Task<bool> TaskEventExistsAsync(Guid eventId)
    {
        await using var db = CreateContext();
        return await db.AgentTaskEvents.AnyAsync(e => e.Id == eventId);
    }

    private static async Task CleanupAsync(string marker)
    {
        await using var db = CreateContext();
        var sessionIds = await db.AgentSessions
            .Where(s => s.Cwd.EndsWith(marker))
            .Select(s => s.Id)
            .ToListAsync();
        if (sessionIds.Count > 0)
        {
            await db.TranscriptEntries.Where(t => sessionIds.Contains(t.AgentSessionId)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => sessionIds.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.RemoteControlModalEpisodes.Where(e => sessionIds.Contains(e.SessionId)).ExecuteDeleteAsync();
        }

        var taskIds = await db.AgentTasks.Where(t => t.Title == marker).Select(t => t.Id).ToListAsync();
        if (taskIds.Count > 0)
        {
            // CARD-0508: intents and notifications hold Restrict FKs to the events and tasks
            // below, so they have to go first or the whole cleanup fails.
            await db.AgentTaskDispatchWarningIntents.Where(i => taskIds.Contains(i.TaskId)).ExecuteDeleteAsync();
            await db.AgentTaskLandNotifications.Where(n => taskIds.Contains(n.TaskId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            var depths = await db.AgentTasks
                .Where(t => t.Title == marker)
                .Select(t => t.Depth)
                .Distinct()
                .OrderByDescending(d => d)
                .ToListAsync();
            foreach (var depth in depths)
                await db.AgentTasks.Where(t => t.Title == marker && t.Depth == depth).ExecuteDeleteAsync();
        }

        await db.Agents.Where(a => a.Name == marker).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Cwd.EndsWith(marker)).ExecuteDeleteAsync();
        await db.AuditRecords.Where(a => a.Summary.StartsWith(marker)).ExecuteDeleteAsync();
    }

    private static async Task<Guid> SeedAuditAsync(
        string marker, DateTime createdAt, string? fullContent, string summarySuffix = "")
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AuditRecords.Add(new AuditRecord
        {
            Id = id,
            EventType = AuditEventType.LlmCall,
            ModelName = "test-model",
            TokensIn = 10,
            TokensOut = 20,
            CostUsd = 0.001m,
            DurationMs = 100,
            Summary = $"{marker}{summarySuffix}",
            FullContent = fullContent,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<AuditRecord?> GetAuditAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.AuditRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }
}
