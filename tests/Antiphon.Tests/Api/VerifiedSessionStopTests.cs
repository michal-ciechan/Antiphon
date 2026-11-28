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
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Server.Infrastructure.Realtime;
using Antiphon.Tests.Application;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
public sealed class VerifiedSessionStopTests
{
    [Test]
    public Task Stop_requires_the_scoped_orchestrator_credential() => RunStopAsync("invalid-token");

    [Test]
    [Arguments("no-token")]
    [Arguments("worker-token")]
    [Arguments("other-workspace")]
    public Task Stop_refuses_an_unscoped_caller(string scenario) => RunStopAsync(scenario);

    [Test]
    public Task Legacy_kill_records_authenticated_operator_and_outcome() => RunStopAsync("legacy-operator");

    [Test]
    public Task Legacy_kill_requires_the_operator_credential() => RunStopAsync("legacy-no-operator");

    [Test]
    [Arguments("reassigned")]
    [Arguments("attached")]
    [Arguments("browser")]
    [Arguments("idle")]
    [Arguments("unconfirmed")]
    [Arguments("new-generation")]
    public Task Stop_binds_to_the_verified_unattached_and_unowned_seat(string scenario) => RunStopAsync(scenario);

    [Test]
    public Task Stop_refuses_an_open_task() => RunStopAsync("open-task");

    [Test]
    public Task Stop_refuses_a_changed_task_revision() => RunStopAsync("revised-task");

    [Test]
    public Task Stop_refuses_a_new_turn_end_after_inspection() => RunStopAsync("changed-turn-end");

    [Test]
    public Task Stop_refuses_Claude_PtyHost_with_unknown_attachment() => RunStopAsync("claude-pty");

    [Test]
    public Task Stop_refuses_a_standing_seat() => RunStopAsync("standing");

    [Test]
    public Task Stop_refuses_an_AlwaysOn_Check_seat() => RunStopAsync("always-on-check");

    [Test]
    public Task Stop_refuses_mismatched_exit_generation() => RunStopAsync("wrong-exit-generation");

    [Test]
    public Task Input_POST_is_refused_while_stop_is_in_flight() => RunStopAsync("input-during-stop");

    [Test]
    public Task Terminal_opening_is_refused_while_stop_is_in_flight() => RunStopAsync("open-during-stop");

    [Test]
    public Task Terminal_reconnecting_is_refused_while_stop_is_in_flight() => RunStopAsync("reconnect-during-stop");

    [Test]
    public Task Lost_readback_keeps_warm_claims_off_the_stopped_generation() => RunStopAsync("lost-readback-claim");

    [Test]
    public Task Confirmed_exit_releases_the_intent_before_the_next_claim() => RunStopAsync("confirmed-claim");

