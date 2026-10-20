using Antiphon.Server.Application.Dtos;
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

public partial class StandingRestartEscalationTests
{
    [Test]
    public async Task C510_S2_three_failed_resumes_reserve_fresh_only_once()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var resumes = Enumerable.Range(1, 3).Select(n => new FakeAgentProtocolAdapter
            { ThrowOnStart = new InvalidOperationException($"resume {n}") }).ToArray();
        var fresh = new FakeAgentProtocolAdapter { ThrowOnStart = new InvalidOperationException("fresh failed") };
        try
        {
            Guid agentId;
            Guid oldId;
            Guid newId;
            await using (var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), .. resumes, fresh], definitionKind: "ClaudeCode"))
            {
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                agentId = agent.Id;
                oldId = Guid.Parse((await h.Control.StartAsync(agent.Id, new(), default)).PersistentSessionId!);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await SessionExitObservation.ObserveMatchingAsync(h.Provider.GetRequiredService<AgentSessionRuntime>(),
                    oldId, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
                await h.Supervisor().TickAsync(default);
                (await ReadAsync(agentId)).AutomaticResumeFailures.ShouldBe(0);
                for (var n = 1; n <= 3; n++)
                {
                    h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, n) + 1));
                    await h.Supervisor().TickAsync(default);
                    await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                    await h.Supervisor().TickAsync(default);
                    resumes[n - 1].StartedSessionId.ShouldBe(oldId);
                    resumes[n - 1].StartedArgs.ShouldContain("--resume");
                    (await ReadAsync(agentId)).AutomaticResumeFailures.ShouldBe(n);
                }
                h.Clock.Advance(TimeSpan.FromSeconds(81));
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await using var db = AgentSupervisionTests.CreateContext();
                newId = Guid.Parse((await db.Agents.FindAsync(agentId))!.PersistentSessionId!);
                newId.ShouldNotBe(oldId, "the fourth recovery must reserve a new conversation");
                fresh.StartedSessionId.ShouldBe(newId);
                fresh.StartedArgs.ShouldNotContain("--resume");
                (await db.AgentSessions.FindAsync(oldId)).ShouldNotBeNull();
                (await db.AgentSessions.FindAsync(newId))!.StandingAgentId.ShouldBe(agentId);
                var state = await ReadAsync(agentId);
                state.AutomaticFreshConsumed.ShouldBeTrue();
                state.AutomaticResumeFailures.ShouldBe(3, "queued fresh is not health");
            }
            // The failed Fresh must not re-arm when the provider and scoped services restart.
            await using var rebuilt = AgentSupervisionTests.BuildHarness(root, [], definitionKind: "ClaudeCode");
            await rebuilt.Supervisor().TickAsync(default);
            rebuilt.Clock.Advance(TimeSpan.FromDays(1));
            await rebuilt.Supervisor().TickAsync(default);
            await using var verify = AgentSupervisionTests.CreateContext();
            (await verify.Agents.FindAsync(agentId))!.PersistentSessionId.ShouldBe(newId.ToString("D"));
            (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(2);
            var held = await ReadAsync(agentId);
            held.AutomaticFreshConsumed.ShouldBeTrue();
            held.ContinuityReason.ShouldBe(StandingContinuityReason.RepeatedResumeFailure);
            held.NextRestartAt.ShouldBeNull();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }
}
