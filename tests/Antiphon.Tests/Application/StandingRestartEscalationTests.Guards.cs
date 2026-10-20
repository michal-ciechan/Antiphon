using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartEscalationTests
{
    [Test]
    [Arguments("repeated-hold")]
    [Arguments("legacy-hold")]
    [Arguments("native-hold")]
    [Arguments("herdr-hold")]
    [Arguments("suspended")]
    [Arguments("latched")]
    [Arguments("disabled")]
    [Arguments("consumed")]
    [Arguments("stale-generation")]
    [Arguments("stale-pointer")]
    [Arguments("live")]
    [Arguments("open-task")]
    [Arguments("attempted")]
    [Arguments("keyed")]
    [Arguments("manual")]
    [Arguments("not-standing")]
    [Arguments("specialist")]
    public async Task C510_S2_admission_guards_and_manual_resume(string variant)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(s => s.AddSingleton(Options.Create(new SupervisionSettings
            { AutomaticFreshAfterResumeFailures = variant == "disabled" ? 0 : 3 })), adapter);
        await f.SeedAsync();
        var generation = await SeedEscalationAsync(f);
        var messageId = Guid.NewGuid();
        DateTime? heldAt = null;
        await using (var db = f.Db())
        {
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            var state = (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!;
            if (variant is "repeated-hold" or "legacy-hold" or "native-hold")
            {
                await new StandingContinuityState(db, TimeProvider.System).HoldAsync(agent.Id, f.B.Id,
                    variant == "native-hold" ? StandingContinuityReason.NativeSessionMissing : StandingContinuityReason.RepeatedResumeFailure, default);
                heldAt = state.ContinuityHeldAt;
            }
            if (variant == "legacy-hold") { state.AutomaticResumeFailures = 0; state.LastAutomaticResumeSessionId = null; }
            if (variant == "herdr-hold") state.HerdrFailureHeldAt = generation;
            if (variant == "suspended") state.Suspended = true;
            if (variant == "latched") state.LivenessLatchedAt = generation;
            if (variant == "consumed") state.AutomaticFreshConsumed = true;
            if (variant == "live") (await db.AgentSessions.FindAsync(f.B.Id))!.Status = SessionStatus.Running;
            if (variant == "not-standing") agent.AlwaysOn = false;
            if (variant == "specialist") agent.StandingSpecialistRole = AgentTaskRole.Check;
            if (variant == "open-task")
            {
                var id = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, AgentId = agent.Id,
                    AgentSessionId = f.B.Id, Title = "active assignment", Goal = "guard", Status = AgentTaskStatus.Working,
                    CreatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid() });
            }
            if (variant is "attempted" or "keyed" or "repeated-hold")
                db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = messageId, AgentSessionId = f.B.Id,
                    Body = "Retain this input", Sequence = 7, CreatedAt = DateTime.UtcNow,
                    DeliveryAttempts = variant == "attempted" ? 1 : 0,
                    LastDeliveryBaselineSequence = variant == "attempted" ? 42 : null,
                    Origin = variant == "keyed" ? QueuedMessageOrigin.Channel : QueuedMessageOrigin.Ui,
                    ConversationKey = variant == "keyed" ? "test:recipient" : null });
            await db.SaveChangesAsync();
        }
        try
        {
            await using var scope = f.Harness.Provider.CreateAsyncScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            if (variant == "manual")
            {
                await control.StartAsync(f.Agent.Id, new(), default);
                await f.IdleAsync();
                adapter.StartedSessionId.ShouldBe(f.B.Id);
                adapter.StartedArgs.ShouldContain("--resume");
                (await ReadAsync(f.Agent.Id)).AutomaticResumeFailures.ShouldBe(0);
                return;
            }
            var attempt = control.StartSupervisedFreshAsync(f.Agent.Id,
                variant == "stale-pointer" ? f.A.Id : f.B.Id,
                variant == "stale-generation" ? generation.AddSeconds(-1) : generation, null, default);
            if (variant == "repeated-hold")
            {
                var accepted = await attempt;
                await f.IdleAsync();
                var id = Guid.Parse(accepted.PersistentSessionId!);
                id.ShouldNotBe(f.B.Id);
                adapter.StartedSessionId.ShouldBe(id);
                (await ReadAsync(f.Agent.Id)).AutomaticFreshConsumed.ShouldBeTrue();
                await using var moved = f.Db();
                var message = (await moved.SessionQueuedMessages.FindAsync(messageId))!;
                message.AgentSessionId.ShouldBe(id);
                message.Body.ShouldBe("Retain this input");
                return;
            }
            var refusal = await Should.ThrowAsync<ConflictException>(attempt);
            var code = variant switch
            {
                "herdr-hold" => HerdrSupervisionStateService.HeldCode,
                "suspended" or "latched" => "standing_start_intent_revoked",
                "stale-generation" or "stale-pointer" => "standing_resume_current_changed",
                "live" => "standing_resume_current_active",
                "open-task" => "standing_resume_work_in_flight",
                "attempted" or "keyed" => "standing_resume_delivery_pending",
                _ => "standing_automatic_fresh_ineligible"
            };
            refusal.Code.ShouldBe(code, variant);
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(2);
            var stateAfter = await ReadAsync(f.Agent.Id);
            stateAfter.AutomaticFreshConsumed.ShouldBe(variant == "consumed");
            if (heldAt is not null) stateAfter.ContinuityHeldAt!.Value.ShouldBe(heldAt.Value, TimeSpan.FromMilliseconds(1));
            if (variant is "attempted" or "keyed")
            {
                var message = (await verify.SessionQueuedMessages.FindAsync(messageId))!;
                message.AgentSessionId.ShouldBe(f.B.Id);
                message.Body.ShouldBe("Retain this input");
                message.Status.ShouldBe(QueuedMessageStatus.Pending);
                message.DeliveryAttempts.ShouldBe(variant == "attempted" ? 1 : 0);
                message.LastDeliveryBaselineSequence.ShouldBe(variant == "attempted" ? 42L : null);
            }
        }
        finally
        {
            await using var cleanup = f.Db();
            await cleanup.AgentTasks.Where(t => t.AgentId == f.Agent.Id).ExecuteDeleteAsync();
        }
    }

    private async Task<DateTime> SeedEscalationAsync(StandingRecoveryFixture f)
    {
        await using var db = f.Db();
        (await db.Agents.FindAsync(f.Agent.Id))!.AlwaysOn = true;
        var current = (await db.AgentSessions.FindAsync(f.B.Id))!;
        db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = f.Agent.Id,
            AutomaticResumeFailures = 3, ContinuityResumeFailures = 4, RestartBackoffFailures = 4,
            LastAutomaticResumeSessionId = f.B.Id, LastAutomaticResumeStartedAt = current.StartedAt,
            LastObservedRestartSessionId = f.B.Id, LastObservedRestartStartedAt = current.StartedAt,
            NextRestartAt = DateTime.UtcNow.AddMinutes(-1), UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return current.StartedAt;
    }

    [Test]
    public async Task C510_S2_concurrent_fresh_reservations_have_one_winner()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        var generation = await SeedEscalationAsync(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        f.Harness.Runner.ListOverride = async ct =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return [];
        };
        async Task<bool> StartAsync()
        {
            await using var scope = f.Harness.Provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<AgentControlService>()
                    .StartSupervisedFreshAsync(f.Agent.Id, f.B.Id, generation, null, default);
                return true;
            }
            catch (ConflictException ex) when (ex.Code is "standing_resume_current_changed" or "standing_resume_current_active") { return false; }
        }
        var first = StartAsync();
        var second = StartAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.TrySetResult();
            var winners = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
            winners.Count(x => x).ShouldBe(1);
            await f.IdleAsync();
            await using var verify = f.Db();
            (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(3);
            (await ReadAsync(f.Agent.Id)).AutomaticFreshConsumed.ShouldBeTrue();
            adapter.StartedSessionId.ShouldNotBe(f.B.Id);
        }
        finally { release.TrySetResult(); await Task.WhenAll(first, second); await f.IdleAsync(); }
    }
}
