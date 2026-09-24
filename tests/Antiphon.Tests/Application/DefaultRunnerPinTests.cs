using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0659 V-3. The default host is chosen from the kind the routing pins and walks actually
/// settled on, never from the raw request: a pin that lands on Codex stays on the desktop, one that
/// lands on Grok/Claude takes the default, a Required-pin conflict refuses before any placement,
/// and an exhausted walk stays local. SourceLanding uses the SELECTED host's custody: a valid
/// Mutation shape can take the default, the dispatch gate admits it, and custody admission asks
/// that runner and refuses rather than falling back.
/// </summary>
[Category("Integration")]
public sealed class DefaultRunnerPinTests
{
    [Test]
    public async Task Kind_pin_is_resolved_before_host()
    {
        foreach (var (row, pin, request, hold, expectedKind, expectedRunner, reason) in new (string, PutRoutingPinRequest, CreateAgentTaskRequest, string?, AgentKind, string?, string)[]
                 {
                     ("preferred pin on Codex stays local",
                         Pin(RoutingPinStrength.Preferred, (AgentKind.Codex, AgentModelLevel.High)),
                         Code("c659 prefer codex"), null, AgentKind.Codex, null, "kind_not_supported"),
                     ("required pin on Codex stays local",
                         Pin(RoutingPinStrength.Required, (AgentKind.Codex, AgentModelLevel.High)),
                         Code("c659 require codex"), null, AgentKind.Codex, null, "kind_not_supported"),
                     ("preferred pin on Grok takes the default",
                         Pin(RoutingPinStrength.Preferred, (AgentKind.Grok, AgentModelLevel.High)),
                         Code("c659 prefer grok"), null, AgentKind.Grok, "server2", "eligible"),
                     ("required pin on Claude takes the default",
                         Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.High)),
                         Code("c659 require claude"), null, AgentKind.ClaudeCode, "server2", "eligible"),
                     ("multi-candidate: held Claude head, final Grok takes the default",
                         Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier)),
                         Code("c659 walk to grok"), "fable", AgentKind.Grok, "server2", "eligible"),
                     ("multi-candidate: held Claude head, final Codex stays local",
                         Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Codex, AgentModelLevel.Frontier)),
                         Code("c659 walk to codex"), "fable", AgentKind.Codex, null, "kind_not_supported"),
                     ("ignored Codex pin: the request's Claude takes the default",
                         Pin(RoutingPinStrength.Required, (AgentKind.Codex, AgentModelLevel.High)),
                         Code("c659 ignore pin", AgentKind.ClaudeCode) with { IgnoreRoutingPin = true },
                         null, AgentKind.ClaudeCode, "server2", "eligible"),
                 })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
            await using var db = kit.Context();
            await Pins(db).UpsertAsync(pin, null, CancellationToken.None);
            if (hold is not null)
                await SeedHoldAsync(db, AgentKind.ClaudeCode, hold);

            var created = await RoutedService(kit, db).CreateAsync(request, kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.AgentKind.ShouldBe(expectedKind, row);
            saved.Task.RunnerId.ShouldBe(expectedRunner, row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Worktree, row);
            saved.Created.ShouldContain(
                $"runner source=default requested=unset default=server2 selected={expectedRunner ?? "local"} reason={reason}",
                Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
            saved.Warnings.ShouldNotContain(w => w.Contains("Default runner", StringComparison.Ordinal), row);
            kit.Directory.ResolveCalls.Count.ShouldBe(expectedRunner is null ? 0 : 1,
                row + ": only a compatible pinned kind consults the readiness gate");
        }
    }

    [Test]
    public async Task Agent_pin_reuses_workspace_with_local_sentinel()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        var standing = await kit.SeedStandingAgentAsync();
        await using var db = kit.Context();
        await Pins(db).UpsertAsync(new PutRoutingPinRequest(
            AgentTaskRole.Code, Provenance: RoutingPinProvenance.Human, Strength: RoutingPinStrength.Preferred,
            AgentId: standing.Id, Reason: "c659 seat pin"), null, CancellationToken.None);
        var service = RoutedService(kit, db);

        foreach (var (row, runnerId, audit) in new (string, string?, string)[]
                 {
                     ("-Local with a configured agent pin", "local",
                         "runner source=explicit-local requested=local default=server2 selected=local reason=local_requested"),
                     ("omitted runner with a configured agent pin", null,
                         "runner source=default requested=unset default=server2 selected=local reason=existing_process"),
                 })
        {
            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("c659 seat " + Guid.NewGuid().ToString("N"), Role: AgentTaskRole.Code, RunnerId: runnerId),
                kit.Caller, CancellationToken.None);

            var saved = await kit.ReadAsync(created.Id);
            saved.Task.AgentId.ShouldBe(standing.Id, row);
            saved.Task.Workspace.ShouldBe(WorkspaceMode.Shared,
                row + ": CARD-0644 reuses the pinned agent's checkout; the local sentinel must not defeat that");
            saved.Task.RunnerId.ShouldBeNull(row);
            saved.Created.ShouldContain(audit, Case.Sensitive, row);
            DefaultRunnerKit.Occurrences(saved.Created, "runner source=").ShouldBe(1, row);
        }

        kit.Directory.ResolveCalls.ShouldBeEmpty("an agent-pinned task never consults the readiness gate");
    }

    [Test]
    public async Task Required_pin_conflict_is_unchanged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2");
        await using var db = kit.Context();
        await Pins(db).UpsertAsync(
            Pin(RoutingPinStrength.Required, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.ClaudeCode, AgentModelLevel.High)),
            null, CancellationToken.None);
        var service = RoutedService(kit, db);
        var before = await kit.TaskCountAsync();

        // A request that disagrees with a Required pin is refused exactly as before, with or
        // without a runner in play.
        foreach (var runnerId in new string?[] { null, "local", "server2" })
        {
            var refused = await Should.ThrowAsync<RoutingPinConflictException>(() => service.CreateAsync(
                Code("c659 conflict", AgentKind.Grok) with { RunnerId = runnerId }, kit.Caller, CancellationToken.None));
            refused.Code.ShouldBe("routing_pin_conflict", runnerId ?? "<omitted>");
        }

        (await kit.TaskCountAsync()).ShouldBe(before, "a pin conflict inserts nothing");
        kit.Directory.ResolveCalls.ShouldBeEmpty("a pin conflict refuses before any runner selection");

        // Every Required candidate held: the exhausted walk is Blocked on the desktop, and the
        // failed head is not presented as a selected remote candidate.
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable");
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus");
        var blocked = await service.CreateAsync(Code("c659 exhausted"), kit.Caller, CancellationToken.None);

        var saved = await kit.ReadAsync(blocked.Id);
        saved.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        saved.Task.FailureReason.ShouldNotBeNull().ShouldStartWith(ComplexityRoutingService.RoutingExhaustedPrefix);
        saved.Task.RunnerId.ShouldBeNull("routing exhaustion keeps the task local");
        saved.Created.ShouldContain(
            "runner source=default requested=unset default=server2 selected=local reason=routing_exhausted", Case.Sensitive);
        kit.Directory.ResolveCalls.ShouldBeEmpty("an exhausted walk never consults the readiness gate");

        // Explicit remote + Codex still refuses at create.
        await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreateAgentTaskRequest("c659 codex remote", Role: AgentTaskRole.Review, AgentKind: AgentKind.Codex,
                Workspace: WorkspaceMode.Worktree, RunnerId: "server2"),
            kit.Caller, CancellationToken.None));
    }

    [Test]
    public async Task SourceLanding_uses_selected_host_custody()
    {
        var kit = DefaultRunnerKit.Create("unused", defaultRunnerId: "server2");
        var phoneHome = new PhoneHomeLaunchPolicy(Options.Create(kit.PhoneHome));
        var policy = new DefaultRunnerRoutingPolicy(kit.Settings, phoneHome, kit.Directory);
        var unset = RunnerRequestIntent.Parse(null);

        // The valid SourceLanding shape (fresh Worker/Mutation/Worktree, Grok or Claude) takes the
        // eligible default like any other fresh task: create admission and dispatch agree.
        foreach (var kind in new[] { AgentKind.Grok, AgentKind.ClaudeCode })
        {
            var decision = policy.Decide(unset, new DefaultRunnerShape(
                WorkspaceMode.Worktree, kind, AgentTaskKind.Worker, AgentTaskRole.Mutation,
                ExistingProcess: false, SourceLanding: true, RoutingExhausted: false)).ShouldNotBeNull();
            decision.SelectedRunnerId.ShouldBe("server2", kind.ToString());
            decision.AuditSegment.ShouldBe(
                "runner source=default requested=unset default=server2 selected=server2 reason=eligible", kind.ToString());
            decision.Warn.ShouldBeFalse(kind.ToString());
        }

        // An invalid SourceLanding role never selects a runner (create refuses that shape anyway).
        policy.Decide(unset, new DefaultRunnerShape(
                WorkspaceMode.Worktree, AgentKind.Grok, AgentTaskKind.Worker, AgentTaskRole.Code,
                ExistingProcess: false, SourceLanding: true, RoutingExhausted: false))
            .ShouldNotBeNull().SelectedRunnerId.ShouldBeNull("a non-Mutation SourceLanding shape stays local");
        // Codex SourceLanding stays local.
        policy.Decide(unset, new DefaultRunnerShape(
                WorkspaceMode.Worktree, AgentKind.Codex, AgentTaskKind.Worker, AgentTaskRole.Mutation,
                ExistingProcess: false, SourceLanding: true, RoutingExhausted: false))
            .ShouldNotBeNull().Reason.ShouldBe("kind_not_supported");

        // The dispatch gate admits the runner-bound SourceLanding Mutation that create admitted
        // (Cut B's runner custody), so an explicit or selected remote Mutation is not refused at
        // launch after create accepted it.
        var pool = new Agent
        {
            Id = Guid.NewGuid(),
            RunnerId = "server2",
            IsPoolDelegate = true,
            WorkingDirectory = kit.RepoRoot,
        };
        foreach (var kind in new[] { AgentKind.Grok, AgentKind.ClaudeCode })
        {
            Should.NotThrow(() => phoneHome.RefuseUnsupportedStart(pool, cardStart: false, delegatedTask: true,
                worktree: true, sourceLanding: true, onAgent: false, SessionBackend.PtyHost, kind, customWrapper: null),
                kind.ToString());
        }
        // Everything else about the remote shape is still refused at the gate.
        Should.Throw<ConflictException>(() => phoneHome.RefuseUnsupportedStart(pool, false, delegatedTask: true,
                worktree: false, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_worktree_refused");
        Should.Throw<ConflictException>(() => phoneHome.RefuseUnsupportedStart(pool, false, delegatedTask: true,
                worktree: true, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_kind_refused");
        // A runner-bound NAMED agent still has no SourceLanding shape.
        var named = new Agent { Id = Guid.NewGuid(), RunnerId = "server2", WorkingDirectory = kit.RepoRoot };
        Should.Throw<ConflictException>(() => phoneHome.RefuseUnsupportedStart(named, false, delegatedTask: false,
                worktree: false, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_sourcelanding_refused");

        // Custody admission asks the SELECTED runner, and a runner without custody is a refusal,
        // never a quiet fall back to the desktop's store.
        var admission = new SourceLandingAdmission(null!, null!, kit.Directory);
        var noCustody = await Should.ThrowAsync<ConflictException>(
            () => admission.RequireSupportAsync("server2", CancellationToken.None));
        noCustody.Code.ShouldBe("verification_custody_unsupported_backend");
        kit.Directory.ResolveCalls[^1].ShouldBe("server2");
        kit.Directory.ResolveCalls.ShouldNotContain((string?)null, "custody was never asked of the desktop");

        // An offline selected runner is the directory's own refusal, not a local answer.
        var offline = DefaultRunnerKit.Create("unused", defaultRunnerId: "server2", eligible: false);
        (await Should.ThrowAsync<ServiceUnavailableException>(
                () => new SourceLandingAdmission(null!, null!, offline.Directory).RequireSupportAsync("server2", CancellationToken.None)))
            .Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
    }

    /// <summary>
    /// Review 5de2b154 item 2: producer to recipient for a runner-bound SourceLanding Mutation.
    /// The real create (default runner selected, custody asked of THAT runner), the real dispatcher
    /// (remote snapshot created and validated through the typed phone-home operations, custody
    /// reservation, launch spec projection), the real launch-time custody preparation and the
    /// real phone-home client carrying the binding to the runner socket.
    ///
    /// Where the harness stops: the runner is <see cref="PhoneHomeScriptedPeer"/>, which answers
    /// each typed frame with scripted results. Nothing runs the runner's own command dispatcher,
    /// custody backend (cgroup/job admission of the binding), git snapshot, pty or Claude process,
    /// so there is no transcript and no UserPrompt receipt. The last proven hop is the Launch frame
    /// received at the runner socket carrying this execution's binding and the snapshot cwd.
    /// </summary>
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task SourceLanding_mutation_reaches_runner_launch_with_binding()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var root = new TempRoot();
        var source = await SeedPublishedSourceAsync(schema, root.Path);
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        const string runnerRepository = "/work/repos/antiphon";
        VerificationCreationCoordinates? created = null;
        string? createdSha = null;
        peer.Reply = frame =>
        {
            switch (frame.Operation)
            {
                case PhoneHomeOperation.Capabilities:
                    return Result(frame, new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                        Features: [RunnerCapabilityFeatures.VerificationCustodyV1],
                        VerificationCustodyBackend: VerificationCustodyBackends.LinuxCgroup,
                        RunnerStoreId: host.StoreId));
                case PhoneHomeOperation.ProviderAuth:
                    return Result(frame, new RunnerProviderAuthDto(
                        "claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null));
                case PhoneHomeOperation.VerificationWorkspaceCreate:
                    var create = frame.Payload!.Value.Deserialize<PhoneHomeVerificationCreateRequest>(PhoneHomeFraming.Json)!;
                    created = new VerificationCreationCoordinates(runnerRepository, runnerRepository + "/.git",
                        "/work/worktrees/" + create.Identifier, runnerRepository + "/.git/worktrees/" + create.Identifier,
                        create.Branch, Guid.NewGuid());
                    createdSha = create.Sha;
                    return Result(frame, new PhoneHomeVerificationCreateResponse(created, create.Sha));
                case PhoneHomeOperation.VerificationWorkspaceValidate:
                    return Result(frame, new PhoneHomeVerificationValidateResponse(true, null));
                case PhoneHomeOperation.VerificationWorkspaceInspect:
                    var c = created!;
                    return Result(frame, new PhoneHomeVerificationInspectResponse(c.CreationId, createdSha, c.Branch,
                        c.RepositoryPath, c.WorktreePath, c.WorktreeGitDirectory, createdSha, true, true, false));
                default:
                    return null;
            }
        };

        var sink = new RecordingLaunchSink();
        await using var services = SourceLandingDispatchGraph(schema, root.Path, host, sink);

        // Producer: an omitted runner on the valid SourceLanding shape takes the default runner.
        Guid taskId;
        await using (var scope = services.CreateAsyncScope())
        {
            var summary = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                source.Request, source.Caller, CancellationToken.None);
            taskId = summary.Id;
        }

        await using (var db = Context(schema))
        {
            var queued = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            queued.RunnerId.ShouldBe(host.AllowedRunnerId, "the default runner was selected for the Mutation");
            queued.SourceLandingSha.ShouldNotBeNull();
            queued.Status.ShouldBe(AgentTaskStatus.Queued, queued.FailureReason);
            (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == taskId
                    && e.Type == AgentTaskEventType.Created)).Detail.ShouldNotBeNull()
                .ShouldContain($"runner source=default requested=unset default={host.AllowedRunnerId} "
                    + $"selected={host.AllowedRunnerId} reason=eligible");
        }

        // Dispatch: snapshot on the runner, custody reservation, projected launch spec.
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        await using var verify = Context(schema);
        var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        var snapshot = created.ShouldNotBeNull("the snapshot is created on the runner, not the desktop");
        createdSha.ShouldBe(task.SourceLandingSha);
        task.WorktreePath.ShouldBe(snapshot.WorktreePath);
        task.RemoteWorktreePath.ShouldBe(snapshot.WorktreePath);
        task.VerificationCreationJson.ShouldBe(JsonSerializer.Serialize(snapshot));
        peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0, "a SourceLanding Mutation runs in its snapshot, never a mirror");
        var session = await verify.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerId.ShouldBe(host.AllowedRunnerId);
        session.RunnerCwd.ShouldBe(snapshot.WorktreePath);
        var execution = await verify.VerificationExecutions.AsNoTracking().SingleAsync(e => e.TaskId == taskId);
        execution.SessionId.ShouldBe(session.Id);

        var launch = sink.Specs.ShouldHaveSingleItem();
        var binding = launch.VerificationBinding.ShouldNotBeNull("the projected launch keeps the reservation's binding");
        binding.ExecutionId.ShouldBe(execution.Id);
        binding.Source.TaskId.ShouldBe(taskId);
        binding.Source.LandedSha.ShouldBe(task.SourceLandingSha);
        binding.Creation.ShouldBe(snapshot);
        binding.Backend.ShouldBe(VerificationCustodyBackends.LinuxCgroup, "custody is the selected runner's, not the desktop's");
        binding.RunnerStoreId.ShouldBe(host.StoreId);
        launch.Cwd.ShouldBe(snapshot.WorktreePath);
        launch.Exe.ShouldBe("claude");
        launch.Backend.ShouldBe(SessionBackend.PtyHost);

        // Recipient: launch-time custody preparation, then the phone-home client to the runner.
        await using (var scope = services.CreateAsyncScope())
        {
            var prepared = await scope.ServiceProvider.GetRequiredService<VerificationExecutionService>()
                .PrepareLaunchAsync(session, launch, CancellationToken.None);
            prepared.VerificationBinding.ShouldBe(binding);
            await new PhoneHomeRunnerClient(live).StartAsync(session.Id, prepared, CancellationToken.None);
        }

        var started = peer.Launches.ShouldHaveSingleItem().Payload!.Value
            .Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)!;
        started.SessionId.ShouldBe(session.Id);
        started.Cwd.ShouldBe(snapshot.WorktreePath);
        started.VerificationBinding.ShouldBe(binding, "the runner receives exactly the reserved binding");
        await using var after = Context(schema);
        (await after.VerificationExecutions.AsNoTracking().SingleAsync(e => e.Id == execution.Id))
            .RunnerCallIntentAt.ShouldNotBeNull("the runner-call intent is recorded before the launch frame");
    }

    private static ServiceProvider SourceLandingDispatchGraph(
        IsolatedTestSchema schema, string root, PhoneHomeTestHost host, RecordingLaunchSink sink)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Context(schema));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            AllowedRoots = [root],
            DefaultRunnerId = host.AllowedRunnerId,
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude.exe" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        // Real git: the lease and the source identity both resolve the seeded repository.
        services.AddSingleton<ILandingGit, LandingGit>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(root, "desktop-worktrees"),
        });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            AllowDelegatedTasks = true,
            HostWorkspaceRoot = root,
            RunnerWorkspace = "/work",
            RunnerRepository = "/work/repos/antiphon",
            CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x",
            ClaudeAuthProbeEnabled = true,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
        services.AddSingleton<RemoteWorkspaceService>();
        services.AddSingleton<RemoteWorkspacePreparer>();
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddScoped<SourceLandingAdmission>();
        services.AddScoped<IVerificationWorkspace, LocalVerificationWorkspace>();
        services.AddScoped<IVerificationWorkspaceDirectory, VerificationWorkspaceDirectory>();
        services.AddScoped<VerificationExecutionService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed record PublishedSource(CreateAgentTaskRequest Request, AgentTaskService.Caller Caller);

    /// <summary>
    /// A confirmed publication of an ordinary Code task, seeded directly: a git repository (the
    /// source identity compares common directories), the source task on its card, a companion card
    /// on the same board and a landing row that satisfies <see cref="AgentTaskLandingState.HasPublication"/>.
    /// The snapshot itself is the runner's, so the landed sha never has to exist on this machine.
    /// </summary>
    private static async Task<PublishedSource> SeedPublishedSourceAsync(IsolatedTestSchema schema, string root)
    {
        var repository = Path.Combine(root, "canonical");
        Directory.CreateDirectory(repository);
        var git = new LandingGit();
        (await git.RunAsync(repository, ["init", "-q"], CancellationToken.None)).Succeeded.ShouldBeTrue("git init");
        var common = await git.CommonDirectoryAsync(repository, CancellationToken.None);
        var sha = "c659" + new string('a', 36);
        var now = DateTime.UtcNow;
        var sourceId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        await using var db = Context(schema);
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"c659-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/c659.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = $"c659 {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var cards = Enumerable.Range(1, 2).Select(i => new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = $"CARD-965{i}",
            Title = "c659 " + i, Description = "CARD-0659.", CreatedAt = now, UpdatedAt = now,
        }).ToArray();
        db.AddRange(project, board, column);
        db.Cards.AddRange(cards);
        db.AgentTasks.Add(new AgentTask
        {
            Id = sourceId, RootTaskId = sourceId, Title = "landed source", Goal = "code",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repository, RepoPath = repository, CardId = cards[0].Id,
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = operationId, TaskId = sourceId, SchemaVersion = 1, Active = false,
            Phase = LandPhase.PublicationConfirmed, Publication = LandPublicationOutcome.Landed,
            CreatedAt = now, UpdatedAt = now,
            RepositoryPath = repository, CommonDirectory = common, WorktreePath = repository, GitDirectory = common,
            SourceFullRef = $"refs/heads/feat/card-task-{sourceId:N}", OriginalSourceSha = sha,
            VerifiedSourceSha = sha, VerificationPassed = true, VerifiedAt = now, SourcePinned = true, TargetPinned = true,
            TargetFullRef = "refs/heads/master", TargetBeforeSha = new string('b', 40),
            DestinationFullRef = "refs/heads/master", RemoteFingerprint = new string('f', 64),
            ObservedRemoteTargetSha = sha, RemoteConfirmedAt = now,
            ConfirmationMethod = "push-endpoint-read-fetch-ancestry",
            RecoveryRefPrefix = $"refs/antiphon/land/{sourceId:N}/{operationId:N}",
        });
        await db.SaveChangesAsync();
        return new PublishedSource(
            new CreateAgentTaskRequest("c659 post-land mutation", Role: AgentTaskRole.Mutation,
                AgentKind: AgentKind.ClaudeCode, Workspace: WorkspaceMode.Worktree, Card: cards[1].Id.ToString("D"),
                SourceLandingOperationId: operationId),
            new AgentTaskService.Caller(null, null, repository));
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c659-sourcelanding").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static PhoneHomeFrame Result(PhoneHomeFrame request, object payload) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    private static CreateAgentTaskRequest Code(string goal, AgentKind? kind = null) =>
        new(goal + " " + Guid.NewGuid().ToString("N")[..8], Role: AgentTaskRole.Code, AgentKind: kind);

    private static PutRoutingPinRequest Pin(RoutingPinStrength strength, params (AgentKind Kind, AgentModelLevel Level)[] candidates) =>
        new(
            AgentTaskRole.Code,
            Provenance: RoutingPinProvenance.Human,
            Strength: strength,
            Candidates: candidates.Select(c => new RoutingCandidateRequest(c.Kind, c.Level)).ToList(),
            Reason: "c659 pin");

    private static RoutingPinService Pins(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);

    private static async Task SeedHoldAsync(AppDbContext db, AgentKind kind, string alias)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            HitAt = DateTime.UtcNow,
            Reason = "c659 hold",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The kit's service plus the real pin/walk/availability services.</summary>
    internal static AgentTaskService RoutedService(DefaultRunnerKit kit, AppDbContext db)
    {
        var settings = Options.Create(kit.Settings);
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: Pins(db),
            complexityRouting: new ComplexityRoutingService(db, settings, TimeProvider.System, availability),
            phoneHome: new PhoneHomeLaunchPolicy(Options.Create(kit.PhoneHome)),
            runners: kit.Directory);
    }
}
