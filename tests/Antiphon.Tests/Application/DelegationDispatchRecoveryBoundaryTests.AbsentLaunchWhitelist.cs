using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("F1-source-task-delivered")]
    [Arguments("F1-custody-on-other-session")]
    [Arguments("F2-queued-prompt")]
    [Arguments("F2-prior-working")]
    [Arguments("F2-pending-ui")]
    [Arguments("F2-followup")]
    [Arguments("F2-control")]
    [Arguments("F2-reply")]
    [Arguments("prior-replied-task")]
    [Arguments("old-transcript")]
    [Arguments("unknown-transcript-kind")]
    [Arguments("missing-brief")]
    [Arguments("verdict-time-only")]
    [Arguments("rules-prompt-only")]
    [Arguments("submission-start-only")]
    [Arguments("correlation-only")]
    [Arguments("note-header-only")]
    [Arguments("missing-dispatch-time")]
    [Arguments("conflicting-task-identity")]
    public async Task C1149_Whitelist_gap_keeps_previous_failure(string condition)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var projects = Directory.CreateTempSubdirectory("c1149-whitelist-").FullName;
        try
        {
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape());
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var brief = await db.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
                var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                switch (condition)
                {
                    case "F1-source-task-delivered":
                        brief.ExecutionTaskId = null;
                        brief.SourceTaskId = seeded.TaskId;
                        brief.Body = DelegationReportFormatter.TaskMarker(seeded.TaskId) + "\noriginal rules brief";
                        brief.Status = QueuedMessageStatus.Sent;
                        brief.SentAt = seeded.DispatchedAt;
                        brief.DeliveryAttempts = 1;
                        brief.DeliveryVerdict = DeliveryVerdict.Delivered;
                        break;
                    case "F1-custody-on-other-session":
                        var other = await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
                        // The relationship is the task custody identity, independent of recipient.
                        var parent = Guid.NewGuid();
                        db.AgentSessions.Add(new AgentSession { Id = parent, Cwd = other.Cwd,
                            CreatedAt = other.CreatedAt, StartedAt = other.StartedAt, LastSeenAt = other.LastSeenAt });
                        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(),
                            AgentSessionId = parent, SourceTaskId = seeded.TaskId, Body = "prior reply",
                            Origin = QueuedMessageOrigin.Delegation, Sequence = 1, CreatedAt = seeded.DispatchedAt });
                        break;
                    case "F2-queued-prompt": Add(TranscriptKinds.QueuedUserPrompt, 1); break;
                    case "F2-prior-working":
                        Add(TranscriptKinds.AssistantText, 1);
                        await db.SaveChangesAsync();
                        (await SessionMessageQueueService.IsWorkingAsync(db, seeded.SessionId, default))
                            .ShouldBeTrue("the session really reported Working before its turn ended");
                        Add(TranscriptKinds.TurnEnd, 2);
                        break;
                    case "F2-pending-ui":
                    case "F2-followup":
                    case "F2-control":
                    case "F2-reply":
                        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(),
                            AgentSessionId = seeded.SessionId, Body = "retained " + condition,
                            Origin = condition == "F2-pending-ui" ? QueuedMessageOrigin.Ui : QueuedMessageOrigin.Delegation,
                            SourceTaskId = condition == "F2-reply" ? seeded.TaskId : null,
                            Sequence = 2, CreatedAt = seeded.DispatchedAt.AddSeconds(1) });
                        break;
                    case "prior-replied-task": task.RepliedAt = seeded.DispatchedAt; break;
                    case "old-transcript": Add(TranscriptKinds.TurnEnd, 1, seeded.DispatchedAt.AddHours(-1)); break;
                    case "unknown-transcript-kind": Add("future-entry-kind", 1); break;
                    case "missing-brief": db.SessionQueuedMessages.Remove(brief); break;
                    case "verdict-time-only": brief.DeliveryVerdictAt = seeded.DispatchedAt; break;
                    case "rules-prompt-only": brief.RulesPromptSequence = 0; break;
                    case "submission-start-only": brief.SubmissionStartedAt = seeded.DispatchedAt; break;
                    case "correlation-only": brief.ContentDigest = "reply-correlation"; break;
                    case "note-header-only": brief.NoteHeader = "reply"; break;
                    case "missing-dispatch-time": task.DispatchedAt = null; break;
                    case "conflicting-task-identity": brief.SourceTaskId = Guid.NewGuid(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(condition));
                }
                await db.SaveChangesAsync();
                (await SessionMessageQueueService.IsWorkingAsync(db, seeded.SessionId, default)).ShouldBeFalse(condition);

                void Add(string kind, long sequence, DateTime? timestamp = null) => db.TranscriptEntries.Add(
                    new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = seeded.SessionId,
                        Kind = kind, Sequence = sequence, Text = "attempt activity", StopReason = "end_turn",
                        Timestamp = timestamp ?? seeded.DispatchedAt, CreatedAt = seeded.DispatchedAt });
            }
            List<(Guid Id, string Body, string? Spill, long Sequence)> before;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
                before = (await db.SessionQueuedMessages.AsNoTracking().ToListAsync())
                    .Select(m => (m.Id, m.Body, m.RemoteSpillBody, m.Sequence)).ToList();
            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(schema.ConnectionString, runner, stopper,
                new FakeTimeProvider(DateTimeOffset.UtcNow), new DeadSessionFirstSeenState(), projectsRoot: projects))
                await host.DueAsync();
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var result = await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            result.Status.ShouldBe(AgentTaskStatus.Failed, condition);
            result.CompletedAt.ShouldNotBeNull(condition);
            result.FailureReason!.ShouldContain(SessionReconciliationService.RunnerUnknownSessionReason, condition);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId
                && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0, condition);
            foreach (var row in before)
            {
                var retained = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == row.Id);
                Encoding.UTF8.GetBytes(retained.Body).ShouldBe(Encoding.UTF8.GetBytes(row.Body), condition);
                retained.RemoteSpillBody.ShouldBe(row.Spill, condition);
                retained.Sequence.ShouldBe(row.Sequence, condition);
            }
            Quiet(runner, stopper, condition);
        }
        finally { Directory.Delete(projects, true); }
    }
}
