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
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1234 (2): role policy values that bypassed configuration validation. The host registers
/// settings without the validator; each test owns an isolated schema.
/// post_agent_tasks_refuses_an_unusable_policy_value_with_a_named_422_and_writes_nothing: a Docs
/// create with no explicit level, which consumes the undefined policy or default level or the
/// unlaunchable policy kind, gets the named 422 and writes no task or session; a Commit create on
/// the same host is created. an_explicit_level_does_not_consult_an_undefined_policy_level: an
/// explicit valid level on the same Docs request overrides the undefined policy level and creates
/// (201, Low). an_orchestrator_refuses_an_undefined_policy_level_instead_of_clamping_it_to_the_floor:
/// orchestrator level resolution with no explicit level throws the named refusal for Docs.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class RolePolicyInvalidValuesCreateTests
{
    private static readonly AgentKind UndefinedKind = (AgentKind)999;
    private static readonly AgentModelLevel UndefinedLevel = (AgentModelLevel)999;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public sealed record RefusalCase(
        string Name,
        Action<DelegationSettings> Configure,
        string Code,
        string Field,
        string ErrorStart)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<Func<RefusalCase>> RefusalCases()
    {
        yield return () => new("policy-level-999", s => s.RolePolicy["Docs"].Level = UndefinedLevel,
            "role_policy_level_invalid", "RolePolicy",
            "Delegation:RolePolicy:Docs:Level is 999, which is not a model level.");
        yield return () => new("default-level-999", s =>
            {
                s.RolePolicy.Remove("Docs");
                s.DefaultLevel = UndefinedLevel;
            },
            "role_policy_level_invalid", "RolePolicy",
            "Delegation:DefaultLevel is 999, which is not a model level.");
        yield return () => new("policy-kind-Raw", s => s.RolePolicy["Docs"].Kind = AgentKind.Raw,
            "role_policy_kind_unsupported", "AgentKind",
            "Raw is configured as the 'Docs' role's kind, but is not a delegate kind.");
        yield return () => new("policy-kind-OpenCode", s => s.RolePolicy["Docs"].Kind = AgentKind.OpenCode,
            "role_policy_kind_unsupported", "AgentKind",
            "OpenCode is configured as the 'Docs' role's kind, but is not a delegate kind.");
        yield return () => new("policy-kind-999", s => s.RolePolicy["Docs"].Kind = UndefinedKind,
            "role_policy_kind_unsupported", "AgentKind",
            "999 is configured as the 'Docs' role's kind, but is not a delegate kind.");
    }

    [Test]
    [MethodDataSource(nameof(RefusalCases))]
    public async Task post_agent_tasks_refuses_an_unusable_policy_value_with_a_named_422_and_writes_nothing(RefusalCase c)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await CreateHost.StartAsync(schema.ConnectionString, c.Configure);

        var refused = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "docs", Role: AgentTaskRole.Docs, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            var root = problem.RootElement;
            root.GetProperty("code").GetString().ShouldBe(c.Code);
            root.GetProperty("status").GetInt32().ShouldBe(422);
            var error = root.GetProperty("errors").GetProperty(c.Field)[0].GetString();
            error.ShouldNotBeNull().ShouldStartWith(c.ErrorStart);
        }

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            (await db.AgentTasks.CountAsync()).ShouldBe(0, "a refused create writes no task");
            (await db.AgentSessions.CountAsync()).ShouldBe(0, "a refused create writes no session");
        }

        // One flip: a role whose policy is usable is still created on the same host.
        var accepted = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "commit", Role: AgentTaskRole.Commit, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await accepted.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json)).ShouldNotBeNull();
        created.ModelLevel.ShouldBe(AgentModelLevel.Medium);
    }

    [Test]
    public async Task an_explicit_level_does_not_consult_an_undefined_policy_level()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await CreateHost.StartAsync(
            schema.ConnectionString, s => s.RolePolicy["Docs"].Level = UndefinedLevel);

        var accepted = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "docs", Role: AgentTaskRole.Docs, ModelLevel: AgentModelLevel.Low, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await accepted.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json)).ShouldNotBeNull();
        created.ModelLevel.ShouldBe(AgentModelLevel.Low);
    }

    [Test]
    public async Task an_orchestrator_refuses_an_undefined_policy_level_instead_of_clamping_it_to_the_floor()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await CreateHost.StartAsync(
            schema.ConnectionString, s => s.RolePolicy["Docs"].Level = UndefinedLevel);
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();

        var ex = Should.Throw<ValidationException>(
            () => service.ResolveLevel(AgentTaskKind.Orchestrator, AgentTaskRole.Docs, null));

        ex.Code.ShouldBe("role_policy_level_invalid");
        ex.Errors["RolePolicy"].ShouldHaveSingleItem()
            .ShouldStartWith("Delegation:RolePolicy:Docs:Level is 999, which is not a model level.");
        service.ResolveLevel(AgentTaskKind.Orchestrator, AgentTaskRole.Test, null).ShouldBe(AgentModelLevel.High);
    }

    private sealed class CreateHost : IAsyncDisposable
    {
        public HttpClient Client { get; }
        public string RepoRoot { get; }
        public IServiceProvider Services => _app.Services;
        private readonly WebApplication _app;

        private CreateHost(WebApplication app, string repoRoot)
        {
            _app = app;
            RepoRoot = repoRoot;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<CreateHost> StartAsync(string connectionString, Action<DelegationSettings> configure)
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
            configure(settings);
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
