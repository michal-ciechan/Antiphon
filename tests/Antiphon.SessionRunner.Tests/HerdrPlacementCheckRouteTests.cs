using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public class HerdrPlacementCheckRouteTests
{
    [Test]
    public async Task Check_reports_create_when_tab_or_workspace_is_absent_and_mutates_nothing()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        await using var runtime = BuildRuntime(settings, fake);
        var from = fake.Requests.Count;
        var result = await runtime.CheckHerdrPlacementAsync(
            new HerdrPlacementCheckRequest(Guid.NewGuid(), NamedOpts(settings.SessionLogPath)),
            CancellationToken.None);
        result.Action.ShouldBe("create");
        var delta = fake.Requests.Skip(from).Select(r => r.GetProperty("method").GetString()!).ToList();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "workspace.list", "tab.list", "ping",
        };
        delta.ShouldAllBe(m => allowed.Contains(m));
        delta.ShouldNotContain("pane.list");
        delta.ShouldNotContain("workspace.create");
        delta.ShouldNotContain("workspace.report_metadata");
        Directory.Exists(HerdrPaneSidecar.DirectoryFor(settings.SessionLogPath)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Check_reports_relaunch_for_an_idle_named_shell()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var ws = fake.SeedWorkspace("w1", "PredictionMarkets", new Dictionary<string, string> { ["antiphon-ws"] = "k1" });
        var tab = fake.SeedTab(ws.WorkspaceId, "Orch");
        await using var runtime = BuildRuntime(settings, fake);
        var result = await runtime.CheckHerdrPlacementAsync(
            new HerdrPlacementCheckRequest(Guid.NewGuid(), NamedOpts(settings.SessionLogPath, "k1")),
            CancellationToken.None);
        result.Action.ShouldBe("relaunch");
        result.TabId.ShouldBe(tab.TabId);
        result.PaneId.ShouldBe(tab.Panes[0].PaneId);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Check_reports_adopt_for_own_live_process()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();
        var ws = fake.SeedWorkspace("w1", "PredictionMarkets", new Dictionary<string, string> { ["antiphon-ws"] = "k1" });
        var tab = fake.SeedTab(ws.WorkspaceId, "Orch");
        fake.SeedDetectedAgent(tab.Panes[0].PaneId, HerdrAgentKinds.Claude);
        fake.SetPaneProcessInfo(
            tab.Panes[0].PaneId,
            4242,
            [(99, "claude.exe", new[] { "claude", "--session-id", sessionId.ToString("D") }, settings.SessionLogPath)]);
        await using var runtime = BuildRuntime(settings, fake);
        var result = await runtime.CheckHerdrPlacementAsync(
            new HerdrPlacementCheckRequest(sessionId, NamedOpts(settings.SessionLogPath, "k1")),
            CancellationToken.None);
        result.Action.ShouldBe("adopt");
        result.PaneId.ShouldBe(tab.Panes[0].PaneId);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Check_refuses_occupied_ambiguous_and_invalid_with_the_launch_codes()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var ws = fake.SeedWorkspace("w1", "PredictionMarkets", new Dictionary<string, string> { ["antiphon-ws"] = "k1" });
        var tab = fake.SeedTab(ws.WorkspaceId, "Orch");
        fake.SetPaneProcessInfo(tab.Panes[0].PaneId, 4242, [(1, "grok.exe", new[] { "grok" }, "c:\\x")]);
        await using var runtime = BuildRuntime(settings, fake);
        var occupied = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.CheckHerdrPlacementAsync(
                new HerdrPlacementCheckRequest(Guid.NewGuid(), NamedOpts(settings.SessionLogPath, "k1")),
                CancellationToken.None));
        occupied.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);

        fake.SeedTab(ws.WorkspaceId, "Orch");
        var amb = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.CheckHerdrPlacementAsync(
                new HerdrPlacementCheckRequest(Guid.NewGuid(), NamedOpts(settings.SessionLogPath, "k1")),
                CancellationToken.None));
        amb.Code.ShouldBe(HerdrProblemTypes.TabAmbiguous);

        foreach (var extra in fake.Workspaces[0].Tabs.Where(t => t.Label == "Orch").ToList())
            fake.RemoveTab(extra.TabId);
        fake.SeedTab(ws.WorkspaceId, "Orch", paneCount: 2);
        var invalid = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.CheckHerdrPlacementAsync(
                new HerdrPlacementCheckRequest(Guid.NewGuid(), NamedOpts(settings.SessionLogPath, "k1")),
                CancellationToken.None));
        invalid.Code.ShouldBe(HerdrProblemTypes.TabInvalid);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public void Check_route_maps_refusals_to_409_problem_details()
    {
        var result = HerdrProblemMapper.MapLaunch(
            new HerdrLaunchException("pane x is occupied", HerdrProblemTypes.PaneOccupied));
        var problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(409);
        problem.ProblemDetails.Type.ShouldBe(HerdrProblemTypes.PaneOccupied);

        var amb = HerdrProblemMapper.MapLaunch(
            new HerdrLaunchException("ambiguous", HerdrProblemTypes.TabAmbiguous));
        amb.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(409);
        amb.ShouldBeOfType<ProblemHttpResult>().ProblemDetails.Type.ShouldBe(HerdrProblemTypes.TabAmbiguous);
    }

    private static HerdrLaunchOptions NamedOpts(string cwd, string key = "k1") =>
        new(key, "PredictionMarkets", cwd, "named-agent", AgentKind: HerdrAgentKinds.Claude, TabLabel: "Orch");

    private static SessionRunnerRuntime BuildRuntime(SessionRunnerSettings settings, FakeHerdrServer fake) =>
        new(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-c384-check-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static void DeleteLogRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { /* best-effort */ }
    }
}
