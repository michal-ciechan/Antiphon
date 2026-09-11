using System.Net;
using System.Net.Http.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class HerdrPaneDisposalApplicationTests
{
    private static async Task CurrentOwner(bool suspended, bool launching, bool active)
    {
        await using var f = new StandingRecoveryFixture(); await f.SeedAsync();
        await using var db = f.Db();
        db.AgentSupervisionStates.Add(new() { AgentId = f.Agent.Id, Suspended = suspended });
        if (active) (await db.AgentSessions.FindAsync(f.B.Id))!.Status = SessionStatus.Running;
        await db.SaveChangesAsync();
        await using var h = new HerdrDisposalHttpFixture();
        var queue = f.Harness.Provider.GetRequiredService<SessionMessageQueueService>();
        h.Ownership = new HerdrPaneDisposalOwnership(db, queue, f.Harness.LaunchQueue);
        await h.StartAsync();
        h.Runner.Backend.Transform = o => o with { Claims = [new(f.B.Id, "last-pane", "launched", false)] };
        var p = await h.Runner.Service.PreviewAsync(new(h.Runner.PaneId, f.B.Id), default);
        if (launching) f.Harness.LaunchQueue.TryRegister(f.B.Id).ShouldBeTrue();
        try
        {
            using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", h.Runner.Request(p));
            response.StatusCode.ShouldBe(!suspended || launching || active ? HttpStatusCode.Conflict : HttpStatusCode.OK);
            h.Runner.Backend.Closes.ShouldBe(!suspended || launching || active ? 0 : 1);
            (await db.Agents.AsNoTracking().SingleAsync(a => a.Id == f.Agent.Id)).PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
            (await db.AgentSupervisionStates.AsNoTracking().SingleAsync(a => a.AgentId == f.Agent.Id)).Suspended.ShouldBe(suspended);
        }
        finally { if (launching) f.Harness.LaunchQueue.Unregister(f.B.Id); }
    }
    [Test] public Task C461_G047_Persisted_stop_intent() => CurrentOwner(false, false, false);
    [Test] public async Task Ownership_refused_preview_cannot_be_reused_after_Stop()
    {
        await using var f = new StandingRecoveryFixture(); await f.SeedAsync(); await using var db = f.Db();
        await using var h = new HerdrDisposalHttpFixture();
        h.Ownership = new HerdrPaneDisposalOwnership(db, f.Harness.Provider.GetRequiredService<SessionMessageQueueService>(), f.Harness.LaunchQueue);
        await h.StartAsync(); h.Runner.Backend.Transform = o => o with { Claims = [new(f.B.Id, "token", null, false)] };
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals/preview", new HerdrPaneDisposalPreviewRequest(h.Runner.PaneId, f.B.Id));
        var p = (await response.Content.ReadFromJsonAsync<HerdrPaneDisposalPreview>())!;
        p.Eligible.ShouldBeFalse(); p.PreviewId.ShouldBe(Guid.Empty);
        await f.Harness.Control.StopAsync(f.Agent.Id, default);
        using var execution = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", h.Runner.Request(p));
        execution.StatusCode.ShouldBe(HttpStatusCode.BadRequest); h.Runner.Backend.Closes.ShouldBe(0);
    }
    [Test] public Task C461_G048_No_launch_generation() => CurrentOwner(true, true, false);
    [Test] public Task C461_G052_No_implicit_stop() => CurrentOwner(true, false, true);
    [Test] public Task C461_G105_No_false_PaneLeftOpen_incident() => CurrentOwner(false, false, false);
    [Test] public Task Stopped_current_target_is_eligible_after_launch_release() => CurrentOwner(true, false, false);

    [Test] public async Task C461_G049_Standing_execution_lock()
    {
        await using var f = new StandingRecoveryFixture(); await f.SeedAsync(); await using var db = f.Db();
        db.AgentSupervisionStates.Add(new() { AgentId = f.Agent.Id, Suspended = true }); await db.SaveChangesAsync();
        await using var h = new HerdrDisposalHttpFixture();
        h.Ownership = new HerdrPaneDisposalOwnership(db, f.Harness.Provider.GetRequiredService<SessionMessageQueueService>(), f.Harness.LaunchQueue);
        await h.StartAsync(); h.Runner.Backend.Transform = o => o with { Claims = [new(f.B.Id, "token", null, false)] };
        var p = await h.Runner.Service.PreviewAsync(new(h.Runner.PaneId, f.B.Id), default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Runner.Backend.BeforeClose = async () => { entered.SetResult(); await release.Task; };
        var disposal = h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", h.Runner.Request(p));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var start = f.StartAsync(new());
        try { await Task.Delay(100); start.IsCompleted.ShouldBeFalse(); f.Harness.LaunchQueue.Owns(f.B.Id).ShouldBeFalse(); }
        finally { release.TrySetResult(); }
        using var response = await disposal; response.EnsureSuccessStatusCode();
        await start; await f.IdleAsync(); h.Runner.Backend.Closes.ShouldBe(1);
    }
    [Test] public async Task C461_G050_No_transaction_over_rpc()
    {
        await using var f = new StandingRecoveryFixture(); await f.SeedAsync(); await using var db = f.Db();
        await using var h = new HerdrDisposalHttpFixture();
        h.Ownership = new HerdrPaneDisposalOwnership(db, f.Harness.Provider.GetRequiredService<SessionMessageQueueService>(), f.Harness.LaunchQueue);
        await h.StartAsync(); var p = await h.Runner.PreviewAsync();
        h.Runner.Backend.BeforeClose = async () =>
        {
            db.Database.CurrentTransaction.ShouldBeNull(); await using var second = f.Db();
            (await second.Agents.SingleAsync(a => a.Id == f.Agent.Id)).PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
        };
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", h.Runner.Request(p)); response.EnsureSuccessStatusCode();
    }
    private static async Task HistoryAndRowless(bool rowless)
    {
        await using var f = new StandingRecoveryFixture(); await f.SeedAsync(); await using var db = f.Db();
        var id = rowless ? Guid.NewGuid() : f.A.Id;
        var initial = await db.AgentSessions.AsNoTracking().Where(s => s.Id == id).ToArrayAsync();
        var count = await db.AgentSessions.CountAsync(s => s.Id == id);
        await using var h = new HerdrDisposalHttpFixture();
        h.Ownership = new HerdrPaneDisposalOwnership(db, f.Harness.Provider.GetRequiredService<SessionMessageQueueService>(), f.Harness.LaunchQueue);
        await h.StartAsync(); h.Runner.Backend.Transform = o => o with { Claims = [new(id, "last-pane", "attached", false)] };
        var p = await h.Runner.Service.PreviewAsync(new(h.Runner.PaneId, id), default);
        using var response = await h.Http.PostAsJsonAsync("/api/herdr/pane-disposals", h.Runner.Request(p)); response.EnsureSuccessStatusCode();
        (await db.AgentSessions.CountAsync(s => s.Id == id)).ShouldBe(count);
        var after = await db.AgentSessions.AsNoTracking().Where(s => s.Id == id).ToArrayAsync();
        System.Text.Json.JsonSerializer.Serialize(after).ShouldBe(System.Text.Json.JsonSerializer.Serialize(initial));
        (await db.Agents.AsNoTracking().SingleAsync(a => a.Id == f.Agent.Id)).PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.Agent.Id)).ShouldBe(0);
    }
    [Test] public Task C461_G051_Replacement_owner_untouched() => HistoryAndRowless(false);
    [Test] public Task C461_G102_No_synthetic_session() => HistoryAndRowless(true);
    [Test] public Task C461_G104_Preserve_exit_history() => HistoryAndRowless(false);
    [Test] [Arguments(false)] [Arguments(true)] public Task Disposes_detached_exited_and_rowless_targets(bool rowless) => HistoryAndRowless(rowless);
    [Test] public async Task C461_G111_Explicit_disposal_only()
    {
        await using var f = new StandingRecoveryFixture(); await f.SeedAsync();
        await using var h = new HerdrDisposalHttpFixture(); await h.StartAsync();
        await f.Harness.Control.StopAsync(f.Agent.Id, default);
        await f.IdleAsync(); h.Runner.Backend.Closes.ShouldBe(0);
        await using var db = f.Db(); (await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == f.Agent.Id)).Suspended.ShouldBeTrue();
    }
}
