using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0650 V-3a/V-3b: silent in-flight work and undelivered caller notes, on real PostgreSQL.
/// </summary>
[Category("Integration")]
public sealed class ExpectationDebtTests
{
    [Test]
    public async Task C650_Dispatched_without_session_and_report_is_detected()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var unbound = Guid.NewGuid();
        var terminal = Guid.NewGuid();
        var young = Guid.NewGuid();
        var localActivity = Guid.NewGuid();
        var reported = Guid.NewGuid();
        var lateTranscript = Guid.NewGuid();
        var stoppedSession = Guid.NewGuid();
        var lateSession = Guid.NewGuid();

        await using (var db = world.Db())
        {
            db.AgentSessions.Add(ExpectationTestWorld.Session(stoppedSession, null, now.AddMinutes(-30), SessionStatus.Stopped));
            db.AgentSessions.Add(ExpectationTestWorld.Session(lateSession, null, now.AddMinutes(-30), SessionStatus.Stopped));
            // Exactly ten minutes silent is due; one second short is not.
            db.AgentTasks.Add(world.Task(unbound, AgentTaskStatus.Dispatched, now.AddMinutes(-10)));
            db.AgentTasks.Add(world.Task(terminal, AgentTaskStatus.Dispatched, now.AddMinutes(-25), stoppedSession));
            db.AgentTasks.Add(world.Task(young, AgentTaskStatus.Dispatched, now.AddMinutes(-10).AddSeconds(1)));
            db.AgentTasks.Add(world.Task(localActivity, AgentTaskStatus.Dispatched, now.AddMinutes(-40)));
            db.AgentTasks.Add(world.Task(reported, AgentTaskStatus.Dispatched, now.AddMinutes(-40), result: "done"));
            db.AgentTasks.Add(world.Task(lateTranscript, AgentTaskStatus.Dispatched, now.AddMinutes(-40), lateSession));
            db.AgentTaskEvents.Add(ExpectationTestWorld.Event(localActivity, AgentTaskEventType.Refined, now.AddMinutes(-4), "refined"));
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(
                lateSession, 1, TranscriptKinds.AssistantText, now.AddMinutes(-3), "still here"));
            await db.SaveChangesAsync();
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
            var silent = scan.Evaluation.SilentInFlight;
            silent.Select(row => row.AffectedTaskIds.Single()).OrderBy(id => id)
                .ShouldBe(new[] { unbound, terminal }.OrderBy(id => id).ToArray());
            var missing = silent.Single(row => row.AffectedTaskIds.Single() == unbound);
            missing.Kind.ShouldBe(ExpectationEpisodeKind.SilentInFlight);
            missing.IsDue.ShouldBeTrue();
            missing.ReasonCode.ShouldBe("missing-session");
            missing.SubjectKey.ShouldBe(ExpectationSubjects.Silent(world.Directive.Id, unbound, now.AddMinutes(-10)));
            silent.Single(row => row.AffectedTaskIds.Single() == terminal).ReasonCode.ShouldBe("terminal-session");
            scan.NudgesCommitted.ShouldBe(1);
        }

        await using var read = world.Db();
        var episodes = await read.ExpectationEpisodes.AsNoTracking()
            .Where(row => row.Kind == ExpectationEpisodeKind.SilentInFlight)
            .ToListAsync();
        episodes.Count.ShouldBe(2);
        var checks = await read.AgentTaskEvents.AsNoTracking()
            .Where(row => row.Type == AgentTaskEventType.Check)
            .Select(row => row.AgentTaskId)
            .ToListAsync();
        checks.OrderBy(id => id).ShouldBe(new[] { unbound, terminal }.OrderBy(id => id).ToArray());
        var nudge = await read.ExpectationNudges.AsNoTracking().SingleAsync();
        nudge.Body.ShouldContain(unbound.ToString("D"));
        nudge.Body.ShouldContain("[expectation-ack:" + nudge.Id.ToString("D") + "]");
    }

    [Test]
    public async Task C650_Note_age_includes_parked_and_retry_debt()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var taskId = Guid.NewGuid();
        var foreignSession = Guid.NewGuid();
        var parkedMessage = Guid.NewGuid();
        var canceledMessage = Guid.NewGuid();
        var parked = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.AwaitingReceipt,
            now.AddMinutes(-15), queueMessageId: parkedMessage);
        // A retry just scheduled does not reset the age: CreatedAt owns it.
        var retry = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.RetryPending,
            now.AddMinutes(-10), attempts: 4, nextAttemptAt: now.AddMinutes(1), lastError: "composer-busy");
        var canceled = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.Canceled,
            now.AddMinutes(-20), kind: LandNotificationKind.Outcome, queueMessageId: canceledMessage);
        var young = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.Queued,
            now.AddMinutes(-10).AddSeconds(1));
        var confirmed = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.Confirmed,
            now.AddMinutes(-60));
        var notRequired = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.NotRequired,
            now.AddMinutes(-60));
        var legacy = ExpectationTestWorld.Note(taskId, world.OwnedSessionId, LandNotificationState.LegacyUnverified,
            now.AddMinutes(-60), legacy: true);
        var foreign = ExpectationTestWorld.Note(taskId, foreignSession, LandNotificationState.AwaitingReceipt,
            now.AddMinutes(-60));

        await using (var db = world.Db())
        {
            db.AgentSessions.Add(ExpectationTestWorld.Session(foreignSession, Guid.NewGuid(), now.AddHours(-1), SessionStatus.Running));
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Completed, now.AddMinutes(-70)));
            db.SessionQueuedMessages.Add(ExpectationTestWorld.Queued(parkedMessage, world.OwnedSessionId,
                QueuedMessageStatus.Pending, now.AddMinutes(-15), 1));
            db.SessionQueuedMessages.Add(ExpectationTestWorld.Queued(canceledMessage, world.OwnedSessionId,
                QueuedMessageStatus.Canceled, now.AddMinutes(-20), 2));
            foreach (var (source, note) in new[] { parked, retry, canceled, young, confirmed, notRequired, legacy, foreign })
            {
                db.AgentTaskEvents.Add(source);
                db.AgentTaskLandNotifications.Add(note);
            }

            await db.SaveChangesAsync();
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
            var notes = scan.Evaluation.UndeliveredNotes;
            notes.Select(row => row.SubjectKey).OrderBy(key => key, StringComparer.Ordinal).ShouldBe(
                new[] { parked.Note.Id, retry.Note.Id, canceled.Note.Id }
                    .Select(id => ExpectationSubjects.Note(world.Directive.Id, id))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray());
            notes.ShouldAllBe(row => row.Kind == ExpectationEpisodeKind.UndeliveredNote && row.IsDue);
            var retryRow = notes.Single(row => row.SubjectKey == ExpectationSubjects.Note(world.Directive.Id, retry.Note.Id));
            retryRow.Evidence.ShouldContain("RetryPending");
            retryRow.Evidence.ShouldContain("composer-busy");
            retryRow.Evidence.ShouldContain("age 10m");
            notes.Single(row => row.SubjectKey == ExpectationSubjects.Note(world.Directive.Id, parked.Note.Id))
                .Evidence.ShouldContain("queue Pending");
            notes.Single(row => row.SubjectKey == ExpectationSubjects.Note(world.Directive.Id, canceled.Note.Id))
                .Evidence.ShouldContain("queue Canceled");
            notes.ShouldAllBe(row => row.AffectedTaskIds.Single() == taskId);
            scan.NudgesCommitted.ShouldBe(1);
        }

        await using var read = world.Db();
        (await read.ExpectationEpisodes.CountAsync(row => row.Kind == ExpectationEpisodeKind.UndeliveredNote)).ShouldBe(3);
        // The watchdog observes note debt. It does not settle or resend CARD-0641's notes.
        var states = await read.AgentTaskLandNotifications.AsNoTracking()
            .ToDictionaryAsync(row => row.Id, row => row.State);
        states[retry.Note.Id].ShouldBe(LandNotificationState.RetryPending);
        states[parked.Note.Id].ShouldBe(LandNotificationState.AwaitingReceipt);
        (await read.SessionQueuedMessages.CountAsync()).ShouldBe(2);
    }
}
