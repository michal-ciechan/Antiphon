using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0136 S1: the pure threshold rule, the shared subscription key, and the settings defaults.
/// Snapshot tests hand-build <see cref="SubscriptionUsageSnapshot"/> so they never touch the
/// shared Postgres (the shared-Postgres rule).
/// </summary>
[Category("Unit")]
public sealed class SubscriptionQuotaGateTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SubscriptionQuotaGateSettings Defaults = new();

    [Test]
    public void Evaluate_passes_when_there_is_no_snapshot()
    {
        SubscriptionQuotaPolicy.Evaluate(null, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_passes_when_the_sample_is_older_than_MaxSampleAge()
    {
        var snapshot = Snap(
            remaining: 3,
            resetsAt: Now.AddHours(36),
            age: TimeSpan.FromMinutes(Defaults.MaxSampleAgeMinutes + 1));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_passes_when_ResetsAt_is_already_in_the_past()
    {
        var snapshot = Snap(
            remaining: 3,
            resetsAt: Now.AddHours(-1),
            age: TimeSpan.FromMinutes(5));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_trips_the_day_rule_at_10_percent_with_36h_left()
    {
        var snapshot = Snap(remaining: 10, resetsAt: Now.AddHours(36), age: TimeSpan.FromMinutes(5));

        var verdict = SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now);
        verdict.ShouldNotBeNull();
        verdict.RuleName.ShouldBe("low-with-a-day-left");
        verdict.RemainingPercent.ShouldBe(10);
        verdict.TimeToReset.ShouldBe(TimeSpan.FromHours(36));
    }

    [Test]
    public void Evaluate_does_not_trip_at_10_percent_with_6h_left()
    {
        var snapshot = Snap(remaining: 10, resetsAt: Now.AddHours(6), age: TimeSpan.FromMinutes(5));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_trips_the_hours_rule_at_5_percent_with_3h_left()
    {
        var snapshot = Snap(remaining: 5, resetsAt: Now.AddHours(3), age: TimeSpan.FromMinutes(5));

        var verdict = SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now);
        verdict.ShouldNotBeNull();
        verdict.RuleName.ShouldBe("critical-with-hours-left");
        verdict.RemainingPercent.ShouldBe(5);
    }

    [Test]
    public void Evaluate_does_not_trip_at_5_percent_with_1h_left()
    {
        var snapshot = Snap(remaining: 5, resetsAt: Now.AddHours(1), age: TimeSpan.FromMinutes(5));

        SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now).ShouldBeNull();
    }

    [Test]
    public void Evaluate_uses_the_assumed_week_when_ResetsAt_is_null()
    {
        var snapshot = Snap(remaining: 10, resetsAt: null, age: TimeSpan.FromMinutes(5));

        var verdict = SubscriptionQuotaPolicy.Evaluate(snapshot, Defaults, Now);
        verdict.ShouldNotBeNull();
        verdict.RuleName.ShouldBe("low-with-a-day-left");
        verdict.TimeToReset.ShouldBe(TimeSpan.FromMinutes(Defaults.AssumedMinutesToResetWhenUnknown));
    }

    [Test]
    public void Evaluate_is_inert_when_Enabled_is_false()
    {
        var snapshot = Snap(remaining: 3, resetsAt: Now.AddHours(36), age: TimeSpan.FromMinutes(5));
        var settings = new SubscriptionQuotaGateSettings { Enabled = false };

        SubscriptionQuotaPolicy.Evaluate(snapshot, settings, Now).ShouldBeNull();
    }

    [Test]
    public void KeyFor_and_SubscriptionUsageKey_agree_for_profile_and_profileless_agents()
    {
        var profileId = Guid.NewGuid();
        var withProfile = new Agent { TuiProfileId = profileId };
        var withoutProfile = new Agent { TuiProfileId = null };

        SubscriptionUsageKey.For(withProfile, AgentKind.Codex)
            .ShouldBe(profileId.ToString("D"));
        SubscriptionUsageMonitorService.KeyFor(withProfile, AgentKind.Codex)
            .ShouldBe(SubscriptionUsageKey.For(withProfile, AgentKind.Codex));

        SubscriptionUsageKey.For(withoutProfile, AgentKind.Codex).ShouldBe("Codex");
        SubscriptionUsageMonitorService.KeyFor(withoutProfile, AgentKind.Codex)
            .ShouldBe(SubscriptionUsageKey.For(withoutProfile, AgentKind.Codex));
        SubscriptionUsageKey.For(null, AgentKind.Grok).ShouldBe("Grok");
        SubscriptionUsageMonitorService.KeyFor(null, AgentKind.Grok)
            .ShouldBe(SubscriptionUsageKey.For(null, AgentKind.Grok));
    }

    [Test]
    public void defaults_match_the_plan()
    {
        Defaults.Enabled.ShouldBeTrue();
        Defaults.MaxSampleAgeMinutes.ShouldBe(180);
        Defaults.AssumedMinutesToResetWhenUnknown.ShouldBe(10_080);
        Defaults.Rules.Count.ShouldBe(2);
        Defaults.Rules[0].Name.ShouldBe("low-with-a-day-left");
        Defaults.Rules[0].MaxRemainingPercent.ShouldBe(10);
        Defaults.Rules[0].MinMinutesToReset.ShouldBe(1440);
        Defaults.Rules[1].Name.ShouldBe("critical-with-hours-left");
        Defaults.Rules[1].MaxRemainingPercent.ShouldBe(5);
        Defaults.Rules[1].MinMinutesToReset.ShouldBe(120);
    }

    [Test]
    public void validator_rejects_out_of_range_rules_and_negative_minutes()
    {
        var result = new SubscriptionQuotaGateSettingsValidator().Validate(null, new SubscriptionQuotaGateSettings
        {
            MaxSampleAgeMinutes = -1,
            AssumedMinutesToResetWhenUnknown = -5,
            Rules =
            [
                new() { Name = "bad-percent", MaxRemainingPercent = 101, MinMinutesToReset = 0 },
                new() { Name = "bad-minutes", MaxRemainingPercent = 10, MinMinutesToReset = -1 },
            ],
        });

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBe(4);
    }

    [Test]
    public void validator_accepts_the_defaults()
    {
        new SubscriptionQuotaGateSettingsValidator()
            .Validate(null, new SubscriptionQuotaGateSettings())
            .Succeeded.ShouldBeTrue();
    }

    [Test]
    [Category("Integration")]
    [NotInParallel]
    public async Task Dispatch_records_an_informational_warning_and_never_refuses_on_a_low_reading()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var (agentId, _) = await SeedWarmCodexAgentAsync(schema.ConnectionString, workspace.Path);
        var task = await SeedQueuedCodexTaskAsync(schema.ConnectionString, workspace.Path, agentId);
        await SeedUsageSampleAsync(schema.ConnectionString, AgentKind.Codex, "Codex", remaining: 3, hoursToReset: 36);
        await DrainOtherQueuedAsync(schema.ConnectionString, task.Id);

        var dispatcher = CreateDispatchHarness(schema.ConnectionString);
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext(schema.ConnectionString);
        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.FailureReason.ShouldBeNull();
        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("dispatched on Codex");
        warning.Detail.ShouldContain("3% remaining");
        warning.Detail.ShouldContain("quota gate was passed/overridden at create");
    }

    private static SubscriptionUsageSnapshot Snap(double remaining, DateTime? resetsAt, TimeSpan age) =>
        new(
            AgentKind.Codex,
            "test-key",
            "SuperPlan",
            remaining,
            resetsAt,
            ObservedAt: Now - age,
            Age: age);

    private static AppDbContext CreateContext(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task<AgentTask> SeedQueuedCodexTaskAsync(
        string connectionString, string directory, Guid pinnedAgentId)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "quota-dispatch test",
            Goal = $"quota dispatch {id:N}",
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Queued,
            AgentId = pinnedAgentId,
            ReplyTo = AgentTaskReplyTo.None,
            ExpectedDurationMinutes = 10,
            CreatedAt = now,
        };
        await using var db = CreateContext(connectionString);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmCodexAgentAsync(
        string connectionString, string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext(connectionString);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "codex",
            AgentKind = AgentKind.Codex,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm Codex pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-3),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task DrainOtherQueuedAsync(string connectionString, params Guid[] keep)
    {
        await using var db = CreateContext(connectionString);
        var leftovers = await db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Queued && !keep.Contains(t.Id))
            .ToListAsync();
        foreach (var leftover in leftovers)
        {
            leftover.Status = AgentTaskStatus.Canceled;
            leftover.CompletedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedUsageSampleAsync(
        string connectionString, AgentKind provider, string key, double remaining, int hoursToReset)
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext(connectionString);
        db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            SubscriptionKey = key,
            PlanLabel = "SuperPlan",
            RemainingPercent = remaining,
            ResetsAt = now.AddHours(hoursToReset),
            ObservedAt = now,
            AgentSessionId = Guid.NewGuid(),
            SourceCommand = "/status",
            ParseStatus = SubscriptionUsageParseStatus.Parsed,
            RawExcerpt = "seeded",
        });
        await db.SaveChangesAsync();
    }

    private static AgentTaskDispatcher CreateDispatchHarness(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
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
            s.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-quota-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<SubscriptionUsageReader>();
        services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
        services.AddScoped<SubscriptionQuotaGate>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-quota-dispatch").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
