using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingContinuityRecoveryTests
{
    [Test]
    [Arguments("first")]
    [Arguments("missing")]
    [Arguments("malformed")]
    [Arguments("legacy-pointer-lost")]
    [Arguments("incompatible")]
    public async Task Default_start_distinguishes_first_launch_from_unavailable_prior_identity(string shape)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync(legacy: true);
        await using (var db = f.Db())
        {
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            if (shape == "first")
            {
                await db.AgentIncidents.Where(i => i.AgentId == f.Agent.Id).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == f.A.Id || s.Id == f.B.Id).ExecuteDeleteAsync();
                agent.PersistentSessionId = null;
            }
            if (shape == "missing") agent.PersistentSessionId = Guid.NewGuid().ToString("D");
            if (shape == "malformed") agent.PersistentSessionId = "invalid-current-pointer";
            if (shape == "legacy-pointer-lost")
            {
                agent.PersistentSessionId = null;
                (await db.AgentSessions.FindAsync(f.B.Id))!.StandingAgentId = null;
            }
            if (shape == "incompatible") (await db.AgentSessions.FindAsync(f.B.Id))!.Cwd = Path.Combine(f.Root, "different");
            await db.SaveChangesAsync();
        }
        if (shape == "first")
        {
            var accepted = await f.StartAsync(new()); await f.IdleAsync();
            adapter.Started.ShouldBeTrue(); adapter.StartedArgs.ShouldContain("--session-id");
            adapter.StartedArgs.ShouldNotContain("--resume");
            adapter.StartedSessionId.ShouldBe(Guid.Parse(accepted.PersistentSessionId!));
        }
        else
        {
            (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new()))).Code.ShouldBe(StandingContinuityState.HeldCode);
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            (await verify.AgentSessions.CountAsync(s => s.Id == f.A.Id || s.Id == f.B.Id)).ShouldBe(2);
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
        }
    }

    [Test]
    [Arguments("retry")]
    [Arguments("selection")]
    [Arguments("fresh")]
    public async Task Held_retry_selection_and_fresh_have_separate_accepted_decisions(string decision)
    {
        var first = new FakeAgentProtocolAdapter { ReadyResult = decision != "fresh" };
        var resumed = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(first, resumed);
        await f.SeedAsync(held: true);
        var request = decision == "fresh" ? new StartAgentRequest(Fresh: true)
            : decision == "retry" ? new(RetryContinuity: true) : new(ResumeSessionId: f.A.Id);
        var accepted = await f.StartAsync(request); await f.IdleAsync();
        var id = Guid.Parse(accepted.PersistentSessionId!);
        await using (var verify = f.Db())
        {
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldBeNull();
            var kind = decision == "fresh" ? AgentIncidentKind.StandingFreshSelected : AgentIncidentKind.StandingResumeSelected;
            var audit = await verify.AgentIncidents.SingleAsync(i => i.AgentId == f.Agent.Id && i.Kind == kind);
            audit.Message.ShouldContain(decision == "fresh" ? "explicit fresh conversation" : decision == "retry" ? "repaired-target retry" : "resume selection");
            (await verify.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
            (await verify.AgentSessions.FindAsync(f.B.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
        }
        if (decision == "fresh")
        {
            id.ShouldNotBe(f.A.Id); id.ShouldNotBe(f.B.Id);
            first.StartedArgs.ShouldContain("--session-id");
            await f.StartAsync(new()); await f.IdleAsync();
            resumed.StartedSessionId.ShouldBe(id); resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedArgs.ShouldNotContain("--session-id");
            await using var verify = f.Db();
            (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(3);
        }
        else
        {
            id.ShouldBe(decision == "retry" ? f.B.Id : f.A.Id);
            first.StartedArgs.ShouldContain("--resume"); first.StartedArgs.ShouldNotContain("--session-id");
        }
    }

    [Test]
    public async Task Missing_native_target_holds_after_one_resume_without_create()
    {
        var missing = new FakeAgentProtocolAdapter { ReadyResult = false,
            StartupOutput = "No conversation found with session ID: missing" };
        var forbiddenFresh = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(missing, forbiddenFresh);
        await f.SeedAsync();
        await f.StartAsync(new(ResumeSessionId: f.A.Id));
        await f.IdleAsync();
        missing.StartedArgs.ShouldContain("--resume");
        missing.Killed.ShouldBeTrue(); missing.Disposed.ShouldBeTrue();
        forbiddenFresh.Started.ShouldBeFalse();
        await using var verify = f.Db();
        var state = await verify.AgentSupervisionStates.FindAsync(f.Agent.Id);
        state!.ContinuityReason.ShouldBe(StandingContinuityReason.NativeSessionMissing);
        state.ContinuitySessionId.ShouldBe(f.A.Id);
        (await verify.AgentSessions.FindAsync(f.A.Id))!.RestartFailureKind.ShouldBe(RestartFailureKind.ContinuityUnavailable);
        (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(2);
        (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new()))).Code.ShouldBe(StandingContinuityState.HeldCode);
    }

    [Test]
    public async Task Infrastructure_failure_with_stale_missing_text_does_not_hold_continuity()
    {
        var adapter = new FakeAgentProtocolAdapter { ThrowOnStart = new Exception("No conversation found with session ID",
            new PostgresException("starting", "FATAL", "FATAL", "57P03")) };
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        await f.StartAsync(new(ResumeSessionId: f.A.Id));
        await f.IdleAsync();
        await using var verify = f.Db();
        var session = (await verify.AgentSessions.FindAsync(f.A.Id))!;
        session.RestartFailureKind.ShouldBe(RestartFailureKind.Infrastructure);
        session.InteractiveLaunchCompletedAt.ShouldBeNull();
        (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldBeNull();
        adapter.Killed.ShouldBeTrue(); adapter.Disposed.ShouldBeTrue();
    }
}
