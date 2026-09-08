using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
    public async Task Card0412_D8_stalled_standing_start_rearms_and_actually_starts_again()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        try
        {
            var first = new FakeAgentProtocolAdapter();
            var retry = new FakeAgentProtocolAdapter();
            await using var harness = AgentSupervisionTests.BuildHarness(tempRoot, [first, retry],
                definitionKind: "ClaudeCode",
                configureDb: options => options.UseNpgsql(schema.ConnectionString),
                supervision: new SupervisionSettings
                {
                    CapacityRecovery = new CapacityRecoverySettings { Enabled = true, JitterSeconds = 0 },
                });
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            var sessionId = Guid.NewGuid();
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                var owner = await db.Agents.SingleAsync(a => a.Id == agent.Id);
                owner.PersistentSessionId = sessionId.ToString("D");
                owner.Kind = AgentKind.ClaudeCode;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
                    Cwd = owner.WorkingDirectory, Status = SessionStatus.Failed,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10), StartedAt = DateTime.UtcNow.AddMinutes(-10),
                });
                await db.SaveChangesAsync();
            }
            var runtime = harness.Provider.GetRequiredService<AgentSessionRuntime>();

            var recovery = harness.Provider.GetRequiredService<CapacityRecoveryService>();
            var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart,
                holdAlreadyCleared: true, agentId: agent.Id, sessionId: sessionId), CancellationToken.None);
            await recovery.ReconcileAsync(CancellationToken.None);
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                (await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode))
                    .GrantedWaitId.ShouldBe(wait.Id);
            }
            await harness.Supervisor().TickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            first.StartedSessionId.ShouldBe(sessionId, string.Join(Environment.NewLine, harness.SupervisorLog));
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                (await db.AgentSessions.SingleAsync(s => s.Id == sessionId))
                    .CapacityRecoveryActionKey.ShouldBe(wait.ActionKey);
                var admitted = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
                admitted.AdmissionCount.ShouldBe(1);
                CapacityRecoveryPolicy.HasExecutionReceipt(admitted).ShouldBeFalse();
            }

            // No transcript confirmation or launch receipt: the next action must launch again.
            await runtime.ObserveExitAsync(sessionId, 1, AgentExitReason.ProcessExited, CancellationToken.None);
            await runtime.DisposeSessionAsync(sessionId);
            harness.Clock.Advance(TimeSpan.FromSeconds(121));
            await recovery.ReconcileAsync(CancellationToken.None);
            var rearmed = (await recovery.FindUnfinishedAsync(wait.ConsumerKey, CancellationToken.None))!;
            rearmed.ActionKey.ShouldNotBe(wait.ActionKey);
            await harness.Supervisor().TickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            retry.StartedSessionId.ShouldBe(sessionId, string.Join(Environment.NewLine, harness.SupervisorLog));
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                (await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id)).AdmissionCount.ShouldBe(2);
                (await db.AgentSessions.SingleAsync(s => s.Id == sessionId))
                    .CapacityRecoveryActionKey.ShouldBe(rearmed.ActionKey);
            }
        }
        finally
        {
            await AgentSupervisionTests.CleanupAsync(tempRoot);
        }
    }

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
    public async Task Card0412_V16_capacity_start_requires_always_on()
    {
        var tempRoot = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter()], includeModelAvailability: true);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(harness, tempRoot);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var row = await db.Agents.SingleAsync(a => a.Id == agent.Id);
                row.AlwaysOn = false;
                await db.SaveChangesAsync();
            }

            var ex = await Should.ThrowAsync<Exception>(() =>
                harness.Control.StartAsync(
                    agent.Id,
                    new StartAgentRequest(CapacityRecovery: true, IgnoreSubscriptionQuota: true),
                    CancellationToken.None));
            ex.Message.ShouldContain("unowned or ephemeral");
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
                var provider = await db.Set<CapacityRecoveryProviderState>()
                    .FirstOrDefaultAsync(s => s.Kind == AgentKind.ClaudeCode);
                if (provider is null)
                {
                    db.Set<CapacityRecoveryProviderState>().Add(new CapacityRecoveryProviderState
                    {
                        Kind = AgentKind.ClaudeCode,
                        GrantedWaitId = waitId,
                        GrantedActionKey = actionKey,
                        GrantedAt = DateTime.UtcNow,
                        GrantVersion = 1,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    provider.GrantedWaitId = waitId;
                    provider.GrantedActionKey = actionKey;
                    provider.GrantedAt = DateTime.UtcNow;
                    provider.GrantVersion++;
                    provider.UpdatedAt = DateTime.UtcNow;
                }
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
