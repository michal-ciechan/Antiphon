using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Data.Common;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class HerdrLabelFollowConcurrencyTests
{
    [Test]
    public async Task Manual_edit_before_follow_wins()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = f.Service(); service.Boundary = async (name, ct) => { if (name == "before-lock") { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        var follow = service.FollowAsync(f.AgentId, CancellationToken.None);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await f.ManualAsync("Manual", "Manual workspace"); }
        finally { release.TrySetResult(); await follow; }
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Manual"); (await f.ReadAsync()).HerdrWorkspaceLabel.ShouldBe("Manual workspace");
        service.ConditionalWrites.ShouldBe(0);
    }

    [Test]
    public async Task Manual_edit_after_follow_wins()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); await using var manualDb = f.Open();
        var stale = await manualDb.Agents.SingleAsync(a => a.Id == f.AgentId); stale.HerdrTabLabel.ShouldBe("Old");
        (await f.ApplyAsync()).ShouldBeTrue();
        var manual = new AgentService(manualDb, new CardWorkflowRunFactory(manualDb, TimeProvider.System), new MockEventBus(),
            TimeProvider.System, new NoDirectories(), NullLogger<AgentService>.Instance);
        await manual.UpdateAsync(f.AgentId, new(stale.Name, stale.WorkingDirectory, stale.Details, null, stale.AssignmentPolicy,
            HerdrTabLabel: "Old", HerdrWorkspaceLabel: "Old workspace"), CancellationToken.None);
        var result = await f.ReadAsync(); result.HerdrTabLabel.ShouldBe("Old"); result.HerdrWorkspaceLabel.ShouldBe("Old workspace"); result.HerdrPlacementEditToken.ShouldNotBe(f.EditToken);
        (await f.ApplyAsync()).ShouldBeFalse();
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Away_and_back_or_clear_and_repin_does_not_rearm(bool clear)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        await f.ManualAsync(clear ? "" : "Away", clear ? "" : "Away workspace"); await f.ManualAsync("Old", "Old workspace");
        (await f.ApplyAsync()).ShouldBeFalse(); var a = await f.ReadAsync(); a.HerdrTabLabel.ShouldBe("Old"); a.HerdrWorkspaceLabel.ShouldBe("Old workspace"); a.HerdrPlacementEditToken.ShouldNotBe(f.EditToken);
    }

    [Test][Arguments("edit")][Arguments("pointer")]
    public async Task Follow_reloads_after_runner_response(string arm)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); await using var db = f.Open();
        _ = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
        f.Runner.GetOverride = async (_, _) => { if (arm == "edit") await f.ManualAsync("Manual"); else await f.MutateAsync((a, _) => a.PersistentSessionId = Guid.NewGuid().ToString()); return f.Dto; };
        (await f.Service(db).FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeFalse();
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe(arm == "edit" ? "Manual" : "Old");
    }

    [Test]
    public async Task Conditional_write_rechecks_session_generation() => await Conditional("generation");
    [Test]
    public async Task Conditional_write_rechecks_session_liveness() => await Conditional("liveness");

    private static async Task Conditional(string arm)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); var service = f.Service();
        service.Boundary = async (name, _) =>
        {
            if (name != "before-write") return;
            await using var db = f.Open();
            if (arm == "generation") await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, f.Dto.AcceptedStartedAt!.Value.AddTicks(10)));
            else await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
        };
        (await service.ApplyAsync(f.AgentId, f.Dto, CancellationToken.None)).ShouldBeFalse(); service.ConditionalWrites.ShouldBe(1);
        var a = await f.ReadAsync(); a.HerdrTabLabel.ShouldBe("Old"); a.HerdrLabelFollowSequence.ShouldBe(0);
    }

    [Test]
    public async Task Specialist_lock_order_is_preserved()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); var ownerId = Guid.NewGuid();
        await using (var db = f.Open())
        {
            var owner = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
            db.Agents.Add(new() { Id = ownerId, Name = "owner", Slug = "owner", StandingSpecialistRole = AgentTaskRole.Check,
                StandingSpecialistOwnerId = ownerId, CreatedAt = owner.CreatedAt, UpdatedAt = owner.UpdatedAt });
            owner.StandingSpecialistRole = AgentTaskRole.Check; owner.StandingSpecialistOwnerId = ownerId; await db.SaveChangesAsync();
        }
        var interceptor = new LockRecorder();
        var opts = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(f.ConnectionString)).AddInterceptors(interceptor).Options;
        await using var observed = new AppDbContext(opts);
        await f.Service(observed).ApplyAsync(f.AgentId, f.Dto, CancellationToken.None);
        interceptor.Ids.ShouldBe(new[] { ownerId, f.AgentId }); interceptor.Ids.Clear();
        // The actual manual writer must acquire the same owner-before-seat pair. A managed
        // alternate's permitted backend is PtyHost; this edit corrects the seeded old backend.
        var agent = await observed.Agents.SingleAsync(a => a.Id == f.AgentId);
        var manual = new AgentService(observed, new CardWorkflowRunFactory(observed, TimeProvider.System), new MockEventBus(),
            TimeProvider.System, new NoDirectories(), NullLogger<AgentService>.Instance);
        await manual.UpdateAsync(f.AgentId, new(agent.Name, agent.WorkingDirectory, agent.Details, null, agent.AssignmentPolicy,
            SessionBackend: SessionBackend.PtyHost, HerdrTabLabel: "Manual"), CancellationToken.None);
        interceptor.Ids.ShouldBe(new[] { ownerId, f.AgentId });
    }

    private sealed class LockRecorder : DbCommandInterceptor
    {
        public List<Guid> Ids { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Ids.Add((Guid)command.Parameters[0].Value!); return ValueTask.FromResult(result); }
    }
    private sealed class NoDirectories : Antiphon.Server.Application.Interfaces.IDirectoryWriter { public void CreateDirectory(string path) { } }
}
