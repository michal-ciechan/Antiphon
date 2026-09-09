using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Settings;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class HerdrAlwaysOnChannelParityTests
{
    [Test]
    public async Task Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-c388-named-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = HerdrAgentKinds.Grok };
            fake.Start();
            await fake.WaitUntilListeningAsync();
            var processProbe = new HoldProcessProbe();
            await using var h = BuildHarness(root, SessionBackend.Herdr, fake, [], AgentKind.Grok, 500, processProbe);
            var b = await CreateHoldSeat(h, root, named: false);
            await StartHoldSeat(h, b.Id);
            var bId = await WaitForPersistentSessionAsync(h, b.Id);
            var logs = Path.Combine(root, "session-logs");
            var bSidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(logs, bId))!;
            // An unpinned specialist's existing tab is operator-labelled; no election/uniqueness rule.
            var ws = fake.Workspaces.Single(w => w.WorkspaceId == bSidecar.WorkspaceId);
            ws.Label = "PredictionMarkets";
            var specialist = ws.Tabs.Single(t => t.TabId == bSidecar.TabId);
            specialist.Label = "MavRef-DL";
            var specialistCount = specialist.Panes.Count;
            var agent = await CreateHoldSeat(h, root, named: true);
            await StartHoldSeat(h, agent.Id);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var id = await WaitForPersistentSessionAsync(h, agent.Id);
                var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(logs, id))!;
                sidecar.TabLabel.ShouldBe("Orch");
                using var pump = h.Runner!.CreateEventPump();
                await pump.StartAsync(CancellationToken.None);
                if (sidecar.ChildPid is int pid) processProbe.Dead.Add(pid);
                fake.RemovePane(sidecar.PaneId);
                fake.AddReplayPaneClosed(sidecar.PaneId, sidecar.WorkspaceId);
                fake.EnqueueEvent(HerdrEventTypes.PaneClosedWire,
                    new { pane_id = sidecar.PaneId, workspace_id = sidecar.WorkspaceId });
                var exited = await WaitForRunnerStatus(h, id, "Exited");
                exited.ExitReason.ShouldBe(AgentExitReason.HerdrPaneClosed);
                await pump.StopAsync(CancellationToken.None);
                await h.Runtime.ObserveExitAsync(id, exited.ExitCode, exited.ExitReason, CancellationToken.None);
                await h.Supervisor().TickAsync(CancellationToken.None);
                (await HoldState(agent.Id)).HerdrConsecutiveFailures.ShouldBe(attempt + 1);
                h.Clock.Advance(TimeSpan.FromSeconds(20));
                if (attempt == 1) fake.LaunchScriptAgentKind = null;
                await h.Supervisor().TickAsync(CancellationToken.None);
                await DrainLaunch(h);
            }

            var timeoutId = await PersistedSessionId(agent.Id);
            await using (var db = CreateContext())
            {
                var row = await db.AgentSessions.SingleAsync(s => s.Id == timeoutId);
                row.Status.ShouldBe(SessionStatus.Failed);
                row.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.DetectTimeout);
            }
            var hintPath = HerdrLastPane.PathFor(logs, timeoutId);
            var hint = HerdrLastPane.TryLoad(hintPath)!;
            hint.ShouldNotBeNull();
            hint.TabLabel.ShouldBe("Orch");
            var bytes = await File.ReadAllBytesAsync(hintPath);
            var requests = fake.Requests.Count;
            var kills = h.Runner!.KillCalls;
            await h.Supervisor().TickAsync(CancellationToken.None);
            (await HoldState(agent.Id)).HerdrFailureHeldAt.ShouldNotBeNull();
            fake.AddReplayPaneClosed(bSidecar.PaneId, bSidecar.WorkspaceId);
            using (var pump = h.Runner.CreateEventPump())
            {
                await pump.StartAsync(CancellationToken.None);
                for (var i = 0; i < 3; i++)
                {
                    h.Clock.Advance(TimeSpan.FromHours(1));
                    await h.Supervisor().TickAsync(CancellationToken.None);
                }
                // Wait for the replay to actually reach the verifier, not just enqueue it.
                await WaitForCondition(() => fake.Requests.Skip(requests).Any(r =>
                    r.GetProperty("method").GetString() == "pane.get" && r.ToString().Contains(bSidecar.PaneId)));
                await pump.StopAsync(CancellationToken.None);
            }
            string[] mutations = ["tab.create", "pane.split", "pane.send_text", "pane.send_keys", "pane.close", "tab.close", "workspace.create", "agent.start"];
            fake.Requests.Skip(requests).Where(r => mutations.Contains(r.GetProperty("method").GetString())).ShouldBeEmpty();
            h.Runner.KillCalls.ShouldBe(kills, "the hold must issue no destructive runner RPC");
            (await File.ReadAllBytesAsync(hintPath)).ShouldBe(bytes);
            ws.Tabs.Count(t => t.Label == "Orch").ShouldBe(1);
            ws.Tabs.Single(t => t.Label == "Orch").Panes.Single().PaneId.ShouldBe(hint.PaneId);
            specialist.Panes.Count.ShouldBe(specialistCount);
            (await h.Runner.GetAsync(bId, CancellationToken.None)).Status.ShouldBe("Running");
            (await HoldState(b.Id)).HerdrConsecutiveFailures.ShouldBe(0);
            await using (var db = CreateContext())
                (await db.AgentSessions.SingleAsync(s => s.Id == bId)).HerdrSupervisionFailureKind.ShouldBeNull();

            fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
            requests = fake.Requests.Count;
            await using (var retryScope = h.Provider.CreateAsyncScope())
                await retryScope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agent.Id,
                    new StartAgentRequest(RemoteControl: false, ResetHerdrFailureHold: true), CancellationToken.None);
            await DrainLaunch(h);
            var repaired = await WaitForPersistentSessionAsync(h, agent.Id);
            (await h.Runner.GetAsync(repaired, CancellationToken.None)).Status.ShouldBe("Running");
            var repairedSidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(logs, repaired))!;
            repairedSidecar.PaneId.ShouldBe(hint.PaneId);
            repairedSidecar.TabLabel.ShouldBe("Orch");
            fake.Requests.Skip(requests).Count(r => r.GetProperty("method").GetString() == "tab.create").ShouldBe(0);
            (await HoldState(agent.Id)).HerdrFailureHeldAt.ShouldBeNull();
        }
        finally { await CleanupAsync(root); }
    }

    [Test]
    public async Task Unsafe_grok_rules_on_the_named_seat_refuse_before_any_pane_and_leave_the_streak_untouched()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-c388-rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using var fake = new FakeHerdrServer();
            fake.Start(); await fake.WaitUntilListeningAsync();
            await using var h = BuildHarness(root, SessionBackend.Herdr, fake, [], AgentKind.Grok, 500);
            var agent = await CreateHoldSeat(h, root, named: true);
            // SystemPromptAppend uses the rules-file transport now. Exercise an actually unsafe
            // raw CLI argument so this remains a pre-pane refusal test.
            h.Provider.GetRequiredService<IOptionsMonitor<AgentRegistrySettings>>().CurrentValue
                .Definitions["grok"].ArgsTemplate = ["--rules", "first line\nsecond line"];
            var requests = fake.Requests.Count;
            var ex = await Should.ThrowAsync<ConflictException>(() => h.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None));
            ex.Code.ShouldBe("grok_rules_argv_unsafe");
            fake.Requests.Skip(requests).ShouldBeEmpty();
            await using var db = CreateContext();
            (await db.AgentSessions.CountAsync(s => s.Cwd.StartsWith(root))).ShouldBe(0);
            var state = await HoldState(agent.Id);
            state.HerdrConsecutiveFailures.ShouldBe(0);
            state.HerdrFailureHeldAt.ShouldBeNull();
        }
        finally { await CleanupAsync(root); }
    }

    [Test]
    public async Task Named_placement_refusal_is_non_qualifying_not_a_detect_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-c388-placement-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using var fake = new FakeHerdrServer();
            var ws = fake.SeedWorkspace("c388-placement", "PredictionMarkets");
            var tab = fake.SeedTab(ws.WorkspaceId, "Orch", cwd: root);
            var pane = tab.Panes.Single();
            fake.SeedDetectedAgent(pane.PaneId, HerdrAgentKinds.Claude, "foreign");
            fake.Start(); await fake.WaitUntilListeningAsync();
            await using var h = BuildHarness(root, SessionBackend.Herdr, fake, [], AgentKind.Grok, 500);
            var agent = await CreateHoldSeat(h, root, named: true);
            await using (var db = CreateContext())
            {
                db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agent.Id, HerdrConsecutiveFailures = 2 });
                await db.SaveChangesAsync();
            }
            var ex = await Should.ThrowAsync<ConflictException>(() => h.Control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None));
            ex.Code.ShouldBe("pane_occupied");
            (await HoldState(agent.Id)).HerdrConsecutiveFailures.ShouldBe(2);
            await using (var db = CreateContext())
                (await db.AgentSessions.CountAsync(s => s.Cwd.StartsWith(root))).ShouldBe(0);
            fake.ClearDetectedAgent(pane.PaneId);
            fake.SetPaneProcessInfo(pane.PaneId, shellPid: 1);
            h.Runner!.BeforeStart = () => fake.SeedDetectedAgent(pane.PaneId, HerdrAgentKinds.Claude, "racing-foreign");
            await StartHoldSeat(h, agent.Id);
            var id = await PersistedSessionId(agent.Id);
            await using (var db = CreateContext())
            {
                var row = await db.AgentSessions.SingleAsync(s => s.Id == id);
                row.Status.ShouldBe(SessionStatus.Failed);
                row.HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.NonQualifying);
            }
            await h.Supervisor().TickAsync(CancellationToken.None);
            (await HoldState(agent.Id)).HerdrConsecutiveFailures.ShouldBe(0);
            (await HoldState(agent.Id)).HerdrFailureHeldAt.ShouldBeNull();
        }
        finally { await CleanupAsync(root); }
    }

    private static async Task<AgentDetailDto> CreateHoldSeat(Harness h, string root, bool named, bool unsafeRules = false)
    {
        var agent = await h.Agents.CreateAsync(new CreateAgentRequest(named ? "Orch" : "Specialist", root,
            SessionBackend: SessionBackend.Herdr, AlwaysOn: true, RemoteControlEnabled: false,
            HerdrWorkspaceLabel: named ? "PredictionMarkets" : null, HerdrTabLabel: named ? "Orch" : null,
            ReplyStyle: unsafeRules ? AgentReplyStyle.Terse : AgentReplyStyle.Normal,
            BundleKeys: unsafeRules ? [InstructionBundles.Orchestrator] : null,
            SystemPromptAppend: unsafeRules ? "first line\nsecond line" : null), CancellationToken.None);
        return await h.Agents.UpdateAsync(agent.Id, new UpdateAgentRequest(agent.Name, agent.WorkingDirectory,
            agent.Details, agent.DefaultWorkflowTemplateId, agent.AssignmentPolicy, BoardId: agent.BoardId,
            Kind: AgentKind.Grok, LaunchEnv: new Dictionary<string, string> { ["GROK_HOME"] = Path.Combine(root, "grok-home") }), CancellationToken.None);
    }

    private static async Task StartHoldSeat(Harness h, Guid id)
    {
        await h.Control.StartAsync(id, new StartAgentRequest(Fresh: true, RemoteControl: false), CancellationToken.None);
        await DrainLaunch(h);
        await using var db = CreateContext();
        var sessionId = await PersistedSessionId(id);
        var row = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        if (row.Status == SessionStatus.Running && row.AgentKind == AgentKind.Grok)
        {
            // FakeHerdr emulates detection, not the provider's filesystem. This hold fixture
            // needs existing native history so its independent Herdr failures remain the cause.
            Directory.CreateDirectory(Path.Combine(row.Cwd, "grok-home", "sessions", Uri.EscapeDataString(Path.GetFullPath(row.Cwd)), row.Id.ToString("D")));
        }
    }

    private static async Task DrainLaunch(Harness h)
    {
        try { await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None); }
        catch (ConflictException) { /* Assert persisted outcome, including expected asynchronous refusal. */ }
    }

    private sealed class HoldProcessProbe : IProcessLivenessProbe
    {
        public HashSet<int> Dead { get; } = [];
        public bool IsAlive(int pid, DateTime expectedStartTimeUtc) => !Dead.Contains(pid);
        public string? TryGetProcessName(int pid) => "powershell";
        public DateTime? TryGetStartTimeUtc(int pid) => DateTime.UtcNow.AddMinutes(-1);
    }

    private static async Task<Guid> PersistedSessionId(Guid agentId)
    {
        await using var db = CreateContext();
        return Guid.Parse((await db.Agents.SingleAsync(a => a.Id == agentId)).PersistentSessionId!);
    }

    private static async Task<AgentSupervisionState> HoldState(Guid id)
    {
        await using var db = CreateContext();
        return await db.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == id);
    }

    private static async Task<SessionRunnerSessionDto> WaitForRunnerStatus(Harness h, Guid id, string status)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        SessionRunnerSessionDto value;
        do
        {
            value = await h.Runner!.GetAsync(id, CancellationToken.None);
            if (value.Status == status) return value;
            await Task.Delay(25);
        } while (DateTime.UtcNow < until);
        value.Status.ShouldBe(status);
        return value;
    }

    private static async Task WaitForCondition(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!predicate() && DateTime.UtcNow < until) await Task.Delay(25);
        predicate().ShouldBeTrue("the replay must reach the live pane verifier");
    }
}
