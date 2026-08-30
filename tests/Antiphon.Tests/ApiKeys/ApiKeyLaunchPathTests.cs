using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S2 — resolution through the REAL launch resolvers, both of them.
///
/// <para>The unit tests beside this one drive <c>ApiKeyEnvResolver</c> directly. These drive the two
/// bottom-level paths a real launch takes — the legacy no-profile path through
/// <c>AgentLaunchResolution</c>, and the managed-profile path through
/// <c>AgentTuiLaunchResolver.ResolveCoreAsync</c> — because a placeholder that resolves in isolation
/// and not in the path a launch actually walks is a feature that does not exist.</para>
/// </summary>
public class ApiKeyLaunchPathTests
{
    private static CancellationToken Ct => CancellationToken.None;

    // ---- the legacy (no managed profile) path -----------------------------------------------------

    [Test]
    public async Task an_agents_own_launch_env_placeholder_resolves_on_the_legacy_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var name = await AddKeyAsync(db, projectId: null, value: "sk-global-legacy");
        var agent = await AddAgentAsync(db, launchEnv: new()
        {
            ["ANTHROPIC_API_KEY"] = $"{{{{key:{name}}}}}",
            ["FOO"] = "literal-non-secret-value",
        });

        var resolved = await ResolveLegacyAsync(db, agent);