    private static async Task RunStopAsync(string scenario)
    {
        var runner = new FakeSessionRunnerClient();
        var inputs = new List<string>();
        runner.SendInputOverride = (_, input, _) =>
        {
            inputs.Add(input);
            return Task.CompletedTask;
        };
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
        var browserRace = scenario is "input-during-stop" or "open-during-stop" or "reconnect-during-stop";
        var dispatchRace = scenario is "lost-readback-claim" or "confirmed-claim";
        await using var db = harness.CreateObserverContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == id);
        session.AgentKind = nativeAttach || scenario == "claude-pty" ? AgentKind.ClaudeCode : AgentKind.Codex;
        session.SessionBackend = nativeAttach ? SessionBackend.Herdr : SessionBackend.PtyHost;
        var agent = await db.Agents.SingleAsync(a => a.Id == harness.AgentId);
        agent.IsPoolDelegate = true;
        agent.Status = AgentStatus.Idle;
        agent.PoolIdleSince = session.StartedAt;
        agent.BoardId = null;
        agent.RemoteControlEnabled = false;
        if (scenario == "standing")
            session.StandingAgentId = agent.Id;
        if (scenario == "always-on-check")
        {
            agent.AlwaysOn = true;
            agent.StandingSpecialistRole = StandingSpecialistSeatPolicy.Role;
        }
        if (dispatchRace)
        {
            agent.Kind = AgentKind.Codex;
            agent.ModelLevel = AgentModelLevel.Medium;
            agent.PoolIdleSince = session.StartedAt.AddMinutes(-3);
        }
        if (unowned)
            agent.PersistentSessionId = null;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            AgentSessionId = id, Sequence = 1, Kind = TranscriptKinds.TurnEnd,
            Timestamp = session.StartedAt, CreatedAt = session.StartedAt,
        });
        if (scenario is "open-task" or "revised-task")
        {
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, AgentId = agent.Id, AgentSessionId = id,
                Status = scenario == "open-task" ? AgentTaskStatus.Working : AgentTaskStatus.Succeeded,
                WorkingDirectory = session.Cwd, CreatedAt = session.StartedAt.AddSeconds(-1),
                CompletedAt = scenario == "open-task" ? null : session.StartedAt,
            });
        }

        var token = Guid.NewGuid().ToString("N");
        var callerId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = callerId, RootTaskId = callerId,
            Kind = scenario == "worker-token" ? AgentTaskKind.Worker : AgentTaskKind.Orchestrator,
            Status = AgentTaskStatus.Working,
            WorkingDirectory = scenario == "other-workspace" ? Path.Combine(session.Cwd, "other") : session.Cwd,
            TokenHash = AgentTaskService.HashToken(token),
            CreatedAt = session.StartedAt,
        });
        await db.SaveChangesAsync();
        await db.Entry(session).ReloadAsync();
        var generation = session.StartedAt;
        var fenceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerReads = 0;
        runner.GetOverride = async (_, ct) =>
        {
            var read = Interlocked.Increment(ref runnerReads);
            var afterKill = runner.KillCalls + runner.KillGenerationCalls.Count != 0;
            // The inspection has already read once. The second read runs under the PostgreSQL
            // dispatch fence. Browser tests pause readback after the command has been issued.
            if ((dispatchRace || scenario == "input-during-stop") && read == 2
                || browserRace && scenario != "input-during-stop" && afterKill && !fenceEntered.Task.IsCompleted)
            {
                fenceEntered.TrySetResult();
                await releaseFence.Task.WaitAsync(ct);
            }
            if (scenario == "changed-turn-end" && read == 2)
            {
                await using var writer = harness.CreateObserverContext();
                writer.TranscriptEntries.Add(new TranscriptEntry
                {
                    AgentSessionId = id, Sequence = 2, Kind = TranscriptKinds.TurnEnd,
                    Timestamp = generation.AddSeconds(1), CreatedAt = generation.AddSeconds(1),
                });
                await writer.SaveChangesAsync(ct);
            }
            if (afterKill && scenario == "lost-readback-claim")
                throw new HttpRequestException("Runner stop readback was lost");
            var status = afterKill && scenario != "unconfirmed" ? "Exited" : "Running";
            var accepted = afterKill && scenario == "wrong-exit-generation" ? generation.AddSeconds(1) : generation;
            return new SessionRunnerSessionDto(id, 123, generation, status,
                null, AgentExitReason.KilledByRequest, 0,
                Backend: nativeAttach ? SessionBackends.Herdr : SessionBackends.PtyHost,
                TranscriptBound: true, HerdrOrigin: nativeAttach ? HerdrPaneOrigins.Attached : null,
                AcceptedStartedAt: accepted);
        };

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddScoped(_ => harness.Scope.ServiceProvider.GetRequiredService<AgentSessionService>());
        builder.Services.AddScoped(_ => harness.CreateObserverContext());
        builder.Services.AddScoped(sp => new AgentTaskService(sp.GetRequiredService<AppDbContext>(),
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()), harness.EventBus, new RecordingSessionStopper(),
            TimeProvider.System, NullLogger<AgentTaskService>.Instance));
        var operatorTokenPath = Path.Combine(harness.TempRoot, "test-stop-operator-token");
        builder.Services.Configure<PhoneHomeRunnerSettings>(settings => settings.OperatorTokenPath = operatorTokenPath);
        builder.Services.AddScoped(sp =>
            (ICurrentUser)sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                .HttpContext!.Items["CurrentUser"]!);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(harness.Runtime);
        builder.Services.AddSingleton(harness.Queue);
        builder.Services.AddSingleton(Options.Create(new AgentSessionSettings()));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<SessionTerminalAttachments>();
        builder.Services.AddScoped<VerifiedSessionStopService>();
        if (browserRace)
        {
            builder.Services.AddSignalR();
            builder.Services.AddScoped<IAuditService, AuditService>();
            builder.Services.AddScoped(_ => ActivatorUtilities.CreateInstance<AgentChannelService>(harness.Scope.ServiceProvider));
        }
        await using var app = builder.Build();
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseMiddleware<CurrentUserMiddleware>();
        var sessions = app.MapGroup("/api/sessions");
        sessions.MapSessionKill();
        sessions.MapSessionInput();
        if (browserRace)
            app.MapHub<AntiphonHub>("/hubs/antiphon");
        await app.StartAsync();
        if (scenario == "browser")
            app.Services.GetRequiredService<SessionTerminalAttachments>().TryAttach(id, "open-terminal").ShouldBeTrue();
        using var client = app.GetTestClient();
        if (scenario == "legacy-operator")
            client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(operatorTokenPath));
        else if (scenario != "no-token")
            client.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader,
                scenario == "invalid-token" ? "invalid-stop-test-token" : token);
        VerifiedSessionStopPreconditions snapshot;
        await using (var inspection = app.Services.CreateAsyncScope())
        {
            snapshot = await inspection.ServiceProvider.GetRequiredService<VerifiedSessionStopService>()
                .InspectAsync(id, CancellationToken.None);
            snapshot.Generation.ShouldBe(generation);
            snapshot.OwnerId.ShouldBe(unowned ? null : agent.Id);
            snapshot.OpenTaskId.HasValue.ShouldBe(scenario == "open-task");
            snapshot.TurnEndSequence.ShouldBe(1L);
            snapshot.Attachment.ShouldBe(scenario switch
            {
                "attached" or "browser" => "Attached",
                "claude-pty" => "Unknown",
                _ => "Unattached",
            });
            snapshot.ProtectedOwner.ShouldBe(scenario is "standing" or "always-on-check");
        }
        var expected = scenario == "new-generation" ? generation.AddSeconds(-1) : generation;
        var path = $"/api/sessions/{id}/kill?expectedGeneration={Uri.EscapeDataString(expected.ToString("O"))}&expectedTurnEndSequence=1";
        if (!unowned)
            path += $"&expectedOwnerId={agent.Id}";
        if (snapshot.LastTaskId is Guid taskIdFromInspection)
            path += $"&expectedLastTaskId={taskIdFromInspection}&expectedLastTaskRevision={snapshot.LastTaskRevision}";
        if (scenario == "revised-task")
            await db.AgentTasks.Where(t => t.Id == snapshot.LastTaskId).ExecuteUpdateAsync(setters =>
                setters.SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
        if (scenario is "legacy-no-operator" or "legacy-operator")
            path = $"/api/sessions/{id}/kill";

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
        else if (browserRace)
        {
            await using var terminal = new HubConnectionBuilder()
                .WithUrl(new Uri(client.BaseAddress!, "/hubs/antiphon"), options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
                }).Build();
            var group = $"session-{id:D}";
            if (scenario == "reconnect-during-stop")
            {
                await terminal.StartAsync(bound.Token);
                await terminal.InvokeAsync("JoinGroup", group, bound.Token);
                await terminal.StopAsync(bound.Token);
                var attachments = app.Services.GetRequiredService<SessionTerminalAttachments>();
                while (attachments.IsAttached(id))
                    await Task.Delay(20, bound.Token);
            }
            var stop = client.PostAsync(path, null, bound.Token);
            try
            {
                await fenceEntered.Task.WaitAsync(bound.Token);
                if (scenario == "input-during-stop")
                {
                    using var input = await client.PostAsJsonAsync($"/api/sessions/{id}/input", new { input = "forbidden" }, bound.Token);
                    input.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                    (await input.Content.ReadAsStringAsync(bound.Token)).ShouldContain("session_stop_in_flight");
                    inputs.ShouldBeEmpty();
                }
                else
                {
                    await terminal.StartAsync(bound.Token);
                    var refusal = await Should.ThrowAsync<HubException>(() => terminal.InvokeAsync("JoinGroup", group, bound.Token));
                    refusal.Message.ShouldContain("being stopped");
                }
            }
            finally
            {
                releaseFence.TrySetResult();
                response = await stop;
            }
            using (response)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                runner.KillGenerationCalls.ShouldBe([(id, generation)]);
            }
            return;
        }
        else if (dispatchRace)
        {
            var task = await AgentTaskPoolTests.SeedQueuedTaskAsync(session.Cwd, agent.ModelLevel,
                pinnedAgentId: agent.Id, kind: AgentKind.Codex, connectionString: schema.ConnectionString);
            var (dispatcher, _, provider) = AgentTaskPoolTests.CreateHarness(connectionString: schema.ConnectionString);
            await using var owner = provider;
            var stop = client.PostAsync(path, null, bound.Token);
            try
            {
                await fenceEntered.Task.WaitAsync(bound.Token);
                // Probe an actual PostgreSQL row conflict from the real dispatcher, not a timing
                // assumption about when its continuation reaches FOR KEY SHARE.
                var dispatch = dispatcher.TickAsync(bound.Token);
                try
                {
                    await using var monitor = new NpgsqlConnection(schema.ConnectionString);
                    await monitor.OpenAsync(bound.Token);
                    await using var query = new NpgsqlCommand(
                        "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() "
                        + "AND query LIKE '%FOR KEY SHARE%' AND cardinality(pg_blocking_pids(pid)) > 0)", monitor);
                    bool waiting;
                    do
                    {
                        waiting = (bool)(await query.ExecuteScalarAsync(bound.Token))!;
                        if (!waiting && !dispatch.IsCompleted)
                            await Task.Delay(20, bound.Token);
                    } while (!waiting && !dispatch.IsCompleted);
                    waiting.ShouldBeTrue("dispatch must meet the real stop fence");
                }
                finally
                {
                    releaseFence.TrySetResult();
                    await dispatch;
                }
            }
            finally
            {
                releaseFence.TrySetResult();
                response = await stop;
            }
            using (response)
                response.StatusCode.ShouldBe(scenario == "lost-readback-claim" ? HttpStatusCode.Conflict : HttpStatusCode.OK);

            var (retry, _, retryProvider) = AgentTaskPoolTests.CreateHarness(connectionString: schema.ConnectionString);
            await using var retryOwner = retryProvider;
            await retry.TickAsync(bound.Token);
            var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, bound.Token);
            dispatched.AgentSessionId.ShouldNotBe(id, "the dispatcher must never claim the stopped generation");
            if (scenario == "lost-readback-claim")
            {
                dispatched.Status.ShouldBe(AgentTaskStatus.Queued, "the pinned process is still reserved for reconciliation");
                (await SessionStopAudit.IsPendingAsync(db, id, bound.Token)).ShouldBeTrue();
                using var input = await client.PostAsJsonAsync($"/api/sessions/{id}/input", new { input = "held" }, bound.Token);
                input.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                inputs.ShouldBeEmpty();
                await harness.Runtime.ObserveExitAsync(id, 0, AgentExitReason.KilledByRequest, bound.Token, generation);
            }
            else
            {
                dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
                dispatched.AgentSessionId.ShouldNotBeNull();
            }
            (await SessionStopAudit.IsPendingAsync(db, id, bound.Token)).ShouldBeFalse();
            await db.Entry(session).ReloadAsync(bound.Token);
            session.Status.ShouldBe(SessionStatus.Stopped);
            return;
        }
        else
        {
            response = await client.PostAsJsonAsync(path, new { reason = "Retire the verified idle seat" }, bound.Token);
        }

        using (response)
        {
            await db.Entry(session).ReloadAsync();
            if (scenario is "invalid-token" or "no-token" or "worker-token" or "other-workspace" or "legacy-no-operator")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
                runner.KillGenerationCalls.ShouldBeEmpty();
                session.Status.ShouldBe(SessionStatus.Running);
            }
            else if (scenario == "legacy-operator")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                runner.KillCalls.ShouldBe(1);
                runner.KillGenerationCalls.ShouldBeEmpty();
                session.Status.ShouldBe(SessionStatus.Stopped);
                var entry = await db.AuditRecords.SingleAsync(a => a.EventType == AuditEventType.SessionStop);
                entry.Summary.ShouldContain("operator:admin");
                entry.Summary.ShouldContain(generation.ToString("O"));
                entry.Summary.ShouldContain("Retire the verified idle seat");
                entry.Summary.ShouldContain("Outcome: Exited");
                return;
            }
            else if (scenario == "idle")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var readback = await response.Content.ReadFromJsonAsync<JsonElement>();
                readback.GetProperty("status").GetString().ShouldBe("Exited");
                readback.GetProperty("openTaskId").ValueKind.ShouldBe(JsonValueKind.Null);
                runner.KillGenerationCalls.ShouldBe([(id, generation)]);
                session.Status.ShouldBe(SessionStatus.Stopped);
                readback.GetProperty("generation").GetDateTime().ShouldBe(generation);
                var entry = await db.AuditRecords.SingleAsync(a => a.EventType == AuditEventType.SessionStop);
                entry.Summary.ShouldContain($"task:{callerId:D}");
                entry.Summary.ShouldContain(generation.ToString("O"));
                entry.Summary.ShouldContain("Retire the verified idle seat");
                entry.Summary.ShouldContain("Outcome: Exited");
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                if (scenario is "unconfirmed" or "wrong-exit-generation")
                    runner.KillGenerationCalls.ShouldBe([(id, generation)]);
                else
                    runner.KillGenerationCalls.ShouldBeEmpty();
                session.Status.ShouldBe(SessionStatus.Running);
            }
            runner.KillCalls.ShouldBe(0);
        }
    }
}
