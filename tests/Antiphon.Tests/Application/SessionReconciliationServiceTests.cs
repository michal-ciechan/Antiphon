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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Pins the reconciliation backstop: DB sessions/agents must converge to runner truth even when
/// exit events were missed entirely (the "Running for a week on a dead PID" incident).
///
/// Globally serial (parameterless NotInParallel): ScanAsync sweeps EVERY live session/Working
/// agent in the shared test database, so running concurrently with other suites would flip their
/// in-flight agents. Assertions are row-scoped for the same reason — other tests' leftovers may
/// legitimately get corrected during our scan.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class SessionReconciliationServiceTests
{
    [Test]
    public async Task Session_unknown_to_runner_is_failed_and_its_agent_reset()
    {
        var marker = NewMarker();
        try
        {
            var (agent, session) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var eventBus = new MockEventBus();
            var service = BuildService(db, new FakeRunnerClient { Sessions = [] }, eventBus);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == session);
            dbSession.Status.ShouldBe(SessionStatus.Failed);
            dbSession.FailureReason.ShouldNotBeNull();
            dbSession.FailureReason.ShouldContain("does not know this session");
            dbSession.EndedAt.ShouldNotBeNull();

            var dbAgent = await verify.Agents.SingleAsync(a => a.Id == agent);
            dbAgent.Status.ShouldBe(AgentStatus.Failed);

            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "SessionExited");
            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Runner_reported_exit_is_mirrored_to_the_db_session()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: 0, ExitReason: AgentExitReason.Unknown, LastSequence: 10)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Stopped); // exit code 0 → clean stop
            dbSession.ExitCode.ShouldBe(0);
            dbSession.EndedAt.ShouldNotBeNull();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Runner_reported_HerdrPaneClosed_exit_fails_the_session_not_a_clean_stop()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: null, ExitReason: AgentExitReason.HerdrPaneClosed,
                        LastSequence: 10)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Failed, "pane-closed must not slip in as Stopped");
            dbSession.FailureReason.ShouldNotBeNull();
            dbSession.FailureReason.ShouldContain("HerdrPaneClosed");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Starting_session_within_grace_is_left_alone()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);

            await using var db = CreateContext();
            var service = BuildService(db, new FakeRunnerClient { Sessions = [] }, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Starting);
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Running);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Working_agent_within_grace_is_left_alone_even_without_live_session()
    {
        var marker = NewMarker();
        try
        {
            // Agent flipped to Working just now, session already closed — e.g. the launch queue is
            // between "session row created" and "process running". Must not be touched yet.
            var agentId = await SeedAgentAsync(marker, AgentStatus.Running, sessionId: Guid.NewGuid(), updatedAt: DateTime.UtcNow);

            await using var db = CreateContext();
            var service = BuildService(db, new FakeRunnerClient { Sessions = [] }, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Running);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Unreachable_runner_skips_the_session_pass()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient { ListError = new HttpRequestException("connection refused") };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            // Sessions untouched (runner may just be restarting) — and because the session is
            // still live in the DB, the agent stays Working too. No guessing while blind.
            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Running);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    // ---------- pass 3 (CARD-0056): the runner is alive, the DB wrote it off ----------

    /// <summary>
    /// The incident, mirrored. A launch-verification false positive marked a healthy session
    /// Failed; the process kept running and no pass ever looked in this direction. Re-adoption is
    /// the default action, and the agent pointer still names the session, so the agent comes back
    /// with it.
    /// </summary>
    [Test]
    public async Task Failed_session_the_runner_still_serves_is_re_adopted_and_its_agent_restored()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true,
                agentStatus: AgentStatus.Failed,
                failureReason: "No composer evidence appeared for the typed body.");

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            var eventBus = new MockEventBus();
            var service = BuildService(db, RunnerRunning(sessionId), eventBus, alerts);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Running);
            dbSession.EndedAt.ShouldBeNull();
            dbSession.ExitCode.ShouldBeNull();
            dbSession.FailureReason.ShouldBeNull();
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Running);

            var incident = await verify.AgentIncidents.SingleAsync(i => i.SessionId == sessionId);
            incident.Kind.ShouldBe(AgentIncidentKind.SessionReAdopted);
            incident.Severity.ShouldBe(AlertSeverity.Warning);
            incident.AgentId.ShouldBe(agentId);
            incident.Message.ShouldContain(
                "No composer evidence appeared for the typed body.",
                customMessage: "the reason it was wrongly failed belongs on the record");
            alerts.For(sessionId).ShouldNotBeEmpty();
            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// The cefed08a shape, and the constraint that outranks everything in this pass: the session's
    /// agent has moved on to a different session, so nothing claims this one. It stays Running and
    /// VISIBLE — never killed. Inferring a kill from "unclaimed" would have killed the operator's
    /// own live conversation mid-sentence, which is the session this whole card is about.
    /// </summary>
    [Test]
    public async Task An_unclaimed_session_is_re_adopted_and_left_running_for_the_operator()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Running);
            // The agent has been relaunched onto a different session since — the pointer no longer
            // names this one, so this session is unclaimed.
            var otherSessionId = Guid.NewGuid();
            await using (var repoint = CreateContext())
            {
                await repoint.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.PersistentSessionId, otherSessionId.ToString("D")));
            }

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            var service = BuildService(db, RunnerRunning(sessionId), new MockEventBus(), alerts);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Running, "unclaimed must never imply kill");
            var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
            agent.PersistentSessionId.ShouldBe(otherSessionId.ToString("D"), "the pointer is not stolen back");

            (await verify.AgentIncidents.Where(i => i.SessionId == sessionId).ToListAsync())
                .ShouldBeEmpty("an incident needs an agent to belong to");
            var alert = alerts.For(sessionId).ShouldHaveSingleItem();
            alert.AgentId.ShouldBeNull();
            alert.Detail.ShouldNotBeNull();
            alert.Detail.ShouldContain("unclaimed");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// Evidence is presence of health, not absence of bad news. The runner lists the session as
    /// Running but its buffer probe does not answer, so the pty-host's pipe cannot be shown to be
    /// alive — change nothing, and say so loudly. Unresponsive-but-running is a state for a human.
    /// </summary>
    [Test]
    public async Task A_session_whose_probe_does_not_answer_is_left_exactly_as_it_was()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var runner = RunnerRunning(sessionId);
            runner.BufferError = new HttpRequestException("the pty-host pipe is gone");
            var alerts = new RecordingAlertService();
            var service = BuildService(db, runner, new MockEventBus(), alerts);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Failed);
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Failed);
            alerts.For(sessionId).ShouldContain(a => a.Severity == AlertSeverity.Error);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// Same rule from the other side: the runner says Running but names no process at all. That is
    /// not evidence of a live process, so nothing is written back.
    /// </summary>
    [Test]
    public async Task A_running_session_with_no_process_behind_it_is_not_re_adopted()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var runner = RunnerRunning(sessionId, pid: null, hostPid: null);
            var alerts = new RecordingAlertService();
            var service = BuildService(db, runner, new MockEventBus(), alerts);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Failed);
            runner.Probed.ShouldBeEmpty("no process named — there is nothing worth probing");
            alerts.For(sessionId).ShouldContain(a => a.Severity == AlertSeverity.Error);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// The only arm that may end a process, and why it is allowed to: an operator already asked for
    /// this session to stop and the kill evidently did not take. Re-issuing it enacts a decision
    /// that was already made rather than inferring one.
    /// </summary>
    [Test]
    public async Task A_stopped_session_the_runner_still_serves_gets_its_kill_re_issued()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Stopped, staleAgent: true, agentStatus: AgentStatus.Stopped);

            await using var db = CreateContext();
            var runner = RunnerRunning(sessionId);
            var service = BuildService(db, runner, new MockEventBus(), new RecordingAlertService());

            await service.ScanAsync(CancellationToken.None);

            runner.Killed.ShouldBe([sessionId]);
            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Stopped);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// A running session the database has no row for at all (the second leak found on 2026-08-16 —
    /// its owning agent's trail was cascade-deleted). Alert only: nothing here knows what it is, and
    /// a session nobody can name is still somebody's work. The operator reaps it from the UI.
    /// </summary>
    [Test]
    public async Task A_runner_session_with_no_database_row_is_only_alerted_about()
    {
        var strayId = Guid.NewGuid();
        await using var db = CreateContext();
        var runner = RunnerRunning(strayId);
        var alerts = new RecordingAlertService();
        var service = BuildService(db, runner, new MockEventBus(), alerts);

        await service.ScanAsync(CancellationToken.None);

        runner.Killed.ShouldBeEmpty("never kill what you cannot name");
        var alert = alerts.Raised
            .Where(a => a.DedupKey == "reconciler:orphans")
            .ShouldHaveSingleItem();
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.Detail.ShouldNotBeNull();
        alert.Detail.ShouldContain(strayId.ToString());
    }

    /// <summary>
    /// Oscillation is bounded, not trusted away. Something that keeps failing a session the runner
    /// keeps serving is a fight between two live components, and the fourth round stops and
    /// escalates instead of running the loop forever.
    /// </summary>
    [Test]
    public async Task Re_adoption_stops_and_escalates_once_the_flap_cap_is_reached()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            // One state object across every sweep — it is a singleton in production for exactly this
            // reason: the service is scoped, so a per-sweep counter would bound nothing.
            var flapState = new SessionReAdoptionState();
            var alerts = new RecordingAlertService();
            for (var round = 1; round <= 4; round++)
            {
                await using var db = CreateContext();
                var service = BuildService(
                    db, RunnerRunning(sessionId), new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);

                await using var verify = CreateContext();
                var status = (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status;
                if (round <= 3)
                {
                    status.ShouldBe(SessionStatus.Running, $"round {round} is within the cap");
                    // Whatever wrongly failed it does so again — the flap this cap exists for.
                    await verify.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, SessionStatus.Failed));
                }
                else
                {
                    status.ShouldBe(SessionStatus.Failed, "the fourth round stops re-adopting");
                }
            }

            flapState.CountFor(sessionId).ShouldBe(4);
            alerts.For(sessionId).ShouldContain(a => a.Severity == AlertSeverity.Critical);
            await using var final = CreateContext();
            (await final.AgentIncidents
                    .Where(i => i.AgentId == agentId && i.Severity == AlertSeverity.Critical)
                    .ToListAsync())
                .ShouldHaveSingleItem();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// The kill switch: the mismatch is still reported, but nothing is written back.
    /// </summary>
    [Test]
    public async Task Re_adoption_can_be_switched_off_and_then_only_reports()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            var service = BuildService(
                db, RunnerRunning(sessionId), new MockEventBus(), alerts, reAdoptEnabled: false);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Failed);
            alerts.For(sessionId).ShouldNotBeEmpty();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// An unreachable runner is no evidence in EITHER direction — pass 3 must be as blind as pass 1
    /// while the truth is unknowable.
    /// </summary>
    [Test]
    public async Task Unreachable_runner_skips_the_re_adoption_pass_too()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient { ListError = new HttpRequestException("connection refused") };
            var service = BuildService(db, runner, new MockEventBus(), new RecordingAlertService());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Failed);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Pending_herdr_session_is_not_failed_by_pass_1()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Starting", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                        LastSequence: 0, Adopted: true, Backend: SessionBackends.Herdr,
                        Pending: HerdrPendingReasons.Unreachable)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Running, "Pending is Starting, not Exited, not unknown");
            dbSession.FailureReason.ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task HerdrUnreachable_incident_fires_only_after_the_alert_minutes_threshold()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
            var pendingState = new HerdrPendingAlertState();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: clock.GetUtcNow().UtcDateTime.AddHours(-1),
                        Status: "Starting", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                        LastSequence: 0, Adopted: true, Backend: SessionBackends.Herdr,
                        Pending: HerdrPendingReasons.Unreachable)
                ]
            };

            await using (var db = CreateContext())
            {
                var service = BuildService(
                    db, runner, new MockEventBus(), pendingAlerts: pendingState, time: clock);
                await service.ScanAsync(CancellationToken.None);
            }

            await using (var early = CreateContext())
            {
                (await early.AgentIncidents.AnyAsync(
                    i => i.AgentId == agentId && i.Kind == AgentIncidentKind.HerdrUnreachable))
                    .ShouldBeFalse("first observation is not yet past the 5-minute threshold");
            }

            clock.Advance(TimeSpan.FromMinutes(6));
            await using (var db = CreateContext())
            {
                var service = BuildService(
                    db, runner, new MockEventBus(), pendingAlerts: pendingState, time: clock);
                await service.ScanAsync(CancellationToken.None);
            }

            await using var verify = CreateContext();
            var incident = await verify.AgentIncidents.SingleAsync(
                i => i.AgentId == agentId && i.Kind == AgentIncidentKind.HerdrUnreachable);
            incident.Severity.ShouldBe(AlertSeverity.Warning);
            incident.SessionId.ShouldBe(sessionId);

            clock.Advance(TimeSpan.FromMinutes(1));
            await using (var db = CreateContext())
            {
                var service = BuildService(
                    db, runner, new MockEventBus(), pendingAlerts: pendingState, time: clock);
                await service.ScanAsync(CancellationToken.None);
            }

            (await verify.AgentIncidents.CountAsync(
                i => i.AgentId == agentId && i.Kind == AgentIncidentKind.HerdrUnreachable))
                .ShouldBe(1, "never re-raised inside the alert window");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task HerdrUnreachable_incident_is_critical_when_channel_bound()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);
            await BindChannelAsync(agentId, marker);

            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
            var pendingState = new HerdrPendingAlertState();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: clock.GetUtcNow().UtcDateTime.AddHours(-1),
                        Status: "Starting", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                        LastSequence: 0, Adopted: true, Backend: SessionBackends.Herdr,
                        Pending: HerdrPendingReasons.Unreachable)
                ]
            };

            await using (var db = CreateContext())
            {
                var service = BuildService(
                    db, runner, new MockEventBus(), pendingAlerts: pendingState, time: clock);
                await service.ScanAsync(CancellationToken.None);
            }

            clock.Advance(TimeSpan.FromMinutes(6));
            await using (var db = CreateContext())
            {
                var service = BuildService(
                    db, runner, new MockEventBus(), pendingAlerts: pendingState, time: clock);
                await service.ScanAsync(CancellationToken.None);
            }

            await using var verify = CreateContext();
            var incident = await verify.AgentIncidents.SingleAsync(
                i => i.AgentId == agentId && i.Kind == AgentIncidentKind.HerdrUnreachable);
            incident.Severity.ShouldBe(AlertSeverity.Critical);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static SessionReconciliationService BuildService(
        AppDbContext db,
        ISessionRunnerClient runnerClient,
        MockEventBus eventBus,
        IAlertService? alerts = null,
        SessionReAdoptionState? reAdoptions = null,
        bool reAdoptEnabled = true,
        IPtyHostCensusProbe? census = null,
        PtyHostCensusAlertState? censusAlerts = null,
        bool censusAlertEnabled = true,
        HerdrPendingAlertState? pendingAlerts = null,
        TimeProvider? time = null,
        int herdrPendingAlertMinutes = 5) =>
        new(
            db,
            runnerClient,
            eventBus,
            alerts ?? new NoOpAlertService(),
            new RunnerReachabilityState(),
            reAdoptions ?? new SessionReAdoptionState(),
            pendingAlerts ?? new HerdrPendingAlertState(),
            // Default OFF for every pre-existing case: the census is global by nature and these
            // tests share a database, so a suite that had not thought about it must not start
            // raising census alerts as a side effect of other tests' rows.
            census ?? new StatedCensusProbe(PtyHostCensus.Unavailable),
            censusAlerts ?? new PtyHostCensusAlertState(),
            Options.Create(new SessionReconciliationSettings
            {
                Enabled = true,
                StartingGraceMs = 90_000,
                AgentGraceMs = 120_000,
                ReAdoptEnabled = reAdoptEnabled,
                CensusAlertEnabled = censusAlertEnabled && census is not null,
                HerdrPendingAlertMinutes = herdrPendingAlertMinutes,
            }),
            time ?? TimeProvider.System,
            NullLogger<SessionReconciliationService>.Instance);

    /// <summary>A runner that reports one session Running, with a real process behind it.</summary>
    private static FakeRunnerClient RunnerRunning(
        Guid sessionId, int? pid = 4242, int? hostPid = 4243) =>
        new()
        {
            Sessions =
            [
                new SessionRunnerSessionDto(
                    sessionId, Pid: pid, StartedAt: DateTime.UtcNow.AddHours(-1),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, HostPid: hostPid)
            ]
        };

    private sealed class NoOpAlertService : IAlertService
    {
        public Task RaiseAsync(AlertRaise alert, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingAlertService : IAlertService
    {
        private readonly List<AlertRaise> _raised = [];

        public IReadOnlyList<AlertRaise> Raised
        {
            get { lock (_raised) return _raised.ToList(); }
        }

        /// <summary>Alerts about one session — scoped, because the sweep is global.</summary>
        public IReadOnlyList<AlertRaise> For(Guid sessionId) =>
            Raised.Where(a => a.SessionId == sessionId).ToList();

        public Task RaiseAsync(AlertRaise alert, CancellationToken ct)
        {
            lock (_raised)
                _raised.Add(alert);
            return Task.CompletedTask;
        }
    }

    private static string NewMarker() => $"antiphon-reconciliation-tests-{Guid.NewGuid():N}";

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWorkingAgentWithSessionAsync(
        string marker,
        SessionStatus sessionStatus,
        bool staleAgent,
        AgentStatus agentStatus = AgentStatus.Running,
        string? failureReason = null)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var startedAt = sessionStatus == SessionStatus.Starting ? now : now.AddHours(-1);
        var closed = sessionStatus is SessionStatus.Failed or SessionStatus.Stopped;

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = sessionStatus,
            Cwd = Path.Combine(Path.GetTempPath(), marker),
            Cols = 120,
            Rows = 30,
            CreatedAt = startedAt,
            StartedAt = startedAt,
            LastSeenAt = startedAt,
            // What a wrongly-failed row really looks like: closed out, with a reason that is wrong.
            EndedAt = closed ? now.AddMinutes(-5) : null,
            ExitCode = closed ? 1 : null,
            FailureReason = failureReason
        });
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = agentStatus,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddHours(-2),
            UpdatedAt = staleAgent ? now.AddHours(-1) : now
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<Guid> SeedAgentAsync(
        string marker, AgentStatus status, Guid sessionId, DateTime updatedAt)
    {
        var agentId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = status,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = updatedAt.AddHours(-2),
            UpdatedAt = updatedAt
        });
        await db.SaveChangesAsync();
        return agentId;
    }

    private static async Task BindChannelAsync(Guid agentId, string marker)
    {
        await using var db = CreateContext();
        db.ChatChannels.Add(new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = marker,
            Kind = ChatChannelKind.Direct,
            Title = "Bound channel (test)",
            AgentId = agentId,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(string marker)
    {
        await using var db = CreateContext();
        // Incidents and alerts first: they reference the rows below.
        var sessionIds = await db.AgentSessions
            .Where(s => s.Cwd.EndsWith(marker))
            .Select(s => s.Id)
            .ToListAsync();
        var agentIds = await db.Agents.Where(a => a.Name == marker).Select(a => a.Id).ToListAsync();
        await db.AgentIncidents.Where(i => i.SessionId != null && sessionIds.Contains(i.SessionId.Value))
            .ExecuteDeleteAsync();
        await db.Alerts.Where(a => a.SessionId != null && sessionIds.Contains(a.SessionId.Value))
            .ExecuteDeleteAsync();
        await db.ChatChannels.Where(c => c.ExternalId == marker || (c.AgentId != null && agentIds.Contains(c.AgentId.Value)))
            .ExecuteDeleteAsync();
        await db.Agents.Where(a => a.Name == marker).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Cwd.EndsWith(marker)).ExecuteDeleteAsync();
    }

    private sealed class FakeRunnerClient : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];
        public Exception? ListError { get; set; }

        /// <summary>The per-session liveness probe: what it answers, and which sessions asked.</summary>
        public Exception? BufferError { get; set; }
        public List<Guid> Probed { get; } = [];
        public List<Guid> Killed { get; } = [];

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            ListError is not null ? Task.FromException<IReadOnlyList<SessionRunnerSessionDto>>(ListError) : Task.FromResult(Sessions);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Func<Guid, SessionRunnerSessionDto>? GetOverride { get; set; }

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            if (GetOverride is { } get)
                return Task.FromResult(get(sessionId));
            var match = Sessions.FirstOrDefault(s => s.SessionId == sessionId)
                ?? throw new KeyNotFoundException($"session {sessionId} not in fake runner");
            return Task.FromResult(match);
        }

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
        {
            Probed.Add(sessionId);
            return BufferError is not null
                ? Task.FromException<SessionRunnerBufferDto>(BufferError)
                : Task.FromResult(new SessionRunnerBufferDto(sessionId, "> ", 10));
        }

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Killed.Add(sessionId);
            return Task.FromResult(
                Sessions.First(s => s.SessionId == sessionId) with { Status = "Exited", ExitCode = 0 });
        }

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    // ---- pass 4: the census alert (CARD-0102 / coverage plan P0-3) --------------------------------
    //
    // Every case below states the runner's session list outright, so "unclaimed" means exactly the
    // sessions this test declared and no DB row exists for. Nothing here depends on what other rows
    // are in the shared test database — which matters, because the sweep is global and the count
    // being asserted is a count.

    /// <summary>
    /// The 2026-08-20 shape, one notch past the threshold: the runner is serving sessions the
    /// database has never heard of. This is the line the server log already printed four hours
    /// before a human found the leak by hand; all that was missing was somebody being told.
    /// </summary>
    [Test]
    public async Task Census_alert_fires_when_unclaimed_runner_sessions_pass_the_threshold()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, RunnerWithUnclaimed(12), new MockEventBus(), alerts,
            census: Census(ptyHosts: 12, claude: 12));

        await service.ScanAsync(CancellationToken.None);

        var census = CensusAlerts(alerts).ShouldHaveSingleItem();
        census.Severity.ShouldBe(AlertSeverity.Warning);
        census.Detail.ShouldNotBeNull();
        census.Detail.ShouldContain("unclaimed by the database: 12");
        census.Detail.ShouldContain("NOTHING WAS KILLED");
        census.DedupKey.ShouldBe($"reconciler:{AgentIncidentKind.PtyHostCensusDiverged}");
    }

    /// <summary>
    /// The measured number (46) must land on the severity that reaches a human. CARD-0101's whole
    /// cascade was Warning-only because Critical was reserved for channel-bound agents, and a
    /// delegate agent is never channel-bound — eleven agent-hours of it were visible to anyone
    /// querying the database and to nobody else.
    /// </summary>
    [Test]
    public async Task Census_alert_is_critical_past_the_hard_ceiling()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, RunnerWithUnclaimed(46), new MockEventBus(), alerts,
            census: Census(ptyHosts: 46, claude: 46));

        await service.ScanAsync(CancellationToken.None);

        CensusAlerts(alerts).ShouldHaveSingleItem().Severity.ShouldBe(AlertSeverity.Critical);
    }

    /// <summary>
    /// The arm that catches what CARD-0102 actually was — and the reason the card's own proposed
    /// remedy would not have helped. 39 hosts alive, ZERO runner sessions, so the unclaimed arm sees
    /// nothing at all: those hosts were holding interactive <c>cmd.exe</c> children that never exit,
    /// which no <c>PtyHostLingerHours</c> can collect (the linger clock starts after the child
    /// exits). Without this arm the whole leak is invisible.
    /// </summary>
    [Test]
    public async Task Census_alert_fires_on_pty_host_surplus_with_no_unclaimed_sessions()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, new FakeRunnerClient { Sessions = [] }, new MockEventBus(), alerts,
            census: Census(ptyHosts: 39, claude: 0));

        await service.ScanAsync(CancellationToken.None);

        var census = CensusAlerts(alerts).ShouldHaveSingleItem();
        census.Severity.ShouldBe(AlertSeverity.Critical, "39 stray hosts is past the hard ceiling");
        census.Detail.ShouldNotBeNull();
        census.Detail.ShouldContain("Live Antiphon.PtyHost processes: 39");
        census.Detail.ShouldContain("surplus over sessions with a live child: 39");
    }

    /// <summary>
    /// A host whose reported child pid is NOT in the process table does not count as a session with
    /// a live agent child. Trusting the runner's own report would count a session whose child died
    /// while the host stands on — which is half of what this pass exists to find.
    /// </summary>
    [Test]
    public async Task A_runner_session_whose_child_pid_is_dead_does_not_offset_the_surplus()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        // Ten runner sessions, each naming a child pid — but the process table knows none of them.
        var runner = RunnerWithUnclaimed(10, pidBase: 900_000);
        var service = BuildService(
            db, runner, new MockEventBus(), alerts,
            census: Census(ptyHosts: 10, claude: 0, livePids: new HashSet<int>()));

        await service.ScanAsync(CancellationToken.None);

        var census = CensusAlerts(alerts).ShouldHaveSingleItem();
        census.Detail.ShouldNotBeNull();
        census.Detail.ShouldContain("with a live agent child: 0");
        census.Detail.ShouldContain("surplus over sessions with a live child: 10");
    }

    /// <summary>
    /// THE constraint, inherited from CARD-0056 and not negotiable: unclaimed never implies kill.
    /// The false positive that created that card fired on a perfectly healthy session, and the
    /// session nobody claimed was the operator's own live conversation — a pass that resolved the
    /// divergence by killing would have killed it mid-sentence.
    /// </summary>
    [Test]
    public async Task The_census_alert_never_kills_anything()
    {
        await using var db = CreateContext();
        var runner = RunnerWithUnclaimed(60);
        var before = runner.Sessions.Select(s => s.SessionId).ToList();
        var service = BuildService(
            db, runner, new MockEventBus(), new RecordingAlertService(),
            census: Census(ptyHosts: 200, claude: 0));

        var corrections = await service.ScanAsync(CancellationToken.None);

        runner.Killed.ShouldBeEmpty(
            "the census REPORTS. It must never reap — CARD-0056's constraint outranks every "
            + "threshold in this file.");
        corrections.ShouldBe(0, "and it corrects nothing: there is nothing here it is entitled to change");

        await using var verify = CreateContext();
        var stillAbsent = await verify.AgentSessions.CountAsync(s => before.Contains(s.Id));
        stillAbsent.ShouldBe(0, "it must not invent rows for them either");
    }

    /// <summary>
    /// §6.5 of the coverage plan, as a test: CARD-0101's refusal fault fired 37 identical Warnings
    /// over three hours, nobody acted, and then the stream went quiet while the fault kept running.
    /// At a 15-second sweep an ungated alert writes 240 rows an hour and means exactly as much.
    /// </summary>
    [Test]
    public async Task The_census_alert_does_not_storm_while_the_condition_holds()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, RunnerWithUnclaimed(12), new MockEventBus(), alerts,
            census: Census(ptyHosts: 12, claude: 12));

        await service.ScanAsync(CancellationToken.None);
        await service.ScanAsync(CancellationToken.None);
        await service.ScanAsync(CancellationToken.None);

        CensusAlerts(alerts).Count.ShouldBe(1, "three sweeps, one alert — the window is 60 minutes");
    }

    /// <summary>
    /// And the other half of that rule, without which the gate is just a mute button: an ESCALATION
    /// goes out immediately. A Warning that has become Critical is news however recently the
    /// Warning was sent.
    /// </summary>
    [Test]
    public async Task An_escalation_to_critical_bypasses_the_repeat_window()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var state = new PtyHostCensusAlertState();

        var warning = BuildService(
            db, RunnerWithUnclaimed(12), new MockEventBus(), alerts,
            census: Census(ptyHosts: 12, claude: 12), censusAlerts: state);
        await warning.ScanAsync(CancellationToken.None);

        var critical = BuildService(
            db, RunnerWithUnclaimed(46), new MockEventBus(), alerts,
            census: Census(ptyHosts: 46, claude: 46), censusAlerts: state);
        await critical.ScanAsync(CancellationToken.None);

        var raised = CensusAlerts(alerts);
        raised.Count.ShouldBe(2, "the escalation must not wait out a window it did not cause");
        raised[0].Severity.ShouldBe(AlertSeverity.Warning);
        raised[1].Severity.ShouldBe(AlertSeverity.Critical);
    }

    /// <summary>Below both thresholds is the normal state of the world and says nothing.</summary>
    [Test]
    public async Task A_census_within_the_thresholds_is_silent()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var runner = RunnerWithUnclaimed(3);
        var live = runner.Sessions.Select(s => s.Pid!.Value).ToHashSet();
        var service = BuildService(
            db, runner, new MockEventBus(), alerts,
            // Three hosts for three live children, plus two lingering after an exit: the system working.
            census: Census(ptyHosts: 5, claude: 3, livePids: live));

        await service.ScanAsync(CancellationToken.None);

        CensusAlerts(alerts).ShouldBeEmpty();
    }

    /// <summary>
    /// A probe that could not read the process table reports "unavailable", never a surplus of
    /// zero. Same rule that makes an unreachable runner mean nothing rather than "no sessions
    /// exist": "I could not look" must never be reported as "nothing is there".
    /// </summary>
    [Test]
    public async Task An_unavailable_process_census_suppresses_the_surplus_arm()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, new FakeRunnerClient { Sessions = [] }, new MockEventBus(), alerts,
            census: new StatedCensusProbe(PtyHostCensus.Unavailable));

        await service.ScanAsync(CancellationToken.None);

        CensusAlerts(alerts).ShouldBeEmpty("no census means no surplus verdict, not a surplus of zero");
    }

    /// <summary>...and when the OTHER arm fires anyway, the detail says the census was unavailable
    /// rather than quietly reporting numbers it does not have.</summary>
    [Test]
    public async Task An_unavailable_process_census_says_so_in_the_detail()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, RunnerWithUnclaimed(12), new MockEventBus(), alerts,
            census: new StatedCensusProbe(PtyHostCensus.Unavailable));

        await service.ScanAsync(CancellationToken.None);

        var census = CensusAlerts(alerts).ShouldHaveSingleItem();
        census.Detail.ShouldNotBeNull();
        census.Detail.ShouldContain("Process census UNAVAILABLE");
        census.Detail.ShouldNotContain("Live Antiphon.PtyHost processes:");
    }

    /// <summary>Off means off: the numbers are still collected, nothing is said about them.</summary>
    [Test]
    public async Task The_census_alert_can_be_turned_off()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var service = BuildService(
            db, RunnerWithUnclaimed(60), new MockEventBus(), alerts,
            census: Census(ptyHosts: 200, claude: 0), censusAlertEnabled: false);

        await service.ScanAsync(CancellationToken.None);

        CensusAlerts(alerts).ShouldBeEmpty();
    }

    // ---- census helpers --------------------------------------------------------------------------

    /// <summary>
    /// Census alerts only. The sweep is global and raises other things (orphans, reachability); an
    /// unfiltered assertion here would be asserting about the rest of the database's state too.
    /// </summary>
    private static IReadOnlyList<AlertRaise> CensusAlerts(RecordingAlertService alerts) =>
        [.. alerts.Raised.Where(a => a.DedupKey == $"reconciler:{AgentIncidentKind.PtyHostCensusDiverged}")];

    /// <summary>
    /// A runner serving <paramref name="count"/> Running sessions with fresh GUIDs — so by
    /// construction the database has no row for any of them, whatever else is in it.
    /// </summary>
    private static FakeRunnerClient RunnerWithUnclaimed(int count, int pidBase = 5_000) => new()
    {
        Sessions =
        [
            .. Enumerable.Range(0, count).Select(i => new SessionRunnerSessionDto(
                Guid.NewGuid(), Pid: pidBase + i, StartedAt: DateTime.UtcNow.AddHours(-3),
                Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                LastSequence: 10, HostPid: pidBase + 10_000 + i))
        ]
    };

    /// <summary>
    /// A stated process table. <paramref name="livePids"/> null means "every pid this test's runner
    /// could name is alive" — the ordinary case, where the interesting number is the host count.
    /// </summary>
    private static StatedCensusProbe Census(int ptyHosts, int claude, IReadOnlySet<int>? livePids = null) =>
        new(new PtyHostCensus(
            ptyHosts, claude, livePids ?? Enumerable.Range(0, 100_000).ToHashSet()));

    private sealed class StatedCensusProbe(PtyHostCensus census) : IPtyHostCensusProbe
    {
        public PtyHostCensus Take() => census;
    }
}
