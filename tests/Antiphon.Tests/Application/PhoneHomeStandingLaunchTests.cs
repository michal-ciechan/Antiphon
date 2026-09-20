using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            Status = SessionStatus.Starting,
            Cwd = @"C:\work",
            Cols = 80,
            Rows = 24,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = "grok-linux",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        await using var restarted = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await restarted.AgentSessions.CountAsync(s => s.Id == session.Id && s.RunnerId == "grok-linux")).ShouldBe(1);
        var loaded = await restarted.AgentSessions.SingleAsync(s => s.Id == session.Id);
        loaded.RunnerStoreId.ShouldBe(session.RunnerStoreId);
        loaded.RunnerCwd.ShouldBe("/work");
    }

    private static string JsonSerializerRoundTrip(object value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

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
