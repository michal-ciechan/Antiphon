using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[NotInParallel("HerdrNamedTabPlacement")]
public class HerdrNamedTabPlacementTests
{
    private static readonly string[] MutationMethods =
    [
        "pane.close", "pane.send_text", "pane.send_keys", "pane.split", "tab.create",
        "tab.rename", "tab.close", "pane.rename", "pane.report_metadata", "agent.rename", "agent.start",
    ];

    [Test]
    public async Task Named_launch_creates_the_labelled_tab_once_with_agent_cwd_and_no_split()
    {
        await using var fx = await Fixture.CreateAsync();
        var env = new Dictionary<string, string> { ["K"] = "v" };
        var dto = await fx.StartNamedAsync(env: env);
        dto.Status.ShouldBe("Running");

        CountMethod(fx.Fake, "tab.create").ShouldBe(1);
        CountMethod(fx.Fake, "tab.rename").ShouldBe(0);
        CountMethod(fx.Fake, "pane.split").ShouldBe(0);
        var create = fx.Fake.Requests.Single(r => r.GetProperty("method").GetString() == "tab.create");
        var p = create.GetProperty("params");
        p.GetProperty("workspace_id").GetString().ShouldBe(fx.WorkspaceId);
        p.GetProperty("label").GetString().ShouldBe("Orch");
        p.GetProperty("cwd").GetString().ShouldBe(fx.Cwd);
        p.GetProperty("env").GetProperty("K").GetString().ShouldBe("v");

        var orch = fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "Orch");
        orch.Panes.Count.ShouldBe(1);
        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, fx.SessionId));
        sidecar.ShouldNotBeNull();
        sidecar!.TabId.ShouldBe(orch.TabId);
        sidecar.TabLabel.ShouldBe("Orch");
        sidecar.WorkspaceLabel.ShouldBe("PredictionMarkets");
        var title = fx.Fake.Requests
            .Where(r => r.GetProperty("method").GetString() == "pane.report_metadata")
            .Select(r => r.GetProperty("params").GetProperty("title").GetString())
            .Last();
        title.ShouldBe("named-agent");
        title.ShouldNotBe("Orch");
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_into_a_new_workspace_renames_the_root_tab_and_creates_no_second_tab()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        await runtime.StartAsync(Request(sessionId, settings.SessionLogPath), CancellationToken.None);

        CountMethod(fake, "workspace.create").ShouldBe(1);
        CountMethod(fake, "tab.rename").ShouldBe(1);
        CountMethod(fake, "tab.create").ShouldBe(0);
        fake.Workspaces.ShouldHaveSingleItem();
        fake.Workspaces[0].Tabs.ShouldHaveSingleItem();
        fake.Workspaces[0].Tabs[0].Label.ShouldBe("Orch");
        fake.Workspaces[0].Tabs[0].Panes.Count.ShouldBe(1);
        fake.LastLaunchScriptContent.ShouldNotBeNull();
        fake.LastLaunchScriptContent.ShouldContain("Set-Location -LiteralPath");
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Named_launch_reuses_an_operator_idle_labelled_tab_in_place_with_cwd()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true, orchCwd: @"D:\operator\elsewhere");
        var beforeTab = fx.OrchTabId!;
        var beforePane = fx.OrchPaneId!;
        var env = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" };
        await fx.StartNamedAsync(env: env);

        CountMethod(fx.Fake, "tab.create").ShouldBe(0);
        CountMethod(fx.Fake, "pane.split").ShouldBe(0);
        CountMethod(fx.Fake, "tab.rename").ShouldBe(0);
        var sidecar = LoadSidecar(fx);
        sidecar.TabId.ShouldBe(beforeTab);
        sidecar.PaneId.ShouldBe(beforePane);
        sidecar.Origin.ShouldBe(HerdrPaneOrigins.Launched);
        fx.Fake.LastLaunchScriptContent.ShouldNotBeNull();
        fx.Fake.LastLaunchScriptContent!.ShouldContain($"Set-Location -LiteralPath '{fx.Cwd}'");
        fx.Fake.LastLaunchScriptContent.ShouldContain("Set-Item -LiteralPath 'Env:A'");
        fx.Fake.LastLaunchScriptContent.ShouldContain("Set-Item -LiteralPath 'Env:B'");
        fx.Fake.Workspaces[0].Tabs.Single(t => t.TabId == beforeTab).Label.ShouldBe("Orch");
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_types_into_an_idle_shell_despite_attached_origin_history()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        WriteLastPane(fx, fx.OrchPaneId!, fx.OrchTabId!, HerdrPaneOrigins.Attached);
        await fx.StartNamedAsync();
        CountMethod(fx.Fake, "tab.create").ShouldBe(0);
        LoadSidecar(fx).Origin.ShouldBe(HerdrPaneOrigins.Launched);
        LoadSidecar(fx).PaneId.ShouldBe(fx.OrchPaneId);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_pin_outranks_a_valid_last_pane_and_an_allocator_slot()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true, seedSpecialist: true);
        var specialistPane = fx.SpecialistPaneId!;
        WriteLastPane(fx, specialistPane, fx.SpecialistTabId!, HerdrPaneOrigins.Launched);
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);

        var unpinnedId = Guid.NewGuid();
        await fx.Runtime.StartAsync(
            Request(unpinnedId, fx.Cwd, tabLabel: null, workspaceKey: fx.WorkspaceKey, paneTitle: "spec"),
            CancellationToken.None);

        var beforeBytes = File.ReadAllBytes(HerdrLastPane.PathFor(fx.Settings.SessionLogPath, fx.SessionId));
        await fx.StartNamedAsync();

        var orch = fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "Orch");
        LoadSidecar(fx).PaneId.ShouldBe(orch.Panes[0].PaneId);
        fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "MavRef-DL").Panes.Count.ShouldBe(1);
        CountMethod(fx.Fake, "pane.split").ShouldBe(0);
        File.Exists(HerdrLastPane.PathFor(fx.Settings.SessionLogPath, fx.SessionId)).ShouldBeFalse();
        beforeBytes.ShouldNotBeEmpty();
        await fx.Runtime.KillAsync(unpinnedId, TimeSpan.FromSeconds(2), CancellationToken.None);
        await fx.CleanupAsync();
        _ = snapshot;
    }

    [Test]
    public async Task Named_same_id_restart_relaunches_into_the_same_labelled_pane()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        var env1 = new Dictionary<string, string> { ["KEEP"] = "1", ["STALE"] = "x" };
        await fx.StartNamedAsync(env: env1);
        var paneId = LoadSidecar(fx).PaneId;
        fx.Runtime.SweepVanishedSessions(new DeadProcessProbe());
        fx.Fake.ClearDetectedAgent(paneId);

        var env2 = new Dictionary<string, string> { ["KEEP"] = "1" };
        var beforeCreate = CountMethod(fx.Fake, "tab.create");
        await fx.StartNamedAsync(env: env2);
        CountMethod(fx.Fake, "tab.create").ShouldBe(beforeCreate);
        LoadSidecar(fx).PaneId.ShouldBe(paneId);
        fx.Fake.LastLaunchScriptContent.ShouldNotBeNull();
        fx.Fake.LastLaunchScriptContent!.ShouldContain("Remove-Item -LiteralPath 'Env:STALE'");
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_fresh_id_lands_on_the_labelled_tab_when_the_previous_pane_was_it()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        await fx.StartNamedAsync();
        var paneId = LoadSidecar(fx).PaneId;
        fx.Runtime.SweepVanishedSessions(new DeadProcessProbe());
        fx.Fake.ClearDetectedAgent(paneId);
        var fresh = Guid.NewGuid();
        await fx.Runtime.StartAsync(
            Request(fresh, fx.Cwd, reuse: fx.SessionId, workspaceKey: fx.WorkspaceKey),
            CancellationToken.None);
        CountMethod(fx.Fake, "tab.create").ShouldBe(0);
        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, fresh));
        sidecar!.PaneId.ShouldBe(paneId);
        File.Exists(HerdrLastPane.PathFor(fx.Settings.SessionLogPath, fx.SessionId)).ShouldBeFalse();
        await fx.Runtime.KillAsync(fresh, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(fx.Settings.SessionLogPath);
    }

    [Test]
    public async Task Named_fresh_id_ignores_a_previous_pane_elsewhere()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true, seedSpecialist: true);
        WriteLastPane(fx, fx.SpecialistPaneId!, fx.SpecialistTabId!, HerdrPaneOrigins.Launched, sessionId: fx.SessionId);
        var fresh = Guid.NewGuid();
        await fx.Runtime.StartAsync(
            Request(fresh, fx.Cwd, reuse: fx.SessionId, workspaceKey: fx.WorkspaceKey),
            CancellationToken.None);
        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, fresh))!;
        sidecar.TabLabel.ShouldBe("Orch");
        fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "MavRef-DL").Panes.Count.ShouldBe(1);
        await fx.Runtime.KillAsync(fresh, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(fx.Settings.SessionLogPath);
    }

    [Test]
    public async Task Named_launch_recreates_a_deleted_labelled_tab()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        await fx.StartNamedAsync();
        var paneId = LoadSidecar(fx).PaneId;
        fx.Runtime.SweepVanishedSessions(new DeadProcessProbe());
        fx.Fake.RemoveTab(fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "Orch").TabId);
        await fx.StartNamedAsync();
        CountMethod(fx.Fake, "tab.create").ShouldBe(1);
        CountMethod(fx.Fake, "pane.split").ShouldBe(0);
        LoadSidecar(fx).PaneId.ShouldNotBe(paneId);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_adopts_its_own_live_process_and_types_nothing()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        var paneId = fx.OrchPaneId!;
        fx.Fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Claude);
        fx.Fake.SetPaneProcessInfo(
            paneId,
            4242,
            [(Pid: 99, Name: "claude.exe", Argv: new[] { "claude", "--session-id", fx.SessionId.ToString("D") }, Cwd: fx.Cwd)]);
        var beforeSend = CountMethod(fx.Fake, "pane.send_text");
        var dto = await fx.StartNamedAsync();
        dto.Status.ShouldBe("Running");
        CountMethod(fx.Fake, "pane.send_text").ShouldBe(beforeSend);
        LoadSidecar(fx).ChildPid.ShouldBe(99);
        LoadSidecar(fx).TabLabel.ShouldBe("Orch");
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_refuses_every_foreign_occupant_shape_with_zero_side_effects()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        var paneId = fx.OrchPaneId!;
        var cases = new (string Name, Action Seed)[]
        {
            ("wrong kind", () =>
            {
                fx.Fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Grok);
                fx.Fake.SetPaneProcessInfo(paneId, 4242,
                    [(1, "grok.exe", new[] { "grok", "--session-id", fx.SessionId.ToString("D") }, fx.Cwd)]);
            }),
            ("right kind different id", () =>
            {
                fx.Fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Claude);
                fx.Fake.SetPaneProcessInfo(paneId, 4242,
                    [(1, "claude.exe", new[] { "claude", "--session-id", Guid.NewGuid().ToString("D") }, fx.Cwd)]);
            }),
            ("right kind no argv id", () =>
            {
                fx.Fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Claude);
                fx.Fake.SetPaneProcessInfo(paneId, 4242, [(1, "claude.exe", new[] { "claude" }, fx.Cwd)]);
            }),
            ("two foreground", () =>
            {
                fx.Fake.SetPaneProcessInfo(paneId, 4242,
                    [(1, "claude.exe", new[] { "claude" }, fx.Cwd), (2, "other.exe", new[] { "other" }, fx.Cwd)]);
            }),
            ("occupied Codex", () =>
            {
                fx.Fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Codex);
                fx.Fake.SetPaneProcessInfo(paneId, 4242, [(1, "codex.exe", new[] { "codex" }, fx.Cwd)]);
            }),
            ("non-PowerShell shell", () =>
            {
                fx.Fake.SetPaneProcessInfo(paneId, 7, Array.Empty<(int, string, string[], string?)>());
            }),
            ("missing shell pid", () =>
            {
                fx.Fake.SetPaneProcessInfo(paneId, null, Array.Empty<(int, string, string[], string?)>());
            }),
        };

        foreach (var (name, seed) in cases)
        {
            fx.Fake.ClearDetectedAgent(paneId);
            seed();
            if (name == "non-PowerShell shell")
            {
                await using var runtime = BuildRuntime(fx.Settings, fx.Fake, new NamedProcessProbe("cmd"));
                await AssertOccupied(runtime, fx, paneId, name);
            }
            else
            {
                await AssertOccupied(fx.Runtime, fx, paneId, name);
            }
        }

        fx.Fake.ClearDetectedAgent(paneId);
        fx.Fake.SetPaneProcessInfo(paneId, 4242, Array.Empty<(int, string, string[], string?)>());
        await fx.StartNamedAsync();
        LoadSidecar(fx).PaneId.ShouldBe(paneId);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_succeeds_on_the_same_tab_once_the_occupant_is_gone()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        fx.Fake.SeedDetectedAgent(fx.OrchPaneId!, HerdrAgentKinds.Grok);
        fx.Fake.SetPaneProcessInfo(fx.OrchPaneId!, 4242, [(1, "grok.exe", new[] { "grok" }, fx.Cwd)]);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() => fx.StartNamedAsync());
        ex.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);
        fx.Fake.ClearDetectedAgent(fx.OrchPaneId!);
        fx.Fake.SetPaneProcessInfo(fx.OrchPaneId!, 4242, Array.Empty<(int, string, string[], string?)>());
        await fx.StartNamedAsync();
        LoadSidecar(fx).PaneId.ShouldBe(fx.OrchPaneId);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_refuses_duplicate_labels_listing_both_tabs()
    {
        await using var fx = await Fixture.CreateAsync();
        fx.Fake.SeedTab(fx.WorkspaceId, "Orch");
        fx.Fake.SeedTab(fx.WorkspaceId, "Orch");
        var from = fx.Fake.Requests.Count;
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() => fx.StartNamedAsync());
        ex.Code.ShouldBe(HerdrProblemTypes.TabAmbiguous);
        ex.Message.ShouldContain("Orch");
        AssertZeroMutation(fx.Fake, from, snapshot, fx.Settings.SessionLogPath);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_refuses_a_multi_pane_or_miscounted_labelled_tab_without_mutation()
    {
        await using var fx = await Fixture.CreateAsync();
        var tab = fx.Fake.SeedTab(fx.WorkspaceId, "Orch", paneCount: 2);
        var from = fx.Fake.Requests.Count;
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() => fx.StartNamedAsync());
        ex.Code.ShouldBe(HerdrProblemTypes.TabInvalid);
        AssertZeroMutation(fx.Fake, from, snapshot, fx.Settings.SessionLogPath);

        fx.Fake.RemoveTab(tab.TabId);
        var one = fx.Fake.SeedTab(fx.WorkspaceId, "Orch", paneCount: 1);
        fx.Fake.SetTabReportedPaneCount(one.TabId, 2);
        from = fx.Fake.Requests.Count;
        snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var ex2 = await Should.ThrowAsync<HerdrLaunchException>(() => fx.StartNamedAsync());
        ex2.Code.ShouldBe(HerdrProblemTypes.TabInvalid);
        AssertZeroMutation(fx.Fake, from, snapshot, fx.Settings.SessionLogPath);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Tab_list_failure_fails_the_named_launch_and_never_allocates()
    {
        await using var fx = await Fixture.CreateAsync();
        fx.Fake.FailMethod("tab.list", "unavailable");
        var ex = await Should.ThrowAsync<HerdrApiException>(() => fx.StartNamedAsync());
        ex.Code.ShouldBe("unavailable");
        CountMethod(fx.Fake, "tab.create").ShouldBe(0);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_refuses_a_pane_claimed_by_another_live_or_pending_session()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        var holder = Guid.NewGuid();
        await fx.Runtime.StartAsync(
            Request(holder, fx.Cwd, workspaceKey: fx.WorkspaceKey),
            CancellationToken.None);
        var from = fx.Fake.Requests.Count;
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() => fx.StartNamedAsync());
        ex.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);
        ex.Message.ShouldContain(holder.ToString("D"));
        AssertZeroMutation(fx.Fake, from, snapshot, fx.Settings.SessionLogPath);

        await fx.Runtime.KillAsync(holder, TimeSpan.FromSeconds(2), CancellationToken.None);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_target_that_vanishes_before_acquisition_is_pane_changed_not_reallocated()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        var gate = fx.Fake.GateMethod("pane.process_info");
        var start = fx.StartNamedAsync();
        await WaitUntil(() => CountMethod(fx.Fake, "pane.process_info") >= 1);
        fx.Fake.MoveTab(fx.OrchTabId!, fx.WorkspaceId);
        var other = fx.Fake.SeedWorkspace("w-other", "other");
        fx.Fake.MoveTab(fx.OrchTabId!, other.WorkspaceId);
        var from = fx.Fake.Requests.Count;
        gate.Release();
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() => start);
        ex.Code.ShouldBe(HerdrProblemTypes.PaneChanged);
        CountMethod(fx.Fake, "tab.create").ShouldBe(0);
        CountMethod(fx.Fake, "pane.split").ShouldBe(0);
        await fx.CleanupAsync();
        _ = from;
    }

    [Test]
    public async Task Two_concurrent_named_launches_yield_one_winner_one_refusal_and_one_tab()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        var gate = fx.Fake.GateMethod("pane.send_text");
        var a = fx.StartNamedAsync();
        await WaitUntil(() => CountMethod(fx.Fake, "pane.send_text") >= 1);
        var bId = Guid.NewGuid();
        var b = fx.Runtime.StartAsync(Request(bId, fx.Cwd, workspaceKey: fx.WorkspaceKey), CancellationToken.None);
        var bEx = await Should.ThrowAsync<HerdrLaunchException>(() => b);
        bEx.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);
        a.IsCompleted.ShouldBeFalse();
        gate.Release();
        var aDto = await a;
        aDto.Status.ShouldBe("Running");
        fx.Fake.Workspaces[0].Tabs.Count(t => t.Label == "Orch").ShouldBe(1);
        CountMethod(fx.Fake, "pane.send_text").ShouldBe(1);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Two_concurrent_named_launches_into_an_absent_tab_create_it_once()
    {
        await using var fx = await Fixture.CreateAsync();
        var gate = fx.Fake.GateMethod("pane.send_text");
        var a = fx.StartNamedAsync();
        await WaitUntil(() => CountMethod(fx.Fake, "pane.send_text") >= 1);
        var bId = Guid.NewGuid();
        var b = fx.Runtime.StartAsync(Request(bId, fx.Cwd, workspaceKey: fx.WorkspaceKey), CancellationToken.None);
        var bEx = await Should.ThrowAsync<HerdrLaunchException>(() => b);
        bEx.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);
        a.IsCompleted.ShouldBeFalse();
        gate.Release();
        await a;
        CountMethod(fx.Fake, "tab.create").ShouldBe(1);
        fx.Fake.Workspaces[0].Tabs.Count(t => t.Label == "Orch").ShouldBe(1);
        CountMethod(fx.Fake, "pane.send_text").ShouldBe(1);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Cancelled_named_launch_releases_its_claim_and_keeps_names_and_hints()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        WriteLastPane(fx, fx.OrchPaneId!, fx.OrchTabId!, HerdrPaneOrigins.Launched);
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var gate = fx.Fake.GateMethod("pane.send_text");
        using var cts = new CancellationTokenSource();
        var a = fx.Runtime.StartAsync(Request(fx.SessionId, fx.Cwd, workspaceKey: fx.WorkspaceKey), cts.Token);
        await WaitUntil(() => CountMethod(fx.Fake, "pane.send_text") >= 1);
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => a);
        gate.Drop();
        File.Exists(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, fx.SessionId)).ShouldBeFalse();
        AssertLastPaneUnchanged(snapshot, fx.Settings.SessionLogPath);
        await fx.Runtime.StartAsync(Request(fx.SessionId, fx.Cwd, workspaceKey: fx.WorkspaceKey), CancellationToken.None);
        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, fx.SessionId));
        sidecar!.PaneId.ShouldBe(fx.OrchPaneId);
        await fx.Runtime.KillAsync(fx.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(fx.Settings.SessionLogPath);
    }

    [Test]
    public async Task Named_detect_timeout_keeps_the_pane_and_the_next_start_relaunches_there()
    {
        await using var fake = new FakeHerdrServer { LaunchScriptAgentKind = null };
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var ws = fake.SeedWorkspace("w1", "PredictionMarkets", new Dictionary<string, string> { ["antiphon-ws"] = "k1" });
        var orch = fake.SeedTab(ws.WorkspaceId, "Orch");
        await using var runtime = BuildRuntime(settings, fake, launchDetectTimeoutMs: 200);
        var sessionId = Guid.NewGuid();
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.StartAsync(Request(sessionId, settings.SessionLogPath, workspaceKey: "k1"), CancellationToken.None));
        ex.Code.ShouldBe(HerdrLaunchException.CodeDetectTimeout);
        CountMethod(fake, "pane.close").ShouldBe(0);
        var last = HerdrLastPane.TryLoad(settings.SessionLogPath, sessionId);
        last.ShouldNotBeNull();
        last!.TabLabel.ShouldBe("Orch");
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Claude;
        await runtime.StartAsync(Request(sessionId, settings.SessionLogPath, workspaceKey: "k1"), CancellationToken.None);
        CountMethod(fake, "tab.create").ShouldBe(0);
        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId));
        sidecar!.PaneId.ShouldBe(orch.Panes[0].PaneId);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Unpinned_launches_never_split_a_live_dedicated_named_tab()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        await fx.StartNamedAsync();
        var orchCount = fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "Orch").Panes.Count;
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        await fx.Runtime.StartAsync(Request(b, fx.Cwd, tabLabel: null, workspaceKey: fx.WorkspaceKey, paneTitle: "B"), CancellationToken.None);
        await fx.Runtime.StartAsync(Request(c, fx.Cwd, tabLabel: null, workspaceKey: fx.WorkspaceKey, paneTitle: "C"), CancellationToken.None);
        await fx.Runtime.StartAsync(Request(d, fx.Cwd, tabLabel: null, workspaceKey: fx.WorkspaceKey, paneTitle: "D"), CancellationToken.None);
        fx.Fake.Workspaces[0].Tabs.Single(t => t.Label == "Orch").Panes.Count.ShouldBe(orchCount);
        CountMethod(fx.Fake, "tab.create").ShouldBeGreaterThanOrEqualTo(1);
        await fx.Runtime.KillAsync(b, TimeSpan.FromSeconds(2), CancellationToken.None);
        await fx.Runtime.KillAsync(c, TimeSpan.FromSeconds(2), CancellationToken.None);
        await fx.Runtime.KillAsync(d, TimeSpan.FromSeconds(2), CancellationToken.None);
        await fx.CleanupAsync();
    }

    [Test]
    public async Task Named_launch_refuses_live_previous_id_on_a_fresh_row()
    {
        await using var fx = await Fixture.CreateAsync(seedOrch: true);
        await fx.StartNamedAsync();
        var fresh = Guid.NewGuid();
        var from = fx.Fake.Requests.Count;
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            fx.Runtime.StartAsync(
                Request(fresh, fx.Cwd, reuse: fx.SessionId, workspaceKey: fx.WorkspaceKey),
                CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneOccupied);
        AssertZeroMutation(fx.Fake, from, snapshot, fx.Settings.SessionLogPath);
        await fx.CleanupAsync();
    }

    private static async Task AssertOccupied(
        SessionRunnerRuntime runtime, Fixture fx, string paneId, string name)
    {
        var from = fx.Fake.Requests.Count;
        var sessionId = Guid.NewGuid();
        WriteLastPane(fx, paneId, fx.OrchTabId!, HerdrPaneOrigins.Launched, sessionId);
        var snapshot = SnapshotLastPane(fx.Settings.SessionLogPath);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(
            () => runtime.StartAsync(Request(sessionId, fx.Cwd, workspaceKey: fx.WorkspaceKey), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneOccupied, name);
        ex.Message.ShouldContain(paneId);
        AssertZeroMutation(fx.Fake, from, snapshot, fx.Settings.SessionLogPath);
        File.Exists(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, sessionId)).ShouldBeFalse();
        HerdrLastPane.TryDelete(fx.Settings.SessionLogPath, sessionId);
    }

    private static RunnerLaunchRequest Request(
        Guid sessionId,
        string cwd,
        string? tabLabel = "Orch",
        string? workspaceKey = "k1",
        string? paneTitle = "named-agent",
        Guid? reuse = null,
        IReadOnlyDictionary<string, string>? env = null) =>
        new(
            sessionId,
            @"C:\tools\claude.exe",
            ["--dangerously-skip-permissions", "--session-id", sessionId.ToString("D")],
            env ?? new Dictionary<string, string>(),
            cwd,
            Cols: 120,
            Rows: 30,
            Backend: SessionBackends.Herdr,
            Herdr: new HerdrLaunchOptions(
                WorkspaceKey: workspaceKey ?? "k1",
                WorkspaceLabel: "PredictionMarkets",
                WorkspaceCwd: cwd,
                PaneTitle: paneTitle ?? "named-agent",
                AgentKind: HerdrAgentKinds.Claude,
                ReusePaneOfSessionId: reuse,
                TabLabel: tabLabel));

    private static SessionRunnerRuntime BuildRuntime(
        SessionRunnerSettings settings,
        FakeHerdrServer fake,
        IProcessLivenessProbe? probe = null,
        int launchDetectTimeoutMs = 5_000) =>
        new(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings
            {
                Enabled = true,
                Session = fake.Session,
                LaunchDetectTimeoutMs = launchDetectTimeoutMs,
            }),
            probe ?? new PowershellProcessProbe());

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-c384-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static int CountMethod(FakeHerdrServer fake, string method) =>
        fake.Requests.Count(r => r.GetProperty("method").GetString() == method);

    private static HerdrPaneSidecar LoadSidecar(Fixture fx) =>
        HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(fx.Settings.SessionLogPath, fx.SessionId))
        ?? throw new InvalidOperationException("sidecar missing");

    private static void WriteLastPane(
        Fixture fx, string paneId, string tabId, string origin, Guid? sessionId = null)
    {
        new HerdrLastPane
        {
            SessionId = sessionId ?? fx.SessionId,
            WorkspaceKey = fx.WorkspaceKey,
            WorkspaceId = fx.WorkspaceId,
            TabId = tabId,
            PaneId = paneId,
            Origin = origin,
            ExitReason = "test",
            ExitedAtUtc = DateTime.UtcNow,
            LaunchEnvNames = ["OLD"],
        }.SaveAtomic(HerdrLastPane.PathFor(fx.Settings.SessionLogPath, sessionId ?? fx.SessionId));
    }

    private static Dictionary<string, byte[]> SnapshotLastPane(string logRoot)
    {
        var dir = HerdrLastPane.DirectoryFor(logRoot);
        if (!Directory.Exists(dir))
            return [];
        return Directory.GetFiles(dir, "*.json").ToDictionary(f => f, File.ReadAllBytes);
    }

    private static void AssertLastPaneUnchanged(Dictionary<string, byte[]> snapshot, string logRoot)
    {
        var now = SnapshotLastPane(logRoot);
        now.Keys.Order().ShouldBe(snapshot.Keys.Order());
        foreach (var (path, bytes) in snapshot)
            File.ReadAllBytes(path).ShouldBe(bytes);
    }

    private static void AssertZeroMutation(
        FakeHerdrServer fake, int fromIndex, Dictionary<string, byte[]> lastPane, string logRoot)
    {
        var delta = fake.Requests.Skip(fromIndex)
            .Select(r => r.GetProperty("method").GetString()!)
            .ToList();
        foreach (var method in MutationMethods)
            delta.ShouldNotContain(method);
        AssertLastPaneUnchanged(lastPane, logRoot);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(10))
                throw new TimeoutException("condition not met");
            await Task.Delay(20);
        }
    }

    private static void DeleteLogRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required FakeHerdrServer Fake { get; init; }
        public required SessionRunnerRuntime Runtime { get; init; }
        public required SessionRunnerSettings Settings { get; init; }
        public Guid SessionId { get; } = Guid.NewGuid();
        public string WorkspaceKey { get; } = "k1";
        public string WorkspaceId { get; init; } = "w1";
        public string Cwd => Settings.SessionLogPath;
        public string? OrchTabId { get; init; }
        public string? OrchPaneId { get; init; }
        public string? SpecialistTabId { get; init; }
        public string? SpecialistPaneId { get; init; }

        public static async Task<Fixture> CreateAsync(
            bool seedOrch = false, bool seedSpecialist = false, string? orchCwd = null)
        {
            var fake = new FakeHerdrServer();
            fake.Start();
            await fake.WaitUntilListeningAsync();
            var settings = BuildSettings();
            var ws = fake.SeedWorkspace("w1", "PredictionMarkets", new Dictionary<string, string> { ["antiphon-ws"] = "k1" });
            string? orchTab = null, orchPane = null, specTab = null, specPane = null;
            if (seedOrch)
            {
                var tab = fake.SeedTab(ws.WorkspaceId, "Orch", cwd: orchCwd);
                orchTab = tab.TabId;
                orchPane = tab.Panes[0].PaneId;
            }

            if (seedSpecialist)
            {
                var tab = fake.SeedTab(ws.WorkspaceId, "MavRef-DL");
                specTab = tab.TabId;
                specPane = tab.Panes[0].PaneId;
            }

            return new Fixture
            {
                Fake = fake,
                Runtime = BuildRuntime(settings, fake),
                Settings = settings,
                WorkspaceId = ws.WorkspaceId,
                OrchTabId = orchTab,
                OrchPaneId = orchPane,
                SpecialistTabId = specTab,
                SpecialistPaneId = specPane,
            };
        }

        public Task<RunnerSessionDto> StartNamedAsync(
            IReadOnlyDictionary<string, string>? env = null) =>
            Runtime.StartAsync(Request(SessionId, Cwd, workspaceKey: WorkspaceKey, env: env), CancellationToken.None);

        public async Task CleanupAsync()
        {
            try
            {
                await Runtime.KillAsync(SessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
            }
            catch
            {
                // Best-effort.
            }

            await Runtime.DisposeAsync();
            await Fake.DisposeAsync();
            DeleteLogRoot(Settings.SessionLogPath);
        }

        public async ValueTask DisposeAsync()
        {
            try { await Runtime.DisposeAsync(); }
            catch { /* already disposed */ }
            try { await Fake.DisposeAsync(); }
            catch { /* already disposed */ }
        }
    }
}
