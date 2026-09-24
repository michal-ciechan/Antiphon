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
/// CARD-0660 V-5 (D-7/D-9/D-10). An explicit <c>-Runner</c> Codex create is admitted, and asks THAT
/// runner whether its Codex home has a login. Only a definite, Codex-attributed "no" refuses, with a
/// Codex-shaped 409 and nothing inserted; unknown, unavailable, timed-out and disabled probes admit,
/// and the caller's own cancellation propagates. A local Codex create never asks any store, and the
/// default placement still keeps Codex on the desktop until S7.
/// </summary>
[Category("Integration")]
public sealed class CodexPhoneHomeCreateTests
{
    private const string Runner = "server2";
    private const string SecretSentinel = "sentinel-token-7c1e";

    [Test]
    public async Task Signed_out_remote_create_refuses_with_codex_problem_details()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = Kit.Create(schema, (_, _) => Task.FromResult<RunnerProviderAuthDto?>(
            new RunnerProviderAuthDto("codex", false, null, null, DateTimeOffset.UtcNow, SecretSentinel)));
        await using var db = kit.Context();

        var ex = await Should.ThrowAsync<ProviderSignInRequiredException>(() =>
            kit.Service(db).CreateAsync(Remote("c660 signed out"), kit.Caller, CancellationToken.None));

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("provider_sign_in_required");
        ex.AgentKind.ShouldBe("Codex");
        ex.CodexHome.ShouldBe("/state/codex");
        ex.GrokHome.ShouldBeNull();
        ex.RunnerId.ShouldBe(Runner);
        var extensions = ex.Extensions.ShouldNotBeNull();
        extensions["agentKind"].ShouldBe("Codex");
        extensions["codexHome"].ShouldBe("/state/codex");
        extensions["runnerId"].ShouldBe(Runner);
        extensions["remedy"].ShouldBe("codex login --device-auth");
        extensions.ContainsKey("grokHome").ShouldBeFalse();
        ex.Message.ShouldContain("codex login --device-auth");
        ex.Message.ShouldContain("CODEX_HOME=/state/codex");
        ex.Message.ShouldContain(Runner);
        ex.Message.ShouldNotContain("grok", Case.Insensitive);
        ex.Message.ShouldNotContain(SecretSentinel);
        extensions.Values.ShouldAllBe(v => v == null || !v.ToString()!.Contains(SecretSentinel));

