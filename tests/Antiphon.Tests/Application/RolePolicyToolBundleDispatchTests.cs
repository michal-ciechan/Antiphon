using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0507 S1 — a role-policy bundle key that is not a tool bundle refuses at create (422
/// <c>stage_tool_bundle_unknown</c>, over the service and over HTTP) and at dispatch before the
/// claim, so nothing launches. Each test owns an isolated schema.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class RolePolicyToolBundleDispatchTests
{
    private const string UnknownKey = "tool-does-not-exist";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    // V-3a. Mutation: CreateAsync no longer calls the resolver -> no throw, red.
    [Test]
    public async Task create_refuses_a_review_whose_role_names_an_unknown_tool_bundle_and_writes_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var workspace = Directory.CreateTempSubdirectory("antiphon-c507-create").FullName;
        try
        {
            var service = CreateService(db, s => s.RolePolicy["Review"].Bundles = [UnknownKey]);

            var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Shared),
                ManualCaller(workspace),
                CancellationToken.None));

            ex.Code.ShouldBe("stage_tool_bundle_unknown");
            var error = ex.Errors["RolePolicy"].ShouldHaveSingleItem();
            error.ShouldContain("Review");
            error.ShouldContain(UnknownKey);
            (await db.AgentTasks.CountAsync()).ShouldBe(0, "a refused create writes no task");
            (await db.AgentSessions.CountAsync()).ShouldBe(0, "a refused create writes no session");

            // One flip: only the configured role is refused.
            var docs = await service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "docs", Role: AgentTaskRole.Docs, Workspace: WorkspaceMode.Shared),
                ManualCaller(workspace),
                CancellationToken.None);
            (await db.AgentTasks.AsNoTracking().SingleAsync()).Id.ShouldBe(docs.Id);
        }
        finally
        {
            DeleteQuietly(workspace);
        }
    }

    // V-3b. Mutation: as V-3a (the problem+json shape is the existing middleware) -> 201, red.
    [Test]
    public async Task post_agent_tasks_returns_422_stage_tool_bundle_unknown_as_problem_details()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await CreateHost.StartAsync(schema.ConnectionString, UnknownKey);

        var refused = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);

        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            var root = problem.RootElement;
            root.GetProperty("code").GetString().ShouldBe("stage_tool_bundle_unknown");
            root.GetProperty("status").GetInt32().ShouldBe(422);
            var error = root.GetProperty("errors").GetProperty("RolePolicy")[0].GetString();
            error.ShouldNotBeNull();
            error.ShouldContain("Review");
            error.ShouldContain(UnknownKey);
        }

        var accepted = await host.Client.PostAsJsonAsync("/api/agent-tasks", new CreateAgentTaskRequest(
            "docs", Role: AgentTaskRole.Docs, Workspace: WorkspaceMode.Worktree,
            WorkingDirectory: host.RepoRoot), Json);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await accepted.Content.ReadFromJsonAsync<AgentTaskCreatedDto>(Json)).ShouldNotBeNull();
    }

    // V-3c. A task queued before the configuration changed. Mutation: the dispatcher's resolver
    // call removed -> the Review task is dispatched with a session, red.
    [Test]
    public async Task the_dispatcher_refuses_a_queued_review_before_the_claim_and_launches_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sink = new RecordingSink();
        await using var provider = BuildProvider(schema.ConnectionString, sink, UnknownKey);
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reviewDir = Directory.CreateTempSubdirectory("antiphon-c507-review").FullName;
        var docsDir = Directory.CreateTempSubdirectory("antiphon-c507-docs").FullName;
        try
        {
            var review = await SeedQueuedAsync(db, reviewDir, AgentTaskRole.Review);
            var docs = await SeedQueuedAsync(db, docsDir, AgentTaskRole.Docs);

            await dispatcher.TickAsync(CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var reviewRow = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == review.Id);
            reviewRow.Status.ShouldBe(AgentTaskStatus.Failed);
            reviewRow.AgentSessionId.ShouldBeNull("nothing launched for the refused task");
            reviewRow.DispatchedAt.ShouldBeNull();
            var failed = await verify.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == review.Id && e.Type == AgentTaskEventType.Failed)
                .Select(e => e.Detail)
                .SingleAsync();
            failed.ShouldContain("stage_tool_bundle_unknown");
            failed.ShouldContain("Review");
            failed.ShouldContain(UnknownKey);

            // The same tick dispatches a role with no tool bundles, so "no session" above is the
            // refusal and not a fixture that never launches.
            var docsRow = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == docs.Id);
            docsRow.Status.ShouldBe(AgentTaskStatus.Dispatched);
            docsRow.AgentSessionId.ShouldNotBeNull();
            (await verify.AgentSessions.CountAsync()).ShouldBe(1);
            sink.Items.ShouldHaveSingleItem().SessionId.ShouldBe(docsRow.AgentSessionId!.Value);
        }
        finally
        {
            DeleteQuietly(reviewDir);
            DeleteQuietly(docsDir);
        }
    }

    // V-3d (review d88d4eec F2). The refusal's caller note, producer to recipient: the tick's
    // FailAndNotifyAsync into the parent's real SessionMessageQueueService on a BridgeQueueHarness,
    // whose fake adapter records each typed body as a transcript UserPrompt (the
    // ReadyGateFailureNoticeDeliveryTests shape; no ConPTY on the Linux runner). A queue row is not
    // the receipt; one complete matching UserPrompt is.
    // eligible: an idle parent takes the note inline.
    // busy: an open turn keeps the row Pending and untyped; the turn-end flush types it once.
    // enqueue-failure: the insert save throws; the durable failure obligation recovers the saved
    // payload on a later tick without arming a legacy reminder.
    // crash-after-enqueue: the insert commits and the pre-type Sent save throws; after a restart a
    // outbox scan reuses the Pending row, and the stranded flush types it once.
    // Mutation: drop the enqueue in FailAndNotifyAsync -> no row after the refusing tick, red.
    [Test]
    [Timeout(120_000)]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("enqueue-failure")]
    [Arguments("crash-after-enqueue")]
    public async Task the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt(
        string path, CancellationToken ct)
    {
        var cut = path switch
        {
            "enqueue-failure" => NoticeCut.Insert,
            "crash-after-enqueue" => NoticeCut.SentStamp,
            "eligible" or "busy" => NoticeCut.None,
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, "unknown V-3d path"),
        };
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var store = schema.ConnectionString;
        var fault = new NoticeFault { Mode = cut };
        var parent = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = store,
            PreserveDatabaseOnDispose = cut == NoticeCut.SentStamp,
            ConfigureDbContext = cut == NoticeCut.None ? null : o => o.AddInterceptors(fault),
        });
        await parent.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "caller baseline before delegation");
        await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        fault.SessionId = parent.SessionId;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sink = new RecordingSink();
        var provider = BuildProvider(store, sink, UnknownKey, parent, clock);
        BridgeQueueHarness? restarted = null;
        var parentDisposed = false;
        var reviewDir = Directory.CreateTempSubdirectory("antiphon-c507-note").FullName;
        Guid? taskId = null;
        try
        {
            AgentTask review;
            await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(store)))
                review = await SeedQueuedAsync(seed, reviewDir, AgentTaskRole.Review, parent.SessionId);
            taskId = review.Id;

            if (path == "busy")
            {
                await parent.MarkWorkingAsync();
                (await NotePromptsAsync(store, parent.SessionId)).ShouldBeEmpty("control: the open turn has no note");
            }

            await TickAsync(provider, ct);

            var failed = await ReadTaskAsync(store, review.Id);
            failed.Status.ShouldBe(AgentTaskStatus.Failed, path);
            failed.AgentSessionId.ShouldBeNull(path);
            failed.DispatchedAt.ShouldBeNull(path);
            failed.FailureReason.ShouldNotBeNull();
            failed.FailureReason.ShouldContain("stage_tool_bundle_unknown");
            sink.Items.ShouldBeEmpty($"{path}: nothing launched");

            if (path == "enqueue-failure")
            {
                fault.Fired.ShouldBeTrue("control: the enqueue save faulted");
                (await RowsAsync(store, parent.SessionId)).ShouldBeEmpty("a failed enqueue commits no row");
                (await NotePromptsAsync(store, parent.SessionId)).ShouldBeEmpty("a failed enqueue types nothing");
                failed.CompletionNoteQueuedAt.ShouldBeNull("a failed insert leaves the task unstamped");
                failed.NextCheckAt.ShouldBeNull("the outbox owns delivery");
                await using var debtDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(store));
                var obligation = await debtDb.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == review.Id);
                var due = obligation.NextAttemptAt;

                fault.Mode = NoticeCut.None;
                AdvanceTo(clock, due);
                await TickAsync(provider, ct);
                var recovered = (await RowsAsync(store, parent.SessionId)).ShouldHaveSingleItem("the outbox recovers one keyed note");
                recovered.SourceLandNotificationId.ShouldBe(obligation.Id);
                recovered.Body.ShouldBe(obligation.Body);
                await parent.Queue.FlushStrandedQueuesAsync(ct);
                await AssertDeliveredOnceAsync(parent, review.Id, recovered.Id, path);

                clock.Advance(TimeSpan.FromHours(2));
                await TickAsync(provider, ct);
                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await AssertDeliveredOnceAsync(parent, review.Id, recovered.Id, path + " replay");
                (await ReadTaskAsync(store, review.Id)).NextCheckAt.ShouldBeNull("no legacy reminder is armed");
            }
            else if (path == "crash-after-enqueue")
            {
                fault.Fired.ShouldBeTrue("control: the pre-type Sent save faulted");
                fault.SawInsert.ShouldBeTrue("control: the insert committed before the cut");
                var pending = (await RowsAsync(store, parent.SessionId)).ShouldHaveSingleItem("the committed row survives the crash");
                pending.Status.ShouldBe(QueuedMessageStatus.Pending, path);
                pending.DeliveryAttempts.ShouldBe(0, path);
                pending.SourceTaskId.ShouldBe(review.Id);
                failed.CompletionNoteQueuedAt.ShouldNotBeNull("the stamp committed with the insert");
                failed.CompletionNoteDigest.ShouldBe(pending.ContentDigest);
                (await NotePromptsAsync(store, parent.SessionId)).ShouldBeEmpty("the crash is before typing");
                failed.NextCheckAt.ShouldBeNull("the outbox owns delivery");
                await using var debtDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(store));
                var obligation = await debtDb.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == review.Id);
                var due = obligation.NextAttemptAt;

                var sessionId = parent.SessionId;
                var agentId = parent.AgentId;
                await provider.DisposeAsync();
                await parent.DisposeAsync();
                parentDisposed = true;

                restarted = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
                {
                    AlwaysOn = true,
                    ConnectionString = store,
                    AttachSessionId = sessionId,
                    AttachAgentId = agentId,
                });
                provider = BuildProvider(store, sink, UnknownKey, restarted, clock);
                AdvanceTo(clock, due);
                await TickAsync(provider, ct);
                var still = (await RowsAsync(store, sessionId)).ShouldHaveSingleItem("the outbox reuses the committed keyed row");
                still.Id.ShouldBe(pending.Id);
                still.Status.ShouldBe(QueuedMessageStatus.Pending, "reconciliation leaves typing to the queue");
                (await NotePromptsAsync(store, sessionId)).ShouldBeEmpty();

                (await restarted.Queue.FlushStrandedQueuesAsync(ct))
                    .ShouldBeGreaterThan(0, "the restarted queue delivers the committed row");
                await AssertDeliveredOnceAsync(restarted, review.Id, pending.Id, path);
                clock.Advance(TimeSpan.FromHours(2));
                await TickAsync(provider, ct);
                (await restarted.Queue.FlushStrandedQueuesAsync(ct)).ShouldBe(0, "a second flush has nothing left to type");
                await restarted.Queue.OnTurnEndAsync(sessionId, ct);
                await AssertDeliveredOnceAsync(restarted, review.Id, pending.Id, path + " replay");
            }
            else if (path == "busy")
            {
                var pending = (await RowsAsync(store, parent.SessionId)).ShouldHaveSingleItem("the open turn keeps the note");
                pending.Status.ShouldBe(QueuedMessageStatus.Pending, path);
                pending.DeliveryAttempts.ShouldBe(0, path);
                (await NotePromptsAsync(store, parent.SessionId)).ShouldBeEmpty("busy: nothing is typed into the open turn");
                parent.Adapter.SubmittedBodies.ShouldNotContain(
                    b => b.Contains("stage_tool_bundle_unknown", StringComparison.Ordinal));

                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                var held = (await RowsAsync(store, parent.SessionId)).ShouldHaveSingleItem("becoming eligible does not drop the row");
                held.Id.ShouldBe(pending.Id);
                held.Status.ShouldBe(QueuedMessageStatus.Pending, "becoming eligible does not type on its own");
                (await NotePromptsAsync(store, parent.SessionId)).ShouldBeEmpty();

                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await AssertDeliveredOnceAsync(parent, review.Id, pending.Id, path);
                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                clock.Advance(TimeSpan.FromHours(2));
                await TickAsync(provider, ct);
                await AssertDeliveredOnceAsync(parent, review.Id, pending.Id, path + " replay");
            }
            else
            {
                var row = (await RowsAsync(store, parent.SessionId)).ShouldHaveSingleItem("an idle parent takes the note inline");
                await AssertDeliveredOnceAsync(parent, review.Id, row.Id, path);
                clock.Advance(TimeSpan.FromHours(2));
                await TickAsync(provider, ct);
                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await AssertDeliveredOnceAsync(parent, review.Id, row.Id, path + " replay");
            }
        }
        finally
        {
            // The task references the parent session. Delete it before a harness dispose deletes that session.
            if (taskId is Guid owned)
            {
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store));
                await db.SessionQueuedMessages.Where(m => m.SourceTaskId == owned).ExecuteDeleteAsync();
                await db.AgentTaskLandNotifications.Where(n => n.TaskId == owned).ExecuteDeleteAsync();
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == owned).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == owned).ExecuteDeleteAsync();
            }
            await provider.DisposeAsync();
            if (restarted is not null)
                await restarted.DisposeAsync();
            if (!parentDisposed)
                await parent.DisposeAsync();
            DeleteQuietly(reviewDir);
        }
    }

    // V-4b. Mutation: the resolver checks Exists(key) only -> no throw, red.
    [Test]
    public async Task create_refuses_a_known_key_without_the_tool_prefix()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var workspace = Directory.CreateTempSubdirectory("antiphon-c507-prefix").FullName;
        try
        {
            InstructionBundles.Exists("stage-code").ShouldBeTrue("the key is in the catalog; only its prefix is wrong");
            var service = CreateService(db, s => s.RolePolicy["Review"].Bundles = ["stage-code"]);

            var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "review", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Shared),
                ManualCaller(workspace),
                CancellationToken.None));

            ex.Code.ShouldBe("stage_tool_bundle_unknown");
            var error = ex.Errors["RolePolicy"].ShouldHaveSingleItem();
            error.ShouldContain("stage-code");
            error.ShouldContain("tool-");
            (await db.AgentTasks.CountAsync()).ShouldBe(0);
        }
        finally
        {
            DeleteQuietly(workspace);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static AgentTaskService CreateService(AppDbContext db, Action<DelegationSettings> configure)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [],
        };
        configure(settings);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    internal static async Task<AgentTask> SeedQueuedAsync(
        AppDbContext db, string cwd, AgentTaskRole role, Guid? parentSessionId = null)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = $"queued {role}",
            Goal = $"queued {role}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        if (parentSessionId is Guid parent)
        {
            task.ReplyTo = AgentTaskReplyTo.Session;
            task.ParentSessionId = parent;
        }
        task.RootTaskId = task.Id;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    // Shape of PinnedProfileLaunchSpecTests.BuildProvider. The launch sink records instead of
    // starting a process, and the claude definition names a path that does not exist. With a
    // parent harness the dispatcher shares its runtime and queue; the queue keeps the harness's
    // system clock and the dispatcher's clock only drives the reminder ramp.
    internal static ServiceProvider BuildProvider(
        string connectionString, RecordingSink sink, string reviewBundle,
        BridgeQueueHarness? parent = null, TimeProvider? clock = null,
        IInterceptor? fault = null, bool checkEnabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            if (fault is not null) options.AddInterceptors(fault);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            CheckEnabled = checkEnabled,
            CheckMaxCount = checkEnabled ? 10 : 0,
            MaxConcurrentTasks = 512,
            PoolIdleRetireMinutes = 525_600,
            PoolMaxIdlePerDirectory = int.MaxValue,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Review"] = new() { Level = AgentModelLevel.Frontier, Bundles = [reviewBundle] },
            },
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.Definitions["claude"] = new AgentDefinition
            {
                Kind = "ClaudeCode",
                Exe = Path.Combine(Path.GetTempPath(), "antiphon-c507-no-such-claude"),
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        if (parent is null)
        {
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
        }
        else
        {
            services.AddSingleton(parent.Runtime);
            services.AddSingleton(parent.Queue);
        }
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c507-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<RecordingLaunchSecretProtector>();
        services.AddSingleton<IAgentTuiSecretProtector>(sp =>
            sp.GetRequiredService<RecordingLaunchSecretProtector>());
        services.AddSingleton<AgentTuiMetrics>();
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddScoped<AgentTuiLaunchResolver>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    internal static async Task TickAsync(ServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
    }

    private static void AdvanceTo(FakeTimeProvider clock, DateTime dueUtc)
    {
        var step = DateTime.SpecifyKind(dueUtc, DateTimeKind.Utc) - clock.GetUtcNow().UtcDateTime;
        clock.Advance(step > TimeSpan.Zero ? step + TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1));
    }

    internal static async Task AssertDeliveredOnceAsync(
        BridgeQueueHarness recipient, Guid taskId, Guid rowId, string path)
    {
        var row = (await RowsAsync(recipient.ConnectionString, recipient.SessionId)).ShouldHaveSingleItem(path);
        row.Id.ShouldBe(rowId, path);
        row.Status.ShouldBe(QueuedMessageStatus.Sent, path);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, path);
        row.Origin.ShouldBe(QueuedMessageOrigin.Delegation, path);
        row.SourceTaskId.ShouldBe(taskId, path);
        row.ConversationKey.ShouldBe($"task:{taskId:N}", path);
        row.NoteHeader.ShouldNotBeNull(path);
        row.NoteHeader.ShouldStartWith($"[task {DelegationReportFormatter.Short(taskId)} failed]");
        row.Body.ShouldContain("stage_tool_bundle_unknown");
        row.Body.ShouldContain("Delegation:RolePolicy:Review:Bundles");
        row.Body.ShouldContain(UnknownKey);

        var task = await ReadTaskAsync(recipient.ConnectionString, taskId);
        task.CompletionNoteQueuedAt.ShouldNotBeNull(path);
        task.CompletionNoteDigest.ShouldBe(DelegationNoteDigest.Compute(task.FailureReason), path);
        row.ContentDigest.ShouldBe(task.CompletionNoteDigest, path);

        var prompt = (await NotePromptsAsync(recipient.ConnectionString, recipient.SessionId)).ShouldHaveSingleItem(path);
        prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt, path);
        prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence.ShouldNotBeNull(path), path);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(recipient.ConnectionString));
        var obligation = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId);
        row.SourceLandNotificationId.ShouldBe(obligation.Id, path);
        row.Body.ShouldBe(obligation.Body, path);
        PromptSubmissionMatch.IsCompleteIn(row.Body, prompt.Text).ShouldBeTrue($"{path}: complete matching UserPrompt");
        recipient.Adapter.SubmittedBodies.Count(b => PromptSubmissionMatch.IsCompleteIn(row.Body, b))
            .ShouldBe(1, $"{path}: typed once");
    }

    internal static async Task<List<SessionQueuedMessage>> RowsAsync(string store, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store));
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
    }

    internal static async Task<List<TranscriptEntry>> NotePromptsAsync(string store, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store));
        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .OrderBy(t => t.Sequence)
            .ToListAsync();
        return rows.Where(t => t.Text != null
            && t.Text.Contains("stage_tool_bundle_unknown", StringComparison.Ordinal)).ToList();
    }

    internal static async Task<AgentTask> ReadTaskAsync(string store, Guid taskId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(store));
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
    }

    private enum NoticeCut
    {
        None = 0,
        Insert = 1,
        SentStamp = 2,
    }

    // ReadyGateFailureNoticeDeliveryTests.NoticeFault: Insert cuts the enqueue before the row
    // commits; SentStamp lets that commit through and throws on the later save that marks the same
    // session's row Sent, which is before typing.
    private sealed class NoticeFault : SaveChangesInterceptor
    {
        public NoticeCut Mode { get; set; }
        public Guid SessionId { get; set; }
        public bool Fired { get; private set; }
        public bool SawInsert { get; private set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            Cut(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Cut(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Cut(DbContext? context)
        {
            if (Fired || Mode == NoticeCut.None || context is null)
                return;
            var mine = context.ChangeTracker.Entries<SessionQueuedMessage>()
                .Where(e => e.Entity.AgentSessionId == SessionId)
                .ToList();
            if (Mode == NoticeCut.Insert && mine.Any(e => e.State == EntityState.Added))
            {
                Fired = true;
                throw new InvalidOperationException("c507: injected enqueue save fault");
            }

            if (Mode != NoticeCut.SentStamp)
                return;
            if (mine.Any(e => e.State == EntityState.Added))
                SawInsert = true;
            if (SawInsert && mine.Any(e => e.State == EntityState.Modified
                && e.Property(m => m.Status).CurrentValue == QueuedMessageStatus.Sent))
            {
                Fired = true;
                throw new InvalidOperationException("c507: injected pre-type sent stamp fault");
            }
        }
    }

    private static void DeleteQuietly(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }

    internal sealed class RecordingSink : IAgentTaskLaunchSink
    {
        public List<(Guid SessionId, Guid AgentId)> Items { get; } = [];

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Items.Add((sessionId, agentId));
    }

    // File-local Kestrel host in the shape of RunnerDefaultTests.DefaultsHost: the real
    // ExceptionMiddleware and AgentTaskService behind POST /api/agent-tasks.
    private sealed class CreateHost : IAsyncDisposable
    {
        public HttpClient Client { get; }
        public string RepoRoot { get; }
        private readonly WebApplication _app;

        private CreateHost(WebApplication app, string repoRoot)
        {
            _app = app;
            RepoRoot = repoRoot;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        }

        public static async Task<CreateHost> StartAsync(string connectionString, string reviewBundle)
        {
            var repo = FindRepo();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
            builder.Services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));
            builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, n =>
            {
                n.MigrationsAssembly("Antiphon.Server");
                n.SetPostgresVersion(16, 0);
            }));
            var settings = new DelegationSettings { AllowedRoots = [repo] };
            settings.RolePolicy["Review"].Bundles = [reviewBundle];
            builder.Services.AddSingleton(Options.Create(settings));
            builder.Services.AddSingleton<IEventBus, MockEventBus>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            builder.Services.AddSingleton<DelegationWorkspaceResolver>();
            builder.Services.AddScoped<AgentTaskService>();
            builder.Services.AddLogging();
            var app = builder.Build();
            app.UseMiddleware<ExceptionMiddleware>();
            app.MapPost("/api/agent-tasks", async (
                CreateAgentTaskRequest request, AgentTaskService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, new AgentTaskService.Caller(null, null, ""), ct);
                return Results.Created($"/api/agent-tasks/{created.Id}", created);
            });
            await app.StartAsync();
            return new CreateHost(app, repo);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static string FindRepo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
        }
    }
}
