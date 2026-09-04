using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0299 S2: NoSubmitOutput on a cold first delivery (null baseline, origin Delegation,
/// attempts 1, Dispatched task) is BootWedged + kill + one relaunch, not a 10-minute watchdog.
///
/// <para>CARD-0312 S4 generalised the KIND and nothing else. "The composer holds the brief and
/// Enter produced no output" was measured on Codex (3 of 55 sessions, 5.5%) but it is a fact
/// about a TUI, not about Codex, so the gate is now "this kind's delivery is transcript-verified".
/// The Codex tests below are the regression guard on that: same incident, same text, same kill,
/// same relaunch count.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueBootWedgeTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    [Test]
    public async Task Codex_cold_first_delivery_NoSubmitOutput_relaunches_once()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        var taskId = await SeedDispatchedTaskAsync(h);
        var dispatchedAt = (await ReadTaskAsync(taskId)).DispatchedAt;
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "codex brief that never submits", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged);
        incident.ShouldNotBeNull();
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain("brief still in composer");
        // CARD-0312 P3: the generalisation must leave Codex byte-identical. The MCP-boot clause is
        // a Codex SCREEN fact and stays Codex-only, so the message shape is exactly what it was.
        incident.Message.ShouldBe("TUI stopped painting; brief still in composer.");

        var oldRow = await db.SessionQueuedMessages.SingleAsync(
            m => m.AgentSessionId == h.SessionId);
        oldRow.Status.ShouldBe(QueuedMessageStatus.Canceled);

        h.Adapter.Killed.ShouldBeTrue();

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.BootWedgeRelaunchCount.ShouldBe(1);
        task.AgentSessionId.ShouldNotBe(h.SessionId);
        task.DispatchedAt.ShouldNotBeNull();
        task.DispatchedAt.Value.ShouldBeGreaterThan(dispatchedAt ?? DateTime.MinValue);

        var newRow = await db.SessionQueuedMessages.SingleOrDefaultAsync(
            m => m.AgentSessionId == task.AgentSessionId && m.Origin == QueuedMessageOrigin.Delegation);
        newRow.ShouldNotBeNull("relaunch re-enqueues the brief onto the new session");
    }

    [Test]
    public async Task a_non_codex_delivery_verified_kind_takes_the_same_recovery()
    {
        // CARD-0312 P2. Before the generalisation this session sat wedged until the 10-minute
        // delivery watchdog; now it is killed and relaunched once, ~40s after dispatch.
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        var taskId = await SeedDispatchedTaskAsync(h, kind: AgentKind.Grok);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "grok brief that never submits", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged);
        incident.ShouldNotBeNull("the wedge shape is a TUI fact, not a Codex fact");
        incident.Message.ShouldNotContain("MCP boot line", customMessage:
            "the MCP clause is a Codex screen fact and must not follow the generalisation");
        h.Adapter.Killed.ShouldBeTrue();

        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "one relaunch, not a failure");
        task.BootWedgeRelaunchCount.ShouldBe(1);
        task.AgentSessionId.ShouldNotBe(h.SessionId);
    }

    [Test]
    public async Task a_kind_with_no_transcript_ground_truth_still_does_not_boot_wedge()
    {
        // The generalisation stops exactly where the evidence does: an OpenCode/Raw session
        // delivers blind, so NoSubmitOutput there is not the measured wedge shape and killing on
        // it would be a screen-only verdict (CARD-0055/CARD-0264).
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.OpenCode);
        var taskId = await SeedDispatchedTaskAsync(h, kind: AgentKind.OpenCode);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "opencode brief", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged)).ShouldBeFalse();
        (await ReadTaskAsync(taskId)).BootWedgeRelaunchCount.ShouldBe(0);
    }

    [Test]
    public async Task Codex_second_wedge_fails_the_task()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        var taskId = await SeedDispatchedTaskAsync(h, relaunchCount: 1);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "second wedge after the one relaunch", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        var task = await ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldContain("relaunched once and wedged again");
        h.Adapter.Killed.ShouldBeTrue();
    }

    [Test]
    public async Task attempts_2_does_not_boot_wedge()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        var taskId = await SeedDispatchedTaskAsync(h);
        await SeedPendingDelegationAsync(h.SessionId, attempts: 1);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "later body", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeFalse();
        h.Adapter.Killed.ShouldBeFalse();
        (await ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task non_null_baseline_does_not_boot_wedge()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        await SeedDispatchedTaskAsync(h);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "a prior turn", timestamp: DateTime.UtcNow);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "later body", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeFalse();
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task ui_origin_does_not_boot_wedge()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        var taskId = await SeedDispatchedTaskAsync(h);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "operator typed this", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Ui);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeFalse();
        h.Adapter.Killed.ShouldBeFalse();
        (await ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    /// <summary>
    /// CARD-0312 S4 turned this pin around, deliberately. It used to assert that only Codex could
    /// boot-wedge, because that is where the shape was MEASURED — but "the composer holds the
    /// brief and Enter produced no output" is a fact about a TUI, and it is the same shape
    /// CARD-0055 measured on Claude, where a delivery marked Sent on a redraw sat unsubmitted for
    /// 104 minutes. The conjunction stays as narrow as it ever was (cold first delivery, origin
    /// Delegation, attempts 1, null baseline, a Dispatched task), so what changed is which kinds
    /// may reach a recovery that was already correct, not when it fires.
    /// </summary>
    [Test]
    public async Task claude_kind_now_takes_the_same_recovery()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        var taskId = await SeedDispatchedTaskAsync(h, kind: AgentKind.ClaudeCode);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "claude brief", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeTrue();
        h.Adapter.Killed.ShouldBeTrue();
        var task = await ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "one relaunch, not a failure");
        task.BootWedgeRelaunchCount.ShouldBe(1);
    }

    [Test]
    public async Task no_dispatched_task_reverts_today()
    {
        await using var h = await CreateBootWedgeHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "no task on this session", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeFalse();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task always_on_NoSubmitOutput_still_kills_without_BootWedged()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureServices = RegisterDispatcher,
        });
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        await h.Queue.EnqueueAsync(
            h.SessionId, "channel-bound always-on", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Ui);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeFalse();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeTrue();
        h.Adapter.Killed.ShouldBeTrue();
    }

    private static Task<BridgeQueueHarness> CreateBootWedgeHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConfigureServices = RegisterDispatcher,
        });

    private static void RegisterDispatcher(IServiceCollection services)
    {
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-bootwedge-wt"),
        }));
        services.AddSingleton<IGitService, GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
        services.AddSingleton<BootWedgeRelaunchState>();
        services.AddScoped<AgentTaskDispatcher>();
    }

    private static async Task SetKindAsync(Guid sessionId, AgentKind kind)
    {
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, kind));
    }

    private static async Task<Guid> SeedDispatchedTaskAsync(
        BridgeQueueHarness h, int relaunchCount = 0, AgentKind kind = AgentKind.Codex)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow.AddMinutes(-1);
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0299 boot-wedge test",
            Goal = "do the work",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Plan,
            AgentKind = kind,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.Combine(h.TempRoot, "workspace"),
            AgentId = h.AgentId,
            AgentName = "boot-wedge",
            AgentSessionId = h.SessionId,
            Status = AgentTaskStatus.Dispatched,
            Ephemeral = true,
            CreatedAt = now,
            DispatchedAt = now,
            BootWedgeRelaunchCount = relaunchCount,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedPendingDelegationAsync(Guid sessionId, int attempts, long? baseline = null)
    {
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = "older brief still pending",
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            Origin = QueuedMessageOrigin.Delegation,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            DeliveryAttempts = attempts,
            LastDeliveryBaselineSequence = baseline,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> ReadTaskAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
    }
}
