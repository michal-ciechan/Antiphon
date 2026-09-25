using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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

[Category("Integration")]
public sealed class PhoneHomeTaskCreateTests
{
    [Test]
    public async Task Runner_bound_create_admits_claude_and_explicit_codex()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var repoRoot = RepoRoot();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var service = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { AllowedRoots = [repoRoot] }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            phoneHome: new PhoneHomeLaunchPolicy(Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true, AllowedRunnerId = "server2", AllowDelegatedTasks = true,
                HostWorkspaceRoot = repoRoot, CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "x",
            })));
        var caller = new AgentTaskService.Caller(null, null, repoRoot);
        var request = new CreateAgentTaskRequest("do remote work", Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode,
            Workspace: WorkspaceMode.Worktree, RunnerId: "server2");

        var created = await service.CreateAsync(request, caller, CancellationToken.None);
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        stored.RunnerId.ShouldBe("server2");
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);

        // CARD-0660: an explicitly named runner now also takes a Codex Worker.
        var codex = await service.CreateAsync(request with { Goal = "run Codex", AgentKind = AgentKind.Codex },
            caller, CancellationToken.None);
        var storedCodex = await db.AgentTasks.SingleAsync(t => t.Id == codex.Id);
        storedCodex.RunnerId.ShouldBe("server2");
        storedCodex.AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task Runner_bound_grok_create_refuses_a_logged_out_runner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var repoRoot = RepoRoot();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var probe = new ProbeDirectory(new RunnerProviderAuthDto("grok", false, null, null, DateTimeOffset.UtcNow, null));
        var service = Service(db, repoRoot, probe);

        var ex = await Should.ThrowAsync<ProviderSignInRequiredException>(() =>
            service.CreateAsync(new CreateAgentTaskRequest("do remote grok", Kind: AgentTaskKind.Worker,
                Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree, RunnerId: "server2"),
                new AgentTaskService.Caller(null, null, repoRoot), CancellationToken.None));

        ex.Code.ShouldBe("provider_sign_in_required");
        ex.GrokHome.ShouldBe("/state/grok");
        ex.RunnerId.ShouldBe("server2");
        ex.Message.ShouldContain("grok login");
        ex.Message.ShouldNotContain("Windows user");
        probe.Client.Providers.ShouldBe(["grok"]);
        (await db.AgentTasks.CountAsync(t => t.Goal == "do remote grok")).ShouldBe(0);
    }

    [Test]
    public async Task AllowUnauthenticatedProvider_does_not_probe_the_runner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var repoRoot = RepoRoot();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var probe = new ProbeDirectory(new RunnerProviderAuthDto("grok", false, null, null, DateTimeOffset.UtcNow, null));
        var service = Service(db, repoRoot, probe);

        var created = await service.CreateAsync(new CreateAgentTaskRequest("queue remote grok", Kind: AgentTaskKind.Worker,
            Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, RunnerId: "server2")
        {
            AllowUnauthenticatedProvider = true,
        }, new AgentTaskService.Caller(null, null, repoRoot), CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        probe.Client.Providers.ShouldBeEmpty();
    }

    private static AgentTaskService Service(AppDbContext db, string repoRoot, ProbeDirectory probe)
    {
        var registry = new AgentRegistrySettings { GrokCredentialProbeEnabled = true };
        registry.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { AllowedRoots = [repoRoot] }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            registrySettings: Options.Create(registry),
            phoneHome: new PhoneHomeLaunchPolicy(Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true, AllowedRunnerId = "server2", AllowDelegatedTasks = true,
                HostWorkspaceRoot = repoRoot, CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "x", ChildGrokHome = "/state/grok",
            })),
            runners: probe);
    }

    private sealed class ProbeDirectory(RunnerProviderAuthDto? answer) : ISessionRunnerDirectory
    {
        public ProbeClient Client { get; } = new(answer);
        public ISessionRunnerClient Local => Client;
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? GetLiveStoreId(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? null : Guid.NewGuid();
        public ISessionRunnerClient Resolve(string? runnerId) => Client;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("test"));
    }

    private sealed class ProbeClient(RunnerProviderAuthDto? answer) : ISessionRunnerClient
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
