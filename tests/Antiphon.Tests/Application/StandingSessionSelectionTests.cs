using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingSessionSelectionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Legacy_historical_owner_can_resume_after_pointer_moved(bool executionOnly)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync(legacy: true);
        if (executionOnly)
        {
            await using var db = f.Db();
            await db.AgentIncidents.Where(i => i.SessionId == f.A.Id).ExecuteDeleteAsync();
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, AgentId = f.Agent.Id,
                AgentSessionId = f.A.Id, Title = "Historical execution", Goal = "Synthetic history",
                WorkingDirectory = f.Root, Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var history = await f.Harness.Control.GetSessionsAsync(f.Agent.Id, 25, null, default);
        history.Items.ShouldContain(s => s.Id == f.A.Id && s.OwnershipEvidence == "Legacy");
        await using (var db = f.Db()) (await db.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBeNull();
        await f.StartAsync(new(ResumeSessionId: f.A.Id));
        await f.IdleAsync();
        adapter.Started.ShouldBeTrue();
        adapter.StartedArgs.ShouldContain("--resume");
        adapter.StartedArgs.ShouldContain(f.A.Id.ToString("D"));
        adapter.StartedArgs.ShouldNotContain("--session-id");
        await using var verify = f.Db();
        (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.A.Id.ToString("D"));
        (await verify.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
        (await verify.AgentSessions.FindAsync(f.B.Id)).ShouldNotBeNull();
        (await verify.AgentSessions.FindAsync(f.A.Id))!.InteractiveLaunchCompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Foreign_conflicting_or_unproven_ownership_refuses_without_side_effects()
    {
        foreach (var shape in new[] { "foreign", "conflicting", "unproven" })
        {
            var adapter = new FakeAgentProtocolAdapter();
            await using var f = new StandingRecoveryFixture(adapter);
            await f.SeedAsync(legacy: true, held: true);
            await using (var db = f.Db())
            {
                var other = new Agent { Id = Guid.NewGuid(), Name = "Other", Slug = $"c466-{Guid.NewGuid():N}",
                    WorkingDirectory = f.Root, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                db.Agents.Add(other);
                if (shape == "foreign") (await db.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId = other.Id;
                if (shape == "conflicting") db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = other.Id,
                    SessionId = f.A.Id, Kind = AgentIncidentKind.Crash, Message = "Contradiction", CreatedAt = DateTime.UtcNow });
                if (shape == "unproven") await db.AgentIncidents.Where(i => i.SessionId == f.A.Id).ExecuteDeleteAsync();
                await db.SaveChangesAsync();
            }
            var error = await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id)));
            error.Code.ShouldBe(shape == "foreign" ? "standing_resume_not_owned" : "standing_resume_owner_unproven");
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
            (await verify.AgentSessions.FindAsync(f.A.Id))!.StartedAt.ShouldBe(f.A.StartedAt, TimeSpan.FromMilliseconds(1));
        }
    }

    [Test]
    public async Task Recovery_options_cannot_bypass_existing_start_guards()
    {
        foreach (var option in new[] { "retry", "selection", "fresh" })
        {
            await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter());
            await f.SeedAsync(held: true);
            await using (var db = f.Db())
            {
                (await db.Agents.FindAsync(f.Agent.Id))!.WorkingDirectory = Path.Combine(f.Root, "missing");
                await db.SaveChangesAsync();
            }
            var request = option == "retry" ? new StartAgentRequest(RetryContinuity: true)
                : option == "selection" ? new(ResumeSessionId: f.A.Id) : new(Fresh: true);
            await Should.ThrowAsync<ConflictException>(() => f.StartAsync(request));
            await using var verify = f.Db();
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
        }
    }

    [Test]
    public async Task Invalid_or_busy_targets_refuse_before_reservation()
    {
        await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter());
        await f.SeedAsync();
        await Should.ThrowAsync<ValidationException>(() => f.StartAsync(new(Fresh: true, ResumeSessionId: f.A.Id)));
        await Should.ThrowAsync<ValidationException>(() => f.StartAsync(new(Fresh: true, RetryContinuity: true)));
        await Should.ThrowAsync<ValidationException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id, RetryContinuity: true)));
        await Should.ThrowAsync<NotFoundException>(() => f.StartAsync(new(ResumeSessionId: Guid.NewGuid())));
        await using var db = f.Db();
        (await db.AgentSessions.FindAsync(f.A.Id))!.Status = SessionStatus.Starting;
        await db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => f.StartAsync(new(ResumeSessionId: f.A.Id))))
            .Code.ShouldBe("standing_resume_target_active");
    }
}
