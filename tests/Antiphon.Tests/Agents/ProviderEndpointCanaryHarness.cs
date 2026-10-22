using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Agents;

// Shared seed/control path for portable sentinel proof and the operator-only real Codex canary.
// Only the real canary supplies a CLI/runner. Nothing here opens a credential file or a Codex home.
internal static class ProviderEndpointCanaryHarness
{
    internal const string KeyName = "c0167-canary";

    internal static AgentControlServiceIntegrationTests.Harness Build(
        string root, string connectionString, AgentKind kind, IReadOnlyList<IAgentProtocolAdapter> adapters,
        Action<IServiceCollection>? configure = null) =>
        AgentControlServiceIntegrationTests.BuildHarness(root, adapters, defaultKind: kind.ToString(),
            connectionString: connectionString, configureServices: services =>
            {
                services.AddSingleton<IApiKeyProtector, ApiKeyStoreTests.FakeApiKeyProtector>();
                services.AddScoped<ApiKeyEnvResolver>();
                configure?.Invoke(services);
            });

    internal static async Task<(Guid AgentId, Guid ProjectId)> SeedAsync(
        string connectionString, string root, AgentKind kind, string baseUrl, string syntheticKey,
        IReadOnlyDictionary<string, string>? launchEnv, CancellationToken ct)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var graph = RealCliStubBServerHarness.CreateGraph(root);
        // Cardless Start must exercise the agent path, without auto-picking the graph's sample card.
        graph.Card.Board.Cards.Clear();
        graph.Card.BoardColumn.Cards.Clear();
        graph.Project.ProviderEndpointsJson = ProviderEndpoint.SerializeMap(
            new Dictionary<AgentKind, ProviderEndpoint> { [kind] = new(baseUrl, KeyName) });
        db.Add(graph.Project);
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(), Name = "Endpoint canary", Slug = $"endpoint-{Guid.NewGuid():N}",
            Kind = kind, BoardId = graph.Card.BoardId, WorkingDirectory = root,
            Status = AgentStatus.Idle, CreatedAt = now, UpdatedAt = now,
            LaunchEnvJson = launchEnv is null ? "{}" : AgentLaunchEnv.Serialize(launchEnv),
        };
        db.Agents.Add(agent);
        var protector = new ApiKeyStoreTests.FakeApiKeyProtector();
        foreach (var projectId in new Guid?[] { null, graph.Project.Id })
        {
            var id = Guid.NewGuid();
            db.ApiKeys.Add(new ApiKey
            {
                Id = id, Name = KeyName, ProjectId = projectId,
                Ciphertext = protector.Protect(id, projectId.HasValue ? syntheticKey : "sentinel-global-decoy"),
                CreatedAt = now, UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync(ct);
        return (agent.Id, graph.Project.Id);
    }

    internal static void AssertProjection(AgentKind kind, string baseUrl, string syntheticKey,
        IReadOnlyDictionary<string, string> env, IReadOnlyList<string> args)
    {
        var keyVariable = kind == AgentKind.Codex ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY";
        env.ShouldContainKey(keyVariable);
        // Boolean comparison avoids rendering even a sentinel credential on assertion failure.
        (env[keyVariable] == syntheticKey).ShouldBeTrue("the project-scoped sentinel key must reach the child env");
        args.Any(arg => arg.Contains(syntheticKey, StringComparison.Ordinal)).ShouldBeFalse("key must not enter argv");
        args.Any(arg => arg.Contains("{{key:", StringComparison.Ordinal)).ShouldBeFalse("no key placeholder in argv");
        if (kind == AgentKind.ClaudeCode)
        {
            env["ANTHROPIC_BASE_URL"].ShouldBe(baseUrl);
            return;
        }

        var providerArgs = args.Where(arg => arg.StartsWith("model_providers.antiphon.", StringComparison.Ordinal)
            || arg.StartsWith("model_provider=", StringComparison.Ordinal)).ToArray();
        providerArgs.ShouldBe(new[]
        {
            "model_providers.antiphon.name=\"Antiphon endpoint\"",
            $"model_providers.antiphon.base_url=\"{baseUrl.TrimEnd('/')}/v1\"",
            "model_providers.antiphon.env_key=\"OPENAI_API_KEY\"",
            "model_providers.antiphon.wire_api=\"responses\"",
            "model_provider=antiphon",
        });
        foreach (var arg in providerArgs)
        {
            var index = args.ToList().IndexOf(arg);
            index.ShouldBeGreaterThan(0);
            args[index - 1].ShouldBe("-c");
        }
    }

    internal sealed class RegistryOptions(AgentRegistrySettings value) : IOptionsMonitor<AgentRegistrySettings>
    {
        public AgentRegistrySettings CurrentValue => value;
        public AgentRegistrySettings Get(string? name) => value;
        public IDisposable? OnChange(Action<AgentRegistrySettings, string?> listener) => null;
    }
}
