using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0140 S3 — the launch spec for a pinned standing agent comes from its own TUI profile.
/// T9 is the assertion standing between this change and a Grok pool delegate launching as Claude.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class PinnedProfileLaunchSpecTests
{
    [Test]
    public async Task T7_pinned_Codex_profile_spec_uses_revision_exe_and_args_not_Claude_flags()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Codex,
            sourceDefinitionName: "codex",
            executable: exe,
            arguments: ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
            models: ["gpt-5.6-terra", "gpt-5.6-luna"]);
        var (dispatcher, db) = DispatcherOf(provider);
        var cwd = Directory.CreateTempSubdirectory("antiphon-t7").FullName;
        try
        {
            var (task, agent, session) = await SeedPinnedStandingAsync(
                db, cwd, profile.Id, AgentKind.Codex, modelId: null);
            var program = new AgentTaskDispatcher.DelegateProgram(AgentKind.Codex, "codex", profile.Id);

            var spec = await dispatcher.BuildLaunchSpecAsync(
                task, agent, session, program, attachedBundleKeys: null, CancellationToken.None);
            var args = spec.Args.ToList();

            spec.Exe.ShouldBe(exe);
            args[0].ShouldBe("--no-alt-screen");
            args[1].ShouldBe("--dangerously-bypass-approvals-and-sandbox");
            args.ShouldContain("-c");
            args.ShouldContain(a => a.StartsWith("developer_instructions=", StringComparison.Ordinal));
            args.ShouldNotContain("--append-system-prompt");
            args.ShouldNotContain("--name");
        }
        finally
        {
            try { Directory.Delete(cwd, recursive: true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task T8_model_rule_one_flag_from_ModelId_or_the_profile_kind_tier_alias()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var (profile, _) = await SeedProfileAsync(
            provider,
            AgentKind.Codex,
            sourceDefinitionName: "codex",
            executable: exe,
            arguments: ["--no-alt-screen"],
            models: ["gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.6-sol"]);
        var (dispatcher, db) = DispatcherOf(provider);
        var cwd = Directory.CreateTempSubdirectory("antiphon-t8").FullName;
        try
        {
            var (exactTask, exactAgent, exactSession) = await SeedPinnedStandingAsync(
                db, cwd, profile.Id, AgentKind.Codex, modelId: "gpt-5.6-luna", modelLevel: AgentModelLevel.High);
            var exactProgram = new AgentTaskDispatcher.DelegateProgram(AgentKind.Codex, "codex", profile.Id);
            var exactArgs = (await dispatcher.BuildLaunchSpecAsync(
                exactTask, exactAgent, exactSession, exactProgram, null, CancellationToken.None))
                .Args.ToList();
            exactArgs.Count(a => a == "--model").ShouldBe(1);
            exactArgs[exactArgs.IndexOf("--model") + 1].ShouldBe("gpt-5.6-luna");

            var (tierTask, tierAgent, tierSession) = await SeedPinnedStandingAsync(
                db, cwd, profile.Id, AgentKind.Codex, modelId: null, modelLevel: AgentModelLevel.High);
            var tierProgram = new AgentTaskDispatcher.DelegateProgram(AgentKind.Codex, "codex", profile.Id);
            var tierArgs = (await dispatcher.BuildLaunchSpecAsync(
                tierTask, tierAgent, tierSession, tierProgram, null, CancellationToken.None))
                .Args.ToList();
            tierArgs.Count(a => a == "--model").ShouldBe(1);
            tierArgs[tierArgs.IndexOf("--model") + 1].ShouldBe("gpt-5.6-terra");
            tierArgs.ShouldNotContain("opus");

            var queued = await SeedQueuedPinnedAsync(
                db, cwd, exactAgent.Id, AgentKind.Codex, AgentModelLevel.High);
            await dispatcher.TickAsync(CancellationToken.None);
            var detail = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == queued.Id && e.Type == AgentTaskEventType.Dispatched)
                .Select(e => e.Detail)
                .SingleAsync();
            detail.ShouldContain("gpt-5.6-luna");
            detail.ShouldContain("agent ModelId");
            detail.ShouldNotContain("gpt-5.6-terra");
        }
        finally
        {
            try { Directory.Delete(cwd, recursive: true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task T9_pool_and_profileless_standing_carve_outs_stay_on_the_registry()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var provider = BuildProvider(schema.ConnectionString);
        var (dispatcher, db) = DispatcherOf(provider);
        var cwd = Directory.CreateTempSubdirectory("antiphon-t9").FullName;
        try
        {
            // Installation-default Claude profile: the D2(3) hazard. ResolveForAgentAsync on a
            // null TuiProfileId would load this and launch Claude for a Grok task.
            await SeedProfileAsync(
                provider,
                AgentKind.ClaudeCode,
                sourceDefinitionName: "claude",
                executable: Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                arguments: ["--dangerously-skip-permissions"],
                isDefault: true);

            var (claudeProfile, _) = await SeedProfileAsync(
                provider,
                AgentKind.ClaudeCode,
                sourceDefinitionName: "claude",
                executable: Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                arguments: ["--dangerously-skip-permissions"],
                displayName: "backfilled-claude");

            // (1) unpinned pool delegate: BuildLaunchSpecAsync with ProfileId null is byte-identical
            // to today's BuildLaunchSpec (the four existing suites' contract).
            var unpinned = TaskAndPool(cwd, AgentKind.Grok, tuiProfileId: null);
            var unpinnedSession = SessionFor(unpinned.Task, unpinned.Agent);
            var today = dispatcher.BuildLaunchSpec(unpinned.Task, unpinned.Agent, unpinnedSession);
            var viaAsync = await dispatcher.BuildLaunchSpecAsync(
                unpinned.Task,
                unpinned.Agent,
                unpinnedSession,
                new AgentTaskDispatcher.DelegateProgram(AgentKind.Grok, "grok", null),
                null,
                CancellationToken.None);
            viaAsync.DefinitionName.ShouldBe(today.DefinitionName);
            viaAsync.Kind.ShouldBe(today.Kind);
            viaAsync.Exe.ShouldBe(today.Exe);
            viaAsync.Args.ShouldBe(today.Args);
            viaAsync.Cwd.ShouldBe(today.Cwd);

            // (2) pinned POOL delegate carrying a backfilled claude profile still resolves by Kind.
            db.Agents.Add(unpinned.Agent);
            var grokTask = new AgentTask
            {
                Id = Guid.NewGuid(),
                RootTaskId = Guid.NewGuid(),
                Title = "grok work",
                Goal = "grok work",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Custom,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = cwd,
                AgentId = unpinned.Agent.Id,
                Status = AgentTaskStatus.Queued,
                CreatedAt = DateTime.UtcNow,
            };
            grokTask.RootTaskId = grokTask.Id;
            var poolWithBackfill = new Agent
            {
                Id = Guid.NewGuid(),
                Name = $"task-{Guid.NewGuid():N}"[..13],
                Slug = $"pool-{Guid.NewGuid():N}"[..13],
                WorkingDirectory = cwd,
                Details = "Historical pool row with a backfilled claude profile (CARD-0138 R1).",
                Status = AgentStatus.Idle,
                ModelLevel = AgentModelLevel.High,
                Kind = AgentKind.Grok,
                TuiProfileId = claudeProfile.Id,
                IsPoolDelegate = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            grokTask.AgentId = poolWithBackfill.Id;
            db.Agents.Add(poolWithBackfill);
            db.AgentTasks.Add(grokTask);
            await db.SaveChangesAsync();

            var poolProgram = await dispatcher.ResolveDelegateProgramAsync(grokTask, CancellationToken.None);
            poolProgram.ProfileId.ShouldBeNull(
                "a pool row's backfilled claude profile must not take the profile path");
            poolProgram.Kind.ShouldBe(AgentKind.Grok);
            poolProgram.DefinitionName.ShouldBe("grok");

            var poolSpec = await dispatcher.BuildLaunchSpecAsync(
                grokTask,
                poolWithBackfill,
                SessionFor(grokTask, poolWithBackfill),
                poolProgram,
                null,
                CancellationToken.None);
            poolSpec.Kind.ShouldBe(AgentKind.Grok);
            poolSpec.DefinitionName.ShouldBe("grok");
            poolSpec.Args.ShouldContain("--always-approve");
            poolSpec.Args.ShouldNotContain("--dangerously-skip-permissions");

            // (3) pinned standing agent with TuiProfileId == null still uses the registry, even
            // though a default Claude profile exists.
            var standing = new Agent
            {
                Id = Guid.NewGuid(),
                Name = "profileless-grok",
                Slug = $"stand-{Guid.NewGuid():N}"[..16],
                WorkingDirectory = cwd,
                Details = "Standing Grok, no profile.",
                Status = AgentStatus.Idle,
                ModelLevel = AgentModelLevel.High,
                Kind = AgentKind.Grok,
                TuiProfileId = null,
                IsPoolDelegate = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            var standingTask = new AgentTask
            {
                Id = Guid.NewGuid(),
                Title = "standing grok",
                Goal = "standing grok",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Custom,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = cwd,
                AgentId = standing.Id,
                Status = AgentTaskStatus.Queued,
                CreatedAt = DateTime.UtcNow,
            };
            standingTask.RootTaskId = standingTask.Id;
            db.Agents.Add(standing);
            db.AgentTasks.Add(standingTask);
            await db.SaveChangesAsync();

            var standingProgram = await dispatcher.ResolveDelegateProgramAsync(
                standingTask, CancellationToken.None);
            standingProgram.ProfileId.ShouldBeNull(
                "absence of a profile is not evidence of the installation default");
            standingProgram.Kind.ShouldBe(AgentKind.Grok);
            standingProgram.DefinitionName.ShouldBe("grok");
        }
        finally
        {
            try { Directory.Delete(cwd, recursive: true); }
            catch (IOException) { }
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            PoolIdleRetireMinutes = 525_600,
            PoolMaxIdlePerDirectory = int.MaxValue,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            FinalMessageGraceSeconds = 0,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok",
                Exe = "grok",
                ArgsTemplate = ["--always-approve", "--no-alt-screen"],
            };
            s.Definitions["codex"] = new AgentDefinition
            {
                Kind = "Codex",
                Exe = "codex",
                ArgsTemplate = ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-pin-profile-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<RecordingLaunchSecretProtector>();
        services.AddSingleton<IAgentTuiSecretProtector>(sp =>
            sp.GetRequiredService<RecordingLaunchSecretProtector>());
        services.AddSingleton<AgentTuiMetrics>();
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddScoped<AgentTuiLaunchResolver>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private static (AgentTaskDispatcher Dispatcher, AppDbContext Db) DispatcherOf(ServiceProvider provider)
    {
        var scope = provider.CreateScope();
        return (
            scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<(AgentTuiProfile Profile, AgentTuiProfileRevision Revision)> SeedProfileAsync(
        ServiceProvider provider,
        AgentKind kind,
        string sourceDefinitionName,
        string executable,
        string[] arguments,
        string[]? models = null,
        bool isDefault = false,
        string? displayName = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName ?? $"profile-{Guid.NewGuid():N}"[..20],
            Kind = kind,
            IsEnabled = true,
            IsDefault = isDefault,
            Source = AgentTuiProfileSource.Operator,
            SourceDefinitionName = sourceDefinitionName,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();

        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = executable,
            ArgumentsJson = JsonSerializer.Serialize(arguments),
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = "[]",
            ModelArgumentName = "--model",
            Guidance = "CARD-0140 S3",
            CreatedAt = now,
        };
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();
        profile.ActiveRevisionId = revision.Id;

        foreach (var model in models ?? [])
        {
            db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Identifier = model,
                DisplayName = model,
                Source = AgentTuiModelSource.Operator,
                Availability = AgentTuiModelAvailability.Verified,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync();
        return (profile, revision);
    }

    private static async Task<(AgentTask Task, Agent Agent, AgentSession Session)> SeedPinnedStandingAsync(
        AppDbContext db,
        string cwd,
        Guid profileId,
        AgentKind kind,
        string? modelId,
        AgentModelLevel modelLevel = AgentModelLevel.High)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"standing-{kind}",
            Slug = $"pin-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = cwd,
            Details = "Pinned standing agent.",
            Status = AgentStatus.Idle,
            ModelLevel = modelLevel,
            ModelId = modelId,
            Kind = kind,
            TuiProfileId = profileId,
            AlwaysOn = false,
            IsPoolDelegate = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "pinned standing",
            Goal = "pinned standing",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = kind,
            ModelLevel = modelLevel,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            AgentId = agent.Id,
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = DateTime.UtcNow,
        };
        task.RootTaskId = task.Id;
        var session = SessionFor(task, agent);
        db.Agents.Add(agent);
        db.AgentTasks.Add(task);
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return (task, agent, session);
    }

    private static async Task<AgentTask> SeedQueuedPinnedAsync(
        AppDbContext db, string cwd, Guid agentId, AgentKind kind, AgentModelLevel level)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "queued pin",
            Goal = "queued pin",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = kind,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            AgentId = agentId,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        task.RootTaskId = task.Id;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static (AgentTask Task, Agent Agent) TaskAndPool(string cwd, AgentKind kind, Guid? tuiProfileId)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "unpinned pool",
            Goal = "unpinned pool",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            AgentKind = kind,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        task.RootTaskId = task.Id;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{DelegationReportFormatter.Short(task.Id)}",
            Slug = $"task-{DelegationReportFormatter.Short(task.Id)}",
            WorkingDirectory = cwd,
            Kind = kind,
            TuiProfileId = tuiProfileId,
            IsPoolDelegate = true,
            ModelLevel = AgentModelLevel.High,
            Status = AgentStatus.Idle,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        return (task, agent);
    }

    private static AgentSession SessionFor(AgentTask task, Agent agent) => new()
    {
        Id = Guid.NewGuid(),
        DefinitionName = task.AgentKind switch
        {
            AgentKind.Codex => "codex",
            AgentKind.Grok => "grok",
            _ => "claude",
        },
        AgentKind = task.AgentKind,
        Status = SessionStatus.Starting,
        Cwd = task.WorkingDirectory,
        Cols = 120,
        Rows = 30,
        CreatedAt = DateTime.UtcNow,
        StartedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };
}
