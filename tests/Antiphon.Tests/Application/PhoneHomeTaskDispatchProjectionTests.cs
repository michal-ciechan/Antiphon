using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PhoneHomeTaskDispatchProjectionTests
{
    [Test]
    public Task Runner_bound_claude_task_reaches_the_queue_projected() =>
        VerifyProjectionAsync(AgentKind.ClaudeCode);

    [Test]
    public Task Runner_bound_grok_task_reaches_the_queue_projected() =>
        VerifyProjectionAsync(AgentKind.Grok);

    private static async Task VerifyProjectionAsync(AgentKind kind)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        const string mirror = "/work/worktrees/task-remote";
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse(mirror)),
            PhoneHomeOperation.ProviderAuth => Result(frame,
                new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null)),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, kind);
        var sink = new RecordingLaunchSink();
        var (dispatcher, preparer) = CreateDispatcher(schema, host, sink);

        // CARD-0633 D-12: the first tick commits the claim and hands the push + mirror to the
        // preparer (HeldForRemotePrep); the launch happens on the tick after the mirror is recorded.
        await dispatcher.TickAsync(CancellationToken.None);
        await preparer.WhenIdleAsync();
        await dispatcher.TickAsync(CancellationToken.None);

        var launch = sink.Specs.Single();
        launch.Cwd.ShouldBe(mirror);
        launch.Exe.ShouldBe(kind == AgentKind.Grok ? "grok" : "claude");
        launch.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net");
        launch.Env["GROK_HOME"].ShouldBe("/state/grok");
        launch.Env["CLAUDE_CONFIG_DIR"].ShouldBe("/state/claude");
        if (kind == AgentKind.ClaudeCode)
        {
            launch.Env["DISABLE_AUTOUPDATER"].ShouldBe("1");
            launch.Env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"].ShouldBe("1");
        }
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.AgentSessionId.ShouldNotBeNull();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerCwd.ShouldBe(mirror);
        session.Cwd.ShouldBe(workspace.Path);
    }

    [Test]
    public async Task Runner_bound_boot_wedge_relaunch_keeps_the_runner_binding_and_projection()
    {
        // Review be0f8640: the boot-wedge relaunch built a desktop-shaped session and an
        // unprojected spec, so a server2 task's retry could route to the desktop.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        const string mirror = "/work/worktrees/task-remote";
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse(mirror)),
            PhoneHomeOperation.ProviderAuth => Result(frame,
                new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null)),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.ClaudeCode);
        var sink = new RecordingLaunchSink();
        var (dispatcher, preparer) = CreateDispatcher(schema, host, sink);
        await dispatcher.TickAsync(CancellationToken.None);
        await preparer.WhenIdleAsync();
        await dispatcher.TickAsync(CancellationToken.None);

        Guid firstSessionId;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            firstSessionId = (await db.AgentTasks.SingleAsync(t => t.Id == taskId)).AgentSessionId!.Value;

        await dispatcher.RelaunchWedgedAsync(taskId, firstSessionId, CancellationToken.None);

        sink.Specs.Count.ShouldBe(2);
        var relaunch = sink.Specs[1];
        relaunch.Cwd.ShouldBe(mirror);
        relaunch.Exe.ShouldBe("claude");
        relaunch.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net");
        relaunch.Env["CLAUDE_CONFIG_DIR"].ShouldBe("/state/claude");

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.BootWedgeRelaunchCount.ShouldBe(1);
        task.AgentSessionId.ShouldNotBe(firstSessionId);
        var first = await verify.AgentSessions.SingleAsync(s => s.Id == firstSessionId);
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerId.ShouldBe(host.AllowedRunnerId);
        session.RunnerCwd.ShouldBe(mirror);
        session.RunnerStoreId.ShouldNotBeNull();
        session.RunnerStoreId.ShouldBe(first.RunnerStoreId);
        session.Cwd.ShouldBe(workspace.Path);
    }

    [Test]
    public async Task Runner_bound_task_launch_reaches_runner_start_with_a_sourced_task_on_record()
    {
        // CARD-0645 item 1: one SourceLanding row anywhere in the database made every runner
        // launch canonicalize the runner cwd on the desktop filesystem (path_missing_or_inaccessible).
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        const string mirror = "/work/worktrees/task-remote";
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse(mirror)),
            PhoneHomeOperation.ProviderAuth => Result(frame,
                new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null)),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        using var snapshot = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.Grok);
        await SeedSourcedTaskAsync(schema, snapshot.Path);
        var sink = new RecordingLaunchSink();
        var (dispatcher, preparer) = CreateDispatcher(schema, host, sink);

        // CARD-0633 D-12: the mirror is recorded between ticks; the launch is the second tick.
        await dispatcher.TickAsync(CancellationToken.None);
        await preparer.WhenIdleAsync();
        await dispatcher.TickAsync(CancellationToken.None);

        var launch = sink.Specs.Single();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        var git = new LandingGit();
        var verification = new VerificationExecutionService(
            db, new SourceLandingAdmission(db, git, host.Directory), TimeProvider.System, git);
        var prepared = await verification.PrepareLaunchAsync(session, launch, CancellationToken.None);
        await new PhoneHomeRunnerClient(live).StartAsync(session.Id, prepared, CancellationToken.None);

        var started = peer.Launches.ShouldHaveSingleItem().Payload!.Value
            .Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)!;
        started.SessionId.ShouldBe(session.Id);
        started.Cwd.ShouldBe(mirror);
    }

    [Test]
    public async Task Card_bound_runner_worktree_task_dispatches()
    {
        // CARD-0645 item 2: a card-bound delegated task is not a card start. Its session has no
        // CardId and runs in the runner mirror, so the card-start refusal must not apply.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        const string mirror = "/work/worktrees/task-remote";
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse(mirror)),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.Grok);
        await BindCardAsync(schema, taskId);
        var sink = new RecordingLaunchSink();
        var (dispatcher, preparer) = CreateDispatcher(schema, host, sink);

        // CARD-0633 D-12: card-bound delegated tasks still prepare the mirror off the claim, then launch.
        await dispatcher.TickAsync(CancellationToken.None);
        await preparer.WhenIdleAsync();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.CardId.ShouldNotBeNull();
        sink.Specs.ShouldHaveSingleItem().Cwd.ShouldBe(mirror);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.CardId.ShouldBeNull();
        session.RunnerCwd.ShouldBe(mirror);
    }

    [Test]
    public async Task Refused_runner_task_leaves_no_pushed_branch_or_mirror()
    {
        // CARD-0645 item 3: the launch policy refuses before remote prep, so a refused task never
        // pushes its branch to origin or asks the runner for a mirror.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame,
                new PhoneHomeWorkspaceMirrorResponse("/work/worktrees/task-remote")),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.Grok);
        var sink = new RecordingLaunchSink();
        var git = new PushGit();
        var (dispatcher, _) = CreateDispatcher(schema, host, sink, git, allowDelegatedTasks: false);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldNotBeNull().ShouldContain("Delegated tasks are not enabled");
        sink.Specs.ShouldBeEmpty();
        git.Pushes.ShouldBe(0);
        peer.Incoming.ShouldNotContain(f => f.Operation == PhoneHomeOperation.WorkspaceMirror);
        task.RemoteWorktreePath.ShouldBeNull();
    }

    [Test]
    public async Task Runner_bound_grok_brief_spills_under_the_runner_cwd()
    {
        const string goal = "runner-brief-sentinel-0647";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var root = Path.Combine(Path.GetTempPath(), "c647-spill-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        var mirror = root + "/worktrees/task-remote";
        Directory.CreateDirectory(mirror);
        var writer = new BriefSpillWriter(root);
        peer.Reply = frame => frame.Operation switch
        {
            PhoneHomeOperation.WorkspaceMirror => Result(frame, new PhoneHomeWorkspaceMirrorResponse(mirror)),
            PhoneHomeOperation.ProviderAuth => Result(frame, new RunnerProviderAuthDto(
                "grok", true, "auth_file", null, DateTimeOffset.UtcNow, null)),
            PhoneHomeOperation.Input => writer.Answer(frame),
            _ => null,
        };
        using var workspace = new TempWorkspace();
        using var grokHome = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, AgentKind.Grok, goal);
        var sink = new RecordingLaunchSink();
        var courier = new RemoteSpillCourier();
        var graph = CreateDispatcher(schema, host, sink, grokCredentialProbe: true, grokHome: grokHome.Path, spills: courier);
        courier.UseScopeFactory(graph.Services.GetRequiredService<IServiceScopeFactory>());

        try
        {
            await graph.Dispatcher.TickAsync(CancellationToken.None);
            await graph.Preparer.WhenIdleAsync();
            await graph.Dispatcher.TickAsync(CancellationToken.None);

            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
            session.RunnerCwd.ShouldBe(mirror);
            var desktopSpill = Path.Combine(workspace.Path, ".antiphon",
                $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            File.Exists(desktopSpill).ShouldBeFalse();

            if (!await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == session.Id
                && m.Origin == QueuedMessageOrigin.Delegation))
            {
                var refresh = new GrokRulesRefreshService(
                    graph.Services.GetRequiredService<IServiceScopeFactory>(),
                    TimeProvider.System, Options.Create(new GrokRulesSettings()));
                await refresh.QueueLaunchBriefAsync(
                    db, session, graph.Services.GetRequiredService<SessionMessageQueueService>(), CancellationToken.None);
            }

            var first = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == session.Id && m.Origin == QueuedMessageOrigin.Delegation)
                .SingleAsync();
            var relative = TypedBodySpill.InboxRelativePath(first.Id.ToString("D"));
            first.Body.ShouldContain(mirror + "/" + relative);
            first.Body.ShouldNotContain(workspace.Path);
            // The observed runner prompt lost its heading line. Keep a marker beside the
            // instruction that survives that loss, before the spilled path is read.
            first.Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id)
                + " YOUR BRIEF IS NOT IN THIS MESSAGE");
            first.RemoteSpillBody.ShouldNotBeNull().ShouldContain(goal);
            first.RemoteSpillRelativePath.ShouldBe(relative);

            // A second queued pointer for the same busy session owns separate bytes and Id.
            var queue = graph.Services.GetRequiredService<SessionMessageQueueService>();
            const string secondOldPath = ".antiphon/second-brief.md";
            var secondBody = "second-brief-sentinel-0647\n" + new string('z', 4096);
            queue.StageRemoteSpill(session.Id, mirror, new PhoneHomeInputSpill(secondOldPath, secondBody));
            await queue.EnqueueAsync(session.Id, "Read " + mirror + "/" + secondOldPath,
                MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Delegation);
            var second = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == session.Id && m.Id != first.Id)
                .SingleAsync();
            second.RemoteSpillBody.ShouldBe(secondBody);
            second.Body.ShouldContain(TypedBodySpill.InboxRelativePath(second.Id.ToString("D")));

            // Replace the service object: no staged bytes survive in memory.
            var restarted = new RemoteSpillCourier(graph.Services.GetRequiredService<IServiceScopeFactory>());
            var client = new PhoneHomeRunnerClient(live, restarted);
            var attempts = 0;
            peer.Reply = frame => frame.Operation == PhoneHomeOperation.Input && ++attempts == 1
                ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId,
                    frame.Operation, ErrorCode: "runner_unavailable", StatusCode: 503)
                : writer.Answer(frame);
            await Should.ThrowAsync<Exception>(() => client.SendInputAsync(session.Id, first.Body, CancellationToken.None));
            var firstFile = Path.Combine(mirror, relative.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(firstFile).ShouldBeFalse();
            await client.SendInputAsync(session.Id, second.Body, CancellationToken.None);
            await client.SendInputAsync(session.Id, first.Body, CancellationToken.None);
            (await File.ReadAllTextAsync(firstFile)).ShouldBe(first.RemoteSpillBody);
            var secondFile = Path.Combine(mirror, second.RemoteSpillRelativePath!.Replace('/', Path.DirectorySeparatorChar));
            (await File.ReadAllTextAsync(secondFile)).ShouldBe(secondBody);
            peer.Incoming.Where(f => f.Operation == PhoneHomeOperation.Input)
                .Select(f => f.Payload!.Value.GetProperty("spill").GetProperty("messageId").GetGuid())
                .ToArray().ShouldBe([first.Id, second.Id, first.Id]);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task SeedSourcedTaskAsync(IsolatedTestSchema schema, string snapshotPath)
    {
        var now = DateTime.UtcNow;
        var sourceId = Guid.NewGuid();
        var sourcedId = Guid.NewGuid();
        var landingId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentTasks.Add(new AgentTask
        {
            Id = sourceId, RootTaskId = sourceId, Title = "landed source", Goal = "code",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = snapshotPath, Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = landingId, TaskId = sourceId, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        db.AgentTasks.Add(new AgentTask
        {
            Id = sourcedId, RootTaskId = sourcedId, Title = "historical mutation", Goal = "mutate",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Mutation, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = snapshotPath, WorktreePath = snapshotPath,
            SourceLandingOperationId = landingId, SourceLandingSha = new string('2', 40),
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task BindCardAsync(IsolatedTestSchema schema, Guid taskId)
    {
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"card0645-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/card0645.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = $"CARD-0645 {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-9645",
            Title = "runner card", Description = "CARD-0645.", CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.CardId = card.Id;
        task.ProjectId = project.Id;
        await db.SaveChangesAsync();
    }

    private static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private static async Task<Guid> SeedAsync(
        IsolatedTestSchema schema, string path, string runnerId, AgentKind kind, string goal = "reply")
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "remote task", Goal = goal,
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = kind,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = path, WorktreePath = path, WorktreeBranch = "feat/test-remote",
            RunnerId = runnerId, Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed record DispatchGraph(AgentTaskDispatcher Dispatcher, RemoteWorkspacePreparer Preparer, ServiceProvider Services)
    {
        public void Deconstruct(out AgentTaskDispatcher dispatcher, out RemoteWorkspacePreparer preparer)
        {
            dispatcher = Dispatcher;
            preparer = Preparer;
        }
    }

    private static DispatchGraph CreateDispatcher(
        IsolatedTestSchema schema, PhoneHomeTestHost host, RecordingLaunchSink sink,
        PushGit? git = null, bool allowDelegatedTasks = true,
        bool grokCredentialProbe = false, string? grokHome = null, RemoteSpillCourier? spills = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString, n =>
        {
            n.MigrationsAssembly("Antiphon.Server");
            n.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512, AllowedRoots = ["C:\\", "/"],
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = grokCredentialProbe;
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude.exe" };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok", Exe = "grok.exe", ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                Env = grokHome is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        if (spills is not null)
            services.AddSingleton(spills);
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-remote-dispatch-{Guid.NewGuid():N}"),
        });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = host.AllowedRunnerId, AllowDelegatedTasks = allowDelegatedTasks,
            HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x", ClaudeAuthProbeEnabled = true,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
        services.AddSingleton<ILandingGit>(git ?? new PushGit());
        services.AddSingleton<RemoteWorkspaceService>();
        services.AddSingleton<RemoteWorkspacePreparer>();
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        return new DispatchGraph(
            provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
            provider.GetRequiredService<RemoteWorkspacePreparer>(),
            provider);
    }

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-remote-dispatch-ws").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class PushGit : ILandingGit
    {
        private int _pushes;
        public int Pushes => Volatile.Read(ref _pushes);

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (arguments[0] == "push")
                Interlocked.Increment(ref _pushes);
            return Task.FromResult(arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, new string('1', 40), ""),
                "push" => new LandingGitResult(0, "", ""),
                _ => throw new NotSupportedException(arguments[0]),
            });
        }
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>The real runner write path for one spilled Input frame.</summary>
    private sealed class BriefSpillWriter
    {
        private readonly PhoneHomeCommandDispatcher _dispatcher;

        public BriefSpillWriter(string root)
        {
            _dispatcher = new PhoneHomeCommandDispatcher(
                new SilentRuntime(),
                new PhoneHomeSettings { AllowedCwd = root, RunnerRepository = root + "/repo" });
        }

        public PhoneHomeFrame Answer(PhoneHomeFrame frame) =>
            _dispatcher.DispatchAsync(frame, CancellationToken.None).GetAwaiter().GetResult();

        private sealed class SilentRuntime : IPhoneHomeRuntimeSurface
        {
            public RunnerCapabilitiesDto Capabilities() =>
                new("InboxConhost", "inbox", "test", false, Features: [], VerificationCustodyBackend: null);
            public string Health() => "Healthy";
            public IReadOnlyList<RunnerSessionDto> List() => [];
            public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
                Task.FromResult(Session(sessionId));
            public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) =>
                Task.FromResult(Session(request.SessionId));
            public RunnerBufferDto GetBuffer(Guid sessionId) => new(sessionId, "", 0);
            public RunnerSnapshotDto GetSnapshot(Guid sessionId) => new(sessionId, "", "", 0, DateTime.UtcNow);
            public RunnerTranscriptDto GetTranscript(Guid sessionId) => new(sessionId, [], 0);
            public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
            public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
                Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
                Task.FromResult(new RunnerConditionalInputResult(
                    sessionId, ConditionalInputOutcomes.Written, DateTime.UtcNow, 1));
            public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
            public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
            public Task<RunnerKillGenerationResult> KillGenerationAsync(
                Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
                Task.FromResult(new RunnerKillGenerationResult(
                    sessionId, true, KillGenerationOutcomes.Killed, DateTime.UtcNow));
            public int OwnedSessionCount => 0;

            private static RunnerSessionDto Session(Guid id) =>
                new(id, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: DateTime.UtcNow);
        }
    }
}
