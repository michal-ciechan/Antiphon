using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1679 D-1 (W2). The brief's liveness probe runs after the claim and the launch have
/// committed. A local runner List that times out (a <see cref="TaskCanceledException"/>) while the
/// dispatcher's own token is live is a transient transport failure: the task stays Dispatched with
/// its brief Pending. Only the dispatcher's own cancellation still propagates.
/// </summary>
public partial class CapacityAdmissionLifecycleTests
{
    [Test]
    [Arguments("remote")]
    [Arguments("local")]
    [Arguments("shutdown")]
    [Timeout(180_000)]
    public async Task C1679_Brief_probe_cancellation_does_not_fail_a_committed_dispatch(
        string target, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var task = await SeedSharedAsync(schema.ConnectionString, workspace.Path, "c1679 " + target, DateTime.UtcNow, ct);
        if (target == "remote")
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            await db.AgentTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RunnerId, RemoteRunnerId)
                .SetProperty(t => t.RemoteWorktreePath, "/tmp/antiphon-c1679-mirror"), ct);
        }

        using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var client = new FaultingListClient(target == "shutdown" ? tickCts : null);
        var sink = new CountingLaunchSink();
        await using var provider = CapacityRecoveryTaskTests.CreateDispatcherProvider(
            schema.ConnectionString,
            configure: services =>
            {
                services.AddSingleton<ISessionRunnerClient>(client);
                services.AddSingleton<IAgentTaskLaunchSink>(sink);
                services.AddSingleton(Options.Create(Roots(workspace.Path)));
                if (target == "remote")
                {
                    services.AddSingleton<ILandingGit, IdleGit>();
                    services.AddSingleton<ISessionRunnerDirectory, ReadyDirectory>();
                    services.AddSingleton<RemoteWorkspaceService>();
                    services.AddSingleton<RemoteWorkspacePreparer>();
                }
            });
        // The fault starts only once the brief row is being ensured: everything before the commit
        // sees a healthy runner, so the failure is the post-launch probe and nothing else.
        provider.GetRequiredService<SessionMessageQueueService>().BeforeDispatchBriefRowLock = _ =>
        {
            client.Armed = true;
            return Task.CompletedTask;
        };

        AgentTaskDispatcher.TickResult tick;
        await using (var scope = provider.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            if (target == "shutdown")
            {
                await Should.ThrowAsync<OperationCanceledException>(async () =>
                    await dispatcher.TickAsync(tickCts.Token));
                client.ArmedListCalls.ShouldBeGreaterThan(0, "the shutdown cut is the brief probe");
                return;
            }

            tick = await dispatcher.TickAsync(tickCts.Token);
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason);
        stored.FailureReason.ShouldBeNull();
        tick.Failures.ShouldBe(0);
        tick.Dispatched.ShouldBe(1);
        var sessionId = stored.AgentSessionId.ShouldNotBeNull();
        sink.Sessions.ShouldBe([sessionId], "one launch enqueued");
        var briefs = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.ExecutionTaskId == task.Id)
            .ToListAsync(ct);
        briefs.Count.ShouldBe(1);
        briefs[0].Status.ShouldBe(QueuedMessageStatus.Pending);
        briefs[0].AgentSessionId.ShouldBe(sessionId);
        if (target == "local")
            client.ArmedListCalls.ShouldBeGreaterThan(0, "the local probe met the canceled List");
        else
            client.ArmedListCalls.ShouldBe(0, "a remote-bound probe never asks the local runner (D-2)");
    }

    private sealed class CountingLaunchSink : IAgentTaskLaunchSink
    {
        private readonly List<Guid> _sessions = [];

        public IReadOnlyList<Guid> Sessions
        {
            get { lock (_sessions) return _sessions.ToArray(); }
        }

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            lock (_sessions) _sessions.Add(sessionId);
        }
    }

    /// <summary>
    /// The local runner during its restart: once armed, its List never answers in time, so it
    /// throws the <see cref="TaskCanceledException"/> an HttpClient or the live-read deadline throws.
    /// With a shutdown source it cancels the dispatcher's own token first.
    /// </summary>
    private sealed class FaultingListClient(CancellationTokenSource? shutdown) : ISessionRunnerClient
    {
        private int _armedListCalls;
        public volatile bool Armed;
        public int ArmedListCalls => Volatile.Read(ref _armedListCalls);

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            if (!Armed)
                return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
            Interlocked.Increment(ref _armedListCalls);
            if (shutdown is not null)
            {
                shutdown.Cancel();
                throw new OperationCanceledException(shutdown.Token);
            }
            throw new TaskCanceledException("C1679 local runner list timed out during its restart");
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
