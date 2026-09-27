using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RunnerRetireJobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Draining_runner_with_retire_when_idle_is_retired_after_the_idle_window()
    {
        await using var f = await Fixture.StartAsync(leaseSeconds: 3600);
        await f.SeedDrainAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(59));
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).IdleObservedAt.ShouldBeNull();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).IdleObservedAt.ShouldBe(T0.AddSeconds(60));
        f.Peer.Retires.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(119));
        await f.Job.RunAsync(CancellationToken.None);
        f.Peer.Retires.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Job.RunAsync(CancellationToken.None);
        f.Peer.Retires.Single().Force.ShouldBeFalse();
        var row = await f.StateAsync();
        row.RetiredAt.ShouldNotBeNull();
        row.RetireReason.ShouldBe("idle");
        f.Clock.Advance(TimeSpan.FromSeconds(120));
        await f.Job.RunAsync(CancellationToken.None);
        f.Peer.Retires.Count.ShouldBe(1);
    }

    [Test]
    public async Task Live_session_queued_task_or_busy_answer_keeps_the_runner_draining()
    {
        await using var f = await Fixture.StartAsync(leaseSeconds: 3600);
        await f.SeedDrainAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(60));
        var runnerSession = Guid.NewGuid();
        f.Peer.Sessions.Add(new RunnerSessionDto(runnerSession, 1, T0.UtcDateTime, "Running", null, "", 0));
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).IdleObservedAt.ShouldBeNull();
        f.Peer.Sessions.Clear();

        var sessionId = await f.SeedSessionAsync(SessionStatus.Starting);
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).IdleObservedAt.ShouldBeNull();
        await f.SetSessionStatusAsync(sessionId, SessionStatus.Failed);

        var taskId = await f.SeedQueuedAsync();
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).IdleObservedAt.ShouldBeNull();
        await f.SetTaskStatusAsync(taskId, AgentTaskStatus.Canceled);

        await f.Job.RunAsync(CancellationToken.None);
        f.Clock.Advance(TimeSpan.FromSeconds(120));
        f.Peer.Reply = frame => frame.Operation == PhoneHomeOperation.Retire
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: PhoneHomeProblemTypes.RunnerBusy, ErrorDetail: "one session", StatusCode: 409)
            : null;
        await f.Job.RunAsync(CancellationToken.None);
        var row = await f.StateAsync();
        row.RetiredAt.ShouldBeNull();
        row.IdleObservedAt.ShouldBeNull();
        f.Peer.Retires.Single().Force.ShouldBeFalse();
    }

    [Test]
    public async Task A_drain_without_retire_when_idle_is_never_retired()
    {
        await using var f = await Fixture.StartAsync(leaseSeconds: 3600);
        await f.SeedDrainAsync(retireWhenIdle: false);
        f.Clock.Advance(TimeSpan.FromMinutes(10));
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).RetiredAt.ShouldBeNull();
        f.Peer.Retires.ShouldBeEmpty();
        await f.UpdateStateAsync(row => { row.Draining = false; row.RetireWhenIdle = true; });
        await f.Job.RunAsync(CancellationToken.None);
        f.Peer.Retires.ShouldBeEmpty();
    }

    [Test]
    public async Task Disconnected_draining_runner_with_no_bound_rows_is_marked_retired_without_a_send()
    {
        await using var f = await Fixture.StartAsync(leaseSeconds: 90);
        await f.SeedDrainAsync();
        f.Host.Directory.Disconnect(f.Host.Directory.SnapshotLive(RollingRunnerSettings.Server2Temp)!, "transport_abort");
        f.Peer.Socket.Abort();
        f.Clock.Advance(TimeSpan.FromSeconds(89));
        await f.Job.RunAsync(CancellationToken.None);
        (await f.StateAsync()).RetiredAt.ShouldBeNull();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Job.RunAsync(CancellationToken.None);
        var row = await f.StateAsync();
        row.RetiredAt.ShouldNotBeNull();
        row.RetireReason.ShouldBe("idle_disconnected");
        f.Peer.Retires.ShouldBeEmpty();

        await using var bound = await Fixture.StartAsync(leaseSeconds: 90);
        await bound.SeedDrainAsync();
        await bound.SeedSessionAsync(SessionStatus.Running);
        bound.Host.Directory.Disconnect(bound.Host.Directory.SnapshotLive(RollingRunnerSettings.Server2Temp)!, "transport_abort");
        bound.Peer.Socket.Abort();
        bound.Clock.Advance(TimeSpan.FromSeconds(90));
        await bound.Job.RunAsync(CancellationToken.None);
        (await bound.StateAsync()).RetiredAt.ShouldBeNull();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required PhoneHomeTestHost Host { get; init; }
        public required PhoneHomeScriptedPeer Peer { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required RunnerRetireJob Job { get; init; }
        public required Guid StoreId { get; init; }

        public static async Task<Fixture> StartAsync(int leaseSeconds)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider(T0);
            var secretA = "retire-a-" + Guid.NewGuid().ToString("N");
            var secretB = "retire-b-" + Guid.NewGuid().ToString("N");
            var settings = RollingRunnerSettings.Pair(secretA, secretB, leaseSeconds);
            var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString, configured: settings);
            var storeId = Guid.NewGuid();
            var peer = await host.ConnectPeerAsync(runnerId: RollingRunnerSettings.Server2Temp, storeId: storeId, secret: secretB);
            host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: RollingRunnerSettings.Server2Temp));
            var job = new RunnerRetireJob(host.App.Services.GetRequiredService<IServiceScopeFactory>(), host.Directory,
                Options.Create(settings), clock, NullLogger<RunnerRetireJob>.Instance);
            return new Fixture { Schema = schema, Host = host, Peer = peer, Clock = clock, Job = job, StoreId = storeId };
        }

        private AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        public async Task SeedDrainAsync(bool retireWhenIdle = true)
        {
            await using var db = Db();
            var row = new SessionRunnerState
            {
                RunnerId = RollingRunnerSettings.Server2Temp, Draining = true, DrainedAt = T0,
                DrainReason = "upgrade", RedirectTo = RollingRunnerSettings.Server2,
                RetireWhenIdle = retireWhenIdle, UpdatedAt = T0,
            };
            db.SessionRunnerStates.Add(row);
            await db.SaveChangesAsync();
            Host.Directory.ApplyState(row.RunnerId, Antiphon.Server.Application.Services.RunnerStateService.ToState(row));
        }

        public async Task<SessionRunnerState> StateAsync()
        {
            await using var db = Db();
            return await db.SessionRunnerStates.AsNoTracking().SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2Temp);
        }

        public async Task UpdateStateAsync(Action<SessionRunnerState> edit)
        {
            await using var db = Db();
            var row = await db.SessionRunnerStates.SingleAsync(s => s.RunnerId == RollingRunnerSettings.Server2Temp);
            edit(row);
            await db.SaveChangesAsync();
            Host.Directory.ApplyState(row.RunnerId, Antiphon.Server.Application.Services.RunnerStateService.ToState(row));
        }

        public async Task<Guid> SeedSessionAsync(SessionStatus status)
        {
            var id = Guid.NewGuid();
            await using var db = Db();
            db.AgentSessions.Add(new AgentSession
            {
                Id = id, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
                Status = status, Cwd = "/work", Cols = 80, Rows = 24,
                CreatedAt = T0.UtcDateTime, StartedAt = T0.UtcDateTime, LastSeenAt = T0.UtcDateTime,
                RunnerId = RollingRunnerSettings.Server2Temp, RunnerStoreId = StoreId, RunnerCwd = "/work",
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task SetSessionStatusAsync(Guid id, SessionStatus status)
        {
            await using var db = Db();
            await db.AgentSessions.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(row => row.Status, status));
        }

        public async Task<Guid> SeedQueuedAsync()
        {
            var id = Guid.NewGuid();
            await using var db = Db();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "queued", Goal = "queued", Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = "/work", WorktreePath = "/work", WorktreeBranch = "feat/queued",
                RunnerId = RollingRunnerSettings.Server2Temp, Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = T0.UtcDateTime,
                RunnerSelectionSource = RunnerSelectionSource.GlobalDefault,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task SetTaskStatusAsync(Guid id, AgentTaskStatus status)
        {
            await using var db = Db();
            await db.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(t => t.SetProperty(row => row.Status, status));
        }

        public async ValueTask DisposeAsync()
        {
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }
}
