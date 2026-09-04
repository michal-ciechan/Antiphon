using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0334 S2 — idle-boundary relaunch when standing instructions drifted. Shared-Postgres
/// rules: every assertion is scoped to a row this test created, and the class takes
/// <c>[NotInParallel]</c> with NO group key because SweepAsync walks every Running standing
/// session.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class PolicyRefreshServiceTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    [Test]
    public async Task Drift_and_idle_kills_resumes_and_records_incident()
    {
        await using var h = await CreateHarnessAsync();
        var factory = Factory(h);
        await SeedRelaunchReadyAsync(h, consecutiveFailures: 2);

        (await SweepAsync(h)).ShouldBe(1);
        await WaitForLaunchAsync(h);

        h.Adapter.Killed.ShouldBeTrue();
        var adapter = factory.Created.ShouldHaveSingleItem();
        adapter.Started.ShouldBeTrue();
        adapter.StartedArgs.ShouldContain("--resume");

        await using var db = CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        session.ComposedBundleStamp.ShouldNotBe("");
        session.ComposedBundleStamp.ShouldContain("board-api v");
        session.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);

        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyRefreshed);
        incident.Severity.ShouldBe(AlertSeverity.Info);
        incident.Message.ShouldContain("board-api added v");
        incident.Message.ShouldContain("AGENTS.md changed");
        incident.Message.ShouldNotContain("fresh: true");

        var state = await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == h.AgentId);
        state.ConsecutiveFailures.ShouldBe(2, "a successful policy relaunch must not grow the ladder");

        adapter.SubmittedBodies.ShouldContain(b => b.Contains("relaunch") && b.Contains("board-api"));
        adapter.SubmittedBodies.ShouldContain(b => b.Contains("AGENTS.md"));
        foreach (var bundle in InstructionBundles.All.Values)
            adapter.SubmittedBodies.ShouldAllBe(b => !b.Contains(bundle.Text));
    }

    [Test]
    public async Task Working_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await h.MarkWorkingAsync();

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
        await using var db = CreateContext();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyRefreshed)).ShouldBe(0);
    }

    [Test]
    public async Task Queued_Pending_row_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await h.SeedPendingMessageAsync("still owed");

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Channel_row_owed_a_reply_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await h.SeedChannelCorrelationAsync("hello from chat", "telegram:family");

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Cooldown_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);

        (await SweepAsync(h)).ShouldBe(1);
        await WaitForLaunchAsync(h);

        await File.WriteAllTextAsync(Path.Combine(h.TempRoot, "workspace", "AGENTS.md"), "floor v3\n");

        (await SweepAsync(h)).ShouldBe(0, "in-memory attempt stamp is the same-process cooldown");

        var fresh = new PolicyRefreshService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Runtime,
            h.Provider.GetRequiredService<ISessionRunnerClient>(),
            h.Provider.GetRequiredService<IOptions<SupervisionSettings>>(),
            TimeProvider.System,
            NullLogger<PolicyRefreshService>.Instance);
        (await fresh.SweepAsync(CancellationToken.None))
            .ShouldBe(0, "the PolicyRefreshed incident is the cross-restart cooldown");
    }

    [Test]
    public async Task Suspended_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await using (var db = CreateContext())
        {
            var state = await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == h.AgentId);
            state.Suspended = true;
            await db.SaveChangesAsync();
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Held_model_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await using (var db = CreateContext())
        {
            await db.ModelAvailabilityHolds
                .Where(x => x.Kind == AgentKind.ClaudeCode && x.ClearedAt == null)
                .ExecuteDeleteAsync();
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = AgentKind.ClaudeCode,
                ModelAlias = ModelAlias.KindWide,
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = DateTime.UtcNow.AddHours(1),
                HitAt = DateTime.UtcNow,
                Reason = "test hold",
                SourceSessionId = h.SessionId,
            });
            await db.SaveChangesAsync();
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Unbound_transcript_is_Notify_not_a_kill()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        h.Runtime.SetTestTranscriptBound(h.SessionId, false);

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
        await using var db = CreateContext();
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyRefreshed)).ShouldBe(0);
        var notified = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyDriftNotified);
        notified.Severity.ShouldBe(AlertSeverity.Info);
        h.Adapter.SubmittedBodies.ShouldContain(b => b.Contains("standing instructions changed"));
    }

    [Test]
    public async Task Codex_is_Notify_not_a_kill()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Kind, AgentKind.Codex));
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, AgentKind.Codex));
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
        await using var verify = CreateContext();
        (await verify.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyDriftNotified)).ShouldBe(1);
        (await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId))
            .PolicyNotifiedStamp.ShouldNotBeNull();
        h.Adapter.SubmittedBodies.ShouldContain(b =>
            b.Contains("standing instructions changed") && b.Contains("in your system prompt only at your next launch"));
        foreach (var bundle in InstructionBundles.All.Values)
            h.Adapter.SubmittedBodies.ShouldAllBe(b => !b.Contains(bundle.Text));
    }

    [Test]
    public async Task Herdr_null_stamp_does_nothing()
    {
        await using var h = await CreateHarnessAsync();
        await using (var db = CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.SessionBackend, SessionBackend.Herdr)
                .SetProperty(s => s.ComposedBundleStamp, (string?)null)
                .SetProperty(s => s.InstructionFileStamp, (string?)null));
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
        await using var verify = CreateContext();
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId)).ShouldBe(0);
    }

    [Test]
    public async Task StartAsync_throw_records_PolicyRefreshFailed_and_does_not_touch_the_ladder()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h, consecutiveFailures: 4);
        var missing = Path.Combine(h.TempRoot, "does-not-exist");
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.WorkingDirectory, missing));
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeTrue("the kill already happened; StartAsync is what threw");

        await using var verify = CreateContext();
        var failed = await verify.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyRefreshFailed);
        failed.Severity.ShouldBe(AlertSeverity.Warning);
        failed.Message.ShouldContain("start failed");
        (await verify.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyRefreshed)).ShouldBe(0);
        var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == h.AgentId);
        state.ConsecutiveFailures.ShouldBe(4);
        state.NextRestartAt.ShouldBeNull();
        var stopped = await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        stopped.Status.ShouldBe(SessionStatus.Stopped);
        stopped.TerminationSource.ShouldBe(SessionTerminationSource.PolicyRefresh);
    }

    [Test]
    public async Task A_server_restart_mid_relaunch_leaves_a_Starting_row_that_CARD_0340_resume_handles()
    {
        await using var h = await CreateHarnessAsync();
        var factory = Factory(h);
        await SeedRelaunchReadyAsync(h);
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.ConfigureNext.Enqueue(a => a.ReadyHold = hold);

        (await SweepAsync(h)).ShouldBe(1);

        await using (var db = CreateContext())
        {
            var starting = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            starting.Status.ShouldBe(SessionStatus.Starting);
        }

        hold.TrySetResult(true);
        await WaitForLaunchAsync(h);

        await h.Runtime.DisposeSessionAsync(h.SessionId);
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status = SessionStatus.Starting;
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "policy-refresh interrupted launch",
                Goal = "Do the thing.",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.Combine(h.TempRoot, "workspace"),
                AgentSessionId = h.SessionId,
                AgentId = h.AgentId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                DispatchedAt = DateTime.UtcNow.AddMinutes(-2),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using var scope = h.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(h.SessionId, h.AgentId, CancellationToken.None);

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            session.LaunchResumedAt.ShouldNotBeNull();
            (await verify.AgentIncidents.SingleAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.LaunchInterruptedByRestart))
                .Severity.ShouldBe(AlertSeverity.Warning);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.AgentTaskEvents.Where(e => e.AgentTaskId == taskId).ExecuteDeleteAsync();
            await cleanup.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Notify_dedupes_across_sweeps_and_a_new_service_instance()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PolicyRefreshMode, PolicyRefreshMode.Notify));
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
        var firstCount = h.Adapter.SubmittedBodies.Count(b => b.Contains("standing instructions changed"));
        firstCount.ShouldBe(1);

        await BackdateTranscriptAsync(h);

        (await SweepAsync(h)).ShouldBe(0, "the same drift must not re-notify in-process");
        h.Adapter.SubmittedBodies.Count(b => b.Contains("standing instructions changed")).ShouldBe(1);

        await using (var db = CreateContext())
        {
            await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyDriftNotified)
                .ExecuteDeleteAsync();
        }

        var fresh = new PolicyRefreshService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Runtime,
            h.Provider.GetRequiredService<ISessionRunnerClient>(),
            h.Provider.GetRequiredService<IOptions<SupervisionSettings>>(),
            TimeProvider.System,
            NullLogger<PolicyRefreshService>.Instance);
        (await fresh.SweepAsync(CancellationToken.None))
            .ShouldBe(0, "PolicyNotifiedStamp is the cross-restart notify dedupe");
        h.Adapter.SubmittedBodies.Count(b => b.Contains("standing instructions changed")).ShouldBe(1);

        await File.WriteAllTextAsync(Path.Combine(h.TempRoot, "workspace", "AGENTS.md"), "floor v4\n");
        await BackdateTranscriptAsync(h);
        (await fresh.SweepAsync(CancellationToken.None)).ShouldBe(0);
        h.Adapter.SubmittedBodies.Count(b => b.Contains("standing instructions changed"))
            .ShouldBe(2, "a new file stamp is a distinct drift");
    }

    [Test]
    public async Task RefreshPolicyAsync_force_skips_idle_minutes_and_relaunches()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);

        (await SweepAsync(h)).ShouldBe(0, "not idle long enough");
        h.Adapter.Killed.ShouldBeFalse();

        using var scope = h.Provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AgentControlService>()
            .RefreshPolicyAsync(h.AgentId, force: true, CancellationToken.None);

        result.Refreshed.ShouldBeTrue();
        result.Notified.ShouldBeFalse();
        result.Agent.Id.ShouldBe(h.AgentId);
        await WaitForLaunchAsync(h);
        h.Adapter.Killed.ShouldBeTrue();
        Factory(h).Created.ShouldHaveSingleItem().Started.ShouldBeTrue();
    }

    [Test]
    public async Task RefreshPolicyAsync_working_is_409_session_working_even_with_force()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await h.MarkWorkingAsync();

        using var scope = h.Provider.CreateScope();
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentControlService>()
                .RefreshPolicyAsync(h.AgentId, force: true, CancellationToken.None));

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe(PolicyRefreshService.SessionWorkingCode);
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task RefreshPolicyAsync_without_a_live_session_is_409_not_resumable()
    {
        await using var h = await CreateHarnessAsync();
        await h.Runtime.DisposeSessionAsync(h.SessionId);
        await using (var db = CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
        }

        using var scope = h.Provider.CreateScope();
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentControlService>()
                .RefreshPolicyAsync(h.AgentId, force: true, CancellationToken.None));

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe(PolicyRefreshService.NotResumableCode);
    }

    [Test]
    public async Task RefreshPolicyAsync_notify_lane_returns_notified()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PolicyRefreshMode, PolicyRefreshMode.Notify));
        }

        using var scope = h.Provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AgentControlService>()
            .RefreshPolicyAsync(h.AgentId, force: true, CancellationToken.None);

        result.Refreshed.ShouldBeFalse();
        result.Notified.ShouldBeTrue();
        h.Adapter.Killed.ShouldBeFalse();
        h.Adapter.SubmittedBodies.ShouldContain(b => b.Contains("standing instructions changed"));
        await using var verify = CreateContext();
        (await verify.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.PolicyDriftNotified))
            .Severity.ShouldBe(AlertSeverity.Info);
    }

    [Test]
    public async Task Off_mode_is_neither_lane()
    {
        await using var h = await CreateHarnessAsync();
        await SeedRelaunchReadyAsync(h);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PolicyRefreshMode, PolicyRefreshMode.Off));
        }

        (await SweepAsync(h)).ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();

        using var scope = h.Provider.CreateScope();
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            scope.ServiceProvider.GetRequiredService<AgentControlService>()
                .RefreshPolicyAsync(h.AgentId, force: true, CancellationToken.None));
        ex.Code.ShouldBe(PolicyRefreshService.NotResumableCode);
    }

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureServices = services =>
            {
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "fake",
                        Definitions =
                        {
                            ["fake"] = new AgentDefinition
                            {
                                Kind = "ClaudeCode",
                                Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                            },
                        },
                    }));
                services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
                    new RegisteringAdapterFactory(sp.GetRequiredService<AgentSessionRuntime>()));
            },
        });

    private static RegisteringAdapterFactory Factory(BridgeQueueHarness h) =>
        (RegisteringAdapterFactory)h.Provider.GetRequiredService<IAgentProtocolAdapterFactory>();

    private static async Task SeedRelaunchReadyAsync(BridgeQueueHarness h, int consecutiveFailures = 0)
    {
        var cwd = Path.Combine(h.TempRoot, "workspace");
        await File.WriteAllTextAsync(Path.Combine(cwd, "AGENTS.md"), "floor v2\n");
        h.Runtime.SetTestTranscriptBound(h.SessionId, true);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow.AddHours(-3));

        await using var db = CreateContext();
        var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        await AgentBundleAttachments.SetAsync(
            db, agent, [InstructionBundles.BoardApi], DateTime.UtcNow, CancellationToken.None);
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.ComposedBundleStamp, "")
            .SetProperty(s => s.InstructionFileStamp, "AGENTS.md v00000000"));
        db.AgentSupervisionStates.Add(new AgentSupervisionState
        {
            AgentId = h.AgentId,
            ConsecutiveFailures = consecutiveFailures,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static Task<int> SweepAsync(BridgeQueueHarness h) =>
        h.Provider.GetRequiredService<PolicyRefreshService>().SweepAsync(CancellationToken.None);

    private static async Task BackdateTranscriptAsync(BridgeQueueHarness h)
    {
        var aged = DateTime.UtcNow.AddHours(-3);
        await using var db = CreateContext();
        await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(t => t.Timestamp, aged)
                .SetProperty(t => t.CreatedAt, aged));
    }

    private static Task WaitForLaunchAsync(BridgeQueueHarness h) =>
        h.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

    private sealed class RegisteringAdapterFactory(AgentSessionRuntime runtime) : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Created { get; } = [];
        public Queue<Action<FakeAgentProtocolAdapter>> ConfigureNext { get; } = new();

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime };
            adapter.OnSubmitted = async submitted =>
            {
                if (adapter.StartedSessionId is not Guid sessionId)
                    return;
                await BridgeQueueHarness.InsertEntryAsync(
                    sessionId, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);
                await BridgeQueueHarness.InsertEntryAsync(
                    sessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
            if (ConfigureNext.Count > 0)
                ConfigureNext.Dequeue()(adapter);
            Created.Add(adapter);
            return adapter;
        }
    }
}
