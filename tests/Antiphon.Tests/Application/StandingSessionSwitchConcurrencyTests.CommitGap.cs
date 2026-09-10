using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingSessionSwitchConcurrencyTests
{
    [Test]
    public async Task Delayed_fresh_enqueue_cannot_launch_over_a_later_resumed_generation()
    {
        var gate = new FreshCommitGate();
        var resumed = new FakeAgentProtocolAdapter();
        var obsolete = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(
            s => s.AddDbContext<AppDbContext>(b => b.AddInterceptors(gate)), resumed, obsolete);
        await f.SeedAsync();
        gate.AgentId = f.Agent.Id;
        var fresh = f.StartAsync(new(Fresh: true));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Guid selected;
            DateTime freshGeneration;
            await using (var db = f.Db())
            {
                selected = Guid.Parse((await db.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId!);
                freshGeneration = (await db.AgentSessions.FindAsync(selected))!.StartedAt;
            }
            f.Harness.LaunchQueue.Owns(selected).ShouldBeFalse();
            // The retained review reproducer gated this exact commit-to-enqueue gap.
            // Fresh must now hold C's queue lock, so Stop can revoke it but a later
            // resume cannot advance C's generation until enqueue has taken ownership.
            await using (var scope = f.Harness.Provider.CreateAsyncScope())
            {
                var queueLock = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>().GetLock(selected);
                var acquired = await queueLock.WaitAsync(0);
                if (acquired) queueLock.Release();
                acquired.ShouldBeFalse("Fresh must protect its new target across commit-to-enqueue");
            }
            await using (var scope = f.Harness.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.Agent.Id, default);
            gate.Release.TrySetResult();
            await fresh;
            await f.IdleAsync();
            resumed.Started.ShouldBeFalse("Stop revoked the delayed Fresh worker before adapter creation");
            await f.StartAsync(new(ResumeSessionId: selected));
            await f.IdleAsync();
            resumed.Started.ShouldBeTrue();
            resumed.StartedArgs.ShouldContain("--resume");
            // Independently exercise the immutable queued generation: even if obsolete
            // work is delivered after the winner settles, it cannot borrow that generation.
            f.Harness.LaunchQueue.EnqueueInteractiveSession(selected, f.Agent.Id, freshGeneration,
                new AgentLaunchSpec("fake", AgentKind.ClaudeCode, "fake", [],
                    new Dictionary<string, string>(), f.Root, 120, 30), null,
                initialPrompt: "obsolete fresh prompt must not be delivered");
            await f.IdleAsync();
            obsolete.Started.ShouldBeFalse("the original Fresh reservation was superseded by Stop and a later resume");
            obsolete.Killed.ShouldBeFalse();
            obsolete.Disposed.ShouldBeFalse();
            await using var verify = f.Db();
            var winner = (await verify.AgentSessions.FindAsync(selected))!;
            winner.StartedAt.ShouldBeGreaterThan(freshGeneration);
            winner.Status.ShouldBe(SessionStatus.Running);
            winner.InteractiveLaunchCompletedAt.ShouldNotBeNull();
            winner.FailureReason.ShouldBeNull();
            winner.RestartFailureKind.ShouldBeNull();
            (await verify.Agents.FindAsync(f.Agent.Id))!.Status.ShouldBe(AgentStatus.Running);
            (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == selected)).ShouldBe(0);
        }
        finally
        {
            gate.Release.TrySetResult();
            await fresh;
            await f.IdleAsync();
        }
    }

    private sealed class FreshCommitGate : DbTransactionInterceptor
    {
        public Guid AgentId { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hits;
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken ct = default)
        {
            if (AgentId != Guid.Empty && data.Context!.ChangeTracker.Entries<AgentIncident>().Any(e =>
                    e.Entity.AgentId == AgentId && e.Entity.Kind == AgentIncidentKind.StandingFreshSelected)
                && Interlocked.CompareExchange(ref _hits, 1, 0) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
        }
    }
}
