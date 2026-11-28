using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
    [Arguments("general-capability")]
    [Arguments("revoked-capability")]
    [Arguments("card-session")]
    [Arguments("stale-orchestrator")]
    [Arguments("ended-orchestrator")]
    [Arguments("orchestrator-task-token")]
    public Task Stop_refuses_credentials_without_a_live_standing_binding(string scenario) => RunStopAsync(scenario);

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

    [Test]
    public Task Unsent_stop_releases_the_generation_and_clears_attention_after_a_commit_cut() => RunStopAsync("unsent-expires");

    [Test]
    public Task Retrying_an_unsent_stop_reuses_the_original_intent() => RunStopAsync("unsent-retry");

    [Test]
    public Task Queued_input_is_held_until_cancellation_then_delivers_in_order_once() => RunStopAsync("queued-stop-cancel");

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
        var unsent = scenario is "unsent-expires" or "unsent-retry" or "queued-stop-cancel";
        var clock = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
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
        if (dispatchRace || scenario == "unsent-expires")
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
        var callerId = SeedCaller(db, session, token, scenario);
        await db.SaveChangesAsync();
        await db.Entry(session).ReloadAsync();
        var generation = session.StartedAt;
        runner.ListOverride = _ => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(
            [new(id, 123, generation, "Running", null, AgentExitReason.Unknown, 1, AcceptedStartedAt: generation),
             new(callerId, 124, generation, "Running", null, AgentExitReason.Unknown, 0, AcceptedStartedAt: generation)]);
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
        var cut = new PreparedCommitCut();
        builder.Services.AddScoped(_ => unsent
            ? new AppDbContext(TestDbFixture.CreateDbContextOptions(harness.ConnectionString, cut))
            : harness.CreateObserverContext());
        var operatorTokenPath = Path.Combine(harness.TempRoot, "test-stop-operator-token");
        builder.Services.Configure<PhoneHomeRunnerSettings>(settings => settings.OperatorTokenPath = operatorTokenPath);
        builder.Services.AddScoped(sp =>
            (ICurrentUser)sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                .HttpContext!.Items["CurrentUser"]!);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(harness.Runtime);
        builder.Services.AddSingleton(harness.Queue);
        builder.Services.AddSingleton(Options.Create(new AgentSessionSettings()));
        // The dispatcher barrier coordinates PostgreSQL locks. Its duration says nothing
        // about runner I/O, so the exercise must not race the production read deadline.
        builder.Services.AddSingleton<TimeProvider>(dispatchRace || unsent
            ? clock
            : TimeProvider.System);
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
        if (unsent)
        {
            using var first = await client.PostAsync(path, null, bound.Token);
            first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            cut.Committed.ShouldBeTrue();
            runner.KillGenerationCalls.ShouldBeEmpty();
            (await SessionStopAudit.IsPendingAsync(db, id, bound.Token)).ShouldBeTrue();
            if (scenario == "unsent-retry")
            {
                using var retry = await client.PostAsync(path, null, bound.Token);
                retry.StatusCode.ShouldBe(HttpStatusCode.OK);
                runner.KillGenerationCalls.ShouldBe([(id, generation)]);
                (await db.AuditRecords.CountAsync(a => a.EventType == AuditEventType.SessionStop, bound.Token)).ShouldBe(1);
                return;
            }

            var messages = scenario == "queued-stop-cancel"
                ? await QueueWhileStopHeldAsync(harness, db, bound.Token) : [];
            clock.Advance(TimeSpan.FromSeconds(11));
            var attention = new AttentionService(db, runner, Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), clock, NullLogger<AttentionService>.Instance);
            (await attention.GetAsync(bound.Token)).Items.ShouldContain(x => x.ConditionKey != null
                && x.ConditionKey.StartsWith($"verified-stop:{id:N}:"));
            clock.Advance(TimeSpan.FromSeconds(50));
            var flushes = new CompletionNoteFlushQueue();
            var reconciliation = SessionReconciliationServiceTests.BuildService(db, runner, harness.EventBus,
                time: clock, inputFlushes: flushes);
            await reconciliation.ScanAsync(bound.Token);
            (await SessionStopAudit.IsPendingAsync(db, id, bound.Token)).ShouldBeFalse();
            (await attention.GetAsync(bound.Token)).Items.ShouldNotContain(x => x.ConditionKey != null
                && x.ConditionKey.StartsWith($"verified-stop:{id:N}:"));
            runner.KillGenerationCalls.ShouldBeEmpty();
            await db.Entry(session).ReloadAsync(bound.Token);
            session.Status.ShouldBe(SessionStatus.Running);
            if (scenario == "unsent-expires")
            {
                var pending = await AgentTaskPoolTests.SeedQueuedTaskAsync(session.Cwd, agent.ModelLevel,
                    pinnedAgentId: agent.Id, kind: AgentKind.Codex, connectionString: schema.ConnectionString);
                var (_, _, provider) = AgentTaskPoolTests.CreateHarness(connectionString: schema.ConnectionString);
                await using (provider)
                    (await TryWarmClaimAsync(provider, pending.Id, bound.Token)).ShouldBe(AgentTaskDispatcher.ReuseOutcome.Reused);
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == pending.Id, bound.Token))
                    .AgentSessionId.ShouldBe(id);
            }
            if (messages.Length > 0)
            {
                await using var wake = flushes.ReadAllAsync(bound.Token).GetAsyncEnumerator();
                (await wake.MoveNextAsync()).ShouldBeTrue();
                wake.Current.ShouldBe(id);
                foreach (var _ in messages)
                    await harness.Queue.FlushIfIdleAsync(id, bound.Token);
                await harness.Queue.FlushIfIdleAsync(id, bound.Token);
                harness.Adapter.SubmittedBodies.ShouldBe(messages);
                var prompts = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == id
                    && t.Kind == TranscriptKinds.UserPrompt).OrderBy(t => t.Sequence).Select(t => t.Text).ToListAsync(bound.Token);
                prompts.ShouldBe(messages);
                var deliveries = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == id)
                    .OrderBy(m => m.Sequence).ToListAsync(bound.Token);
                deliveries.Count.ShouldBe(2);
                deliveries.ShouldAllBe(m => m.DeliveryAttempts == 1 && m.DeliveryVerdict == DeliveryVerdict.Delivered);
            }
            return;
        }

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
            var (_, _, provider) = AgentTaskPoolTests.CreateHarness(connectionString: schema.ConnectionString);
            await using var owner = provider;
            await using (var warmup = provider.CreateAsyncScope())
            {
                var warmDb = warmup.ServiceProvider.GetRequiredService<AppDbContext>();
                await warmDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, bound.Token);
            }
            await using var monitor = new NpgsqlConnection(schema.ConnectionString);
            await monitor.OpenAsync(bound.Token);
            var stop = client.PostAsync(path, null, bound.Token);
            try
            {
                await fenceEntered.Task.WaitAsync(bound.Token);
                // Probe an actual PostgreSQL row conflict from the real dispatcher, not a timing
                // assumption about when its continuation reaches FOR KEY SHARE.
                var claimant = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var dispatch = TryWarmClaimAsync(provider, task.Id, bound.Token, claimant);
                try
                {
                    await using var query = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@claimant)) > 0", monitor);
                    query.Parameters.AddWithValue("claimant", await claimant.Task.WaitAsync(bound.Token));
                    bool waiting;
                    do
                    {
                        waiting = (bool)(await query.ExecuteScalarAsync(bound.Token))!;
                        if (!waiting && !dispatch.IsCompleted)
                            await Task.Delay(20, bound.Token);
                    } while (!waiting && !dispatch.IsCompleted);
                    if (!waiting)
                    {
                        var earlyOutcome = await dispatch;
                        waiting.ShouldBeTrue($"dispatch returned {earlyOutcome} before taking the real stop fence");
                    }
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

            var retry = await TryWarmClaimAsync(provider, task.Id, bound.Token);
            var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, bound.Token);
            dispatched.AgentSessionId.ShouldNotBe(id, "the dispatcher must never claim the stopped generation");
            if (scenario == "lost-readback-claim")
            {
                dispatched.Status.ShouldBe(AgentTaskStatus.Queued, "the pinned process is still reserved for reconciliation");
                retry.ShouldBe(AgentTaskDispatcher.ReuseOutcome.WaitForAgent);
                (await SessionStopAudit.IsPendingAsync(db, id, bound.Token)).ShouldBeTrue();
                clock.Advance(TimeSpan.FromSeconds(61));
                var reconciliation = SessionReconciliationServiceTests.BuildService(db, runner, harness.EventBus, time: clock);
                await reconciliation.ScanAsync(bound.Token);
                (await SessionStopAudit.IsPendingAsync(db, id, bound.Token)).ShouldBeTrue();
                using (var retryStop = await client.PostAsync(path, null, bound.Token))
                    retryStop.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                runner.KillGenerationCalls.ShouldBe([(id, generation)]);
                using var input = await client.PostAsJsonAsync($"/api/sessions/{id}/input", new { input = "held" }, bound.Token);
                input.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                inputs.ShouldBeEmpty();
                await QueueWhileStopHeldAsync(harness, db, bound.Token);
                await harness.Runtime.ObserveExitAsync(id, 0, AgentExitReason.KilledByRequest, bound.Token, generation);
                using (var replay = await client.PostAsync(path, null, bound.Token))
                    replay.StatusCode.ShouldBe(HttpStatusCode.OK);
                runner.KillGenerationCalls.ShouldBe([(id, generation)]);
            }
            else
            {
                retry.ShouldBe(AgentTaskDispatcher.ReuseOutcome.SpawnFresh);
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
            if (scenario is "invalid-token" or "no-token" or "worker-token" or "other-workspace" or "legacy-no-operator"
                or "general-capability" or "revoked-capability" or "card-session" or "stale-orchestrator"
                or "ended-orchestrator" or "orchestrator-task-token")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
                runner.KillGenerationCalls.ShouldBeEmpty();
                runnerReads.ShouldBe(1, "only the test's independent precondition inspection may reach the runner");
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
                entry.Summary.ShouldContain($"session:{callerId:D}");
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

    private static async Task<string[]> QueueWhileStopHeldAsync(BridgeQueueHarness harness, AppDbContext db, CancellationToken ct)
    {
        var id = harness.SessionId;
        harness.Runtime.Register(id, harness.Adapter);
        var refused = await Should.ThrowAsync<ConflictException>(() =>
            harness.Queue.EnqueueAsync(id, "Immediate input is held", MessageSendMode.Now, ct));
        refused.Message.ShouldBe("Session input is waiting for stop or transcript evidence.");
        string[] bodies = ["First held input", "Second held input"];
        foreach (var body in bodies)
            await harness.Queue.EnqueueAsync(id, body, MessageSendMode.WhenIdle, ct);
        var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == id)
            .OrderBy(m => m.Sequence).ToListAsync(ct);
        await harness.Queue.SendNowAsync(id, rows[0].Id, ct);
        await harness.Queue.FlushIfIdleAsync(id, ct);
        harness.Adapter.Inputs.ShouldBeEmpty();
        rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == id)
            .OrderBy(m => m.Sequence).ToListAsync(ct);
        rows.Select(m => m.Body).ShouldBe(bodies);
        rows.ShouldAllBe(m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0);
        return bodies;
    }

    private sealed class PreparedCommitCut : DbTransactionInterceptor
    {
        public bool Committed { get; private set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Committed) return Task.CompletedTask;
            Committed = true;
            throw new InvalidOperationException("Simulated request loss after PostgreSQL committed the intent");
        }
    }

    private static Guid SeedCaller(AppDbContext db, AgentSession target, string token, string scenario)
    {
        var id = Guid.NewGuid();
        var hash = AgentTaskService.HashToken(token);
        var now = target.StartedAt;
        if (scenario is "worker-token" or "orchestrator-task-token")
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, WorkingDirectory = target.Cwd, TokenHash = hash, CreatedAt = now,
                Kind = scenario == "worker-token" ? AgentTaskKind.Worker : AgentTaskKind.Orchestrator,
                Status = AgentTaskStatus.Working,
            });
            return id;
        }
        if (scenario is "general-capability" or "revoked-capability")
        {
            db.DelegationCapabilities.Add(new DelegationCapability
            {
                Id = id, Name = "stop-fixture", TokenHash = hash, CreatedAt = now,
                RootsJson = DelegationCapabilityRoots.Serialize([target.Cwd]),
                RevokedAt = scenario == "revoked-capability" ? now : null,
            });
            return id;
        }

        var source = new AgentSession
        {
            Id = id, Status = SessionStatus.Running, DelegationTokenHash = hash,
            Cwd = scenario == "other-workspace" ? Path.Combine(target.Cwd, "other") : target.Cwd,
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        };
        if (scenario == "ended-orchestrator")
        {
            source.Status = SessionStatus.Stopped;
            source.EndedAt = now;
        }
        db.AgentSessions.Add(source);
        if (scenario == "card-session")
        {
            var project = new Project { Id = Guid.NewGuid(), Name = "Card session project", CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Test board", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog", CreatedAt = now, UpdatedAt = now };
            var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
                Identifier = "CARD-0001", Title = "Card session", CreatedAt = now, UpdatedAt = now };
            db.AddRange(project, board, column, card);
            source.CardId = card.Id;
            return id;
        }

        var caller = new Agent
        {
            Id = Guid.NewGuid(), Name = "Standing orchestrator", Slug = "standing-orchestrator",
            WorkingDirectory = source.Cwd, Status = AgentStatus.Running, CreatedAt = now, UpdatedAt = now,
            PersistentSessionId = scenario == "stale-orchestrator" ? Guid.NewGuid().ToString("D") : id.ToString("D"),
        };
        source.StandingAgentId = caller.Id;
        db.Agents.Add(caller);
        db.AgentBundleAttachments.Add(new AgentBundleAttachment
        {
            AgentId = caller.Id, BundleKey = InstructionBundles.Orchestrator, CreatedAt = now,
        });
        return id;
    }

    private static async Task<AgentTaskDispatcher.ReuseOutcome> TryWarmClaimAsync(
        IServiceProvider provider, Guid taskId, CancellationToken ct, TaskCompletionSource<int>? claimant = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        claimant?.SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        var outcome = await dispatcher.TryReuseWarmAgentAsync(task, DateTime.UtcNow, ct);
        if (outcome == AgentTaskDispatcher.ReuseOutcome.Reused)
            await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return outcome;
    }
}
