using System.Net;
using System.Net.Sockets;
using System.Text;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: restart-apphost (AppHostRestart) in process on
/// AppHostWorld. The CARD-0716 rows were the deleted scripts/test-apphost-graceful-stop.ps1 cases (T-1..T-6, T-8 against a
/// loopback server with the real HTTP, T-9, T-10, T-12; scripts/test-cutover-postgres-server2.ps1 W-7 ran T-9, T-10 and T-12),
/// the CARD-1533 hint rows were RestartApphostLockHintTests (a text check of the script), and the outcome rows (CARD-0310,
/// CARD-1527, CARD-1680, CARD-1806) are new. Dropped: T-7 (ASCII and 5.1/pwsh parse of the deleted script, common library and
/// driver; the kept launcher's parse is AntiphonCliLauncherTests') and T-13/T-14 (the driver's dot-source closure copy).
/// </summary>
[Category("Unit")]
public sealed class AppHostRestartTests
{
    private static (int Exit, string Text) Run(AppHostWorld world, params string[] arguments)
    {
        var before = world.Out.ToString().Length;
        var exit = AppHostRestart.Run(AppHostRestartOptions.Parse(arguments, world.Root), world.Host(), world);
        return (exit, world.Out.ToString()[before..] + world.Error.ToString());
    }

    private static AppHostWorld World(Action<AppHostWorld>? configure = null)
    {
        var world = new AppHostWorld();
        File.WriteAllText(world.LogPath("apphost.pid"), "111");
        configure?.Invoke(world);
        return world;
    }

    [Test]
    public void T1_graceful_stop_runs_before_the_force_kill_and_reports_the_exit()
    {
        using var world = World(w => { w.GracefulClass = "accepted"; w.AliveCalls[4242] = 2; });
        var r = Run(world, "--timeout-sec", "8");
        r.Exit.ShouldBe(0, r.Text);
        world.IndexOf("graceful").ShouldBeGreaterThanOrEqualTo(0);
        world.IndexOf("stop-tree").ShouldBeGreaterThan(world.IndexOf("graceful"));
        r.Text.ShouldContain("graceful stop accepted; server PID 4242 exited after 2s");
        world.Trace.ShouldContain("graceful accepted restart-apphost 5");
        r.Text.ShouldContain("AppHost restarted - dashboard http://127.0.0.1:9/, backend healthy, server " + AppHostWorld.Head + ".");
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse("a healthy exit removes the restart lock");
    }

