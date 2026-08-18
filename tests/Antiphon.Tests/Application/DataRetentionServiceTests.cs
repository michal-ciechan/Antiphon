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
/// CARD-0044 slice 1: transcript deletion is per-session all-or-nothing, and settled queue rows
/// are pruned independently of session liveness. The sweep is global, so this class is
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
        await db.Agents.Where(a => a.Name == marker).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Cwd.EndsWith(marker)).ExecuteDeleteAsync();
    }
}
