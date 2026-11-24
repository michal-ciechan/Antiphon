using System.Globalization;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the AppHost locks, probes and git index lock in
/// process (AppHostCommon.cs). The rows were the deleted drivers' cases: scripts/test-apphost-lock-age.ps1 (CARD-0152 T1..T7,
/// CARD-0310 T8..T10 and T-keep), scripts/test-apphost-probe-class.ps1 (CARD-0310 C1..C6b; C6 and C6b captured a stub
/// restart child and now capture restart-apphost run in process) and scripts/test-apphost-git-index-lock.ps1 (CARD-0543
/// T1..T6; T5's text check that three scripts call the reader is now behaviour: the restart prints the note, the watchdog
/// logs a stale one, verify-dev-stack reports it).
/// </summary>
[Category("Unit")]
public sealed class AppHostCommonTests
{
    private static string Round(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    // ---- CARD-0152 / CARD-0310: lock age is UTC against UTC; a dead holder with a fresh stamp is in flight ----

    [Test]
    public void Lock_age_is_utc_against_utc_and_a_75_minute_stamp_is_stale()
    {
        using var world = new AppHostWorld();
        var now = world.UtcNow;
        var iso = Round(now.AddMinutes(-75));
        iso.ShouldEndWith("Z");
        var path = world.WriteLock("apphost.restart.lock", world.CurrentPid, now.AddMinutes(-75));
        world.AlwaysAlive.Add(world.CurrentPid);
        var info = AppHostLocks.Read(world, path, now);
        info.Readable.ShouldBeTrue();
        info.StampUtc!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        info.AgeMinutes!.Value.ShouldBe(75, 0.05);
        info.StampRaw.ShouldBe(iso);
        AppHostLocks.Active(world, path, 15, "restart lock", now).ShouldBeNull("T1 75-min-old lock is stale at MaxAgeMinutes=15");

        world.WriteLock("apphost.restart.lock", world.CurrentPid, now.AddMinutes(-45));
        AppHostLocks.Read(world, path, now).AgeMinutes!.Value.ShouldBe(45, 0.05);
        AppHostLocks.Active(world, path, 15, "restart lock", now).ShouldBeNull("T2 45-min-old lock is stale (fresh under the UTC-vs-local bug)");
    }

    [Test]
    public void A_live_five_minute_lock_is_active_and_its_reason_names_stamp_and_age()
    {
        using var world = new AppHostWorld();
        var now = world.UtcNow;
        var iso = Round(now.AddMinutes(-5));
        world.AlwaysAlive.Add(world.CurrentPid);
        var path = world.WriteLock("apphost.restart.lock", world.CurrentPid, now.AddMinutes(-5));
        AppHostLocks.Read(world, path, now).AgeMinutes!.Value.ShouldBe(5, 0.05);
        var held = AppHostLocks.Active(world, path, 15, "restart lock", now);
        held.ShouldBe("restart lock held by PID " + world.CurrentPid + " (stamp " + iso + ", 5.0 min old; " + path + ")");
        held.ShouldNotContain("exited");
    }

    [Test]
    public void Stamps_with_an_offset_or_none_are_utc()
    {
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        var z = AppHostStamp.ToUtc(Round(now.AddMinutes(-75)))!.Value;
        (z.Kind, z.Hour, z.Minute).ShouldBe((DateTimeKind.Utc, 10, 45), "T4 the Z clock face stays UTC");
        var plusOne = new DateTimeOffset(now.AddMinutes(-75), TimeSpan.Zero).ToOffset(TimeSpan.FromHours(1)).ToString("o", CultureInfo.InvariantCulture);
        AppHostStamp.AgeMinutes(AppHostStamp.ToUtc(plusOne)!.Value, now).ShouldBe(75, 0.05, "T4 +01:00");
        var bare = now.AddMinutes(-75).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        bare.ShouldNotEndWith("Z");
        AppHostStamp.AgeMinutes(AppHostStamp.ToUtc(bare)!.Value, now).ShouldBe(75, 0.05, "T5 offset-less is UTC, not local");
        AppHostStamp.ToUtc("not a stamp").ShouldBeNull();
        AppHostStamp.ToUtc("  ").ShouldBeNull();
    }

    [Test]
    public void T6_take_writes_pid_and_round_trip_utc()
    {
        using var world = new AppHostWorld();
        world.AlwaysAlive.Add(world.CurrentPid);
        var path = world.LogPath("apphost.restart.lock");
        var taken = AppHostLocks.Take(world, path, 15, "restart lock", world.UtcNow);
        try
        {
            taken.Acquired.ShouldBeTrue(taken.Reason);
            var written = ReadShared(path);
            written.ShouldMatch(@"^\d+ \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}.*Z$");
            written.ShouldBe(world.CurrentPid + " " + Round(world.UtcNow));
            var live = AppHostLocks.Read(world, path, world.UtcNow);
            live.Readable.ShouldBeTrue("a live holder's lock is read with read-write sharing");
            live.AgeMinutes!.Value.ShouldBe(0, 1);
            AppHostLocks.Take(world, path, 15, "restart lock", world.UtcNow).Acquired.ShouldBeFalse("a held lock refuses a second taker");
        }
        finally
        {
            taken.Release(keepFile: false);
        }
        File.Exists(path).ShouldBeFalse("release removes the file that still holds this lock's line");
    }

    [Test]
    public void T7_format_helpers_name_the_raw_stamp_and_the_age()
    {
        using var world = new AppHostWorld();
        var iso = Round(world.UtcNow.AddMinutes(-75));
        var path = world.WriteLock("apphost.restart.lock", 1, world.UtcNow.AddMinutes(-75));
        var info = AppHostLocks.Read(world, path, world.UtcNow);
        (info.StampText, info.AgeText).ShouldBe((iso, "75.0 min old"));
    }

    [Test]
    public void T8_to_T10_a_dead_holder_with_a_fresh_stamp_is_in_flight_and_never_stolen()
    {
        using var world = new AppHostWorld();
        var now = world.UtcNow;
        var iso5 = Round(now.AddMinutes(-5));
        var dead5 = world.WriteLock("apphost.restart.lock", 999999, now.AddMinutes(-5));
        var held = AppHostLocks.Active(world, dead5, 15, "restart lock", now);
        held.ShouldBe("restart lock stamp " + iso5 + " is 5.0 min old; holder PID 999999 exited; child may still be launching (" + dead5 + ")");

        world.WriteLock("apphost.restart.lock", 999999, now.AddMinutes(-20));
        AppHostLocks.Active(world, dead5, 15, "restart lock", now).ShouldBeNull("T9 20-min-old dead lock is litter");

        world.WriteLock("apphost.restart.lock", 999999, now.AddMinutes(-5));
        var stolen = AppHostLocks.Take(world, dead5, 15, "restart lock", now);
        stolen.Acquired.ShouldBeFalse("T10 a 5-min-old dead leftover is not stolen");
        stolen.Reason!.ShouldContain("exited");
        File.Exists(dead5).ShouldBeTrue();

        world.WriteLock("apphost.restart.lock", 999999, now.AddMinutes(-20));
        var replaced = AppHostLocks.Take(world, dead5, 15, "restart lock", now);
        replaced.Acquired.ShouldBeTrue("a stale leftover is cleared once and retaken");
        replaced.Release(false);
    }

    [Test]
    public void T_keep_a_kept_lock_stays_active_after_release()
    {
        using var world = new AppHostWorld();
        world.AlwaysAlive.Add(world.CurrentPid);
        var path = world.LogPath("keep.lock");
        var taken = AppHostLocks.Take(world, path, 15, "restart lock", world.UtcNow);
        taken.Acquired.ShouldBeTrue();
        taken.Release(keepFile: true);
        File.Exists(path).ShouldBeTrue();
        AppHostLocks.Active(world, path, 15, "restart lock", world.UtcNow).ShouldNotBeNull();
    }

    [Test]
    public void An_unreadable_lock_is_judged_by_its_file_time()
    {
        using var world = new AppHostWorld();
        var path = world.LogPath("apphost.launch.lock");
        File.WriteAllText(path, "garbage");
        File.SetLastWriteTimeUtc(path, world.UtcNow.AddMinutes(-3));
        var reason = AppHostLocks.Active(world, path, 15, "launch lock", world.UtcNow);
        reason.ShouldStartWith("launch lock present but unreadable (stamp ");
        reason.ShouldContain("3.0 min old");
    }

    // ---- CARD-1527: the dead-launch decision ----

    [Test]
    public void A_dead_launch_needs_two_distinct_fires_within_five_minutes()
    {
        using var world = new AppHostWorld();
        var now = world.UtcNow;
        var path = world.WriteLock("apphost.restart.lock", 777, now.AddMinutes(-2));
        var info = AppHostLocks.Read(world, path, now);
        info.HolderState.ShouldBe("Dead");
        var first = AppHostDeadLaunch.Resolve(info, "Absent", true, false, null, "fire-1", now, revalidate: false);
        (first.Reclaim, first.Proof.Count, first.Proof.Identity).ShouldBe((false, 1, info.Identity));
        var again = AppHostDeadLaunch.Resolve(info, "Absent", true, false, first.Proof, "fire-1", now.AddMinutes(1), revalidate: false);
        again.Proof.Count.ShouldBe(1, "the same fire counts once");
        var second = AppHostDeadLaunch.Resolve(info, "Absent", true, false, first.Proof, "fire-2", now.AddMinutes(2), revalidate: false);
        (second.Reclaim, second.Proof.Count).ShouldBe((true, 2));
        var late = AppHostDeadLaunch.Resolve(info, "Absent", true, false, first.Proof, "fire-3", now.AddMinutes(6), revalidate: false);
        (late.Reclaim, late.Proof.Count).ShouldBe((false, 1), "more than five minutes apart starts again");
        foreach (var (activity, dead, blocked) in new[] { ("Unknown", true, false), ("Present", true, false), ("Absent", false, false), ("Absent", true, true) })
        {
            var veto = AppHostDeadLaunch.Resolve(info, activity, dead, blocked, second.Proof, "fire-4", now.AddMinutes(3), revalidate: false);
            (veto.Reclaim, veto.Proof.Count, veto.Proof.Identity).ShouldBe((false, 0, (string?)null), activity + " " + dead + " " + blocked);
        }
        second.Proof.ToJson().ToJsonString().ShouldStartWith("{\"Identity\":\"777 ");
        AppHostDeadLaunchProof.From(second.Proof.ToJson())!.Count.ShouldBe(2);
    }

    [Test]
    public void Launch_activity_reads_listeners_the_task_and_the_wrapper_command_line()
    {
        using var world = new AppHostWorld();
        var info = AppHostLocks.Read(world, world.WriteLock("apphost.restart.lock", 777, world.UtcNow.AddMinutes(-2)), world.UtcNow);
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Absent");
        world.Listening.Add(17205);
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Present");
        world.Listening.Clear();
        world.LogonTaskRunning = null;
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Unknown");
        world.LogonTaskRunning = true;
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Present");
        world.LogonTaskRunning = false;
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("Set-Location '" + Path.Combine(world.Root, "Antiphon.AppHost") + "'"));
        world.CensusRows.Add(new AppHostProcessRow(5000, "pwsh.exe", "pwsh -NonInteractive -NoLogo -EncodedCommand " + encoded, null));
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Present", "the decoded wrapper names this checkout");
        world.CensusRows.Clear();
        world.CensusRows.Add(new AppHostProcessRow(5001, "dcpctrl.exe", "dcpctrl --monitor 12", null));
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Unknown", "an unowned DCP vetoes");
        world.CensusRows.Clear();
        world.CensusRows.Add(new AppHostProcessRow(5002, "pwsh.exe", "", null));
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Unknown", "an unreadable command line");
        world.CensusRows.Clear();
        world.CensusRows.Add(new AppHostProcessRow(world.CurrentPid, "dotnet.exe", "dotnet exec " + world.Root + "\\bin-launcher\\run\\Antiphon.Scripts.dll watchdog-apphost", null));
        world.OwnChain.Add(6000);
        world.CensusRows.Add(new AppHostProcessRow(6000, "powershell.exe", "powershell -File " + world.Root + "\\scripts\\watchdog-apphost.ps1", null));
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Absent", "this process and its launcher chain are not a launch");
        File.WriteAllText(world.LogPath("apphost.pid"), "7000");
        world.CensusRows.Add(new AppHostProcessRow(7000, "pwsh.exe", "pwsh -NoProfile", world.UtcNow.AddMinutes(-10)));
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Present", "the recorded wrapper started before the stamp");
        world.CensusFails = true;
        AppHostDeadLaunch.Activity(world, world.Root, info).ShouldBe("Unknown");
    }

