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
