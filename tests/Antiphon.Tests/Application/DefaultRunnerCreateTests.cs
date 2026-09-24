using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0659 V-1. <c>Delegation:DefaultRunnerId</c> is decided once, inside
/// <see cref="AgentTaskService.CreateAsync"/>, from the RESOLVED shape: an omitted runner on a
/// fresh runner-compatible Worktree task selects the configured runner when it is
/// dispatch-eligible, otherwise the desktop with a durable reason. <c>local</c> is an explicit
/// desktop request persisted as null. The oracle is the saved row and its Created/Warning events
/// read back through a fresh context, never stdout.
/// </summary>
[Category("Integration")]
public sealed class DefaultRunnerCreateTests
{
    [Test]
    public async Task Eligible_worktree_uses_default()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        await using var db = kit.Context();
        var service = kit.Service(db);

        foreach (var (row, request, expectedKind) in new (string, CreateAgentTaskRequest, AgentKind)[]
                 {
                     ("grok worker, omitted workspace",
                         new CreateAgentTaskRequest("c659 grok worker", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok),
                         AgentKind.Grok),
                     ("claude worker, explicit worktree",
                         new CreateAgentTaskRequest("c659 claude worker", Role: AgentTaskRole.Code,
                             AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.Worktree),
                         AgentKind.ClaudeCode),
                     ("claude orchestrator, omitted workspace",
                         new CreateAgentTaskRequest("c659 claude orchestrator", Kind: AgentTaskKind.Orchestrator,
                             Role: AgentTaskRole.Plan, AgentKind: AgentKind.ClaudeCode),
                         AgentKind.ClaudeCode),
                     ("whitespace runner is omission",
                         new CreateAgentTaskRequest("c659 blank runner", Role: AgentTaskRole.Code,
                             AgentKind: AgentKind.ClaudeCode, RunnerId: "   "),
                         AgentKind.ClaudeCode),
                 })
        {
            var created = await service.CreateAsync(request, kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.RunnerId.ShouldBe("server2", row);
            saved.Task.AgentKind.ShouldBe(expectedKind, row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, row);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued, row);
            saved.Created.ShouldContain(
                "runner source=default requested=unset default=server2 selected=server2 reason=eligible", Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
            saved.Warnings.ShouldBeEmpty(row + ": a successful selection is not a warning");
            created.Warning.ShouldBeNull(row);
        }

        kit.Directory.ResolveCalls.ShouldBe(["server2", "server2", "server2", "server2"]);
    }