    // ---- CARD-0310: probe classification and the named restart exits ----

    [Test]
    public void Probe_rounds_classify_up_slow_and_down()
    {
        const string httpTimeout = "The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing.";
        const string ps51Timeout = "The operation has timed out.";
        const string refusedHealth = "No connection could be made because the target machine actively refused it. (localhost:17202)";
        const string refusedClient = "No connection could be made because the target machine actively refused it. (localhost:17203)";
        var c1 = AppHostProbes.Classify(false, httpTimeout, null, true, null, 200);
        (c1.Kind, c1.RestartWorthy).ShouldBe(("Slow", false));
        c1.Summary.ShouldContain("client=200");
        c1.Summary.ShouldContain("health=FAIL");
        AppHostProbes.Classify(false, ps51Timeout, null, true, null, 200).Kind.ShouldBe("Slow");
        var c2 = AppHostProbes.Classify(false, refusedHealth, null, false, refusedClient, null);
        (c2.Kind, c2.RestartWorthy).ShouldBe(("Down", true));
        var c3 = AppHostProbes.Classify(true, null, 200, true, null, 200);
        (c3.Kind, c3.RestartWorthy, c3.Summary).ShouldBe(("Up", false, "health=200 client=200"));
        var c4 = AppHostProbes.Classify(false, refusedHealth, null, true, null, 200);
        (c4.Kind, c4.RestartWorthy).ShouldBe(("Down", true));
        AppHostProbes.Classify(false, "Response status code does not indicate success: 503.", 503, true, null, 200).Summary.ShouldBe("health=503 client=200");
    }

