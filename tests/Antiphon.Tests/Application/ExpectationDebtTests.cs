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
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Succeeded, now.AddMinutes(-70)));
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

    [Test]
    public async Task C650_Unknown_runner_is_not_missing_session()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var remote = Guid.NewGuid();
        var remoteSession = Guid.NewGuid();
        var runnerOnly = Guid.NewGuid();
        var runnerOnlySession = Guid.NewGuid();
        var proven = Guid.NewGuid();
        var provenSession = Guid.NewGuid();
        var dispatched = now.AddMinutes(-30);

        await using (var db = world.Db())
        {
            foreach (var id in new[] { remoteSession, runnerOnlySession, provenSession })
                db.AgentSessions.Add(ExpectationTestWorld.Session(id, null, dispatched, SessionStatus.Running));
            db.AgentTasks.Add(world.Task(remote, AgentTaskStatus.Working, dispatched, remoteSession, "server2"));
            db.AgentTasks.Add(world.Task(runnerOnly, AgentTaskStatus.Working, dispatched, runnerOnlySession, "server2"));
            db.AgentTasks.Add(world.Task(proven, AgentTaskStatus.Working, dispatched, provenSession));
            await db.SaveChangesAsync();
        }

        var remoteSubject = ExpectationSubjects.Silent(world.Directive.Id, remote, dispatched);
        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        // Runner confirms the session is gone: that is a missing session, and it opens an episode.
        await using (var db = world.Db())
        {
            var gone = Probe(new() { [remoteSession] = new ExpectationSessionProbe(false, now, "gone") }, available: true);
            var scan = await world.Service(db, clock).ScanAsync(world.Directive, gone, CancellationToken.None);
            scan.Evaluation.SilentInFlight.ShouldHaveSingleItem().SubjectKey.ShouldBe(remoteSubject);
            scan.Evaluation.SilentInFlight[0].ReasonCode.ShouldBe("missing-session");
        }

        // The runner then cannot be asked: Unknown, not missing, and the open episode stays open.
        foreach (var minutes in new[] { 2, 4 })
        {
            clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(minutes), TimeSpan.Zero));
            await using var db = world.Db();
            var unknown = Probe(new() { [remoteSession] = new ExpectationSessionProbe(null, null, "unreachable") }, available: null);
            var scan = await world.Service(db, clock).ScanAsync(world.Directive, unknown, CancellationToken.None);
            scan.Evaluation.SilentInFlight.ShouldBeEmpty();
            scan.Evaluation.UnknownSubjectKeys.ShouldContain(remoteSubject);
            scan.Evaluation.UnknownSubjectKeys.ShouldContain(ExpectationSubjects.Silent(world.Directive.Id, runnerOnly, dispatched));
            scan.Evaluation.UnknownSubjectKeys.ShouldNotContain(ExpectationSubjects.Silent(world.Directive.Id, proven, dispatched));
            scan.NudgesCommitted.ShouldBe(0);
        }

        await using var read = world.Db();
        var episode = await read.ExpectationEpisodes.AsNoTracking().SingleAsync(row => row.SubjectKey == remoteSubject);
        episode.ResolvedAt.ShouldBeNull();
        (await read.ExpectationEpisodes.CountAsync(row => row.Kind == ExpectationEpisodeKind.SilentInFlight)).ShouldBe(1);
    }

    [Test]
    public async Task C650_Fresh_stall_policy_verdict_is_rolled_up()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var looping = Guid.NewGuid();
        var loopingSession = Guid.NewGuid();
        var editing = Guid.NewGuid();
        var editingSession = Guid.NewGuid();
        var dispatched = now.AddMinutes(-40);

        await using (var db = world.Db())
        {
            foreach (var (task, session) in new[] { (looping, loopingSession), (editing, editingSession) })
            {
                db.AgentSessions.Add(ExpectationTestWorld.Session(session, null, dispatched, SessionStatus.Running));
                db.AgentTasks.Add(world.Task(task, AgentTaskStatus.Working, dispatched, session));
                db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(
                    session, 1, TranscriptKinds.UserPrompt, dispatched.AddMinutes(1), "go"));
                var sequence = 2L;
                foreach (var minutesAgo in new[] { 35, 12, 9, 6, 3, 1 })
                {
                    db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(
                        session, sequence++, TranscriptKinds.ToolCall, now.AddMinutes(-minutesAgo),
                        toolName: "Bash", toolInput: "{\"command\":\"git status\"}"));
                }
            }

            await db.SaveChangesAsync();
        }

        var delegation = new Antiphon.Server.Application.Settings.DelegationSettings();
        delegation.StallDetection.StallMinutes = 10;
        delegation.StallDetection.LookBackMinutes = 15;
        delegation.StallDetection.MinRowsInWindow = 3;
        // The existing workspace arm withholds a stall when a file changed recently.
        var probes = ExpectationProbeInput.None with
        {
            Workspace = new Dictionary<Guid, Antiphon.Server.Application.Dtos.WorkspaceProgressArm>
            {
                [editing] = new(true, now.AddMinutes(-2), null, false),
            },
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock, delegation: delegation)
                .ScanAsync(world.Directive, probes, CancellationToken.None);
            var stalled = scan.Evaluation.SilentInFlight.ShouldHaveSingleItem();
            stalled.AffectedTaskIds.ShouldBe([looping]);
            stalled.ReasonCode.ShouldBe("progress-stalled");
            stalled.Evidence.ShouldContain("no novel progress");
            stalled.SubjectKey.ShouldBe(ExpectationSubjects.Silent(world.Directive.Id, looping, dispatched));
            scan.NudgesCommitted.ShouldBe(1);
        }

        // The stall policy's own switch is honoured: disabled means no roll-up.
        var disabled = new Antiphon.Server.Application.Settings.DelegationSettings();
        disabled.StallDetection.Enabled = false;
        clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(1), TimeSpan.Zero));
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock, delegation: disabled)
                .ScanAsync(world.Directive, probes, CancellationToken.None);
            scan.Evaluation.SilentInFlight.ShouldBeEmpty();
        }

        await using var read = world.Db();
        (await read.AgentTaskEvents.CountAsync(row => row.Type == AgentTaskEventType.Check && row.AgentTaskId == editing))
            .ShouldBe(0);
        (await read.AgentTasks.SingleAsync(row => row.Id == looping)).Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task C650_Receipt_catchup_and_concurrent_settlement_withhold_stale_nudge()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var noteTask = Guid.NewGuid();
        var silentTask = Guid.NewGuid();
        var note = ExpectationTestWorld.Note(noteTask, world.OwnedSessionId, LandNotificationState.AwaitingReceipt,
            now.AddMinutes(-15));

        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(noteTask, AgentTaskStatus.Succeeded, now.AddMinutes(-70)));
            db.AgentTasks.Add(world.Task(silentTask, AgentTaskStatus.Dispatched, now.AddMinutes(-20)));
            db.AgentTaskEvents.Add(note.Source);
            db.AgentTaskLandNotifications.Add(note.Note);
            await db.SaveChangesAsync();
        }

        // The receipt lands during catch-up, and the dispatcher settles the silent task meanwhile.
        var catchUp = new SettleDuringCatchUp(world, note.Note.Body, silentTask, now);
        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock, catchUp).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
            scan.Evaluation.UndeliveredNotes.ShouldHaveSingleItem();
            scan.Evaluation.SilentInFlight.ShouldHaveSingleItem();
            scan.NudgesCommitted.ShouldBe(0);
            scan.NudgeId.ShouldBeNull();
        }

        catchUp.Calls.ShouldBe(1);
        catchUp.Sessions.ShouldContain(world.OwnedSessionId);
        await using (var read = world.Db())
        {
            (await read.ExpectationNudges.CountAsync()).ShouldBe(0);
            (await read.CardComments.CountAsync(row => row.Author == ExpectationLedger.AuditAuthor)).ShouldBe(0);
            (await read.AgentTaskEvents.CountAsync(row => row.Type == AgentTaskEventType.Check)).ShouldBe(0);
            // The watchdog never settles CARD-0641's note itself.
            (await read.AgentTaskLandNotifications.SingleAsync(row => row.Id == note.Note.Id)).State
                .ShouldBe(LandNotificationState.AwaitingReceipt);
        }

        // Refreshed observation now excludes both, so a later scan does not nudge either.
        clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(11), TimeSpan.Zero));
        await using (var db = world.Db())
        {
            var scan = await world.Service(db, clock).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
            scan.Evaluation.UndeliveredNotes.ShouldBeEmpty();
            scan.Evaluation.SilentInFlight.ShouldBeEmpty();
            scan.NudgesCommitted.ShouldBe(0);
        }
    }

    private static ExpectationProbeInput Probe(Dictionary<Guid, ExpectationSessionProbe> sessions, bool? available) =>
        ExpectationProbeInput.None with
        {
            Runners = new Dictionary<string, ExpectationRunnerProbe>(StringComparer.Ordinal)
            {
                ["server2"] = new(available, null, available is null ? "unreachable" : "ok"),
            },
            Sessions = sessions,
        };

    private sealed class SettleDuringCatchUp(ExpectationTestWorld world, string body, Guid taskId, DateTime now)
        : IExpectationCatchUp
    {
        public int Calls { get; private set; }
        public List<Guid> Sessions { get; } = [];

        public async Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
        {
            Calls++;
            Sessions.AddRange(sessionIds);
            await using var db = world.Db();
            db.TranscriptEntries.Add(ExpectationTestWorld.Transcript(
                world.OwnedSessionId, 10, TranscriptKinds.UserPrompt, now, body));
            var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId, ct);
            task.Status = AgentTaskStatus.Succeeded;
            task.Result = "settled concurrently";
            task.CompletedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }
}
