using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0547 D-4: the /retry body flag reaches the service; an absent or empty body is false.
public sealed partial class AgentTaskCommitEndpointTests
{
    private async Task<(Guid TaskId, Guid SessionId, Guid EventId, string Digest)> SeedObligatedDispatchedTaskAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "retry commit recovery",
            Goal = "hold an obligation",
            Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Dispatched,
            AgentSessionId = sessionId,
            DispatchedAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        var eventId = Guid.NewGuid();
        var digest = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = eventId,
            AgentTaskId = id,
            Type = AgentTaskEventType.CommitRecoveryStarted,
            Detail = digest,
            At = DateTime.UtcNow.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
        return (id, sessionId, eventId, digest);
    }

    private RecordingSessionStopper Stopper() =>
        (RecordingSessionStopper)_factory.Services.GetRequiredService<IDelegateSessionStopper>();

    [Test]
    [Arguments("omitted")]
    [Arguments("empty-object")]
    [Arguments("false")]
    public async Task Retry_without_the_abandon_flag_is_a_409_commit_recovery_pending_problem(string body)
    {
        var (taskId, sessionId, eventId, digest) = await SeedObligatedDispatchedTaskAsync();
        using var client = _factory.CreateClient();
        var url = $"/api/agent-tasks/{taskId}/retry";

        using var response = body switch
        {
            "omitted" => await client.PostAsync(url, null),
            "empty-object" => await client.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json")),
            _ => await client.PostAsJsonAsync(url, new { abandonCommitRecovery = false }),
        };

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("commit_recovery_pending");
        problem.GetProperty("obligationEventId").GetString().ShouldBe(eventId.ToString("D"));
        problem.GetProperty("settlement").GetString().ShouldBe(digest);
        Stopper().Killed.ShouldNotContain(sessionId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task Retry_with_abandonCommitRecovery_true_requeues_through_the_endpoint()
    {
        var (taskId, sessionId, eventId, _) = await SeedObligatedDispatchedTaskAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{taskId}/retry", new { abandonCommitRecovery = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
        summary.GetProperty("status").ToString().ShouldBe("Queued");
        Stopper().Killed.ShouldContain(sessionId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == taskId
                && e.Type == AgentTaskEventType.CommitRecoveryAbandoned))
            .Detail.ShouldStartWith($"{eventId:D} requeue:Retried: ");
    }
}
