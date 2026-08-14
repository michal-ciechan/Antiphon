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
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The delivery backstop (CARD-0003/CARD-0020): a Dispatched task whose session has ZERO transcript
/// entries after the delivery window never received its brief and must FAIL with the reason, never
/// sit Dispatched forever and never escalate. Live miss 2026-08-09: four delegated tasks lost their
/// boot prompt to a pty-host race and every surface reported Running for up to 26 minutes.
///
/// A global-sweep suite: FailNeverStartedAsync scans every Dispatched task in the shared test
/// database, so this class takes NotInParallel with NO group key (see the shared-Postgres rule in
/// CLAUDE.md) and every assertion is scoped to rows this test created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskDeliveryWatchdogTests
{
    [Test]
    public async Task a_dispatched_task_with_no_transcript_after_the_window_fails_with_the_reason()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("never delivered", customMessage: "the reason must say WHAT went wrong");
        stopper.Killed.ShouldContain(task.AgentSessionId!.Value, "the never-started session must be stopped");
    }

    [Test]
    public async Task the_reason_names_a_brief_stranded_pending()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedBriefAsync(task.AgentSessionId!.Value, QueuedMessageStatus.Pending);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .FailureReason.ShouldContain("Pending", customMessage: "the queue's own state is the evidence");
    }

    [Test]
    public async Task any_transcript_entry_at_all_means_the_task_is_left_alone()
    {
        // Slow work is the stall scan's business; one transcript entry proves delivery happened.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 45);
        await SeedTranscriptEntryAsync(task.AgentSessionId!.Value);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
    }

    /// <summary>
    /// The opposite failure to a lost boot prompt, and the one that actually stranded three tasks
    /// overnight on 2026-08-11: the session ran, worked and REPORTED, but no turn could be matched
    /// to the task, so nothing settled it. Starting is not the test of a healthy task — settling
    /// is. Red before this branch existed: the "any transcript entry" check above waved it through
    /// forever on the strength of a transcript it could not use.
    /// </summary>
    [Test]
    public async Task a_task_whose_report_could_never_be_correlated_fails_instead_of_hanging()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedTranscriptEntryAsync(sessionId);
        await SeedUncorrelatedIncidentAsync(sessionId);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(
            AgentTaskStatus.Failed, "a task that reported but never correlated must not sit Dispatched");
        failed.FailureReason.ShouldContain(
            "could not be attributed", customMessage: "and the reason must not read as a lost prompt");
        failed.FailureReason.ShouldContain(
            sessionId.ToString(), customMessage: "the work may be real — say where to find it");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task a_task_inside_the_delivery_window_is_not_touched()
    {
        // The stranded-queue watchdog gets the whole window to redeliver a reverted brief first.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
    }

    /// <summary>
    /// CARD-0046's grace has to have a clock. Settlement defers while the turn-ending response's own
    /// text is still in flight, and the ordinary resolution is that text's arrival re-triggering
    /// settlement — but a response that never writes text is real (1 in 180 measured: a lone
    /// <c>end_turn</c> thinking record, then "API Error: Connection lost mid-response"), and nothing
    /// would ever come back for it. TickAsync sweeps those, so the task settles instead of sitting
    /// Dispatched until this watchdog kills it ten minutes later.
    /// </summary>
    [Test]
    public async Task a_deferred_settlement_is_swept_after_the_grace_window()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 3);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        // The sweep TickAsync runs, driven directly: TickAsync itself also dispatches every Queued
        // task in the shared test database, which is not this test's business (CLAUDE.md's
        // shared-Postgres rule) — the wiring is the one call in TickAsync above the early return.
        (await harness.SettleDeferredReportsAsync(CancellationToken.None))
            .ShouldBeGreaterThanOrEqualTo(1, "a global sweep count, so other suites' rows may add to it");

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Succeeded,
            "no transcript arrives for a response that never writes text — only the sweep can end this");
        settled.Result.ShouldBe("I'll start by reading the spec.");
    }

    [Test]
    public async Task a_deferred_settlement_inside_the_grace_window_is_left_alone()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 3);
        await SeedSplitTurnTailAsync(task.AgentSessionId!.Value, task.Id, storedMinutesAgo: 0);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "the text is very probably still ~1 s away");
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
        // Default settings on purpose: DeliveryFailTimeoutMinutes = 10 is the shipped window.
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
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-delivery-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        // CARD-0046: the dispatcher's deferred-settlement sweep calls into the reply service, so it
        // has to be a real one here (it is optional in the constructor — an unregistered one simply
        // leaves the sweep unarmed, which every other harness relies on).
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper);
    }

    private static async Task<AgentTask> SeedDispatchedTaskAsync(int dispatchedMinutesAgo)
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
            Cwd = Path.GetTempPath(),
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
            Title = "Delivery watchdog test",
            Goal = "Do the thing.",
            Role = AgentTaskRole.Plan,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedBriefAsync(Guid sessionId, QueuedMessageStatus status)
    {
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = "[delegated task] Do the thing.",
            Status = status,
            Sequence = 1,
            Origin = QueuedMessageOrigin.Delegation,
            CreatedAt = DateTime.UtcNow.AddMinutes(-11),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTranscriptEntryAsync(Guid sessionId)
    {
        var at = DateTime.UtcNow.AddMinutes(-40);
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.UserPrompt,
            Uuid = $"delivery-{Guid.NewGuid():N}",
            Role = "user",
            Text = "[delegated task] Do the thing.",
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// CARD-0046's split shape, stored <paramref name="storedMinutesAgo"/> ago: the marked brief,
    /// mid-turn narration under its own API call, and the turn-ending response's BARE TurnEnd — a
    /// thinking record carrying only the response id. Its text never arrives.
    ///
    /// CreatedAt is what the grace reads (never the record Timestamp, which is backdated up to 30 s),
    /// so that is what this back-dates.
    /// </summary>
    private static async Task SeedSplitTurnTailAsync(Guid sessionId, Guid taskId, int storedMinutesAgo)
    {
        var at = DateTime.UtcNow.AddMinutes(-storedMinutesAgo);
        await using var db = CreateContext();
        db.TranscriptEntries.AddRange(
            new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"deferred-{Guid.NewGuid():N}",
                Role = "user",
                Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing.",
                Timestamp = at,
                CreatedAt = at,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 2,
                Kind = TranscriptKinds.AssistantText,
                Uuid = $"deferred-{Guid.NewGuid():N}",
                Role = "assistant",
                Text = "I'll start by reading the spec.",
                ApiCallId = $"msg_{Guid.NewGuid():N}",
                Timestamp = at,
                CreatedAt = at,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 3,
                Kind = TranscriptKinds.TurnEnd,
                Uuid = $"deferred-{Guid.NewGuid():N}",
                Role = "assistant",
                StopReason = "end_turn",
                ApiCallId = $"msg_{Guid.NewGuid():N}",
                Timestamp = at,
                CreatedAt = at,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>The mark the reply path leaves when a finished turn fails the marker gate.</summary>
    private static async Task SeedUncorrelatedIncidentAsync(Guid sessionId)
    {
        var name = $"wd-{Guid.NewGuid():N}"[..16];
        await using var db = CreateContext();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = Path.GetTempPath(),
            Details = "Delivery watchdog test delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agent.Id,
            SessionId = sessionId,
            Kind = AgentIncidentKind.DelegateReportUncorrelated,
            Severity = AlertSeverity.Warning,
            Message = "Report could not be correlated to the task.",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
