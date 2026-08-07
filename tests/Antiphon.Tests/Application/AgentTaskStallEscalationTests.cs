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
/// The automatic rung of the tier ladder: a Debug task that has produced nothing for its policy's
/// EscalateAfterMinutes gets stopped and requeued one tier up — that IS the "opus, then fable if it
/// can't figure it out" behaviour the feature promises, so it must fire without a human watching.
///
/// The counterpart guarantees matter as much: transcript progress resets the clock (slow ≠ stuck),
/// and roles with no configured escalation are never touched.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskStallEscalationTests
{
    [Test]
    public async Task a_stalled_task_with_an_escalation_policy_is_bumped_automatically()
    {
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness();
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High,
            dispatchedMinutesAgo: 30); // policy: Debug escalates to Frontier after 25 silent minutes

        var escalated = await harness.AutoEscalateStalledAsync(CancellationToken.None);

        escalated.ShouldBe(1);
        await using var verify = CreateContext();
        var bumped = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        bumped.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        bumped.EscalatedFrom.ShouldBe(AgentModelLevel.High);
        bumped.Status.ShouldBe(AgentTaskStatus.Queued, "requeued for a fresh dispatch at the new tier");
        bumped.FailureReason.ShouldContain("Stalled", customMessage: "the handoff must say WHY this escalated");
        stopper.Killed.ShouldBe([task.AgentSessionId!.Value], "the stalled session must actually stop");
    }

    [Test]
    public async Task transcript_progress_resets_the_stall_clock()
    {
        // Dispatched 30 minutes ago but still writing output two minutes ago: thinking, not stuck.
        // Escalating here would throw away a long investigation right before it lands.
        using var workspace = new TempWorkspace();
        var (harness, _) = CreateHarness();
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
        // A Debug task manually escalated to Frontier that stalls again has nowhere to go —
        // re-"escalating" it would just kill and restart the same tier forever.
        using var workspace = new TempWorkspace();
        var (harness, stopper) = CreateHarness();
        await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.Frontier, dispatchedMinutesAgo: 60);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);
        stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task a_task_inside_its_window_is_not_touched()
    {
        using var workspace = new TempWorkspace();
        var (harness, _) = CreateHarness();
        var task = await SeedRunningTaskAsync(
            workspace.Path, AgentTaskRole.Debug, AgentModelLevel.High, dispatchedMinutesAgo: 10);

        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Working);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper) CreateHarness()
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        // Default settings on purpose: these tests pin the SHIPPED policy (Debug: High → Frontier
        // after 25 minutes), because that default is the feature's advertised behaviour.
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-stall-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
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