    [Test]
    public void T2_a_server_that_never_exits_is_forced_after_the_graceful_timeout()
    {
        using var baseline = World(w => w.GracefulClass = "unreachable");
        using var subject = World(w => { w.GracefulClass = "accepted"; w.AlwaysAlive.Add(4242); });
        var b = Run(baseline, "--timeout-sec", "8", "--graceful-stop-timeout-sec", "3");
        var s = Run(subject, "--timeout-sec", "8", "--graceful-stop-timeout-sec", "3");
        s.Exit.ShouldBe(b.Exit, s.Text);
        s.Text.ShouldContain("graceful stop accepted; server PID 4242 still alive after 3s; forcing");
        subject.Count("stop-tree").ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public void T3_unreachable_graceful_stop_does_not_wait_and_still_forces()
    {
        using var world = World(w => { w.GracefulClass = "unreachable"; w.GracefulDetail = "connection refused"; });
        var r = Run(world, "--timeout-sec", "8");
        r.Text.ShouldMatch(@"graceful stop unreachable: .*; forcing");
        world.Trace.Count(row => row.StartsWith("sleep 1", StringComparison.Ordinal)).ShouldBe(1, "only the port-free pause; no exit wait");
        world.Count("stop-tree").ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    [Arguments("unsupported", "http 404")]
    [Arguments("refused", "http 403")]
    public void T4_unsupported_and_refused_graceful_stops_still_force(string graceful, string detail)
    {
        using var world = World(w => { w.GracefulClass = graceful; w.GracefulDetail = detail; });
        var r = Run(world, "--timeout-sec", "8");
        r.Text.ShouldContain("graceful stop " + graceful + ": " + detail + "; forcing");
        world.Count("stop-tree").ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public void T5_skip_graceful_stop_goes_straight_to_the_force_kill()
    {
        using var world = World(w => w.GracefulClass = "accepted");
        var r = Run(world, "--timeout-sec", "8", "--skip-graceful-stop");
        world.Count("graceful").ShouldBe(0, r.Text);
        world.Count("stop-tree").ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public void T6_a_held_restart_lock_refuses_before_any_stop()
    {
        using var world = World(w => w.GracefulClass = "accepted");
        world.WriteLock("apphost.restart.lock", 12345, world.UtcNow);
        var r = Run(world, "--timeout-sec", "8");
        r.Exit.ShouldBe(3, r.Text);
        world.Count("graceful").ShouldBe(0);
        world.Count("stop-tree").ShouldBe(0);
        world.Count("launch").ShouldBe(0);
    }

    [Test]
    public void T9_stop_only_what_if_neither_posts_nor_launches_nor_locks()
    {
        using var world = World(w => w.GracefulClass = "accepted");
        var r = Run(world, "--stop-only", "--what-if");
        r.Exit.ShouldBe(0, r.Text);
        (world.Count("graceful"), world.Count("launch"), world.Count("stop-tree")).ShouldBe((0, 0, 0));
        r.Text.ShouldContain("STOP-ONLY WHATIF");
        r.Text.ShouldContain("  ports: 17200,17202,17203,17205,17206,17207,17209");
        r.Text.ShouldNotContain("launching dev-aspire");
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse();
    }

    [Test]
    public void T10_stop_only_posts_frees_the_tree_and_exits_before_the_relaunch()
    {
        using var world = World(w => { w.GracefulClass = "accepted"; w.AliveCalls[4242] = 1; });
        var r = Run(world, "--stop-only", "--timeout-sec", "8");
        r.Exit.ShouldBe(0, r.Text);
        world.IndexOf("graceful").ShouldBeGreaterThanOrEqualTo(0);
        world.IndexOf("stop-tree").ShouldBeGreaterThan(world.IndexOf("graceful"));
        world.Count("launch").ShouldBe(0);
        r.Text.ShouldContain("STOP-ONLY: AppHost stopped");
        r.Text.ShouldNotContain("launching dev-aspire");
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse();
    }

    [Test]
    public void T12_what_if_without_stop_only_refuses()
    {
        using var world = World(w => w.GracefulClass = "accepted");
        var r = Run(world, "--what-if");
        r.Exit.ShouldBe(3, r.Text);
        (world.Count("graceful"), world.Count("launch")).ShouldBe((0, 0));
        r.Text.ShouldContain("REFUSED:");
        r.Text.ShouldContain("Nothing was killed");
    }

    // CARD-0716 T-8: the real graceful stop (AppHostGracefulStop.Run on AppHostHttp.Post) against a loopback server: the class,
    // pid and detail of each answer, the token header and path, and the token never in any result.
    [Test]
    [ParallelLimiter<TestHelpers.ProcessSpawnLimit>]
    public async Task T8_the_real_graceful_stop_classifies_each_answer_and_never_shows_the_token()
    {
        const string sentinel = "C716-SENTINEL-TOKEN-9f3a";
        using var world = new AppHostWorld();
        var token = world.PathOf("operator-token");
        File.WriteAllText(token, sentinel);
        var outputs = new List<string>();
        foreach (var (status, body, hold, timeout, klass, pid, detail) in new (int, string, int, int, string, int?, string?)[]
                 {
                     (202, "{\"pid\":4242}", 0, 2, "accepted", 4242, null),
                     (403, "", 0, 2, "refused", null, "http 403"),
                     (404, "", 0, 2, "unsupported", null, "http 404"),
                     (500, "", 0, 2, "error", null, "http 500"),
                     (200, "", 5, 1, "unreachable", null, "timeout"),
                 })
        {
            using var server = new LoopbackServer(status, body, TimeSpan.FromSeconds(hold));
            var result = await Task.Run(() => AppHostGracefulStop.Run(world.Files, token, server.BaseUrl, "restart-apphost", timeout, AppHostHttp.Post));
            outputs.Add(result.ToString());
            (result.Class, result.Pid, result.Detail).ShouldBe((klass, pid, detail), status + " hold " + hold);
            if (hold == 0)
            {
                server.Seen.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
                (server.Path, server.Token).ShouldBe(("/api/operator/shutdown", sentinel));
                server.Body.ShouldBe("{\"reason\":\"restart-apphost\"}");
            }
        }
        var closed = LoopbackServer.FreePort();
        var refused = AppHostGracefulStop.Run(world.Files, token, "http://127.0.0.1:" + closed, "restart-apphost", 2, AppHostHttp.Post);
        refused.Class.ShouldBe("unreachable");
        var missing = AppHostGracefulStop.Run(world.Files, world.PathOf("missing-token"), "http://127.0.0.1:" + closed, "restart-apphost", 2, AppHostHttp.Post);
        (missing.Class, missing.Detail).ShouldBe(("refused", "token file missing"));
        outputs.Add(refused.ToString());
        outputs.Add(missing.ToString());
        string.Join("\n", outputs).ShouldNotContain(sentinel);
    }

    // ---- CARD-1533: the refusal and the timeout never tell an operator to delete a fresh lock ----

    [Test]
    public void Refusal_hint_refuses_holder_death_as_delete_permission()
    {
        using var world = World();
        world.AlwaysAlive.Add(5150);
        world.WriteLock("apphost.restart.lock", 5150, world.UtcNow.AddMinutes(-2));
        var r = Run(world);
        r.Exit.ShouldBe(3, r.Text);
        r.Text.ShouldNotContain("genuinely gone");
        var lines = r.Text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        lines.ShouldContain("  Nothing was killed. A dead holder is not a reason to delete " + world.LogPath("apphost.restart.lock") + ".");
        lines.ShouldContain("  Inspect both locks, launcher output, owned processes, listeners and health.");
        lines.ShouldContain("  Early recovery needs the same lock identity on two watchdog fires within five minutes,");
        lines.ShouldContain("  holder state Dead, launcher activity Absent, connection-refused health, and no launch lock.");
        lines[^1].ShouldBe("  Otherwise wait for LockMaxAgeMinutes. Unknown, live, slow, or replacement evidence does not qualify.");
        world.Count("graceful").ShouldBe(0);
    }

    [Test]
    public void Timeout_hint_refuses_delete_to_force_a_retry()
    {
        using var world = World(w => w.OnLaunch = null);
        var r = Run(world, "--timeout-sec", "6");
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldNotContain("; delete it to force a retry.");
        (r.Text.Length - r.Text.Replace("force a retry", "").Length).ShouldBe("force a retry".Length, "one mention");
        var lines = r.Text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        lines.ShouldContain("  Leaving " + world.LogPath("apphost.restart.lock") + " for LockMaxAgeMinutes. Do not delete it to force a retry.");
        lines[^1].ShouldBe("  Prefer inspection, guarded dead-launch admission, or ordinary expiry.");
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeTrue("a timed-out launch may still be coming up (CARD-0310)");
    }

    // ---- outcomes ----

    [Test]
    public void A_different_loaded_sha_is_exit_5_with_the_lock_kept_and_the_child_left_running()
    {
        using var world = World(w => w.VersionSha = new string('b', 40));
        var r = Run(world, "--timeout-sec", "9");
        r.Exit.ShouldBe(5, r.Text);
        r.Text.ShouldContain("REFUSED: server build unverified (exit 5).");
        r.Text.ShouldContain("  expected SHA: " + AppHostWorld.Head);
        r.Text.ShouldContain("  observed SHA: " + new string('b', 40));
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeTrue();
        world.Count("child-stop").ShouldBe(0);
    }

    [Test]
    public void A_checkout_that_moved_during_the_wait_is_exit_5_naming_both_heads()
    {
        var moved = new string('c', 40);
        using var world = World(w => w.OnLaunch = x =>
        {
            File.WriteAllText(x.LogPath("apphost-dashboard-url.txt"), "http://127.0.0.1:9/");
            x.CurrentHead = moved;
        });
        var r = Run(world, "--timeout-sec", "9");
        r.Exit.ShouldBe(5, r.Text);
        r.Text.ShouldContain("  reason: checkout moved");
        r.Text.ShouldContain("  checkout moved: expected " + AppHostWorld.Head + ", HEAD now " + moved);
    }

    [Test]
    public void A_failed_build_is_exit_1_stopping_the_launch_and_removing_the_lock()
    {
        using var world = World(w => w.OnLaunch = x => File.WriteAllText(x.LogPath("apphost.log"), "Restore complete\nThe build failed. Fix the build errors and run again.\n"));
        var r = Run(world, "--timeout-sec", "9");
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("AppHost build FAILED - check: Get-Content '" + world.LogPath("apphost.log") + "' -Tail 40");
        world.Count("child-stop").ShouldBe(1);
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse();
        r.Text.ShouldContain("LIVE_TIMING {\"version\":1,");
        r.Text.ShouldContain("\"phase\":\"network-loopback\",\"operation\":\"health-wait\"");
    }

    [Test]
    public void A_dcp_dependency_timeout_is_exit_4_with_the_docker_verdict_and_the_lock_kept()
    {
        using var world = World(w => w.OnLaunch = x => File.WriteAllText(x.LogPath("apphost.log"),
            "fail: Aspire.Hosting.Dcp.DcpDependencyCheck: dependency check returned an error: podman: executable file not found\n"));
        world.Commands = (file, args) => new AppHostCommandResult(false, 0, "antiphon-postgres (Up 3 hours)\n");
        var r = Run(world, "--timeout-sec", "9");
        r.Exit.ShouldBe(4, r.Text);
        r.Text.ShouldContain("AppHost launch FAILED: Aspire's DCP dependency check TIMED OUT.");
        r.Text.ShouldContain("  docker: docker answered in 0.0s: 1 container(s) running");
        r.Text.ShouldContain("  IGNORE the podman text in that message.");
        r.Text.ShouldContain("    " + world.LogPath("apphost.restart.lock"));
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeTrue();
        world.Trace.ShouldContain("run docker ps --format {{.Names}} ({{.Status}})");
    }

    [Test]
    public void A_launcher_that_exits_nonzero_ends_the_wait_and_removes_the_lock()
    {
        using var world = World(w => { w.OnLaunch = null; w.ChildExit = 7; });
        var r = Run(world, "--timeout-sec", "60");
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("AppHost launcher exited 7 before health was confirmed; removing this restart's lock.");
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse();
    }

    [Test]
    public void An_apphost_that_died_with_nothing_on_17202_ends_the_wait_at_once()
    {
        using var world = World(w => w.OnLaunch = x => File.WriteAllText(x.LogPath("apphost.pid"), "5555"));
        var r = Run(world, "--timeout-sec", "60");
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("AppHost process 5555 exited before health was confirmed and nothing listens on 17202; removing this restart's lock.");
        world.Count("child-stop").ShouldBe(1);
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse();
    }

    [Test]
    public void The_dashboard_port_answer_is_progress_when_no_url_file_appears()
    {
        using var world = World(w => w.OnLaunch = null);
        world.Http = url => url.Contains(":17205/", StringComparison.Ordinal) ? new AppHostHttpAnswer(302, null, "redirect")
            : url.EndsWith("/health", StringComparison.Ordinal) ? new AppHostHttpAnswer(200, "Healthy", null)
            : new AppHostHttpAnswer(200, "{\"version\":\"" + AppHostWorld.Head + "\"}", null);
        var r = Run(world, "--timeout-sec", "9");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("AppHost restarted - dashboard http://localhost:17205, backend healthy");
    }

    [Test]
    public void A_present_phone_home_configuration_refuses_a_launch_but_not_a_stop_only()
    {
        using var world = World();
        File.WriteAllText(world.PhoneHomeConfigPath, "{}");
        var r = Run(world);
        r.Exit.ShouldBe(3, r.Text);
        r.Text.ShouldContain("REFUSED: phone-home-config-present");
        r.Text.ShouldContain("delete " + world.PhoneHomeConfigPath + ", run scripts\\antiphon.cmd restart-session-runner --hard, then retry.");
        world.Count("graceful").ShouldBe(0);
        File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse();
        var stop = Run(world, "--stop-only");
        stop.Exit.ShouldBe(0, stop.Text);
    }

    [Test]
    public void Sha_admission_refuses_before_any_stop()
    {
        using (var world = World())
        {
            var r = Run(world, "--expected-server-sha", "abc123");
            (r.Exit, world.Count("graceful")).ShouldBe((3, 0), r.Text);
            r.Text.ShouldContain("REFUSED: --expected-server-sha is not a full 40- or 64-character SHA: abc123");
        }
        using (var world = World())
        {
            var r = Run(world, "--expected-server-sha", new string('d', 40));
            (r.Exit, world.Count("graceful")).ShouldBe((3, 0), r.Text);
            r.Text.ShouldContain("does not match source-root HEAD " + AppHostWorld.Head);
        }
        using (var world = World(w => w.HeadExit = 128))
        {
            var r = Run(world, "--expected-server-sha", new string('d', 40));
            r.Exit.ShouldBe(3, r.Text);
            r.Text.ShouldContain("REFUSED: could not read a full HEAD SHA from source root " + world.Root);
            r.Text.ShouldContain("  supplied --expected-server-sha: " + new string('d', 40));
            File.Exists(world.LogPath("apphost.restart.lock")).ShouldBeFalse("a refusal releases the lock it took");
        }
        using (var world = World())
        {
            var r = Run(world, "--expected-server-sha", AppHostWorld.Head.ToUpperInvariant(), "--timeout-sec", "8");
            r.Exit.ShouldBe(0, r.Text);
        }
    }

    [Test]
    public void The_session_runner_and_its_port_are_preserved_and_other_owners_freed()
    {
        using var world = World();
        world.Owners[17204] = [7204];
        world.Owners[17203] = [7203, 7204];
        world.Named.Add(new AppHostProcessRow(8001, "dcpctrl", null, null));
        world.Named.Add(new AppHostProcessRow(7204, "Aspire.Dashboard", null, null));
        world.TrackedEdits = true;
        var r = Run(world, "--timeout-sec", "8");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("  preserving session-runner (PID 7204 on 17204)");
        r.Text.ShouldContain("  freed port 17203 (PID 7203)");
        r.Text.ShouldContain("  killed stale dcpctrl (PID 8001)");
        r.Text.ShouldContain("NOTE: source checkout has tracked edits");
        world.Trace.ShouldNotContain("stop-id 7204");
        world.Trace.ShouldContain("check-daemon-build");
        // Unit 37: the relaunch is the dev-aspire command, this CLI's run copy started detached (no dev-aspire.ps1, no pwsh).
        world.Trace.ShouldContain("launch dev-aspire");
        world.CliStarts.ShouldHaveSingleItem().ShouldBe(["dev-aspire"]);
    }

    [Test]
    public void The_launch_passes_no_build_and_allow_worktree_and_rotates_the_log()
    {
        using var world = World();
        File.WriteAllText(world.LogPath("apphost.log"), "previous launch\n");
        File.WriteAllText(world.LogPath("apphost.log.1"), "older launch\n");
        var r = Run(world, "--no-build", "--allow-worktree", "--timeout-sec", "8");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("  launching dev-aspire --no-build...");
        world.CliStarts.ShouldHaveSingleItem().ShouldBe(["dev-aspire", "--no-build", "--allow-worktree"]);
        File.ReadAllText(world.LogPath("apphost.log.1")).ShouldBe("previous launch\n");
        File.ReadAllText(world.LogPath("apphost.log.2")).ShouldBe("older launch\n");
    }

    // ---- CARD-1527: the restart lock reclaims a generation the watchdog proved dead on two fires, revalidated here ----

    [Test]
    [Arguments(2, 0)]
    [Arguments(1, 3)]
    public void A_proven_dead_restart_generation_is_reclaimed_in_place(int count, int exit)
    {
        using var world = World(w => w.HealthStatus = 0);
        var stamp = world.UtcNow.AddMinutes(-2);
        var path = world.WriteLock("apphost.restart.lock", 777, stamp);
        var identity = "777 " + stamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(world.LogPath("apphost-watchdog.state"), "{\"deadLaunch\":{\"Identity\":\"" + identity + "\",\"Count\":" + count
            + ",\"FireId\":\"f\",\"ObservedUtc\":\"" + world.UtcNow.AddMinutes(-1).ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\"}}");
        world.HealthStatus = 0;
        var held = AppHostLocks.TakeRestart(world, world.Host(), path, world.Root, 15);
        try
        {
            held.Acquired.ShouldBe(exit == 0, held.Reason);
            if (held.Acquired) held.Identity.ShouldStartWith("424242 ");
            else held.Reason!.ShouldContain("holder PID 777 exited; child may still be launching");
        }
        finally
        {
            held.Release(keepFile: false);
        }
    }

    [Test]
    public void A_live_launch_vetoes_the_reclaim()
    {
        using var world = World(w => w.HealthStatus = 0);
        var stamp = world.UtcNow.AddMinutes(-2);
        var path = world.WriteLock("apphost.restart.lock", 777, stamp);
        File.WriteAllText(world.LogPath("apphost-watchdog.state"), "{\"deadLaunch\":{\"Identity\":\"777 " + stamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
            + "\",\"Count\":2,\"FireId\":\"f\",\"ObservedUtc\":\"" + world.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\"}}");
        // A dev-aspire launch from this checkout's run copy (dotnet exec bin-launcher/run-<hash>/Antiphon.Scripts.dll dev-aspire).
        world.CensusRows.Add(new AppHostProcessRow(31337, "dotnet.exe", "dotnet exec " + Path.Combine(world.Root, "bin-launcher", "run-0123", "Antiphon.Scripts.dll") + " dev-aspire", null));
        var held = AppHostLocks.TakeRestart(world, world.Host(), path, world.Root, 15);
        held.Acquired.ShouldBeFalse();
        held.Release(false);
    }

    /// <summary>A one-request loopback HTTP server (no http.sys): records the path, the token header and the body, then answers.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;

        public LoopbackServer(int status, string body, TimeSpan hold)
        {
            _listener.Start();
            BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = Task.Run(async () =>
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    var buffer = new byte[65536];
                    var text = new StringBuilder();
                    while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var read = await stream.ReadAsync(buffer);
                        if (read == 0) return;
                        text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }
                    var request = text.ToString();
                    var head = request[..request.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
                    var lines = head.Split("\r\n");
                    Path = lines[0].Split(' ')[1];
                    Token = lines.Select(line => line.Split(':', 2)).Where(pair => pair.Length == 2 && pair[0].Trim().Equals("X-Antiphon-Operator-Token", StringComparison.OrdinalIgnoreCase))
                        .Select(pair => pair[1].Trim()).FirstOrDefault();
                    var length = lines.Select(line => line.Split(':', 2)).Where(pair => pair.Length == 2 && pair[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        .Select(pair => int.Parse(pair[1].Trim(), System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
                    var received = request[(head.Length + 4)..];
                    while (Encoding.ASCII.GetByteCount(received) < length)
                    {
                        var read = await stream.ReadAsync(buffer);
                        if (read == 0) break;
                        received += Encoding.ASCII.GetString(buffer, 0, read);
                    }
                    Body = received;
                    Seen.Set();
                    if (hold > TimeSpan.Zero) await Task.Delay(hold);
                    var payload = Encoding.ASCII.GetBytes(body);
                    var answer = "HTTP/1.1 " + status + " X\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
                    await stream.WriteAsync(payload);
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            });
        }

        public string BaseUrl { get; }

        public ManualResetEventSlim Seen { get; } = new();

        public string? Path { get; private set; }

        public string? Token { get; private set; }

        public string? Body { get; private set; }

        public static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _listener.Stop();
            try { _serve.Wait(TimeSpan.FromSeconds(10)); }
            catch (AggregateException) { }
        }
    }
}
