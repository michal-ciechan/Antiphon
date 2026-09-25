using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>CARD-0716 D-2: POST /api/operator/shutdown is operator-token gated and then stops the host.</summary>
[Category("Integration")]
public class OperatorShutdownEndpointTests
{
    [Test]
    public async Task Shutdown_without_the_operator_token_is_403_and_stops_nothing()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        using var response = await host.PostOperatorAsync(
            "/api/operator/shutdown", new { reason = "restart-apphost" }, token: null);
        ((int)response.StatusCode).ShouldBe(403);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("operator_token_required");
        await Task.Delay(500);
        host.App.Lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
    }

    [Test]
    public async Task Shutdown_with_the_token_answers_202_then_begins_stopping()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        using (var version = await host.Http.GetAsync("/api/version"))
        {
            version.EnsureSuccessStatusCode();
            var versionBody = await version.Content.ReadAsStringAsync();
            versionBody.ShouldContain("operator-shutdown-v1");
        }

        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = host.App.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        using var response = await host.PostOperatorAsync(
            "/api/operator/shutdown", new { reason = "restart-apphost" }, token);
        ((int)response.StatusCode).ShouldBe(202);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("accepted").GetBoolean().ShouldBeTrue();
        dto.GetProperty("pid").GetInt32().ShouldBe(Environment.ProcessId);

        var completed = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.ShouldBe(stopping.Task);
    }

    // CARD-0716 repair: the 202 body used to send StartingLaunches = 0 unconditionally.
    [Test]
    public async Task Shutdown_202_reports_the_starting_launch_count()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        var starting = Session("starting", SessionStatus.Starting, now);
        var startingWithoutTask = Session("idle", SessionStatus.Starting, now);
        var running = Session("running", SessionStatus.Running, now);
        db.AgentSessions.AddRange(starting, startingWithoutTask, running);
        await db.SaveChangesAsync();
        db.AgentTasks.Add(TaskOn(starting.Id, now));
        db.AgentTasks.Add(TaskOn(running.Id, now));
        await db.SaveChangesAsync();

        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        using var response = await host.PostOperatorAsync(
            "/api/operator/shutdown", new { reason = "restart-apphost" }, token);
        ((int)response.StatusCode).ShouldBe(202);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("startingLaunches").GetInt32().ShouldBe(1);
    }

    private static AgentSession Session(string name, SessionStatus status, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        DefinitionName = name,
        AgentKind = AgentKind.ClaudeCode,
        Status = status,
        Cwd = "/tmp",
        Cols = 80,
        Rows = 24,
        CreatedAt = now,
        StartedAt = now,
        LastSeenAt = now,
    };

    private static AgentTask TaskOn(Guid sessionId, DateTime now)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "starting launch",
            Goal = "count me only when the session is Starting",
            WorkingDirectory = "/tmp",
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = now,
            DispatchedAt = now,
        };
    }
}
