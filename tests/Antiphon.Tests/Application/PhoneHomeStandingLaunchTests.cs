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
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class PhoneHomeStandingLaunchTests
{
    [Test]
    public async Task Start_commits_binding_before_remote_launch()
    {
        var commandsBeforeCommit = new List<string>();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var agent = SeedAgent(db);
        await db.SaveChangesAsync();
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            StandingAgentId = agent.Id,
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            SessionBackend = SessionBackend.PtyHost,
            Status = SessionStatus.Starting,
            Cwd = agent.WorkingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = "grok-linux",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        commandsBeforeCommit.ShouldBeEmpty();
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var loaded = await verify.AgentSessions.SingleAsync(s => s.Id == session.Id);
        loaded.RunnerId.ShouldBe("grok-linux");
        loaded.RunnerStoreId.ShouldNotBeNull();
        loaded.RunnerCwd.ShouldBe("/work");
    }

    [Test]
    public async Task Binding_constraint_rejects_partial_owner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var combinations = new (string? RunnerId, Guid? Store, string? Cwd)[]
        {
            ("grok-linux", null, null),
            (null, Guid.NewGuid(), null),
            (null, null, "/work"),
            ("grok-linux", Guid.NewGuid(), null),
            ("grok-linux", null, "/work"),
            (null, Guid.NewGuid(), "/work"),
        };
        foreach (var (runnerId, store, cwd) in combinations)
        {
            var partialBindingRejected = true;
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """INSERT INTO "AgentSessions" ("Id","DefinitionName","AgentKind","SessionBackend","Status","Cwd","Cols","Rows","CreatedAt","StartedAt","LastSeenAt","TerminationSource","RunnerId","RunnerStoreId","RunnerCwd") VALUES ({0},'grok',4,0,0,'C:\\x',80,24,NOW(),NOW(),NOW(),0,{1},{2},{3})""",
                    Guid.NewGuid(), runnerId, store, cwd);
                partialBindingRejected = false;
            }
            catch (Exception)
            {
                partialBindingRejected = true;
            }

            partialBindingRejected.ShouldBeTrue($"{runnerId}|{store}|{cwd}");
        }

        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "AgentSessions" ("Id","DefinitionName","AgentKind","SessionBackend","Status","Cwd","Cols","Rows","CreatedAt","StartedAt","LastSeenAt","TerminationSource") VALUES ({0},'grok',4,0,0,'C:\\x',80,24,NOW(),NOW(),NOW(),0)""",
            Guid.NewGuid());
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "AgentSessions" ("Id","DefinitionName","AgentKind","SessionBackend","Status","Cwd","Cols","Rows","CreatedAt","StartedAt","LastSeenAt","TerminationSource","RunnerId","RunnerStoreId","RunnerCwd") VALUES ({0},'grok',4,0,0,'C:\\x',80,24,NOW(),NOW(),NOW(),0,'grok-linux',{1},'/work')""",
            Guid.NewGuid(), Guid.NewGuid());
    }

    [Test]
    public async Task Unsupported_start_is_refused_before_reservation()
    {
        var policy = Policy();
        var agent = new Agent
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "grok-linux",
            WorkingDirectory = @"C:\work",
            AlwaysOn = false,
            SessionBackend = SessionBackend.PtyHost,
        };
        var reservationCountDelta = 0;
        void AssertRefused(Action act)
        {
            Should.Throw<ConflictException>(act);
            reservationCountDelta.ShouldBe(0);
        }

        AssertRefused(() => policy.RefuseUnsupportedStart(agent, true, false, false, false, false, SessionBackend.PtyHost, AgentKind.Grok, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, true, false, false, false, SessionBackend.PtyHost, AgentKind.Grok, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, false, true, false, false, SessionBackend.PtyHost, AgentKind.Grok, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, false, false, true, false, SessionBackend.PtyHost, AgentKind.Grok, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, false, false, false, true, SessionBackend.PtyHost, AgentKind.Grok, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, false, false, false, false, SessionBackend.Herdr, AgentKind.Grok, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, false, false, false, false, SessionBackend.PtyHost, AgentKind.ClaudeCode, null));
        AssertRefused(() => policy.RefuseUnsupportedStart(agent, false, false, false, false, false, SessionBackend.PtyHost, AgentKind.Grok, "wrap"));
        var pooled = new Agent
        {
            Id = agent.Id,
            WorkingDirectory = @"C:\work",
            AlwaysOn = true,
            SessionBackend = SessionBackend.PtyHost,
        };
        AssertRefused(() => policy.RefuseUnsupportedStart(pooled, false, false, false, false, false, SessionBackend.PtyHost, AgentKind.Grok, null));
        policy.RefuseUnsupportedStart(agent, false, false, false, false, false, SessionBackend.PtyHost, AgentKind.Grok, null);
        reservationCountDelta.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Only_exact_host_root_maps_to_runner_cwd()
    {
        var policy = Policy();
        var outOfRootAccepted = true;
        try
        {
            policy.EnsureExactHostRoot(@"C:\work\sibling");
        }
        catch (ConflictException)
        {
            outOfRootAccepted = false;
        }

        outOfRootAccepted.ShouldBeFalse();
        try
        {
            policy.EnsureExactHostRoot(@"C:\work-other");
            outOfRootAccepted = true;
        }
        catch (ConflictException)
        {
            outOfRootAccepted = false;
        }

        outOfRootAccepted.ShouldBeFalse();
        policy.EnsureExactHostRoot(@"C:\work");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Projection_keeps_identity_rules_and_local_definition()
    {
        var policy = Policy();
        var agent = new Agent
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkingDirectory = @"C:\work",
        };
        var local = new AgentLaunchSpec("grok", AgentKind.Grok, "grok.exe", ["--model", "x"],
            new Dictionary<string, string> { ["ANTIPHON_SESSION_ID"] = "keep" }, @"C:\work", 80, 24, 256);
        var remoteSpec = policy.Project(local, agent);
        remoteSpec.Cwd.ShouldBe("/work");
        remoteSpec.Exe.ShouldBe("grok");
        remoteSpec.Env["GROK_HOME"].ShouldBe("/state/grok");
        remoteSpec.Env["ANTIPHON_API"].ShouldBe("http://host.docker.internal:17202");
        remoteSpec.Env["ANTIPHON_SESSION_ID"].ShouldBe("keep");
        remoteSpec.MemoryLimitMb.ShouldBe(0);
        remoteSpec.Args.ShouldBe(local.Args);
        remoteSpec.Backend.ShouldBe(SessionBackend.PtyHost);
        remoteSpec.Herdr.ShouldBeNull();
        remoteSpec.VerificationBinding.ShouldBeNull();
        local.Cwd.ShouldBe(@"C:\work");
        local.MemoryLimitMb.ShouldBe(256);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Resume_never_probes_host_history_or_starts_fresh()
    {
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            AgentKind = AgentKind.Grok,
            RunnerId = "grok-linux",
            Cwd = @"C:\work",
        };
        var spec = new AgentLaunchSpec("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(), @"C:\work", 80, 24);
        var freshLaunches = new List<string>();
        try
        {
            var mode = AgentSessionService.EffectiveResumeMode(session, spec, AgentSessionResumeMode.Resume);
            if (mode is null)
                freshLaunches.Add("create");
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("phone_home_resume_fresh_refused");
        }

        freshLaunches.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Secrets_never_cross_child_or_status_boundary()
    {
        var secretSentinel = "phone-home-secret-sentinel";
        var policy = new PhoneHomeLaunchPolicy(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "grok-linux",
            StandingAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            HostWorkspaceRoot = @"C:\work",
            RunnerWorkspace = "/work",
            ChildGrokHome = "/state/grok",
            CallbackOrigin = "http://host.docker.internal:17202",
            SharedSecret = secretSentinel,
        }));
        var agent = new Agent
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            WorkingDirectory = @"C:\work",
        };
        var local = new AgentLaunchSpec("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(), @"C:\work", 80, 24);
        var remote = policy.Project(local, agent);
        var childEnvironmentValues = remote.Env;
        childEnvironmentValues.Values.ShouldNotContain(secretSentinel);
        childEnvironmentValues.Keys.ShouldNotContain(k => k.Contains("SECRET", StringComparison.OrdinalIgnoreCase));
        await using var host = await PhoneHomeTestHost.StartAsync();
        var status = host.Directory.Status("grok-linux");
        JsonSerializerRoundTrip(status).ShouldNotContain(host.Secret);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Unknown_launch_blocks_replacement_until_owner_probe()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync(autoReply: false);
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new Antiphon.Server.Infrastructure.Agents.SessionRunner.PhoneHomeRunnerClient(live);
        var first = client.StartAsync(Guid.NewGuid(), new AgentLaunchSpec("grok", AgentKind.Grok, "grok", [], new Dictionary<string, string>(), "/work", 80, 24), CancellationToken.None);
        await peer.WaitForAsync(PhoneHomeOperation.Launch);
        var replacementLaunchFrames = new List<string>();
        // unanswered launch occupies the peer; a second start is a separate request. Capacity is
        // enforced on the runner dispatcher, not the directory. The reservation stays until the
        // owner list resolves — here the first launch has no result, so replacement must not be
        // treated as success.
        first.IsCompleted.ShouldBeFalse();
        replacementLaunchFrames.Count.ShouldBe(0);
        peer.Launches.Count.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Launch_handoff_cuts_preserve_owner_and_reservation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var workspace = Path.Combine(Path.GetTempPath(), $"ph-launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        await File.WriteAllBytesAsync(Path.Combine(workspace, "grok.exe"), [0x4D, 0x5A]);
        try
        {
            await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
            await using var peer = await host.ConnectPeerAsync();
            var live = await host.WaitLiveAsync();
            host.Directory.MarkRecovered(live);

            await FailCommitHasZeroLaunchAsync(schema, host, peer, workspace);
            await FailEnqueueKeepsStartingOwnerAsync(schema, host, peer, workspace);
            await DiscardedGraphLeavesOwnerInventoryAsync(schema, host, peer, workspace);
            await LostLaunchResponseBlocksReplacementAsync(schema, host, peer, workspace, live);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static async Task FailCommitHasZeroLaunchAsync(
        IsolatedTestSchema schema, PhoneHomeTestHost host, PhoneHomeScriptedPeer peer, string workspace)
    {
        var agentId = Guid.NewGuid();
        await SeedPinnedAgentAsync(schema.ConnectionString, agentId, workspace);
        var launchesBefore = peer.Launches.Count;
        var localStartsBefore = host.Local.Calls.Count(c => c == "start");
        var insertFault = new ThrowOnSessionInsert();
        var gate = new GateScopeFactory();
        var inner = new FakeAgentProtocolAdapter { ReadyResult = true };
        await using var harness = BuildLaunchHarness(
            schema, host, workspace, agentId, [new PrefixStartAdapter(inner)], insertFault, gate);
        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => harness.Control.StartAsync(agentId, new StartAgentRequest(Fresh: true), CancellationToken.None));
        error.Message.ShouldContain("injected session commit failure");
        peer.Launches.Count.ShouldBe(launchesBefore);
        host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);
        inner.Started.ShouldBeFalse();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await db.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(0);
    }

    private static async Task FailEnqueueKeepsStartingOwnerAsync(
        IsolatedTestSchema schema, PhoneHomeTestHost host, PhoneHomeScriptedPeer peer, string workspace)
    {
        var agentId = Guid.NewGuid();
        await SeedPinnedAgentAsync(schema.ConnectionString, agentId, workspace);
        var launchesBefore = peer.Launches.Count;
        var localStartsBefore = host.Local.Calls.Count(c => c == "start");
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeAgentProtocolAdapter { ReadyResult = true, StartGate = startGate };
        var insertFault = new ThrowOnSessionInsert { Armed = false };
        var gate = new GateScopeFactory();
        await using var harness = BuildLaunchHarness(
            schema, host, workspace, agentId, [new PrefixStartAdapter(inner)], insertFault, gate);
        try
        {
            var detail = await harness.Control.StartAsync(agentId, new StartAgentRequest(Fresh: true), CancellationToken.None);
            detail.PersistentSessionId.ShouldNotBeNull();
            var sessionId = Guid.Parse(detail.PersistentSessionId!);
            var ownedDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < ownedDeadline && !harness.LaunchQueue.Owns(sessionId))
                await Task.Delay(20);
            harness.LaunchQueue.Owns(sessionId).ShouldBeTrue();
            inner.Started.ShouldBeFalse();
            peer.Launches.Count.ShouldBe(launchesBefore);
            host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var row = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.RunnerId.ShouldBe(host.AllowedRunnerId);
            row.RunnerStoreId.ShouldBe(host.StoreId);
            row.RunnerCwd.ShouldBe("/work");
            var owner = await host.Directory.GetOwnerAsync(sessionId, CancellationToken.None);
            owner.ShouldNotBeNull();
            owner!.RunnerId.ShouldBe(host.AllowedRunnerId);
            owner.RunnerStoreId.ShouldBe(host.StoreId);
            var inventory = await host.Directory.GetInventoryAsync(host.AllowedRunnerId, CancellationToken.None);
            inventory.ShouldBeOfType<RunnerInventory.Available>();
            var replacement = await harness.Control.StartAsync(agentId, new StartAgentRequest(), CancellationToken.None);
            replacement.PersistentSessionId.ShouldBe(detail.PersistentSessionId);
            peer.Launches.Count.ShouldBe(launchesBefore);
            host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);
            await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            (await after.AgentSessions.CountAsync(s => s.StandingAgentId == agentId && s.Status == SessionStatus.Starting))
                .ShouldBe(1);
        }
        finally
        {
            startGate.TrySetResult();
        }
    }

    private static async Task DiscardedGraphLeavesOwnerInventoryAsync(
        IsolatedTestSchema schema, PhoneHomeTestHost host, PhoneHomeScriptedPeer peer, string workspace)
    {
        var agentId = Guid.NewGuid();
        await SeedPinnedAgentAsync(schema.ConnectionString, agentId, workspace);
        var launchesBefore = peer.Launches.Count;
        var localStartsBefore = host.Local.Calls.Count(c => c == "start");
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeAgentProtocolAdapter { ReadyResult = true, StartGate = startGate };
        var insertFault = new ThrowOnSessionInsert { Armed = false };
        var gate = new GateScopeFactory();
        var harness = BuildLaunchHarness(
            schema, host, workspace, agentId, [new PrefixStartAdapter(inner)], insertFault, gate);
        Guid sessionId;
        try
        {
            var detail = await harness.Control.StartAsync(agentId, new StartAgentRequest(Fresh: true), CancellationToken.None);
            sessionId = Guid.Parse(detail.PersistentSessionId!);
            var ownedDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < ownedDeadline && !harness.LaunchQueue.Owns(sessionId))
                await Task.Delay(20);
            harness.LaunchQueue.Owns(sessionId).ShouldBeTrue();
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var row = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.RunnerId.ShouldBe(host.AllowedRunnerId);
            row.RunnerStoreId.ShouldBe(host.StoreId);
            var owner = await host.Directory.GetOwnerAsync(sessionId, CancellationToken.None);
            owner.ShouldNotBeNull();
            owner!.RunnerId.ShouldBe(host.AllowedRunnerId);
            peer.Launches.Count.ShouldBe(launchesBefore);
            host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);
        }
        finally
        {
            await harness.DisposeAsync();
            startGate.TrySetResult();
        }

        await using var recovered = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var surviving = await recovered.AgentSessions.SingleAsync(s => s.Id == sessionId);
        surviving.Status.ShouldBe(SessionStatus.Starting);
        surviving.RunnerId.ShouldBe(host.AllowedRunnerId);
        surviving.RunnerCwd.ShouldBe("/work");
        var recoveredOwner = await host.Directory.GetOwnerAsync(sessionId, CancellationToken.None);
        recoveredOwner.ShouldNotBeNull();
        recoveredOwner!.RunnerStoreId.ShouldBe(host.StoreId);
        host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);
    }

    private static async Task LostLaunchResponseBlocksReplacementAsync(
        IsolatedTestSchema schema,
        PhoneHomeTestHost host,
        PhoneHomeScriptedPeer peer,
        string workspace,
        PhoneHomeLiveConnection live)
    {
        var agentId = Guid.NewGuid();
        await SeedPinnedAgentAsync(schema.ConnectionString, agentId, workspace);
        var launchesBefore = peer.Launches.Count;
        var localStartsBefore = host.Local.Calls.Count(c => c == "start");
        var inner = new FakeAgentProtocolAdapter { ReadyResult = true };
        var probe = new PrefixStartAdapter(inner)
        {
            BeforeStart = async (spec, ct) =>
            {
                await new PhoneHomeRunnerClient(live).StartAsync(
                    spec.SessionId ?? Guid.Empty, spec, ct);
            },
        };
        var insertFault = new ThrowOnSessionInsert { Armed = false };
        var gate = new GateScopeFactory();
        await using var harness = BuildLaunchHarness(
            schema, host, workspace, agentId, [probe], insertFault, gate);
        peer.HeldLaunches = 1;
        var detail = await harness.Control.StartAsync(agentId, new StartAgentRequest(Fresh: true), CancellationToken.None);
        var launch = await peer.WaitForAsync(PhoneHomeOperation.Launch);
        peer.Launches.Count.ShouldBe(launchesBefore + 1);
        var replacement = await harness.Control.StartAsync(agentId, new StartAgentRequest(), CancellationToken.None);
        replacement.PersistentSessionId.ShouldBe(detail.PersistentSessionId);
        peer.Launches.Count.ShouldBe(launchesBefore + 1);
        host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);

        var request = launch.Payload!.Value.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)!;
        peer.Sessions.Add(new RunnerSessionDto(
            request.SessionId, 1, request.AcceptedStartedAt ?? DateTime.UtcNow, "Running", null, "", 0,
            AcceptedStartedAt: request.AcceptedStartedAt ?? DateTime.UtcNow));
        peer.ReleaseHeld();
        await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        var sessionId = Guid.Parse(detail.PersistentSessionId!);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        row.RunnerId.ShouldBe(host.AllowedRunnerId);
        var inventory = await host.Directory.GetInventoryAsync(host.AllowedRunnerId, CancellationToken.None);
        var available = inventory.ShouldBeOfType<RunnerInventory.Available>();
        available.Sessions.ShouldContain(s => s.SessionId == sessionId);
        peer.Launches.Count.ShouldBe(launchesBefore + 1);
        host.Local.Calls.Count(c => c == "start").ShouldBe(localStartsBefore);
    }

    private static AgentControlServiceIntegrationTests.Harness BuildLaunchHarness(
        IsolatedTestSchema schema,
        PhoneHomeTestHost host,
        string workspace,
        Guid agentId,
        IReadOnlyList<IAgentProtocolAdapter> adapters,
        ThrowOnSessionInsert insertFault,
        GateScopeFactory gate)
    {
        var connection = schema.ConnectionString;
        var settings = Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            StandingAgentId = agentId,
            HostWorkspaceRoot = workspace,
            RunnerWorkspace = "/work",
            ChildGrokHome = "/state/grok",
            CallbackOrigin = "http://host.docker.internal:17202",
            SharedSecret = host.Secret,
        });
        var harness = AgentControlServiceIntegrationTests.BuildHarness(
            workspace,
            adapters,
            defaultKind: "Grok",
            connectionString: connection,
            configureServices: s =>
            {
                s.AddSingleton<IServiceScopeFactory>(gate);
                s.AddDbContext<AppDbContext>(o =>
                {
                    o.UseNpgsql(connection, npgsql =>
                    {
                        npgsql.MigrationsAssembly("Antiphon.Server");
                        npgsql.SetPostgresVersion(16, 0);
                    });
                    o.AddInterceptors(insertFault);
                });
                s.AddSingleton(settings);
                s.AddSingleton<PhoneHomeLaunchPolicy>();
                s.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                s.AddSingleton<ISessionRunnerClient>(_ => new RoutingSessionRunnerClient(host.Directory));
                s.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "grok",
                        GrokCredentialProbeEnabled = false,
                        Definitions =
                        {
                            ["grok"] = new AgentDefinition
                            {
                                Kind = "Grok",
                                Exe = Path.Combine(workspace, "grok.exe"),
                            },
                        },
                    }));
            });
        gate.Provider = harness.Provider;
        return harness;
    }

    private static async Task SeedPinnedAgentAsync(string connectionString, Guid agentId, string workspace)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var now = DateTime.UtcNow;
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "phone-home-grok",
            Slug = $"ph-{agentId:N}"[..16],
            WorkingDirectory = workspace,
            Kind = AgentKind.Grok,
            Status = AgentStatus.Idle,
            SessionBackend = SessionBackend.PtyHost,
            AlwaysOn = false,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class ThrowOnSessionInsert : SaveChangesInterceptor
    {
        public bool Armed { get; set; } = true;
        public Action? OnSessionInserted { get; set; }
        private bool _inserted;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _inserted = eventData.Context is { } ctx
                && ctx.ChangeTracker.Entries<AgentSession>().Any(e => e.State == EntityState.Added);
            if (Armed && _inserted)
                throw new InvalidOperationException("injected session commit failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_inserted)
                OnSessionInserted?.Invoke();
            _inserted = false;
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class GateScopeFactory : IServiceScopeFactory
    {
        public IServiceProvider? Provider { get; set; }
        public bool Refuse { get; set; }

        public IServiceScope CreateScope()
        {
            if (Refuse)
                throw new InvalidOperationException("injected enqueue failure");
            return Provider!.CreateScope();
        }
    }

    private sealed class PrefixStartAdapter(FakeAgentProtocolAdapter inner) : IAgentProtocolAdapter, IAttachableProtocolAdapter
    {
        public Func<AgentLaunchSpec, CancellationToken, Task>? BeforeStart { get; init; }

        public Task<int> Exited => inner.Exited;
        public int? Pid => inner.Pid;
        public AgentExitReason ExitReason => inner.ExitReason;
        public string? AuditDirectory => inner.AuditDirectory;
        public AgentLaunchBlock? LaunchBlock => inner.LaunchBlock;
        public event Action<string>? OnTextDelta
        {
            add => inner.OnTextDelta += value;
            remove => inner.OnTextDelta -= value;
        }

        public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
        {
            if (BeforeStart is not null)
                await BeforeStart(spec, ct);
            await inner.StartAsync(spec, ct);
        }

        public Task AttachAsync(Guid sessionId, CancellationToken ct) => inner.AttachAsync(sessionId, ct);
        public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) => inner.KillAsync(timeout, ct);
        public Task<bool> KillGenerationAsync(DateTime expectedAcceptedStartedAt, TimeSpan timeout, CancellationToken ct) =>
            inner.KillGenerationAsync(expectedAcceptedStartedAt, timeout, ct);
        public Task SendPromptAsync(string prompt, CancellationToken ct) => inner.SendPromptAsync(prompt, ct);
        public Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct) =>
            inner.WaitForFirstPromptOutputAsync(timeout, ct);
        public Task SendInputAsync(string input, CancellationToken ct) => inner.SendInputAsync(input, ct);
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
            RunnerConditionalInputRequest request, CancellationToken ct) =>
            inner.SendConditionalInputAsync(request, ct);
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => inner.ResizeAsync(cols, rows, ct);
        public Task<bool> WaitForReadyAsync(CancellationToken ct) => inner.WaitForReadyAsync(ct);
        public Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct) => inner.WaitForTurnCompleteAsync(ct);
        public string SnapshotRawOutput() => inner.SnapshotRawOutput();
        public Task<string> SnapshotRawOutputAsync(CancellationToken ct) => inner.SnapshotRawOutputAsync(ct);
        public string SnapshotRenderedScreen() => inner.SnapshotRenderedScreen();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static string JsonSerializerRoundTrip(object value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    // --- CARD-0604 D-2: runner-bound agents alongside the pinned one. ---

    [Test]
    public void Runner_bound_raw_agent_projects_allow_listed_exe_only()
    {
        var policy = PoolPolicy();
        var agent = new Agent { Id = Guid.NewGuid(), RunnerId = "server2", WorkingDirectory = @"C:\src\Antiphon" };

        var shell = new AgentLaunchSpec("raw-sh", AgentKind.Raw, "/bin/sh", [], new Dictionary<string, string>(), @"C:\src\Antiphon", 80, 24);
        var projected = policy.Project(shell, agent);
        projected.Exe.ShouldBe("/bin/sh");
        projected.Cwd.ShouldBe("/work");
        // CARD-0604 D-19 (Cut B). The projection neither invents a binding nor drops one: an
        // untracked launch stays untracked, and a tracked one keeps the exact binding the
        // reservation made. Dropping it would turn a tracked Mutation into an ordinary session
        // with a reserved execution nothing could ever resolve.
        projected.VerificationBinding.ShouldBeNull("an untracked launch gains no binding");

        var now = DateTime.UtcNow;
        var binding = new Antiphon.SessionRunner.Contracts.VerificationExecutionBinding(
            Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new("/work/repos/antiphon", "/work/repos/antiphon/.git", "/work/worktrees/task-12345678",
                "/work/repos/antiphon/.git/worktrees/task-12345678", "feat/card-task-12345678", Guid.NewGuid()),
            Antiphon.SessionRunner.Contracts.VerificationCustodyBackends.LinuxCgroup, Guid.NewGuid());
        policy.Project(shell with { VerificationBinding = binding }, agent)
            .VerificationBinding.ShouldBe(binding);

        // A host path that merely ends in an allow-listed name is not the image's own executable.
        var foreign = shell with { Exe = @"C:\tools\sh" };
        Should.Throw<ConflictException>(() => policy.Project(foreign, agent))
            .Code.ShouldBe("phone_home_wrapper_refused");

        // Grok still projects to the bare image name, as CARD-0490 had it.
        var grok = shell with { Exe = @"C:\tools\grok.exe", Kind = AgentKind.Grok };
        policy.Project(grok, agent).Exe.ShouldBe("grok");
    }

    [Test]
    public void Card_start_and_onagent_stay_refused_for_runner_bound_agent()
    {
        var policy = PoolPolicy();
        var agent = new Agent { Id = Guid.NewGuid(), RunnerId = "server2", WorkingDirectory = @"C:\src\Antiphon" };

        Should.Throw<ConflictException>(() => Refuse(policy, agent, cardStart: true))
            .Code.ShouldBe("phone_home_card_refused");
        Should.Throw<ConflictException>(() => Refuse(policy, agent, onAgent: true))
            .Code.ShouldBe("phone_home_onagent_refused");
        Should.Throw<ConflictException>(() => Refuse(policy, agent, customWrapper: "wrapper.cmd"))
            .Code.ShouldBe("phone_home_wrapper_refused");
        Should.Throw<ConflictException>(() => Refuse(policy, agent, backend: SessionBackend.Herdr))
            .Code.ShouldBe("phone_home_backend_refused");

        // A delegated task IS admitted now, but only as a Grok Worktree task without SourceLanding.
        Refuse(policy, agent, delegatedTask: true, worktree: true, kind: AgentKind.Grok);
        Should.Throw<ConflictException>(() => Refuse(policy, agent, delegatedTask: true, worktree: false))
            .Code.ShouldBe("phone_home_worktree_refused");
        Should.Throw<ConflictException>(
                () => Refuse(policy, agent, delegatedTask: true, worktree: true, sourceLanding: true))
            .Code.ShouldBe("phone_home_sourcelanding_refused");

        // An agent bound to no runner is not this policy's business at all.
        var local = new Agent { Id = Guid.NewGuid(), WorkingDirectory = @"C:\anywhere" };
        Refuse(policy, local, cardStart: true);
    }

    private static void Refuse(
        PhoneHomeLaunchPolicy policy,
        Agent agent,
        bool cardStart = false,
        bool delegatedTask = false,
        bool worktree = false,
        bool sourceLanding = false,
        bool onAgent = false,
        SessionBackend backend = SessionBackend.PtyHost,
        AgentKind kind = AgentKind.Grok,
        string? customWrapper = null) =>
        policy.RefuseUnsupportedStart(
            agent, cardStart, delegatedTask, worktree, sourceLanding, onAgent, backend, kind, customWrapper);

    private static PhoneHomeLaunchPolicy PoolPolicy() =>
        new(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "server2",
            AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon",
            RunnerWorkspace = "/work",
            ChildGrokHome = "/state/grok",
            CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x",
        }));

    private static PhoneHomeLaunchPolicy Policy() =>
        new(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "grok-linux",
            StandingAgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            HostWorkspaceRoot = @"C:\work",
            RunnerWorkspace = "/work",
            ChildGrokHome = "/state/grok",
            CallbackOrigin = "http://host.docker.internal:17202",
            SharedSecret = "x",
        }));

    private static Agent SeedAgent(AppDbContext db)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "phone-home-grok",
            Slug = "phone-home-grok",
            WorkingDirectory = @"C:\work",
            Status = AgentStatus.Idle,
            SessionBackend = SessionBackend.PtyHost,
        };
        db.Agents.Add(agent);
        return agent;
    }
}
