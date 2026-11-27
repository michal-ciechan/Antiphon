using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
public sealed class VerifiedSessionStopTests
{
    [Test]
    [Arguments("reassigned")]
    [Arguments("attached")]
    [Arguments("browser")]
    [Arguments("idle")]
    [Arguments("unconfirmed")]
    [Arguments("new-generation")]
    public async Task Stop_binds_to_the_verified_unattached_and_unowned_seat(string scenario)
    {
        var runner = new FakeSessionRunnerClient();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<ISessionRunnerClient>(runner),
        });
        harness.Runtime.TryRemove(harness.SessionId, out _);
        var id = harness.SessionId;
        var nativeAttach = scenario == "attached";
        var unowned = scenario is "idle" or "unconfirmed" or "new-generation";
        await using var db = harness.CreateObserverContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == id);
        session.AgentKind = nativeAttach ? AgentKind.ClaudeCode : AgentKind.Codex;
        session.SessionBackend = nativeAttach ? SessionBackend.Herdr : SessionBackend.PtyHost;
        var agent = await db.Agents.SingleAsync(a => a.Id == harness.AgentId);
        agent.IsPoolDelegate = true;
        agent.Status = AgentStatus.Idle;
        agent.PoolIdleSince = session.StartedAt;
        agent.BoardId = null;
        agent.RemoteControlEnabled = false;
        if (unowned)
            agent.PersistentSessionId = null;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            AgentSessionId = id, Sequence = 1, Kind = TranscriptKinds.TurnEnd,
            Timestamp = session.StartedAt, CreatedAt = session.StartedAt,
        });
        await db.SaveChangesAsync();
        await db.Entry(session).ReloadAsync();
        var generation = session.StartedAt;
        runner.GetOverride = (_, _) => Task.FromResult(new SessionRunnerSessionDto(id, 123, generation,
            scenario == "unconfirmed" || runner.KillCalls + runner.KillGenerationCalls.Count == 0 ? "Running" : "Exited",
            null, AgentExitReason.Unknown, 0, Backend: nativeAttach ? SessionBackends.Herdr : SessionBackends.PtyHost,
            TranscriptBound: true, HerdrOrigin: nativeAttach ? HerdrPaneOrigins.Attached : null, AcceptedStartedAt: generation));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddScoped(_ => harness.Scope.ServiceProvider.GetRequiredService<AgentSessionService>());
        builder.Services.AddScoped(_ => harness.CreateObserverContext());
        builder.Services.AddSingleton(harness.Runtime);
        builder.Services.AddSingleton(harness.Queue);
        builder.Services.AddSingleton(Options.Create(new AgentSessionSettings()));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<SessionTerminalAttachments>();
        builder.Services.AddScoped<VerifiedSessionStopService>();
        await using var app = builder.Build();
        app.UseMiddleware<ExceptionMiddleware>();
        app.MapGroup("/api/sessions").MapSessionKill();
        await app.StartAsync();
        if (scenario == "browser")
            app.Services.GetRequiredService<SessionTerminalAttachments>().TryAttach(id, "open-terminal").ShouldBeTrue();
        using var client = app.GetTestClient();
        await using (var inspection = app.Services.CreateAsyncScope())
        {
            var snapshot = await inspection.ServiceProvider.GetRequiredService<VerifiedSessionStopService>()
                .InspectAsync(id, CancellationToken.None);
            snapshot.Generation.ShouldBe(generation);
            snapshot.OwnerId.ShouldBe(unowned ? null : agent.Id);
            snapshot.OpenTaskId.ShouldBeNull();
            snapshot.TurnEndSequence.ShouldBe(1L);
            snapshot.Attachment.ShouldBe(scenario is "attached" or "browser" ? "Attached" : "Unattached");
        }
        var expected = scenario == "new-generation" ? generation.AddSeconds(-1) : generation;
        var path = $"/api/sessions/{id}/kill?expectedGeneration={Uri.EscapeDataString(expected.ToString("O"))}&expectedTurnEndSequence=1";
        if (!unowned)
            path += $"&expectedOwnerId={agent.Id}";

        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        HttpResponseMessage response;
        if (scenario == "reassigned")
        {
            // The real warm-claim lock order takes session KEY SHARE before binding the new task.
            // Keep that binding uncommitted until the stop is waiting on the same session row.
            await using var claim = await db.Database.BeginTransactionAsync(bound.Token);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"AgentSessions\" WHERE \"Id\" = {id} FOR KEY SHARE", bound.Token);
            var newTask = new AgentTask
            {
                Id = Guid.NewGuid(), AgentId = agent.Id, AgentSessionId = id, Status = AgentTaskStatus.Dispatched,
                WorkingDirectory = session.Cwd, CreatedAt = generation.AddSeconds(1),
            };
            newTask.RootTaskId = newTask.Id;
            db.AgentTasks.Add(newTask);
            await db.SaveChangesAsync(bound.Token);
            var stop = client.PostAsync(path, null, bound.Token);
            var holder = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
            await using var monitor = new NpgsqlConnection(harness.ConnectionString);
            bool waitedForClaim;
            try
            {
                await monitor.OpenAsync(bound.Token);
                await using var query = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE @holder = ANY(pg_blocking_pids(pid)))", monitor);
                query.Parameters.AddWithValue("holder", holder);
                do
                {
                    waitedForClaim = (bool)(await query.ExecuteScalarAsync(bound.Token))!;
                    if (!waitedForClaim && !stop.IsCompleted)
                        await Task.Delay(20, bound.Token);
                } while (!waitedForClaim && !stop.IsCompleted);
            }
            finally
            {
                await claim.CommitAsync(CancellationToken.None);
            }
            response = await stop;
            waitedForClaim.ShouldBeTrue("the stop must wait for the concrete dispatch row lock");
        }
        else
        {
            response = await client.PostAsync(path, null, bound.Token);
        }

        using (response)
        {
            await db.Entry(session).ReloadAsync();
            if (scenario == "idle")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var readback = await response.Content.ReadFromJsonAsync<JsonElement>();
                readback.GetProperty("status").GetString().ShouldBe("Exited");
                readback.GetProperty("openTaskId").ValueKind.ShouldBe(JsonValueKind.Null);
                runner.KillGenerationCalls.ShouldBe([(id, generation)]);
                session.Status.ShouldBe(SessionStatus.Stopped);
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                if (scenario == "unconfirmed")
                    runner.KillGenerationCalls.ShouldBe([(id, generation)]);
                else
                    runner.KillGenerationCalls.ShouldBeEmpty();
                session.Status.ShouldBe(SessionStatus.Running);
            }
            runner.KillCalls.ShouldBe(0);
        }
    }
}
