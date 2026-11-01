using System.Text.Json;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

// CARD-1241: files outside the write roots are stamped before the build slot, re-checked once it is
// granted and verified after its release; inside the slot the build stamps its write roots and what
// the ledger does not hold.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointConfinementLedgerTests : CheckpointTestBase
{
    private const int IgnoredFiles = 2_000;
    private static readonly string[] Arguments = ["build", "App/App.csproj", "--property:OutputPath=bin-probe/"];
    private static readonly string Sha = new('a', 40);

    [Test]
    public void fixture_text_is_normalized_before_git_can_stage_it()
    {
        var root = TempDir();
        Write(root, "App/App.csproj", "<Project>\r\n</Project>\r\n");
        File.ReadAllText(Path.Combine(root, "App", "App.csproj"))
            .ShouldBe("<Project>\n</Project>\n");
    }

    [Test]
    [Timeout(300_000)]
    public Task ignored_tree_outside_write_roots_is_stamped_before_the_slot_not_inside_it(CancellationToken cancellationToken)
    {
        // red: Prepare ignores the ledger and stamps the 2,000-file ignored tree inside the slot again.
        var root = BeginRepo();
        var ledger = Capture(root);
        ledger.Files.Keys.Count(path => path.StartsWith("node_modules/", StringComparison.Ordinal)).ShouldBe(IgnoredFiles);
        ledger.Files.ShouldContainKey("App/Marker.cs");
        ledger.Files.ShouldContainKey("notes.txt");
        ledger.Files.ShouldNotContainKey("App/obj/stale.txt");

        var inSlot = Prepare(root, ledger, cancellationToken);
        inSlot.Untracked.Keys.ShouldNotContain(path => path.StartsWith("node_modules/", StringComparison.Ordinal));
        inSlot.Untracked.ShouldNotContainKey("notes.txt");
        inSlot.Untracked.ShouldContainKey("App/obj/stale.txt");
        inSlot.Content.ShouldBeEmpty();

        // Without a ledger the build keeps today's full stamp, so the fixture tree is reachable.
        var unledgered = Prepare(root, null, cancellationToken);
        unledgered.Untracked.Keys.Count(path => path.StartsWith("node_modules/", StringComparison.Ordinal)).ShouldBe(IgnoredFiles);
        unledgered.Content.ShouldContainKey("App/Marker.cs");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task tracked_file_under_a_write_root_is_stamped_inside_the_slot_not_in_the_ledger(CancellationToken cancellationToken)
    {
        // red: the ledger takes tracked paths under the write roots too (Review 06ff09f8 D2 scope), or the
        // in-slot capture drops them, so a tracked obj/ file is stamped in neither place or in both.
        var root = BeginRepo();
        Write(root, "App/obj/tracked.txt", "force-added under a write root\n");
        Git(root, "add", "-f", "App/obj/tracked.txt");
        Git(root, "-c", "user.email=card-1241@example.invalid", "-c", "user.name=card-1241",
            "-c", "commit.gpgsign=false", "commit", "-m", "tracked under obj", "--no-verify");
        var ledger = Capture(root);
        ledger.Tracked.ShouldContain("App/obj/tracked.txt");
        ledger.Files.ShouldNotContainKey("App/obj/tracked.txt");
        ledger.Files.ShouldContainKey("App/Marker.cs");

        var inSlot = Prepare(root, ledger, cancellationToken);
        inSlot.Content.Keys.ToArray().ShouldBe(new[] { "App/obj/tracked.txt" });
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task changed_file_under_an_output_root_is_still_refused_inside_the_slot(CancellationToken cancellationToken)
    {
        // red: the in-slot baseline drops write-root files, so the changed obj/ file passes.
        var root = BeginRepo();
        var ledger = Capture(root);
        var baseline = Prepare(root, ledger, cancellationToken);
        File.WriteAllText(Path.Combine(root, "App", "obj", "stale.txt"), "rewritten by the build");

        var settled = BuildConfinement.Settle(baseline);
        settled.Kind.ShouldBe("tampered", settled.Detail);
        settled.Paths.ShouldContain("App/obj/stale.txt");
        settled.Detail.ShouldContain("TAMPERED App/obj/stale.txt", Case.Sensitive);
        BuildConfinement.VerifyLedger(ledger).Kind.ShouldBeNull("the write root is checked inside the slot, not by the ledger");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task untracked_source_file_added_during_the_build_is_still_refused(CancellationToken cancellationToken)
    {
        // red: Settle treats a path absent from the in-slot baseline as covered by the ledger.
        var root = BeginRepo();
        var ledger = Capture(root);
        var baseline = Prepare(root, ledger, cancellationToken);
        File.WriteAllText(Path.Combine(root, "App", "Extra.cs"), "namespace App; public static class Extra { }\n");

        var settled = BuildConfinement.Settle(baseline);
        settled.Kind.ShouldBe("tampered", settled.Detail);
        settled.Paths.ToArray().ShouldBe(new[] { "App/Extra.cs" }, settled.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(120_000)]
    public Task non_output_file_changed_before_slot_release_is_refused()
    {
        // red: the ledger is not re-stamped after release, so ignored, untracked and tracked changes pass.
        var root = BeginRepo();
        var ledger = Capture(root);
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0007.js"), "changed\n");
        File.Delete(Path.Combine(root, "notes.txt"));
        File.AppendAllText(Path.Combine(root, "App", "Marker.cs"), "// changed\n");

        var verified = BuildConfinement.VerifyLedger(ledger);
        verified.Kind.ShouldBe("tampered");
        verified.Paths.ToArray().ShouldBe(new[] { "App/Marker.cs", "node_modules/pkg/f0007.js", "notes.txt" }, verified.Detail);
        verified.Detail.ShouldContain("restore --source=" + ledger.Head + " --staged --worktree -- 'App/Marker.cs'", Case.Sensitive);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public async Task scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release(CancellationToken cancellationToken)
    {
        // red: the ledger is taken inside the lease (capture sees Held) or verified before release (f0002 passes).
        // The build itself writes f0001: a write during the slot wait that moves its stat stamp is refused before launch (next test).
        var root = BeginRepo();
        var slots = new RecordingSlotClient(
            onAcquire: () => { },
            onRelease: () => File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0002.js"), "changed at release\n"));
        var driver = new LedgerDriver(slots, () => File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0001.js"), "changed by the build\n"));
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.LedgerCaptures.ToArray().ShouldBe(new[] { false }, "the ledger is stamped once, before the slot is held");
        driver.BuildLedgers.ToArray().ShouldBe(new[] { true }, "the build receives the ledger");
        exit.ShouldNotBe(0);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        var build = report.Builds.Single();
        build.State.ShouldBe("failed");
        build.SlotReason.ShouldBe("refused: tampered");
        var log = File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"));
        log.ShouldContain("TAMPERED node_modules/pkg/f0001.js", Case.Sensitive);
        log.ShouldContain("TAMPERED node_modules/pkg/f0002.js", Case.Sensitive);
        report.Rows.Single().State.ShouldNotBe("green");
    }

    [Test]
    [Timeout(300_000)]
    public async Task file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch(CancellationToken cancellationToken)
    {
        // red (Review 06ff09f8 D1): A -> B while the build waits for its slot, then the build writes A back; the
        // after-release check compares A with the pre-slot A and the build succeeds over the user's newer B.
        var root = BeginRepo();
        var user = Path.Combine(root, "node_modules", "pkg", "f0003.js");
        var original = File.ReadAllText(user);
        var slots = new RecordingSlotClient(
            onAcquire: () => File.WriteAllText(user, "newer user bytes written during the slot wait\n"),
            onRelease: () => { });
        var driver = new LedgerDriver(slots, () => File.WriteAllText(user, original));
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.LedgerCaptures.ToArray().ShouldBe(new[] { false });
        driver.BuildLedgers.ShouldBeEmpty("the build is refused once the slot is granted, before it starts");
        File.ReadAllText(user).ShouldBe("newer user bytes written during the slot wait\n");
        exit.ShouldNotBe(0);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        var build = report.Builds.Single();
        build.State.ShouldBe("failed");
        build.SlotReason.ShouldBe("refused: tampered");
        var log = File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"));
        log.ShouldContain("TAMPERED node_modules/pkg/f0003.js", Case.Sensitive);
        report.Rows.Single().State.ShouldNotBe("green");
    }

    [NativeLinuxOnly("the statx stamp (inode and ctime) is read on Linux only")]
    [Test]
    [Timeout(300_000)]
    public Task unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted()
    {
        // red: admission hashes each ledger entry again instead of the entries whose stat stamp changed.
        if (!OperatingSystem.IsLinux()) Skip.Test("the statx stamp (inode and ctime) is read on Linux only");
        var root = BeginRepo();
        AgeWorktree(root);
        var ledger = Capture(root);
        ledger.Files.Count.ShouldBeGreaterThan(IgnoredFiles);
        ledger.Stats.Values.ShouldAllBe(stat => stat.HasIdentityAndChangeTime);

        var unchanged = BuildConfinement.AdmitLedger(ledger);
        unchanged.Settlement.Kind.ShouldBeNull(unchanged.Settlement.Detail);
        unchanged.Hashed.ShouldBe(0, "no ledger entry changed, so none is hashed inside the slot");

        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0007.js"), "changed while waiting\n");
        var changed = BuildConfinement.AdmitLedger(ledger);
        changed.Hashed.ShouldBe(1, "only the entry whose stat stamp changed is hashed");
        changed.Settlement.Kind.ShouldBe("tampered");
        changed.Settlement.Paths.ToArray().ShouldBe(new[] { "node_modules/pkg/f0007.js" }, changed.Settlement.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task entry_modified_just_before_the_ledger_is_hashed_when_the_slot_is_granted()
    {
        // red: an entry modified within the racy window is skipped on an equal stat stamp, although a second
        // write in the same timestamp tick would leave size, inode and timestamps equal. The complete stamp keeps
        // each ctime old, so the mtime alone puts notes.txt inside the window.
        var root = BeginRepo();
        AgeWorktree(root);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "user notes written just before the ledger\n");
        var complete = Capture(root, path => LedgerStat.Portable(path) with { Device = 1, Inode = 1, Changed = 1 });

        var admitted = BuildConfinement.AdmitLedger(complete);
        admitted.Hashed.ShouldBe(1, "only the recently modified entry is hashed");
        admitted.Settlement.Kind.ShouldBeNull(admitted.Settlement.Detail);

        // red (Review 9f1c8a2e D2): the portable stamp (non-Linux, or the statx fallback) has no identity and no
        // change time, so admission hashes each captured entry, not only the recent one.
        var portable = Capture(root, LedgerStat.Portable);
        var outside = NonOutputFiles(root);
        outside.Length.ShouldBeGreaterThan(IgnoredFiles);
        portable.Files.Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(outside);
        var hashed = BuildConfinement.AdmitLedger(portable);
        hashed.Hashed.ShouldBe(outside.Length, "each non-output file of the fixture is hashed once at admission");
        hashed.Settlement.Kind.ShouldBeNull(hashed.Settlement.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public async Task portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch(CancellationToken cancellationToken)
    {
        // red (Review 157c4013 D1): the portable stamp holds type, size and mtime only; B written with A's size and
        // A's mtime restored matches it, so admission skips the hash and the build writes A back over B.
        var root = BeginRepo();
        AgeWorktree(root);
        var user = Path.Combine(root, "node_modules", "pkg", "f0011.js");
        var slots = new RecordingSlotClient(
            onAcquire: () => RewriteKeepingSizeAndMtime(user, "module.exports = 99;\n"),
            onRelease: () => { });
        var driver = new LedgerDriver(slots, () => RewriteKeepingSizeAndMtime(user, "module.exports = 11;\n"), LedgerStat.Portable);
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.BuildLedgers.ShouldBeEmpty("the portable stamp does not show the rewrite, so admission hashes the entry");
        File.ReadAllText(user).ShouldBe("module.exports = 99;\n");
        exit.ShouldNotBe(0);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        report.Builds.Single().SlotReason.ShouldBe("refused: tampered");
        File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"))
            .ShouldContain("TAMPERED node_modules/pkg/f0011.js", Case.Sensitive);
        report.Rows.Single().State.ShouldNotBe("green");
    }

    [Test]
    [Timeout(300_000)]
    public async Task rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch(CancellationToken cancellationToken)
    {
        // red: admission trusts a stamp that a timestamp-preserving rewrite leaves equal. On Linux the statx ctime
        // moves with the rewrite and its mtime restore; elsewhere the portable stamp has no ctime and is hashed.
        var root = BeginRepo();
        AgeWorktree(root);
        var user = Path.Combine(root, "node_modules", "pkg", "f0012.js");
        var slots = new RecordingSlotClient(
            onAcquire: () => RewriteKeepingSizeAndMtime(user, "module.exports = 98;\n"),
            onRelease: () => { });
        var driver = new LedgerDriver(slots, () => RewriteKeepingSizeAndMtime(user, "module.exports = 12;\n"));
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.BuildLedgers.ShouldBeEmpty("the rewrite is refused once the slot is granted, before the build starts");
        File.ReadAllText(user).ShouldBe("module.exports = 98;\n");
        exit.ShouldNotBe(0);
        File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"))
            .ShouldContain("TAMPERED node_modules/pkg/f0012.js", Case.Sensitive);
        if (OperatingSystem.IsLinux())
        {
            var recorded = driver.Captured.ShouldNotBeNull().Stats["node_modules/pkg/f0012.js"];
            var now = LedgerStat.Of(user);
            now.Changed.ShouldNotBe(recorded.Changed, "the rewrite and the mtime restore move the ctime");
            (now with { Changed = recorded.Changed }).ShouldBe(recorded, "type, device, inode, size and mtime are kept");
        }
    }

    [Test]
    [Timeout(300_000)]
    public Task stat_without_a_change_time_hashes_each_entry_at_admission()
    {
        // red: a stamp without a change time is trusted on equal identity, size and mtime, so A -> B keeping
        // size and mtime is not hashed and not refused.
        var root = BeginRepo();
        AgeWorktree(root);
        var ledger = Capture(root, path => LedgerStat.Of(path) with { Changed = 0 });

        var unchanged = BuildConfinement.AdmitLedger(ledger);
        unchanged.Settlement.Kind.ShouldBeNull(unchanged.Settlement.Detail);
        unchanged.Hashed.ShouldBe(ledger.Files.Count, "a stamp without a change time does not show an entry unchanged");

        RewriteKeepingSizeAndMtime(Path.Combine(root, "node_modules", "pkg", "f0013.js"), "module.exports = 97;\n");
        var changed = BuildConfinement.AdmitLedger(ledger);
        changed.Settlement.Kind.ShouldBe("tampered");
        changed.Settlement.Paths.ToArray().ShouldBe(new[] { "node_modules/pkg/f0013.js" }, changed.Settlement.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task entry_with_a_change_time_inside_the_racy_window_is_hashed_at_admission()
    {
        // red: the racy window looks at the mtime only, so an entry with an old mtime and a ctime in the same
        // tick as the capture is skipped on an equal stamp, although a rewrite in that tick keeps the ctime.
        var root = BeginRepo();
        AgeWorktree(root);
        var recent = LedgerStat.Now();
        var ledger = Capture(root, path =>
        {
            var stat = LedgerStat.Portable(path) with { Device = 1, Inode = 1 };
            return stat with { Changed = path.EndsWith("notes.txt", StringComparison.Ordinal) ? recent : stat.Modified };
        });

        var admitted = BuildConfinement.AdmitLedger(ledger);
        admitted.Hashed.ShouldBe(1, "only the entry whose ctime is inside the racy window is hashed");
        admitted.Settlement.Kind.ShouldBeNull(admitted.Settlement.Detail);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task shared_mapping_style_change_with_an_unchanged_stat_is_refused_at_admission_only_in_strict_mode()
    {
        // Review 9f1c8a2e D1: a store through a shared memory mapping can change the bytes and leave the stat stamp
        // equal; the stat provider here returns the stamp it first read whatever the content. Default admission
        // accepts A -> B (the stated limit); strict admission hashes each entry and refuses it.
        // red: strict admission still skips entries on their stat stamp.
        var root = BeginRepo();
        AgeWorktree(root);
        var ledger = Capture(root, FrozenStat());
        var user = Path.Combine(root, "node_modules", "pkg", "f0014.js");
        var original = File.ReadAllText(user);
        File.WriteAllText(user, "module.exports = 'B';\n");

        var stat = BuildConfinement.AdmitLedger(ledger);
        stat.Hashed.ShouldBe(0, "default admission trusts an unchanged, aged, complete stamp");
        stat.Settlement.Kind.ShouldBeNull("default admission does not see a change its stat stamp does not show");

        var strict = BuildConfinement.AdmitLedger(ledger, strict: true);
        strict.Hashed.ShouldBe(ledger.Files.Count, "strict admission hashes each ledger entry");
        strict.Settlement.Kind.ShouldBe("tampered");
        strict.Settlement.Paths.ToArray().ShouldBe(new[] { "node_modules/pkg/f0014.js" }, strict.Settlement.Detail);

        File.WriteAllText(user, original);
        BuildConfinement.VerifyLedger(ledger).Kind.ShouldBeNull("A written back leaves no difference for the after-release hash");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public async Task strict_admission_hash_in_the_manifest_refuses_the_build_before_launch(CancellationToken cancellationToken)
    {
        // red: build.admissionHash: strict does not reach admission, so a change the stat stamp does not show is
        // admitted and the build writes A back over B. The default run beside it admits that change (Review 9f1c8a2e D1).
        var (defaultRun, defaultDriver, defaultUser, defaultOriginal) = await RunWithUnseenChangeAsync(null, cancellationToken);
        defaultDriver.BuildLedgers.ToArray().ShouldBe(new[] { true }, "default admission launches the build");
        File.ReadAllText(defaultUser).ShouldBe(defaultOriginal, "the build wrote A back over B");
        var defaultReport = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(defaultRun, "report.json")), ReportWriter.Json)!;
        defaultReport.Builds.Single().State.ShouldBe("ok");
        var defaultLog = Path.Combine(defaultRun, "builds", "bin-c1241", "build.log"); // the fake build writes none
        (File.Exists(defaultLog) ? File.ReadAllText(defaultLog) : "").ShouldNotContain("TAMPERED");

        var (strictRun, strictDriver, strictUser, _) = await RunWithUnseenChangeAsync("strict", cancellationToken);
        strictDriver.BuildLedgers.ShouldBeEmpty("strict admission hashes the entry and refuses before the build starts");
        File.ReadAllText(strictUser).ShouldBe("module.exports = 'B';\n");
        var strictReport = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(strictRun, "report.json")), ReportWriter.Json)!;
        strictReport.Builds.Single().State.ShouldBe("failed");
        strictReport.Builds.Single().SlotReason.ShouldBe("refused: tampered");
        File.ReadAllText(Path.Combine(strictRun, "builds", "bin-c1241", "build.log"))
            .ShouldContain("TAMPERED node_modules/pkg/f0015.js", Case.Sensitive);
        strictReport.Rows.Single().State.ShouldNotBe("green");
    }

    [Test]
    [Timeout(120_000)]
    public Task ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered()
    {
        // red (CARD-1358): an entry whose after-release read is refused is printed TAMPERED with a restore command,
        // although no content difference was observed.
        var root = BeginRepo();
        var unreadable = false;
        var ledger = Capture(root, stamp: StalledStamp("node_modules/pkg/f0005.js", () => unreadable));
        unreadable = true;

        var verified = BuildConfinement.VerifyLedger(ledger);
        verified.Kind.ShouldBe("unreadable", verified.Detail);
        verified.Paths.ToArray().ShouldBe(new[] { "node_modules/pkg/f0005.js" }, verified.Detail);
        verified.Detail.ShouldStartWith("refused: unreadable");
        verified.Detail.ShouldContain("UNREADABLE node_modules/pkg/f0005.js: ", Case.Sensitive);
        verified.Detail.ShouldContain("read-stalled", Case.Sensitive);
        verified.Detail.ShouldNotContain("TAMPERED");
        verified.Detail.ShouldNotContain("restore --source");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(120_000)]
    public Task tampered_entry_beside_an_unreadable_entry_is_still_refused_tampered()
    {
        // red (CARD-1358): the unreadable entry is printed TAMPERED with a restore command beside the real change.
        var root = BeginRepo();
        var unreadable = false;
        var ledger = Capture(root, stamp: StalledStamp("node_modules/pkg/f0005.js", () => unreadable));
        unreadable = true;
        File.WriteAllText(Path.Combine(root, "node_modules", "pkg", "f0006.js"), "changed while waiting\n");

        var settlement = BuildConfinement.AdmitLedger(ledger, strict: true).Settlement;
        settlement.Kind.ShouldBe("tampered", settlement.Detail);
        settlement.Paths.ToArray().ShouldBe(new[] { "node_modules/pkg/f0005.js", "node_modules/pkg/f0006.js" }, settlement.Detail);
        settlement.Detail.ShouldStartWith("refused: tampered");
        settlement.Detail.ShouldContain("TAMPERED node_modules/pkg/f0006.js\nmanual command: git -C ", Case.Sensitive);
        settlement.Detail.ShouldContain("status --short -- 'node_modules/pkg/f0006.js'", Case.Sensitive);
        settlement.Detail.ShouldContain("UNREADABLE node_modules/pkg/f0005.js: ", Case.Sensitive);
        settlement.Detail.ShouldNotContain("TAMPERED node_modules/pkg/f0005.js");
        settlement.Detail.ShouldNotContain("'node_modules/pkg/f0005.js'");
        settlement.Detail.ShouldContain("manual restore required", Case.Sensitive);
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public Task in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable(CancellationToken cancellationToken)
    {
        // red (CARD-1358): the in-slot baseline reports an entry it could not re-read as TAMPERED.
        var root = BeginRepo();
        var ledger = Capture(root);
        var baseline = Prepare(root, ledger, cancellationToken);

        var settled = BuildConfinement.Settle(baseline, StalledStamp("App/obj/stale.txt", () => true));
        settled.Kind.ShouldBe("unreadable", settled.Detail);
        settled.Paths.ToArray().ShouldBe(new[] { "App/obj/stale.txt" }, settled.Detail);
        settled.Detail.ShouldContain("UNREADABLE App/obj/stale.txt: ", Case.Sensitive);
        settled.Detail.ShouldNotContain("TAMPERED");
        return Task.CompletedTask;
    }

    [Test]
    [Timeout(300_000)]
    public async Task scheduler_reports_an_unreadable_ledger_entry_as_unreadable(CancellationToken cancellationToken)
    {
        // red (CARD-1358): report.json says refused: tampered and build.log prints TAMPERED for an entry the
        // after-release read could not stamp. The entry is readable at capture and admission; the build makes it stall.
        var root = BeginRepo();
        var unreadable = false;
        var slots = new RecordingSlotClient(onAcquire: () => { }, onRelease: () => { });
        var driver = new LedgerDriver(slots, () => unreadable = true, stamp: StalledStamp("node_modules/pkg/f0005.js", () => unreadable));
        var (run, exit) = await RunScheduledAsync(root, driver, slots, cancellationToken);

        driver.BuildLedgers.ToArray().ShouldBe(new[] { true }, "the entry was readable at capture and at admission, so the build launched");
        exit.ShouldNotBe(0);
        var report = JsonSerializer.Deserialize<ReportModel>(File.ReadAllText(Path.Combine(run, "report.json")), ReportWriter.Json)!;
        var build = report.Builds.Single();
        build.State.ShouldBe("failed");
        build.SlotReason.ShouldBe("refused: unreadable");
        var log = File.ReadAllText(Path.Combine(run, "builds", "bin-c1241", "build.log"));
        log.ShouldContain("UNREADABLE node_modules/pkg/f0005.js", Case.Sensitive);
        log.ShouldNotContain("TAMPERED");
        report.Rows.Single().State.ShouldNotBe("green");
    }

    [Test]
    public void admission_hash_other_than_stat_or_strict_is_rejected()
    {
        // red: an unknown build.admissionHash value loads and runs with the default stat admission.
        var root = TempDir();
        ManifestLoader.LoadYaml("schemaVersion: 1\nbuild:\n  admissionHash: strict\n", root).Build.AdmissionHash.ShouldBe("strict");
        ManifestLoader.LoadYaml("schemaVersion: 1\n", root).Build.AdmissionHash.ShouldBeNull();
        var rejected = Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml("schemaVersion: 1\nbuild:\n  admissionHash: fast\n", root));
        rejected.Field.ShouldBe("admissionHash");
    }

    // A -> B during the slot wait that the stat stamp does not show; the build writes A back.
    private async Task<(string Run, LedgerDriver Driver, string User, string Original)> RunWithUnseenChangeAsync(
        string? admissionHash, CancellationToken cancellationToken)
    {
        var root = BeginRepo();
        AgeWorktree(root);
        var user = Path.Combine(root, "node_modules", "pkg", "f0015.js");
        var original = File.ReadAllText(user);
        var acquires = 0;
        var slots = new RecordingSlotClient(
            onAcquire: () => { if (acquires++ == 0) File.WriteAllText(user, "module.exports = 'B';\n"); }, // the build's slot, not the row's
            onRelease: () => { });
        var driver = new LedgerDriver(slots, () => File.WriteAllText(user, original), FrozenStat());
        var (run, _) = await RunScheduledAsync(root, driver, slots, cancellationToken, admissionHash);
        return (run, driver, user, original);
    }

    // A complete stat stamp (identity and an old ctime) that keeps the value first read for each path, as a
    // write through a shared memory mapping can leave it.
    private static Func<string, LedgerStat> FrozenStat()
    {
        var frozen = new Dictionary<string, LedgerStat>(StringComparer.Ordinal);
        return path => frozen.TryGetValue(path, out var stat)
            ? stat
            : frozen[path] = LedgerStat.Portable(path) with { Device = 1, Inode = 1, Changed = 1 };
    }

    // The fixture files outside .git and the App/obj write root, repository-relative and sorted.
    private static string[] NonOutputFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !relative.StartsWith(".git/", StringComparison.Ordinal) && !relative.StartsWith("App/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static async Task<(string Run, int Exit)> RunScheduledAsync(
        string root, LedgerDriver driver, RecordingSlotClient slots, CancellationToken cancellationToken, string? admissionHash = null)
    {
        var source = new SourceObservation(Sha, 0, new string('1', 64), DateTimeOffset.UtcNow, "known");
        var manifest = new CheckpointManifest
        {
            Build = new BuildDefaults { AdmissionHash = admissionHash },
            Builds = [new BuildSpec { Id = "bin-c1241", Project = "App/App.csproj", OutputPath = "bin-c1241/" }],
            Checkpoints = [new CheckpointSpec
            {
                Id = "CP-1", After = ["all"], Build = "bin-c1241",
                Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1, EstimatedMinutes = 1,
            }],
        };
        var run = CheckpointApp.CreateRun(manifest, new RunRequest
        {
            Commit = Sha, Branch = "fixture", KeepOutputs = true, Slots = "off",
        }, root, new CheckpointApp.Runtime { SourceCapture = _ => source });

        var exit = await CheckpointApp.ExecuteAsync(run, cancellationToken, new CheckpointApp.Runtime
        {
            Driver = driver, Slots = slots, SourceCapture = _ => source,
            // The fixture is not an owned executor. An ambient task id with no token
            // returns owner-unverified before the scheduler runs.
            EnvironmentLookup = _ => null,
        });
        return (run, exit);
    }

    // Moves the modification time of the worktree files outside .git an hour back, outside the racy window,
    // then waits the window out: setting the mtime moves the ctime to now, and a ctime inside the window is hashed.
    private static void AgeWorktree(string root)
    {
        var old = DateTime.UtcNow.AddHours(-1);
        var git = Path.Combine(root, ".git") + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            if (!file.StartsWith(git, StringComparison.Ordinal))
                File.SetLastWriteTimeUtc(file, old);
        Thread.Sleep(TimeSpan.FromMilliseconds(BuildConfinement.RacyWindow / 1_000_000 + 250));
    }

    private string BeginRepo()
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1241");
        Write(root, ".gitignore", "bin-*/\nobj/\nnode_modules/\n.antiphon/\n.checkpoint-test-root.json\n");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <DefaultItemExcludes>$(DefaultItemExcludes);bin-*/**</DefaultItemExcludes>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        Git(root, "add", "-A");
        Git(root, "-c", "user.email=card-1241@example.invalid", "-c", "user.name=card-1241",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
        for (var i = 0; i < IgnoredFiles; i++)
            Write(root, $"node_modules/pkg/f{i:D4}.js", "module.exports = " + i + ";\n");
        Write(root, "App/obj/stale.txt", "pre-existing intermediate\n");
        Write(root, "notes.txt", "user notes, untracked\n");
        return root;
    }

    // Writes same-sized bytes and puts the previous modification time back (utimes, touch -r).
    private static void RewriteKeepingSizeAndMtime(string path, string content)
    {
        var modified = File.GetLastWriteTimeUtc(path);
        new FileInfo(path).Length.ShouldBe(content.Length);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, modified);
    }

    private static BuildConfinement.Ledger Capture(string root, Func<string, LedgerStat>? stat = null, Func<string, string>? stamp = null)
    {
        var captured = BuildConfinement.CaptureLedger(root, Arguments, stat, stamp);
        captured.Refusal.ShouldBeNull(captured.Detail);
        return captured.Value.ShouldNotBeNull();
    }

    // The real content stamp, except the named entry, which answers as a read the reader refused as stalled once
    // the flag is set (CARD-1358): the ledger never sees a hash, only the refusal reason.
    private static Func<string, string> StalledStamp(string relative, Func<bool> unreadable)
    {
        var suffix = Path.DirectorySeparatorChar + relative.Replace('/', Path.DirectorySeparatorChar);
        return path => unreadable() && path.EndsWith(suffix, StringComparison.Ordinal)
            ? "unreadable|receipt-read-refused: read-stalled: 0 of 7864320 bytes; no progress within 3s"
            : BuildConfinement.Stamp(path);
    }

    private static BuildConfinement.Baseline Prepare(string root, BuildConfinement.Ledger? ledger, CancellationToken cancellationToken)
    {
        var prepared = BuildConfinement.Prepare(root, Arguments, null, cancellationToken, ledger);
        prepared.Refusal.ShouldBeNull(prepared.Detail);
        return prepared.Baseline.ShouldNotBeNull();
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Setup Git sees the user's autocrlf config; confinement uses hardened Git.
        // Persist the same LF bytes on both hosts before either observes the file.
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    }

    private static void Git(string root, params string[] args) => _ = CheckpointFixtureProcess.Git(root, args);

    private sealed class RecordingSlotClient(Action onAcquire, Action onRelease) : IBuildSlotClient
    {
        public bool Held { get; private set; }

        public Task<SlotSession> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SlotSession("granted", 4));

        public Task<SlotLease> AcquireAsync(SlotSession session, string label, CancellationToken cancellationToken)
        {
            onAcquire();
            Held = true;
            return Task.FromResult(new SlotLease
            {
                State = "granted",
                MaxCpuCount = 4,
                ReleaseAsync = _ =>
                {
                    Held = false;
                    onRelease();
                    return Task.CompletedTask;
                },
            });
        }
    }

    // Uses the real ledger capture around a fake build; the build runs no Prepare/Settle, so changes reach the report through the scheduler's ledger checks.
    private sealed class LedgerDriver(RecordingSlotClient slots, Action onBuild, Func<string, LedgerStat>? stat = null,
        Func<string, string>? stamp = null) : IConfinedDriver
    {
        private readonly FakeDriver _inner = new();
        public List<bool> LedgerCaptures { get; } = [];
        public List<bool> BuildLedgers { get; } = [];
        public BuildConfinement.Ledger? Captured { get; private set; }

        public BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request)
        {
            LedgerCaptures.Add(slots.Held);
            var captured = BuildConfinement.CaptureLedger(request.WorkingDirectory, request.Arguments, stat, stamp);
            Captured = captured.Value;
            return captured;
        }

        public Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
        {
            if (CheckpointFixtures.IsBuild(request))
            {
                BuildLedgers.Add(request.Ledger is not null);
                onBuild();
            }
            return _inner.RunAsync(request, cancellationToken);
        }

        public void Kill(bool entireProcessTree) => _inner.Kill(entireProcessTree);
    }
}
