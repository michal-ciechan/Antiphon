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
            var (agent, session, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            dbSession.HerdrSupervisionFailureKind.ShouldBeNull();
            dbSession.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: 0, ExitReason: AgentExitReason.Unknown, LastSequence: 10,
                        AcceptedStartedAt: startedAt)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Stopped); // exit code 0 → clean stop
            dbSession.ExitCode.ShouldBe(0);
            dbSession.EndedAt.ShouldNotBeNull();
            dbSession.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Unobserved_failed_exit_gets_the_factual_reconciliation_wording()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: 1, ExitReason: AgentExitReason.ProcessExited, LastSequence: 10,
                        AcceptedStartedAt: startedAt)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());
            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Failed);
            dbSession.ExitCode.ShouldBe(1);
            dbSession.FailureReason.ShouldBe(
                "Reconciliation found the runner exited while the database session was still live (ProcessExited, code 1).");
            dbSession.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_specific_failure_reason_already_on_the_live_row_survives_reconciliation()
    {
        var marker = NewMarker();
        const string seeded =
            "codex_command_line_too_long: launcher node.exe codex.js measured 30,001 UTF-16 units against an effective budget of 30,000.";
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: true, failureReason: seeded);
            DateTime stamped;
            await using (var stamp = CreateContext())
            {
                var session = await stamp.AgentSessions.SingleAsync(s => s.Id == sessionId);
                session.StartedAt = DateTime.UtcNow.AddHours(-1);
                stamped = session.StartedAt;
                await stamp.SaveChangesAsync();
            }

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: stamped,
                        Status: "Exited", ExitCode: 1, ExitReason: AgentExitReason.ProcessExited, LastSequence: 0,
                        AcceptedStartedAt: stamped)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());
            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.FailureReason.ShouldBe(seeded);
            dbSession.Status.ShouldBe(SessionStatus.Failed);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Runner_reported_CpuSpinKilled_exit_records_SystemRequest()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: -1, ExitReason: AgentExitReason.CpuSpinKilled, LastSequence: 10,
                        AcceptedStartedAt: startedAt)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Stopped);
            dbSession.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Unobserved_exit_does_not_overwrite_an_OperatorRequest_source()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);
            await using (var stamp = CreateContext())
            {
                var session = await stamp.AgentSessions.SingleAsync(s => s.Id == sessionId);
                session.TerminationSource = SessionTerminationSource.OperatorRequest;
                await stamp.SaveChangesAsync();
            }

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: 0, ExitReason: AgentExitReason.Unknown, LastSequence: 10,
                        AcceptedStartedAt: startedAt)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Stopped);
            dbSession.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: null, ExitReason: AgentExitReason.HerdrPaneClosed,
                        LastSequence: 10, AcceptedStartedAt: startedAt)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Failed, "pane-closed must not slip in as Stopped");
            dbSession.FailureReason.ShouldNotBeNull();
            dbSession.FailureReason.ShouldContain("HerdrPaneClosed");
            dbSession.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.PaneClosed);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    [Arguments(AgentExitReason.HerdrChildGone, HerdrSupervisionFailureKind.ChildGone)]
    [Arguments(AgentExitReason.ProcessExited, HerdrSupervisionFailureKind.NonQualifying)]
    public async Task Runner_reported_typed_exit_stamps_evidence(AgentExitReason reason, HerdrSupervisionFailureKind expected)
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(marker, SessionStatus.Running, staleAgent: true);
            await using var db = CreateContext();
            var runner = new FakeRunnerClient { Sessions = [new SessionRunnerSessionDto(sessionId, 4242, startedAt, "Exited", null, reason, 10, AcceptedStartedAt: startedAt)] };
            await BuildService(db, runner, new MockEventBus()).ScanAsync(CancellationToken.None);
            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).HerdrSupervisionFailureKind.ShouldBe(expected);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task A_reconciler_close_after_the_runtime_stamped_DetectTimeout_keeps_it()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(marker, SessionStatus.Running, staleAgent: true);
            await using var db = CreateContext();
            // The launch catch and runner exit can publish in either order. Keep the row in the
            // reconciliation scan's live set so this test exercises its actual evidence writer.
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.HerdrSupervisionFailureKind, HerdrSupervisionFailureKind.DetectTimeout));
            var runner = new FakeRunnerClient { Sessions = [new SessionRunnerSessionDto(sessionId, 4242, startedAt,
                "Exited", null, AgentExitReason.HerdrPaneClosed, 10, AcceptedStartedAt: startedAt)] };
            await BuildService(db, runner, new MockEventBus()).ScanAsync(CancellationToken.None);
            await using var verify = CreateContext();
            var row = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            row.Status.ShouldBe(SessionStatus.Failed);
            row.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.DetectTimeout);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task Starting_runner_Running_unowned_resumes_the_launch()
    {
        var marker = NewMarker();
        var ownership = new RecordingLaunchOwnership();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);

            await using var db = CreateContext();
            var service = BuildService(db, RunnerRunning(sessionId, startedAt), new MockEventBus(), ownership: ownership);

            await service.ScanAsync(CancellationToken.None);

            ownership.Resumes.ShouldBe([(sessionId, agentId)]);
            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId))
                .Status.ShouldBe(SessionStatus.Starting, "pass 1c changes no row itself");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Starting_runner_Running_owned_is_not_resumed()
    {
        var marker = NewMarker();
        var ownership = new RecordingLaunchOwnership();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);
            ownership.Owned.Add(sessionId);

            await using var db = CreateContext();
            var service = BuildService(db, RunnerRunning(sessionId, startedAt), new MockEventBus(), ownership: ownership);

            await service.ScanAsync(CancellationToken.None);

            ownership.Resumes.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Starting_runner_Pending_is_not_resumed()
    {
        var marker = NewMarker();
        var ownership = new RecordingLaunchOwnership();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddMinutes(-2),
                        Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                        LastSequence: 10, Pending: HerdrPendingReasons.Unreachable)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus(), ownership: ownership);

            await service.ScanAsync(CancellationToken.None);

            ownership.Resumes.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Runner_Exited_snapshot_for_a_superseded_generation_does_not_close_the_row()
    {
        var marker = NewMarker();
        try
        {
            var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-10));
            var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
            var (_, sessionId, _) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generationB));
            }

            await using var scan = CreateContext();
            var alerts = new RecordingAlertService();
            var eventBus = new MockEventBus();
            var service = BuildService(scan, new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 1, StartedAt: generationA, Status: "Exited",
                        ExitCode: 1, ExitReason: AgentExitReason.KilledByRequest, LastSequence: 1,
                        AcceptedStartedAt: generationA)
                ]
            }, eventBus, alerts);
            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
            alerts.For(sessionId).ShouldBeEmpty();
            eventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionExited");
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task Runner_Running_snapshot_for_a_superseded_generation_does_not_resume_an_interrupted_launch()
    {
        var marker = NewMarker();
        var ownership = new RecordingLaunchOwnership();
        try
        {
            var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-10));
            var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow.AddHours(-2));
            var (_, sessionId, _) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generationB));
            }

            await using var scan = CreateContext();
            var service = BuildService(scan, RunnerRunning(sessionId, generationA), new MockEventBus(), ownership: ownership);
            await service.ScanAsync(CancellationToken.None);
            ownership.Resumes.ShouldBeEmpty();
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task A_Failed_row_at_a_newer_generation_is_not_re_adopted_from_older_Running_evidence()
    {
        var marker = NewMarker();
        try
        {
            var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-10));
            var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
            var (_, sessionId, _) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generationB));
            }

            var flapState = new SessionReAdoptionState();
            var runner = RunnerRunning(sessionId, generationA);
            await using var scan = CreateContext();
            var service = BuildService(scan, runner, new MockEventBus(), reAdoptions: flapState);
            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Failed);
            runner.Probed.ShouldBeEmpty();
            flapState.CountFor(sessionId).ShouldBe(0);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task A_stale_list_absence_does_not_close_a_row_accepted_after_the_list()
    {
        var marker = NewMarker();
        try
        {
            var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddHours(-3));
            var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow.AddHours(-2));
            var (_, sessionId, _) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generationA)
                    .SetProperty(s => s.Status, SessionStatus.Starting));
            }

            var refreshed = false;
            var runner = new FakeRunnerClient
            {
                OnList = async ct =>
                {
                    await using var db = CreateContext();
                    await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.StartedAt, generationB)
                        .SetProperty(s => s.Status, SessionStatus.Starting), ct);
                },
                Sessions = [],
                GetOverride = _ =>
                {
                    refreshed = true;
                    return new SessionRunnerSessionDto(
                        sessionId, Pid: 1, StartedAt: generationB, Status: "Running",
                        ExitCode: null, ExitReason: AgentExitReason.Unknown, LastSequence: 0,
                        AcceptedStartedAt: generationB);
                }
            };

            await using var scan = CreateContext();
            var service = BuildService(scan, runner, new MockEventBus());
            await service.ScanAsync(CancellationToken.None);

            refreshed.ShouldBeTrue();
            await using var verify = CreateContext();
            var row = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.StartedAt.ShouldBe(generationB);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task LaunchResumeEnabled_false_does_not_resume()
    {
        var marker = NewMarker();
        var ownership = new RecordingLaunchOwnership();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);

            await using var db = CreateContext();
            var service = BuildService(
                db, RunnerRunning(sessionId, startedAt), new MockEventBus(),
                ownership: ownership, launchResumeEnabled: false);

            await service.ScanAsync(CancellationToken.None);

            ownership.Resumes.ShouldBeEmpty();
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true,
                agentStatus: AgentStatus.Failed,
                failureReason: "No composer evidence appeared for the typed body.");

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            var eventBus = new MockEventBus();
            var flapState = new SessionReAdoptionState();
            var service = BuildService(db, RunnerRunning(sessionId, startedAt), eventBus, alerts, flapState);

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
            flapState.CountFor(sessionId).ShouldBe(1);
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            var flapState = new SessionReAdoptionState();
            var service = BuildService(db, RunnerRunning(sessionId, startedAt), new MockEventBus(), alerts, flapState);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Running, "unclaimed must never imply kill");
            flapState.CountFor(sessionId).ShouldBe(1);
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var runner = RunnerRunning(sessionId, startedAt);
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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var runner = RunnerRunning(sessionId, startedAt, pid: null, hostPid: null);
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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Stopped, staleAgent: true, agentStatus: AgentStatus.Stopped);

            await using var db = CreateContext();
            var runner = RunnerRunning(sessionId, startedAt);
            var service = BuildService(db, runner, new MockEventBus(), new RecordingAlertService());

            await service.ScanAsync(CancellationToken.None);

            runner.Killed.ShouldBe([sessionId]);
            runner.KillGenerationCalls.ShouldBeEmpty();
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
    public async Task Re_adoption_counts_committed_transitions_and_latches_after_the_cap()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            var flapState = new SessionReAdoptionState();
            var alerts = new RecordingAlertService();
            var logs = new List<string>();
            var logger = new ListLogger<SessionReconciliationService>(logs);
            var runner = RunnerRunning(sessionId, startedAt);
            for (var round = 1; round <= 104; round++)
            {
                await using var db = CreateContext();
                var service = BuildService(
                    db, runner, new MockEventBus(), alerts, flapState, logger: logger);
                await service.ScanAsync(CancellationToken.None);

                await using var verify = CreateContext();
                var row = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
                if (round <= 3)
                {
                    row.Status.ShouldBe(SessionStatus.Running, $"round {round} is within the cap");
                    flapState.CountFor(sessionId).ShouldBe(round);
                    alerts.For(sessionId).Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(0);
                    await verify.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, SessionStatus.Failed)
                        .SetProperty(s => s.StartedAt, startedAt)
                        .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                    await verify.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                        .SetProperty(a => a.Status, AgentStatus.Failed));
                }
                else
                {
                    row.Status.ShouldBe(SessionStatus.Failed, $"round {round} stays Failed");
                    flapState.CountFor(sessionId).ShouldBe(3);
                }
            }

            runner.Probed.Count.ShouldBe(4);
            runner.Killed.ShouldBeEmpty();
            await using var final = CreateContext();
            var critical = await final.AgentIncidents
                .Where(i => i.AgentId == agentId && i.Severity == AlertSeverity.Critical)
                .ToListAsync();
            critical.ShouldHaveSingleItem();
            critical[0].FailureReason.ShouldBe("ReAdoptCapReached");
            critical[0].Message.ShouldContain("Re-adopted this session 3 times during this server uptime (cap 3).");
            critical[0].Message.ShouldContain("A further Failed/runner-Running mismatch was observed; automatic re-adoption is stopped.");
            alerts.For(sessionId).Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);
            logs.Count(l => l.Contains("automatic re-adoption is stopped", StringComparison.OrdinalIgnoreCase)
                || l.Contains("A further Failed/runner-Running mismatch")).ShouldBe(1);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Cap_zero_or_negative_allows_no_transition_and_escalates_on_the_first_mismatch(int cap)
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var flapState = new SessionReAdoptionState();
            var alerts = new RecordingAlertService();
            var runner = RunnerRunning(sessionId, startedAt);
            for (var sweep = 1; sweep <= 21; sweep++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState, maxReAdoptions: cap);
                await service.ScanAsync(CancellationToken.None);
                await using var verify = CreateContext();
                (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Failed);
                flapState.CountFor(sessionId).ShouldBe(0);
            }

            runner.Probed.Count.ShouldBe(1);
            await using var final = CreateContext();
            var critical = await final.AgentIncidents
                .Where(i => i.SessionId == sessionId && i.Severity == AlertSeverity.Critical)
                .ToListAsync();
            critical.ShouldHaveSingleItem();
            critical[0].Message.ShouldContain("0 times");
            critical[0].Message.ShouldContain("(cap 0)");
            alerts.For(sessionId).Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task Cap_state_is_per_session_and_a_new_singleton_starts_over()
    {
        var marker = NewMarker();
        try
        {
            var (agent1, s1, started1) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var s2 = Guid.NewGuid();
            await using (var db = CreateContext())
            {
                var now = DateTime.UtcNow.AddHours(-1);
                db.AgentSessions.Add(new AgentSession
                {
                    Id = s2,
                    DefinitionName = "claude",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Failed,
                    Cwd = Path.Combine(Path.GetTempPath(), marker),
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = started1,
                    LastSeenAt = now,
                    EndedAt = DateTime.UtcNow,
                    ExitCode = 1,
                });
                await db.SaveChangesAsync();
            }

            var flapState = new SessionReAdoptionState();
            var alerts = new RecordingAlertService();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(s1, 1, started1, "Running", null, AgentExitReason.Unknown, 10, HostPid: 2, AcceptedStartedAt: started1),
                    new SessionRunnerSessionDto(s2, 1, started1, "Running", null, AgentExitReason.Unknown, 10, HostPid: 3, AcceptedStartedAt: started1),
                ]
            };
            for (var round = 1; round <= 4; round++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);
                if (round <= 3)
                {
                    await using var verify = CreateContext();
                    await verify.AgentSessions.Where(s => s.Id == s1).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, SessionStatus.Failed)
                        .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                    await verify.Agents.Where(a => a.Id == agent1).ExecuteUpdateAsync(u => u
                        .SetProperty(a => a.Status, AgentStatus.Failed));
                }
            }

            flapState.CountFor(s1).ShouldBe(3);
            flapState.CountFor(s2).ShouldBe(1);
            alerts.Raised.Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);

            var fresh = new SessionReAdoptionState();
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == s1).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow));
            }

            await using (var db = CreateContext())
            {
                var service = BuildService(db, runner, new MockEventBus(), new RecordingAlertService(), fresh);
                await service.ScanAsync(CancellationToken.None);
            }

            fresh.CountFor(s1).ShouldBe(1);
            alerts.Raised.Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task A_healthy_sweep_or_same_id_resume_does_not_reset_the_count()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var flapState = new SessionReAdoptionState();
            var runner = RunnerRunning(sessionId, startedAt);
            for (var round = 1; round <= 2; round++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), reAdoptions: flapState);
                await service.ScanAsync(CancellationToken.None);
                await using var verify = CreateContext();
                await verify.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                await verify.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Failed));
            }

            flapState.CountFor(sessionId).ShouldBe(2);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Running)
                    .SetProperty(s => s.EndedAt, (DateTime?)null));
            }

            for (var i = 0; i < 3; i++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), reAdoptions: flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            var generationB = SessionGeneration.Next(startedAt, DateTime.UtcNow);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generationB)
                    .SetProperty(s => s.Status, SessionStatus.Running));
            }

            await using (var db = CreateContext())
            {
                var service = BuildService(db, RunnerRunning(sessionId, generationB), new MockEventBus(), reAdoptions: flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            flapState.CountFor(sessionId).ShouldBe(2);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.StartedAt, startedAt)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Failed));
            }

            await using (var db = CreateContext())
            {
                var service = BuildService(db, runner, new MockEventBus(), reAdoptions: flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            flapState.CountFor(sessionId).ShouldBe(3);
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Failed));
            }

            var alerts = new RecordingAlertService();
            await using (var db = CreateContext())
            {
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            flapState.CountFor(sessionId).ShouldBe(3);
            alerts.For(sessionId).Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task Two_reconcilers_racing_one_Failed_row_commit_exactly_one_transition()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var flapState = new SessionReAdoptionState();
            var barrier = new Barrier(2);
            var runner = RunnerRunning(sessionId, startedAt);
            runner.OnProbe = (_, _) =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                return Task.CompletedTask;
            };

            await using var db1 = CreateContext();
            await using var db2 = CreateContext();
            var alerts = new RecordingAlertService();
            var a = BuildService(db1, runner, new MockEventBus(), alerts, flapState);
            var b = BuildService(db2, runner, new MockEventBus(), alerts, flapState);
            await Task.WhenAll(a.ScanAsync(CancellationToken.None), b.ScanAsync(CancellationToken.None));

            flapState.CountFor(sessionId).ShouldBe(1);
            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
            (await verify.AgentIncidents.CountAsync(i => i.SessionId == sessionId && i.Severity == AlertSeverity.Warning))
                .ShouldBe(1);
            (await verify.AgentIncidents.CountAsync(i => i.SessionId == sessionId && i.Severity == AlertSeverity.Critical))
                .ShouldBe(0);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    [Arguments("probe")]
    [Arguments("save")]
    [Arguments("cancel")]
    [Arguments("superseded")]
    [Arguments("publish-after-commit")]
    public async Task A_failed_or_superseded_transition_never_counts_and_a_post_commit_publish_failure_never_refunds(string shape)
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var flapState = new SessionReAdoptionState();
            var runner = RunnerRunning(sessionId, startedAt);
            var eventBus = new MockEventBus();
            var alerts = new RecordingAlertService();
            using var cts = new CancellationTokenSource();
            AppDbContext scan;
            if (shape == "save")
            {
                var interceptor = new ThrowOnceSaveInterceptor();
                var options = new DbContextOptionsBuilder<AppDbContext>();
                options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                });
                options.AddInterceptors(interceptor);
                scan = new AppDbContext(options.Options);
            }
            else
            {
                scan = CreateContext();
            }

            await using (scan)
            {
                if (shape == "probe")
                    runner.BufferError = new HttpRequestException("pipe gone");
                if (shape == "cancel")
                    runner.OnProbe = (_, _) => throw new OperationCanceledException();
                if (shape == "superseded")
                {
                    var generationB = SessionGeneration.Next(startedAt, DateTime.UtcNow);
                    runner.OnProbe = async (_, ct) =>
                    {
                        await using var db = CreateContext();
                        await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                            .SetProperty(s => s.StartedAt, generationB)
                            .SetProperty(s => s.Status, SessionStatus.Running)
                            .SetProperty(s => s.EndedAt, (DateTime?)null)
                            .SetProperty(s => s.ExitCode, (int?)null), ct);
                    };
                }

                if (shape == "publish-after-commit")
                    eventBus.ThrowOnceOnEvent = "SessionStarted";

                var service = BuildService(scan, runner, eventBus, alerts, flapState);
                if (shape == "cancel")
                    await service.ScanAsync(cts.Token);
                else
                    await service.ScanAsync(CancellationToken.None);
            }

            if (shape is "probe" or "save" or "cancel")
            {
                flapState.CountFor(sessionId).ShouldBe(0);
                await using var verify = CreateContext();
                (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Failed);
                if (shape == "save")
                    (await verify.AgentIncidents.CountAsync(i => i.SessionId == sessionId)).ShouldBe(0);
            }
            else if (shape == "superseded")
            {
                flapState.CountFor(sessionId).ShouldBe(0);
                await using var verify = CreateContext();
                var row = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
                row.Status.ShouldBe(SessionStatus.Running);
                (await verify.AgentIncidents.CountAsync(i => i.SessionId == sessionId)).ShouldBe(0);
            }
            else
            {
                flapState.CountFor(sessionId).ShouldBe(1);
                await using var verify = CreateContext();
                (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
                (await verify.AgentIncidents.CountAsync(i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.SessionReAdopted))
                    .ShouldBe(1);
                alerts.For(sessionId).ShouldNotBeEmpty();
            }
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    [Arguments("incident-throws")]
    [Arguments("alert-throws")]
    [Arguments("ambiguous-save")]
    public async Task Cap_escalation_retries_only_unfinished_work_and_converges_to_one_incident(string shape)
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var flapState = new SessionReAdoptionState();
            var runner = RunnerRunning(sessionId, startedAt);
            for (var round = 1; round <= 3; round++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), reAdoptions: flapState);
                await service.ScanAsync(CancellationToken.None);
                await using var verify = CreateContext();
                await verify.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                await verify.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Failed));
            }

            var alerts = new RecordingAlertService { ThrowOnce = shape == "alert-throws" };
            SaveChangesInterceptor? interceptor = shape switch
            {
                "incident-throws" => new ThrowOnceCapIncidentInterceptor(),
                "ambiguous-save" => new ThrowOnceSaveInterceptor(onSaving: false, onSaved: true),
                _ => null,
            };
            AppDbContext FirstScan()
            {
                if (interceptor is null)
                    return CreateContext();
                var options = new DbContextOptionsBuilder<AppDbContext>();
                options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                });
                options.AddInterceptors(interceptor);
                return new AppDbContext(options.Options);
            }

            await using (var db = FirstScan())
            {
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            await using (var db = CreateContext())
            {
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            await using var final = CreateContext();
            (await final.AgentIncidents.CountAsync(i =>
                i.SessionId == sessionId && i.Severity == AlertSeverity.Critical)).ShouldBe(1);
            alerts.For(sessionId).Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);

            var reportedAlerts = alerts.Raised.Count;
            var reportedIncidents = await final.AgentIncidents.CountAsync(i => i.SessionId == sessionId);
            for (var i = 0; i < 10; i++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);
            }

            alerts.Raised.Count.ShouldBe(reportedAlerts);
            await using var after = CreateContext();
            (await after.AgentIncidents.CountAsync(i => i.SessionId == sessionId)).ShouldBe(reportedIncidents);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task An_unclaimed_capped_session_gets_the_alert_and_no_incident()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);
            var other = Guid.NewGuid();
            await using (var db = CreateContext())
            {
                await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.PersistentSessionId, other.ToString("D")));
            }

            var flapState = new SessionReAdoptionState();
            var alerts = new RecordingAlertService();
            var runner = RunnerRunning(sessionId, startedAt);
            for (var round = 1; round <= 4; round++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState);
                await service.ScanAsync(CancellationToken.None);
                if (round <= 3)
                {
                    await using var verify = CreateContext();
                    await verify.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, SessionStatus.Failed)
                        .SetProperty(s => s.EndedAt, DateTime.UtcNow));
                }
            }

            await using var final = CreateContext();
            (await final.AgentIncidents.CountAsync(i => i.SessionId == sessionId)).ShouldBe(0);
            alerts.For(sessionId).Count(a => a.Severity == AlertSeverity.Critical).ShouldBe(1);
        }
        finally { await CleanupAsync(marker); }
    }

    [Test]
    public async Task A_legacy_runner_DTO_without_a_generation_closes_nothing_re_adopts_nothing_and_alerts_once()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);
            var alerts = new RecordingAlertService();
            var compat = new SessionGenerationCompatState();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, 1, startedAt, "Exited", 1, AgentExitReason.KilledByRequest, 1)
                ]
            };
            for (var i = 0; i < 10; i++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), alerts, generationCompat: compat);
                await service.ScanAsync(CancellationToken.None);
            }

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
            alerts.For(sessionId).Count.ShouldBe(1);

            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow));
            }

            runner.Sessions =
            [
                new SessionRunnerSessionDto(
                    sessionId, 1, startedAt, "Running", null, AgentExitReason.Unknown, 10, HostPid: 2)
            ];
            var flapState = new SessionReAdoptionState();
            for (var i = 0; i < 10; i++)
            {
                await using var db = CreateContext();
                var service = BuildService(db, runner, new MockEventBus(), alerts, flapState, generationCompat: compat);
                await service.ScanAsync(CancellationToken.None);
            }

            await using var failed = CreateContext();
            (await failed.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Failed);
            flapState.CountFor(sessionId).ShouldBe(0);
            alerts.For(sessionId).Count.ShouldBe(1);
        }
        finally { await CleanupAsync(marker); }
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add(formatter(state, exception));
    }

    private sealed class ThrowOnceCapIncidentInterceptor : SaveChangesInterceptor
    {
        private int _threw;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentIncident>().Any(e =>
                    e.State == EntityState.Added
                    && e.Entity.FailureReason == "ReAdoptCapReached")
                && Interlocked.Exchange(ref _threw, 1) == 0)
            {
                throw new InvalidOperationException("ThrowOnceCapIncidentInterceptor");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            var service = BuildService(
                db, RunnerRunning(sessionId, startedAt), new MockEventBus(), alerts, reAdoptEnabled: false);

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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            var (_, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
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

    /// <summary>
    /// CARD-0186 S4: pass 3 re-adopts a Failed herdr row only when the single-session GET's
    /// <c>HerdrVerifiedAtUtc</c> is at least as fresh as this sweep's start. GetBuffer is not
    /// evidence on herdr (the ansi log always answers).
    /// </summary>
    [Test]
    public async Task Failed_herdr_session_with_fresh_HerdrVerifiedAtUtc_is_re_adopted()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true,
                agentStatus: AgentStatus.Failed,
                failureReason: "Process exited (HerdrRestartPresumedDead, code unknown).");

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            var eventBus = new MockEventBus();
            var runner = RunnerRunningHerdr(sessionId, DateTime.UtcNow.AddMinutes(1), startedAt);
            var service = BuildService(db, runner, eventBus, alerts);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Running);
            dbSession.EndedAt.ShouldBeNull();
            dbSession.FailureReason.ShouldBeNull();
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Running);

            var incident = await verify.AgentIncidents.SingleAsync(i => i.SessionId == sessionId);
            incident.Kind.ShouldBe(AgentIncidentKind.SessionReAdopted);
            incident.FailureReason.ShouldBe("SessionReAdopted");
            runner.Probed.ShouldBeEmpty("herdr re-adopt must not use the pty-host buffer probe");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Failed_herdr_session_with_stale_or_absent_HerdrVerifiedAtUtc_is_left_alone(bool stale)
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId, startedAt) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Failed, staleAgent: true, agentStatus: AgentStatus.Failed);

            await using var db = CreateContext();
            var alerts = new RecordingAlertService();
            DateTime? verified = stale ? DateTime.UtcNow.AddHours(-1) : null;
            var runner = RunnerRunningHerdr(sessionId, verified, startedAt);
            var service = BuildService(db, runner, new MockEventBus(), alerts);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status
                .ShouldBe(SessionStatus.Failed);
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Failed);
            alerts.For(sessionId).ShouldContain(a => a.Severity == AlertSeverity.Error);
            var incident = await verify.AgentIncidents.SingleAsync(i => i.SessionId == sessionId);
            incident.FailureReason.ShouldBe("ReAdoptProbeFailed");
            incident.Message.ShouldContain("HerdrVerifiedAtUtc");
            runner.Probed.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// CARD-0186 S4: a Pending herdr session is neither unclaimed nor surplus. The census used to
    /// walk every Running runner session; Pending is Starting-shaped adoption, not a live child.
    /// </summary>
    [Test]
    public async Task Pending_herdr_sessions_are_excluded_from_the_census_unclaimed_count()
    {
        await using var db = CreateContext();
        var alerts = new RecordingAlertService();
        var runner = new FakeRunnerClient
        {
            Sessions =
            [
                .. Enumerable.Range(0, 12).Select(i => new SessionRunnerSessionDto(
                    Guid.NewGuid(), Pid: 5_000 + i, StartedAt: DateTime.UtcNow.AddHours(-3),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, Adopted: true, Backend: SessionBackends.Herdr,
                    Pending: HerdrPendingReasons.Unreachable))
            ]
        };
        var service = BuildService(
            db, runner, new MockEventBus(), alerts,
            census: new StatedCensusProbe(PtyHostCensus.Unavailable));

        await service.ScanAsync(CancellationToken.None);

        CensusAlerts(alerts).ShouldBeEmpty(
            "Pending sessions must not count as unclaimed Running children; an unavailable census "
            + "also suppresses the surplus arm, so this sweep should be silent.");
        runner.Killed.ShouldBeEmpty();
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    internal static SessionReconciliationService BuildService(
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
        int herdrPendingAlertMinutes = 5,
        ILaunchOwnership? ownership = null,
        bool launchResumeEnabled = true,
        int maxReAdoptions = 3,
        ILogger<SessionReconciliationService>? logger = null,
        SessionGenerationCompatState? generationCompat = null) =>
        new(
            db,
            runnerClient,
            eventBus,
            alerts ?? new NoOpAlertService(),
            new RunnerReachabilityState(),
            reAdoptions ?? new SessionReAdoptionState(),
            generationCompat ?? new SessionGenerationCompatState(),
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
                LaunchResumeEnabled = launchResumeEnabled,
                MaxReAdoptionsPerSession = maxReAdoptions,
            }),
            time ?? TimeProvider.System,
            logger ?? NullLogger<SessionReconciliationService>.Instance,
            ownership);

    /// <summary>A runner that reports one session Running, with a real process behind it.</summary>
    private static FakeRunnerClient RunnerRunning(
        Guid sessionId, DateTime? acceptedStartedAt = null, int? pid = 4242, int? hostPid = 4243) =>
        new()
        {
            Sessions =
            [
                new SessionRunnerSessionDto(
                    sessionId, Pid: pid, StartedAt: acceptedStartedAt ?? DateTime.UtcNow.AddHours(-1),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, HostPid: hostPid,
                    AcceptedStartedAt: acceptedStartedAt)
            ]
        };

    /// <summary>
    /// CARD-0186 S4: Running on herdr. <paramref name="verifiedAtUtc"/> is what the single-session
    /// GET returns (pass 3's evidence); the list endpoint stays cheap and is not consulted.
    /// </summary>
    private static FakeRunnerClient RunnerRunningHerdr(Guid sessionId, DateTime? verifiedAtUtc, DateTime? acceptedStartedAt = null) =>
        new()
        {
            Sessions =
            [
                new SessionRunnerSessionDto(
                    sessionId, Pid: 4243, StartedAt: acceptedStartedAt ?? DateTime.UtcNow.AddHours(-1),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, Adopted: true, Backend: SessionBackends.Herdr,
                    HerdrVerifiedAtUtc: verifiedAtUtc, AcceptedStartedAt: acceptedStartedAt)
            ],
            GetOverride = _ => new SessionRunnerSessionDto(
                sessionId, Pid: 4243, StartedAt: acceptedStartedAt ?? DateTime.UtcNow.AddHours(-1),
                Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                LastSequence: 10, Adopted: true, Backend: SessionBackends.Herdr,
                HerdrVerifiedAtUtc: verifiedAtUtc, AcceptedStartedAt: acceptedStartedAt)
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

        public bool ThrowOnce { get; set; }
        private int _threw;

        public Task RaiseAsync(AlertRaise alert, CancellationToken ct)
        {
            if (ThrowOnce && Interlocked.Exchange(ref _threw, 1) == 0)
                throw new InvalidOperationException("RecordingAlertService throw-once");
            lock (_raised)
                _raised.Add(alert);
            return Task.CompletedTask;
        }
    }

    private static string NewMarker() => $"antiphon-reconciliation-tests-{Guid.NewGuid():N}";

    private static async Task<(Guid AgentId, Guid SessionId, DateTime StartedAt)> SeedWorkingAgentWithSessionAsync(
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
        return (agentId, sessionId, startedAt);
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

    private sealed class RecordingLaunchOwnership : ILaunchOwnership
    {
        public HashSet<Guid> Owned { get; } = [];
        public List<(Guid SessionId, Guid AgentId)> Resumes { get; } = [];

        public bool Owns(Guid sessionId) => Owned.Contains(sessionId);

        public void ResumeInterrupted(Guid sessionId, Guid agentId) =>
            Resumes.Add((sessionId, agentId));

        public bool TryRegister(Guid sessionId) => Owned.Add(sessionId);

        public void Unregister(Guid sessionId) => Owned.Remove(sessionId);
    }

    internal sealed class FakeRunnerClient : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];
        public Exception? ListError { get; set; }

        /// <summary>The per-session liveness probe: what it answers, and which sessions asked.</summary>
        public Exception? BufferError { get; set; }
        public List<Guid> Probed { get; } = [];
        public List<Guid> Killed { get; } = [];
        public List<(Guid SessionId, DateTime Expected)> KillGenerationCalls { get; } = [];
        public Func<CancellationToken, Task>? OnList { get; set; }
        public Func<Guid, CancellationToken, Task>? OnProbe { get; set; }

        public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            if (OnList is not null)
                await OnList(ct);
            if (ListError is not null)
                throw ListError;
            return Sessions;
        }

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

        public async Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
        {
            if (OnProbe is not null)
                await OnProbe(sessionId, ct);
            Probed.Add(sessionId);
            if (BufferError is not null)
                throw BufferError;
            return new SessionRunnerBufferDto(sessionId, "> ", 10);
        }

        public Task<RunnerKillGenerationResult> KillGenerationAsync(
            Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
        {
            KillGenerationCalls.Add((sessionId, expectedAcceptedStartedAt));
            return Task.FromResult(new RunnerKillGenerationResult(
                sessionId, false, KillGenerationOutcomes.Mismatch, expectedAcceptedStartedAt));
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

    /// <summary>
    /// CARD-0205, the headline: force the exact 2026-08-25 divergence through the REAL
    /// <see cref="AlertService"/> and real PostgreSQL, and require ROWS — not log lines.
    ///
    /// <para>Every other case in this file records alerts into an in-memory fake, which is why four
    /// days of failed inserts were invisible: nothing had ever asked a column to hold the text. On
    /// the live machine this sweep produced two log lines and zero rows — the orphan alert
    /// overflowed <c>Alerts.Detail</c> with 190 guids, and the census alert behind it, whose own
    /// detail was 617 legal characters, died on the poison row EF had left in the shared change
    /// tracker.</para>
    ///
    /// <para>Scoped by CreatedAt, not by an isolated schema: both dedup keys here are fixed strings
    /// this sweep owns, and the class is globally serial, so the window is enough — and a schema
    /// per test leaves a pooled data source behind for the rest of the assembly run.</para>
    /// </summary>
    [Test]
    public async Task A_forced_divergence_persists_its_alert_rows()
    {
        var since = DateTime.UtcNow;
        try
        {
            await using var db = CreateContext();
            var alerts = new AlertService(
                db, new MockEventBus(), new NullAlertRouter(),
                AlertScopes.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System, NullLogger<AlertService>.Instance);
            var service = BuildService(
                db, RunnerWithUnclaimed(190), new MockEventBus(), alerts,
                census: Census(ptyHosts: 205, claude: 24));

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var rows = await verify.Alerts.Where(a => a.CreatedAt >= since).ToListAsync();

            // The detector CARD-0204's leak was supposed to trip. This is the row that never existed.
            var census = rows
                .Where(a => a.DedupKey == $"reconciler:{AgentIncidentKind.PtyHostCensusDiverged}")
                .ToList()
                .ShouldHaveSingleItem();
            census.Severity.ShouldBe(AlertSeverity.Critical);
            census.Detail.ShouldNotBeNull();
            census.Detail!.ShouldContain("unclaimed by the database: 190");

            // And the alert that was actually oversized, now bounded: the count is exact, the ids
            // are a sample, and the whole thing fits the column it has to live in.
            var orphans = rows
                .Where(a => a.DedupKey == "reconciler:orphans")
                .ToList()
                .ShouldHaveSingleItem();
            orphans.Detail.ShouldNotBeNull();
            orphans.Detail!.ShouldStartWith("190 running session(s) are unknown to the database");
            orphans.Detail.ShouldContain("(and 170 more)");
            orphans.Detail.Length.ShouldBeLessThanOrEqualTo(Alert.DetailMaxLength);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Alerts
                .Where(a => a.CreatedAt >= since
                    && (a.DedupKey == "reconciler:orphans"
                        || a.DedupKey == $"reconciler:{AgentIncidentKind.PtyHostCensusDiverged}"))
                .ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// One provider for the class, over the SHARED connection string: it exists only so
    /// <see cref="AlertService"/>'s out-of-band retry has a context of its own, and a distinct
    /// connection string per test would leave a pooled Npgsql data source behind for the rest of
    /// the assembly run.
    /// </summary>
    private static readonly ServiceProvider AlertScopes = new ServiceCollection()
        .AddDbContext<AppDbContext>(options => options.UseNpgsql(
            TestDbFixture.ConnectionString,
            npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }))
        .BuildServiceProvider();

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
