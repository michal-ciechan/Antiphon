using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0507 S1 — a role-policy bundle key that is not a tool bundle refuses at create (422
/// <c>stage_tool_bundle_unknown</c>, over the service and over HTTP) and at dispatch before the
/// claim, so nothing launches. Each test owns an isolated schema.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class RolePolicyToolBundleDispatchTests
{
    private const string UnknownKey = "tool-does-not-exist";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    // V-3a. Mutation: CreateAsync no longer calls the resolver -> no throw, red.
    [Test]
    public async Task create_refuses_a_review_whose_role_names_an_unknown_tool_bundle_and_writes_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var workspace = Directory.CreateTempSubdirectory("antiphon-c507-create").FullName;
        try
        {
            var service = CreateService(db, s => s.RolePolicy["Review"].Bundles = [UnknownKey]);

            var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Shared),
                ManualCaller(workspace),
                CancellationToken.None));

            ex.Code.ShouldBe("stage_tool_bundle_unknown");
            var error = ex.Errors["RolePolicy"].ShouldHaveSingleItem();
            error.ShouldContain("Review");
            error.ShouldContain(UnknownKey);
            (await db.AgentTasks.CountAsync()).ShouldBe(0, "a refused create writes no task");
            (await db.AgentSessions.CountAsync()).ShouldBe(0, "a refused create writes no session");

            // One flip: only the configured role is refused.
            var docs = await service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "docs", Role: AgentTaskRole.Docs, Workspace: WorkspaceMode.Shared),
                ManualCaller(workspace),
                CancellationToken.None);
            (await db.AgentTasks.AsNoTracking().SingleAsync()).Id.ShouldBe(docs.Id);
        }
        finally
        {
            DeleteQuietly(workspace);
        }
    }

    // V-3b. Mutation: as V-3a (the problem+json shape is the existing middleware) -> 201, red.
    [Test]
    public async Task post_agent_tasks_returns_422_stage_tool_bundle_unknown_as_problem_details()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await CreateHost.StartAsync(schema.ConnectionString, UnknownKey);

        var refused = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            var root = problem.RootElement;
            root.GetProperty("code").GetString().ShouldBe("stage_tool_bundle_unknown");
            root.GetProperty("status").GetInt32().ShouldBe(422);
            var error = root.GetProperty("errors").GetProperty("RolePolicy")[0].GetString();
            error.ShouldNotBeNull();
            error.ShouldContain("Review");
            error.ShouldContain(UnknownKey);
        }

        var accepted = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "docs", Role: AgentTaskRole.Docs, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await accepted.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json)).ShouldNotBeNull();
    }

    // V-3c. A task queued before the configuration changed. Mutation: the dispatcher's resolver
    // call removed -> the Review task is dispatched with a session, red.
    [Test]
    public async Task the_dispatcher_refuses_a_queued_review_before_the_claim_and_launches_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sink = new RecordingSink();
        await using var provider = BuildProvider(schema.ConnectionString, sink, UnknownKey);
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reviewDir = Directory.CreateTempSubdirectory("antiphon-c507-review").FullName;
        var docsDir = Directory.CreateTempSubdirectory("antiphon-c507-docs").FullName;
        try
        {
            var review = await SeedQueuedAsync(db, reviewDir, AgentTaskRole.Review);
            var docs = await SeedQueuedAsync(db, docsDir, AgentTaskRole.Docs);

            await dispatcher.TickAsync(CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var reviewRow = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == review.Id);
            reviewRow.Status.ShouldBe(AgentTaskStatus.Failed);
            reviewRow.AgentSessionId.ShouldBeNull("nothing launched for the refused task");
            reviewRow.DispatchedAt.ShouldBeNull();
            var failed = await verify.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == review.Id && e.Type == AgentTaskEventType.Failed)
                .Select(e => e.Detail)
                .SingleAsync();
            failed.ShouldContain("stage_tool_bundle_unknown");
            failed.ShouldContain("Review");
            failed.ShouldContain(UnknownKey);

            // The same tick dispatches a role with no tool bundles, so "no session" above is the
            // refusal and not a fixture that never launches.
            var docsRow = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == docs.Id);
            docsRow.Status.ShouldBe(AgentTaskStatus.Dispatched);
            docsRow.AgentSessionId.ShouldNotBeNull();
            (await verify.AgentSessions.CountAsync()).ShouldBe(1);
            sink.Items.ShouldHaveSingleItem().SessionId.ShouldBe(docsRow.AgentSessionId!.Value);
        }
        finally
        {
            DeleteQuietly(reviewDir);
            DeleteQuietly(docsDir);
        }
    }

    // V-4b. Mutation: the resolver checks Exists(key) only -> no throw, red.
    [Test]
    public async Task create_refuses_a_known_key_without_the_tool_prefix()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var workspace = Directory.CreateTempSubdirectory("antiphon-c507-prefix").FullName;
        try
        {
            InstructionBundles.Exists("stage-code").ShouldBeTrue("the key is in the catalog; only its prefix is wrong");
            var service = CreateService(db, s => s.RolePolicy["Review"].Bundles = ["stage-code"]);

            var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Shared),
                ManualCaller(workspace),
                CancellationToken.None));

            ex.Code.ShouldBe("stage_tool_bundle_unknown");
            var error = ex.Errors["RolePolicy"].ShouldHaveSingleItem();
            error.ShouldContain("stage-code");
            error.ShouldContain("tool-");
            (await db.AgentTasks.CountAsync()).ShouldBe(0);
        }
        finally
        {
            DeleteQuietly(workspace);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static AgentTaskService CreateService(AppDbContext db, Action<DelegationSettings> configure)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [],
        };
        configure(settings);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    private static async Task<AgentTask> SeedQueuedAsync(AppDbContext db, string cwd, AgentTaskRole role)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = $"queued {role}",
            Goal = $"queued {role}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        task.RootTaskId = task.Id;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    // Shape of PinnedProfileLaunchSpecTests.BuildProvider. The launch sink records instead of
    // starting a process, and the claude definition names a path that does not exist.
    private static ServiceProvider BuildProvider(string connectionString, RecordingSink sink, string reviewBundle)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            PoolIdleRetireMinutes = 525_600,
            PoolMaxIdlePerDirectory = int.MaxValue,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Review"] = new() { Level = AgentModelLevel.Frontier, Bundles = [reviewBundle] },
            },
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition
            {
                Kind = "ClaudeCode",
                Exe = Path.Combine(Path.GetTempPath(), "antiphon-c507-no-such-claude"),
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c507-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<RecordingLaunchSecretProtector>();
        services.AddSingleton<IAgentTuiSecretProtector>(sp =>
            sp.GetRequiredService<RecordingLaunchSecretProtector>());
        services.AddSingleton<AgentTuiMetrics>();
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddScoped<AgentTuiLaunchResolver>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private static void DeleteQuietly(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }

    private sealed class RecordingSink : IAgentTaskLaunchSink
    {
        public List<(Guid SessionId, Guid AgentId)> Items { get; } = [];

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Items.Add((sessionId, agentId));
    }

    // File-local Kestrel host in the shape of RunnerDefaultTests.DefaultsHost: the real
    // ExceptionMiddleware and AgentTaskService behind POST /api/agent-tasks.
    private sealed class CreateHost : IAsyncDisposable
    {
        public HttpClient Client { get; }
        public string RepoRoot { get; }
        private readonly WebApplication _app;

        private CreateHost(WebApplication app, string repoRoot)
        {
            _app = app;
            RepoRoot = repoRoot;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<CreateHost> StartAsync(string connectionString, string reviewBundle)
        {
            var repo = FindRepo();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
            builder.Services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
            builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, n =>
            {
                n.MigrationsAssembly("Antiphon.Server");
                n.SetPostgresVersion(16, 0);
            }));
            var settings = new DelegationSettings { AllowedRoots = [repo] };
            settings.RolePolicy["Review"].Bundles = [reviewBundle];
            builder.Services.AddSingleton(Options.Create(settings));
            builder.Services.AddSingleton<IEventBus, MockEventBus>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            builder.Services.AddSingleton<DelegationWorkspaceResolver>();
            builder.Services.AddScoped<AgentTaskService>();
            builder.Services.AddLogging();
            var app = builder.Build();
            app.UseMiddleware<ExceptionMiddleware>();
            app.MapPost("/api/agent-tasks", async (
                CreateAgentTaskRequest request, AgentTaskService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, new AgentTaskService.Caller(null, null, ""), ct);
                return Results.Created($"/api/agent-tasks/{created.Id}", created);
            });
            await app.StartAsync();
            return new CreateHost(app, repo);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static string FindRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
        }
    }
}
