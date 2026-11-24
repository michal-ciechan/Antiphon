using System.Globalization;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: watchdog-apphost (AppHostWatchdog) and
/// autostart-apphost (AppHostAutostart, DevCompose) in process on AppHostWorld. scripts/watchdog-apphost.ps1 and
/// scripts/autostart-apphost.ps1 had no driver (the watchdog's -RestartScript stub existed for one); these rows pin the fire's
/// order (CARD-0075, CARD-0310, CARD-1527), the outage sinks and the logon start (dev-aspire in its process since unit 37).
/// </summary>
[Category("Unit")]
public sealed class AppHostWatchdogTests
{
    private const string Refused = "No connection could be made because the target machine actively refused it.";

    private static void Stack(AppHostWorld world, AppHostHttpAnswer health, AppHostHttpAnswer client) =>
        world.Http = url => url.EndsWith("/health", StringComparison.Ordinal) ? health : client;

    private static void Up(AppHostWorld world) => Stack(world, new AppHostHttpAnswer(200, "Healthy", null), new AppHostHttpAnswer(200, "<html>", null));

    private static void Down(AppHostWorld world) => Stack(world, new AppHostHttpAnswer(null, null, Refused), new AppHostHttpAnswer(null, null, Refused));

    private static (int Exit, string Text) Fire(AppHostWorld world, AppHostRestartCapture? restart = null, Func<string, string?>? variables = null, params string[] arguments)
    {
        var before = world.Out.ToString().Length;
        var exit = AppHostWatchdog.Run(AppHostWatchdogOptions.Parse(arguments, world.Root), world.Host(variables), world, () =>
        {
            world.Record("restart");
            return restart ?? new AppHostRestartCapture(0, "0=healthy", ["Restarting Antiphon AppHost...", "AppHost restarted - dashboard x, backend healthy, server y."]);
        });
        return (exit, world.Out.ToString()[before..] + world.Error.ToString());
    }

    private static JsonObject State(AppHostWorld world) => (JsonObject)JsonNode.Parse(File.ReadAllText(world.LogPath("apphost-watchdog.state")))!;

    private static void WriteState(AppHostWorld world, JsonObject state) => File.WriteAllText(world.LogPath("apphost-watchdog.state"), state.ToJsonString());

