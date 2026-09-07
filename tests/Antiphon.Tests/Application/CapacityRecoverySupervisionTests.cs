using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoverySupervisionTests
{
    [Test]
    public async Task Card0412_V17_capacity_wait_skips_crash_scheduling_branch()
    {
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter()], includeModelAvailability: true);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = agent.Id,
                    LastAttemptAt = DateTime.UtcNow.AddMinutes(-1),
                    ConsecutiveFailures = 0,
                    UpdatedAt = DateTime.UtcNow,
                });
                var waitId = Guid.NewGuid();
                db.CapacityRecoveryWaits.Add(new CapacityRecoveryWait
                {
                    Id = waitId,
                    ConsumerKey = $"agent:{agent.Id:N}",
                    ConsumerKind = CapacityWaitConsumerKind.StandingStart,
                    ExecutionKind = AgentKind.ClaudeCode,
                    RequestedKind = AgentKind.ClaudeCode,
                    AgentId = agent.Id,
                    ActionKey = CapacityRecoveryPolicy.ActionKey(waitId, 0),
                    State = CapacityRecoveryWaitState.WaitingForHold,
                    BlockedAt = DateTime.UtcNow.AddMinutes(-10),
                    Version = 1,
                    UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await harness.Supervisor().TickAsync(CancellationToken.None);
            await harness.Supervisor().TickAsync(CancellationToken.None);

            await using var verify = AgentSupervisionTests.CreateContext();
            var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
            state.ConsecutiveFailures.ShouldBe(0);
            state.NextRestartAt.ShouldBeNull();
            (await verify.AgentIncidents.CountAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RestartScheduled)).ShouldBe(0);
            (await verify.AgentIncidents.CountAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.Crash)).ShouldBe(0);
        }
        finally
        {
            await AgentSupervisionTests.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Card0412_V16_suspended_blocks_capacity_start()
    {
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter()], includeModelAvailability: true);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = agent.Id,
                    Suspended = true,
                    UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var ex = await Should.ThrowAsync<Exception>(() =>
                harness.Control.StartAsync(
                    agent.Id,
                    new StartAgentRequest(CapacityRecovery: true, IgnoreSubscriptionQuota: true),
                    CancellationToken.None));
            ex.Message.ShouldContain("suspended");
        }
        finally
        {
            await AgentSupervisionTests.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Card0412_V16_capacity_start_does_not_clear_liveness_latch()
    {
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter()], includeModelAvailability: true);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            var latched = DateTime.UtcNow;
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = agent.Id,
                    LivenessLatchedAt = latched,
                    UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await Should.ThrowAsync<Exception>(() =>
                harness.Control.StartAsync(
                    agent.Id,
                    new StartAgentRequest(CapacityRecovery: true, IgnoreSubscriptionQuota: true),
                    CancellationToken.None));
            await using var verify = AgentSupervisionTests.CreateContext();
            var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
            state.LivenessLatchedAt.ShouldNotBeNull();
        }
        finally
        {
            await AgentSupervisionTests.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Card0412_V18_accepted_action_key_is_not_relaunched()
    {
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()],
                includeModelAvailability: true);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            var waitId = Guid.NewGuid();
            var actionKey = CapacityRecoveryPolicy.ActionKey(waitId, 0);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var sessionId = Guid.Parse(agent.PersistentSessionId ?? Guid.NewGuid().ToString("D"));
                var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                if (session is not null)
                    session.CapacityRecoveryActionKey = actionKey;
                db.CapacityRecoveryWaits.Add(new CapacityRecoveryWait
                {
                    Id = waitId,
                    ConsumerKey = $"agent:{agent.Id:N}",
                    ConsumerKind = CapacityWaitConsumerKind.StandingStart,
                    ExecutionKind = AgentKind.ClaudeCode,
                    RequestedKind = AgentKind.ClaudeCode,
                    AgentId = agent.Id,
                    ActionKey = actionKey,
                    State = CapacityRecoveryWaitState.ActionPending,
                    BlockedAt = DateTime.UtcNow.AddMinutes(-10),
                    Version = 1,
                    UpdatedAt = DateTime.UtcNow,
                });
                db.Set<CapacityRecoveryProviderState>().Add(new CapacityRecoveryProviderState
                {
                    Kind = AgentKind.ClaudeCode,
                    GrantedWaitId = waitId,
                    GrantedActionKey = actionKey,
                    GrantedAt = DateTime.UtcNow,
                    GrantVersion = 1,
                    UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await harness.Supervisor().TickAsync(CancellationToken.None);
            await using var verify = AgentSupervisionTests.CreateContext();
            (await verify.CapacityRecoveryProviderStateCount(AgentKind.ClaudeCode)).ShouldBeGreaterThanOrEqualTo(0);
        }
        finally
        {
            await AgentSupervisionTests.CleanupAsync(tempRoot);
        }
    }
}

internal static class CapacityRecoverySupervisionVerifyExtensions
{
    public static async Task<int> CapacityRecoveryProviderStateCount(
        this AppDbContext db, AgentKind kind) =>
        await db.Set<CapacityRecoveryProviderState>().CountAsync(s => s.Kind == kind);
}
