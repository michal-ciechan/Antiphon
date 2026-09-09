using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
                Kind = AgentKind.ClaudeCode, WorkingDirectory = Path.GetTempPath(), CreatedAt = now, UpdatedAt = now };
            agentId = agent.Id;
            db.Agents.Add(agent);
            for (var i = 0; i < 2; i++) db.AgentSessions.Add(new AgentSession { Id = Guid.NewGuid(), StandingAgentId = agentId,
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
        foreach (var body in new object[] { new { fresh = true, resumeSessionId = id },
            new { fresh = true, retryContinuity = true }, new { resumeSessionId = id, retryContinuity = true } })
        {
            using var refused = await client.PostAsJsonAsync($"/api/agents/{agentId}/start", body);
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(2);
        (await verify.Agents.FindAsync(agentId))!.PersistentSessionId.ShouldBeNull();
    }
}

public sealed class StandingRecoveryWebAppFactory : AntiphonWebAppFactory;
