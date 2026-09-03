using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0186 S4 + CARD-0187 S2: one integration smoke through
/// <see cref="DirectSessionRunnerClient"/> + <see cref="FakeHerdrServer"/> covering AlwaysOn herdr
/// + runner restart adopt (R1) + emptied pane (no false adopt) + supervisor resume into a new
/// pane + channel inbound/reply. The PtyHost arm is the control: the same AlwaysOn + death +
/// supervisor resume + channel story on the shared server path, so a regression there cannot hide
/// behind a herdr-only test. CARD-0187 S2 adds a launch-definition arm parametrised over
/// ClaudeCode / Grok / Codex.
///
/// Ungrouped <see cref="NotInParallelAttribute"/>: the supervisor sweep is global on the shared
/// test database (same lesson as <c>AgentSupervisionTests</c>).
/// </summary>
[Category("Integration")]
[NotInParallel]
public class HerdrAlwaysOnChannelParityTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    [Test]
    [Arguments(SessionBackend.Herdr)]
    [Arguments(SessionBackend.PtyHost)]
    public async Task AlwaysOn_channel_bound_survives_child_death_and_replies(SessionBackend backend)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-card0186-s4-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        FakeHerdrServer? fake = null;
        try
        {
            if (backend == SessionBackend.Herdr)
            {
                fake = new FakeHerdrServer();
                fake.EchoSendTextToScreen = true;
                fake.Start();
                await fake.WaitUntilListeningAsync();
            }

            var ptyAdapters = backend == SessionBackend.PtyHost
                ? new[]
                {
                    new FakeAgentProtocolAdapter(),
                    new FakeAgentProtocolAdapter(),
                }
                : Array.Empty<FakeAgentProtocolAdapter>();

            await using var harness = BuildHarness(tempRoot, backend, fake, ptyAdapters);
            foreach (var adapter in ptyAdapters)
                adapter.RegisterOnStart = harness.Runtime;

            var workspace = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspace);
            var agent = await harness.Agents.CreateAsync(
                new CreateAgentRequest(
                    $"CARD0186-{backend}",
                    workspace,
                    SessionBackend: backend,
                    AlwaysOn: true,
                    RemoteControlEnabled: false),
                CancellationToken.None);
            agent.SessionBackend.ShouldBe(backend);
            agent.AlwaysOn.ShouldBeTrue();

            await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

            var firstSessionId = await WaitForPersistentSessionAsync(harness, agent.Id);
            await using (var verify = CreateContext())
            {
                var row = await verify.AgentSessions.SingleAsync(s => s.Id == firstSessionId);
                row.Status.ShouldBe(SessionStatus.Running);
                row.SessionBackend.ShouldBe(backend);
            }

            string? firstPane = null;
            var panesBeforeDeath = 0;
            if (backend == SessionBackend.Herdr)
            {
                fake.ShouldNotBeNull();
                harness.Runner.ShouldNotBeNull();
                firstPane = fake.RequireAgentPaneId();
                panesBeforeDeath = CountAgentPanes(fake);

                await harness.Runner.SimulateRunnerRestartAsync();
                var adopted = await harness.Runner.GetAsync(firstSessionId, CancellationToken.None);
                adopted.Status.ShouldBe("Running", "R1: pane still lists the child");
                adopted.Adopted.ShouldBeTrue();
                adopted.Backend.ShouldBe(SessionBackends.Herdr);

                // P7 / R2: herdr "restart" restores the pane with a bare shell — child not listed.
                fake.SetPaneProcessInfo(firstPane, shellPid: 1);
                fake.ClearDetectedAgent(firstPane);
                await harness.Runner.SimulateRunnerRestartAsync();
                var afterEmpty = await harness.Runner.GetAsync(firstSessionId, CancellationToken.None);
                afterEmpty.Status.ShouldBe("Exited", "emptied pane must not stay Running");
                afterEmpty.ExitReason.ShouldBe(AgentExitReason.HerdrRestartPresumedDead);
                File.Exists(HerdrPaneSidecar.PathFor(
                    Path.Combine(tempRoot, "session-logs"), firstSessionId))
                    .ShouldBeFalse("R2 deletes the sidecar; nothing to false-adopt next restart");

                await harness.Runtime.ObserveExitAsync(
                    firstSessionId, afterEmpty.ExitCode, afterEmpty.ExitReason, CancellationToken.None);
            }
            else
            {
                await harness.Runtime.ObserveExitAsync(
                    firstSessionId, 1, AgentExitReason.ProcessExited, CancellationToken.None);
                harness.Runtime.TryRemove(firstSessionId, out _);
            }

            await using (var verify = CreateContext())
            {
                (await verify.AgentSessions.SingleAsync(s => s.Id == firstSessionId)).Status
                    .ShouldBe(SessionStatus.Failed);
            }

            // Tick 1: Crash + RestartScheduled. Tick 2 (past backoff): resume into the same pane.
            await harness.Supervisor().TickAsync(CancellationToken.None);
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
            await harness.Supervisor().TickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

            var resumedSessionId = await WaitForPersistentSessionAsync(harness, agent.Id);
            resumedSessionId.ShouldBe(firstSessionId, "supervisor resume reuses the Failed row");
            await using (var verify = CreateContext())
            {
                var row = await verify.AgentSessions.SingleAsync(s => s.Id == resumedSessionId);
                row.Status.ShouldBe(SessionStatus.Running);
                row.SessionBackend.ShouldBe(backend);
            }

            if (backend == SessionBackend.Herdr)
            {
                fake.ShouldNotBeNull();
                var resumedPane = fake.RequireAgentPaneId();
                resumedPane.ShouldBe(firstPane, "CARD-0224: resume reuses the standing pane");
                CountAgentPanes(fake).ShouldBe(
                    panesBeforeDeath, "resume reuses the pane; no new tab is allocated");
            }

            var chatId = await BindChannelAsync(harness, agent.Id);
            var nonce = $"CARD0186-{Guid.NewGuid():N}"[..20];
            var inbound = new ChannelMessage
            {
                Id = Guid.NewGuid().ToString("n"),
                Channel = "telegram",
                ChannelMessageId = Guid.NewGuid().ToString("n")[..12],
                Conversation = new Conversation { Id = chatId, Kind = ConversationKind.Group, Title = "Family" },
                Author = new Participant { Id = "1001", DisplayName = "Mike" },
                Timestamp = DateTimeOffset.UtcNow,
                Text = $"What's for dinner {nonce}?",
                ReplyHandle = chatId,
                Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            };

            if (backend == SessionBackend.Herdr)
            {
                fake.ShouldNotBeNull();
                var confirm = ConfirmHerdrDeliveryAsync(fake, resumedSessionId, nonce);
                await harness.Bridge.HandleInboundAsync(inbound, CancellationToken.None);
                await confirm;
            }
            else
            {
                var resumeAdapter = ptyAdapters[1];
                resumeAdapter.OnSubmitted = async submitted =>
                {
                    await InsertEntryAsync(resumedSessionId, TranscriptKinds.UserPrompt, submitted,
                        timestamp: DateTime.UtcNow);
                };
                await harness.Bridge.HandleInboundAsync(inbound, CancellationToken.None);
            }

            await using (var db = CreateContext())
            {
                var queued = await db.SessionQueuedMessages.SingleAsync(
                    m => m.AgentSessionId == resumedSessionId && m.Origin == QueuedMessageOrigin.Channel);
                queued.Status.ShouldBe(QueuedMessageStatus.Sent);
                queued.ConversationKey.ShouldBe($"telegram:{chatId}");
                queued.Body.ShouldContain(nonce);
            }

            await using (var db = CreateContext())
            {
                var prompt = await db.SessionQueuedMessages.SingleAsync(
                    m => m.AgentSessionId == resumedSessionId && m.Origin == QueuedMessageOrigin.Channel);
                await InsertEntryAsync(resumedSessionId, TranscriptKinds.AssistantText,
                    $"Pasta tonight — {nonce}.");
                await InsertEntryAsync(resumedSessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn");
                _ = prompt;
            }

            await harness.Dispatcher.OnTurnEndAsync(resumedSessionId, CancellationToken.None);
            var reply = harness.Messaging.SentReplies.ShouldHaveSingleItem();
            reply.Channel.ShouldBe("telegram");
            reply.ConversationId.ShouldBe(chatId);
            reply.Text.ShouldContain(nonce);
            (await harness.Dispatcher.PendingCountAsync(resumedSessionId)).ShouldBe(0);
        }
        finally
        {
            if (fake is not null)
                await fake.DisposeAsync();
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>
    /// CARD-0187 S2: the herdr launch definition is parametrised over ClaudeCode / Grok / Codex.
    /// Claude/Grok use the fake CLIs; Codex has none so it uses the same <c>cmd.exe</c> stub as
    /// the CARD-0186 S4 test and asserts only launch / adopt / exit — not transcript content.
    /// </summary>
    [Test]
    [Arguments(AgentKind.ClaudeCode)]
    [Arguments(AgentKind.Grok)]
    [Arguments(AgentKind.Codex)]
    public async Task Herdr_launch_definition_starts_adopts_and_exits(AgentKind kind)
    {
        if (kind == AgentKind.ClaudeCode && !File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");
        if (kind == AgentKind.Grok && !File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-card0187-s2-{kind}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        FakeHerdrServer? fake = null;
        try
        {
            fake = new FakeHerdrServer
            {
                EchoSendTextToScreen = true,
                LaunchScriptAgentKind = kind switch
                {
                    AgentKind.Grok => HerdrAgentKinds.Grok,
                    AgentKind.Codex => HerdrAgentKinds.Codex,
                    _ => HerdrAgentKinds.Claude,
                },
            };
            fake.Start();
            await fake.WaitUntilListeningAsync();

            await using var harness = BuildHarness(
                tempRoot, SessionBackend.Herdr, fake, [], launchKind: kind);

            var workspace = Path.Combine(tempRoot, "workspace");
            Directory.CreateDirectory(workspace);
            var agent = await harness.Agents.CreateAsync(
                new CreateAgentRequest(
                    $"CARD0187-{kind}",
                    workspace,
                    SessionBackend: SessionBackend.Herdr,
                    AlwaysOn: true,
                    RemoteControlEnabled: false),
                CancellationToken.None);

            if (kind != AgentKind.ClaudeCode)
            {
                agent = await harness.Agents.UpdateAsync(
                    agent.Id,
                    new UpdateAgentRequest(
                        agent.Name,
                        agent.WorkingDirectory,
                        agent.Details,
                        agent.DefaultWorkflowTemplateId,
                        agent.AssignmentPolicy,
                        BoardId: agent.BoardId,
                        Kind: kind),
                    CancellationToken.None);
            }

            agent.Kind.ShouldBe(kind);
            agent.SessionBackend.ShouldBe(SessionBackend.Herdr);

            await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

            var firstSessionId = await WaitForPersistentSessionAsync(harness, agent.Id);
            await using (var verify = CreateContext())
            {
                var row = await verify.AgentSessions.SingleAsync(s => s.Id == firstSessionId);
                row.Status.ShouldBe(SessionStatus.Running);
                row.SessionBackend.ShouldBe(SessionBackend.Herdr);
                row.AgentKind.ShouldBe(kind);
            }

            fake.ShouldNotBeNull();
            harness.Runner.ShouldNotBeNull();
            var firstPane = fake.RequireAgentPaneId();
            firstPane.ShouldNotBeNull();

            string? definitionName;
            await using (var titleVerify = CreateContext())
            {
                var row = await titleVerify.AgentSessions.SingleAsync(s => s.Id == firstSessionId);
                definitionName = row.DefinitionName;
            }

            definitionName.ShouldNotBe(agent.Name);
            fake.Requests.Any(r => r.GetProperty("method").GetString() == "tab.create")
                .ShouldBeFalse("CARD-0323: first launch uses workspace.create's root pane");
            var tabRename = fake.Requests.First(r => r.GetProperty("method").GetString() == "tab.rename");
            tabRename.GetProperty("params").GetProperty("label").GetString().ShouldBe(agent.Name);
            tabRename.GetProperty("params").GetProperty("label").GetString().ShouldNotBe(definitionName);
            var paneRename = fake.Requests.First(r => r.GetProperty("method").GetString() == "pane.rename");
            paneRename.GetProperty("params").GetProperty("label").GetString().ShouldBe(agent.Name);
            paneRename.GetProperty("params").GetProperty("label").GetString().ShouldNotBe(definitionName);

            var agentRename = fake.Requests
                .Where(r => r.GetProperty("method").GetString() == "agent.rename")
                .ShouldHaveSingleItem();
            agentRename.GetProperty("params").GetProperty("target").GetString().ShouldBe(firstPane);
            agentRename.GetProperty("params").GetProperty("name").GetString()
                .ShouldBe(agent.Slug, "CARD-0211: herdr agent name is the sanitised Agent.Slug");

            await harness.Runner.SimulateRunnerRestartAsync();
            var adopted = await harness.Runner.GetAsync(firstSessionId, CancellationToken.None);
            adopted.Status.ShouldBe("Running", "R1: pane still lists the child");
            adopted.Adopted.ShouldBeTrue();
            adopted.Backend.ShouldBe(SessionBackends.Herdr);

            fake.SetPaneProcessInfo(firstPane, shellPid: 1);
            await harness.Runner.SimulateRunnerRestartAsync();
            var afterEmpty = await harness.Runner.GetAsync(firstSessionId, CancellationToken.None);
            afterEmpty.Status.ShouldBe("Exited", "emptied pane must not stay Running");
            afterEmpty.ExitReason.ShouldBe(AgentExitReason.HerdrRestartPresumedDead);
            File.Exists(HerdrPaneSidecar.PathFor(
                Path.Combine(tempRoot, "session-logs"), firstSessionId))
                .ShouldBeFalse("R2 deletes the sidecar; nothing to false-adopt next restart");
        }
        finally
        {
            if (fake is not null)
                await fake.DisposeAsync();
            await CleanupAsync(tempRoot);
        }
    }

    private static int CountAgentPanes(FakeHerdrServer fake) =>
        fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes)).Count(p => p.Agent is not null);

    private static async Task ConfirmHerdrDeliveryAsync(FakeHerdrServer fake, Guid sessionId, string nonce)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var typed = fake.Requests.Any(r =>
                r.TryGetProperty("method", out var method)
                && method.GetString() == "pane.send_text"
                && r.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("text", out var text)
                && (text.GetString() ?? "").Contains(nonce, StringComparison.Ordinal));
            if (typed)
            {
                await using var db = CreateContext();
                var row = await db.SessionQueuedMessages
                    .Where(m => m.AgentSessionId == sessionId && m.Body.Contains(nonce))
                    .OrderByDescending(m => m.Sequence)
                    .FirstAsync();
                await InsertEntryAsync(sessionId, TranscriptKinds.UserPrompt, row.Body,
                    timestamp: DateTime.UtcNow);
                return;
            }

            await Task.Delay(40);
        }

        throw new System.TimeoutException(
            $"FakeHerdrServer never saw pane.send_text containing '{nonce}'.");
    }

    private static async Task<long> InsertEntryAsync(
        Guid sessionId, string kind, string? text = null, string? stopReason = null, DateTime? timestamp = null)
    {
        await using var db = CreateContext();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = kind,
            Text = text,
            StopReason = stopReason,
            Timestamp = timestamp,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return seq;
    }

    private static async Task<string> BindChannelAsync(Harness harness, Guid agentId)
    {
        var chatId = $"-100{Random.Shared.Next(100_000, 999_999)}";
        await harness.Bridge.HandleInboundAsync(
            new ChannelMessage
            {
                Id = Guid.NewGuid().ToString("n"),
                Channel = "telegram",
                ChannelMessageId = "seed",
                Conversation = new Conversation { Id = chatId, Kind = ConversationKind.Group, Title = "Family" },
                Author = new Participant { Id = "1001" },
                Timestamp = DateTimeOffset.UtcNow,
                Text = null,
                ReplyHandle = chatId,
                Raw = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            },
            CancellationToken.None);

        await using var scope = harness.Provider.CreateAsyncScope();
        var channels = scope.ServiceProvider.GetRequiredService<ChatChannelService>();
        var channel = (await channels.GetAllAsync(CancellationToken.None))
            .Single(c => c.ExternalId == chatId);
        await channels.UpdateAsync(
            channel.Id, new UpdateChatChannelRequest(AgentId: agentId), CancellationToken.None);
        return chatId;
    }

    private static async Task<Guid> WaitForPersistentSessionAsync(Harness harness, Guid agentId)
    {
        var service = harness.Provider.CreateScope().ServiceProvider.GetRequiredService<AgentService>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var detail = await service.GetByIdAsync(agentId, CancellationToken.None);
            if (Guid.TryParse(detail.PersistentSessionId, out var id)
                && detail.Status == AgentStatus.Running
                && detail.LiveSession is { Status: SessionStatus.Running })
                return id;

            await Task.Delay(100);
        }

        throw new System.TimeoutException($"Agent {agentId} never reached a Running persistent session.");
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        string tempRoot,
        SessionBackend backend,
        FakeHerdrServer? fake,
        IReadOnlyList<FakeAgentProtocolAdapter> ptyAdapters,
        AgentKind launchKind = AgentKind.ClaudeCode)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        var messaging = new FakeAntiphonMessagingClient();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<IAntiphonMessagingProducer>(messaging);
        services.AddSingleton<IAntiphonMessagingConsumer>(messaging);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(Options.Create(new ChannelBridgeSettings
        {
            Enabled = true,
            DebounceWindowMs = 0,
        }));
        services.AddSingleton<ChannelInboundDebouncer>();
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SessionLogPath = Path.Combine(tempRoot, "session-logs"),
            RemoteControlArmTimeoutMs = 200,
            RemoteControlSetupTimeoutMs = 500,
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot,
        }));
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            TickSeconds = 1,
            BackoffBaseSeconds = 5,
            HealthyUptimeResetMinutes = 10,
            FreshAfterResumeFailures = 2,
            DeliveryVerification = new DeliveryVerificationSettings
            {
                Enabled = true,
                EvidenceTimeoutSeconds = 2,
                PollIntervalMs = 50,
                PostSubmitAdvanceTimeoutSeconds = 1,
                TranscriptConfirmTimeoutSeconds = 5,
                ReEnterIntervalSeconds = 1,
                PostFailureConfirmGraceSeconds = 2,
                BootPromptRetryDelaySeconds = 0,
            },
        }));
        services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
        var registrySettings = new AgentRegistrySettings
        {
            DefaultDefinition = launchKind switch
            {
                AgentKind.Grok => "grok",
                AgentKind.Codex => "codex",
                _ => "claude",
            },
            ClaudeReadyQuietPeriodMs = 150,
            ClaudeReadyMaxWaitMs = 5_000,
            ClaudeReadyMinTotalWaitMs = 0,
            ClaudeInputProbeTimeoutMs = 3_000,
            ClaudeInputProbePollIntervalMs = 50,
            ClaudeInputProbeClearTimeoutMs = 1_000,
            ClaudeInputProbeRetypeIntervalMs = 2_000,
            ClaudeTrustPromptSettleMs = 200,
            GrokReadyQuietPeriodMs = 150,
            GrokReadyMaxWaitMs = 5_000,
            GrokReadyMinTotalWaitMs = 0,
            CodexReadyQuietPeriodMs = 150,
            CodexReadyMaxWaitMs = 5_000,
            Definitions =
            {
                ["claude"] = new AgentDefinition
                {
                    Kind = "ClaudeCode",
                    Exe = launchKind == AgentKind.ClaudeCode && File.Exists(FakeClaudeExe)
                        ? FakeClaudeExe
                        : Cmd,
                },
                ["grok"] = new AgentDefinition
                {
                    Kind = "Grok",
                    Exe = File.Exists(FakeGrokExe) ? FakeGrokExe : Cmd,
                },
                ["codex"] = new AgentDefinition { Kind = "Codex", Exe = Cmd },
            },
        };
        services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(registrySettings));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new OptionsMonitorStub<AgentRegistrySettings>(registrySettings));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
        services.AddSingleton<IWorkspaceHookRunner>(
            new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddLogging();

        DirectSessionRunnerClient? runner = null;
        if (backend == SessionBackend.Herdr)
        {
            fake.ShouldNotBeNull();
            var herdrClient = new HerdrClient(Options.Create(new HerdrSettings
            {
                Enabled = true,
                Session = fake.Session,
            }));
            runner = new DirectSessionRunnerClient(
                Path.Combine(tempRoot, "session-logs"),
                herdrClient: herdrClient,
                processLiveness: new FakeHerdrPowershellProbe());
            services.AddSingleton<ISessionRunnerClient>(runner);
            services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
                new AgentProtocolAdapterFactory(
                    sp.GetRequiredService<IOptions<AgentRegistrySettings>>(),
                    sp.GetRequiredService<ISessionRunnerClient>(),
                    sp.GetRequiredService<IOptions<SupervisionSettings>>()));
        }
        else
        {
            services.AddSingleton<ISessionRunnerClient>(new EmptyRunnerClient());
            services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(ptyAdapters));
        }

        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<ChannelReplyDispatcher>();
        services.AddScoped<ChatChannelService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        // OrchestratorService depends on AgentSessionLaunchComposer since 1b1b667 (2026-08-26);
        // this harness's copy of that registration was missed at the time.
        services.AddScoped<AgentSessionLaunchComposer>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentControlService>();
        services.AddScoped<AgentSupervisorService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IAlertRouter, NullAlertRouter>();
        services.AddGitWorkspaceService();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<CardService>();
        services.AddScoped<BoardService>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var bridge = new ChannelBridgeService(
            messaging,
            provider.GetRequiredService<SessionMessageQueueService>(),
            provider.GetRequiredService<ChannelInboundDebouncer>(),
            eventBus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            clock,
            NullLogger<ChannelBridgeService>.Instance);

        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<AgentService>(),
            scope.ServiceProvider.GetRequiredService<AgentControlService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            provider.GetRequiredService<AgentSessionRuntime>(),
            provider.GetRequiredService<ChannelReplyDispatcher>(),
            bridge,
            messaging,
            clock,
            runner);
    }

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
            .Select(a => a.Id)
            .ToListAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => s.Cwd.StartsWith(tempRoot))
            .Select(s => s.Id)
            .ToListAsync();
        await db.SessionQueuedMessages.Where(m => sessionIds.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
        await db.TranscriptEntries.Where(t => sessionIds.Contains(t.AgentSessionId)).ExecuteDeleteAsync();
        await db.AgentIncidents.Where(i => i.AgentId != null && agentIds.Contains(i.AgentId.Value)).ExecuteDeleteAsync();
        await db.Alerts.Where(a => a.AgentId != null && agentIds.Contains(a.AgentId.Value)).ExecuteDeleteAsync();
        await db.AgentSupervisionStates.Where(s => agentIds.Contains(s.AgentId)).ExecuteDeleteAsync();
        await db.ChatChannels.Where(c => c.AgentId != null && agentIds.Contains(c.AgentId.Value)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();

        try
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        AgentService Agents,
        AgentControlService Control,
        AgentSessionLaunchQueue LaunchQueue,
        AgentSessionRuntime Runtime,
        ChannelReplyDispatcher Dispatcher,
        ChannelBridgeService Bridge,
        FakeAntiphonMessagingClient Messaging,
        MutableTimeProvider Clock,
        DirectSessionRunnerClient? Runner) : IAsyncDisposable
    {
        public AgentSupervisorService Supervisor()
        {
            var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
        }

        public async ValueTask DisposeAsync()
        {
            if (Runner is not null)
                await Runner.DisposeAsync();
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class FakeHerdrPowershellProbe : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => true;
        public string? TryGetProcessName(int pid) => "powershell";
        public DateTime? TryGetStartTimeUtc(int pid) => DateTime.UtcNow.AddMinutes(-1);
    }

    /// CARD-0222: an OFFSET over the real clock, not a frozen instant — this is the only
    /// TimeProvider in the harness, so it also feeds every `deadline = UtcNow() + N` poll loop in
    /// SessionMessageQueueService, whose Task.Delay(…, _timeProvider) runs in real time. A frozen
    /// GetUtcNow never reaches the deadline (dumpasync leaf: SettlePostEvidenceAsync → DelayPromise).
    /// Same lesson as AgentSupervisionTests' identical provider.
    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private TimeSpan _offset = start - DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;

        public void Advance(TimeSpan by) => _offset += by;
    }

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters = new(adapters);

        public IAgentProtocolAdapter Create(AgentKind kind) =>
            _adapters.TryDequeue(out var adapter)
                ? adapter
                : throw new InvalidOperationException("No fake adapter was queued for dispatch.");
    }

    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException("CARD-0186 S4 never spawns card work.");

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class EmptyRunnerClient : ISessionRunnerClient
    {
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }
}
