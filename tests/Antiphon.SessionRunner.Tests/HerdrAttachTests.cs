using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0213 S1: inspect + attach + origin-aware kill. R4–R11, R13 plus the detach / re-adoption
/// / allocator-exclusion guards. Server-side R1–R3 / R12 live in Antiphon.Tests.
/// </summary>
[NotInParallel("SessionLiveness")]
public class HerdrAttachTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Attach_binds_a_live_grok_by_argv_and_writes_an_attached_sidecar()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var logs = new List<string>();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using var runtime = BuildRuntime(settings, fake, logs);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var events = runtime.Subscribe(cts.Token);

        var dto = await runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), cts.Token);

        dto.Status.ShouldBe("Running");
        dto.Adopted.ShouldBeFalse();
        dto.HerdrOrigin.ShouldBe(HerdrPaneOrigins.Attached);
        dto.SessionId.ShouldBe(nativeId);
        dto.Pid.ShouldBe(pane.Pid);

        var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId));
        sidecar.ShouldNotBeNull();
        sidecar!.Origin.ShouldBe(HerdrPaneOrigins.Attached);
        sidecar.ChildPid.ShouldBe(pane.Pid);
        sidecar.PaneId.ShouldBe(pane.PaneId);

        var transcript = TranscriptSidecar.TryLoad(TranscriptSidecar.PathFor(settings.SessionLogPath, nativeId));
        transcript.ShouldNotBeNull();
        transcript!.Format.ShouldBe(TranscriptFormats.Grok);
        transcript.How.ShouldBe(TranscriptBindMethods.Deterministic);
        transcript.TranscriptPath.ShouldBe(Path.Combine(grok.SessionDir, "updates.jsonl"));
        transcript.ResumeLaunch.ShouldBeTrue();

        var methods = Methods(fake);
        methods.ShouldContain("pane.report_metadata");
        methods.Count(m => m == "pane.report_metadata").ShouldBe(1);
        methods.ShouldNotContain("pane.send_text");
        methods.ShouldNotContain("pane.rename");
        methods.ShouldNotContain("agent.rename");
        methods.ShouldNotContain("tab.create");
        methods.ShouldNotContain("pane.split");

        var started = await WaitForEventAsync(events, SessionRunnerEventNames.SessionStarted, cts.Token);
        JsonSerializer.Deserialize<RunnerSessionStartedEvent>(started.Json)!.SessionId.ShouldBe(nativeId);

        await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), cts.Token);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Attach_locates_the_grok_directory_by_guid_when_cwd_encoding_differs()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var logs = new List<string>();
        var nativeId = Guid.NewGuid();
        var processCwd = Path.Combine(settings.SessionLogPath, "process-cwd");
        Directory.CreateDirectory(processCwd);
        var encodedCwd = Uri.EscapeDataString(Path.GetFullPath(processCwd).ToUpperInvariant());
        using var grok = SeedGrokHome(settings, nativeId, processCwd, encodedCwdOverride: encodedCwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, processCwd);

        await using var runtime = BuildRuntime(settings, fake, logs);
        var dto = await runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, processCwd), CancellationToken.None);
        dto.Status.ShouldBe("Running");
        dto.HerdrOrigin.ShouldBe(HerdrPaneOrigins.Attached);
        logs.ShouldContain(l => l.Contains("differs from process cwd", StringComparison.OrdinalIgnoreCase));

        await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Attach_takes_native_id_from_agent_session_when_argv_is_silent()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd, argv: ["grok"]);
        fake.SetPaneAgentSession(pane.PaneId, source: "herdr", kind: "session_id", value: nativeId.ToString("D"));

        await using var runtime = BuildRuntime(settings, fake);
        var inspect = await runtime.InspectHerdrPaneAsync(pane.PaneId, CancellationToken.None);
        inspect.NativeSessionId.ShouldBe(nativeId);
        inspect.NativeSessionSource.ShouldBe(HerdrNativeSessionSources.AgentSession);

        var dto = await runtime.AttachHerdrAsync(
            AttachRequest(nativeId, pane, cwd) with { ExpectedNativeSessionId = nativeId },
            CancellationToken.None);
        dto.Status.ShouldBe("Running");

        await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Antiphon_agent_session_is_not_evidence_of_native_id()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd, argv: ["grok"]);
        fake.SetPaneAgentSession(pane.PaneId, source: "antiphon", kind: "session_id", value: nativeId.ToString("D"));

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd) with { ExpectedNativeSessionId = null }, CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.NativeIdUnknown);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Unknown_pane_is_404()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        await using var runtime = BuildRuntime(settings, fake);

        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.InspectHerdrPaneAsync("w-missing:p1", CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneNotFound);
        AssertNoSidecar(settings);
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Unoccupied_pane_is_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, settings.SessionLogPath);
        fake.ClearDetectedAgent(pane.PaneId);
        fake.SetPaneProcessInfo(pane.PaneId, shellPid: 1,
            [(pane.Pid, "grok.exe", new[] { "grok", "--session-id", nativeId.ToString("D") }, settings.SessionLogPath)]);

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, settings.SessionLogPath), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneUnoccupied);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Kind_mismatch_is_refused_naming_both()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, settings.SessionLogPath);
        fake.SeedDetectedAgent(pane.PaneId, HerdrAgentKinds.Claude);

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, settings.SessionLogPath), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.KindMismatch);
        ex.Message.ShouldContain("claude");
        ex.Message.ShouldContain("grok");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Two_foreground_processes_are_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, settings.SessionLogPath);
        fake.SetPaneProcessInfo(pane.PaneId, shellPid: 1,
        [
            (pane.Pid, "grok.exe", new[] { "grok", "--session-id", nativeId.ToString("D") }, settings.SessionLogPath),
            (9001, "node.exe", new[] { "node" }, settings.SessionLogPath),
        ]);

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, settings.SessionLogPath), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneForeign);
        ex.Message.ShouldContain(pane.Pid.ToString());
        ex.Message.ShouldContain("9001");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Wrong_executable_family_is_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, settings.SessionLogPath, exeName: "node.exe");

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, settings.SessionLogPath), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneForeign);
        ex.Message.ShouldContain("node.exe");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Bound_pane_is_refused_naming_the_holder()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using var runtime = BuildRuntime(settings, fake);
        await runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), CancellationToken.None);

        var otherId = Guid.NewGuid();
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(
                AttachRequest(nativeId, pane, cwd) with { SessionId = otherId },
                CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneBound);
        ex.Message.ShouldContain(nativeId.ToString("D"));
        ex.Message.ShouldContain(HerdrPaneOrigins.Attached);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, otherId)).ShouldBeFalse();

        await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Last_pane_of_another_session_is_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, settings.SessionLogPath);
        new HerdrLastPane
        {
            SessionId = holderId,
            WorkspaceKey = "none",
            WorkspaceId = "w2",
            TabId = "w2:t-seed",
            PaneId = pane.PaneId,
            Origin = HerdrPaneOrigins.Launched,
            ExitReason = HerdrExitReasons.PaneClosed,
            ExitedAtUtc = DateTime.UtcNow,
        }.SaveAtomic(HerdrLastPane.PathFor(settings.SessionLogPath, holderId));

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, settings.SessionLogPath), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneBound);
        ex.Message.ShouldContain(holderId.ToString("D"));
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Grok_without_session_id_is_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, settings.SessionLogPath, argv: ["grok"]);

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(
                AttachRequest(nativeId, pane, settings.SessionLogPath) with { ExpectedNativeSessionId = null },
                CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.NativeIdUnknown);
        ex.Message.ShouldContain("--session-id");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Grok_with_no_session_directory_is_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = new GrokHomePin(Path.Combine(settings.SessionLogPath, "empty-grok"));
        Directory.CreateDirectory(Path.Combine(grok.Home, "sessions"));
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using var runtime = BuildRuntime(settings, fake);
        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.TranscriptNotFound);
        ex.Message.ShouldContain(Path.Combine(grok.Home, "sessions"));
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        AssertOnlyReads(fake);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Pane_that_changed_since_inspect_is_refused()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using var runtime = BuildRuntime(settings, fake);
        var inspect = await runtime.InspectHerdrPaneAsync(pane.PaneId, CancellationToken.None);
        inspect.NativeSessionId.ShouldBe(nativeId);
        inspect.Foreground.ShouldHaveSingleItem().Pid.ShouldBe(pane.Pid);

        fake.SetPaneProcessInfo(pane.PaneId, shellPid: 1,
            [(pane.Pid + 1, "grok.exe", new[] { "grok", "--session-id", nativeId.ToString("D") }, cwd)]);

        var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
            runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneChanged);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Unreachable_herdr_is_503_and_writes_nothing()
    {
        var settings = BuildSettings();
        var client = new HerdrClient(new HerdrSettings
        {
            Enabled = true,
            Session = $"antiphon-herdr-attach-down-{Guid.NewGuid():N}",
            ConnectTimeoutMs = 250,
        });
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            client);

        var nativeId = Guid.NewGuid();
        var ex = await Should.ThrowAsync<HerdrBackendUnavailableException>(() =>
            runtime.AttachHerdrAsync(
                new HerdrAttachRequest(
                    nativeId, "w2:p3", HerdrAgentKinds.Grok, TranscriptFormats.Grok,
                    ExpectedChildPid: 1, WorkspaceKey: "none"),
                CancellationToken.None));
        ex.ShouldNotBeNull();
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        Directory.Exists(settings.SessionLogPath).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Attached_kill_detaches()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        using var dummy = StartDummy();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd, pid: dummy.Id);

        await using var runtime = BuildRuntime(settings, fake);
        await runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), CancellationToken.None);

        var killed = await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        killed.Status.ShouldBe("Exited");
        killed.ExitCode.ShouldBe(0);
        killed.ExitReason.ShouldBe(HerdrExitReasons.Detached);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        File.Exists(HerdrLastPane.PathFor(settings.SessionLogPath, nativeId)).ShouldBeFalse();
        dummy.HasExited.ShouldBeFalse();
        fake.Workspaces[0].Tabs[0].Panes.ShouldContain(p => p.PaneId == pane.PaneId);
        Methods(fake).ShouldNotContain("pane.close");
        fake.Requests.Count(r => r.GetProperty("method").GetString() == "pane.report_metadata"
                                 && r.GetProperty("params").TryGetProperty("clear_state_labels", out var clear)
                                 && clear.GetBoolean())
            .ShouldBe(1);

        KillBestEffort(dummy);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Runner_restart_readopts_an_attached_sidecar_with_origin_intact()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using (var runtimeA = BuildRuntime(settings, fake))
        {
            await runtimeA.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), CancellationToken.None);
        }

        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, nativeId)).ShouldBeTrue();

        await using var runtimeB = BuildRuntime(settings, fake);
        await runtimeB.AdoptOrphanedHostsAsync(new PowershellProcessProbe(), CancellationToken.None);
        var dto = runtimeB.Get(nativeId);
        dto.Status.ShouldBe("Running");
        dto.Adopted.ShouldBeTrue();
        dto.HerdrOrigin.ShouldBe(HerdrPaneOrigins.Attached);

        await runtimeB.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Attached_orphan_is_dropped_not_killed()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        using var dummy = StartDummy();
        try
        {
            var sessionId = Guid.NewGuid();
            var launched = DateTime.UtcNow;
            var sidecar = new HerdrPaneSidecar
            {
                SessionId = sessionId,
                WorkspaceKey = "none",
                WorkspaceId = "wZ",
                TabId = "wZ:t1",
                PaneId = "wZ:p-missing",
                ChildPid = dummy.Id,
                ShellPid = 1,
                LaunchedAtUtc = launched,
                Cwd = settings.SessionLogPath,
                Origin = HerdrPaneOrigins.Attached,
                UpdatedAtUtc = launched,
            };
            sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId));

            await using var runtime = BuildRuntime(settings, fake);
            await runtime.AdoptOrphanedHostsAsync(new PowershellProcessProbe(), CancellationToken.None);

            var dto = runtime.Get(sessionId);
            dto.Status.ShouldBe("Exited");
            dto.ExitReason.ShouldBe(HerdrExitReasons.RestartPresumedDead);
            dummy.HasExited.ShouldBeFalse();
            File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
            File.Exists(HerdrLastPane.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        }
        finally
        {
            KillBestEffort(dummy);
            DeleteLogRoot(settings.SessionLogPath);
        }
    }

    [Test]
    public async Task Attached_pane_is_not_an_allocator_slot()
    {
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Grok;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        const string workspaceKey = "card0213-allocator";
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using var runtime = BuildRuntime(settings, fake);
        await runtime.AttachHerdrAsync(
            AttachRequest(nativeId, pane, cwd) with { WorkspaceKey = workspaceKey },
            CancellationToken.None);

        var launchId = Guid.NewGuid();
        await runtime.StartAsync(
            new RunnerLaunchRequest(
                launchId,
                "grok",
                ["--session-id", launchId.ToString("D")],
                new Dictionary<string, string>(),
                cwd,
                Cols: 120,
                Rows: 30,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: workspaceKey,
                    WorkspaceLabel: "card0213-alloc",
                    WorkspaceCwd: cwd,
                    PaneTitle: "card0213-alloc",
                    AgentKind: HerdrAgentKinds.Grok)),
            CancellationToken.None);

        Methods(fake).Count(m => m == "tab.create").ShouldBe(1);
        Methods(fake).ShouldNotContain("pane.split");

        await runtime.KillAsync(launchId, TimeSpan.FromSeconds(2), CancellationToken.None);
        await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public void Pane_occupied_on_sessions_post_is_a_409_with_its_code()
    {
        var result = HerdrProblemMapper.MapLaunch(
            new HerdrLaunchException(
                "pane w2:p3 is occupied by grok.exe pid 1 (no --session-id); not stolen — run attach (CARD-0213) or free the pane",
                HerdrLaunchException.CodePaneOccupied));
        var problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(409);
        problem.ProblemDetails.Type.ShouldBe(HerdrProblemTypes.PaneOccupied);
        problem.ProblemDetails.Detail.ShouldContain("CARD-0213");
    }

    [Test]
    public async Task Inspect_reports_bound_session_after_attach()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var nativeId = Guid.NewGuid();
        var cwd = settings.SessionLogPath;
        using var grok = SeedGrokHome(settings, nativeId, cwd);
        var pane = SeedGrokPane(fake, "w2:p3", nativeId, cwd);

        await using var runtime = BuildRuntime(settings, fake);
        var before = await runtime.InspectHerdrPaneAsync(pane.PaneId, CancellationToken.None);
        before.BoundToSessionId.ShouldBeNull();
        before.Agent.ShouldBe(HerdrAgentKinds.Grok);
        before.NativeSessionSource.ShouldBe(HerdrNativeSessionSources.Argv);

        await runtime.AttachHerdrAsync(AttachRequest(nativeId, pane, cwd), CancellationToken.None);
        var after = await runtime.InspectHerdrPaneAsync(pane.PaneId, CancellationToken.None);
        after.BoundToSessionId.ShouldBe(nativeId);
        after.BoundOrigin.ShouldBe(HerdrPaneOrigins.Attached);

        await runtime.KillAsync(nativeId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public void Executable_family_lists_kind_binaries_and_excludes_pwsh()
    {
        HerdrAgentKinds.IsFamilyMember(HerdrAgentKinds.Grok, "grok.exe").ShouldBeTrue();
        HerdrAgentKinds.IsFamilyMember(HerdrAgentKinds.Claude, "node.exe").ShouldBeTrue();
        HerdrAgentKinds.IsFamilyMember(HerdrAgentKinds.Codex, "cmd.exe").ShouldBeTrue();
        HerdrAgentKinds.IsFamilyMember(HerdrAgentKinds.Grok, "pwsh").ShouldBeFalse();
        HerdrAgentKinds.IsFamilyMember(HerdrAgentKinds.Grok, "node.exe").ShouldBeFalse();
    }

    private static HerdrAttachRequest AttachRequest(Guid sessionId, SeededPane pane, string cwd) =>
        new(
            sessionId,
            pane.PaneId,
            HerdrAgentKinds.Grok,
            TranscriptFormats.Grok,
            pane.Pid,
            WorkspaceKey: "card0213",
            ExpectedNativeSessionId: sessionId);

    private static SeededPane SeedGrokPane(
        FakeHerdrServer fake,
        string paneId,
        Guid nativeId,
        string cwd,
        string exeName = "grok.exe",
        string[]? argv = null,
        int? pid = null)
    {
        fake.SeedDetectedAgent(paneId, HerdrAgentKinds.Grok);
        var childPid = pid ?? 4243;
        argv ??= ["grok", "--session-id", nativeId.ToString("D")];
        fake.SetPaneProcessInfo(paneId, shellPid: 1, [(childPid, exeName, argv, cwd)]);
        return new SeededPane(paneId, childPid);
    }

    private static GrokHomePin SeedGrokHome(
        SessionRunnerSettings settings,
        Guid nativeId,
        string cwd,
        string? encodedCwdOverride = null)
    {
        var home = Path.Combine(settings.SessionLogPath, "grok-home");
        var encoded = encodedCwdOverride ?? Uri.EscapeDataString(Path.GetFullPath(cwd));
        var sessionDir = Path.Combine(home, "sessions", encoded, nativeId.ToString("D"));
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "updates.jsonl"), "");
        return new GrokHomePin(home, sessionDir);
    }

    private static SessionRunnerRuntime BuildRuntime(
        SessionRunnerSettings settings,
        FakeHerdrServer fake,
        List<string>? logs = null) =>
        new(
            Options.Create(settings),
            logs is null
                ? NullLogger<SessionRunnerRuntime>.Instance
                : new ListLogger<SessionRunnerRuntime>(logs),
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }),
            new PowershellProcessProbe());

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-attach-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static List<string> Methods(FakeHerdrServer fake) =>
        fake.Requests.Select(r => r.GetProperty("method").GetString()!).ToList();

    private static readonly HashSet<string> ReadMethods =
        new(StringComparer.Ordinal) { "ping", "pane.get", "pane.process_info", "pane.list", "workspace.list" };

    private static void AssertOnlyReads(FakeHerdrServer fake)
    {
        foreach (var method in Methods(fake))
            ReadMethods.ShouldContain(method);
    }

    private static void AssertNoSidecar(SessionRunnerSettings settings)
    {
        var dir = HerdrPaneSidecar.DirectoryFor(settings.SessionLogPath);
        if (!Directory.Exists(dir))
            return;
        Directory.GetFiles(dir, "*.json").ShouldBeEmpty();
    }

    private static async Task<RunnerServerSentEvent> WaitForEventAsync(
        System.Threading.Channels.ChannelReader<RunnerServerSentEvent> events,
        string name,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var published = await events.ReadAsync(ct);
            if (published.EventName == name)
                return published;
        }

        throw new TimeoutException($"Did not see {name}");
    }

    private static Process StartDummy()
    {
        var psi = new ProcessStartInfo(Cmd, "/d /q /k @echo off & prompt $G")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi) ?? throw new InvalidOperationException("failed to start dummy");
    }

    private static void KillBestEffort(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
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

    private sealed record SeededPane(string PaneId, int Pid);

    private sealed class GrokHomePin : IDisposable
    {
        private readonly IDisposable _scope;
        public string Home { get; }
        public string SessionDir { get; }

        public GrokHomePin(string home, string? sessionDir = null)
        {
            Home = home;
            SessionDir = sessionDir ?? home;
            Directory.CreateDirectory(home);
            _scope = GrokTranscriptTailer.OverrideGrokHome(home);
        }

        public void Dispose() => _scope.Dispose();
    }
}
