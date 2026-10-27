using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
/// Remote preparation commits while the task is still queued. That commit must keep the
/// capacity grant. A later process, and the launch itself, together spend one admission.
/// The launched brief is accepted only as a runtime UserPrompt.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class CapacityAdmissionTimingTests
{
    private const string RunnerId = "runner-b095";

    [Test]
    [Arguments("after-receipt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", false)]
    [Timeout(180_000)]
    public async Task Preparation_restart_and_dispatch_spend_one_admission(
        string cut, bool busy, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var workspace = Directory.CreateTempSubdirectory("antiphon-admission-timing").FullName;
        try
        {
            var taskId = Guid.NewGuid();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId,
                    RootTaskId = taskId,
                    Title = "remote admission " + cut,
                    Goal = "remote admission " + cut + (busy ? " busy" : ""),
                    Kind = AgentTaskKind.Worker,
                    Role = AgentTaskRole.Custom,
                    AgentKind = AgentKind.ClaudeCode,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Shared,
                    WorkingDirectory = workspace,
                    Ephemeral = true,
                    RunnerId = RunnerId,
                    Status = AgentTaskStatus.Queued,
                    ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = DateTime.UtcNow,
                    ConcurrencyToken = Guid.NewGuid(),
                });
                await db.SaveChangesAsync(ct);
            }

            var (capacity, _, capacityProvider) = CapacityRecoveryTestSupport.CreateService(
                schema, intervalSeconds: 1, jitterSeconds: 0);
            await using var capacityScope = capacityProvider;
            var wait = await capacity.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"task:{taskId:N}", CapacityWaitConsumerKind.QueuedTask,
                executionKind: AgentKind.ClaudeCode, alias: "fable", taskId: taskId, holdAlreadyCleared: true), ct);
            (await capacity.GrantReadyAsync(ct)).ShouldBe(1);

            await using (var preparing = CreateProvider(schema.ConnectionString))
            {
                await using var scope = preparing.CreateAsyncScope();
                var preparer = preparing.GetRequiredService<RemoteWorkspacePreparer>();
                var prepared = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
                await AssertUnspentAsync(schema.ConnectionString, taskId, wait.Id, prepared, "preparation", ct);
                await preparer.WhenIdleAsync(ct);
                await AssertUnspentAsync(schema.ConnectionString, taskId, wait.Id, prepared, "preparation-finished", ct);
            }

            await using (var restarted = CreateProvider(schema.ConnectionString))
            {
                await using var scope = restarted.CreateAsyncScope();
                var recovered = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
                await AssertUnspentAsync(schema.ConnectionString, taskId, wait.Id, recovered, "restart", ct);
            }

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.DispatchNotBeforeAt, (DateTime?)null)
                    .SetProperty(t => t.RemoteWorktreePath, "/tmp/antiphon-admission-mirror"), ct);
            }

            AgentTaskDispatcher.TickResult launched;
            await using (var dispatching = CreateProvider(schema.ConnectionString))
            {
                await using var scope = dispatching.CreateAsyncScope();
                launched = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            }

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason);
            launched.Failures.ShouldBe(0, stored.FailureReason);
            var sessionId = stored.AgentSessionId.ShouldNotBeNull();
            var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
            admitted.AdmissionCount.ShouldBe(1);
            admitted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
            admitted.LaunchSessionId.ShouldBe(sessionId);
            (await verify.CapacityRecoveryProviderStates.AsNoTracking()
                .SingleAsync(s => s.Kind == admitted.ExecutionKind, ct)).GrantedWaitId.ShouldBeNull();
            (await verify.AgentSessions.CountAsync(s => s.Id == sessionId, ct)).ShouldBe(1);

            var queued = await verify.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.ExecutionTaskId == taskId, ct);
            queued.AgentSessionId.ShouldBe(sessionId);
            queued.Status.ShouldBe(QueuedMessageStatus.Pending);
            queued.DeliveryAttempts.ShouldBe(0);

            await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                PreserveDatabaseOnDispose = true,
                ConnectionString = schema.ConnectionString,
                Delegation = new Antiphon.Server.Application.Settings.DelegationSettings
                {
                    AllowedRoots = [workspace, "/"],
                },
            });
            await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
                schema.ConnectionString, harness, queued, sessionId, busy, cut);

            await using var accepted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var prompts = await accepted.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
                .ToListAsync(ct);
            prompts.Count.ShouldBe(1);
            prompts[0].AgentSessionId.ShouldBe(sessionId);
            prompts[0].Text.ShouldNotBeNullOrWhiteSpace();
            var delivered = await accepted.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id, ct);
            var startedAt = (await accepted.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct)).StartedAt;
            delivered.LastDeliveryStartedAt.ShouldNotBeNull();
            delivered.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(startedAt));
            PromptSubmissionMatch.IsCompleteIn(queued.Body, prompts[0].Text!).ShouldBeTrue();
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { }
        }
    }

    private static async Task AssertUnspentAsync(
        string connection, Guid taskId, Guid waitId, AgentTaskDispatcher.TickResult tick, string phase,
        CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
        var wait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == waitId, ct);
        var detail = $"{phase}: status={task.Status} reason={task.FailureReason} dispatched={tick.Dispatched} "
            + $"failures={tick.Failures} skippedCapacity={tick.SkippedCapacityWait} heldOnRunner={tick.HeldOnRunner} "
            + $"admission={wait.AdmissionCount} state={wait.State}";
        task.Status.ShouldBe(AgentTaskStatus.Queued, detail);
        task.AgentSessionId.ShouldBeNull(detail);
        wait.AdmissionCount.ShouldBe(0, detail);
        wait.LaunchSessionId.ShouldBeNull(detail);
        wait.State.ShouldNotBe(CapacityRecoveryWaitState.Admitted, detail);
        wait.State.ShouldNotBe(CapacityRecoveryWaitState.StartAccepted, detail);
        wait.State.ShouldNotBe(CapacityRecoveryWaitState.Canceled, detail);
        var grant = await db.CapacityRecoveryProviderStates.AsNoTracking()
            .SingleAsync(s => s.Kind == wait.ExecutionKind, ct);
        grant.GrantedWaitId.ShouldBe(wait.Id, detail);
        grant.GrantedActionKey.ShouldBe(wait.ActionKey, detail);
    }

    private static ServiceProvider CreateProvider(string connection) =>
        CapacityRecoveryTaskTests.CreateDispatcherProvider(connection, configure: services =>
        {
            services.AddSingleton<ILandingGit, IdleGit>();
            services.AddSingleton<ISessionRunnerDirectory, ReadyDirectory>();
            services.AddSingleton<RemoteWorkspaceService>();
            services.AddSingleton<RemoteWorkspacePreparer>();
        });

    private sealed class ReadyDirectory : ISessionRunnerDirectory
    {
        private static readonly Guid StoreId = Guid.Parse("b095c160-0000-4000-8000-000000000001");
        private readonly ISessionRunnerClient _client = new IdleClient();

        public ISessionRunnerClient Local => _client;
        public IReadOnlyList<string> KnownRunnerIds { get; } = [RunnerId];
        public Guid? GetLiveStoreId(string? runnerId) => runnerId == RunnerId ? StoreId : null;
        public ISessionRunnerClient Resolve(string? runnerId) => _client;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("unused"));
    }

    private sealed class IdleClient : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
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

    private sealed class IdleGit : ILandingGit
    {
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Task.FromResult(new LandingGitResult(1, "", "idle"));
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => Task.FromResult(path);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => Task.FromResult(repository);
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LandingRegistration>>([]);
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
