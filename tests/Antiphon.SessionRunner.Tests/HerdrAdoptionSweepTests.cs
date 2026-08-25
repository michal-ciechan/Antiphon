using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0186 S2: <see cref="SessionRunnerRuntime.AdoptOrphanedHostsAsync"/> herdr arm — there was
/// previously no test of AdoptHerdrSessionsAsync at all. One test per matrix row R1–R5, R7, R8, R13.
/// P7 (2026-08-25): Claude dies with herdr; the live restart shape is R2. R3/R5 are defensive.
/// </summary>
[NotInParallel("SessionLiveness")]
public class HerdrAdoptionSweepTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task R1_runner_restart_adopts_when_pane_lists_the_child()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using (var runtimeA = BuildRuntime(settings, fake))
        {
            await StartHerdrSessionAsync(runtimeA, sessionId, settings.SessionLogPath);
        }

        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();

        await using var runtimeB = BuildRuntime(settings, fake);
        await runtimeB.AdoptOrphanedHostsAsync(new StubProbe(alive: true), CancellationToken.None);

        var dto = runtimeB.Get(sessionId);
        dto.Status.ShouldBe("Running");
        dto.Adopted.ShouldBeTrue();
        dto.Backend.ShouldBe(SessionBackends.Herdr);
        dto.Pending.ShouldBeNull();
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();
        await runtimeB.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R2_restored_empty_pane_with_os_dead_is_RestartPresumedDead()
    {
        // P7: this is the measured herdr-restart shape — pane id answers, new shell, Claude dead.
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sessionId = Guid.NewGuid();

        await using (var runtimeA = BuildRuntime(settings, fake))
        {
            await StartHerdrSessionAsync(runtimeA, sessionId, settings.SessionLogPath);
        }

        var paneId = fake.RequireAgentPaneId();
        fake.SetPaneProcessInfo(paneId, shellPid: 41628); // empty foreground — restored shell only

        await using var runtimeB = BuildRuntime(settings, fake);
        await runtimeB.AdoptOrphanedHostsAsync(new StubProbe(alive: false), CancellationToken.None);

        var dto = runtimeB.Get(sessionId);
        dto.Status.ShouldBe("Exited");
        dto.ExitReason.ShouldBe(HerdrExitReasons.RestartPresumedDead);
        dto.Backend.ShouldBe(SessionBackends.Herdr);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R3_os_alive_orphan_in_empty_pane_is_killed_then_RestartPresumedDead()
    {
        // Defensive: P7 never produced an OS-alive orphan. Pin the kill-by-pid arm anyway.
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        using var dummy = StartDummy();
        try
        {
            var sidecar = WriteSidecar(settings, fake, dummy.Id);
            fake.SetPaneProcessInfo(sidecar.PaneId, shellPid: 1); // child not listed

            await using var runtime = BuildRuntime(settings, fake);
            await runtime.AdoptOrphanedHostsAsync(new StubProbe(alive: true), CancellationToken.None);

            var dto = runtime.Get(sidecar.SessionId);
            dto.Status.ShouldBe("Exited");
            dto.ExitReason.ShouldBe(HerdrExitReasons.RestartPresumedDead);
            dummy.WaitForExit(5_000).ShouldBeTrue("orphan kill is of the process we named");
            File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeFalse();
        }
        finally
        {
            KillBestEffort(dummy);
            DeleteLogRoot(settings.SessionLogPath);
        }
    }

    [Test]
    public async Task R4_unknown_pane_with_os_dead_is_RestartPresumedDead()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        var sidecar = WriteSidecar(settings, paneId: "no-such-pane", workspaceId: "w0", tabId: "w0:t0", childPid: 4243);

        await using var runtime = BuildRuntime(settings, fake);
        await runtime.AdoptOrphanedHostsAsync(new StubProbe(alive: false), CancellationToken.None);

        var dto = runtime.Get(sidecar.SessionId);
        dto.Status.ShouldBe("Exited");
        dto.ExitReason.ShouldBe(HerdrExitReasons.RestartPresumedDead);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R5_unknown_pane_with_os_alive_orphan_is_killed_then_RestartPresumedDead()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        using var dummy = StartDummy();
        try
        {
            var sidecar = WriteSidecar(
                settings, paneId: "no-such-pane", workspaceId: "w0", tabId: "w0:t0", childPid: dummy.Id);

            await using var runtime = BuildRuntime(settings, fake);
            await runtime.AdoptOrphanedHostsAsync(new StubProbe(alive: true), CancellationToken.None);

            var dto = runtime.Get(sidecar.SessionId);
            dto.Status.ShouldBe("Exited");
            dto.ExitReason.ShouldBe(HerdrExitReasons.RestartPresumedDead);
            dummy.WaitForExit(5_000).ShouldBeTrue();
            File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeFalse();
        }
        finally
        {
            KillBestEffort(dummy);
            DeleteLogRoot(settings.SessionLogPath);
        }
    }

    [Test]
    public async Task R7_herdr_unreachable_and_os_dead_is_ChildGone_without_a_socket()
    {
        var settings = BuildSettings();
        var sidecar = WriteSidecar(
            settings, paneId: "wX:p1", workspaceId: "wX", tabId: "wX:t1", childPid: 4243);

        var client = new HerdrClient(new HerdrSettings
        {
            Enabled = true,
            Session = $"antiphon-herdr-gone-{Guid.NewGuid():N}",
            ConnectTimeoutMs = 250,
        });
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            client);

        await runtime.AdoptOrphanedHostsAsync(new StubProbe(alive: false), CancellationToken.None);

        var dto = runtime.Get(sidecar.SessionId);
        dto.Status.ShouldBe("Exited");
        dto.ExitReason.ShouldBe(HerdrExitReasons.ChildGone);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R8_liveness_sweep_on_a_dead_herdr_child_is_ProcessVanished_and_deletes_the_sidecar()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();

        var marked = runtime.SweepVanishedSessions(new StubProbe(alive: false));
        marked.ShouldBe([sessionId]);

        var dto = runtime.Get(sessionId);
        dto.Status.ShouldBe("Exited");
        dto.ExitReason.ShouldBe("ProcessVanished");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R6_unreachable_at_restart_with_os_alive_is_pending_then_adopts_when_herdr_returns()
    {
        var settings = BuildSettings();
        var sessionName = $"antiphon-herdr-pending-{Guid.NewGuid():N}";
        var sidecar = WriteSidecar(
            settings, paneId: "wP:p1", workspaceId: "wP", tabId: "wP:t1", childPid: 4243);

        var client = new HerdrClient(new HerdrSettings
        {
            Enabled = true,
            Session = sessionName,
            ConnectTimeoutMs = 250,
        });
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            client);

        await runtime.AdoptOrphanedHostsAsync(new StubProbe(alive: true), CancellationToken.None);

        var pending = runtime.Get(sidecar.SessionId);
        pending.Status.ShouldBe("Starting");
        pending.Adopted.ShouldBeTrue();
        pending.Backend.ShouldBe(SessionBackends.Herdr);
        pending.Pending.ShouldBe(HerdrPendingReasons.Unreachable);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeTrue();

        var sw = Stopwatch.StartNew();
        await Should.ThrowAsync<HerdrBackendUnavailableException>(
            () => runtime.SendInputAsync(sidecar.SessionId, "hello", CancellationToken.None));
        sw.ElapsedMilliseconds.ShouldBeLessThan(500, "pending WriteAsync must not wait on _clientReady");

        await using var fake = new FakeHerdrServer(sessionName);
        fake.Start();
        await fake.WaitUntilListeningAsync();
        SeedPane(fake, sidecar);

        var marked = await runtime.SweepVanishedSessionsAsync(
            new StubProbe(alive: true), CancellationToken.None);
        marked.ShouldBeEmpty("R1 adopt in place is not an exit");

        var adopted = runtime.Get(sidecar.SessionId);
        adopted.Status.ShouldBe("Running");
        adopted.Adopted.ShouldBeTrue();
        adopted.Pending.ShouldBeNull();
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeTrue();

        var stamped = await runtime.GetAsync(sidecar.SessionId, CancellationToken.None);
        stamped.HerdrVerifiedAtUtc.ShouldNotBeNull("single-session GET stamps after a passing verify");
        runtime.List().Single(s => s.SessionId == sidecar.SessionId)
            .HerdrVerifiedAtUtc.ShouldBe(stamped.HerdrVerifiedAtUtc, "list reports the last stamp");

        await runtime.KillAsync(sidecar.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R6_kill_on_pending_deletes_sidecar_kills_child_and_exits_PaneLeftOpen()
    {
        var settings = BuildSettings();
        using var dummy = StartDummy();
        try
        {
            var sidecar = WriteSidecar(
                settings, paneId: "wK:p1", workspaceId: "wK", tabId: "wK:t1", childPid: dummy.Id);
            var client = new HerdrClient(new HerdrSettings
            {
                Enabled = true,
                Session = $"antiphon-herdr-pending-kill-{Guid.NewGuid():N}",
                ConnectTimeoutMs = 250,
            });
            await using var runtime = new SessionRunnerRuntime(
                Options.Create(settings),
                NullLogger<SessionRunnerRuntime>.Instance,
                client,
                processLiveness: new StubProbe(alive: true));

            await runtime.AdoptOrphanedHostsAsync(new StubProbe(alive: true), CancellationToken.None);
            runtime.Get(sidecar.SessionId).Pending.ShouldBe(HerdrPendingReasons.Unreachable);

            await runtime.KillAsync(sidecar.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None);

            var dto = runtime.Get(sidecar.SessionId);
            dto.Status.ShouldBe("Exited");
            dto.ExitReason.ShouldBe(HerdrExitReasons.PaneLeftOpen);
            dummy.WaitForExit(5_000).ShouldBeTrue();
            File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId)).ShouldBeFalse();
        }
        finally
        {
            KillBestEffort(dummy);
            DeleteLogRoot(settings.SessionLogPath);
        }
    }

    [Test]
    public async Task R9_herdr_restart_mid_session_input_throws_then_pump_bar_exits_on_empty_pane()
    {
        var sessionName = $"antiphon-herdr-r9-{Guid.NewGuid():N}";
        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings
            {
                Enabled = true,
                Session = sessionName,
                ConnectTimeoutMs = 250,
            }),
            processLiveness: new StubProbe(alive: false));

        var sessionId = Guid.NewGuid();
        HerdrPaneSidecar? sidecar;
        var herdrSettings = new HerdrSettings
        {
            Enabled = true,
            Session = sessionName,
            ConnectTimeoutMs = 250,
            EventsReconnectMinSeconds = 1,
            EventsReconnectMaxSeconds = 2,
        };
        var pump = new HerdrEventPumpService(
            runtime,
            new HerdrClient(herdrSettings),
            Options.Create(herdrSettings),
            NullLogger<HerdrEventPumpService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        {
            await using var fake = new FakeHerdrServer(sessionName);
            fake.Start();
            await fake.WaitUntilListeningAsync();
            await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
            sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId));
            sidecar.ShouldNotBeNull();

            await pump.StartAsync(cts.Token);
            var subscribed = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < subscribed && fake.SubscriptionRecords.Count == 0)
                await Task.Delay(50);
            fake.SubscriptionRecords.Count.ShouldBeGreaterThan(0);
        }

        await Should.ThrowAsync<HerdrBackendUnavailableException>(
            () => runtime.SendInputAsync(sessionId, "while-down", CancellationToken.None));
        var whileDown = runtime.Get(sessionId);
        whileDown.Status.ShouldBe("Running", "R9 while down does not convert Running to Pending");
        whileDown.Pending.ShouldBeNull();

        await using var fake2 = new FakeHerdrServer(sessionName);
        fake2.Start();
        await fake2.WaitUntilListeningAsync();
        SeedPane(fake2, sidecar!, listedPid: null);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        RunnerSessionDto? after = null;
        while (DateTime.UtcNow < deadline)
        {
            after = runtime.Get(sessionId);
            if (after.Status == "Exited")
                break;
            await Task.Delay(100);
        }

        after.ShouldNotBeNull();
        after!.Status.ShouldBe("Exited", "pump reconnect re-runs the bar → R2 empty pane");
        after.ExitReason.ShouldBe(HerdrExitReasons.PaneClosed);
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();

        await pump.StopAsync(CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task R13_replayed_pane_closed_on_a_healthy_pane_does_nothing()
    {
        await using var fake = new FakeHerdrServer();
        fake.Start();
        await fake.WaitUntilListeningAsync();
        var settings = BuildSettings();
        await using var runtime = BuildRuntime(settings, fake);
        var sessionId = Guid.NewGuid();
        await StartHerdrSessionAsync(runtime, sessionId, settings.SessionLogPath);
        var paneId = fake.RequireAgentPaneId();
        var workspaceId = fake.Workspaces[0].WorkspaceId;
        fake.AddReplayPaneClosed(paneId, workspaceId);

        var herdrSettings = new HerdrSettings { Enabled = true, Session = fake.Session };
        var pump = new HerdrEventPumpService(
            runtime,
            new HerdrClient(herdrSettings),
            Options.Create(herdrSettings),
            NullLogger<HerdrEventPumpService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await pump.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && fake.SubscriptionRecords.Count == 0)
            await Task.Delay(50);
        fake.SubscriptionRecords.Count.ShouldBeGreaterThan(0, "pump must subscribe so the E5 replay is delivered");
        await Task.Delay(200);

        var dto = runtime.Get(sessionId);
        dto.Status.ShouldBe("Running", "CARD-0162 E5: a replayed pane_closed is a trigger, not evidence");
        File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeTrue();

        await pump.StopAsync(CancellationToken.None);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
        DeleteLogRoot(settings.SessionLogPath);
    }

    private static SessionRunnerRuntime BuildRuntime(SessionRunnerSettings settings, FakeHerdrServer fake) =>
        new(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session }));

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-herdr-adopt-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static async Task StartHerdrSessionAsync(
        SessionRunnerRuntime runtime, Guid sessionId, string cwd)
    {
        var dto = await runtime.StartAsync(
            new RunnerLaunchRequest(
                sessionId,
                "claude",
                ["--dangerously-skip-permissions"],
                new Dictionary<string, string>(),
                cwd,
                Cols: 120,
                Rows: 30,
                TranscriptEnabled: false,
                Backend: SessionBackends.Herdr,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: $"test-{sessionId:N}"[..32],
                    WorkspaceLabel: "card0186-adopt",
                    WorkspaceCwd: cwd,
                    PaneTitle: "card0186-adopt")),
            CancellationToken.None);
        dto.Status.ShouldBe("Running");
    }

    private static void SeedPane(FakeHerdrServer fake, HerdrPaneSidecar sidecar, int? listedPid = 4243)
    {
        fake.Workspaces.Add(new FakeHerdrServer.WorkspaceState(
            sidecar.WorkspaceId, "seed", 1, sidecar.TabId,
            [new FakeHerdrServer.TabState(sidecar.TabId, sidecar.WorkspaceId, "1", 1,
                [new FakeHerdrServer.PaneState(
                    sidecar.PaneId, sidecar.TabId, sidecar.WorkspaceId, "term_seed",
                    null, null, null, null, null)])],
            new Dictionary<string, string>()));
        if (listedPid is int pid)
            fake.SetPaneProcessInfo(sidecar.PaneId, shellPid: 1, (pid, "claude.exe"));
        else
            fake.SetPaneProcessInfo(sidecar.PaneId, shellPid: 1);
    }

    private static HerdrPaneSidecar WriteSidecar(
        SessionRunnerSettings settings,
        FakeHerdrServer fake,
        int childPid)
    {
        // Seed a pane herdr knows about so R3 can fail the listed check rather than pane.get.
        if (fake.Workspaces.Count == 0)
        {
            fake.Workspaces.Add(new FakeHerdrServer.WorkspaceState(
                "wZ", "seed", 1, "wZ:t1",
                [new FakeHerdrServer.TabState("wZ:t1", "wZ", "1", 1,
                    [new FakeHerdrServer.PaneState("wZ:p1", "wZ:t1", "wZ", "term_seed", null, null, null, null, null)])],
                new Dictionary<string, string>()));
        }

        var pane = fake.Workspaces[0].Tabs[0].Panes[0];
        return WriteSidecar(settings, pane.PaneId, pane.WorkspaceId, pane.TabId, childPid);
    }

    private static HerdrPaneSidecar WriteSidecar(
        SessionRunnerSettings settings,
        string paneId,
        string workspaceId,
        string tabId,
        int? childPid)
    {
        var launched = DateTime.UtcNow;
        var sidecar = new HerdrPaneSidecar
        {
            SessionId = Guid.NewGuid(),
            WorkspaceKey = "none",
            WorkspaceId = workspaceId,
            TabId = tabId,
            PaneId = paneId,
            ChildPid = childPid,
            ShellPid = 1,
            LaunchedAtUtc = launched,
            Cwd = settings.SessionLogPath,
            UpdatedAtUtc = launched,
        };
        sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sidecar.SessionId));
        return sidecar;
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

    private sealed class StubProbe(bool alive) : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => alive;
    }
}
