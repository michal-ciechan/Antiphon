using System.Data.Common;
using Antiphon.Server.Application.Services;
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
    [Arguments("before-spawn")] [Arguments("start-rpc")] [Arguments("saved-running")]
    public async Task Stop_at_launch_boundaries_prevents_obsolete_work_and_allows_later_owned_generation(string boundary)
    {
        var gate = new LaunchBoundaryGate(boundary);
        var adapter = new FakeAgentProtocolAdapter();
        var next = new FakeAgentProtocolAdapter();
        if (boundary == "start-rpc")
        {
            adapter.ThrowOnStartFactory = () => { gate.Entered.TrySetResult(); return null; };
            adapter.StartGate = gate.Release;
        }
        await using var f = new StandingRecoveryFixture(s => s.AddDbContext<AppDbContext>(b => b.AddInterceptors(gate, gate.Save)), adapter, next);
        await f.SeedAsync(); gate.SessionId = f.A.Id; gate.Owns = () => f.Harness.LaunchQueue.Owns(f.A.Id);
        try
        {
            await f.StartAsync(new(ResumeSessionId: f.A.Id, Prompt: "Obsolete prompt must never be queued", RemoteControl: true));
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using (var scope = f.Harness.Provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.Agent.Id, default);
            (await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                f.StartAsync(new(ResumeSessionId: f.B.Id)))).Code.ShouldBe("standing_resume_current_active");
            gate.Release.TrySetResult(); await f.IdleAsync();
            adapter.Started.ShouldBe(boundary != "before-spawn");
            adapter.Killed.ShouldBe(boundary != "before-spawn");
            adapter.Disposed.ShouldBe(boundary != "before-spawn");
            adapter.Inputs.ShouldBeEmpty(); adapter.Prompts.ShouldBeEmpty();
            await using (var db = f.Db())
            {
                (await db.AgentSessions.FindAsync(f.A.Id))!.Status.ShouldBe(SessionStatus.Stopped);
                (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!.Suspended.ShouldBeTrue();
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.A.Id)).ShouldBe(0);
            }
            // Only once the obsolete launch has released ownership may another selected
            // generation launch. The prior adapter cannot revive or overwrite its pointer.
            await f.StartAsync(new(ResumeSessionId: f.B.Id)); await f.IdleAsync();
            var winner = boundary == "before-spawn" ? adapter : next;
            winner.StartedSessionId.ShouldBe(f.B.Id);
            await using var verify = f.Db();
            (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await verify.AgentSessions.FindAsync(f.A.Id))!.Status.ShouldBe(SessionStatus.Stopped);
            (await verify.AgentSessions.FindAsync(f.B.Id))!.InteractiveLaunchCompletedAt.ShouldNotBeNull();
        }
        finally { gate.Release.TrySetResult(); await f.IdleAsync(); }
    }

    private sealed class LaunchBoundaryGate(string boundary) : DbCommandInterceptor
    {
        public Guid SessionId { get; set; }
        public Func<bool> Owns { get; set; } = () => false;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SaveChangesInterceptor Save => new RunningSave(this);
        private int _hits;
        private bool HoldRunning => boundary == "saved-running";
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (boundary == "before-spawn" && Owns() && command.CommandText.Contains("FROM \"AgentSessions\"")
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == SessionId)
                && Interlocked.CompareExchange(ref _hits, 1, 0) == 0)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
        private sealed class RunningSave(LaunchBoundaryGate gate) : SaveChangesInterceptor
        {
            public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
            {
                if (gate.HoldRunning && data.Context!.ChangeTracker.Entries<AgentSession>().Any(e => e.Entity.Id == gate.SessionId
                    && e.Entity.Status == SessionStatus.Running && e.Entity.InteractiveLaunchCompletedAt == null)
                    && Interlocked.CompareExchange(ref gate._hits, 1, 0) == 0)
                { gate.Entered.TrySetResult(); await gate.Release.Task.WaitAsync(ct); }
                return result;
            }
        }
    }
}
