using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingSessionSwitchConcurrencyTests
{
    [Test]
    public async Task Concurrent_selections_reserve_one_generation_without_locking_during_runner_probe()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = ready };
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        var calls = 0;
        f.Harness.Runner.ListOverride = async ct =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return [];
        };
        async Task<bool> Start()
        {
            try { await f.StartAsync(new(ResumeSessionId: f.A.Id, Prompt: "One accepted prompt")); return true; }
            catch (ConflictException ex) when (ex.Code == "standing_resume_current_changed") { return false; }
        }
        var first = Start(); var second = Start();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var db = f.Db())
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2s'");
                (await db.Agents.FromSqlInterpolated($"SELECT * FROM \"Agents\" WHERE \"Id\" = {f.Agent.Id} FOR UPDATE").SingleAsync()).Id.ShouldBe(f.Agent.Id);
                await transaction.CommitAsync();
            }
            release.TrySetResult();
            var accepted = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
            accepted.Count(x => x).ShouldBe(1);
            ready.TrySetResult(true); await f.IdleAsync();
            adapter.StartedArgs.ShouldContain("--resume");
            adapter.StartedSessionId.ShouldBe(f.A.Id);
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.A.Id.ToString("D"));
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == f.Agent.Id && i.Kind == AgentIncidentKind.StandingResumeSelected)).ShouldBe(1);
            (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.A.Id && m.Body == "One accepted prompt")).ShouldBe(1);
        }
        finally { release.TrySetResult(); ready.TrySetResult(true); await Task.WhenAll(first, second); await f.IdleAsync(); }
    }

    [Test]
    [Arguments("pointer")]
    [Arguments("execution")]
    [Arguments("delivery")]
    [Arguments("owner")]
    public async Task Reservation_rechecks_changes_committed_after_runner_preflight(string change)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync(held: true);
        f.Harness.Runner.ListOverride = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return []; };
        var start = f.StartAsync(new(ResumeSessionId: f.A.Id));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var db = f.Db())
            {
                if (change == "pointer") (await db.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId = f.A.Id.ToString("D");
                if (change == "delivery") db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(),
                    AgentSessionId = f.B.Id, Sequence = 1, Body = "Previously submitted", LastDeliveryBaselineSequence = 0, CreatedAt = DateTime.UtcNow });
                if (change == "execution")
                {
                    var id = Guid.NewGuid();
                    db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, AgentId = f.Agent.Id, AgentSessionId = f.B.Id,
                        Title = "Execution arrived", Goal = "Synthetic", WorkingDirectory = f.Root,
                        Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow });
                }
                if (change == "owner") db.Agents.Add(new Agent { Id = Guid.NewGuid(), Name = "Competing pointer",
                    Slug = $"competing-{Guid.NewGuid():N}", WorkingDirectory = f.Root, PersistentSessionId = f.A.Id.ToString("D"),
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            release.TrySetResult();
            var refusal = await Should.ThrowAsync<ConflictException>(start);
            refusal.Code.ShouldBe(change switch { "pointer" => "standing_resume_current_changed", "delivery" => "standing_resume_delivery_pending",
                "execution" => "standing_resume_work_in_flight", _ => "standing_resume_owner_unproven" });
            adapter.Started.ShouldBeFalse();
            await using var verify = f.Db();
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldNotBeNull();
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == f.Agent.Id && i.Kind == AgentIncidentKind.StandingResumeSelected)).ShouldBe(0);
            (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.A.Id)).ShouldBe(0);
        }
        finally { release.TrySetResult(); try { await start; } catch (ConflictException) { } await f.IdleAsync(); }
    }

    [Test]
    public async Task Stop_supersedes_launch_before_spawn_and_before_typing()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = ready };
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        try
        {
            await f.StartAsync(new(ResumeSessionId: f.A.Id, Prompt: "Must never type"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!adapter.Started) await Task.Delay(10, timeout.Token);
            await using var scope = f.Harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.Agent.Id, default);
            ready.TrySetResult(true);
            await f.IdleAsync();
            adapter.Killed.ShouldBeTrue(); adapter.Disposed.ShouldBeTrue();
            adapter.SentInput.ShouldBeEmpty();
            await using var db = f.Db();
            (await db.Agents.FindAsync(f.Agent.Id))!.Status.ShouldBe(AgentStatus.Stopped);
            (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!.Suspended.ShouldBeTrue();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.A.Id)).ShouldBe(0);
        }
        finally { ready.TrySetResult(true); await f.IdleAsync(); }
    }
}
