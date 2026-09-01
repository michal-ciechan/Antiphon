using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
/// The automatic rung of the tier ladder (CARD-0020 S4 / CARD-0158).
///
/// <para>CARD-0158 disarmed the shipped default: Debug keeps <c>EscalateTo = Frontier</c> for the
/// manual ladder but no longer carries <c>EscalateAfterMinutes</c>, so the sweep short-circuits
/// before its first query. Tests that pin the mechanism still works do so under an explicit
/// opt-in settings object — the shipped default is no longer the advertised auto-fire.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskStallEscalationTests
{
    // ---- V1: the behaviour change, pinned from the historical evidence ----------------------

    /// <summary>
    /// Fixture_9775fe45 under DEFAULT settings: the shape that escalated at 09:15:55Z on
    /// 2026-08-11 must no longer auto-escalate. Red on the pre-CARD-0158 default.
    /// </summary>
    [Test]
    public async Task The_2026_08_11_idle_after_done_shape_is_no_longer_escalated()
    {
        var (harness, stopper) = CreateHarness(); // shipped default — disarmed
        var (task, sessionId) = await EscalateClockHistoricalFixture.Seed_9775fe45Async(
            status: AgentTaskStatus.Working);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        row.ModelLevel.ShouldBe(AgentModelLevel.High);
        row.Status.ShouldBe(AgentTaskStatus.Working);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Escalated))
            .ShouldBe(0);
        stopper.Killed.ShouldNotContain(sessionId);

        // Shared-Postgres: park this quiet Debug row so a later opt-in sweep cannot escalate it.
        await EscalateClockHistoricalFixture.RetireAsync(task.Id);
    }

    [Test]
    public async Task a_stalled_task_with_an_escalation_policy_is_bumped_automatically()
    {
        // Mechanism pin under explicit opt-in: the shipped default is now DISARMED (CARD-0158).
        // An operator who re-arms EscalateAfterMinutes = 25 still gets the kill-and-requeue.
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness(OptInEscalationSettings());
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High,
            dispatchedMinutesAgo: 30);

        var escalated = await harness.AutoEscalateStalledAsync(CancellationToken.None);

        // Sweep total is shared-DB noise; pin OUR row (CLAUDE.md shared-Postgres rule).
        escalated.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = CreateContext();
        var bumped = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        bumped.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        bumped.EscalatedFrom.ShouldBe(AgentModelLevel.High);
        bumped.Status.ShouldBe(AgentTaskStatus.Queued, "requeued for a fresh dispatch at the new tier");
        bumped.FailureReason.ShouldContain("Stalled", customMessage: "the handoff must say WHY this escalated");
        stopper.Killed.ShouldContain(task.AgentSessionId!.Value, "the stalled session must actually stop");
    }

    [Test]
    public async Task transcript_progress_resets_the_stall_clock()
    {
        // Opt-in settings: under the default this would pass vacuously (nothing is armed).
        using var workspace = new TempWorkspace();
        var (harness, _) = CreateHarness(OptInEscalationSettings());
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High, dispatchedMinutesAgo: 30);
        await SeedTranscriptActivityAsync(task.AgentSessionId!.Value, minutesAgo: 2);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .ModelLevel.ShouldBe(AgentModelLevel.High);
    }

    [Test]
    public async Task a_role_with_no_escalation_target_is_left_alone()
    {
        // Deploy has no EscalateTo — a hung deploy needs a human or a timeout, not a bigger model.
        // Default settings are fine here: Deploy is unarmed either way.
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness();
        await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Deploy, AgentModelLevel.Low, dispatchedMinutesAgo: 120);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task a_task_already_at_the_target_tier_is_not_bumped_again()
    {
        // Opt-in: under the default this would pass vacuously.
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness(OptInEscalationSettings());
        await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.Frontier, dispatchedMinutesAgo: 60);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task a_task_inside_its_window_is_not_touched()
    {
        // Opt-in: under the default this would pass vacuously.
        using var workspace = new TempWorkspace();
        var (harness, _) = CreateHarness(OptInEscalationSettings());
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High, dispatchedMinutesAgo: 10);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Working);
    }

    /// <summary>
    /// The sweep's own arming predicate over <c>new DelegationSettings()</c>: no role carries both
    /// knobs, so AutoEscalateStalledAsync returns 0 before touching the database. Goes red if a
    /// future RolePolicy entry quietly re-arms the sweep.
    /// </summary>
    [Test]
    public async Task the_shipped_default_arms_no_role_at_all()
    {
        var settings = new DelegationSettings();
        settings.RolePolicy.Any(p =>
                p.Value.EscalateTo is not null && p.Value.EscalateAfterMinutes is not null)
            .ShouldBeFalse(
                "shipped RolePolicy must leave AutoEscalateStalledAsync's arming predicate empty");

        // Debug still names the manual ladder target; Test already did.
        settings.RolePolicy["Debug"].EscalateTo.ShouldBe(AgentModelLevel.Frontier);
        settings.RolePolicy["Debug"].EscalateAfterMinutes.ShouldBeNull();
        settings.RolePolicy["Test"].EscalateTo.ShouldBe(AgentModelLevel.Medium);
        settings.RolePolicy["Test"].EscalateAfterMinutes.ShouldBeNull();

        // Short-circuit: returns 0 with no DB work. A quiet Debug task is present so a regression
        // that re-arms would escalate it rather than vacuous-pass.
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness(settings);
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High, dispatchedMinutesAgo: 40);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);
        stopper.Killed.ShouldBeEmpty();
        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .ModelLevel.ShouldBe(AgentModelLevel.High);
        await EscalateClockHistoricalFixture.RetireAsync(task.Id);
    }

    /// <summary>
    /// The false-positive trap CARD-0158 retired: a working session whose last row is a ToolCall
    /// quiet for 30+ minutes (well inside the measured 88.5-min healthy local-execution window)
    /// must not be escalated by the default config, and the phase deadline that owns the shape
    /// must be the one named.
    /// </summary>
    [Test]
    public async Task a_quiet_long_local_tool_is_not_escalated_by_default()
    {
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness(); // shipped default
        // Ages past PreviewFraction (0.8 * 90 = 72) so EvaluateAsync surfaces LocalExecution,
        // but still under the 90-min breach — the measured-healthy shape.
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High, dispatchedMinutesAgo: 80);
        await SeedToolCallAsync(task.AgentSessionId!.Value, minutesAgo: 75);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);

        await using var db = CreateContext();
        var deadline = await TaskDeadlinePolicy.EvaluateAsync(
            db, task, DateTime.UtcNow, new DelegationSettings(), CancellationToken.None);
        deadline.ShouldNotBeNull("75 min quiet ToolCall is past the 0.8 preview of the 90-min limit");
        deadline.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution);
        deadline.Breached.ShouldBeFalse("30..88 min of quiet local work is healthy, not overdue");

        var (phaseKind, phaseLimit) = TaskDeadlinePolicy.ClassifyPhase(
            TranscriptKinds.ToolCall,
            TimeSpan.FromMinutes(new DelegationSettings().ModelWaitDeadlineMinutes),
            TimeSpan.FromMinutes(new DelegationSettings().LocalExecutionDeadlineMinutes));
        phaseKind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution);
        phaseLimit.ShouldBe(TimeSpan.FromMinutes(90));
        await EscalateClockHistoricalFixture.RetireAsync(task.Id);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// Re-arms the Debug auto-trigger the way an operator would in appsettings. Used by tests that
    /// pin the mechanism still works; the shipped default no longer carries EscalateAfterMinutes.
    /// </summary>
    private static DelegationSettings OptInEscalationSettings()
    {
        var settings = new DelegationSettings();
        settings.RolePolicy["Debug"].EscalateAfterMinutes = 25;
        return settings;
    }

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper) CreateHarness(
        DelegationSettings? settings = null)
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(settings ?? new DelegationSettings()));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-stall-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper);
    }

    private static async Task<AgentTask> SeedRunningTaskAsync(
        string workingDirectory, AgentTaskRole role, AgentModelLevel level, int dispatchedMinutesAgo)
    {
        var sessionId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
        });
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = $"Stall test {role}",
            Goal = "Find out why it is broken.",
            Role = role,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Working,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedTranscriptActivityAsync(Guid sessionId, int minutesAgo)
    {
        var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.AssistantText,
            Uuid = $"stall-{Guid.NewGuid():N}",
            Role = "assistant",
            Text = "Still narrowing it down...",
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedToolCallAsync(Guid sessionId, int minutesAgo)
    {
        var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.ToolCall,
            Uuid = $"stall-tool-{Guid.NewGuid():N}",
            Role = "assistant",
            ToolName = "Bash",
            ToolInput = "{\"command\":\"dotnet run --project tests/Antiphon.Tests\"}",
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-stall-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