        resolved.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe("sk-global-legacy");
        resolved.Spec.Env["FOO"].ShouldBe("literal-non-secret-value");
    }

    [Test]
    public async Task a_pinned_agents_board_selects_its_projects_key_over_the_global_one()
    {
        // The case the plan names explicitly: a delegate pinned to a standing agent that has a
        // board, and therefore a project, needing that project's own credential.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var project = await AddProjectAsync(db);
        var board = await AddBoardAsync(db, project.Id);
        var name = NewName();
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: project.Id, value: "sk-project", name: name);
        var agent = await AddAgentAsync(
            db, launchEnv: new() { ["K"] = $"{{{{key:{name}}}}}" }, boardId: board.Id);

        (await ResolveLegacyAsync(db, agent)).Spec.Env["K"].ShouldBe("sk-project");
    }

    [Test]
    public async Task an_agent_with_no_board_gets_the_global_key_and_never_the_project_one()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var project = await AddProjectAsync(db);
        var name = NewName();
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: project.Id, value: "sk-project", name: name);
        var agent = await AddAgentAsync(
            db, launchEnv: new() { ["K"] = $"{{{{key:{name}}}}}" }, boardId: null);

        (await ResolveLegacyAsync(db, agent)).Spec.Env["K"].ShouldBe("sk-global");
    }

    [Test]
    public async Task a_pool_delegates_recorded_task_scope_selects_its_projects_key_over_the_global_one()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var project = await AddProjectAsync(db);
        var name = NewName();
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: project.Id, value: "sk-project", name: name);
        var poolDelegate = await AddAgentAsync(
            db, launchEnv: new() { ["K"] = $"{{{{key:{name}}}}}" }, boardId: null);

        (await ResolveLegacyAsync(db, poolDelegate, apiKeyProjectId: project.Id)).Spec.Env["K"]
            .ShouldBe("sk-project");
    }

    [Test]
    public async Task a_pool_delegates_task_scope_cannot_leak_another_projects_key()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var projectA = await AddProjectAsync(db);
        var projectB = await AddProjectAsync(db);
        var name = NewName();
        await AddKeyAsync(db, projectId: null, value: "sk-global", name: name);
        await AddKeyAsync(db, projectId: projectA.Id, value: "sk-project-a", name: name);
        await AddKeyAsync(db, projectId: projectB.Id, value: "sk-project-b", name: name);
        var poolDelegate = await AddAgentAsync(
            db, launchEnv: new() { ["K"] = $"{{{{key:{name}}}}}" }, boardId: null);

        (await ResolveLegacyAsync(db, poolDelegate, apiKeyProjectId: projectA.Id)).Spec.Env["K"]
            .ShouldBe("sk-project-a", "project B's same-named row must never resolve for project A");
        (await ResolveLegacyAsync(db, poolDelegate)).Spec.Env["K"]
            .ShouldBe("sk-global", "an unscoped task must not receive either project's key");
    }

    [Test]
    public async Task a_tasks_recorded_project_scope_overrides_its_pinned_agents_board_project()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var taskProject = await AddProjectAsync(db);
        var agentProject = await AddProjectAsync(db);
        var agentBoard = await AddBoardAsync(db, agentProject.Id);
        var name = NewName();
        await AddKeyAsync(db, projectId: taskProject.Id, value: "sk-task-project", name: name);
        await AddKeyAsync(db, projectId: agentProject.Id, value: "sk-agent-project", name: name);
        var pinnedAgent = await AddAgentAsync(
            db, launchEnv: new() { ["K"] = $"{{{{key:{name}}}}}" }, boardId: agentBoard.Id);

        (await ResolveLegacyAsync(db, pinnedAgent, apiKeyProjectId: taskProject.Id)).Spec.Env["K"]
            .ShouldBe("sk-task-project");
    }

    [Test]
    public async Task an_unknown_key_under_a_task_scope_names_the_project_then_global_scopes_it_searched()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var project = await AddProjectAsync(db);
        var poolDelegate = await AddAgentAsync(
            db, launchEnv: new() { ["K"] = "{{key:never-stored-for-task-scope}}" }, boardId: null);

        var ex = await Should.ThrowAsync<ConflictException>(
            ResolveLegacyAsync(db, poolDelegate, apiKeyProjectId: project.Id));

        ex.Code.ShouldBe("api_key_not_found");
        ex.Message.ShouldContain("never-stored-for-task-scope");
        ex.Message.ShouldContain($"project {project.Id:D}, then the global scope");
    }

    [Test]
    public async Task an_agent_env_entry_cannot_take_over_the_orchestration_identity()
    {
        // A per-agent override of ANTIPHON_SESSION_ID would be a self-inflicted CARD-0006: the
        // delegate would bind to another session's transcript. ExtraEnv is merged last for exactly
        // this reason, and this pins it through the path a real launch takes.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var agent = await AddAgentAsync(db, launchEnv: new()
        {
            ["ANTIPHON_SESSION_ID"] = "hijacked",
            ["ANTIPHON_TASK_TOKEN"] = "stolen",
        });
        var realSession = Guid.NewGuid().ToString("D");

        var resolved = await ResolveLegacyAsync(db, agent, extraEnv: new Dictionary<string, string>
        {
            ["ANTIPHON_SESSION_ID"] = realSession,
            ["ANTIPHON_TASK_TOKEN"] = "the-real-token",
        });

        resolved.Spec.Env["ANTIPHON_SESSION_ID"].ShouldBe(realSession);
        resolved.Spec.Env["ANTIPHON_TASK_TOKEN"].ShouldBe("the-real-token");
    }

    [Test]
    public async Task an_unknown_key_fails_the_launch_by_name_on_the_legacy_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema);
        var agent = await AddAgentAsync(db, launchEnv: new() { ["K"] = "{{key:never-stored}}" });

        var ex = await Should.ThrowAsync<ConflictException>(ResolveLegacyAsync(db, agent));

        ex.Code.ShouldBe("api_key_not_found");
        ex.Message.ShouldContain("never-stored");
    }

    // ---- the managed-profile path -----------------------------------------------------------------

    [Test]
    public async Task a_placeholder_in_a_profiles_non_secret_env_resolves()
    {
        // The migration road away from AgentTuiSecret (plan section 7): because resolution runs over
        // the MERGED env, a profile's plain env value can reference a stored key, which is what will
        // let that convergence card be small.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var name = await AddKeyThroughProviderAsync(provider, projectId: null, value: "sk-from-profile");
        var profile = await SeedProfileAsync(
            provider,
            nonSecretEnv: new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = $"{{{{key:{name}}}}}" });
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Managed", TuiProfileId = profile };

        var resolved = await ResolveManagedAsync(provider, agent);

        resolved.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe("sk-from-profile");
    }

    [Test]
    public async Task the_agents_launch_env_merges_and_resolves_on_the_managed_path_too()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var name = await AddKeyThroughProviderAsync(provider, projectId: null, value: "sk-agent-managed");
        var profile = await SeedProfileAsync(
            provider,
            nonSecretEnv: new Dictionary<string, string> { ["CONTESTED"] = "from-profile" });
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Managed",
            TuiProfileId = profile,
            LaunchEnvJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ANTHROPIC_API_KEY"] = $"{{{{key:{name}}}}}",
                ["CONTESTED"] = "from-agent",
            }),
        };

        var resolved = await ResolveManagedAsync(provider, agent);

        resolved.Spec.Env["ANTHROPIC_API_KEY"].ShouldBe("sk-agent-managed");
        resolved.Spec.Env["CONTESTED"].ShouldBe(
            "from-agent", "the agent's own field is more specific than the profile it shares");
    }

    [Test]
    public async Task a_placeholder_in_an_extra_ARGUMENT_is_refused_before_the_launch_is_built()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var name = await AddKeyThroughProviderAsync(provider, null, "sk-never-in-argv");
        var profile = await SeedProfileAsync(provider);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Managed", TuiProfileId = profile };

        var ex = await Should.ThrowAsync<InvalidOperationException>(ResolveManagedAsync(
            provider,
            agent,
            extraArgs: ["--append-system-prompt", $"Use {{{{key:{name}}}}} for the API."]));

        ex.Message.ShouldContain("environment VALUES only");
        ex.Message.ShouldNotContain("sk-never-in-argv");
    }

    [Test]
    public async Task a_launch_with_no_placeholder_anywhere_is_unchanged_by_all_of_this()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var profile = await SeedProfileAsync(
            provider,
            nonSecretEnv: new Dictionary<string, string> { ["PLAIN"] = "value" });
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Managed", TuiProfileId = profile };

        var resolved = await ResolveManagedAsync(provider, agent);

        resolved.Spec.Env["PLAIN"].ShouldBe("value");
        resolved.Spec.Env.ShouldContainKey("DISABLE_AUTOUPDATER");
    }

    [Test]
    public async Task the_launch_override_beats_the_agent_env_and_loses_to_ExtraEnv_on_the_managed_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var profile = await SeedProfileAsync(
            provider,
            nonSecretEnv: new Dictionary<string, string> { ["FROM_PROFILE"] = "profile" });
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Managed",
            TuiProfileId = profile,
            LaunchEnvJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["CONTESTED"] = "agent",
            }),
        };

        var resolved = await ResolveManagedAsync(
            provider,
            agent,
            extraEnv: new Dictionary<string, string> { ["ANTIPHON_SESSION_ID"] = "the-real-session" },
            launchEnvOverride: new Dictionary<string, string>
            {
                ["CONTESTED"] = "override",
                ["ANTIPHON_SESSION_ID"] = "hijacked",
            });

        resolved.Spec.Env["FROM_PROFILE"].ShouldBe("profile");
        resolved.Spec.Env["CONTESTED"].ShouldBe("override");
        resolved.Spec.Env["ANTIPHON_SESSION_ID"].ShouldBe("the-real-session");
    }

    [Test]
    public async Task a_project_default_beats_the_profile_env_and_loses_to_the_agent_on_the_managed_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var profile = await SeedProfileAsync(
            provider,
            nonSecretEnv: new Dictionary<string, string>
            {
                ["FROM_PROFILE"] = "profile",
                ["CONTESTED_PROFILE_PROJ"] = "profile",
            });
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Managed",
            TuiProfileId = profile,
            LaunchEnvJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["CONTESTED_PROJ_AGENT"] = "agent",
            }),
        };

        var resolved = await ResolveManagedAsync(
            provider,
            agent,
            projectDefaultEnv: new Dictionary<string, string>
            {
                ["CONTESTED_PROFILE_PROJ"] = "project",
                ["CONTESTED_PROJ_AGENT"] = "project",
                ["FROM_PROJECT"] = "project",
            });

        resolved.Spec.Env["FROM_PROFILE"].ShouldBe("profile");
        resolved.Spec.Env["FROM_PROJECT"].ShouldBe("project");
        resolved.Spec.Env["CONTESTED_PROFILE_PROJ"].ShouldBe("project");
        resolved.Spec.Env["CONTESTED_PROJ_AGENT"].ShouldBe("agent");
    }

    [Test]
    public async Task inherited_env_beats_the_project_default_and_loses_to_the_agent_on_the_managed_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var profile = await SeedProfileAsync(
            provider,
            nonSecretEnv: new Dictionary<string, string> { ["FROM_PROFILE"] = "profile" });
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Managed",
            TuiProfileId = profile,
            LaunchEnvJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "agent",
            }),
        };

        var resolved = await ResolveManagedAsync(
            provider,
            agent,
            projectDefaultEnv: new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "project",
                ["ANTHROPIC_BASE_URL"] = "http://from-project",
            },
            inheritedEnv: new Dictionary<string, string>
            {
                ["X_LLM_PROJECT"] = "inherited",
                ["ANTHROPIC_BASE_URL"] = "http://from-inherited",
            });

        resolved.Spec.Env["FROM_PROFILE"].ShouldBe("profile");
        resolved.Spec.Env["X_LLM_PROJECT"].ShouldBe("agent");
        resolved.Spec.Env["ANTHROPIC_BASE_URL"].ShouldBe("http://from-inherited");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static string NewName() => $"launch-{Guid.NewGuid():N}";

    private static AppDbContext NewDb(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<ResolvedAgentTuiLaunch> ResolveLegacyAsync(
        AppDbContext db,
        Agent agent,
        IReadOnlyDictionary<string, string>? extraEnv = null,
        Guid? apiKeyProjectId = null)
    {
        var registry = new AgentRegistry(new OptionsMonitorStub(new AgentRegistrySettings
        {
            DefaultDefinition = "test",
            Definitions = { ["test"] = new AgentDefinition { Kind = nameof(AgentKind.Raw), Exe = "cmd.exe" } },
        }));
        var apiKeys = new ApiKeyEnvResolver(
            db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<ApiKeyEnvResolver>.Instance);

        return await AgentLaunchResolution.ResolveForAgentAsync(
            agent,
            registry,
            launchResolver: null,
            new AgentLaunchOptions(
                Cwd: Path.GetTempPath(), ExtraEnv: extraEnv, ApiKeyProjectId: apiKeyProjectId),
            Ct,
            apiKeys);
    }

    private static async Task<ResolvedAgentTuiLaunch> ResolveManagedAsync(
        ServiceProvider provider,
        Agent agent,
        IReadOnlyList<string>? extraArgs = null,
        IReadOnlyDictionary<string, string>? extraEnv = null,
        IReadOnlyDictionary<string, string>? launchEnvOverride = null,
        IReadOnlyDictionary<string, string>? projectDefaultEnv = null,
        IReadOnlyDictionary<string, string>? inheritedEnv = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>();
        return await resolver.ResolveForAgentAsync(
            agent,
            new AgentLaunchOptions(
                Cols: 120,
                Rows: 30,
                ExtraArgs: extraArgs,
                ExtraEnv: extraEnv,
                LaunchEnvOverride: launchEnvOverride,
                ProjectDefaultEnv: projectDefaultEnv,
                InheritedEnv: inheritedEnv),
            Ct);
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IAgentTuiSecretProtector, NoOpSecretProtector>();
        services.AddSingleton<IApiKeyProtector, ApiKeyStoreTests.FakeApiKeyProtector>();
        services.AddSingleton<AgentTuiMetrics>();
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddScoped<ApiKeyEnvResolver>();
        services.AddScoped<AgentTuiLaunchResolver>();
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedProfileAsync(
        ServiceProvider provider,
        IDictionary<string, string>? nonSecretEnv = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"Launch profile {Guid.NewGuid():N}",
            Kind = AgentKind.ClaudeCode,
            IsEnabled = true,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync(Ct);

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = "cmd.exe",
            ArgumentsJson = "[]",
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            WorkingDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = JsonSerializer.Serialize(
                nonSecretEnv ?? new Dictionary<string, string>()),
            SecretEnvironmentNamesJson = "[]",
            ModelArgumentName = "--model",
            Guidance = "Launch test",
            CreatedAt = now,
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync(Ct);

        profile.ActiveRevisionId = revision.Id;
        await db.SaveChangesAsync(Ct);
        return profile.Id;
    }

    private static async Task<string> AddKeyThroughProviderAsync(
        ServiceProvider provider, Guid? projectId, string value)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await AddKeyAsync(db, projectId, value);
    }

    private static async Task<string> AddKeyAsync(
        AppDbContext db, Guid? projectId, string value, string? name = null)
    {
        var keyName = name ?? NewName();
        var id = Guid.NewGuid();
        db.ApiKeys.Add(new ApiKey
        {
            Id = id,
            Name = keyName,
            ProjectId = projectId,
            Ciphertext = new ApiKeyStoreTests.FakeApiKeyProtector().Protect(id, value),
            ProtectionVersion = "v1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
        return keyName;
    }

    private static async Task<Agent> AddAgentAsync(
        AppDbContext db,
        Dictionary<string, string>? launchEnv = null,
        Guid? boardId = null)
    {
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"Agent {Guid.NewGuid():N}",
            Slug = $"agent-{Guid.NewGuid():N}",
            WorkingDirectory = Path.GetTempPath(),
            BoardId = boardId,
            LaunchEnvJson = JsonSerializer.Serialize(launchEnv ?? []),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync(Ct);
        return agent;
    }

    private static async Task<Project> AddProjectAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Launch Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(Ct);
        return project;
    }

    private static async Task<Board> AddBoardAsync(AppDbContext db, Guid projectId)
    {
        var now = DateTime.UtcNow;
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = $"Board {Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Boards.Add(board);
        await db.SaveChangesAsync(Ct);
        return board;
    }

    private sealed class NoOpSecretProtector : IAgentTuiSecretProtector
    {
        public string Protect(Guid profileId, string environmentName, string plaintext) => plaintext;

        public string Unprotect(Guid profileId, string environmentName, string protectedValue) =>
            protectedValue;
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<AgentRegistrySettings>
    {
        public OptionsMonitorStub(AgentRegistrySettings value) => CurrentValue = value;

        public AgentRegistrySettings CurrentValue { get; }

        public AgentRegistrySettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AgentRegistrySettings, string?> listener) => null;
    }
}
