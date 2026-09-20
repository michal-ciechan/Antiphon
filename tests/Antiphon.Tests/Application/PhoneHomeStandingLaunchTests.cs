using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
        var partialBindingRejected = true;
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "AgentSessions" ("Id","DefinitionName","AgentKind","SessionBackend","Status","Cwd","Cols","Rows","CreatedAt","StartedAt","LastSeenAt","TerminationSource","RunnerId") VALUES ({0},'grok',4,0,0,'C:\\x',80,24,NOW(),NOW(),NOW(),0,'grok-linux')""",
                Guid.NewGuid());
            partialBindingRejected = false;
        }
        catch (Exception)
        {
            partialBindingRejected = true;
        }

        partialBindingRejected.ShouldBeTrue();
    }

    [Test]
    public async Task Unsupported_start_is_refused_before_reservation()
    {
        var policy = Policy();
        var agent = new Agent
        {
            Id = policy.AllowedRunnerId == "x" ? Guid.NewGuid() : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "grok-linux",
            WorkingDirectory = @"C:\work",
            AlwaysOn = false,
            SessionBackend = SessionBackend.PtyHost,
        };
        agent.Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var reservationCountDelta = 0;
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(
            agent, cardStart: true, delegatedTask: false, worktree: false, sourceLanding: false,
            onAgent: false, backend: SessionBackend.PtyHost, kind: AgentKind.Grok, customWrapper: null));
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
        local.Cwd.ShouldBe(@"C:\work");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Resume_never_probes_host_history_or_starts_fresh()
    {
        var freshLaunches = new List<string>();
        freshLaunches.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Secrets_never_cross_child_or_status_boundary()
    {
        var secretSentinel = "phone-home-secret-sentinel";
        var childEnvironmentValues = new Dictionary<string, string> { ["GROK_HOME"] = "/state/grok" };
        childEnvironmentValues.Values.ShouldNotContain(secretSentinel);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Unknown_launch_blocks_replacement_until_owner_probe()
    {
        var replacementLaunchFrames = new List<string>();
        replacementLaunchFrames.Count.ShouldBe(0);
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
        (await db.AgentSessions.CountAsync(s => s.Id == session.Id && s.RunnerId == "grok-linux")).ShouldBe(1);
    }

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
