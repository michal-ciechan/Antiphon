using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class AgentPinnedInstructionEndpointTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private readonly List<Guid> _agentIds = [];

    public AgentPinnedInstructionEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        using var client = _factory.CreateClient();
        foreach (var id in _agentIds)
            await client.DeleteAsync($"/api/agents/{id}");
        _agentIds.Clear();
    }

    [Test]
    public async Task V03_headerless_operator_capture_get_and_revoke_succeed()
    {
        var agent = await CreateAgentAsync();
        using var client = _factory.CreateClient();
        var requestId = Guid.NewGuid();
        var capture = await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId, expectedRevision = 0, text = "operator pin", sourceNamespace = "kb", sourceKey = "row" });
        capture.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadAsync(capture);
        created.GetProperty("revision").GetInt32().ShouldBe(1);
        created.GetProperty("pins")[0].GetProperty("source").GetString().ShouldBe("Operator");
        created.GetProperty("reconciliation").GetProperty("status").GetString().ShouldBe("Pending");

        var replay = await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId, expectedRevision = 0, text = "operator pin", sourceNamespace = "kb", sourceKey = "row" });
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);

        var get = await client.GetAsync($"/api/agents/{agent}/pinned-instructions");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await ReadAsync(get);
        listed.GetProperty("pins").GetArrayLength().ShouldBe(1);

        var pinId = listed.GetProperty("pins")[0].GetProperty("id").GetGuid();
        var revoke = await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions/{pinId}/revoke",
            new { requestId = Guid.NewGuid(), expectedRevision = 1 });
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(revoke)).GetProperty("revision").GetInt32().ShouldBe(2);
        _factory.SessionRunner.LaunchAttempts.ShouldBeEmpty();
    }

    [Test]
    public async Task V03_own_live_session_token_captures_as_agent_source()
    {
        var agent = await CreateAgentAsync();
        var (token, _) = await SeedLiveSessionAsync(agent);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, token);
        var capture = await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "from the agent" });
        capture.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(capture);
        body.GetProperty("pins")[0].GetProperty("source").GetString().ShouldBe("Agent");
        body.GetProperty("ownSessionReadTarget").GetString().ShouldNotBeNullOrEmpty();
        body.GetProperty("ownSessionReadTarget").GetString()!.ShouldContain(agent.ToString("N"));
    }

    [Test]
    public async Task V03_token_present_failures_never_fall_back_to_operator()
    {
        var agent = await CreateAgentAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(AgentTaskEndpoints.TokenHeader, " ");
        var empty = await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "should fail" });
        empty.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(empty)).ShouldBe("pin_caller_mismatch");

        using var invalid = _factory.CreateClient();
        invalid.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, "not-a-real-token");
        var bad = await invalid.GetAsync($"/api/agents/{agent}/pinned-instructions");
        bad.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await worldPins(agent)).ShouldBe(0);
    }

    [Test]
    public async Task V03_wrong_stopped_task_capability_and_delegate_tokens_are_forbidden()
    {
        var agentA = await CreateAgentAsync();
        var agentB = await CreateAgentAsync();
        var (tokenA, _) = await SeedLiveSessionAsync(agentA);
        var (stoppedToken, _) = await SeedLiveSessionAsync(agentB, SessionStatus.Stopped);
        var taskToken = await SeedTaskTokenAsync(agentA, orchestrator: true);
        var capabilityToken = await SeedCapabilityTokenAsync();

        using var cross = _factory.CreateClient();
        cross.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, tokenA);
        var wrong = await cross.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "cross" });
        wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var stopped = _factory.CreateClient();
        stopped.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, stoppedToken);
        (await stopped.GetAsync($"/api/agents/{agentB}/pinned-instructions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var task = _factory.CreateClient();
        task.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, taskToken);
        (await task.GetAsync($"/api/agents/{agentA}/pinned-instructions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var capability = _factory.CreateClient();
        capability.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, capabilityToken);
        (await capability.PostAsJsonAsync(
            $"/api/agents/{agentA}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "cap" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await worldPins(agentA)).ShouldBe(0);
        (await worldPins(agentB)).ShouldBe(0);
    }

    [Test]
    public async Task V03_agent_cannot_forge_source_or_change_import_mode_or_touch_operator_pins()
    {
        var agent = await CreateAgentAsync();
        using var op = _factory.CreateClient();
        var created = await ReadAsync(await op.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "operator owned" }));
        var pinId = created.GetProperty("pins")[0].GetProperty("id").GetGuid();

        var (token, _) = await SeedLiveSessionAsync(agent);
        using var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, token);

        var forged = await agentClient.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 1, text = "forged", source = "Operator" });
        forged.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var mode = await agentClient.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 1, text = "mode", pinClaudeImportMode = "Dedicated" });
        mode.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var revoke = await agentClient.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions/{pinId}/revoke",
            new { requestId = Guid.NewGuid(), expectedRevision = 1 });
        revoke.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var patch = await agentClient.PatchAsJsonAsync(
            $"/api/agents/{agent}",
            new
            {
                name = "keep",
                workingDirectory = $@"D:\src\unused",
                details = "",
                assignmentPolicy = "AutoPick",
                pinClaudeImportMode = "Dedicated"
            });
        patch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task V03_foreign_pin_is_404_without_text_and_conflicts_are_409()
    {
        var agentA = await CreateAgentAsync();
        var agentB = await CreateAgentAsync();
        using var client = _factory.CreateClient();
        var created = await ReadAsync(await client.PostAsJsonAsync(
            $"/api/agents/{agentA}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "secret-canary-text" }));
        var pinId = created.GetProperty("pins")[0].GetProperty("id").GetGuid();

        var foreign = await client.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions/{pinId}/revoke",
            new { requestId = Guid.NewGuid(), expectedRevision = 0 });
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreignBody = await foreign.Content.ReadAsStringAsync();
        foreignBody.ShouldNotContain("secret-canary-text");

        var requestId = Guid.NewGuid();
        var first = await client.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions",
            new { requestId, expectedRevision = 0, text = "one" });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var replayConflict = await client.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions",
            new { requestId, expectedRevision = 0, text = "two" });
        replayConflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(replayConflict)).ShouldBe("pin_request_conflict");

        var revision = await client.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "stale" });
        revision.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(revision)).ShouldBe("pin_revision_conflict");

        await client.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 1, text = "keyed", sourceNamespace = "kb", sourceKey = "k" });
        var source = await client.PostAsJsonAsync(
            $"/api/agents/{agentB}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 2, text = "other", sourceNamespace = "kb", sourceKey = "k" });
        source.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(source)).ShouldBe("pin_source_conflict");
    }

    [Test]
    public async Task V03_operator_can_set_dedicated_and_reconcile_does_not_start_or_send()
    {
        var createdAgent = await CreateAgentJsonAsync();
        var agent = createdAgent.GetProperty("id").GetGuid();
        using var client = _factory.CreateClient();
        var startsBefore = _factory.SessionRunner.LaunchAttempts.Count;

        var patch = await client.PatchAsJsonAsync(
            $"/api/agents/{agent}",
            new
            {
                name = createdAgent.GetProperty("name").GetString(),
                workingDirectory = createdAgent.GetProperty("workingDirectory").GetString(),
                details = createdAgent.GetProperty("details").GetString(),
                assignmentPolicy = "AutoPick",
                pinClaudeImportMode = "Dedicated"
            });
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(patch)).GetProperty("pinClaudeImportMode").GetString().ShouldBe("Dedicated");

        await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions",
            new { requestId = Guid.NewGuid(), expectedRevision = 0, text = "need reconcile" });
        var reconcile = await client.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions/reconcile",
            new { expectedRevision = 1 });
        reconcile.StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.SessionRunner.LaunchAttempts.Count.ShouldBe(startsBefore);

        using var agentClient = _factory.CreateClient();
        var (token, _) = await SeedLiveSessionAsync(agent);
        agentClient.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, token);
        var agentReconcile = await agentClient.PostAsJsonAsync(
            $"/api/agents/{agent}/pinned-instructions/reconcile",
            new { expectedRevision = 1 });
        agentReconcile.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<int> worldPins(Guid agentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agentId);
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var json = await CreateAgentJsonAsync();
        return json.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreateAgentJsonAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/agents", new
        {
            name = $"CARD-0262 {suffix}",
            workingDirectory = $@"D:\src\card-0262-{suffix}"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await ReadAsync(response);
        _agentIds.Add(json.GetProperty("id").GetGuid());
        return json;
    }

    private async Task<(string Token, Guid SessionId)> SeedLiveSessionAsync(
        Guid agentId,
        SessionStatus status = SessionStatus.Running)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            StandingAgentId = agentId,
            DelegationTokenHash = AgentTaskService.HashToken(token),
            DefinitionName = "test-raw",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = agent.WorkingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            EndedAt = status is SessionStatus.Stopped or SessionStatus.Failed ? now : null
        };
        db.AgentSessions.Add(session);
        agent.PersistentSessionId = session.Id.ToString("D");
        await db.SaveChangesAsync();
        return (token, session.Id);
    }

    private async Task<string> SeedTaskTokenAsync(Guid agentId, bool orchestrator)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "pin-task",
            Goal = "pin-task",
            Kind = orchestrator ? AgentTaskKind.Orchestrator : AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = $@"D:\src\task-{id:N}",
            AgentId = agentId,
            Status = AgentTaskStatus.Working,
            TokenHash = AgentTaskService.HashToken(token),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<string> SeedCapabilityTokenAsync()
    {
        using var root = new TempDir();
        using var client = _factory.CreateClient();
        var name = $"pin-cap-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync(
            "/api/delegation-capabilities",
            new { name, roots = new[] { root.Path } });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await ReadAsync(response);
        return json.GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(text, Json);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var json = await ReadAsync(response);
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antiphon-pin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
