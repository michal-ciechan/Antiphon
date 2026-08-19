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
/// CARD-0044 slices 1-3: transcript deletion is per-session all-or-nothing, settled queue rows
/// are pruned independently of session liveness, terminal AgentSession rows past 90d are
/// deleted when nothing still names them, and AgentTask trees past 180d are deleted whole or
/// not at all. The sweep is global, so this class is
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

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static DataRetentionService CreateService(AppDbContext db, RetentionSettings? settings = null) =>
        new(
            db,
            Options.Create(settings ?? new RetentionSettings()),
            TimeProvider.System,
            NullLogger<DataRetentionService>.Instance);

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
        DateTime? settledAt = null)
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
        }

        var taskIds = await db.AgentTasks.Where(t => t.Title == marker).Select(t => t.Id).ToListAsync();
        if (taskIds.Count > 0)
        {
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
    }
}