        kit.Client.Calls.ShouldBe([(Runner, "codex")]);
        await using var verify = kit.Context();
        (await verify.AgentTasks.CountAsync()).ShouldBe(0, "a refused create inserts nothing");
    }

    [Test]
    public async Task Present_unknown_and_unavailable_probe_admit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var cases = new (string Name, Func<string, CancellationToken, Task<RunnerProviderAuthDto?>> Probe)[]
        {
            ("present", (p, _) => Answer(p, true)),
            ("unknown", (p, _) => Answer(p, null)),
            ("no answer", (_, _) => Task.FromResult<RunnerProviderAuthDto?>(null)),
            ("transport fault", (_, _) => throw new InvalidOperationException("runner went away")),
            ("probe budget expired", (_, _) => throw new OperationCanceledException("probe budget")),
            // A "no" attributed to a different provider is not an answer about Codex.
            ("stale other-provider answer", (_, _) => Answer("grok", false)),
        };
        foreach (var (name, probe) in cases)
        {
            var kit = Kit.Create(schema, probe);
            await using var db = kit.Context();

            var created = await kit.Service(db).CreateAsync(Remote("c660 admit " + name), kit.Caller, CancellationToken.None);

            await using var verify = kit.Context();
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            stored.RunnerId.ShouldBe(Runner, name);
            stored.AgentKind.ShouldBe(AgentKind.Codex, name);
            stored.Status.ShouldBe(AgentTaskStatus.Queued, name);
            kit.Client.Calls.ShouldBe([(Runner, "codex")], name);
        }

        // A disabled probe is never asked, even by a runner that would say "no".
        var disabled = Kit.Create(schema, (p, _) => Answer(p, false), codexProbe: false);
        await using (var db = disabled.Context())
        {
            var created = await disabled.Service(db).CreateAsync(Remote("c660 admit disabled"), disabled.Caller, CancellationToken.None);
            created.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        disabled.Client.Calls.ShouldBeEmpty();

        // The caller's own cancellation is not an "unknown": it propagates and inserts nothing.
        using var caller = new CancellationTokenSource();
        var hanging = Kit.Create(schema, async (_, ct) =>
        {
            await caller.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        });
        await using (var db = hanging.Context())
        {
            await Should.ThrowAsync<OperationCanceledException>(() =>
                hanging.Service(db).CreateAsync(Remote("c660 caller cancelled"), hanging.Caller, caller.Token));
        }
        await using var check = hanging.Context();
        (await check.AgentTasks.CountAsync(t => t.Goal == "c660 caller cancelled")).ShouldBe(0);
    }

    [Test]
    public async Task Explicit_override_only_bypasses_create_probe()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = Kit.Create(schema, (p, _) => Answer(p, false));
        Guid taskId;
        await using (var db = kit.Context())
        {
            var created = await kit.Service(db).CreateAsync(
                Remote("c660 override") with { AllowUnauthenticatedProvider = true }, kit.Caller, CancellationToken.None);
            created.Status.ShouldBe(AgentTaskStatus.Queued);
            taskId = created.Id;
        }
        kit.Client.Calls.ShouldBeEmpty("the override skips the create-time question");

        // The override is not persisted permission: a retry of the same task asks the runner again.
        await MarkFailedAsync(kit, taskId);
        await using (var db = kit.Context())
        {
            var ex = await Should.ThrowAsync<ProviderSignInRequiredException>(() =>
                kit.Service(db).RetryAsync(taskId, CancellationToken.None));
            ex.AgentKind.ShouldBe("Codex");
        }
        kit.Client.Calls.ShouldBe([(Runner, "codex")]);
    }

    [Test]
    public async Task Local_codex_never_reads_remote_or_desktop_auth()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        // A conflicting desktop state: a signed-out Grok store and an empty desktop Codex home, and a
        // runner that would refuse. None of them is Codex-on-the-desktop's business.
        var emptyHome = Directory.CreateTempSubdirectory("c660-desktop-home-");
        try
        {
            await AssertLocalCodexIsNotProbedAsync(schema, emptyHome.FullName);
        }
        finally
        {
            emptyHome.Delete(recursive: true);
        }
    }

    private static async Task AssertLocalCodexIsNotProbedAsync(IsolatedTestSchema schema, string emptyHome)
    {
        var kit = Kit.Create(schema, (p, _) => Answer(p, false), desktopHome: emptyHome);
        await using var db = kit.Context();

        var created = await kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest("c660 local codex", Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Codex, Workspace: WorkspaceMode.Worktree),
            kit.Caller, CancellationToken.None);

        await using var verify = kit.Context();
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        stored.RunnerId.ShouldBeNull();
        stored.AgentKind.ShouldBe(AgentKind.Codex);
        kit.Client.Calls.ShouldBeEmpty("a desktop Codex task never asks a runner");
        kit.Directory.ResolveCalls.ShouldBeEmpty();

        // The same holds on retry.
        await MarkFailedAsync(kit, created.Id);
        await using (var retryDb = kit.Context())
            (await kit.Service(retryDb).RetryAsync(created.Id, CancellationToken.None)).Status.ShouldBe(AgentTaskStatus.Queued);
        kit.Client.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Retry_checks_the_selected_runner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        bool? loggedIn = false;
        var kit = Kit.Create(schema, (p, _) => Answer(p, loggedIn));
        var taskId = await SeedFailedRemoteCodexAsync(kit);

        await using (var db = kit.Context())
        {
            var ex = await Should.ThrowAsync<ProviderSignInRequiredException>(() =>
                kit.Service(db).RetryAsync(taskId, CancellationToken.None));
            ex.AgentKind.ShouldBe("Codex");
            ex.RunnerId.ShouldBe(Runner);
            ex.CodexHome.ShouldBe("/state/codex");
        }
        kit.Client.Calls.ShouldBe([(Runner, "codex")], "the task's own runner is asked, about Codex");
        await using (var verify = kit.Context())
        {
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            stored.Status.ShouldBe(AgentTaskStatus.Failed, "a refused retry leaves the task as it was");
            stored.RunnerId.ShouldBe(Runner);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Retried))
                .ShouldBe(0);
        }

        loggedIn = true;
        await using (var db = kit.Context())
            (await kit.Service(db).RetryAsync(taskId, CancellationToken.None)).Status.ShouldBe(AgentTaskStatus.Queued);
        await using (var verify = kit.Context())
        {
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            stored.RunnerId.ShouldBe(Runner, "a retry never moves the task to the desktop");
            stored.AgentKind.ShouldBe(AgentKind.Codex);
        }
    }

    [Test]
    public async Task Explicit_runner_admits_codex_while_default_placement_keeps_it_local()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = Kit.Create(schema, (p, _) => Answer(p, true), defaultRunnerId: Runner);
        await using var db = kit.Context();

        var explicitRemote = await kit.Service(db).CreateAsync(Remote("c660 explicit"), kit.Caller, CancellationToken.None);
        var automatic = await kit.Service(db).CreateAsync(
            Remote("c660 automatic") with { RunnerId = null }, kit.Caller, CancellationToken.None);

        await using var verify = kit.Context();
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == explicitRemote.Id)).RunnerId.ShouldBe(Runner);
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == automatic.Id))
            .RunnerId.ShouldBeNull("CARD-0660 D-10: Codex takes no default placement before S7");
        var created = await verify.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == automatic.Id && e.Type == AgentTaskEventType.Created);
        created.Detail.ShouldNotBeNull().ShouldContain(
            "runner source=default requested=unset default=server2 selected=local reason=kind_not_supported", Case.Sensitive);

        // The persisted-kind dispatch fence admits what explicit create admitted; automatic kind
        // moves onto a runner (reroute, rewalk, wall) still stop at Grok and Claude Code.
        DefaultRunnerRoutingPolicy.IsHostKindAdmitted(Runner, AgentKind.Codex).ShouldBeTrue();
        DefaultRunnerRoutingPolicy.IsHostKindAdmitted(Runner, AgentKind.OpenCode).ShouldBeFalse();
        DefaultRunnerRoutingPolicy.IsHostKindAdmitted(null, AgentKind.OpenCode).ShouldBeTrue();
        DefaultRunnerRoutingPolicy.IsHostKindCompatible(Runner, AgentKind.Codex).ShouldBeFalse();
        DefaultRunnerRoutingPolicy.IsHostKindCompatible(Runner, AgentKind.ClaudeCode).ShouldBeTrue();
    }

    private static CreateAgentTaskRequest Remote(string goal) =>
        new(goal, Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Code, AgentKind: AgentKind.Codex,
            Workspace: WorkspaceMode.Worktree, RunnerId: Runner);

    private static Task<RunnerProviderAuthDto?> Answer(string provider, bool? loggedIn) =>
        Task.FromResult<RunnerProviderAuthDto?>(new RunnerProviderAuthDto(
            provider, loggedIn, loggedIn == true ? "auth_file" : null, null, DateTimeOffset.UtcNow, null));

    private static async Task MarkFailedAsync(Kit kit, Guid taskId)
    {
        await using var db = kit.Context();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Failed;
        task.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        kit.Client.Calls.Clear();
        kit.Directory.ResolveCalls.Clear();
    }

    private static async Task<Guid> SeedFailedRemoteCodexAsync(Kit kit)
    {
        var id = Guid.NewGuid();
        await using var db = kit.Context();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "c660 remote codex",
            Goal = "c660 retry " + id.ToString("N")[..8],
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = kit.RepoRoot,
            RunnerId = Runner,
            Status = AgentTaskStatus.Failed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class Kit
    {
        public required string ConnectionString { get; init; }
        public required string RepoRoot { get; init; }
        public required DelegationSettings Settings { get; init; }
        public required PhoneHomeRunnerSettings PhoneHome { get; init; }
        public required AgentRegistrySettings Registry { get; init; }
        public required ProbeDirectory Directory { get; init; }
        public ProbeClient Client => Directory.Client;
        public AgentTaskService.Caller Caller => new(null, null, RepoRoot);

        public static Kit Create(
            IsolatedTestSchema schema,
            Func<string, CancellationToken, Task<RunnerProviderAuthDto?>> probe,
            bool codexProbe = true,
            string? defaultRunnerId = null,
            string? desktopHome = null)
        {
            var repoRoot = FindRepoRoot();
            // The desktop Grok store is signed out (an empty GROK_HOME), so a probe that wrongly
            // consulted the desktop for Codex would refuse where it must not.
            var registry = new AgentRegistrySettings { GrokCredentialProbeEnabled = true };
            registry.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok", Exe = "grok",
                Env = desktopHome is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["GROK_HOME"] = desktopHome },
            };
            registry.Definitions["codex"] = new AgentDefinition
            {
                Kind = "Codex", Exe = "codex.cmd",
                Env = desktopHome is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["CODEX_HOME"] = desktopHome },
            };
            return new Kit
            {
                ConnectionString = schema.ConnectionString,
                RepoRoot = repoRoot,
                Settings = new DelegationSettings { AllowedRoots = [repoRoot], DefaultRunnerId = defaultRunnerId },
                PhoneHome = new PhoneHomeRunnerSettings
                {
                    Enabled = true,
                    AllowedRunnerId = Runner,
                    AllowDelegatedTasks = true,
                    HostWorkspaceRoot = repoRoot,
                    CallbackOrigin = "https://antiphon.test",
                    SharedSecret = "x",
                    ChildGrokHome = "/state/grok",
                    CodexAuthProbeEnabled = codexProbe,
                },
                Registry = registry,
                Directory = new ProbeDirectory(new ProbeClient(probe)),
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
            registrySettings: Options.Create(Registry),
            phoneHome: new PhoneHomeLaunchPolicy(Options.Create(PhoneHome)),
            runners: Directory);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
        }
    }

    private sealed class ProbeDirectory(ProbeClient client) : ISessionRunnerDirectory
    {
        public ProbeClient Client { get; } = client;
        public List<string?> ResolveCalls { get; } = [];
        public ISessionRunnerClient Local => Client;
        public IReadOnlyList<string> KnownRunnerIds => [Runner];
        public Guid? LiveStoreId => Guid.NewGuid();

        public ISessionRunnerClient Resolve(string? runnerId)
        {
            ResolveCalls.Add(runnerId);
            Client.CurrentRunner = runnerId;
            return Client;
        }

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("test"));
    }

    private sealed class ProbeClient(Func<string, CancellationToken, Task<RunnerProviderAuthDto?>> probe) : ISessionRunnerClient
    {
        public string? CurrentRunner { get; set; }
        public List<(string? Runner, string Provider)> Calls { get; } = [];

        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct)
        {
            Calls.Add((CurrentRunner, provider));
            return probe(provider, ct);
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
