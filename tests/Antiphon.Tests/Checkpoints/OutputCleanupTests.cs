using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Antiphon.Checkpoints;
using Antiphon.HostCleanup.Checkpoints;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class OutputCleanupTests : CheckpointTestBase
{
    [Test]
    public void build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            Skip.Test("Output custody requires Linux or Windows");
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1368");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var app = Path.Combine(output, "app.dll");
        File.WriteAllText(app, "COMPILER");
        var big = Path.Combine(output, "big.bin");
        using (var file = File.Create(big))
            file.SetLength(64L * 1024 * 1024 + 1);
        var exited = CheckpointFixtures.AfterFileClock(app, big);

        Should.Throw<IOException>(() => ReceiptFile.ReadBytes(big)).Message
            .ShouldContain("selected file exceeds size limit");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        SafeWrite.MatchesCreation(big).ShouldBeTrue();

        var clean = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1368"], 0, false, false,
            createdPaths: [output]);
        clean.Refusals.ShouldBeEmpty();
        clean.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();
    }

    [Test]
    public void deletes_only_this_runs_bin_names()
    {
        var root = Tree();
        var created = new[] { Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a") };
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false, createdPaths: created);
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
        Directory.Exists(Path.Combine(root, "src", "bin-c723a")).ShouldBeFalse();
        File.Exists(Path.Combine(root, "bin-foreign", "keep.txt")).ShouldBeTrue();
        result.Deleted.Count.ShouldBe(2);
    }

    [Test]
    public void never_deletes_bin_obj_workspace_or_foreign_bin_star()
    {
        var root = Tree();
        Directory.CreateDirectory(Path.Combine(root, "bin", "bin-c723a"));
        File.WriteAllText(Path.Combine(root, "bin", "bin-c723a", "sentinel"), "keep");
        File.WriteAllText(Path.Combine(root, "bin", "keep.dll"), "keep");
        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false,
            createdPaths: [Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a")]);
        File.Exists(Path.Combine(root, "bin", "keep.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "bin", "bin-c723a", "sentinel")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "obj", "keep.o")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "workspace", "keep.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(root, "bin-foreign", "keep.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void red_keeps_unless_clean_on_red()
    {
        var root = Tree();
        var created = new[] { Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a") };
        var kept = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 1, false, false, createdPaths: created);
        kept.KeptBecauseRed.ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeTrue();
        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 1, true, false, createdPaths: created);
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void older_than_preserves_run_folders_without_a_creation_ledger()
    {
        var root = TempDir();
        var old = Path.Combine(root, "20200101-000000-aaaa");
        var young = Path.Combine(root, "20990101-000000-bbbb");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(young);
        CheckpointFixtures.MarkRun(old, alive: false);
        CheckpointFixtures.MarkRun(young, alive: true);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        var dry = OutputCleanup.RemoveOlderRuns(root, TimeSpan.FromDays(7), dryRun: true);
        dry.Deleted.ShouldBeEmpty();
        dry.Refusals.ShouldContain("output-ledger-missing");
        Directory.Exists(old).ShouldBeTrue();
        var removed = OutputCleanup.RemoveOlderRuns(root, TimeSpan.FromDays(7), dryRun: false);
        removed.Deleted.ShouldBeEmpty();
        removed.Refusals.ShouldContain("output-ledger-missing");
        Directory.Exists(old).ShouldBeTrue();
        Directory.Exists(young).ShouldBeTrue();
    }

    [Test]
    public void evidence_under_antiphon_survives_and_dry_run_says_would_delete()
    {
        var root = Tree();
        var evidence = Path.Combine(root, ".antiphon", "checkpoints", "run", "builds", "bin-c723a");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "build.log"), "log");
        var created = new[] { Path.Combine(root, "bin-c723a"), Path.Combine(root, "src", "bin-c723a") };
        var dry = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, dryRun: true, createdPaths: created);
        dry.Deleted.ShouldContain(path => path.EndsWith("bin-c723a", StringComparison.Ordinal) && !path.Contains(".antiphon", StringComparison.Ordinal));
        OutputCleanup.DeletedLine(true, dry.Deleted[0]).ShouldStartWith("would delete ");
        OutputCleanup.DeletedLine(false, dry.Deleted[0]).ShouldStartWith("deleted ");
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeTrue();
        var deleted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, dryRun: false, createdPaths: created);
        deleted.Deleted.ShouldAllBe(path => !path.Contains(".antiphon", StringComparison.Ordinal));
        File.Exists(Path.Combine(evidence, "build.log")).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "bin-c723a")).ShouldBeFalse();
    }

    [Test]
    public void refuses_unsafe_build_ids_and_keeps_tracked_files()
    {
        var root = InitGitRepo();
        var tests = Path.Combine(root, "tests");
        var src = Path.Combine(root, "src");
        var trackedBin = Path.Combine(root, "bin-tracked");
        var safe = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(tests);
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(trackedBin);
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(safe, SafeWrite.Under(root)));
        File.WriteAllText(Path.Combine(tests, "kept.cs"), "tracked");
        File.WriteAllText(Path.Combine(src, "kept.cs"), "tracked");
        File.WriteAllText(Path.Combine(trackedBin, "kept.txt"), "tracked");
        // This positive cleanup control records its payload through SafeWrite; a payload
        // written by another process is adopted at Seal (CARD-1242), and a later write is retained.
        SafeWrite.Require(SafeWrite.Text(Path.Combine(safe, "out.dll"), "untracked", SafeWrite.Under(safe)));
        SafeWrite.Require(OwnedOutputLedger.Seal(safe));
        Git(root, "add", "--", "tests/kept.cs", "src/kept.cs", "bin-tracked/kept.txt");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "card-1199", "--no-verify");

        var outside = Path.Combine(Path.GetTempPath(), "c1199-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "outside");
        var parent = Directory.GetParent(root)!.FullName;
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root,
                ["tests", ".", "..", "src", outside, "bin-tracked", "bin-safe"],
                0, false, false,
                createdPaths: [tests, src, outside, trackedBin, safe]);
            result.Refusals.ShouldContain("tracked");

            File.Exists(Path.Combine(tests, "kept.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(src, "kept.cs")).ShouldBeTrue();
            File.Exists(Path.Combine(trackedBin, "kept.txt")).ShouldBeTrue();
            Directory.Exists(root).ShouldBeTrue();
            Directory.Exists(parent).ShouldBeTrue();
            File.Exists(Path.Combine(outside, "keep.txt")).ShouldBeTrue();
            Directory.Exists(safe).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    // CARD-1242: red at 15a9071fa with output-ledger-mismatch on app.dll; the build child's
    // files had no creation record, so every green build run ended exit 2 (cleanup_refused).
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void external_build_payload_is_adopted_at_seal_and_removed_on_green()
    {
        var (root, output, exited) = ExternalBuild("bin-c1242");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);

        result.Refusals.ShouldBeEmpty();
        result.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();
        ReportValidator.Validate(new ReportModel { ExitCode = 0, Verdict = "GREEN", CleanedOutputs = true, CleanupRefusals = result.Refusals },
            new string('a', 40)).ShouldNotBe("cleanup_refused");
    }

    // CARD-1242: adoption happens at seal, not at cleanup. Red at 15a9071fa because the refusal
    // named an unrecorded build file instead of the late addition.
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void file_added_after_seal_is_refused_and_kept()
    {
        var (root, output, exited) = ExternalBuild("bin-c1242");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        var late = Path.Combine(output, "runtimes", "late.txt");
        File.WriteAllText(late, "USER");

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);

        result.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + late]);
        result.Deleted.ShouldBeEmpty();
        File.ReadAllText(late).ShouldBe("USER");
        File.ReadAllText(Path.Combine(output, "app.dll")).ShouldBe("COMPILER");
    }

    [Test]
    public void unknown_lifecycle_report_after_seal_is_refused_and_kept()
    {
        var root = Tree();
        var output = Path.Combine(root, "bin-c723a");
        var host = Path.Combine(output, "checkpoint-lifecycle-host");
        var reports = Path.Combine(host, "TestResults");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(host, SafeWrite.Under(root)));
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(reports, SafeWrite.Under(root)));
        SafeWrite.Require(OwnedOutputLedger.Seal(output));
        var unknown = Path.Combine(reports, "unknown.txt");
        File.WriteAllText(unknown, "UNKNOWN REPORT BYTES");
        var control = Path.Combine(root, "src", "bin-c723a");

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false,
            createdPaths: [output, control]);

        // Ledger entries retain portable relative separators in their diagnostic.
        result.Refusals.ShouldBe(["output-ledger-mismatch; left in place: "
            + Path.Combine(output, "checkpoint-lifecycle-host/TestResults/unknown.txt")]);
        result.Deleted.ShouldBe([control]);
        Directory.Exists(control).ShouldBeFalse("unchanged sealed output is a removable control");
        File.ReadAllText(unknown).ShouldBe("UNKNOWN REPORT BYTES");
        File.ReadAllText(Path.Combine(output, "out.dll")).ShouldBe("x");
    }

    // CARD-1242: the seal record holds size and SHA-256; a same-size rewrite must still refuse.
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void file_modified_after_seal_is_refused_and_kept()
    {
        var (root, output, exited) = ExternalBuild("bin-c1242");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        var native = Path.Combine(output, "runtimes", "native.so");
        File.WriteAllText(native, "NATIVF");

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);

        result.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + native]);
        result.Deleted.ShouldBeEmpty();
        File.ReadAllText(native).ShouldBe("NATIVF");
    }

    // CARD-1242: a link written by the build fails the seal; one added after the seal refuses cleanup.
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void symlink_in_a_build_output_is_refused_before_and_after_seal()
    {
        var (root, output, exited) = ExternalBuild("bin-c1242");
        var outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(outside, "OUTSIDE");
        var linkedBeforeSeal = Path.Combine(root, "bin-c1242-pre");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(linkedBeforeSeal, SafeWrite.Under(root)));
        var early = Path.Combine(linkedBeforeSeal, "linked.dll");
        var link = Path.Combine(output, "runtimes", "linked.dll");
        try
        {
            File.CreateSymbolicLink(early, outside);
            OwnedOutputLedger.Seal(linkedBeforeSeal).ShouldBe("output-inventory-failed");

            SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
            File.CreateSymbolicLink(link, outside);

            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242", "bin-c1242-pre"], 0, false, false,
                createdPaths: [output, linkedBeforeSeal]);

            result.Refusals.Count.ShouldBe(2);
            result.Deleted.ShouldBeEmpty();
            File.ReadAllText(outside).ShouldBe("OUTSIDE");
            new FileInfo(link).LinkTarget.ShouldBe(outside);
            File.ReadAllText(Path.Combine(output, "app.dll")).ShouldBe("COMPILER");
        }
        finally
        {
            // The fixture teardown refuses a linked tree; remove only the two links made here.
            if (new FileInfo(early).LinkTarget is not null) File.Delete(early);
            if (new FileInfo(link).LinkTarget is not null) File.Delete(link);
        }
    }

    // CARD-1242: adoption gives no authority over a tracked path inside the output.
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void tracked_file_in_an_adopted_build_output_is_refused_and_kept()
    {
        var (root, output, exited) = ExternalBuild("bin-c1242");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        Git(root, "add", "--", "bin-c1242/app.dll");
        Git(root, "-c", "user.email=card-1242@example.invalid", "-c", "user.name=card-1242",
            "-c", "commit.gpgsign=false", "commit", "-m", "card-1242", "--no-verify");

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);

        result.Refusals.ShouldContain("tracked");
        result.Deleted.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(output, "app.dll")).ShouldBe("COMPILER");
    }

    // CARD-1242: adoption does not lift the entry cap; a pathological tree is still unsafe-tree.
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void adopted_build_output_over_the_entry_cap_is_unsafe_tree()
    {
        var (root, output, _) = ExternalBuild("bin-c1242");
        var many = Path.Combine(output, "many");
        Directory.CreateDirectory(many);
        for (var i = 0; i < 10_000; i++)
            File.WriteAllText(Path.Combine(many, i + ".dll"), "x");
        // The build is the 10,000 writes too, so it exits after them.
        SafeWrite.Require(OwnedOutputLedger.Seal(output, DateTimeOffset.UtcNow));

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);

        result.Refusals.ShouldBe(["unsafe-tree"]);
        result.Deleted.ShouldBeEmpty();
        File.Exists(Path.Combine(many, "9999.dll")).ShouldBeTrue();
    }

    // CARD-1242: only the process that created the root adopts; a separate invocation still cannot.
    [Test]
    public void seal_of_a_root_this_process_did_not_create_adopts_nothing()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1242");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, ".checkpoint-output-owner.json"), "foreign");
        File.WriteAllText(Path.Combine(output, "app.dll"), "COMPILER");

        OwnedOutputLedger.Seal(output).ShouldBeNull();

        SafeWrite.MatchesCreation(Path.Combine(output, "app.dll")).ShouldBeFalse();
        OwnedOutputLedger.Check(output)!.ShouldStartWith("output-memory-authority-missing");
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);
        result.Deleted.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(output, "app.dll")).ShouldBe("COMPILER");
    }

    // CARD-1242 F1 (Review e18f0dc1): red at 2b293e51f, where Seal adopted every entry of a root this
    // process created. The build runs through the real ProcessDriver; a separate process writes after
    // the driver-observed exit. The build's file is adopted; the late file and its changed directory
    // are not, cleanup refuses and the bytes stay.
    [NativeLinuxOnly("seal-time custody needs statx")]
    [Test]
    public async Task independent_writer_after_build_exit_is_not_adopted_and_cleanup_keeps_it()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("seal-time custody needs statx");
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1242");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        (OutputCustody.Stat(output)!.Value.Mode & 0x3F).ShouldBe(0);
        var built = await new ProcessDriver().RunAsync(new DriverRequest("sh",
            ["-c", "printf BUILD > \"$1/app.dll\" && mkdir \"$1/runtimes\" && printf NATIVE > \"$1/runtimes/native.so\"", "build", output], root),
            CancellationToken.None);
        built.ExitCode.ShouldBe(0);
        built.Exited.ShouldNotBeNull();
        PastKernelClock(built.Exited!.Value);
        Shell("printf FOREIGN > \"$1/runtimes/foreign.txt\"", output);

        SafeWrite.Require(OwnedOutputLedger.Seal(output, built.Exited));

        SafeWrite.MatchesCreation(Path.Combine(output, "app.dll")).ShouldBeTrue();
        SafeWrite.MatchesCreation(Path.Combine(output, "runtimes", "foreign.txt")).ShouldBeFalse();
        SafeWrite.MatchesCreation(Path.Combine(output, "runtimes")).ShouldBeFalse();
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);
        result.Deleted.ShouldBeEmpty();
        result.Refusals.Single().ShouldStartWith("output-ledger-mismatch; left in place: " + Path.Combine(output, "runtimes"));
        File.ReadAllText(Path.Combine(output, "runtimes", "foreign.txt")).ShouldBe("FOREIGN");
    }

    // CARD-1242 F1: the reviewer's probe. Red at 2b293e51f. Without a driver-observed build exit the
    // seal cannot bound custody, so a pre-seal write by a separate process is not adopted.
    [NativeLinuxOnly("seal-time custody needs statx")]
    [Test]
    public void seal_without_driver_observed_build_exit_adopts_nothing()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("seal-time custody needs statx");
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1242");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        Shell("printf FOREIGN > \"$1/foreign.txt\"", output);

        SafeWrite.Require(OwnedOutputLedger.Seal(output));

        SafeWrite.MatchesCreation(Path.Combine(output, "foreign.txt")).ShouldBeFalse();
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);
        result.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + Path.Combine(output, "foreign.txt")]);
        result.Deleted.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(output, "foreign.txt")).ShouldBe("FOREIGN");
    }

    // CARD-1242 F1: a hard link made inside the build window is not adopted; the outside bytes stay.
    // Green at 2b293e51f too (the confined reader already refused it); kept as a custody control.
    [NativeLinuxOnly("this helper needs Linux statx")]
    [Test]
    public void hard_linked_build_file_is_not_adopted_and_outside_bytes_stay()
    {
        var (root, output, _) = ExternalBuild("bin-c1242");
        var outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(outside, "OUTSIDE");
        var start = new ProcessStartInfo("ln");
        start.ArgumentList.Add(outside);
        start.ArgumentList.Add(Path.Combine(output, "linked.dll"));
        CheckpointFixtureProcess.Run(start, 30_000).Exit.ShouldBe(0);

        SafeWrite.Require(OwnedOutputLedger.Seal(output, DateTimeOffset.UtcNow));

        SafeWrite.MatchesCreation(Path.Combine(output, "linked.dll")).ShouldBeFalse();
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1242"], 0, false, false, createdPaths: [output]);
        result.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + Path.Combine(output, "linked.dll")]);
        result.Deleted.ShouldBeEmpty();
        File.ReadAllText(outside).ShouldBe("OUTSIDE");
    }

    // CARD-1242 F1: mock inventory entry, since this test cannot chown. Red at 2b293e51f (no custody).
    [Test]
    public void custody_refuses_an_entry_owned_by_another_uid()
    {
        OutputCustody.Refusal(InWindow(), CustodyWindow).ShouldBeNull();
        OutputCustody.Refusal(InWindow() with { Owner = 0 }, CustodyWindow).ShouldBe("foreign-owner");
    }

    // CARD-1242 F1: an entry whose ctime precedes the root's creation was not written by this build.
    [Test]
    public void custody_refuses_an_entry_changed_before_the_root_was_created()
    {
        OutputCustody.Refusal(InWindow() with { Changed = DateTimeOffset.FromUnixTimeSeconds(999) }, CustodyWindow)
            .ShouldBe("changed-before-root");
    }

    // CARD-1242 F1: links, special files and entries changed or modified after the exit are refused;
    // a directory inside the window is adopted.
    [Test]
    public void custody_refuses_links_special_files_and_entries_after_the_build_exit()
    {
        OutputCustody.Refusal(InWindow() with { Links = 2 }, CustodyWindow).ShouldBe("multiple-links");
        OutputCustody.Refusal(InWindow(0xA1FF), CustodyWindow).ShouldBe("not-regular");
        OutputCustody.Refusal(InWindow(0x11A4), CustodyWindow).ShouldBe("not-regular");
        OutputCustody.Refusal(InWindow() with { Changed = DateTimeOffset.FromUnixTimeSeconds(2_001) }, CustodyWindow)
            .ShouldBe("changed-after-build");
        OutputCustody.Refusal(InWindow() with { Modified = DateTimeOffset.FromUnixTimeSeconds(2_001) }, CustodyWindow)
            .ShouldBe("modified-after-build");
        OutputCustody.Refusal(InWindow(0x41C0) with { Links = 3 }, CustodyWindow).ShouldBeNull();
    }

    [Test]
    public void deletes_only_paths_recorded_for_this_run()
    {
        var root = Tree();
        var preexisting = Path.Combine(root, "bin-c723a");
        var nested = Path.Combine(root, "src", "bin-c723a");
        var before = OutputCleanup.MatchingIsolatedDirectories(root, "bin-c723a");
        var fresh = Path.Combine(root, "fresh", "bin-c723a");
        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(fresh, SafeWrite.Under(root)));
        SafeWrite.Require(OwnedOutputLedger.Seal(fresh));
        Directory.CreateDirectory(Path.Combine(root, "tests"));
        var state = new RunState();
        OutputCleanup.RecordCreatedOutputs(state, root, "bin-c723a", before);
        OutputCleanup.RecordCreatedOutputs(state, root, "tests", []);
        state.CreatedOutputPaths.ShouldBe([Path.GetFullPath(fresh)]);

        var deleted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a", "tests"], 0, false, false,
            createdPaths: state.CreatedOutputPaths);
        Directory.Exists(fresh).ShouldBeFalse();
        Directory.Exists(preexisting).ShouldBeTrue();
        Directory.Exists(nested).ShouldBeTrue();
        Directory.Exists(Path.Combine(root, "tests")).ShouldBeTrue();
        deleted.Deleted.Count.ShouldBe(1);

        OutputCleanup.CleanOwnedOutputs(root, ["bin-c723a"], 0, false, false, createdPaths: []);
        Directory.Exists(preexisting).ShouldBeTrue();
    }

    [Test]
    public void corrupted_git_metadata_refuses_cleanup_and_keeps_the_file()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "out.dll");
        File.WriteAllText(sentinel, "keep");
        Directory.Delete(Path.Combine(root, ".git", "objects"), recursive: true);
        Directory.Delete(Path.Combine(root, ".git", "refs"), recursive: true);
        File.Delete(Path.Combine(root, ".git", "HEAD"));

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-safe"], 0, false, false, createdPaths: [output]);

        File.ReadAllText(sentinel).ShouldBe("keep");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("not-a-repository");
    }

    [Test]
    public void git_missing_from_path_refuses_cleanup_and_keeps_the_file()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-safe");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "out.dll");
        File.WriteAllText(sentinel, "keep");
        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-safe"], 0, false, false, createdPaths: [output],
            gitExecutable: Path.Combine(Path.GetTempPath(), "c1199-no-git", "git"));
        File.ReadAllText(sentinel).ShouldBe("keep");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("git-unavailable");
    }

    [Test]
    public void nested_repository_tracked_output_is_owner_checked_and_kept()
    {
        var outer = InitGitRepo();
        var nested = Path.Combine(outer, "nested");
        Directory.CreateDirectory(nested);
        Git(nested, "init", "-q", "-b", "inner");
        var output = Path.Combine(nested, "bin-inner");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "kept.txt");
        File.WriteAllText(sentinel, "tracked-inner");
        Git(nested, "add", "--", "bin-inner/kept.txt");
        Git(nested, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");

        var result = OutputCleanup.CleanOwnedOutputs(outer, ["bin-inner"], 0, false, false, createdPaths: [output]);

        File.ReadAllText(sentinel).ShouldBe("tracked-inner");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("tracked");
    }

    [Test]
    public void manual_clean_refuses_an_unrecorded_directory()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-manual");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
        var run = Path.Combine(root, ".antiphon", "checkpoints", "run-manual");
        Directory.CreateDirectory(run);
        var manifest = new CheckpointManifest();
        manifest.Builds.Add(new BuildSpec { Id = "bin-manual", Project = "sample", OutputPath = "bin-manual/" });
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = "CP-1", After = ["all"], Build = "bin-manual", Filter = "/*/*/*/*",
            EstimatedMinutes = 1, MinExecuted = 1,
        });
        File.WriteAllText(Path.Combine(run, "manifest.resolved.yaml"), ManifestLoader.ToYaml(manifest));
        new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
        {
            RunId = "run-manual",
            Phase = "done",
            CreatedOutputPaths = [],
        });
        var outputText = new StringWriter();
        var exit = Antiphon.Checkpoints.Program.RunAsync(["clean", "--repo-root", root, "--run", "run-manual"],
            new CheckpointApp.Runtime { Output = outputText }).GetAwaiter().GetResult();

        File.Exists(Path.Combine(output, "keep.txt")).ShouldBeTrue();
        exit.ShouldNotBe(0);
        var text = outputText.ToString();
        text.ShouldContain("refused: not-created-by-run");
        text.ShouldNotContain("deleted ");
    }

    [Test]
    public void junction_outside_the_isolated_root_is_refused()
    {
        var root = InitGitRepo();
        var outside = Path.Combine(Path.GetTempPath(), "c1199-junction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "outside");
        var link = Path.Combine(root, "bin-link");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-link"], 0, false, false, createdPaths: [link]);
            File.ReadAllText(sentinel).ShouldBe("outside");
            Directory.Exists(link).ShouldBeTrue();
            result.Deleted.ShouldBeEmpty();
            result.Refusals.ShouldContain("reparse-outside-root");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public void tracked_build_stamp_is_not_deleted_or_replaced()
    {
        var root = InitGitRepo();
        var binding = new CheckpointBuildBinding
        {
            RepositoryRoot = Path.GetFullPath(root),
            Project = Path.GetFullPath(Path.Combine(root, "sample")),
            OutputPath = Path.GetFullPath(Path.Combine(root, "sample", "bin-tracked")),
        };
        Directory.CreateDirectory(binding.OutputPath);
        File.WriteAllText(binding.PathName, "original-stamp");
        Git(root, "add", "--", "sample/bin-tracked/checkpoint-build-source.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "stamp", "--no-verify");

        binding.Invalidate().ShouldBe("tracked");
        File.ReadAllText(binding.PathName).ShouldBe("original-stamp");
        binding.Write().ShouldBe("tracked");
        File.ReadAllText(binding.PathName).ShouldBe("original-stamp");
    }

    [Test]
    public void refusal_report_names_the_reason_and_does_not_claim_deleted()
    {
        var model = new ReportModel
        {
            RunId = "run",
            ExitCode = 2,
            Verdict = "RED",
            OutputNames = ["bin-safe/"],
            CleanedOutputs = false,
            CleanupRefusals = ["tracked"],
        };
        var text = ReportWriter.Markdown(model);
        text.ShouldContain("refused: tracked");
        text.ShouldNotContain("deleted");
        ReportValidator.Validate(model, new string('a', 40)).ShouldBe("cleanup_refused");
    }

    [Test]
    public void nested_repository_inside_an_owned_output_is_refused()
    {
        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1199");
        var nested = Path.Combine(output, "nested");
        Directory.CreateDirectory(nested);
        var kept = Path.Combine(nested, "kept.txt");
        File.WriteAllText(kept, "tracked-nested");
        Git(nested, "init", "-q", "-b", "inner");
        Git(nested, "add", "--", "kept.txt");
        Git(nested, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");

        var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1199"], 0, false, false, createdPaths: [output]);

        File.ReadAllText(kept).ShouldBe("tracked-nested");
        result.Deleted.ShouldBeEmpty();
        result.Refusals.ShouldContain("nested_repository");
    }

    [Test]
    public void parent_symlink_does_not_delete_a_tracked_stamp()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("Windows junction variant is unverified");

        var root = InitGitRepo();
        var output = Path.Combine(root, "proj", "bin-stamp");
        Directory.CreateDirectory(output);
        var stamp = Path.Combine(output, "checkpoint-build-source.json");
        File.WriteAllText(stamp, "original-stamp");
        Git(root, "add", "--", "proj/bin-stamp/checkpoint-build-source.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "stamp", "--no-verify");

        var linkRoot = Path.Combine(Path.GetTempPath(), "c1199-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(linkRoot);
        var link = Path.Combine(linkRoot, "parent-link");
        Directory.CreateSymbolicLink(link, output);
        var via = Path.Combine(link, "checkpoint-build-source.json");
        var probe = Path.Combine(Path.GetTempPath(), "c1199-stamp-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            File.WriteAllText(probe, StampProbe);
            var scriptPath = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1");
            var start = new ProcessStartInfo("pwsh");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(probe);
            start.Environment["CARD1199_SCRIPT"] = scriptPath;
            start.Environment["CARD1199_STAMP"] = via;
            var result = CheckpointFixtureProcess.Run(start, 30_000);
            result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
            File.ReadAllText(stamp).ShouldBe("original-stamp");
            result.Stdout.ShouldContain("REFUSED:reparse-point");

            var script = File.ReadAllText(scriptPath);
            script.ShouldNotContain("if (Test-Path -LiteralPath $stampPath) { $stampRefusal = Test-CheckpointStampRefusal $stampPath }");
            var check = script.IndexOf("$replaceRefusal = Test-CheckpointStampRefusal $stampPath", StringComparison.Ordinal);
            var makeDir = script.IndexOf("New-CheckpointDirectory $stampDirectory", StringComparison.Ordinal);
            check.ShouldBeGreaterThanOrEqualTo(0);
            makeDir.ShouldBeGreaterThan(check);
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(linkRoot))
                Directory.Delete(linkRoot, recursive: true);
            if (File.Exists(probe))
                File.Delete(probe);
        }
    }

    [Test]
    public async Task stop_refuses_to_overwrite_tracked_state_json()
    {
        var root = InitGitRepo();
        var run = Path.Combine(root, "run-stop");
        Directory.CreateDirectory(run);
        var statePath = Path.Combine(run, "state.json");
        new RunStateStore().Write(statePath, new RunState { RunId = "run-stop", Phase = "running" });
        var original = File.ReadAllBytes(statePath);
        Git(root, "add", "--", "run-stop/state.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "state", "--no-verify");
        CheckpointFixtures.MarkRun(run, alive: false);

        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
            ["stop", "--run", run, "--repo-root", root],
            new CheckpointApp.Runtime { Output = output });

        exit.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(statePath).ShouldBe(original);
        var text = output.ToString();
        text.ShouldContain("refused: tracked");
        text.ShouldNotContain("stopped ");
    }

    [Test]
    public async Task cleanup_refusal_exits_2_without_claiming_deleted()
    {
        var root = InitGitRepo();
        var results = Path.Combine(root, "results");
        var run = Path.Combine(results, "20200101-000000-aaaa");
        var nested = Path.Combine(run, "nested");
        Directory.CreateDirectory(nested);
        var kept = Path.Combine(nested, "kept.txt");
        File.WriteAllText(kept, "tracked-nested");
        Git(nested, "init", "-q", "-b", "inner");
        Git(nested, "add", "--", "kept.txt");
        Git(nested, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "inner", "--no-verify");
        CheckpointFixtures.MarkRun(run, alive: false);
        Directory.SetLastWriteTimeUtc(run, DateTime.UtcNow.AddDays(-8));

        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
            ["clean", "--repo-root", root, "--results-root", results, "--older-than", "7d"],
            new CheckpointApp.Runtime { Output = output });

        File.ReadAllText(kept).ShouldBe("tracked-nested");
        exit.ShouldBe(ExitCodes.Invalid);
        var text = output.ToString();
        // A separate clean invocation has no creation custody and therefore
        // refuses without traversing or deleting any older run.
        text.ShouldContain("refused: not-created-by-run; cleanup requires current-process creation records");
        text.ShouldContain("left in place: " + results + "; remove it manually");
        Directory.Exists(run).ShouldBeTrue();
        text.ShouldNotContain("deleted ");
    }

    [NativeLinuxOnly("hanging-git proof runs on Linux")]
    [Test]
    [Timeout(45_000)]
    public void hanging_git_is_refused_and_killed_within_the_bound(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("hanging-git proof runs on Linux");
        _ = cancellationToken.CanBeCanceled;

        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-hang");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "out.dll");
        File.WriteAllText(sentinel, "keep");
        // Keep the hanger inside this test's root. A script in the shared temp
        // directory can fail exec in the same moment the timeout wait starts, and
        // that start failure was reported as git-unavailable.
        var fake = Path.Combine(root, "hang-git");
        var pidfile = Path.Combine(root, "hang-git.pid");
        var script = "#!/bin/sh\nprintf '%s\\n' \"$$\" > '" + pidfile.Replace("'", "'\\''", StringComparison.Ordinal) + "'\nwhile :; do\n  sleep 30 || true\ndone\n";
        using (var stream = new FileStream(fake, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.ASCII.GetBytes(script));
            stream.Flush(true);
        }
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var started = Stopwatch.StartNew();
        try
        {
            var result = OutputCleanup.CleanOwnedOutputs(root, ["bin-hang"], 0, false, false,
                createdPaths: [output], gitExecutable: fake);
            started.Elapsed.TotalSeconds.ShouldBeLessThan(25);
            File.ReadAllText(sentinel).ShouldBe("keep");
            result.Deleted.ShouldBeEmpty();
            result.Refusals.ShouldContain("git-timeout");
            var pidText = File.ReadAllText(pidfile).Trim();
            int.TryParse(pidText, out var pid).ShouldBeTrue(pidText);
            Directory.Exists("/proc/" + pid).ShouldBeFalse();
        }
        finally
        {
            if (File.Exists(pidfile) && int.TryParse(File.ReadAllText(pidfile).Trim(), out var pid))
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2_000);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                }

                File.Delete(pidfile);
            }

            if (File.Exists(fake))
                File.Delete(fake);
        }
    }

    [Test]
    public async Task run_refuses_tracked_latest_before_writing()
    {
        var root = InitGitRepo();
        var results = Path.Combine(root, "results");
        Directory.CreateDirectory(results);
        var latest = Path.Combine(results, "latest");
        File.WriteAllText(latest, "original-latest");
        Git(root, "add", "--", "results/latest");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "latest", "--no-verify");
        var original = File.ReadAllBytes(latest);
        var launched = 0;
        var output = new StringWriter();
        var manifest = new CheckpointManifest
        {
            ResultsRoot = "results",
            Builds = [new BuildSpec { Id = "bin-audit", Project = "proj/App.csproj", OutputPath = "bin-audit/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Build = "bin-audit", Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1 }],
        };

        var started = await CheckpointApp.StartAsync(manifest, new RunRequest(), root, output,
            new CheckpointApp.Runtime { Launch = _ => { launched++; return 1; } });

        started.ExitCode.ShouldBe(ExitCodes.Invalid);
        launched.ShouldBe(0);
        File.ReadAllBytes(latest).ShouldBe(original);
        output.ToString().ShouldContain("refused: tracked");
        Directory.GetDirectories(results).ShouldBeEmpty();
    }

    [Test]
    public async Task execute_refuses_tracked_paths_before_rejecting_input()
    {
        var root = InitGitRepo();
        var run = Path.Combine(root, "run-exec");
        Directory.CreateDirectory(run);
        var ownership = Path.Combine(run, "executor-ownership.json");
        var report = Path.Combine(run, "report.md");
        var request = Path.Combine(run, "request.json");
        File.WriteAllText(ownership, "owned-bytes");
        File.WriteAllText(report, "report-bytes");
        File.WriteAllText(request, "{not-json");
        Git(root, "add", "--", "run-exec/executor-ownership.json", "run-exec/report.md", "run-exec/request.json");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "tracked-run", "--no-verify");
        var owned = File.ReadAllBytes(ownership);
        var reported = File.ReadAllBytes(report);
        var requested = File.ReadAllBytes(request);
        var output = new StringWriter();

        var exit = await CheckpointApp.ExecuteAsync(run, CancellationToken.None, new CheckpointApp.Runtime { Output = output });

        exit.ShouldBe(ExitCodes.Invalid);
        exit.ShouldNotBe(ExitCodes.ExecutorCrashed);
        File.ReadAllBytes(ownership).ShouldBe(owned);
        File.ReadAllBytes(report).ShouldBe(reported);
        File.ReadAllBytes(request).ShouldBe(requested);
        output.ToString().ShouldContain("refused: tracked");
    }

    [Test]
    public async Task build_refuses_tracked_output_before_the_driver()
    {
        var root = InitGitRepo();
        var outputDir = Path.Combine(root, "proj", "bin-audit");
        Directory.CreateDirectory(outputDir);
        var dll = Path.Combine(outputDir, "App.dll");
        File.WriteAllText(dll, "dll-bytes");
        Git(root, "add", "--", "proj/bin-audit/App.dll");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "dll", "--no-verify");
        var original = File.ReadAllBytes(dll);
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        var results = Path.Combine(root, "row-results");
        Directory.CreateDirectory(results);
        var output = new StringWriter();
        var result = await new RowRunner(driver, new RuntimePlatform()).RunAsync(new RowRequest
        {
            Name = "CP-1",
            Project = "proj/App.csproj",
            OutputPath = "bin-audit/",
            Filter = "/*/*/ExampleSurfaceTests/*",
            ResultsDirectory = results,
            WorkingDirectory = root,
            Source = new SourceEvidence { Start = CheckpointFixtures.CleanSource() },
        }, output, CancellationToken.None);

        result.ExitCode.ShouldBe(ExitCodes.Invalid);
        driver.Calls.Count.ShouldBe(0);
        File.ReadAllBytes(dll).ShouldBe(original);
        output.ToString().ShouldContain("refused: tracked");
    }

    [Test]
    public async Task referenced_project_tracked_output_is_refused_before_the_build()
    {
        var root = InitGitRepo();
        WriteProject(root, "Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        WriteProject(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup>
            </Project>
            """);
        var dll = Path.Combine(root, "Lib", "bin-probe2", "Lib.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        var original = "tracked-lib-dll-bytes!!"u8.ToArray();
        File.WriteAllBytes(dll, original);
        CommitAll(root);
        Directory.CreateDirectory(Path.Combine(root, "results"));
        var driver = new FakeDriver();
        driver.When(CheckpointFixtures.IsBuild, (_, _) => Task.FromResult(new DriverResult(0, "", "")));
        var rowOutput = new StringWriter();
        var row = await new RowRunner(driver, new RuntimePlatform()).RunAsync(new RowRequest
        {
            Name = "CP-1",
            Project = "App/App.csproj",
            OutputPath = "bin-probe2/",
            Filter = "/*/*/Unused/*",
            ResultsDirectory = Path.Combine(root, "row-results"),
            WorkingDirectory = root,
            Source = new SourceEvidence { Start = CheckpointFixtures.CleanSource() },
        }, rowOutput, CancellationToken.None);
        driver.Calls.Count.ShouldBe(0);
        row.ExitCode.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(dll).ShouldBe(original);
        rowOutput.ToString().ShouldContain("refused: tracked");

        var launched = 0;
        var output = new StringWriter();
        var manifest = ProbeManifest("App/App.csproj", "bin-probe2");

        var started = await CheckpointApp.StartAsync(manifest, new RunRequest(), root, output,
            new CheckpointApp.Runtime { Launch = _ => { launched++; return 1; } });

        started.ExitCode.ShouldBe(ExitCodes.Invalid);
        launched.ShouldBe(0);
        File.ReadAllBytes(dll).ShouldBe(original);
        output.ToString().ShouldContain("refused: tracked");

        var broken = InitGitRepo();
        WriteProject(broken, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\Missing\Missing.csproj" /></ItemGroup>
            </Project>
            """);
        CommitAll(broken);
        Directory.CreateDirectory(Path.Combine(broken, "results"));
        var brokenLaunched = 0;
        var brokenOutput = new StringWriter();
        var brokenStart = await CheckpointApp.StartAsync(ProbeManifest("App/App.csproj", "bin-probe2"), new RunRequest(), broken, brokenOutput,
            new CheckpointApp.Runtime { Launch = _ => { brokenLaunched++; return 1; } });
        brokenStart.ExitCode.ShouldBe(ExitCodes.Invalid);
        brokenLaunched.ShouldBe(0);
        brokenOutput.ToString().ShouldContain("refused: project-closure");
    }

    [Test]
    public async Task wrapper_refuses_a_tracked_file_under_a_csproj_output_directory()
    {
        var root = InitGitRepo();
        WriteProject(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        var dll = Path.Combine(root, "App", "bin-wrap", "App.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        var original = "tracked-app-dll-21b!!"u8.ToArray();
        original.Length.ShouldBe(21);
        File.WriteAllBytes(dll, original);
        CommitAll(root);
        var outside = Path.Combine(Path.GetTempPath(), "c1199-wrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var shim = Path.Combine(outside, "dotnet-shim.ps1");
        var called = Path.Combine(outside, "called.txt");
        File.WriteAllText(shim, """
            param()
            Set-Content -LiteralPath $env:CARD1199_CALLED -Value ($args -join ' ')
            if ($args -contains 'build') {
                [System.IO.File]::WriteAllText((Join-Path $env:CARD1199_REPO 'App/bin-wrap/App.dll'), 'OVERWRITTEN')
            }
            exit 0
            """);
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root };
            foreach (var arg in new[]
            {
                "-NoProfile", "-NonInteractive", "-File",
                Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1"),
                "-Name", "wrapper-preflight", "-Project", "App/App.csproj", "-OutputPath", "bin-wrap/",
                "-Filter", "/*/*/Unused/*", "-ResultsRoot", Path.Combine(outside, "results"),
                "-MinExecuted", "1", "-NoSlot", "-DotnetShim", shim,
            })
                start.ArgumentList.Add(arg);
            start.Environment["CARD1199_REPO"] = root;
            start.Environment["CARD1199_CALLED"] = called;
            var result = CheckpointFixtureProcess.Run(start, 90_000);
            var stdout = result.Stdout;
            var stderr = result.Stderr;
            output.Write(stdout);
            error.Write(stderr);
            result.Exit.ShouldBe(ExitCodes.Invalid, stdout + stderr);
            File.ReadAllBytes(dll).ShouldBe(original);
            File.Exists(called).ShouldBeFalse(stdout + stderr);
            stdout.ShouldContain("refused: tracked");
        }
        finally
        {
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public async Task smoke_detach_refuses_tracked_foreign_and_linked_targets()
    {
        var trackedRoot = InitGitRepo();
        var tracked = Path.Combine(trackedRoot, "victim.txt");
        File.WriteAllText(tracked, "TRACKED ORIGINAL");
        CommitAll(trackedRoot);
        var trackedBytes = File.ReadAllBytes(tracked);
        var trackedOut = new StringWriter();
        var trackedExit = await Antiphon.Checkpoints.Program.RunAsync(["smoke-detach", tracked], new CheckpointApp.Runtime { Output = trackedOut });
        trackedExit.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(tracked).ShouldBe(trackedBytes);
        trackedOut.ToString().ShouldContain("refused: tracked");

        var foreign = Path.Combine(TempDir(), "victim.txt");
        File.WriteAllText(foreign, "FOREIGN ORIGINAL");
        var foreignBytes = File.ReadAllBytes(foreign);
        var foreignOut = new StringWriter();
        var foreignExit = await Antiphon.Checkpoints.Program.RunAsync(["smoke-detach", foreign], new CheckpointApp.Runtime { Output = foreignOut });
        foreignExit.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(foreign).ShouldBe(foreignBytes);
        foreignOut.ToString().ShouldContain("refused: unrecorded-existing");

        if (!OperatingSystem.IsLinux())
            return;
        var linkedRoot = TempDir();
        var real = Path.Combine(linkedRoot, "real");
        Directory.CreateDirectory(real);
        var kept = Path.Combine(real, "victim.txt");
        File.WriteAllText(kept, "LINK ORIGINAL");
        var link = Path.Combine(linkedRoot, "link");
        Directory.CreateSymbolicLink(link, real);
        try
        {
            var linkOut = new StringWriter();
            var linkExit = await Antiphon.Checkpoints.Program.RunAsync(["smoke-detach", Path.Combine(link, "victim.txt")],
                new CheckpointApp.Runtime { Output = linkOut });
            linkExit.ShouldBe(ExitCodes.Invalid);
            File.ReadAllText(kept).ShouldBe("LINK ORIGINAL");
            linkOut.ToString().ShouldContain("refused: link-component");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    [Test]
    public async Task stop_refuses_foreign_state_without_writing()
    {
        var root = InitGitRepo();
        var run = Path.Combine(root, "run-foreign");
        Directory.CreateDirectory(run);
        var statePath = Path.Combine(run, "state.json");
        // Supply every serialized fact so the foreign-owner guard is reached after shape validation.
        File.WriteAllText(statePath, System.Text.Json.JsonSerializer.Serialize(new RunState
        { RunId = "other-run", ToolOwner = "not-this-tool", Phase = "running" }, RunStateStore.Json));
        var original = File.ReadAllBytes(statePath);
        CheckpointFixtures.MarkRun(run, alive: false);
        var output = new StringWriter();

        var exit = await Antiphon.Checkpoints.Program.RunAsync(["stop", "--run", run, "--repo-root", root],
            new CheckpointApp.Runtime { Output = output });

        exit.ShouldBe(ExitCodes.Invalid);
        File.ReadAllBytes(statePath).ShouldBe(original);
        output.ToString().ShouldContain("refused: foreign_state");
        output.ToString().ShouldNotContain("stopped ");
    }

    [Test]
    public void cleanup_report_follows_the_directory_post_condition()
    {
        var dir = Path.Combine(TempDir(), "bin-audit");
        Directory.CreateDirectory(dir);
        var kept = SafeWrite.FormatOutputs(["bin-audit/"], [dir]);
        Directory.Exists(dir).ShouldBeTrue();
        kept.ShouldBe("outputs: refused/kept bin-audit/");
        kept.ShouldNotContain("deleted");
        Directory.Delete(dir);
        Directory.Exists(dir).ShouldBeFalse();
        SafeWrite.FormatOutputs(["bin-audit/"], [dir]).ShouldBe("outputs: deleted bin-audit/");
    }

    [Test]
    public void safe_write_refuses_tracked_linked_unrecorded_and_git_failure()
    {
        var trackedRoot = InitGitRepo();
        var tracked = Path.Combine(trackedRoot, "keep.txt");
        File.WriteAllText(tracked, "original");
        Git(trackedRoot, "add", "--", "keep.txt");
        Git(trackedRoot, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "keep", "--no-verify");
        SafeWrite.Text(tracked, "changed", SafeWrite.Under(trackedRoot)).ShouldBe("tracked");
        File.ReadAllText(tracked).ShouldBe("original");

        var plain = TempDir();
        var existing = Path.Combine(plain, "keep.txt");
        File.WriteAllText(existing, "original");
        SafeWrite.Text(existing, "changed", SafeWrite.Under(plain)).ShouldBe("unrecorded-existing");
        File.ReadAllText(existing).ShouldBe("original");

        var failing = InitGitRepo();
        var victim = Path.Combine(failing, "keep.txt");
        File.WriteAllText(victim, "original");
        var failingGit = new FailingGit();
        var write = new CheckpointWrite(new(CheckpointCleanupDefaults.Context.Io, failingGit));
        write.Text(victim, "changed", write.Under(failing)).ShouldBe("git-failed");
        failingGit.Calls.ShouldBe(1);
        File.ReadAllText(victim).ShouldBe("original");

        if (!OperatingSystem.IsLinux())
            return;
        var linkedRoot = TempDir();
        var real = Path.Combine(linkedRoot, "real");
        Directory.CreateDirectory(real);
        var kept = Path.Combine(real, "keep.txt");
        File.WriteAllText(kept, "original");
        var link = Path.Combine(linkedRoot, "link");
        Directory.CreateSymbolicLink(link, real);
        try
        {
            SafeWrite.Text(Path.Combine(link, "keep.txt"), "changed", SafeWrite.Under(linkedRoot)).ShouldBe("link-component");
            File.ReadAllText(kept).ShouldBe("original");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    [Test]
    public async Task preflight_git_failure_exits_2_without_writing()
    {
        var root = InitGitRepo();
        var results = Path.Combine(root, "results");
        var outputDir = Path.Combine(root, "proj", "bin-audit");
        Directory.CreateDirectory(results);
        Directory.CreateDirectory(outputDir);
        var latest = Path.Combine(results, "latest");
        var dll = Path.Combine(outputDir, "App.dll");
        File.WriteAllText(latest, "original-latest");
        File.WriteAllText(dll, "dll-bytes");
        var before = new[] { latest, dll }.ToDictionary(path => path, path => (Bytes: File.ReadAllBytes(path), Mtime: File.GetLastWriteTimeUtc(path)));
        var failingGit = new FailingGit();
        var launched = 0;
        var output = new StringWriter();
        var manifest = new CheckpointManifest
        {
            ResultsRoot = "results",
            Builds = [new BuildSpec { Id = "bin-audit", Project = "proj/App.csproj", OutputPath = "bin-audit/" }],
            Checkpoints = [new CheckpointSpec { Id = "CP-1", Build = "bin-audit", Filter = "/*/*/ExampleSurfaceTests/*", MinExecuted = 1 }],
        };
        var started = await CheckpointApp.StartAsync(manifest, new RunRequest(), root, output,
            new CheckpointApp.Runtime
            {
                CleanupContext = new(CheckpointCleanupDefaults.Context.Io, failingGit),
                Launch = _ => { launched++; return 1; },
            });
        started.ExitCode.ShouldBe(ExitCodes.Invalid);
        failingGit.Calls.ShouldBeGreaterThan(0);
        launched.ShouldBe(0);
        output.ToString().ShouldContain("refused: git-failed");
        Directory.GetDirectories(results).ShouldBeEmpty();
        foreach (var (path, snapshot) in before)
        {
            File.ReadAllBytes(path).ShouldBe(snapshot.Bytes);
            File.GetLastWriteTimeUtc(path).ShouldBe(snapshot.Mtime);
        }
    }

    private sealed class FailingGit : ICheckpointGitRunner
    {
        public int Calls { get; private set; }
        public CheckpointGitResult Run(CheckpointGitRequest request)
        {
            Calls++;
            return new(1, "", "fixture git failure", false);
        }
    }

    private const string StampProbe = """
        $ErrorActionPreference = 'Stop'
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:CARD1199_SCRIPT, [ref]$tokens, [ref]$errors)
        if ($errors -and @($errors).Count -gt 0) { throw (($errors | ForEach-Object { $_.ToString() }) -join "`n") }
        $names = @('Test-CheckpointComponentLink','Find-CheckpointGitDir','Invoke-CheckpointGit','Test-CheckpointStampRefusal')
        $chunks = New-Object System.Collections.Generic.List[string]
        foreach ($name in $names) {
            $fn = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true) | Select-Object -First 1
            if ($null -ne $fn) { $chunks.Add($fn.Extent.Text) }
        }
        $invoke = ($chunks -join "`n") + @'

        $reason = Test-CheckpointStampRefusal $env:CARD1199_STAMP
        if ([string]::IsNullOrEmpty($reason)) {
            if (Test-Path -LiteralPath $env:CARD1199_STAMP) { [System.IO.File]::Delete($env:CARD1199_STAMP) }
            Write-Output 'DELETED'
        } else {
            Write-Output ('REFUSED:' + $reason)
        }
        '@
        & ([scriptblock]::Create($invoke))
        """;

    // CARD-1310: Windows seal must adopt a file the build overwrote before the observed exit, and must
    // still refuse a write after that exit, a second link, and a reparse point.
    [Test]
    public void windows_seal_adopts_an_overwrite_before_exit_and_keeps_a_later_write()
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1310 Windows output custody");

        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1310");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var map = Path.Combine(output, ".msCoverageExtensionSourceRootsMapping_App");
        File.WriteAllText(map, "EARLY");
        File.WriteAllText(map, "FINAL");
        var exited = CheckpointFixtures.AfterFileClock(map);
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));

        SafeWrite.MatchesCreation(map).ShouldBeTrue();
        var cleaned = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1310"], 0, false, false, createdPaths: [output]);
        cleaned.Refusals.ShouldBeEmpty();
        cleaned.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();

        var lateRoot = InitGitRepo();
        var lateOutput = Path.Combine(lateRoot, "bin-c1310");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(lateOutput, SafeWrite.Under(lateRoot)));
        var built = Path.Combine(lateOutput, "app.dll");
        File.WriteAllText(built, "COMPILER");
        var lateExit = CheckpointFixtures.AfterFileClock(built);
        SafeWrite.Require(OwnedOutputLedger.Seal(lateOutput, lateExit));
        var late = Path.Combine(lateOutput, "late.txt");
        File.WriteAllText(late, "USER");
        var kept = OutputCleanup.CleanOwnedOutputs(lateRoot, ["bin-c1310"], 0, false, false, createdPaths: [lateOutput]);
        kept.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + late]);
        kept.Deleted.ShouldBeEmpty();
        File.ReadAllText(late).ShouldBe("USER");
        File.ReadAllText(built).ShouldBe("COMPILER");

        var linkRoot = InitGitRepo();
        var linkOutput = Path.Combine(linkRoot, "bin-c1310");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(linkOutput, SafeWrite.Under(linkRoot)));
        var outside = Path.Combine(linkRoot, "outside.txt");
        File.WriteAllText(outside, "OUTSIDE");
        var linked = Path.Combine(linkOutput, "linked.dll");
        CreateHardLink(linked, outside, IntPtr.Zero).ShouldBeTrue(Marshal.GetLastWin32Error().ToString());
        var sibling = Path.Combine(linkOutput, "app.dll");
        File.WriteAllText(sibling, "COMPILER");
        var linkExit = CheckpointFixtures.AfterFileClock(sibling, linked);
        SafeWrite.Require(OwnedOutputLedger.Seal(linkOutput, linkExit));
        SafeWrite.MatchesCreation(linked).ShouldBeFalse();
        SafeWrite.MatchesCreation(sibling).ShouldBeTrue();
        var linkedResult = OutputCleanup.CleanOwnedOutputs(linkRoot, ["bin-c1310"], 0, false, false, createdPaths: [linkOutput]);
        linkedResult.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + linked]);
        linkedResult.Deleted.ShouldBeEmpty();
        File.ReadAllText(outside).ShouldBe("OUTSIDE");

        var reparseRoot = InitGitRepo();
        var reparseOutput = Path.Combine(reparseRoot, "bin-c1310");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(reparseOutput, SafeWrite.Under(reparseRoot)));
        var target = Path.Combine(reparseRoot, "target");
        Directory.CreateDirectory(target);
        var keptBytes = Path.Combine(target, "kept.txt");
        File.WriteAllText(keptBytes, "KEPT");
        var junction = Path.Combine(reparseOutput, "joined");
        try
        {
            Junction(junction, target);
            OwnedOutputLedger.Seal(reparseOutput, DateTimeOffset.UtcNow).ShouldBe("output-inventory-failed");
            File.ReadAllText(keptBytes).ShouldBe("KEPT");
            Directory.Exists(reparseOutput).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction);
        }
    }

    // CARD-1318 (b): a post-exit write whose creation and last-write times are stamped back inside
    // the window stays unrecorded, a root DACL that grants another SID makes For return null, and
    // an owner-only root still seals and deletes.
    [Test]
    public void windows_set_clock_and_foreign_dacl_are_not_adopted()
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1318 Windows custody hardening");

        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1318");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var built = Path.Combine(output, "app.dll");
        File.WriteAllText(built, "COMPILER");
        var exited = CheckpointFixtures.AfterFileClock(built);
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        SafeWrite.MatchesCreation(built).ShouldBeTrue();
        var cleaned = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1318"], 0, false, false, createdPaths: [output]);
        cleaned.Refusals.ShouldBeEmpty();
        cleaned.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();

        var clockRoot = InitGitRepo();
        var clockOutput = Path.Combine(clockRoot, "bin-c1318");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(clockOutput, SafeWrite.Under(clockRoot)));
        var stamped = Path.Combine(clockOutput, "app.dll");
        File.WriteAllText(stamped, "COMPILER");
        var created = SafeWrite.FreshRootCreated(clockOutput)!.Value;
        var clockExit = CheckpointFixtures.AfterFileClock(stamped);
        File.WriteAllText(stamped, "TAMPERED");
        var inside = created + (clockExit - created) / 2;
        File.SetCreationTimeUtc(stamped, inside.UtcDateTime);
        File.SetLastWriteTimeUtc(stamped, inside.UtcDateTime);
        SafeWrite.Require(OwnedOutputLedger.Seal(clockOutput, clockExit));
        SafeWrite.MatchesCreation(stamped).ShouldBeFalse();
        var stampedResult = OutputCleanup.CleanOwnedOutputs(clockRoot, ["bin-c1318"], 0, false, false, createdPaths: [clockOutput]);
        stampedResult.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + stamped]);
        stampedResult.Deleted.ShouldBeEmpty();
        File.ReadAllText(stamped).ShouldBe("TAMPERED");

        var daclRoot = InitGitRepo();
        var daclOutput = Path.Combine(daclRoot, "bin-c1318");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(daclOutput, SafeWrite.Under(daclRoot)));
        var daclExit = SafeWrite.FreshRootCreated(daclOutput)!.Value.AddMinutes(1);
        OutputCustody.For(daclOutput, daclExit).ShouldNotBeNull();
        GrantWorld(daclOutput);
        OutputCustody.For(daclOutput, daclExit).ShouldBeNull();
    }

    // CARD-1318 D-1: CopyFile keeps the source NTFS change time. A file copied into the fresh
    // root from an older file is adopted and deleted when its creation time is inside the window.
    [Test]
    public void windows_copied_old_change_time_is_adopted()
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1318 Windows copy change time");

        var root = InitGitRepo();
        var old = Path.Combine(root, "package.dll");
        File.WriteAllText(old, "PACKAGE");
        var ancient = DateTime.UtcNow.AddDays(-30);
        File.SetCreationTimeUtc(old, ancient);
        File.SetLastWriteTimeUtc(old, ancient);
        var oldChanged = OutputCustody.Stat(old)!.Value.Changed;
        for (var i = 0; i < 50 && DateTimeOffset.UtcNow <= oldChanged; i++)
            Thread.Sleep(20);

        var output = Path.Combine(root, "bin-c1318");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var copied = Path.Combine(output, "package.dll");
        File.Copy(old, copied);
        var rootCreated = SafeWrite.FreshRootCreated(output)!.Value;
        var copiedStat = OutputCustody.Stat(copied)!.Value;
        (copiedStat.Changed < rootCreated).ShouldBeTrue("copy kept a change time from before the root");
        var copyCreated = new DateTimeOffset(DateTime.SpecifyKind(File.GetCreationTimeUtc(copied), DateTimeKind.Utc));
        (copyCreated >= rootCreated).ShouldBeTrue("copy creation is not before the root");
        var exited = CheckpointFixtures.AfterFileClock(copied);
        (copyCreated <= exited).ShouldBeTrue("copy creation is not after the observed exit");
        SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        SafeWrite.MatchesCreation(copied).ShouldBeTrue();
        var cleaned = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1318"], 0, false, false, createdPaths: [output]);
        cleaned.Refusals.ShouldBeEmpty();
        cleaned.Deleted.ShouldBe([output]);
        Directory.Exists(output).ShouldBeFalse();
        File.ReadAllText(old).ShouldBe("PACKAGE");
    }

    // CARD-1318 (c): FileShare.None across the first stat, released inside the retry bound, is adopted.
    // A holder kept for the whole bound stays unrecorded and cleanup refuses. The release delay sits
    // inside CheckpointNativeIo's sharing-violation bound.
    [Test]
    public void windows_sharing_violation_retries_then_adopts()
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1318 Windows custody hardening");

        var root = InitGitRepo();
        var output = Path.Combine(root, "bin-c1318");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        var payload = Path.Combine(output, "app.dll");
        File.WriteAllText(payload, "COMPILER");
        var exited = CheckpointFixtures.AfterFileClock(payload);
        var boundMs = (CheckpointNativeIo.CustodySharingViolationAttempts - 1)
            * CheckpointNativeIo.CustodySharingViolationDelayMilliseconds;
        const int releaseMs = 400;
        (boundMs > releaseMs).ShouldBeTrue("release delay must sit inside the sharing retry");
        var hold = new FileStream(payload, FileMode.Open, FileAccess.Read, FileShare.None);
        var releaser = Task.Run(() =>
        {
            Thread.Sleep(releaseMs);
            hold.Dispose();
        });
        try
        {
            SafeWrite.Require(OwnedOutputLedger.Seal(output, exited));
        }
        finally
        {
            releaser.GetAwaiter().GetResult();
        }

        SafeWrite.MatchesCreation(payload).ShouldBeTrue();
        var adopted = OutputCleanup.CleanOwnedOutputs(root, ["bin-c1318"], 0, false, false, createdPaths: [output]);
        adopted.Refusals.ShouldBeEmpty();
        adopted.Deleted.ShouldBe([output]);

        var heldRoot = InitGitRepo();
        var heldOutput = Path.Combine(heldRoot, "bin-c1318");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(heldOutput, SafeWrite.Under(heldRoot)));
        var heldPayload = Path.Combine(heldOutput, "app.dll");
        File.WriteAllText(heldPayload, "COMPILER");
        var heldExit = CheckpointFixtures.AfterFileClock(heldPayload);
        using (new FileStream(heldPayload, FileMode.Open, FileAccess.Read, FileShare.None))
            SafeWrite.Require(OwnedOutputLedger.Seal(heldOutput, heldExit));
        SafeWrite.MatchesCreation(heldPayload).ShouldBeFalse();
        var refused = OutputCleanup.CleanOwnedOutputs(heldRoot, ["bin-c1318"], 0, false, false, createdPaths: [heldOutput]);
        refused.Refusals.ShouldBe(["output-ledger-mismatch; left in place: " + heldPayload]);
        refused.Deleted.ShouldBeEmpty();
        File.ReadAllText(heldPayload).ShouldBe("COMPILER");
    }

    private static void GrantWorld(string directory)
    {
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe");
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target })
            start.ArgumentList.Add(arg);
        var result = CheckpointFixtureProcess.Run(start, 30_000);
        result.Exit.ShouldBe(0, result.Stdout + result.Stderr);
    }

    // CARD-1318: the landed Windows seal adopts a regular single-link non-reparse file
    // after the observed exit. Red while the owner doc or this helper still denies that.
    // The CARD-1242 matrix keeps its Linux skip; the admission-stamp sentence stays.
    [Test]
    public void windows_seal_docs_match_the_landed_adoption()
    {
        var root = CheckpointFixtures.RepoRoot;
        var testing = File.ReadAllText(Path.Combine(root, "docs", "testing-and-build.md"));
        var source = File.ReadAllText(Path.Combine(
            root, "tests", "Antiphon.Tests", "Checkpoints", "OutputCleanupTests.cs"));
        var denied = "off Linux nothing is " + "adopted";
        var linuxOnlySeal = "seal-time custody window above (" + "Linux only)";

        testing.Contains(denied, StringComparison.Ordinal)
            .ShouldBeFalse("owner doc still denies adoption off Linux");
        testing.Contains(linuxOnlySeal, StringComparison.Ordinal)
            .ShouldBeFalse("owner doc still limits the seal to Linux");
        source.Contains(denied, StringComparison.Ordinal)
            .ShouldBeFalse("ExternalBuild still denies adoption off Linux");

        const string custody =
            "after the observed build exit the seal adopts a regular single-link file or directory that is not a reparse";
        const string exitCodes =
            "on Windows a regular single-link file or directory that is not a reparse after the observed build exit";
        testing.Contains(custody, StringComparison.Ordinal)
            .ShouldBeTrue("custody paragraph states the landed Windows seal");
        testing.Contains(exitCodes, StringComparison.Ordinal)
            .ShouldBeTrue("exit-code paragraph states the landed Windows seal");
        testing.Contains("off Linux each entry is hashed", StringComparison.Ordinal)
            .ShouldBeTrue("admission-stamp rule stays");

        var helperAt = source.LastIndexOf(
            "private (string Root, string Output, DateTimeOffset Exited) ExternalBuild(string id)",
            StringComparison.Ordinal);
        helperAt.ShouldBeGreaterThanOrEqualTo(0, "ExternalBuild");
        var commentAt = source.LastIndexOf("A fresh owned root", helperAt, StringComparison.Ordinal);
        var helperEnd = source.IndexOf("private void PastKernelClock", helperAt, StringComparison.Ordinal);
        (commentAt >= 0 && helperEnd > helperAt).ShouldBeTrue("ExternalBuild region");
        var helper = source[commentAt..helperEnd];
        helper.Contains("if (!OperatingSystem.IsLinux())", StringComparison.Ordinal)
            .ShouldBeTrue("CARD-1242 matrix stays on Linux");
        helper.Contains("This helper needs Linux statx", StringComparison.Ordinal)
            .ShouldBeTrue("comment names the helper, not the platform");
        helper.Contains("Skip.Test(\"this helper needs Linux statx\")", StringComparison.Ordinal)
            .ShouldBeTrue("skip names the helper, not the platform");
    }

    // A fresh owned root whose payload is written outside SafeWrite, as the child dotnet build writes it.
    // Exited stands for the driver-observed build exit. This helper needs Linux statx
    // (ctime, ln, and UtcNow); the Windows seal is witnessed separately.
    private (string Root, string Output, DateTimeOffset Exited) ExternalBuild(string id)
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("this helper needs Linux statx");
        var root = InitGitRepo();
        var output = Path.Combine(root, id);
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(output, SafeWrite.Under(root)));
        File.WriteAllText(Path.Combine(output, "app.dll"), "COMPILER");
        Directory.CreateDirectory(Path.Combine(output, "runtimes"));
        File.WriteAllText(Path.Combine(output, "runtimes", "native.so"), "NATIVE");
        return (root, output, DateTimeOffset.UtcNow);
    }

    // ctime comes from a coarse kernel clock: wait until it has passed the stamp so a later write is
    // unambiguously after it (one tick is the disclosed custody boundary).
    private void PastKernelClock(DateTimeOffset stamp)
    {
        var probe = Path.Combine(TempDir(), "clock");
        for (var i = 0; i < 400; i++)
        {
            File.WriteAllText(probe, i.ToString());
            if (OutputCustody.Stat(probe)!.Value.Changed > stamp) return;
            Thread.Sleep(5);
        }
        throw new TimeoutException("kernel clock did not pass the exit stamp");
    }

    private static void Shell(string script, string directory)
    {
        var start = new ProcessStartInfo("sh");
        foreach (var arg in new[] { "-c", script, "writer", directory })
            start.ArgumentList.Add(arg);
        CheckpointFixtureProcess.Run(start, 30_000).Exit.ShouldBe(0);
    }

    private static readonly OutputCustody.Window CustodyWindow = new(
        DateTimeOffset.FromUnixTimeSeconds(1_000), DateTimeOffset.FromUnixTimeSeconds(2_000), 1654);

    private static OutputCustody.Entry InWindow(ushort mode = 0x81A4) => new(mode, 1, 1654, 7, 4,
        DateTimeOffset.FromUnixTimeSeconds(1_500), DateTimeOffset.FromUnixTimeSeconds(1_500));

    private string InitGitRepo()
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1199");
        return root;
    }

    private void CommitAll(string root)
    {
        Git(root, "add", "-A");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
    }

    private static void WriteProject(string root, string relative, string xml)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xml);
    }

    private static CheckpointManifest ProbeManifest(string project, string output) => new()
    {
        ResultsRoot = "results",
        Builds = [new BuildSpec { Id = output, Project = project, OutputPath = output + "/" }],
        Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["S1"], Build = output, Filter = "/*/*/Unused/*", MinExecuted = 1 }],
    };

    private static void Git(string root, params string[] args) => CheckpointFixtureProcess.Git(root, args);

    private string Tree()
    {
        var root = TempDir();
        Git(root, "init", "-q", "-b", "card1199");
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(Path.Combine(root, "bin-c723a"), SafeWrite.Under(root)));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(Path.Combine(root, "src/bin-c723a"), SafeWrite.Under(root)));
        Write(root, "bin-c723a", "out.dll");
        Write(root, Path.Combine("src", "bin-c723a"), "out.dll");
        SafeWrite.Require(OwnedOutputLedger.Seal(Path.Combine(root, "bin-c723a")));
        SafeWrite.Require(OwnedOutputLedger.Seal(Path.Combine(root, "src/bin-c723a")));
        Write(root, "bin-foreign", "keep.txt");
        Write(root, "obj", "keep.o");
        Write(root, "workspace", "keep.txt");
        Write(root, "bin", "keep.dll");
        return root;
    }

    private static void Write(string root, string dir, string file)
    {
        var path = Path.Combine(root, dir);
        Directory.CreateDirectory(path);
        if (SafeWrite.WasCreated(path)) SafeWrite.Require(SafeWrite.Text(Path.Combine(path, file), "x", SafeWrite.Under(path)));
        else File.WriteAllText(Path.Combine(path, file), "x");
    }
}
