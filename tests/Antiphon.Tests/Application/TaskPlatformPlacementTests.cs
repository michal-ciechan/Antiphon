using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-4. Placement reads the runtime snapshot, filters a hard platform before a
/// preference can win, and does not insert a task when the named runner disagrees.
/// </summary>
[Category("Integration")]
public sealed class TaskPlatformPlacementTests
{
    [Test]
    public async Task Specific_platform_filters_candidates_in_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2",
            [new PutRunnerKindDefault(AgentKind.Codex, "desktop")],
            "Codex prefers the desktop; Linux must not follow it.",
            "Human"), null, CancellationToken.None);
        await using var createDb = kit.Context();

        var created = await kit.Service(createDb, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 linux past desktop kind default",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Explicit_mismatch_has_no_side_effects()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var before = await kit.TaskCountAsync();

        var refused = await Should.ThrowAsync<ConflictException>(() => kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 windows on server2",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2",
                RequiredPlatform: RequiredPlatform.Windows),
            kit.Caller, CancellationToken.None));

        refused.Code.ShouldBe(RunnerPlatformProblems.Mismatch);
        (await kit.TaskCountAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Codex_worker_inherits_global_server2()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2", allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        await defaults.EnsureInitializedAsync(CancellationToken.None);
        await using var createDb = kit.Context();

        var created = await kit.Service(createDb, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 any codex inherits server2",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    private static MatrixDirectory Matrix()
    {
        var directory = new MatrixDirectory();
        directory.Rows["desktop"] = Describe("desktop", "windows", eligible: true);
        directory.Rows["server2"] = Describe("server2", "linux", eligible: true);
        return directory;
    }

    private static RunnerDescriptor Describe(string id, string platform, bool eligible) => new(
        id, id, platform, DateTimeOffset.UtcNow, eligible, eligible, !eligible, 4,
        new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
            Features: [RunnerPlatformWire.Feature], Platform: platform));

    private sealed class MatrixDirectory : ISessionRunnerDirectory
    {
        public Dictionary<string, RunnerDescriptor> Rows { get; } = new(StringComparer.Ordinal);
        public ISessionRunnerClient Local { get; } = new StubClient();
        public IReadOnlyList<string> KnownRunnerIds => Rows.Keys.ToList();
        public Guid? GetLiveStoreId(string? runnerId) =>
            string.IsNullOrWhiteSpace(runnerId) ? null : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var key = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId)
                ? "desktop" : runnerId.Trim();
            return Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);
        }

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            if (runnerId is null || !Rows.TryGetValue(runnerId, out var row) || !row.DispatchEligible)
                throw new ServiceUnavailableException("Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
            return Local;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new InvalidOperationException("placement must not list inventory");

        private sealed class StubClient : ISessionRunnerClient
        {
            public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
            public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
            public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
                throw new NotSupportedException();
            public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();
            private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }
}