    private static string Round(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    [Test]
    public void Maintenance_skips_after_observing_and_clears_the_dead_launch_proof()
    {
        using var world = new AppHostWorld();
        Down(world);
        File.WriteAllText(world.LogPath("apphost.down-on-purpose"), "down-on-purpose");
        WriteState(world, new JsonObject { ["deadLaunch"] = new JsonObject { ["Identity"] = "1 x", ["Count"] = 1 } });
        var r = Fire(world);
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("[watchdog-apphost] [INFO] skip: intentional AppHost maintenance");
        State(world)["deadLaunch"].ShouldBeNull();
        State(world)["downSinceUtc"].ShouldBeNull("deliberate downtime is not an outage");
        world.Count("restart").ShouldBe(0);
        File.ReadAllText(world.LogPath("watchdog-apphost.log")).ShouldContain("skip: intentional AppHost maintenance");
    }

    [Test]
    public void A_healthy_stack_logs_ok_once_an_hour()
    {
        using var world = new AppHostWorld();
        Up(world);
        var first = Fire(world);
        first.Text.ShouldContain("[INFO] OK health=200 client=200");
        State(world)["lastOkUtc"]!.GetValue<string>().ShouldBe(Round(world.UtcNow));
        var second = Fire(world);
        second.Text.ShouldContain("fire beginning (PID 424242)");
        second.Text.ShouldNotContain("OK health=");
        world.Clock.Advance(TimeSpan.FromHours(1));
        Fire(world).Text.ShouldContain("OK health=200 client=200");
        world.Count("restart").ShouldBe(0);
    }

    [Test]
    public void A_held_launch_lock_or_running_logon_task_skips_the_fire()
    {
        using var world = new AppHostWorld();
        Down(world);
        world.AlwaysAlive.Add(5150);
        world.WriteLock("apphost.launch.lock", 5150, world.UtcNow.AddMinutes(-1));
        Fire(world).Text.ShouldContain("skip: launch in flight - launch lock held by PID 5150");
        File.Delete(world.LogPath("apphost.launch.lock"));
        world.LogonTaskRunning = true;
        Fire(world).Text.ShouldContain("skip: launch in flight - AppHost logon task is Running");
        world.LogonTaskRunning = null;
        Fire(world).Text.ShouldContain("skip: launch in flight - AppHost logon task inventory unknown");
        world.Count("restart").ShouldBe(0);
    }

    [Test]
    public void Three_down_rounds_restart_in_process_and_stamp_the_restart()
    {
        using var world = new AppHostWorld();
        Down(world);
        var r = Fire(world);
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("[WARN] probe round 1/3 failed: health=FAIL " + Refused + " client=FAIL " + Refused);
        r.Text.ShouldContain("[WARN] all 3 probe rounds failed");
        r.Text.ShouldContain("[WARN] restarting AppHost via restart-apphost");
        r.Text.ShouldContain("[INFO] restart-apphost: AppHost restarted - dashboard x, backend healthy, server y.");
        r.Text.ShouldContain("[INFO] restart-apphost exited 0=healthy; stamped restart in " + world.LogPath("apphost-watchdog.state"));
        world.Trace.Count(row => row == "sleep 15").ShouldBe(2);
        world.Trace.ShouldContain("run docker info");
        var restarts = State(world)["restartsUtc"]!.AsArray();
        restarts.Count.ShouldBe(1);
        restarts[0]!.GetValue<string>().ShouldBe(Round(world.UtcNow));
    }

    [Test]
    public void A_refused_restart_is_not_stamped()
    {
        using var world = new AppHostWorld();
        Down(world);
        var r = Fire(world, new AppHostRestartCapture(3, AppHostProbes.ExitName(3), ["REFUSED: another restart is in flight - x"]));
        r.Text.ShouldContain("restart-apphost REFUSED (3=refused (already unstamped), nothing killed) - not stamping a restart");
        State(world)["restartsUtc"]!.AsArray().Count.ShouldBe(0);
    }

    [Test]
    public void Cooldown_and_the_flap_cap_skip_the_restart()
    {
        using (var world = new AppHostWorld())
        {
            Down(world);
            WriteState(world, new JsonObject { ["restartsUtc"] = new JsonArray(Round(world.UtcNow.AddMinutes(-5))) });
            Fire(world).Text.ShouldContain("[INFO] skip: cooldown (5.5 min since last restart, need 15)");
            world.Count("restart").ShouldBe(0);
        }
        using (var world = new AppHostWorld())
        {
            Down(world);
            WriteState(world, new JsonObject { ["restartsUtc"] = new JsonArray(Round(world.UtcNow.AddMinutes(-20)), Round(world.UtcNow.AddMinutes(-30)), Round(world.UtcNow.AddMinutes(-40))) });
            Fire(world).Text.ShouldContain("[ERROR] flap cap: 3 restarts in the last 60 min - not restarting until the window clears");
            world.Count("restart").ShouldBe(0);
        }
    }

    [Test]
    public void A_slow_health_with_the_client_up_is_not_a_down_round()
    {
        using var world = new AppHostWorld();
        Stack(world, new AppHostHttpAnswer(null, null, "The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing."), new AppHostHttpAnswer(200, "<html>", null));
        var r = Fire(world);
        r.Text.ShouldContain("[WARN] health slow, client up - not counting as down (health=FAIL The request was canceled");
        world.Trace.ShouldNotContain("run docker info");
        world.Count("restart").ShouldBe(0);
    }

    [Test]
    public void Docker_down_probe_only_and_the_lease_skip_without_restarting()
    {
        using (var world = new AppHostWorld())
        {
            Down(world);
            world.Commands = (file, args) => new AppHostCommandResult(false, 1, "error during connect");
            Fire(world).Text.ShouldContain("[WARN] skip: Docker is not responsive (not counting against flap budget)");
            world.Count("restart").ShouldBe(0);
        }
        using (var world = new AppHostWorld())
        {
            Down(world);
            var r = Fire(world, arguments: "--probe-only");
            r.Text.ShouldContain("fire beginning (PID 424242; ProbeOnly)");
            r.Text.ShouldContain("[INFO] ProbeOnly: would restart via restart-apphost");
            world.Count("restart").ShouldBe(0);
        }
        using (var world = new AppHostWorld())
        {
            Down(world);
            using var lease = new FileStream(world.LogPath("apphost-watchdog.fire.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Fire(world).Text.ShouldContain("[INFO] skip: another watchdog fire owns observation");
            world.Count("restart").ShouldBe(0);
        }
    }

    [Test]
    public void An_unreadable_state_stops_the_fire_and_keeps_the_file()
    {
        using var world = new AppHostWorld();
        Down(world);
        File.WriteAllText(world.LogPath("apphost-watchdog.state"), "{not json");
        var r = Fire(world);
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("Watchdog state unreadable; preserving outage and restart history for repair.");
        File.ReadAllText(world.LogPath("apphost-watchdog.state")).ShouldBe("{not json");
    }

    [Test]
    public void A_stale_git_index_lock_is_logged()
    {
        using var world = new AppHostWorld();
        Up(world);
        var lockPath = world.PathOf(".git", "index.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllBytes(lockPath, []);
        File.SetLastWriteTimeUtc(lockPath, world.UtcNow.AddHours(-2));
        world.IndexLockPath = lockPath;
        Fire(world).Text.ShouldContain("[WARN] NOTE: git index lock (stale) present at " + lockPath);
    }

    // ---- CARD-1527: outage sinks ----

    [Test]
    public void A_three_minute_outage_alerts_every_sink_once_and_recovery_follows()
    {
        using var world = new AppHostWorld();
        Down(world);
        var t0 = world.UtcNow;
        WriteState(world, new JsonObject { ["downSinceUtc"] = Round(t0.AddMinutes(-4)), ["outageId"] = "apphost:fixture", ["outageAlerted"] = false });
        Fire(world);
        world.Events.Count.ShouldBe(1);
        world.Events[0].Information.ShouldBeFalse();
        world.Events[0].Message.ShouldBe("AppHost down; outage began " + Round(t0.AddMinutes(-4)) + "; observed " + Round(t0) + "; event apphost:fixture:down");
        var journal = File.ReadAllLines(world.LogPath("apphost-outage-events.jsonl"));
        journal.Length.ShouldBe(1);
        var sent = (JsonObject)JsonNode.Parse(journal[0])!;
        (sent["Id"]!.GetValue<string>(), sent["Kind"]!.GetValue<string>()).ShouldBe(("apphost:fixture:down", "down"));
        (sent["EventLogAttempted"]!.GetValue<bool>(), sent["EventLogWritten"]!.GetValue<bool>(), sent["WebhookConfigured"]!.GetValue<bool>(), sent["WebhookComplete"]!.GetValue<bool>())
            .ShouldBe((true, true, false, true));
        State(world)["pendingAlerts"]!.AsArray().Count.ShouldBe(0);
        State(world)["outageAlerted"]!.GetValue<bool>().ShouldBeTrue();
        var marker = (JsonObject)JsonNode.Parse(File.ReadAllText(world.LogPath("apphost-down.alert")))!;
        marker["downSinceUtc"]!.GetValue<string>().ShouldBe(Round(t0.AddMinutes(-4)));
        marker["lastRestartAttemptUtc"]!.GetValue<string>().ShouldBe(Round(world.UtcNow));

        Up(world);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        Fire(world);
        world.Events.Count.ShouldBe(2);
        world.Events[1].Information.ShouldBeTrue();
        world.Events[1].Message.ShouldEndWith("; event apphost:fixture:recovery");
        File.ReadAllLines(world.LogPath("apphost-outage-events.jsonl")).Length.ShouldBe(2);
        File.Exists(world.LogPath("apphost-down.alert")).ShouldBeFalse("health removes the marker");
        State(world)["downSinceUtc"].ShouldBeNull();
    }

    [Test]
    public void A_configured_webhook_is_retried_until_it_answers_and_only_over_https()
    {
        using var world = new AppHostWorld();
        Stack(world, new AppHostHttpAnswer(null, null, Refused), new AppHostHttpAnswer(200, "<html>", null));
        WriteState(world, new JsonObject { ["downSinceUtc"] = Round(world.UtcNow.AddMinutes(-4)), ["outageId"] = "apphost:hook", ["outageAlerted"] = false });
        world.WebhookOk = false;
        var hook = (Func<string, string?>)(name => name == "ANTIPHON_WATCHDOG_ALERT_WEBHOOK" ? "https://example.invalid/hook" : null);
        world.LogonTaskRunning = true;
        var r = Fire(world, variables: hook);
        r.Text.ShouldContain("[WARN] optional outage webhook pending (apphost:hook:down); local sinks were attempted independently");
        world.Webhooks.Count.ShouldBe(1);
        world.Webhooks[0].Key.ShouldBe("apphost:hook:down");
        world.Webhooks[0].Body.ShouldStartWith("{\"Id\":\"apphost:hook:down\",\"Kind\":\"down\",\"DownSinceUtc\":");
        world.Webhooks[0].Body.ShouldNotContain("EventLog");
        State(world)["pendingAlerts"]!.AsArray().Count.ShouldBe(1);
        world.Events.Count.ShouldBe(1);

        world.WebhookOk = true;
        Fire(world, variables: hook);
        world.Events.Count.ShouldBe(1, "the event log entry is attempted once");
        State(world)["pendingAlerts"]!.AsArray().Count.ShouldBe(0);
        File.ReadAllLines(world.LogPath("apphost-outage-events.jsonl")).Length.ShouldBe(1);

        using var plain = new AppHostWorld();
        Down(plain);
        plain.LogonTaskRunning = true;
        WriteState(plain, new JsonObject { ["downSinceUtc"] = Round(plain.UtcNow.AddMinutes(-4)), ["outageId"] = "apphost:http", ["outageAlerted"] = false });
        Fire(plain, variables: name => name == "ANTIPHON_WATCHDOG_ALERT_WEBHOOK" ? "http://example.invalid/hook" : null);
        plain.Webhooks.Count.ShouldBe(0, "only https is sent");
        State(plain)["pendingAlerts"]!.AsArray().Count.ShouldBe(1);
    }

    [Test]
    public void A_dead_restart_generation_proven_on_two_fires_is_admitted()
    {
        using var world = new AppHostWorld();
        Down(world);
        world.WriteLock("apphost.restart.lock", 777, world.UtcNow.AddMinutes(-2));
        var first = Fire(world);
        first.Text.ShouldContain("skip: launch in flight - restart lock stamp ");
        first.Text.ShouldContain("holder PID 777 exited; child may still be launching");
        State(world)["deadLaunch"]!["Count"]!.GetValue<int>().ShouldBe(1);
        world.Count("restart").ShouldBe(0);

        world.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = Fire(world);
        second.Text.ShouldContain("[WARN] dead launch proven on two fires; restart admission must revalidate");
        world.Count("restart").ShouldBe(1);
    }

    // ---- the logon auto-start ----

    private static (int Exit, string Text) Autostart(AppHostWorld world, Func<string, string?>? variables = null, params string[] arguments) =>
        Autostart(world, (options, host) =>
        {
            world.Record("dev-aspire" + (options.NoBrowser ? " --no-browser" : "") + (options.NoBuild ? " --no-build" : ""));
            host.Out.WriteLine("dev-aspire output line");
            return 0;
        }, variables, arguments);

    private static (int Exit, string Text) Autostart(AppHostWorld world, DevAspireRunner devAspire, Func<string, string?>? variables = null, params string[] arguments)
    {
        var exit = AppHostAutostart.Run(AppHostAutostartOptions.Parse(arguments, world.Root), world.Host(variables), world, devAspire);
        return (exit, world.Out.ToString() + world.Error.ToString());
    }

    [Test]
    public void Autostart_never_clobbers_a_running_apphost()
    {
        using var world = new AppHostWorld();
        world.Owners[17202] = [1234];
        var r = Autostart(world);
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("[autostart-apphost] port 17202 already listening - AppHost is up, nothing to do.");
        world.Trace.ShouldNotContain(row => row.StartsWith("run docker", StringComparison.Ordinal));
        File.ReadAllText(world.LogPath("autostart-apphost.log")).ShouldContain("logon auto-start beginning (PID 424242)");
    }

    [Test]
    public void Autostart_waits_for_docker_and_gives_up_after_the_timeout()
    {
        using var world = new AppHostWorld();
        world.Commands = (file, args) => new AppHostCommandResult(false, 1, "");
        var r = Autostart(world, arguments: ["--docker-timeout-sec", "30"]);
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("ERROR: Docker Desktop did not become responsive within 30s - aborting.");
        world.Trace.Count(row => row == "run docker info").ShouldBe(3);
        r.Text.ShouldContain("\"operation\":\"docker-info\",\"elapsedMs\":30000,\"outcome\":\"failed-or-canceled\"");
    }

    [Test]
    public void Autostart_ensures_postgres_launches_dev_aspire_and_waits_for_health()
    {
        using var world = new AppHostWorld();
        world.Commands = (file, args) => args.Contains("ps") ? new AppHostCommandResult(false, 0, "Up 2 minutes (healthy)\n") : new AppHostCommandResult(false, 0, "");
        world.HealthStatus = 200;
        var r = Autostart(world, arguments: "--no-build");
        r.Exit.ShouldBe(0, r.Text);
        r.Text.ShouldContain("Postgres container ensured.");
        r.Text.ShouldContain("Postgres status: Up 2 minutes (healthy)");
        r.Text.ShouldContain("launching dev-aspire --no-browser --no-build...");
        // Unit 37: dev-aspire runs in this process, its output in the log (not on the console), as the child's was.
        world.Count("dev-aspire --no-browser --no-build").ShouldBe(1);
        world.IndexOf("dev-aspire").ShouldBeGreaterThan(world.IndexOf("run docker compose"));
        File.ReadAllText(world.LogPath("autostart-apphost.log")).ShouldContain("dev-aspire output line");
        r.Text.ShouldNotContain("dev-aspire output line");
        r.Text.ShouldContain("OK - backend healthy on :17202, dashboard http://localhost:17205");
        world.Trace.ShouldContain("run docker compose -f " + Path.Combine(world.Root, "docker-compose.dev.yml") + " up -d");
    }

    [Test]
    public void Autostart_with_the_postgres_opt_out_starts_the_other_services_only()
    {
        using var world = new AppHostWorld();
        File.WriteAllText(world.PathOf("docker-compose.dev.yml"), "name: antiphon\nservices:\n  postgres:\n    image: postgres\n  redpanda:\n    image: redpanda\nvolumes:\n  data:\n");
        world.HealthStatus = 0;
        var r = Autostart(world, name => name == "ANTIPHON_SKIP_DEV_POSTGRES" ? " 1 " : null, "--health-timeout-sec", "10");
        r.Exit.ShouldBe(1, r.Text);
        r.Text.ShouldContain("ANTIPHON_SKIP_DEV_POSTGRES=1: desktop Postgres bring-up skipped (database moved off-host); compose up -d --no-deps redpanda only");
        r.Text.ShouldContain("Postgres status: desktop container not checked (ANTIPHON_SKIP_DEV_POSTGRES=1).");
        world.Trace.ShouldContain("run docker compose -f " + world.PathOf("docker-compose.dev.yml") + " up -d --no-deps redpanda");
        r.Text.ShouldContain("ERROR: backend not healthy within 10s - check logs/apphost.log");
    }

    [Test]
    public void Dev_compose_arguments_follow_the_opt_out()
    {
        const string compose = "services:\n  postgres:\n    image: p\n  redpanda:\n    image: r\n  # comment\n  console:\n    image: c\nnetworks:\n  default:\n";
        DevCompose.ServiceNames(compose).ToArray().ShouldBe(["postgres", "redpanda", "console"]);
        DevCompose.UpArguments("f.yml", compose, skipPostgres: false).ToArray().ShouldBe(["compose", "-f", "f.yml", "up", "-d"]);
        DevCompose.UpArguments("f.yml", compose, skipPostgres: true).ToArray().ShouldBe(["compose", "-f", "f.yml", "up", "-d", "--no-deps", "redpanda", "console"]);
        DevCompose.UpArguments("f.yml", "services:\n  postgres:\n", skipPostgres: true).ShouldBeEmpty();
        (DevCompose.Skipped(null), DevCompose.Skipped("0"), DevCompose.Skipped(" 1")).ShouldBe((false, false, true));
    }
}
