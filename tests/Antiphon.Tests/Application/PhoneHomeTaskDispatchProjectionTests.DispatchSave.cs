using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1471. Remote dispatch is two-phase (CARD-0633): the first claim commits with the task
/// still Queued and hands the push + mirror to the preparer; the ready claim binds the agent and
/// session. The fresh pool agent must be persisted only by that second, binding claim. An agent
/// committed by the preparation-only save is unbound (no session, no task pointing at it), and
/// the ready claim then inserts a second row with the same <c>task-&lt;short&gt;</c> slug and fails
/// on <c>IX_Agents_Slug</c> (23505) unless an idle-pool sweep happened to delete the first row.
/// These tests claim through <see cref="AgentTaskDispatcher.DispatchOneAsync"/> on fresh scopes so
/// no sweep runs between the claims.
/// </summary>
public sealed partial class PhoneHomeTaskDispatchProjectionTests
{
    private const string DispatchSaveMirror = "/work/worktrees/task-dispatch-save";

    [Test]
    public async Task C_dispatch_save_preparation_has_no_unbound_agent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        // The mirror request is recorded but never answered until the test releases it, so the
        // preparation stays pending while the committed state is read.
        peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        peer.Reply = AnswerDispatchSave;
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.ClaudeCode);
        var sink = new RecordingLaunchSink();
        var graph = CreateDispatcher(schema, host, sink);
        await using var services = graph.Services;
        PhoneHomeFrame? held = null;
        try
        {
            (await ClaimAsync(services, taskId)).ShouldBe(AgentTaskDispatcher.DispatchOneResult.HeldForRemotePrep);
            held = await peer.WaitForAsync(PhoneHomeOperation.WorkspaceMirror, TimeSpan.FromSeconds(30));
            graph.Preparer.IsInFlight(taskId, out _).ShouldBeTrue("the mirror is outstanding");

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(AgentTaskStatus.Queued, task.FailureReason);
            task.WorktreePath.ShouldBe(workspace.Path);
            task.RemoteWorktreePath.ShouldBeNull();
            task.AgentId.ShouldBeNull();
            task.AgentSessionId.ShouldBeNull();
            task.DispatchedAt.ShouldBeNull();
            (await verify.TaskWorktreeMirrorBindings.CountAsync(b => b.TaskId == taskId))
                .ShouldBe(1, "the preparation row is the durable prep state");
            (await verify.Agents.Select(a => a.Slug).ToListAsync())
                .ShouldBeEmpty("the preparation-only save must not commit an unbound pool agent");
            (await verify.AgentSessions.CountAsync()).ShouldBe(0);
            (await verify.SessionQueuedMessages.CountAsync()).ShouldBe(0);
            sink.Specs.ShouldBeEmpty();
        }
        finally
        {
            await ReleaseMirrorAsync(peer, held);
            await graph.Preparer.WhenIdleAsync();
        }
    }

    [Test]
    public async Task C_dispatch_save_ready_claim_needs_no_pool_sweep()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.Reply = AnswerDispatchSave;
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.ClaudeCode);
        var sink = new RecordingLaunchSink();
        var graph = CreateDispatcher(schema, host, sink);
        await using var services = graph.Services;

        (await ClaimAsync(services, taskId)).ShouldBe(AgentTaskDispatcher.DispatchOneResult.HeldForRemotePrep);
        await graph.Preparer.WhenIdleAsync();
        // The ready claim on a fresh scope: no TickAsync, so no RetireIdleWarmAgentsAsync sweep can
        // delete an unbound row between the two claims. The live failure resumed this way.
        (await ClaimAsync(services, taskId)).ShouldBe(AgentTaskDispatcher.DispatchOneResult.Dispatched);

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.DispatchedAt.ShouldNotBeNull();
        var agent = (await verify.Agents.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
        agent.Id.ShouldBe(task.AgentId.ShouldNotBeNull());
        agent.Slug.ShouldBe("task-" + DelegationReportFormatter.Short(taskId));
        agent.IsPoolDelegate.ShouldBeTrue();
        agent.Status.ShouldBe(AgentStatus.Running);
        var session = (await verify.AgentSessions.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
        session.Id.ShouldBe(task.AgentSessionId.ShouldNotBeNull());
        agent.PersistentSessionId.ShouldBe(session.Id.ToString("D"));
        session.RunnerId.ShouldBe(host.AllowedRunnerId);
        session.RunnerCwd.ShouldBe(DispatchSaveMirror);
        session.Cwd.ShouldBe(workspace.Path);
        sink.Specs.ShouldHaveSingleItem().Cwd.ShouldBe(DispatchSaveMirror);
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == session.Id
                && (m.ExecutionTaskId == taskId || m.SourceTaskId == taskId)))
            .ShouldBe(1, "exactly one brief");
        (await verify.AgentTaskEvents.Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Failed)
                .Select(e => e.Detail).ToListAsync())
            .ShouldBeEmpty();
    }

    [Test]
    public async Task C_dispatch_save_repeated_pending_claim_stays_unbound()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        peer.Reply = AnswerDispatchSave;
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.ClaudeCode);
        var sink = new RecordingLaunchSink();
        var graph = CreateDispatcher(schema, host, sink);
        await using var services = graph.Services;
        PhoneHomeFrame? held = null;
        try
        {
            (await ClaimAsync(services, taskId)).ShouldBe(AgentTaskDispatcher.DispatchOneResult.HeldForRemotePrep);
            held = await peer.WaitForAsync(PhoneHomeOperation.WorkspaceMirror, TimeSpan.FromSeconds(30));

            // Re-enter the still-pending preparation from a second scope, without any sweep.
            await using var stale = services.CreateAsyncScope();
            var staleDb = stale.ServiceProvider.GetRequiredService<AppDbContext>();
            var staleTask = await staleDb.AgentTasks.SingleAsync(t => t.Id == taskId);
            var staleDispatcher = stale.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            (await staleDispatcher.DispatchOneAsync(staleTask, CancellationToken.None))
                .ShouldBe(AgentTaskDispatcher.DispatchOneResult.HeldForRemotePrep);

            await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                task.Status.ShouldBe(AgentTaskStatus.Queued, task.FailureReason);
                task.Attempt.ShouldBe(1);
                task.AgentId.ShouldBeNull();
                task.AgentSessionId.ShouldBeNull();
                task.RemoteWorktreePath.ShouldBeNull();
                (await verify.Agents.Select(a => a.Slug).ToListAsync())
                    .ShouldBeEmpty("a repeated pending claim must not commit a fresh agent");
                (await verify.AgentSessions.CountAsync()).ShouldBe(0);
                sink.Specs.ShouldBeEmpty();
            }

            // Finish the preparation and claim once.
            await ReleaseMirrorAsync(peer, held);
            held = null;
            await graph.Preparer.WhenIdleAsync();
            (await ClaimAsync(services, taskId)).ShouldBe(AgentTaskDispatcher.DispatchOneResult.Dispatched);

            // A further claim through the stale scope loses the token test and adds nothing.
            (await staleDispatcher.DispatchOneAsync(staleTask, CancellationToken.None))
                .ShouldBe(AgentTaskDispatcher.DispatchOneResult.NotClaimed);

            await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var dispatched = await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason);
            var agent = (await after.Agents.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
            agent.Id.ShouldBe(dispatched.AgentId.ShouldNotBeNull());
            var session = (await after.AgentSessions.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
            session.Id.ShouldBe(dispatched.AgentSessionId.ShouldNotBeNull());
            session.RunnerCwd.ShouldBe(DispatchSaveMirror);
            sink.Specs.ShouldHaveSingleItem();
        }
        finally
        {
            await ReleaseMirrorAsync(peer, held);
            await graph.Preparer.WhenIdleAsync();
        }
    }

    [Test]
    public async Task C_dispatch_failure_reason_names_the_violated_constraint()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.Reply = AnswerDispatchSave;
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.ClaudeCode);
        var slug = "task-" + DelegationReportFormatter.Short(taskId);
        var occupantId = Guid.NewGuid();
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            // A row that already owns the slug. It is not a pool delegate, so the tick's idle-pool
            // sweep leaves it alone and the binding claim's insert hits IX_Agents_Slug.
            var now = DateTime.UtcNow;
            seed.Agents.Add(new Agent
            {
                Id = occupantId, Name = "C1471 slug occupant", Slug = slug,
                Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                WorkingDirectory = workspace.Path, IsPoolDelegate = false, Status = AgentStatus.Idle,
                CreatedAt = now, UpdatedAt = now,
            });
            await seed.SaveChangesAsync();
        }
        var sink = new RecordingLaunchSink();
        var graph = CreateDispatcher(schema, host, sink);
        await using var services = graph.Services;

        await graph.Dispatcher.TickAsync(CancellationToken.None);
        await graph.Preparer.WhenIdleAsync();
        await graph.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        var reason = task.FailureReason.ShouldNotBeNull();
        reason.ShouldContain("IX_Agents_Slug");
        reason.ShouldContain("23505");
        reason.ShouldNotContain(schema.ConnectionString);
        reason.ShouldNotContain("Password", Case.Insensitive);
        task.AgentId.ShouldBeNull();
        task.AgentSessionId.ShouldBeNull();
        (await verify.Agents.AsNoTracking().ToListAsync()).ShouldHaveSingleItem().Id.ShouldBe(occupantId);
        (await verify.AgentSessions.CountAsync()).ShouldBe(0);
        sink.Specs.ShouldBeEmpty();
    }

    private static PhoneHomeFrame? AnswerDispatchSave(PhoneHomeFrame frame) => frame.Operation switch
    {
        PhoneHomeOperation.WorkspaceMirror => Result(frame, new PhoneHomeWorkspaceMirrorResponse(DispatchSaveMirror)),
        PhoneHomeOperation.ProviderAuth => Result(frame,
            new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null)),
        _ => null,
    };

    /// <summary>One claim on its own scope and DbContext: the dispatcher's row lock and token test, no tick sweeps.</summary>
    private static async Task<AgentTaskDispatcher.DispatchOneResult> ClaimAsync(IServiceProvider services, Guid taskId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
            .DispatchOneAsync(task, CancellationToken.None);
    }

    /// <summary>Answers the recorded mirror request and lets later mirror requests through.</summary>
    private static async Task ReleaseMirrorAsync(PhoneHomeScriptedPeer peer, PhoneHomeFrame? held)
    {
        peer.Speak(PhoneHomeOperation.WorkspaceMirror);
        if (held is not null)
            await peer.EmitAsync(Result(held, new PhoneHomeWorkspaceMirrorResponse(DispatchSaveMirror)));
    }
}