    [Test]
    public async Task Unset_default_preserves_behavior()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var configured in new string?[] { null, "", "   ", "local", "LOCAL" })
        {
            var row = $"default '{configured ?? "<null>"}'";
            var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: configured);
            await using var db = kit.Context();
            var service = kit.Service(db);

            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("c659 unset " + Guid.NewGuid().ToString("N"), Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.ClaudeCode),
                kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.RunnerId.ShouldBeNull(row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, row);
            saved.Created.ShouldNotContain("runner source=", Case.Sensitive, row + ": no placement noise when no default is configured");
            saved.Warnings.ShouldBeEmpty(row);
            created.Warning.ShouldBeNull(row);
            kit.Directory.ResolveCalls.ShouldBeEmpty(row + ": an unset default never consults the runner directory");
        }
    }

    [Test]
    public async Task Explicit_local_is_persisted_as_null()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var (configured, token) in new (string?, string)[]
                 {
                     ("server2", "local"), ("server2", " Local "), (null, "LOCAL"),
                 })
        {
            var row = $"default '{configured ?? "<null>"}', runnerId '{token}'";
            var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: configured);
            await using var db = kit.Context();
            var service = kit.Service(db);

            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("c659 local " + Guid.NewGuid().ToString("N"), Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.ClaudeCode, RunnerId: token),
                kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.RunnerId.ShouldBeNull(row + ": the desktop is stored as null, never the literal sentinel");
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, row);
            saved.Created.ShouldContain(
                $"runner source=explicit-local requested=local default={configured ?? "unset"} selected=local reason=local_requested",
                Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
            saved.Warnings.ShouldBeEmpty(row);
            kit.Directory.ResolveCalls.ShouldBeEmpty(row + ": an explicit desktop request never asks the runner");
        }

        // Still an explicit desktop request for shapes the runner cannot express: it does not trip
        // the remote-only guards (Shared, Codex) and adds no warning.
        var sharedKit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        await using var sharedDb = sharedKit.Context();
        var shared = await sharedKit.Service(sharedDb).CreateAsync(
            new CreateAgentTaskRequest("c659 local shared codex", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Shared, RunnerId: "local"),
            sharedKit.Caller, CancellationToken.None);
        var sharedSaved = await sharedKit.ReadAsync(shared.Id);
        sharedSaved.Task.RunnerId.ShouldBeNull();
        sharedSaved.Task.Workspace.ShouldBe(WorkspaceMode.Shared);
        sharedSaved.Task.AgentKind.ShouldBe(AgentKind.Codex);
        sharedSaved.Created.ShouldContain("reason=local_requested", Case.Sensitive);
    }

    [Test]
    public async Task Explicit_remote_is_not_replaced_or_fallen_back()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        // The runner is known DOWN: an automatic request would fall back, an explicit one must not.
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2", eligible: false);
        await using var db = kit.Context();
        var service = kit.Service(db);

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest("c659 explicit remote", Role: AgentTaskRole.Code,
                AgentKind: AgentKind.ClaudeCode, RunnerId: " server2 "),
            kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBe("server2", "an explicit runner is operator intent and is admitted while offline");
        saved.Created.ShouldContain(
            "runner source=explicit requested=server2 default=server2 selected=server2 reason=requested", Case.Sensitive);
        DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1);
        saved.Warnings.ShouldBeEmpty();
        kit.Directory.ResolveCalls.ShouldBeEmpty("explicit placement does not ask the readiness gate");

        // A typo is never a fallback, and never replaced by the default.
        var before = await kit.TaskCountAsync();
        foreach (var typo in new[] { "server3", "Server2" })
        {
            var refused = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest("c659 typo " + typo, Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.ClaudeCode, RunnerId: typo),
                kit.Caller, CancellationToken.None));
            refused.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.RunnerId), typo);
        }

        // Out-of-contract identifiers refuse before anything else.
        foreach (var bad in new[] { new string('r', 65), "server\u00072" })
        {
            var refused = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest("c659 bad id", Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.ClaudeCode, RunnerId: bad),
                kit.Caller, CancellationToken.None));
            refused.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.RunnerId));
        }

        (await kit.TaskCountAsync()).ShouldBe(before, "a refused explicit runner inserts nothing");
    }

    [Test]
    public async Task Excluded_shapes_keep_existing_placement()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        var standing = await kit.SeedStandingAgentAsync();
        await using var db = kit.Context();
        var service = kit.Service(db);

        // A prior task on the standing agent (a live process to follow up on), and a prior
        // Shared task whose agent is gone (a retired follow-up that degrades to a fresh Worktree).
        var livePrior = await service.CreateAsync(
            new CreateAgentTaskRequest("c659 live prior", Role: AgentTaskRole.Code, AgentId: standing.Id),
            kit.Caller, CancellationToken.None);
        var retiredPrior = await service.CreateAsync(
            new CreateAgentTaskRequest("c659 retired prior", Role: AgentTaskRole.Code,
                AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.Shared),
            kit.Caller, CancellationToken.None);

        foreach (var (row, request, reason) in new (string, CreateAgentTaskRequest, string)[]
                 {
                     ("codex", new CreateAgentTaskRequest("c659 codex", Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex),
                         "kind_not_supported"),
                     ("shared", new CreateAgentTaskRequest("c659 shared", Role: AgentTaskRole.Code,
                         AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.Shared), "workspace_not_worktree"),
                     ("readonly", new CreateAgentTaskRequest("c659 readonly", Role: AgentTaskRole.Review,
                         AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.ReadOnly), "workspace_not_worktree"),
                     ("agentId", new CreateAgentTaskRequest("c659 agent id", Role: AgentTaskRole.Code,
                         AgentId: standing.Id), "existing_process"),
                     ("agent name", new CreateAgentTaskRequest("c659 agent name", Role: AgentTaskRole.Code)
                         { Agent = standing.Name }, "existing_process"),
                     ("live follow-up", new CreateAgentTaskRequest("c659 live follow", Role: AgentTaskRole.Code,
                         FollowUpOnTask: livePrior.Id.ToString("D")), "existing_process"),
                     ("retired follow-up", new CreateAgentTaskRequest("c659 retired follow", Role: AgentTaskRole.Code,
                         FollowUpOnTask: retiredPrior.Id.ToString("D")), "existing_process"),
                 })
        {
            var created = await service.CreateAsync(request, kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.RunnerId.ShouldBeNull(row);
            saved.Created.ShouldContain(
                $"runner source=default requested=unset default=server2 selected=local reason={reason}", Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
            saved.Warnings.ShouldNotContain(w => w.Contains("runner", StringComparison.OrdinalIgnoreCase),
                row + ": an excluded shape is not a warning");
            if (row == "retired follow-up")
                saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, "the retired follow-up really did degrade to a fresh Worktree");
        }

        kit.Directory.ResolveCalls.ShouldBeEmpty("an excluded shape is decided before the readiness gate");
    }

    [Test]
    public async Task Fallback_is_durable_and_explained()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var (row, kit, reason) in new (string, DefaultRunnerKit, string)[]
                 {
                     ("runner not dispatch-eligible",
                         DefaultRunnerKit.Create(schema.ConnectionString, "server2", eligible: false), "runner_not_dispatch_eligible"),
                     ("unknown configured runner",
                         DefaultRunnerKit.Create(schema.ConnectionString, "server9"), "default_runner_not_enabled"),
                     ("phone-home disabled",
                         DefaultRunnerKit.Create(schema.ConnectionString, "server2", phoneHomeEnabled: false), "default_runner_not_enabled"),
                     ("delegated tasks disabled",
                         DefaultRunnerKit.Create(schema.ConnectionString, "server2", delegatedTasks: false), "runner_tasks_disabled"),
                     ("no runner directory",
                         DefaultRunnerKit.Create(schema.ConnectionString, "server2", withDirectory: false), "runner_directory_unavailable"),
                 })
        {
            await using var db = kit.Context();
            var created = await kit.Service(db).CreateAsync(
                new CreateAgentTaskRequest("c659 fallback " + Guid.NewGuid().ToString("N"), Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.ClaudeCode),
                kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.RunnerId.ShouldBeNull(row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, row);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued, row);
            var expected = $"runner source=default requested=unset default={(row == "unknown configured runner" ? "server9" : "server2")} selected=local reason={reason}";
            saved.Created.ShouldContain(expected, Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
            saved.Warnings.Count(w => w.Contains(reason, StringComparison.Ordinal)).ShouldBe(1, row + ": exactly one Warning event");
            created.Warning.ShouldNotBeNull(row).ShouldContain(reason, Case.Sensitive, row);
        }

        // Only the directory's typed unavailable refusal is a fallback: an arbitrary fault is not
        // permission to run the work on the desktop, and it inserts nothing.
        var faulty = DefaultRunnerKit.Create(schema.ConnectionString, "server2", fault: new InvalidOperationException("boom"));
        await using var faultyDb = faulty.Context();
        var before = await faulty.TaskCountAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => faulty.Service(faultyDb).CreateAsync(
            new CreateAgentTaskRequest("c659 faulty directory", Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode),
            faulty.Caller, CancellationToken.None));
        (await faulty.TaskCountAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Provider_refusal_does_not_fallback()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, "server2", grokProbe: true,
            grokAuth: new RunnerProviderAuthDto("grok", false, null, null, DateTimeOffset.UtcNow, null));
        await using var db = kit.Context();
        var before = await kit.TaskCountAsync();

        var ex = await Should.ThrowAsync<ProviderSignInRequiredException>(() => kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest("c659 logged-out runner", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok),
            kit.Caller, CancellationToken.None));

        ex.RunnerId.ShouldBe("server2", "the SELECTED runner's store was asked, and its refusal stands");
        kit.Directory.Client.Providers.ShouldBe(["grok"]);
        (await kit.TaskCountAsync()).ShouldBe(before, "a refusal after selection inserts nothing and never re-routes to the desktop");
    }
}

