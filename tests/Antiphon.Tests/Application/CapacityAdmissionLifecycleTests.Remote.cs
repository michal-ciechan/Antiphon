using System.Reflection;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class CapacityAdmissionLifecycleTests
{
    private const string RemoteRunnerId = "runner-b095";

    [Test]
    [Arguments("after-receipt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", false)]
    [Arguments("grant-expired-during-prep", false)]
    [Arguments("grant-expired-tick-first", false)]
    [Timeout(180_000)]
    public async Task C1593_preparation_restart_and_dispatch_consume_one_admission(
        string cut, bool busy, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var workspace = Directory.CreateTempSubdirectory("antiphon-c1593-remote").FullName;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var clock = new FakeTimeProvider(new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero));
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
                    RunnerId = RemoteRunnerId,
                    Status = AgentTaskStatus.Queued,
                    ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                    ConcurrencyToken = Guid.NewGuid(),
                });
                await db.SaveChangesAsync(ct);
            }

            ServiceProvider Create() => CapacityRecoveryTaskTests.CreateDispatcherProvider(
                schema.ConnectionString, timeProvider: clock, configure: services =>
                {
                    services.AddSingleton<ILandingGit, IdleGit>();
                    services.AddSingleton<ISessionRunnerDirectory, ReadyDirectory>();
                    services.AddSingleton<RemoteWorkspaceService>();
                    services.AddSingleton<RemoteWorkspacePreparer>();
                    services.AddSingleton(OptionsFor(workspace));
                });

            await using var preparing = Create();
            var preparer = preparing.GetRequiredService<RemoteWorkspacePreparer>();
            var wait = await GrantAsync(preparing, taskId, ct, alias: "fable");
            var ordinal = wait.ActionOrdinal;
            var actionKey = wait.ActionKey;
            await using (var scope = preparing.CreateAsyncScope())
            {
                var prepared = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
                await AssertUnspentAsync(schema.ConnectionString, taskId, wait.Id, prepared, "preparation", ct);
                await preparer.WhenIdleAsync(ct);
                await AssertUnspentAsync(schema.ConnectionString, taskId, wait.Id, prepared, "preparation-finished", ct);
            }

            await using (var restarted = Create())
            {
                await using var scope = restarted.CreateAsyncScope();
                var recovered = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
                await AssertUnspentAsync(schema.ConnectionString, taskId, wait.Id, recovered, "restart", ct);
            }

            if (cut == "grant-expired-during-prep")
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                var recovery = preparing.GetRequiredService<CapacityRecoveryService>();
                await recovery.GrantReadyAsync(ct);
                await using var revoked = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                var row = await revoked.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
                row.AdmissionCount.ShouldBe(0);
                row.ActionOrdinal.ShouldBe(ordinal + 1);
                row.ActionKey.ShouldNotBe(actionKey);
                row.OutcomeReason.ShouldBe("grant-expired");
                (await revoked.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(ct)).GrantedWaitId.ShouldBeNull();

                await using var heldProvider = Create();
                await using var heldScope = heldProvider.CreateAsyncScope();
                var held = await heldScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
                held.Dispatched.ShouldBe(0);
                await using var still = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                (await still.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct)).Status
                    .ShouldBe(AgentTaskStatus.Queued);

                var due = (await still.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct)).DueAt;
                due.ShouldNotBeNull();
                clock.Advance(due.Value - clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(1));
                (await recovery.GrantReadyAsync(ct)).ShouldBe(1);
            }
            else if (cut == "grant-expired-tick-first")
            {
                clock.Advance(TimeSpan.FromSeconds(3));
            }

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.DispatchNotBeforeAt, (DateTime?)null)
                    .SetProperty(t => t.RemoteWorktreePath, "/tmp/antiphon-admission-mirror"), ct);
            }

            AgentTaskDispatcher.TickResult launched;
            await using (var dispatching = Create())
            {
                await using var scope = dispatching.CreateAsyncScope();
                launched = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            }

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            stored.Status.ShouldBe(AgentTaskStatus.Dispatched, stored.FailureReason);
            launched.Failures.ShouldBe(0, stored.FailureReason);
            launched.Dispatched.ShouldBe(1);
            var sessionId = stored.AgentSessionId.ShouldNotBeNull();
            var admitted = await verify.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
            admitted.AdmissionCount.ShouldBe(1);
            admitted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
            admitted.LaunchSessionId.ShouldBe(sessionId);
            if (cut == "grant-expired-tick-first")
                admitted.ActionOrdinal.ShouldBe(ordinal);
            (await verify.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(ct)).GrantedWaitId.ShouldBeNull();

            if (cut == "grant-expired-during-prep")
            {
                var started = preparer.GetType().GetProperty(
                    "StartedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (started?.GetValue(preparer) is not int count)
                    throw new InvalidOperationException("RemoteWorkspacePreparer.StartedCount is missing");
                count.ShouldBe(1, "the recorded mirror is reused");
            }

            var queued = await verify.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(m => m.ExecutionTaskId == taskId, ct);
            var settings = new DelegationSettings { AllowedRoots = [workspace, "/"] };
            await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                PreserveDatabaseOnDispose = true,
                ConnectionString = schema.ConnectionString,
                Delegation = settings,
                TimeProvider = clock,
            });
            await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
                schema.ConnectionString, harness, queued, sessionId, busy, cut);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { }
        }
    }

    private static Microsoft.Extensions.Options.IOptions<DelegationSettings> OptionsFor(string workspace) =>
        Microsoft.Extensions.Options.Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 6,
            AllowedRoots = [workspace, "/"],
        });

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

    private sealed class ReadyDirectory : ISessionRunnerDirectory
    {
        private static readonly Guid StoreId = Guid.Parse("b095c160-0000-4000-8000-000000000001");
        private readonly ISessionRunnerClient _client = new IdleClient();
        public ISessionRunnerClient Local => _client;
        public IReadOnlyList<string> KnownRunnerIds { get; } = [RemoteRunnerId];
        public Guid? GetLiveStoreId(string? runnerId) => runnerId == RemoteRunnerId ? StoreId : null;
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
        public Task<LandingGitResult> RunOwnedAsync(
            string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started,
            CancellationToken ct) =>
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
        public Task<LandingRemoteObservation> ObserveAsync(
            string repository, LandingDestination destination, string sourceSha, string observationRef,
            CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(
            string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(
            string repository, LandingDestination destination, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(
            string repository, LandingDestination destination, string sha,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