    [Test]
    public void Restart_exits_are_named_and_the_lock_clock_is_15_minutes()
    {
        AppHost.LockMaxAgeMinutes.ShouldBe(15);
        new[] { 0, 1, 3, 4, 5, 7 }.Select(AppHostProbes.ExitName).ToArray().ShouldBe(
            ["0=healthy", "1=timeout/build", "3=refused (already unstamped)", "4=DCP dependency timeout", "5=server build unverified", "7"]);
    }

    [Test]
    public void C6_a_captured_restart_carries_its_lines_and_named_exit()
    {
        using var world = new AppHostWorld();
        File.WriteAllText(world.LogPath("apphost.pid"), "111");
        world.OnLaunch = x => File.WriteAllText(x.LogPath("apphost.log"), "fail: DcpDependencyCheck timed out; podman: not found\n");
        var dcp = AppHostWatchdog.CaptureRestart(world.Root, world.Host(), world);
        (dcp.ExitCode, dcp.ExitName).ShouldBe((4, "4=DCP dependency timeout"));
        var tail = string.Join("\n", dcp.Tail);
        tail.ShouldContain("TIMED OUT");
        tail.ShouldContain("podman");
        dcp.Tail.Count.ShouldBeLessThanOrEqualTo(40);

        using var unverified = new AppHostWorld { VersionSha = new string('e', 40) };
        File.WriteAllText(unverified.LogPath("apphost.pid"), "111");
        var five = AppHostWatchdog.CaptureRestart(unverified.Root, unverified.Host(), unverified);
        (five.ExitCode, five.ExitName).ShouldBe((5, "5=server build unverified"));
        five.Tail.ShouldContain("REFUSED: server build unverified (exit 5).");
    }

