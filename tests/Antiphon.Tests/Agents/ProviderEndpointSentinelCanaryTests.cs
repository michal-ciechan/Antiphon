using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
[NotInParallel("AgentControl")]
public sealed class ProviderEndpointSentinelCanaryTests
{
    [Test]
    [Arguments(AgentKind.ClaudeCode)]
    [Arguments(AgentKind.Codex)]
    public async Task C0167_agent_launch_resolves_project_key_and_clear_removes_projection(AgentKind kind)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = Directory.CreateTempSubdirectory("c0167-sentinel-").FullName;
        try
        {
            var first = new FakeAgentProtocolAdapter();
            var cleared = new FakeAgentProtocolAdapter();
            await using var harness = ProviderEndpointCanaryHarness.Build(
                root, schema.ConnectionString, kind, [first, cleared]);
            const string url = "http://127.0.0.1:18087"; // never contacted by the capturing adapters
            var sentinel = $"sentinel-c0167-{Guid.NewGuid():N}";
            var (agentId, projectId) = await ProviderEndpointCanaryHarness.SeedAsync(
                schema.ConnectionString, root, kind, url, sentinel, null, CancellationToken.None);

            await harness.Control.StartAsync(agentId, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            first.Started.ShouldBeTrue();
            ProviderEndpointCanaryHarness.AssertProjection(kind, url, sentinel, first.StartedEnv, first.StartedArgs);
            await harness.Control.StopAsync(agentId, CancellationToken.None);

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var project = await db.Projects.SingleAsync(p => p.Id == projectId);
                project.ProviderEndpointsJson = "{}";
                await db.SaveChangesAsync();
            }
            using var scope = harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>()
                .StartAsync(agentId, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            cleared.Started.ShouldBeTrue();
            cleared.StartedEnv.ContainsKey(kind == AgentKind.Codex ? "OPENAI_API_KEY" : "ANTHROPIC_API_KEY")
                .ShouldBeFalse("cleared endpoint must not inject a credential");
            cleared.StartedEnv.ContainsKey("ANTHROPIC_BASE_URL").ShouldBeFalse();
            cleared.StartedArgs.Any(arg => arg.StartsWith("model_providers.antiphon.", StringComparison.Ordinal)
                || arg == "model_provider=antiphon").ShouldBeFalse();
        }
        finally
        {
            RealCliStubBServerHarness.TryDelete(root);
        }
    }
}
