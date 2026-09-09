using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.Agents;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ClassDataSource<StandingRecoveryWebAppFactory>(Shared = SharedType.PerClass)]
public class StandingSessionRecoveryHttpTests(StandingRecoveryWebAppFactory factory)
{
    [Test]
    public async Task History_and_start_preserve_wire_contract_and_revalidate_eligibility()
    {
        using var client = factory.CreateClient();
        Guid agentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow.AddHours(-1);
            var agent = new Agent { Id = Guid.NewGuid(), Name = "HTTP recovery", Slug = $"http-{Guid.NewGuid():N}",
                Kind = AgentKind.ClaudeCode, WorkingDirectory = factory.RecoveryRoot, CreatedAt = now, UpdatedAt = now };
            agentId = agent.Id;
            db.Agents.Add(agent);
            for (var i = 0; i < 2; i++) db.AgentSessions.Add(new AgentSession { Id = Guid.NewGuid(), StandingAgentId = agentId,
                DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode, Cwd = agent.WorkingDirectory,
                Status = SessionStatus.Stopped, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            db.AgentSessions.Add(new AgentSession { Id = Guid.NewGuid(), StandingAgentId = Guid.NewGuid(),
                DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode, Cwd = agent.WorkingDirectory,
                Status = SessionStatus.Stopped, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            await db.SaveChangesAsync();
        }
        using var first = await client.GetAsync($"/api/agents/{agentId}/sessions?take=1");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await first.Content.ReadFromJsonAsync<JsonElement>();
        var row = page.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        row.GetProperty("ownershipEvidence").GetString().ShouldBe("Stamped");
        row.TryGetProperty("delegationTokenHash", out _).ShouldBeFalse();
        row.TryGetProperty("env", out _).ShouldBeFalse();
        var id = row.GetProperty("id").GetGuid();
        var next = await client.GetFromJsonAsync<JsonElement>($"/api/agents/{agentId}/sessions?take=1&before={page.GetProperty("nextBefore").GetString()}");
        next.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldNotBe(id);
        next.GetProperty("nextBefore").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("eligible").GetBoolean().ShouldBeTrue();
        foreach (var body in new object[] { new { fresh = true, resumeSessionId = id },
            new { fresh = true, retryContinuity = true }, new { resumeSessionId = id, retryContinuity = true } })
        {
            using var refused = await client.PostAsJsonAsync($"/api/agents/{agentId}/start", body);
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Agents.FindAsync(agentId))!.PersistentSessionId.ShouldBeNull();
            (await db.AgentSessions.FindAsync(id))!.Status = SessionStatus.Starting;
            await db.SaveChangesAsync();
        }
        using (var stale = await client.PostAsJsonAsync($"/api/agents/{agentId}/start", new { resumeSessionId = id }))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await stale.Content.ReadAsStringAsync()).ShouldContain("standing_resume_target_active");
        }
        factory.Adapter.Started.ShouldBeFalse();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AgentSessions.FindAsync(id))!.Status = SessionStatus.Stopped;
            await db.SaveChangesAsync();
        }
        using (var accepted = await client.PostAsJsonAsync($"/api/agents/{agentId}/start", new { resumeSessionId = id, remoteControl = false }))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
            var detail = await accepted.Content.ReadFromJsonAsync<JsonElement>();
            detail.GetProperty("id").GetGuid().ShouldBe(agentId);
            detail.GetProperty("persistentSessionId").GetString().ShouldBe(id.ToString("D"));
        }
        await factory.Services.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(20), default);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(2);
            var held = (await db.AgentSupervisionStates.FindAsync(agentId))!;
            held.ContinuitySessionId.ShouldBe(id);
            held.ContinuityReason.ShouldBe(StandingContinuityReason.NativeSessionMissing);
            (await db.AgentSessions.FindAsync(id))!.RestartFailureKind.ShouldBe(RestartFailureKind.ContinuityUnavailable);
        }
        factory.Adapter.StartedArgs.ShouldContain("--resume");
        factory.Adapter.StartedArgs.ShouldNotContain("--session-id");
        factory.Adapter.Killed.ShouldBeTrue(); factory.Adapter.Disposed.ShouldBeTrue();
    }
}

public sealed class StandingRecoveryWebAppFactory : AntiphonWebAppFactory
{
    public string RecoveryRoot { get; } = Path.Combine(Path.GetTempPath(), $"c466-http-{Guid.NewGuid():N}");
    internal FakeAgentProtocolAdapter Adapter { get; } = new() { ReadyResult = false,
        StartupOutput = "No conversation found with session ID: synthetic missing target" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(RecoveryRoot);
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agents:DefaultDefinition"] = "http-claude",
            ["Agents:Definitions:http-claude:Kind"] = "ClaudeCode",
            ["Agents:Definitions:http-claude:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe")
        }));
    }
    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        // Both boundaries are in-memory fakes; Program retains its production-runner guard.
        services.RemoveAll<ISessionRunnerClient>();
        services.AddSingleton<ISessionRunnerClient>(new FakeSessionRunnerClient());
        services.RemoveAll<IAgentProtocolAdapterFactory>();
        services.AddSingleton<IAgentProtocolAdapterFactory>(new MissingAdapterFactory(Adapter));
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await AgentSupervisionTests.CleanupAsync(RecoveryRoot);
    }
    private sealed class MissingAdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        private int _created;
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (Interlocked.Increment(ref _created) != 1) throw new InvalidOperationException("Unexpected second/create attempt");
            return adapter;
        }
    }
}
