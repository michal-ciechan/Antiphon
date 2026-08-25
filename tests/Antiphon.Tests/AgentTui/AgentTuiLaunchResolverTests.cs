using System.Security.Cryptography;
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
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

public sealed class AgentTuiLaunchResolverTests
{
    [Test]
    public async Task Resolve_omits_model_argument_when_agent_has_no_exact_model()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, revision) = await SeedProfileAsync(provider, AgentKind.OpenCode);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Atlas",
            TuiProfileId = profile.Id,
            ModelId = null
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Args.ShouldNotContain("--model");
        resolved.EffectiveModelId.ShouldBeNull();
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.None);
        resolved.ProfileRevisionId.ShouldBe(revision.Id);
        resolved.ActivityMode.ShouldBe(AgentTuiLaunchActivityMode.QuietTime);
    }

    [Test]
    public async Task Resolve_appends_exact_model_as_separate_arguments()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, revision) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            models: ["llmgateway/grok-4-5"]);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Atlas",
            TuiProfileId = profile.Id,
            ModelId = "llmgateway/grok-4-5"
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Args.TakeLast(2).ShouldBe(new[] { "--model", "llmgateway/grok-4-5" });
        resolved.EffectiveModelId.ShouldBe("llmgateway/grok-4-5");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Exact);
        resolved.ProfileRevisionId.ShouldBe(revision.Id);
    }

    [Test]
    public async Task Grok_resolve_appends_model_and_uses_structured_activity()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, revision) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            models: ["grok-4.6", "grok-4.5"]);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Grok-Agent",
            TuiProfileId = profile.Id,
            ModelId = "grok-4.6"
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Kind.ShouldBe(AgentKind.Grok);
        resolved.Spec.Args.TakeLast(2).ShouldBe(new[] { "--model", "grok-4.6" });
        resolved.EffectiveModelId.ShouldBe("grok-4.6");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Exact);
        resolved.ActivityMode.ShouldBe(AgentTuiLaunchActivityMode.Structured);
        resolved.Spec.Env["GROK_TELEMETRY_ENABLED"].ShouldBe("0");
        resolved.ProfileRevisionId.ShouldBe(revision.Id);
    }

    [Test]
    public async Task Resolve_injects_managed_secrets_and_keeps_wrapper_profiles_secret_free()
    {
        var protector = new RecordingLaunchSecretProtector();
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString, protector);
        var (managed, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            authenticationMode: AgentTuiAuthenticationMode.ManagedEnvironment,
            secretNames: ["OPENAI_API_KEY"],
            canary: "canary-secret");
        var managedAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Managed",
            TuiProfileId = managed.Id
        };
        var managedLaunch = await ResolveAsync(provider, managedAgent);
        managedLaunch.Spec.Env["OPENAI_API_KEY"].ShouldBe("canary-secret");
        protector.UnprotectCalls.ShouldBe(1);

        var (wrapper, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            displayName: "Wrapper profile",
            authenticationMode: AgentTuiAuthenticationMode.WrapperManaged);
        var wrapperAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Wrapper",
            TuiProfileId = wrapper.Id
        };
        var wrapperLaunch = await ResolveAsync(provider, wrapperAgent);
        wrapperLaunch.Spec.Env.ContainsKey("OPENAI_API_KEY").ShouldBeFalse();
        protector.UnprotectCalls.ShouldBe(1);
    }

    [Test]
    public async Task Resolve_fails_closed_for_disabled_profile_and_unknown_model()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            enabled: false,
            models: ["llmgateway/grok-4-5"]);
        var disabled = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Disabled",
            TuiProfileId = profile.Id
        };
        var disabledError = await Should.ThrowAsync<ConflictException>(
            () => ResolveAsync(provider, disabled));
        disabledError.Code.ShouldBe("profile_disabled");

        var (enabled, _) = await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            models: ["llmgateway/grok-4-5"]);
        var unknownModel = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Unknown model",
            TuiProfileId = enabled.Id,
            ModelId = "provider/not-in-catalogue"
        };
        var modelError = await Should.ThrowAsync<ConflictException>(
            () => ResolveAsync(provider, unknownModel));
        modelError.Code.ShouldBe("model_not_in_profile");
    }

    [Test]
    public async Task T1_blank_model_argument_name_suppresses_the_tier_alias()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            modelArgumentName: null);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "GKP",
            TuiProfileId = profile.Id,
            ModelId = null,
            ModelLevel = AgentModelLevel.High
        };

        var resolved = await ResolveAsync(
            provider,
            agent,
            new AgentLaunchOptions(Cols: 120, Rows: 30, TierModelAlias: "grok-4.6"));

        resolved.Spec.Args.ShouldNotContain("--model");
        resolved.Spec.Args.ShouldNotContain("grok-4.6");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.ProfileOwned);
        resolved.EffectiveModelId.ShouldBeNull();
    }

    [Test]
    public async Task T2_declared_model_argument_appends_the_tier_alias_once()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(provider, AgentKind.Grok);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Grok-Tier",
            TuiProfileId = profile.Id,
            ModelId = null
        };

        var resolved = await ResolveAsync(
            provider,
            agent,
            new AgentLaunchOptions(Cols: 120, Rows: 30, TierModelAlias: "grok-4.6"));

        resolved.Spec.Args.Count(a => a == "--model").ShouldBe(1);
        resolved.Spec.Args[resolved.Spec.Args.ToList().IndexOf("--model") + 1].ShouldBe("grok-4.6");
        resolved.ModelArgument.ShouldBe(LaunchModelArgument.Tier);
        resolved.EffectiveModelId.ShouldBeNull();
    }

    [Test]
    public async Task T4_exact_model_on_a_blank_field_profile_is_model_argument_unsupported()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Grok,
            modelArgumentName: null);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Pinned-GKP",
            TuiProfileId = profile.Id,
            ModelId = "maven-grok"
        };

        var error = await Should.ThrowAsync<ConflictException>(
            () => ResolveAsync(provider, agent));

        error.Code.ShouldBe("model_argument_unsupported");
        error.StatusCode.ShouldBe(409);
        error.Message.ShouldContain("passes no model argument");
    }

    [Test]
    public async Task Resolve_uses_installation_default_when_agent_has_no_profile()
    {
        await using var isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(isolatedSchema.ConnectionString);
        await AssertNoProfilesAsync(provider);
        var defaultName = $"Default OpenCode {Guid.NewGuid():N}";
        await SeedProfileAsync(
            provider,
            AgentKind.OpenCode,
            displayName: defaultName,
            isDefault: true);
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Legacy",
            TuiProfileId = null
        };

        var resolved = await ResolveAsync(provider, agent);

        resolved.Spec.Kind.ShouldBe(AgentKind.OpenCode);
        resolved.Spec.DefinitionName.ShouldBe(defaultName);
    }

    [Test]
    public async Task Resolver_test_schemas_start_profile_empty_and_do_not_share_profiles()
    {
        await using var firstSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var firstProvider = BuildProvider(firstSchema.ConnectionString);
        await using var secondSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var secondProvider = BuildProvider(secondSchema.ConnectionString);

        await AssertNoProfilesAsync(firstProvider);
        await AssertNoProfilesAsync(secondProvider);
        await SeedProfileAsync(firstProvider, AgentKind.OpenCode, isDefault: true);

        await AssertNoProfilesAsync(secondProvider);
    }

    [Test]
    public async Task Legacy_resolution_rejects_selected_profile_when_resolver_is_absent()
    {
        var registry = new AgentRegistry(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "legacy",
            Definitions =
            {
                ["legacy"] = new AgentDefinition
                {
                    Kind = "Raw",
                    Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe")
                }
            }
        }));
        var selectedAgent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Selected profile",
            TuiProfileId = Guid.NewGuid()
        };

        var exception = await Should.ThrowAsync<ConflictException>(() =>
            AgentLaunchResolution.ResolveForAgentAsync(
                selectedAgent,
                registry,
                launchResolver: null,
                new AgentLaunchOptions(Cols: 120, Rows: 30),
                CancellationToken.None));

        exception.Code.ShouldBe("profile_resolution_unavailable");
    }

    private static async Task<ResolvedAgentTuiLaunch> ResolveAsync(
        ServiceProvider provider,
        Agent agent,
        AgentLaunchOptions? options = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<AgentTuiLaunchResolver>();
        return await resolver.ResolveForAgentAsync(
            agent,
            options ?? new AgentLaunchOptions(Cols: 120, Rows: 30),
            CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        RecordingLaunchSecretProtector? protector = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            connectionString));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(protector ?? new RecordingLaunchSecretProtector());
        services.AddSingleton<IAgentTuiSecretProtector>(sp =>
            sp.GetRequiredService<RecordingLaunchSecretProtector>());
        services.AddSingleton<AgentTuiMetrics>();
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddScoped<AgentTuiLaunchResolver>();
        return services.BuildServiceProvider();
    }

    private static async Task AssertNoProfilesAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTuiProfiles.AnyAsync()).ShouldBeFalse(
            "each resolver test must use a new schema, so profile seeds cannot leak to another test");
    }

    private static async Task<(AgentTuiProfile Profile, AgentTuiProfileRevision Revision)> SeedProfileAsync(
        ServiceProvider provider,
        AgentKind kind,
        string? displayName = null,
        bool enabled = true,
        bool isDefault = false,
        AgentTuiAuthenticationMode authenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
        string[]? secretNames = null,
        string[]? models = null,
        string canary = "canary-secret",
        string? modelArgumentName = "--model")
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<RecordingLaunchSecretProtector>();
        var now = DateTime.UtcNow;
        displayName ??= $"Launch profile {Guid.NewGuid():N}";
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            Kind = kind,
            IsEnabled = enabled,
            IsDefault = isDefault,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = "pwsh.exe",
            ArgumentsJson = JsonSerializer.Serialize(new[] { "--auto", "--mini" }),
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            WorkingDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            AuthenticationMode = authenticationMode,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = JsonSerializer.Serialize(secretNames ?? []),
            ModelArgumentName = modelArgumentName,
            Guidance = "Launch test",
            CreatedAt = now
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();

        profile.ActiveRevisionId = revision.Id;
        foreach (var secretName in secretNames ?? [])
        {
            db.AgentTuiSecrets.Add(new AgentTuiSecret
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Name = secretName,
                Ciphertext = protector.Protect(profile.Id, secretName, canary),
                ProtectionVersion = "v1",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        foreach (var model in models ?? [])
        {
            db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Identifier = model,
                DisplayName = model,
                Source = AgentTuiModelSource.Operator,
                Availability = AgentTuiModelAvailability.Verified,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync();
        return (profile, revision);
    }
}

public sealed class RecordingLaunchSecretProtector : IAgentTuiSecretProtector
{
    private int _unprotectCalls;
    public int UnprotectCalls => Volatile.Read(ref _unprotectCalls);

    public string Protect(Guid profileId, string environmentName, string plaintext) =>
        $"cipher:{profileId:N}:{environmentName}:{plaintext}";

    public string Unprotect(Guid profileId, string environmentName, string protectedValue)
    {
        Interlocked.Increment(ref _unprotectCalls);
        var prefix = $"cipher:{profileId:N}:{environmentName}:";
        if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
            throw new CryptographicException("Purpose mismatch.");
        return protectedValue[prefix.Length..];
    }
}

internal sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
{
    public OptionsMonitorStub(T currentValue)
    {
        CurrentValue = currentValue;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
