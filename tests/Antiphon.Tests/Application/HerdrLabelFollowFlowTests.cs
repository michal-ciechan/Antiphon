using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrLabelFollowFlowTests
{
    [Test][Arguments(false, false)][Arguments(false, true)][Arguments(true, false)][Arguments(true, true)]
    public async Task Renamed_pin_is_used_by_the_next_named_launch(bool busy, bool workspace)
    {
        // workspace=false: tab-only pin in a managed (token-selected) workspace chosen by the production launch.
        // workspace=true: both pins in a unique untagged workspace.
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(workspace);
        var pane = f.Saved.PaneId; var tab = f.Saved.TabId; var ws = f.Saved.WorkspaceId; var wsSnapshot = f.Saved.WorkspaceLabel;
        f.Fake.SetPaneScreenText(pane, busy ? "Working..." : "Ready");
        f.Tab.Label = "Renamed"; f.Workspace.Label = "Renamed workspace";
        var start = f.Methods.Length;
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeTrue(); f.AssertReadOnly(start);
        var receipt = await f.Client.GetAsync(f.SessionId, CancellationToken.None);
        receipt.LabelObservation!.TabLabel.ShouldBe("Renamed"); f.Saved.TabLabel.ShouldBe("Renamed");
        receipt.LabelObservation.WorkspaceLabel.ShouldBe(workspace ? "Renamed workspace" : null);
        f.Saved.WorkspaceLabel.ShouldBe(workspace ? "Renamed workspace" : wsSnapshot);
        var saved = await f.ReadAsync(); saved.HerdrTabLabel.ShouldBe("Renamed"); saved.HerdrWorkspaceLabel.ShouldBe(workspace ? "Renamed workspace" : null);
        using (var scope = f.Harness.Provider.CreateScope())
        { var detail = await scope.ServiceProvider.GetRequiredService<AgentService>().GetByIdAsync(f.AgentId, CancellationToken.None); detail.HerdrTabLabel.ShouldBe("Renamed"); }
        var sessionId = f.SessionId;
        await RetireAsync(f);
        HerdrLastPane.TryLoad(f.Logs, sessionId)!.TabLabel.ShouldBe("Renamed");
        var furniture = Furniture(f);
        await f.LaunchAsync();
        f.Launches.Last().Herdr!.TabLabel.ShouldBe("Renamed");
        f.Saved.TabId.ShouldBe(tab); f.Saved.PaneId.ShouldBe(pane); f.Saved.WorkspaceId.ShouldBe(ws);
        Furniture(f).ShouldBe(furniture);
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Workspace_only_pin_follows_and_relaunches_without_a_tab_label(bool busy)
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(workspacePin: true, tabPin: false);
        f.Launches.Last().Herdr!.TabLabel.ShouldBeNull(); f.Saved.TabLabel.ShouldBeNull();
        var pane = f.Saved.PaneId; var ws = f.Saved.WorkspaceId;
        f.Fake.SetPaneScreenText(pane, busy ? "Working..." : "Ready");
        f.Workspace.Label = "Renamed workspace"; f.Tab.Label = "Renamed tab";
        var start = f.Methods.Length;
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeTrue(); f.AssertReadOnly(start);
        var saved = await f.ReadAsync(); saved.HerdrWorkspaceLabel.ShouldBe("Renamed workspace"); saved.HerdrTabLabel.ShouldBeNull();
        f.Saved.WorkspaceLabel.ShouldBe("Renamed workspace"); f.Saved.TabLabel.ShouldBeNull();
        var workspaces = f.Fake.Workspaces.Select(w => w.WorkspaceId).Order().ToArray();
        await RetireAsync(f); await f.LaunchAsync();
        f.Launches.Last().Herdr!.WorkspaceLabel.ShouldBe("Renamed workspace"); f.Launches.Last().Herdr!.TabLabel.ShouldBeNull();
        f.Saved.WorkspaceId.ShouldBe(ws); f.Saved.TabLabel.ShouldBeNull();
        f.Fake.Workspaces.Select(w => w.WorkspaceId).Order().ToArray().ShouldBe(workspaces, "the renamed workspace is reused, not recreated");
        (await f.ReadAsync()).HerdrTabLabel.ShouldBeNull();
    }

    [Test][Arguments(false, false)][Arguments(false, true)][Arguments(true, false)][Arguments(true, true)]
    public async Task Coherent_pane_move_to_another_tab_is_never_followed(bool rename, bool cached)
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync();
        var boundTab = f.Saved.TabId; var boundPane = f.Saved.PaneId; var ws = f.Saved.WorkspaceId;
        if (cached)
        {
            // A validated rename candidate is cached by the runner, but the server never received it.
            f.Tab.Label = "Renamed"; f.DropNextGet = true;
            await Should.ThrowAsync<HttpRequestException>(() => f.Service().FollowAsync(f.AgentId, CancellationToken.None));
            f.Saved.TabLabel.ShouldBe("Renamed"); f.Saved.LabelFollow!.Observation!.ResultCode.ShouldBe("validated");
        }
        var target = f.Fake.SeedTab(ws, rename ? "Moved" : "Old", paneCount: 0);
        f.Fake.MovePane(boundPane, target.TabId);
        (await f.Herdr.PaneGetAsync(boundPane, CancellationToken.None)).TabId.ShouldBe(target.TabId);
        (await f.Herdr.TabGetAsync(target.TabId, CancellationToken.None)).PaneCount.ShouldBe(1);
        (await f.Herdr.TabListAsync(ws, CancellationToken.None)).ShouldNotContain(t => t.TabId == boundTab);
        var before = Furniture(f); var getters = f.Methods.Count(m => m == "tab.get"); var start = f.Methods.Length;
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeFalse(); f.AssertReadOnly(start);
        if (cached) f.Methods.Count(m => m == "tab.get").ShouldBe(getters, "during cooldown the cached candidate is suppressed without label getters");
        (await f.Client.GetAsync(f.SessionId, CancellationToken.None)).LabelObservation.ShouldBeNull();
        Furniture(f).ShouldBe(before);
        var agent = await f.ReadAsync(); agent.HerdrTabLabel.ShouldBe("Old"); agent.HerdrWorkspaceLabel.ShouldBe("Old workspace");
        f.Bus.Count.ShouldBe(0);
        var destination = rename ? f.Fake.SeedTab(ws, "Old") : target;
        await RetireAsync(f); await f.LaunchAsync();
        f.Launches.Last().Herdr!.TabLabel.ShouldBe("Old");
        f.Saved.TabId.ShouldBe(destination.TabId); f.Saved.WorkspaceId.ShouldBe(ws); f.Saved.TabLabel.ShouldBe("Old");
    }

    [Test]
    public async Task Move_keeps_the_pinned_destination_and_never_creates_during_follow()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync();
        var boundTab = f.Saved.TabId; var boundPane = f.Saved.PaneId;
        var other = f.Fake.SeedWorkspace("other", "Other"); f.Fake.MoveTab(boundTab, other.WorkspaceId);
        var before = Furniture(f); var start = f.Methods.Length;
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeFalse(); f.AssertReadOnly(start);
        Furniture(f).ShouldBe(before); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
        var replacement = f.Fake.SeedTab(f.Workspace.WorkspaceId, "Old");
        await RetireAsync(f); await f.LaunchAsync();
        f.Saved.TabId.ShouldBe(replacement.TabId); f.Saved.PaneId.ShouldNotBe(boundPane);
    }

    [Test]
    public async Task Observation_never_mutates_herdr_furniture()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync();
        f.Tab.Label = "Renamed"; var before = Furniture(f); var start = f.Methods.Length;
        await f.Service().SweepAsync(CancellationToken.None); f.AssertReadOnly(start); Furniture(f).ShouldBe(before);
        (await f.Client.GetAsync(f.SessionId, CancellationToken.None)).Status.ShouldBe("Running");
    }

    [Test]
    public async Task Missed_observation_is_recovered_after_server_restart()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        var pane = f.Saved.PaneId; var tab = f.Saved.TabId;
        f.DropNextGet = true;
        await Should.ThrowAsync<HttpRequestException>(() => f.Service().FollowAsync(f.AgentId, CancellationToken.None));
        f.Saved.TabLabel.ShouldBe("Renamed"); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
        var due = f.Saved.LabelFollow!.NextDueAtUtc!.Value;
        // Both sides restart: the old server host, harness, runner runtime and runner HTTP host are discarded and joined.
        await f.RecreateAsync(runner: true);
        f.Saved.LabelFollow!.NextDueAtUtc.ShouldBe(due);
        var getters = f.Methods.Count(m => m == "tab.get");
        (await f.Service().SweepAsync(CancellationToken.None)).ShouldBe(0);
        f.Methods.Count(m => m == "tab.get").ShouldBe(getters, "cooldown survives the runner restart");
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old"); (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Old");
        f.Clock.Advance(due - f.Clock.GetUtcNow().UtcDateTime);
        (await f.Service().SweepAsync(CancellationToken.None)).ShouldBe(1);
        f.Methods.Count(m => m == "tab.get").ShouldBeGreaterThan(getters, "labels equal to the sidecar are freshly validated when due");
        f.Saved.TabLabel.ShouldBe("Renamed");
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Renamed"); f.Bus.Count.ShouldBe(1);
        (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Renamed");
        await RetireAsync(f); await f.LaunchAsync();
        f.Launches.Last().Herdr!.TabLabel.ShouldBe("Renamed");
        f.Saved.TabId.ShouldBe(tab); f.Saved.PaneId.ShouldBe(pane);
    }

    [Test]
    public async Task Db_failure_retries_the_same_current_snapshot()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        var pane = f.Saved.PaneId; var tab = f.Saved.TabId;
        var service = f.Service(); service.Boundary = (name, _) => name == "before-commit" ? Task.FromException(new IOException("injected commit failure")) : Task.CompletedTask;
        await Should.ThrowAsync<IOException>(() => service.FollowAsync(f.AgentId, CancellationToken.None));
        (await f.ReadAsync()).HerdrLabelFollowSequence.ShouldBe(0); f.Bus.Count.ShouldBe(0);
        var getters = f.Methods.Count(m => m == "tab.get");
        await f.RecreateAsync();
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeTrue();
        var agent = await f.ReadAsync(); agent.HerdrTabLabel.ShouldBe("Renamed"); agent.HerdrLabelFollowSequence.ShouldBe(f.Saved.LabelFollow!.Sequence);
        f.Bus.Count.ShouldBe(1); f.Methods.Count(m => m == "tab.get").ShouldBe(getters);
        (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Renamed");
        await RetireAsync(f); await f.LaunchAsync();
        f.Launches.Last().Herdr!.TabLabel.ShouldBe("Renamed");
        f.Saved.TabId.ShouldBe(tab); f.Saved.PaneId.ShouldBe(pane);
    }

    [Test]
    public async Task Committed_pin_is_visible_after_notification_failure()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        f.Bus.BeforeRecord = () => Task.FromException(new IOException("notification lost"));
        await Should.ThrowAsync<IOException>(() => f.Service().FollowAsync(f.AgentId, CancellationToken.None));
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Renamed");
        f.Bus.BeforeRecord = null;
        await f.RecreateAsync();
        (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Renamed");
        (await f.DetailLabelAsync("herdrWorkspaceLabel")).ShouldBe("Old workspace");
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeFalse(); f.Bus.Count.ShouldBe(0);
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Hosted_timer_delivers_follow_without_manual_calls(bool busy)
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync();
        f.Fake.SetPaneScreenText(f.Saved.PaneId, busy ? "Working..." : "Ready");
        // An earlier-ordered standing agent whose runner session is unknown: its GET fails on every sweep.
        var failingAgent = Guid.Parse("00000000-0000-0000-0000-000000000001"); var failingSession = Guid.NewGuid();
        await using (var db = f.Open())
        {
            var now = DateTime.UtcNow;
            db.Agents.Add(new Antiphon.Server.Domain.Entities.Agent { Id = failingAgent, Name = "Unreachable", Slug = "unreachable-" + failingSession.ToString("N"),
                SessionBackend = SessionBackend.Herdr, HerdrTabLabel = "Elsewhere", PersistentSessionId = failingSession.ToString(), CreatedAt = now, UpdatedAt = now });
            db.AgentSessions.Add(new Antiphon.Server.Domain.Entities.AgentSession { Id = failingSession, StandingAgentId = failingAgent,
                SessionBackend = SessionBackend.Herdr, Status = SessionStatus.Running, StartedAt = now, CreatedAt = now, LastSeenAt = now });
            await db.SaveChangesAsync();
        }
        await f.StartServerAsync(enabled: true);
        (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Old");
        // Startup sweep: the failing session first, then the due equal-label attempt commits only the watermark.
        await WaitUntilAsync(async () => (await f.ReadAsync()).HerdrLabelFollowSequence > 0);
        f.FollowGets.ShouldContain(failingSession);
        var first = await f.ReadAsync(); first.HerdrTabLabel.ShouldBe("Old"); var notifications = f.Bus.Count;
        f.Tab.Label = "Renamed"; var gets = f.FollowGets.Count(id => id == f.SessionId);
        f.Clock.Advance(TimeSpan.FromHours(1));
        await WaitUntilAsync(async () => (await f.ReadAsync()).HerdrTabLabel == "Renamed");
        f.FollowGets.Count(id => id == f.SessionId).ShouldBeGreaterThan(gets);
        f.FollowGets.Count(id => id == failingSession).ShouldBeGreaterThanOrEqualTo(2, "a failed session does not stop later ticks");
        (await f.ReadAsync()).HerdrLabelFollowSequence.ShouldBeGreaterThan(first.HerdrLabelFollowSequence);
        // AgentChanged is published one DB round trip after commit (D-9), so the Renamed label does not prove delivery yet.
        await WaitUntilAsync(() => Task.FromResult(f.Bus.Count >= notifications + 1));
        f.Bus.Count.ShouldBe(notifications + 1);
        (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Renamed");
        var host = f.Server!.FollowHost; host.ExecuteTask!.IsCompleted.ShouldBeFalse();
        await f.StopServerAsync().WaitAsync(TimeSpan.FromSeconds(10));
        host.ExecuteTask.IsCompleted.ShouldBeTrue("shutdown joins the hosted sweep");
        var after = f.FollowGets.Count; f.Clock.Advance(TimeSpan.FromHours(2)); await Task.Delay(300);
        f.FollowGets.Count.ShouldBe(after);
    }

    [Test]
    public async Task Hosted_timer_disabled_polls_nothing_across_ticks()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        var gets = f.FollowGets.Count;
        await f.StartServerAsync(enabled: false);
        (await f.DetailLabelAsync("herdrTabLabel")).ShouldBe("Old");
        var host = f.Server!.FollowHost;
        await host.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        for (var tick = 0; tick < 3; tick++) { f.Clock.Advance(TimeSpan.FromHours(1)); await Task.Delay(100); }
        f.FollowGets.Count.ShouldBe(gets);
        var agent = await f.ReadAsync(); agent.HerdrTabLabel.ShouldBe("Old"); agent.HerdrLabelFollowSequence.ShouldBe(0);
        await f.StopServerAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Label_failure_does_not_change_session_lifecycle()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Fake.FailMethod("tab.get", "unavailable");
        var before = Furniture(f); var start = f.Methods.Length;
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeFalse();
        (await f.Client.GetAsync(f.SessionId, CancellationToken.None)).Status.ShouldBe("Running");
        await using var db = f.Open(); (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old"); f.AssertReadOnly(start); Furniture(f).ShouldBe(before);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition()) await Task.Delay(25, limit.Token);
    }

    private static string[] Furniture(HerdrLabelFollowHttpFixture f) => f.Fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes.Select(p => $"{w.WorkspaceId}/{t.TabId}/{p.PaneId}"))).Order().ToArray();
    private static async Task RetireAsync(HerdrLabelFollowHttpFixture f)
    {
        var sidecar = f.Saved;
        f.Fake.ClearDetectedAgent(sidecar.PaneId);
        (await f.Runtime.GetAsync(f.SessionId, CancellationToken.None)).Status.ShouldBe("Exited");
        f.ResumeMonitor();
        await using var db = f.Open();
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped).SetProperty(s => s.EndedAt, DateTime.UtcNow));
        f.Harness.Provider.GetRequiredService<AgentSessionRuntime>().TryRemove(f.SessionId, out _);
    }
}
