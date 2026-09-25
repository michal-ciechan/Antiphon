using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
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

    [Test]
    public async Task Any_keeps_default_and_local_fallback()
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
            "Codex stays on the desktop; other kinds inherit server2.",
            "Human"), null, CancellationToken.None);

        await using var grokDb = kit.Context();
        var grok = await kit.Service(grokDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any grok", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var grokSaved = await kit.ReadAsync(grok.Id);
        grokSaved.Task.RunnerId.ShouldBe("server2");
        grokSaved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Any);
        grokSaved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.GlobalDefault);

        await using var codexDb = kit.Context();
        var codex = await kit.Service(codexDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any codex desktop override", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var codexSaved = await kit.ReadAsync(codex.Id);
        codexSaved.Task.RunnerId.ShouldBeNull();
        codexSaved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.KindDefault);

        await using var clearDb = kit.Context();
        var cleared = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            cleared.Revision, null, [], "Clear every preference.", "Human"), null, CancellationToken.None);
        var local = await kit.Service(clearDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any with no default", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var localSaved = await kit.ReadAsync(local.Id);
        localSaved.Task.RunnerId.ShouldBeNull("a null global uses the built-in desktop fallback and does not scan other runners");
    }

    [Test]
    public async Task Unknown_platform_refuses_specific_only()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var directory = Matrix();
        directory.Rows["server2"] = Describe("server2", null, eligible: true);
        kit.RealDirectory = directory;
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "Unknown platform is still the named default.", "Human"), null, CancellationToken.None);
        var before = await kit.TaskCountAsync();

        var refused = await Should.ThrowAsync<ConflictException>(() => kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 windows on unknown",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2",
                RequiredPlatform: RequiredPlatform.Windows),
            kit.Caller, CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformProblems.Unknown);
        (await kit.TaskCountAsync()).ShouldBe(before);

        await using var anyDb = kit.Context();
        var created = await kit.Service(anyDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 any may keep an unknown host", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.ObservedPlatform.ShouldBeNull();
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Any);
    }

    [Test]
    public async Task Shapes_and_codex_worker_admission_are_preserved()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "Supported workers inherit server2.", "Human"), null, CancellationToken.None);

        await using var workerDb = kit.Context();
        var worker = await kit.Service(workerDb, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 codex linux worker",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);
        (await kit.ReadAsync(worker.Id)).Task.RunnerId.ShouldBe("server2");

        var orchestrator = await Should.ThrowAsync<ValidationException>(() => kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 codex orchestrator",
                Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Plan,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None));
        orchestrator.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.AgentKind));

        var landing = await Should.ThrowAsync<ConflictException>(() => kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 codex sourcelanding",
                Role: AgentTaskRole.Mutation,
                AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux,
                SourceLandingOperationId: Guid.NewGuid()),
            kit.Caller, CancellationToken.None));
        landing.Code.ShouldBe(RunnerPlatformProblems.Unavailable);
    }

    [Test]
    public async Task Full_runner_is_selected_and_queued()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var directory = Matrix();
        directory.Rows["server2"] = Describe("server2", "linux", eligible: true, capacity: 0);
        directory.Rows["runner-b"] = Describe("runner-b", "linux", eligible: true, capacity: 4);
        kit.RealDirectory = directory;
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "server2", [], "A full default still wins.", "Human"), null, CancellationToken.None);

        var created = await kit.Service(db, defaults).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 full runner queues",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Existing_process_is_never_relocated()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = Matrix();
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var priorId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = kit.Context())
        {
            seed.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = kit.RepoRoot,
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = "server2",
            });
            seed.Agents.Add(new Agent
            {
                Id = agentId,
                Name = "c710-seat",
                Slug = "c710-seat",
                WorkingDirectory = kit.RepoRoot,
                Details = "existing remote process",
                Status = AgentStatus.Idle,
                Kind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Medium,
                PersistentSessionId = sessionId.ToString("D"),
                RunnerId = "server2",
                CreatedAt = now,
                UpdatedAt = now,
            });
            seed.AgentTasks.Add(new AgentTask
            {
                Id = priorId,
                RootTaskId = priorId,
                Title = "prior",
                Goal = "prior goal",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = kit.RepoRoot,
                RunnerId = "server2",
                RequiredPlatform = RequiredPlatform.Linux,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now,
                CompletedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(new PutRunnerDefaultsRequest(
            current.Revision, "desktop", [], "A later desktop preference must not move the live process.", "Human"), null, CancellationToken.None);

        await using var followDb = kit.Context();
        var created = await kit.Service(followDb, defaults).CreateAsync(
            new CreateAgentTaskRequest("c710 follow the live process", FollowUpOnTask: priorId.ToString("D")),
            kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2");
        saved.Task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        saved.Task.RequirementSource.ShouldBe(RequirementSource.FollowUp);
        saved.Task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.ExistingProcess);
        saved.Task.AgentId.ShouldBe(agentId);
    }

    private static MatrixDirectory Matrix()
    {
        var directory = new MatrixDirectory();
        directory.Rows["desktop"] = Describe("desktop", "windows", eligible: true);
        directory.Rows["server2"] = Describe("server2", "linux", eligible: true);
        return directory;
    }

    private static RunnerDescriptor Describe(string id, string? platform, bool eligible, int? capacity = 4) => new(
        id, id, platform, platform is null ? null : DateTimeOffset.UtcNow, eligible, eligible, !eligible, capacity,
        platform is null ? null : new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
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
