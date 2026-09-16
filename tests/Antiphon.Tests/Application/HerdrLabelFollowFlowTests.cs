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
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrLabelFollowFlowTests
{
    [Test][Arguments(false, false)][Arguments(false, true)][Arguments(true, false)][Arguments(true, true)]
    public async Task Renamed_pin_is_used_by_the_next_named_launch(bool busy, bool workspace)
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(workspace);
        var pane = f.Saved.PaneId; var tab = f.Saved.TabId; var ws = f.Saved.WorkspaceId;
        f.Fake.SetPaneScreenText(pane, busy ? "Working..." : "Ready");
        f.Tab.Label = "Renamed"; if (workspace) f.Workspace.Label = "Renamed workspace";
        var start = f.Methods.Length;
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeTrue(); f.AssertReadOnly(start);
        var receipt = await f.Client.GetAsync(f.SessionId, CancellationToken.None);
        receipt.LabelObservation!.TabLabel.ShouldBe("Renamed"); f.Saved.TabLabel.ShouldBe("Renamed");
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
        f.DropNextGet = true;
        await Should.ThrowAsync<HttpRequestException>(() => f.Service().FollowAsync(f.AgentId, CancellationToken.None));
        f.Saved.TabLabel.ShouldBe("Renamed"); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
        var getters = f.Methods.Count(m => m == "tab.get");
        (await f.Service().SweepAsync(CancellationToken.None)).ShouldBe(1);
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Renamed"); f.Bus.Count.ShouldBe(1);
        f.Methods.Count(m => m == "tab.get").ShouldBe(getters);
    }

    [Test]
    public async Task Db_failure_retries_the_same_current_snapshot()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        var service = f.Service(); service.Boundary = (name, _) => name == "before-commit" ? Task.FromException(new IOException("injected commit failure")) : Task.CompletedTask;
        await Should.ThrowAsync<IOException>(() => service.FollowAsync(f.AgentId, CancellationToken.None));
        (await f.ReadAsync()).HerdrLabelFollowSequence.ShouldBe(0); f.Bus.Count.ShouldBe(0);
        var getters = f.Methods.Count(m => m == "tab.get");
        (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeTrue();
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Renamed"); f.Bus.Count.ShouldBe(1); f.Methods.Count(m => m == "tab.get").ShouldBe(getters);
    }

    [Test]
    public async Task Committed_pin_is_visible_after_notification_failure()
    {
        await using var f = new HerdrLabelFollowHttpFixture(); await f.StartAsync(); f.Tab.Label = "Renamed";
        f.Bus.BeforeRecord = () => Task.FromException(new IOException("notification lost"));
        await Should.ThrowAsync<IOException>(() => f.Service().FollowAsync(f.AgentId, CancellationToken.None));
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Renamed");
        f.Bus.BeforeRecord = null; (await f.Service().FollowAsync(f.AgentId, CancellationToken.None)).ShouldBeFalse(); f.Bus.Count.ShouldBe(0);
        using var scope = f.Harness.Provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AgentService>().GetByIdAsync(f.AgentId, CancellationToken.None)).HerdrTabLabel.ShouldBe("Renamed");
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

    private static string[] Furniture(HerdrLabelFollowHttpFixture f) => f.Fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes.Select(p => $"{w.WorkspaceId}/{t.TabId}/{p.PaneId}"))).Order().ToArray();
    private static async Task RetireAsync(HerdrLabelFollowHttpFixture f)
    {
        var sidecar = f.Saved;
        f.Fake.SetPaneProcessInfo(sidecar.PaneId, sidecar.ShellPid ?? 1); f.Fake.ClearDetectedAgent(sidecar.PaneId);
        (await f.Runtime.GetAsync(f.SessionId, CancellationToken.None)).Status.ShouldBe("Exited");
        f.ResumeMonitor();
        await using var db = f.Open();
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped).SetProperty(s => s.EndedAt, DateTime.UtcNow));
        f.Harness.Provider.GetRequiredService<AgentSessionRuntime>().TryRemove(f.SessionId, out _);
    }
}
