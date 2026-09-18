using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0511 S3. A standing agent whose resume is refused because the session runner is an older
/// build holds instead of climbing the 1.4 h backoff ladder, and the supervisor releases it the
/// moment a DIFFERENT runner identity answers. <c>[NotInParallel]</c> like every other supervision
/// suite: the supervisor sweeps every always-on agent in the shared database.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class StandingRunnerBuildHoldTests
{
    /// <summary>
    /// V-511-8 / G-511-11, G-511-13, PC-511-13. The 09-13 outcome, corrected: the refusal is
    /// recorded once as Critical, the hold is taken on the identity it was refused against, and
    /// nothing at all is charged or scheduled.
    /// </summary>
    [Test]
    public async Task C511_V8_Refused_resume_holds_without_charging()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            var (h, agent, id, _) = await HeldOnAAsync(root);
            await using var harness = h;
            await using var verify = AgentSupervisionTests.CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.RunnerBuildHeldAt.ShouldNotBeNull();
            state.RunnerBuildHeldIdentity.ShouldBe(RunnerIdentity.Describe(A));
            state.RunnerBuildHoldEvidence.ShouldBe(Message(A));
            state.NextRestartAt.ShouldBeNull();
            state.RestartBackoffFailures.ShouldBe(1);
            state.ConsecutiveFailures.ShouldBe(1);

            var session = (await verify.AgentSessions.FindAsync(id))!;
            session.Status.ShouldBe(SessionStatus.Failed);
            session.RestartFailureKind.ShouldBe(RestartFailureKind.RunnerBuildStale);
            session.FailureReason.ShouldBe(Message(A));

            var stale = await verify.AgentIncidents
                .Where(i => i.SessionId == id && i.Kind == AgentIncidentKind.RunnerBuildStale)
                .ToListAsync();
            stale.Count.ShouldBe(1);
            stale[0].Severity.ShouldBe(AlertSeverity.Critical);
            stale[0].AgentId.ShouldBe(agent.Id);
            stale[0].FailureReason.ShouldBe(Message(A));

            (await CountAsync(verify, agent.Id, AgentIncidentKind.RestartScheduled)).ShouldBe(1);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-8b / G-511-12, G-511-14, PC-511-14. The recovery path: an outcome that says the runner
    /// was stale but carries no hold (a process death between the two commits, or an older server)
    /// is held by the schedule branch on "unknown", which then releases on any answered probe.
    /// </summary>
    [Test]
    public async Task C511_V8b_Dead_stale_row_without_a_hold_is_held_by_the_schedule_branch_and_released_by_any_answer()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(
                root, [new FakeAgentProtocolAdapter(), resumed], definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                session.RestartFailureKind = RestartFailureKind.RunnerBuildStale;
                session.FailureReason = Message(A);
                session.EndedAt = h.Clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync();
            }

            h.Runner.CapabilitiesOverride = _ => Task.FromResult<RunnerCapabilitiesDto?>(null);
            await h.Supervisor().TickAsync(default);

            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RunnerBuildHeldAt.ShouldNotBeNull();
                state.RunnerBuildHeldIdentity.ShouldBe("unknown");
                state.RunnerBuildHoldEvidence.ShouldBe(Message(A));
                state.NextRestartAt.ShouldBeNull();
                state.RestartBackoffFailures.ShouldBe(0);
                state.ConsecutiveFailures.ShouldBe(0);
                state.LastObservedRestartSessionId.ShouldBe(id);
                (await CountAsync(db, agent.Id, AgentIncidentKind.RestartScheduled)).ShouldBe(0);
                (await CountAsync(db, agent.Id, AgentIncidentKind.Crash)).ShouldBe(0);
            }

            h.Runner.CapabilitiesOverride = null;
            h.Runner.Build = A;
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RunnerBuildHeldAt.ShouldBeNull();
                (await CountAsync(db, agent.Id, AgentIncidentKind.RunnerBuildReplaced)).ShouldBe(1);
            }

            resumed.Started.ShouldBeTrue();
            resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedSessionId.ShouldBe(id);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-9 / G-511-15, G-511-19, PC-511-15, PC-511-19. Held means held: no attempt over half an
    /// hour of ticks, not even with a due NextRestartAt, and exactly one identity probe per tick.
    /// </summary>
    [Test]
    [Arguments("idle")]
    [Arguments("due")]
    public async Task C511_V9_Held_agent_is_not_scheduled_or_attempted(string shape)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var unused = new FakeAgentProtocolAdapter();
        try
        {
            var (h, agent, _, _) = await HeldOnAAsync(root, unused);
            await using var harness = h;

            if (shape == "due")
            {
                await using var arm = AgentSupervisionTests.CreateContext();
                var state = (await arm.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await arm.SaveChangesAsync();
            }

            var calls = h.Runner.CapabilitiesCalls;
            int incidents;
            DateTime? due;
            await using (var before = AgentSupervisionTests.CreateContext())
            {
                incidents = await before.AgentIncidents.CountAsync(i => i.AgentId == agent.Id);
                due = (await before.AgentSupervisionStates.FindAsync(agent.Id))!.NextRestartAt;
            }

            await h.Supervisor().TickAsync(default);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            await h.Supervisor().TickAsync(default);
            h.Clock.Advance(TimeSpan.FromMinutes(30));
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            unused.Started.ShouldBeFalse();
            h.Runner.CapabilitiesCalls.ShouldBe(calls + 3, "one bounded identity probe per tick");
            await using var verify = AgentSupervisionTests.CreateContext();
            var held = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            held.RunnerBuildHeldAt.ShouldNotBeNull();
            held.RunnerBuildHeldIdentity.ShouldBe(RunnerIdentity.Describe(A));
            held.RunnerBuildHoldEvidence.ShouldBe(Message(A));
            held.NextRestartAt.ShouldBe(due);
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == agent.Id)).ShouldBe(incidents);
            (await CountAsync(verify, agent.Id, AgentIncidentKind.RestartScheduled)).ShouldBe(1);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-10 / G-511-17, G-511-20, PC-511-17, PC-511-20. The whole point of the hold: the
    /// operator rebuilds, and the very next tick releases AND retries — no waiting out the ladder.
    /// </summary>
    [Test]
    public async Task C511_V10_Runner_replacement_releases_and_retries_at_once()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var resumed = new FakeAgentProtocolAdapter();
        try
        {
            var (h, agent, id, _) = await HeldOnAAsync(root, resumed);
            await using var harness = h;

            h.Runner.Build = B;
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            await using var verify = AgentSupervisionTests.CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.RunnerBuildHeldAt.ShouldBeNull();
            state.RunnerBuildHeldIdentity.ShouldBeNull();
            state.RunnerBuildHoldEvidence.ShouldBeNull();
            state.RestartBackoffFailures.ShouldBe(1);
            state.ConsecutiveFailures.ShouldBe(1);

            var replaced = await verify.AgentIncidents
                .Where(i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RunnerBuildReplaced)
                .ToListAsync();
            replaced.Count.ShouldBe(1);
            replaced[0].Severity.ShouldBe(AlertSeverity.Info);
            replaced[0].Message.ShouldContain("bbbbbbb");
            replaced[0].Message.ShouldContain("retrying");
            (await CountAsync(verify, agent.Id, AgentIncidentKind.RestartScheduled)).ShouldBe(1);

            resumed.Started.ShouldBeTrue();
            resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedArgs.ShouldNotContain("--session-id");
            resumed.StartedSessionId.ShouldBe(id);
            (await verify.AgentSessions.FindAsync(id))!.Status.ShouldBe(SessionStatus.Running);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-11 / G-511-18, PC-511-18. "I could not find out" is never evidence of a replacement:
    /// an unreachable identity probe keeps the hold and writes nothing.
    /// </summary>
    [Test]
    public async Task C511_V11_Unreachable_identity_probe_keeps_the_hold()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var unused = new FakeAgentProtocolAdapter();
        try
        {
            var (h, agent, _, _) = await HeldOnAAsync(root, unused);
            await using var harness = h;
            int incidents;
            await using (var before = AgentSupervisionTests.CreateContext())
                incidents = await before.AgentIncidents.CountAsync(i => i.AgentId == agent.Id);

            h.Runner.CapabilitiesOverride = _ => Task.FromResult<RunnerCapabilitiesDto?>(null);
            await h.Supervisor().TickAsync(default);
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            unused.Started.ShouldBeFalse();
            await using var verify = AgentSupervisionTests.CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.RunnerBuildHeldAt.ShouldNotBeNull();
            state.RunnerBuildHeldIdentity.ShouldBe(RunnerIdentity.Describe(A));
            (await CountAsync(verify, agent.Id, AgentIncidentKind.RunnerBuildReplaced)).ShouldBe(0);
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == agent.Id)).ShouldBe(incidents);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-12 / G-511-22a, PC-511-22a. A rebuild that is still not the right build: the one
    /// released attempt re-holds on the NEW identity with its own row, and still charges nothing.
    /// </summary>
    [Test]
    public async Task C511_V12_Second_stale_build_reholds_with_a_second_incident()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var refusedAgain = new FakeAgentProtocolAdapter { ThrowOnStart = Refusal(C) };
        var unused = new FakeAgentProtocolAdapter();
        try
        {
            var (h, agent, id, _) = await HeldOnAAsync(root, refusedAgain, unused);
            await using var harness = h;

            h.Runner.Build = C;
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RunnerBuildHeldIdentity.ShouldBe(RunnerIdentity.Describe(C));
                state.RunnerBuildHoldEvidence.ShouldBe(Message(C));
                state.RestartBackoffFailures.ShouldBe(1);
                state.ConsecutiveFailures.ShouldBe(1);

                var stale = await verify.AgentIncidents
                    .Where(i => i.SessionId == id && i.Kind == AgentIncidentKind.RunnerBuildStale)
                    .Select(i => i.FailureReason)
                    .ToListAsync();
                stale.Count.ShouldBe(2);
                stale.ShouldContain(Message(A));
                stale.ShouldContain(Message(C));
                (await CountAsync(verify, agent.Id, AgentIncidentKind.RunnerBuildReplaced)).ShouldBe(1);
                (await CountAsync(verify, agent.Id, AgentIncidentKind.RestartScheduled)).ShouldBe(1);
            }

            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            unused.Started.ShouldBeFalse();
            await using var settled = AgentSupervisionTests.CreateContext();
            var after = (await settled.AgentSupervisionStates.FindAsync(agent.Id))!;
            after.RunnerBuildHeldIdentity.ShouldBe(RunnerIdentity.Describe(C));
            (await CountAsync(settled, agent.Id, AgentIncidentKind.RunnerBuildReplaced)).ShouldBe(1);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-13 / G-511-16, PC-511-16. A manual Start under this hold is NEVER refused (no
    /// HeldCode — that belongs to the continuity hold, which asks for a decision this one does
    /// not). It clears the hold before queueing, and the queued launch re-decides on a fresh probe.
    /// </summary>
    [Test]
    [Arguments("runs")]
    [Arguments("reholds")]
    public async Task C511_V13_Manual_start_clears_the_hold(string outcome)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var next = outcome == "runs"
            ? new FakeAgentProtocolAdapter()
            : new FakeAgentProtocolAdapter { ThrowOnStart = Refusal(C) };
        try
        {
            var (h, agent, id, _) = await HeldOnAAsync(root, next);
            await using var harness = h;

            // No ConflictException: this hold has no HeldCode. (A fresh scope, as every real HTTP
            // request gets: the harness's long-lived Control scope still tracks the session as
            // Starting after the launch worker's own scope failed it.)
            await ManualStartAsync(h, agent.Id);

            await using (var immediate = AgentSupervisionTests.CreateContext())
                (await immediate.AgentSupervisionStates.FindAsync(agent.Id))!.RunnerBuildHeldAt.ShouldBeNull();

            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await using var verify = AgentSupervisionTests.CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            if (outcome == "runs")
            {
                next.Started.ShouldBeTrue();
                next.StartedArgs.ShouldContain("--resume");
                next.StartedSessionId.ShouldBe(id);
                state.RunnerBuildHeldAt.ShouldBeNull();
                (await verify.AgentSessions.FindAsync(id))!.Status.ShouldBe(SessionStatus.Running);
            }
            else
            {
                state.RunnerBuildHeldIdentity.ShouldBe(RunnerIdentity.Describe(C));
                state.RunnerBuildHoldEvidence.ShouldBe(Message(C));
                state.RestartBackoffFailures.ShouldBe(1);
                state.ConsecutiveFailures.ShouldBe(1);
                (await verify.AgentIncidents.CountAsync(
                    i => i.SessionId == id && i.Kind == AgentIncidentKind.RunnerBuildStale)).ShouldBe(2);
            }
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-15 / G-511-21, G-511-22b, PC-511-21, PC-511-22b. The hole the card named: Kind-29 was
    /// written only on the card-launch path, so the interactive/standing refusal left the DB with
    /// zero rows. The known agent id is passed because a standing session has no AgentTask to
    /// resolve ownership through.
    /// </summary>
    [Test]
    public async Task C511_V15_Interactive_mismatch_records_kind_29_once_per_message_for_any_agent()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(
                root,
                [new FakeAgentProtocolAdapter { ThrowOnStart = Refusal(A) },
                    new FakeAgentProtocolAdapter { ThrowOnStart = Refusal(A) }],
                definitionKind: "ClaudeCode");
            var workspace = Path.Combine(root, $"agent-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workspace);
            var agent = await h.Scope.ServiceProvider.GetRequiredService<AgentService>()
                .CreateAsync(new CreateAgentRequest("Interactive", workspace), default);

            var accepted = await ManualStartAsync(h, agent.Id);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                var session = (await verify.AgentSessions.FindAsync(id))!;
                session.Status.ShouldBe(SessionStatus.Failed);
                session.RestartFailureKind.ShouldBe(RestartFailureKind.RunnerBuildStale);
                session.FailureReason.ShouldBe(Message(A));

                var stale = await verify.AgentIncidents
                    .Where(i => i.SessionId == id && i.Kind == AgentIncidentKind.RunnerBuildStale)
                    .ToListAsync();
                stale.Count.ShouldBe(1);
                stale[0].AgentId.ShouldBe(agent.Id);
                stale[0].Severity.ShouldBe(AlertSeverity.Critical);
                stale[0].FailureReason.ShouldBe(Message(A));
            }

            // Same build, same message, same session: a retry is not a new incident.
            await ManualStartAsync(h, agent.Id);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            await using var settled = AgentSupervisionTests.CreateContext();
            (await settled.AgentIncidents.CountAsync(
                i => i.SessionId == id && i.Kind == AgentIncidentKind.RunnerBuildStale)).ShouldBe(1);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>V-511-16 / G-511-23, PC-511-23. The hold is visible to an operator.</summary>
    [Test]
    public async Task C511_V16_Hold_is_visible_on_the_agent_dto()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            var (h, agent, _, _) = await HeldOnAAsync(root);
            await using var harness = h;
            using var scope = h.Provider.CreateScope();
            var detail = await scope.ServiceProvider.GetRequiredService<AgentService>()
                .GetByIdAsync(agent.Id, default);

            detail.Supervision.ShouldNotBeNull();
            detail.Supervision!.RunnerBuildHeldAt.ShouldNotBeNull();
            detail.Supervision.RunnerBuildHoldEvidence.ShouldBe(Message(A));
            detail.Supervision.ContinuityHeldAt.ShouldBeNull();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    /// <summary>
    /// V-511-18 / G-511-19. A live session clears a leftover hold silently (no release trail — no
    /// runner was replaced), and an unheld sweep never spends a probe on identity at all.
    /// </summary>
    [Test]
    public async Task C511_V18_Live_session_clears_a_leftover_hold_and_unheld_ticks_never_probe_identity()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(
                root, [new FakeAgentProtocolAdapter()], definitionKind: "ClaudeCode");
            h.Runner.Build = A;
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

            var calls = h.Runner.CapabilitiesCalls;
            await h.Supervisor().TickAsync(default);
            await h.Supervisor().TickAsync(default);
            h.Runner.CapabilitiesCalls.ShouldBe(calls, "an unheld sweep never probes runner identity");

            await using (var arm = AgentSupervisionTests.CreateContext())
            {
                var state = (await arm.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RunnerBuildHeldAt = h.Clock.GetUtcNow().UtcDateTime;
                state.RunnerBuildHeldIdentity = RunnerIdentity.Describe(A);
                state.RunnerBuildHoldEvidence = Message(A);
                await arm.SaveChangesAsync();
            }

            await h.Supervisor().TickAsync(default);

            h.Runner.CapabilitiesCalls.ShouldBe(calls + 1);
            await using var verify = AgentSupervisionTests.CreateContext();
            var cleared = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            cleared.RunnerBuildHeldAt.ShouldBeNull();
            cleared.RunnerBuildHeldIdentity.ShouldBeNull();
            cleared.RunnerBuildHoldEvidence.ShouldBeNull();
            (await CountAsync(verify, agent.Id, AgentIncidentKind.RunnerBuildReplaced)).ShouldBe(0);
            (await verify.AgentSessions.FindAsync(id))!.Status.ShouldBe(SessionStatus.Running);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    // ---- fixtures ------------------------------------------------------------------------------

    private static RunnerBuildDto Build(char fill, int hour, int minute) => new(
        $"1.0.0+{new string(fill, 40)}", new string(fill, 40), DateTime.UnixEpoch,
        new DateTime(2026, 9, 13, hour, minute, 0, DateTimeKind.Utc));

    private static readonly RunnerBuildDto A = Build('a', 9, 0);
    private static readonly RunnerBuildDto B = Build('b', 15, 50);
    private static readonly RunnerBuildDto C = Build('c', 16, 30);

    /// <summary>The runner's own refusal sentence, as SessionRunnerHttpClient composes it.</summary>
    private static string Message(RunnerBuildDto build) =>
        $"The session runner does not advertise {RunnerCapabilityFeatures.SessionGenerationV1} and was "
        + $"built from {build.CommitSha![..7]} on {build.AssemblyWriteTimeUtc:yyyy-MM-dd HH:mm} "
        + $"(running since {build.ProcessStartUtc:HH:mm}). "
        + "Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.";

    private static RunnerCapabilityMismatchException Refusal(RunnerBuildDto build) =>
        new(Message(build), build);

    /// <summary>
    /// A manual Start the way a real HTTP request makes one: through a FRESH scope. The harness's
    /// own <c>Harness.Control</c> is long-lived, so after a launch worker (another scope) failed the
    /// session its DbContext still identity-resolves the row as Starting and Start refuses with
    /// <c>standing_resume_target_active</c> — a harness artifact, not the behaviour under test.
    /// </summary>
    private static async Task<AgentDetailDto> ManualStartAsync(
        AgentSupervisionTests.Harness h, Guid agentId)
    {
        await using var scope = h.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentControlService>()
            .StartAsync(agentId, new(), default);
    }

    private static Task<int> CountAsync(
        Antiphon.Server.Infrastructure.Data.AppDbContext db, Guid agentId, AgentIncidentKind kind) =>
        db.AgentIncidents.CountAsync(i => i.AgentId == agentId && i.Kind == kind);

    /// <summary>
    /// Boots a standing agent, lets its session die, lets the ladder schedule one attempt, and has
    /// that attempt refused by runner A — i.e. the exact 09-13 sequence, held on A. Adapters after
    /// the refusal are queued in order for whatever the test does next.
    /// </summary>
    private static async Task<(AgentSupervisionTests.Harness Harness, AgentDetailDto Agent, Guid SessionId, FakeAgentProtocolAdapter Boot)>
        HeldOnAAsync(string root, params FakeAgentProtocolAdapter[] afterRefusal)
    {
        var boot = new FakeAgentProtocolAdapter();
        var adapters = new List<IAgentProtocolAdapter>
        {
            boot, new FakeAgentProtocolAdapter { ThrowOnStart = Refusal(A) },
        };
        adapters.AddRange(afterRefusal);
        var h = AgentSupervisionTests.BuildHarness(root, adapters, definitionKind: "ClaudeCode");
        h.Runner.Build = A;
        var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
        var accepted = await h.Control.StartAsync(agent.Id, new(), default);
        var id = Guid.Parse(accepted.PersistentSessionId!);
        await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);

        await SessionExitObservation.ObserveMatchingAsync(
            h.Provider.GetRequiredService<AgentSessionRuntime>(),
            id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
        await h.Supervisor().TickAsync(default);
        h.Clock.Advance(TimeSpan.FromSeconds(11));
        await h.Supervisor().TickAsync(default);
        await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
        return (h, agent, id, boot);
    }
}
