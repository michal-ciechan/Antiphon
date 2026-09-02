using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0213 S2: <c>POST /api/agents/{id}/attach-herdr</c> success, restamp, R1–R3, R12,
/// runner-refusal passthrough, and Stop-on-attached leaves the fake pane's process alive.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentAttachHerdrTests
{
    [Test]
    public async Task Attach_binds_the_native_id_running_with_origin_attached()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var nativeId = Guid.NewGuid();
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            using var grok = PinGrokHome(tempRoot, nativeId, cwd);
            var pane = SeedGrokPane(fake, nativeId, cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);

            var detail = await harness.Control.AttachHerdrAsync(
                agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None);

            detail.Status.ShouldBe(AgentStatus.Running);
            detail.PersistentSessionId.ShouldBe(nativeId.ToString("D"));
            detail.LiveSession.ShouldNotBeNull();
            detail.LiveSession!.Id.ShouldBe(nativeId);
            detail.LiveSession.Status.ShouldBe(SessionStatus.Running);
            detail.LiveSession.HerdrOrigin.ShouldBe(HerdrPaneOrigins.Attached);

            await using (var db = CreateContext())
            {
                var row = await db.AgentSessions.SingleAsync(s => s.Id == nativeId);
                row.Status.ShouldBe(SessionStatus.Running);
                row.ComposedBundleStamp.ShouldBeNull();
                row.CardId.ShouldBeNull();
                var queued = await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == nativeId);
                queued.ShouldBe(0);
            }

            fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.send_text")
                .ShouldBeFalse("attach types nothing — no remote-control, no launch note");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Restamps_the_agents_own_stopped_row_with_the_same_id()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var nativeId = Guid.NewGuid();
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            using var grok = PinGrokHome(tempRoot, nativeId, cwd);
            var pane = SeedGrokPane(fake, nativeId, cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);

            await using (var db = CreateContext())
            {
                var now = DateTime.UtcNow.AddHours(-1);
                db.AgentSessions.Add(new AgentSession
                {
                    Id = nativeId,
                    DefinitionName = "grok",
                    AgentKind = AgentKind.Grok,
                    SessionBackend = SessionBackend.Herdr,
                    Status = SessionStatus.Stopped,
                    Cwd = cwd,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                    EndedAt = now,
                    ExitCode = 0,
                });
                var row = await db.Agents.SingleAsync(a => a.Id == agent.Id);
                row.PersistentSessionId = nativeId.ToString("D");
                await db.SaveChangesAsync();
            }

            var detail = await harness.Control.AttachHerdrAsync(
                agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None);
            detail.LiveSession!.Id.ShouldBe(nativeId);

            await using (var db = CreateContext())
            {
                (await db.AgentSessions.CountAsync(s => s.Cwd == cwd)).ShouldBe(1);
                var row = await db.AgentSessions.SingleAsync(s => s.Id == nativeId);
                row.Status.ShouldBe(SessionStatus.Running);
                row.EndedAt.ShouldBeNull();
            }
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Refuses_non_herdr_agent_and_unmapped_kinds()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            var pty = await harness.Agents.CreateAsync(
                new CreateAgentRequest($"CARD0213-pty-{Guid.NewGuid():N}"[..40], cwd, SessionBackend: SessionBackend.PtyHost),
                CancellationToken.None);

            var ptyEx = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.AttachHerdrAsync(pty.Id, new AttachHerdrPaneRequest("w2:p3"), CancellationToken.None));
            ptyEx.Code.ShouldBe(HerdrProblemTypes.Refused);

            var herdr = await CreateGrokHerdrAgentAsync(harness, cwd);
            var db = harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Agents.SingleAsync(a => a.Id == herdr.Id);
            row.Kind = AgentKind.Raw;
            await db.SaveChangesAsync();

            var rawEx = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.AttachHerdrAsync(herdr.Id, new AttachHerdrPaneRequest("w2:p3"), CancellationToken.None));
            rawEx.Code.ShouldBe("herdr_refused");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Refuses_when_a_live_session_exists()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var nativeId = Guid.NewGuid();
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            using var grok = PinGrokHome(tempRoot, nativeId, cwd);
            var pane = SeedGrokPane(fake, nativeId, cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);
            await harness.Control.AttachHerdrAsync(
                agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.AttachHerdrAsync(
                    agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None));
            ex.Code.ShouldBe(HerdrProblemTypes.SessionActive);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Refuses_when_runner_lacks_attach_capability()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake, advertiseAttach: false);
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.AttachHerdrAsync(agent.Id, new AttachHerdrPaneRequest("w2:p3"), CancellationToken.None));
            ex.Code.ShouldBe(HerdrProblemTypes.Refused);
            ex.Message.ShouldContain("herdr-attach");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Native_id_owned_elsewhere_is_refused()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var nativeId = Guid.NewGuid();
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            using var grok = PinGrokHome(tempRoot, nativeId, cwd);
            var pane = SeedGrokPane(fake, nativeId, cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);
            var other = await CreateGrokHerdrAgentAsync(harness, cwd, nameSuffix: "other");

            await using (var db = CreateContext())
            {
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = nativeId,
                    DefinitionName = "grok",
                    AgentKind = AgentKind.Grok,
                    SessionBackend = SessionBackend.Herdr,
                    Status = SessionStatus.Stopped,
                    Cwd = cwd,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                    EndedAt = now,
                });
                var owner = await db.Agents.SingleAsync(a => a.Id == other.Id);
                owner.PersistentSessionId = nativeId.ToString("D");
                await db.SaveChangesAsync();
            }

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.AttachHerdrAsync(
                    agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None));
            ex.Code.ShouldBe(HerdrProblemTypes.SessionIdTaken);
            ex.Message.ShouldContain(other.Name);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Runner_refusal_fails_the_row_with_the_code()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            var nativeId = Guid.NewGuid();
            using var grok = PinEmptyGrokHome(tempRoot);
            var pane = SeedGrokPane(fake, nativeId, cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.Control.AttachHerdrAsync(
                    agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None));
            ex.Code.ShouldBe(HerdrProblemTypes.TranscriptNotFound);

            await using var db = CreateContext();
            var row = await db.AgentSessions.SingleAsync(s => s.Id == nativeId);
            row.Status.ShouldBe(SessionStatus.Failed);
            row.FailureReason.ShouldBe(HerdrProblemTypes.TranscriptNotFound);
            row.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Stop_on_an_attached_agent_leaves_the_pane_process_alive()
    {
        var tempRoot = NewTemp();
        await using var fake = StartFake();
        try
        {
            await using var harness = BuildHarness(tempRoot, fake);
            var nativeId = Guid.NewGuid();
            var cwd = Path.Combine(tempRoot, "cwd");
            Directory.CreateDirectory(cwd);
            using var grok = PinGrokHome(tempRoot, nativeId, cwd);
            var pane = SeedGrokPane(fake, nativeId, cwd);
            var agent = await CreateGrokHerdrAgentAsync(harness, cwd);
            await harness.Control.AttachHerdrAsync(
                agent.Id, new AttachHerdrPaneRequest(pane.PaneId), CancellationToken.None);

            await harness.Control.StopAsync(agent.Id, CancellationToken.None);

            fake.Workspaces.SelectMany(w => w.Tabs).SelectMany(t => t.Panes)
                .ShouldContain(p => p.PaneId == pane.PaneId);
            fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.close")
                .ShouldBeFalse();

            await using var db = CreateContext();
            var row = await db.AgentSessions.SingleAsync(s => s.Id == nativeId);
            row.Status.ShouldBe(SessionStatus.Stopped);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    private static async Task<AgentDetailDto> CreateGrokHerdrAgentAsync(
        Harness harness, string cwd, string? nameSuffix = null)
    {
        var agent = await harness.Agents.CreateAsync(
            new CreateAgentRequest(
                $"CARD0213-{nameSuffix ?? Guid.NewGuid().ToString("N")[..8]}",
                cwd,
                SessionBackend: SessionBackend.Herdr,
                RemoteControlEnabled: false),
            CancellationToken.None);
        var db = harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Agents.SingleAsync(a => a.Id == agent.Id);
        row.Kind = AgentKind.Grok;
        await db.SaveChangesAsync();
        return await harness.Agents.GetByIdAsync(agent.Id, CancellationToken.None);
    }

    private static (string PaneId, int Pid) SeedGrokPane(FakeHerdrServer fake, Guid nativeId, string cwd)
    {
        const string paneId = "w2:p3";
        fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Grok);
        fake.SetPaneProcessInfo(
            paneId, shellPid: 1,
            [(4243, "grok.exe", new[] { "grok", "--session-id", nativeId.ToString("D") }, cwd)]);
        return (paneId, 4243);
    }

    private static GrokHomePin PinGrokHome(string tempRoot, Guid nativeId, string cwd)
    {
        var home = Path.Combine(tempRoot, "grok-home");
        var dir = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), nativeId.ToString("D"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "updates.jsonl"), "");
        var previous = Environment.GetEnvironmentVariable("GROK_HOME");
        Environment.SetEnvironmentVariable("GROK_HOME", home);
        return new GrokHomePin(previous);
    }

    private static GrokHomePin PinEmptyGrokHome(string tempRoot)
    {
        var home = Path.Combine(tempRoot, "empty-grok");
        Directory.CreateDirectory(Path.Combine(home, "sessions"));
        var previous = Environment.GetEnvironmentVariable("GROK_HOME");
        Environment.SetEnvironmentVariable("GROK_HOME", home);
        return new GrokHomePin(previous);
    }

    private static FakeHerdrServer StartFake()
    {
        var fake = new FakeHerdrServer();
        fake.Start();
        fake.WaitUntilListeningAsync().GetAwaiter().GetResult();
        return fake;
    }

    private static string NewTemp() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-card0213-{Guid.NewGuid():N}");

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(string tempRoot, FakeHerdrServer fake, bool advertiseAttach = true)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SessionLogPath = Path.Combine(tempRoot, "session-logs"),
        }));
        services.AddSingleton(Options.Create(new OrchestratorSettings()));
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings()));
        var registrySettings = new AgentRegistrySettings
        {
            DefaultDefinition = "grok",
            Definitions =
            {
                ["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok.exe" },
                ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude.exe" },
            },
        };
        services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(registrySettings));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new OptionsMonitorStub<AgentRegistrySettings>(registrySettings));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new BridgeQueueHarness.NoWorktreeManager());
        services.AddSingleton<IWorkspaceHookRunner>(
            new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddLogging();

        var runner = new DirectSessionRunnerClient(
            Path.Combine(tempRoot, "session-logs"),
            herdrClient: new HerdrClient(Options.Create(new HerdrSettings
            {
                Enabled = true,
                Session = fake.Session,
            })),
            processLiveness: new FakeHerdrPowershellProbe())
        {
            AdvertiseHerdrAttach = advertiseAttach,
        };
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
            new AgentProtocolAdapterFactory(
                sp.GetRequiredService<IOptions<AgentRegistrySettings>>(),
                sp.GetRequiredService<ISessionRunnerClient>(),
                sp.GetRequiredService<IOptions<SupervisionSettings>>()));
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<ChannelReplyDispatcher>();
        services.AddScoped<ChatChannelService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<AgentSessionLaunchComposer>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentControlService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IAlertRouter, NullAlertRouter>();
        services.AddGitWorkspaceService();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<CardService>();
        services.AddScoped<BoardService>();
        services.AddScoped<HerdrLaunchContextResolver>();
        services.AddScoped<AgentSupervisorService>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<AgentService>(),
            scope.ServiceProvider.GetRequiredService<AgentControlService>(),
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
        await db.AgentSupervisionStates.Where(s => agentIds.Contains(s.AgentId)).ExecuteDeleteAsync();
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
            // Best-effort.
        }
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        AgentService Agents,
        AgentControlService Control,
        DirectSessionRunnerClient Runner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
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

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable OnChange(Action<T, string?> listener) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class GrokHomePin(string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable("GROK_HOME", previous);
    }

    private sealed class NullAlertRouter : IAlertRouter
    {
        public Task RouteAsync(Guid alertId, CancellationToken ct) => Task.CompletedTask;
    }
}
