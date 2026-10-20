using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public partial class StandingRestartEscalationTests
{
    [Test]
    public async Task C510_counts_only_distinct_admitted_failed_resumes()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var healthy = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(),
                    new FakeAgentProtocolAdapter { ThrowOnStart = new InvalidOperationException("resume 1") },
                    new FakeAgentProtocolAdapter { ThrowOnStart = new ArgumentException("resume 2") },
                    new FakeAgentProtocolAdapter { ThrowOnStart = new InvalidOperationException("resume 3") },
                    healthy],
                new SupervisionSettings { HealthyUptimeResetMinutes = 5 }, definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await SessionExitObservation.ObserveMatchingAsync(
                h.Provider.GetRequiredService<AgentSessionRuntime>(),
                id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
            await h.Supervisor().TickAsync(default);
            (await ReadAsync(agent.Id)).AutomaticResumeFailures.ShouldBe(0, "initial crash was not an automatic resume");
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, attempt) + 1));
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await h.Supervisor().TickAsync(default);
                await h.Supervisor().TickAsync(default);
                var state = await ReadAsync(agent.Id);
                state.AutomaticResumeFailures.ShouldBe(attempt, "each admitted generation is consumed once");
                state.LastAutomaticResumeSessionId.ShouldBe(id);
                state.LastAutomaticResumeStartedAt.ShouldBe(state.LastObservedRestartStartedAt);
                state.ContinuityResumeFailures.ShouldBe(attempt + 1, "legacy accounting still includes the initial crash");
            }

            // S1 records the threshold; S2 alone will authorize a new conversation.
            h.Clock.Advance(TimeSpan.FromSeconds(81));
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            healthy.StartedSessionId.ShouldBe(id);
            healthy.StartedArgs.ShouldContain("--resume");
            (await ReadAsync(agent.Id)).AutomaticResumeFailures.ShouldBe(3, "queued acceptance is not recovery");
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.AutomaticFreshConsumed = true;
                state.AutomaticFreshAlertedHoldAt = h.Clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync();
                var completed = (await db.AgentSessions.FindAsync(id))!.InteractiveLaunchCompletedAt!.Value;
                h.Clock.Advance(completed.AddMinutes(5).AddTicks(-1) - h.Clock.GetUtcNow().UtcDateTime);
            }
            await h.Supervisor().TickAsync(default);
            (await ReadAsync(agent.Id)).AutomaticResumeFailures.ShouldBe(3);
            (await ReadAsync(agent.Id)).AutomaticFreshConsumed.ShouldBeTrue();
            h.Clock.Advance(TimeSpan.FromTicks(1));
            await h.Supervisor().TickAsync(default);
            var recovered = await ReadAsync(agent.Id);
            recovered.AutomaticResumeFailures.ShouldBe(0);
            recovered.LastAutomaticResumeSessionId.ShouldBeNull();
            recovered.LastAutomaticResumeStartedAt.ShouldBeNull();
            recovered.AutomaticFreshConsumed.ShouldBeFalse();
            recovered.AutomaticFreshAlertedHoldAt.ShouldBeNull();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    internal static async Task<AgentSupervisionState> ReadAsync(Guid agentId)
    {
        await using var db = AgentSupervisionTests.CreateContext();
        return await db.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == agentId);
    }
}