/// <summary>CARD-0659 test kit: a real <see cref="AgentTaskService"/> with a controlled runner directory.</summary>
internal sealed class DefaultRunnerKit
{
    public required string ConnectionString { get; init; }
    public required string RepoRoot { get; init; }
    public required DelegationSettings Settings { get; init; }
    public required PhoneHomeRunnerSettings PhoneHome { get; init; }
    public required FakeRunnerDirectory Directory { get; init; }
    public bool WithDirectory { get; init; } = true;
    public AgentRegistrySettings? Registry { get; init; }
    public ISessionRunnerDirectory? RealDirectory { get; init; }

    public AgentTaskService.Caller Caller => new(null, null, RepoRoot);

    public static DefaultRunnerKit Create(
        string connectionString,
        string? defaultRunnerId,
        bool eligible = true,
        bool phoneHomeEnabled = true,
        bool delegatedTasks = true,
        bool withDirectory = true,
        Exception? fault = null,
        bool grokProbe = false,
        RunnerProviderAuthDto? grokAuth = null,
        string allowedRunnerId = "server2",
        ISessionRunnerDirectory? realDirectory = null)
    {
        var repoRoot = FindRepoRoot();
        AgentRegistrySettings? registry = null;
        if (grokProbe)
        {
            registry = new AgentRegistrySettings { GrokCredentialProbeEnabled = true };
            registry.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
        }

        return new DefaultRunnerKit
        {
            ConnectionString = connectionString,
            RepoRoot = repoRoot,
            Settings = new DelegationSettings { AllowedRoots = [repoRoot], DefaultRunnerId = defaultRunnerId },
            PhoneHome = new PhoneHomeRunnerSettings
            {
                Enabled = phoneHomeEnabled,
                AllowedRunnerId = allowedRunnerId,
                AllowDelegatedTasks = delegatedTasks,
                HostWorkspaceRoot = repoRoot,
                CallbackOrigin = "https://antiphon.test",
                SharedSecret = "x",
                ChildGrokHome = "/state/grok",
            },
            Directory = new FakeRunnerDirectory(eligible, fault, grokAuth),
            WithDirectory = withDirectory,
            Registry = registry,
            RealDirectory = realDirectory,
        };
    }