    [Test]
    public void Loaded_identity_reads_the_version_sha()
    {
        AppHostProbes.LoadedIdentity(new AppHostHttpAnswer(200, "{\"version\":\"" + AppHostWorld.Head + "\"}", null)).ShouldBe(new AppHostLoadedIdentity(true, AppHostWorld.Head, null));
        AppHostProbes.LoadedIdentity(new AppHostHttpAnswer(200, "{\"version\":\"unknown\"}", null)).ShouldBe(new AppHostLoadedIdentity(false, "unknown", "malformed"));
        AppHostProbes.LoadedIdentity(new AppHostHttpAnswer(200, "not json", null)).Reason.ShouldBe("malformed");
        AppHostProbes.LoadedIdentity(new AppHostHttpAnswer(503, null, "down")).Reason.ShouldBe("unavailable");
        AppHostProbes.LoadedIdentity(null).Reason.ShouldBe("unavailable");
        AppHostProbes.IsSha(new string('A', 64)).ShouldBeTrue();
        AppHostProbes.IsSha(new string('a', 39)).ShouldBeFalse();
    }

    // ---- CARD-0543: the git index lock is reported, never deleted ----

    [Test]
    public void Git_index_lock_is_absent_fresh_or_stale_and_never_deleted()
    {
        using var world = new AppHostWorld();
        AppHostGitIndexLock.Read(world, world.Host(), world.Root).ShouldBeNull("T1 git names no lock");
        var lockPath = world.PathOf(".git", "index.lock");
        world.IndexLockPath = lockPath.Replace('\\', '/');
        AppHostGitIndexLock.Read(world, world.Host(), world.Root).ShouldBeNull("T1 named but absent");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllBytes(lockPath, []);
        File.SetLastWriteTimeUtc(lockPath, world.UtcNow.AddSeconds(-30));
        var fresh = AppHostGitIndexLock.Read(world, world.Host(), world.Root)!;
        (fresh.Present, fresh.Stale).ShouldBe((true, false), "T2");
        fresh.Note().ShouldContain("The lock is younger than 300 s");

        File.SetLastWriteTimeUtc(lockPath, world.UtcNow.AddMinutes(-60));
        var stale = AppHostGitIndexLock.Read(world, world.Host(), world.Root)!;
        stale.Stale.ShouldBeTrue("T3 60 min old with no older git process");
        var note = stale.Note();
        note.ShouldStartWith("NOTE: git index lock (stale) present at " + world.IndexLockPath + " (age 1h 0m 0s, 0 bytes). No git process older than the lock");
        note.ShouldContain("Remove-Item '" + world.IndexLockPath + "'");

        world.Named.Add(new AppHostProcessRow(321, "git", null, world.UtcNow.AddHours(-2)));
        var held = AppHostGitIndexLock.Read(world, world.Host(), world.Root)!;
        (held.Stale, held.HolderPid).ShouldBe((false, 321));
        held.Note().ShouldContain("A git process started before the lock is still running (PID 321, started " + Round(world.UtcNow.AddHours(-2)) + ")");
        File.Exists(lockPath).ShouldBeTrue("T6 the reader never deletes");
    }

    [Test]
    public void T5_the_restart_prints_the_index_lock_note()
    {
        using var world = new AppHostWorld();
        File.WriteAllText(world.LogPath("apphost.pid"), "111");
        var lockPath = world.PathOf(".git", "index.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllBytes(lockPath, [1, 2]);
        File.SetLastWriteTimeUtc(lockPath, world.UtcNow.AddMinutes(-90));
        world.IndexLockPath = lockPath;
        var exit = AppHostRestart.Run(AppHostRestartOptions.Parse(["--timeout-sec", "8"], world.Root), world.Host(), world);
        exit.ShouldBe(0, world.Out.ToString());
        world.Out.ToString().ShouldContain("NOTE: git index lock (stale) present at " + lockPath + " (age 1h 30m 0s, 2 bytes).");
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
