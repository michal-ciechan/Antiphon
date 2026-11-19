using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S3 CP-4 (D-3, D-8, D-12). A Windows runner leases only when its id is on
/// <c>Delegation:WindowsSlotRunnerIds</c> and its role is on <c>Delegation:WindowsSlotRoles</c>.
/// The server2 global role string does not admit it. Catalogue id <c>desktop</c> never leases.
/// </summary>
[Category("Integration")]
[Timeout(60_000)]
public sealed class C0824WindowsSlotAdmissionTests
{
    [Test]
    public async Task Windows_runner_stays_legacy_until_listed()
    {
        // Global roles are "*". The runner advertises workspaceSlotV1 and reports windows.
        // Defaults leave the Windows allow-list empty, so the preparation stays legacy.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.Parse("08240000-0000-4000-8000-000000000001");
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "windows slot",
            Goal = "windows slot",
            Role = AgentTaskRole.Investigate,
            RunnerId = "desktop-win",
            Attempt = 1,
            WorktreeBranch = "feat/card-task-08240000",
            CreatedAt = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();

        var service = new RemoteWorkspaceService(
            new WindowsRunnerDirectory("desktop-win"),
            new UnusedGit(),
            NullLogger<RemoteWorkspaceService>.Instance,
            settings: Options.Create(new DelegationSettings { WorkspaceSlotsEnabled = true }));
        var at = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
        var expectation = new MirrorBindingExpectation(
            taskId, 1, Guid.Parse("08240000-0000-4000-8000-0000000000aa"), "desktop-win",
            Guid.Parse("08240000-0000-4000-8000-0000000000bb").ToString("D"),
            "/work/antiphon", "refs/heads/feat/card-task-08240000", new string('a', 40));

        var decision = await service.ResolveSlotDecisionAsync(db, task, expectation, at, CancellationToken.None);

        decision.ShouldBe(new WorkspaceSlotDecision(false, "windows_roles_none", Recorded: true, At: at));
    }

    private sealed class WindowsRunnerDirectory(string runnerId) : ISessionRunnerDirectory
    {
        private readonly FakeSessionRunnerClient _local = new();

        public ISessionRunnerClient Local => _local;

        public ISessionRunnerClient Resolve(string? id) => _local;

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);

        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);

        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable(RunnerInventoryReasons.PhoneHomeUnavailable));

        public IReadOnlyList<string> KnownRunnerIds => [runnerId];

        public Guid? GetLiveStoreId(string? id) =>
            string.Equals(id, runnerId, StringComparison.Ordinal) ? Guid.Parse("08240000-0000-4000-8000-0000000000bb") : null;

        public Task<RunnerDescriptor?> DescribeAsync(string? id, CancellationToken ct)
        {
            if (!string.Equals(id, runnerId, StringComparison.Ordinal))
                return Task.FromResult<RunnerDescriptor?>(null);
            return Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(
                runnerId, runnerId, RunnerPlatformWire.Windows, DateTimeOffset.UnixEpoch,
                true, true, false, 2,
                new RunnerCapabilitiesDto("ModernConPty", "ModernConPty", "test", false,
                    Features: [RunnerCapabilityFeatures.WorkspaceSlotV1],
                    Platform: RunnerPlatformWire.Windows)));
        }
    }

    private sealed class UnusedGit : ILandingGit
    {
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
            throw new NotSupportedException();

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
