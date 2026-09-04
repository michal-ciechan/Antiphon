using Antiphon.Server.Application.Dtos;
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

    private static async Task<int> SweepAsync(BridgeQueueHarness h)
    {
        var service = new PolicyRefreshService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Runtime,
            h.Provider.GetRequiredService<ISessionRunnerClient>(),
            h.Provider.GetRequiredService<IOptions<SupervisionSettings>>(),
            TimeProvider.System,
            NullLogger<PolicyRefreshService>.Instance);
        return await service.SweepAsync(CancellationToken.None);
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