    public AppDbContext Context() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));

    public AgentTaskService Service(AppDbContext db) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(Settings),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance,
        registrySettings: Registry is null ? null : Options.Create(Registry),
        phoneHome: new PhoneHomeLaunchPolicy(Options.Create(PhoneHome)),
        runners: RealDirectory ?? (WithDirectory ? Directory : null));

    public sealed record Saved(AgentTask Task, string Created, IReadOnlyList<string> Warnings);

    /// <summary>Reads the row and its events through a fresh context: what was persisted, not what is tracked.</summary>
    public async Task<Saved> ReadAsync(Guid taskId)
    {
        await using var db = Context();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var events = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId)
            .ToListAsync();
        var created = events.Single(e => e.Type == AgentTaskEventType.Created).Detail ?? "";
        var warnings = events.Where(e => e.Type == AgentTaskEventType.Warning).Select(e => e.Detail ?? "").ToList();
        return new Saved(task, created, warnings);
    }

    public async Task<int> TaskCountAsync()
    {
        await using var db = Context();
        return await db.AgentTasks.CountAsync();
    }

    public async Task<Agent> SeedStandingAgentAsync()
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "c659-seat-" + Guid.NewGuid().ToString("N")[..8],
            WorkingDirectory = RepoRoot,
            Details = "CARD-0659 standing seat",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddMinutes(-30),
            UpdatedAt = now,
        };
        agent.Slug = agent.Name;
        await using var db = Context();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = RepoRoot,
            Cols = 120,
            Rows = 30,
            CreatedAt = now.AddMinutes(-30),
            StartedAt = now.AddMinutes(-30),
            LastSeenAt = now,
        });
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    public static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    /// <summary>
    /// A directory whose readiness answer is controlled: eligible returns the client, not eligible
    /// throws the same typed refusal <c>PhoneHomeRunnerDirectory.Resolve</c> does.
    /// </summary>
    internal sealed class FakeRunnerDirectory(bool eligible, Exception? fault, RunnerProviderAuthDto? grokAuth)
        : ISessionRunnerDirectory
    {
        public List<string?> ResolveCalls { get; } = [];
        public FakeRunnerClient Client { get; } = new(grokAuth);
        public ISessionRunnerClient Local => Client;
        public IReadOnlyList<string> KnownRunnerIds => ["local", "server2"];
        public Guid? LiveStoreId => Guid.NewGuid();

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            ResolveCalls.Add(runnerId);
            if (fault is not null)
                throw fault;
            if (!eligible)
                throw new ServiceUnavailableException("Phone-home runner has not completed recovery.", PhoneHomeProblemTypes.Unavailable);
            return Client;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new InvalidOperationException("CARD-0659 D-3: placement must not use inventory.");
    }

    internal sealed class FakeRunnerClient(RunnerProviderAuthDto? answer) : ISessionRunnerClient
    {
        public List<string> Providers { get; } = [];
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct)
        {
            Providers.Add(provider);
            return Task.FromResult(answer);
        }
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
