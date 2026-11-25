using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1405, CARD-1413 and the backup retarget (CARD-1831), unit 53 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md:
/// the snapshot (Antiphon.Nightly.PostgresSnapshot, the postgres-snapshot command behind scripts/postgres-snapshot.ps1) in process.
/// These are the cases of the deleted driver scripts/test-postgres-snapshot.ps1. Docker and age are the driver's scenario fakes
/// (SnapshotDockerFake) on a recording launcher, so the POSIX-fake cases the driver skipped on Windows run on both systems; the
/// ACL, owner and mode rules run on FakeSnapshotPlatform (both rule sets on either OS) and, where the driver exercised the real
/// system, on PostgresSnapshotOs. The expected lines and exits are the script's, captured from it before its body was replaced.
/// No docker, age, pg_dump, OneDrive or production path is touched.
/// </summary>
[Category("Unit")]
public sealed class PostgresSnapshotTests
{
    private static readonly string Repo = DockerStackDocuments.RepoRoot;

    private static readonly string[] AgePlan =
    [
        "step dump: docker exec antiphon-postgres pg_dump -Fc --no-owner --no-acl -U antiphon -d antiphon -f /tmp/antiphon-snapshot.dump",
        "step verify: pg_restore --list must include TABLE DATA and the dump must be at least 1048576 bytes",
        "step encrypt: age -R <recipient-file> -o <staging>.dump.age",
        "step copy: move the ciphertext into the snapshot directory as a new antiphon-<utc>.dump.age file",
        "step retention: keep 14 dump.age files and prune only after the new file is verified",
        "SNAPSHOT plan exit=0 reason=ok dump=1 encrypt=1 copy=1 retention=14",
    ];

    private static readonly string[] DrillPlan =
    [
        "step drill: decrypt the newest snapshot with age -d -i <identity-file>",
        "step drill: pg_restore --list must include TABLE DATA and the plaintext must be at least 1048576 bytes",
        "step drill: pg_restore into scratch container antiphon-postgres-drill database antiphon_drill volume antiphon-postgres-drill-pgdata",
        "step drill: leave the production database and volume untouched",
        "step drill: remove the labelled scratch container and volume unless -KeepScratch",
        "SNAPSHOT plan exit=0 reason=ok drill=1 scratch=antiphon-postgres-drill",
    ];

    private const string Banner = "PLAIN UNENCRYPTED operator-decision=2026-11-10 encryption=none";
    private const string Server2Source = "step source: target=server2 container=antiphon-server2-postgres-postgres-1 context=antiphon-server2";

    private static string[] PlainSteps(string dest, string container = "antiphon-postgres", bool server2 = false, string? oneDrive = null, string acl = "step acl: user-only directory=absent")
    {
        var lines = new List<string> { Banner, "step destination: " + dest + " outside-onedrive=yes", acl };
        if (server2) lines.Add(Server2Source);
        lines.Add("step dump: docker exec " + container + " pg_dump -Fc --no-owner --no-acl -U antiphon -d antiphon -f /tmp/antiphon-snapshot.dump");
        lines.Add("step verify: pg_restore --list on the new file must include TABLE DATA before prune; minimum 1048576 bytes");
        lines.Add("step copy: write the dump to antiphon-<utc>.dump.partial then rename to antiphon-<utc>.dump");
        lines.Add("step retention: keep 14 plain dump files and prune only after the new file is verified");
        lines.Add("step status: snapshot-status.json partial-write then rename (utc, exit, bytes, sha256 prefix, duration, tables, mode=plain)");
        if (oneDrive != null)
            lines.Add("step onedrive: copy the verified unencrypted dump into " + Path.Combine(oneDrive, "Backups", "antiphon-postgres")
                + " as .partial then rename when sha256 matches; keep 7");
        return lines.ToArray();
    }

    private static string[] Server2DrillPlan(string dest) =>
    [
        Banner,
        Server2Source,
        "step drill: copy the newest antiphon-<utc>.dump from " + dest + " into scratch container antiphon-postgres-drill with docker cp (no bind mount)",
        "step drill: the scratch image is the source container image id",
        "step drill: pg_restore --list must include TABLE DATA, then pg_restore --exit-on-error into database antiphon_drill volume antiphon-postgres-drill-pgdata network none",
        "step drill: count public tables in antiphon_drill only; leave the production database and volume untouched",
        "step drill: remove the labelled scratch container and volume unless -KeepScratch",
    ];

    private const string PlanOk = "SNAPSHOT plan exit=0 reason=ok plain=1 encryption=none retention=14";

    // ---- assertions (the driver's Assert-NoSecret, Assert-NoDocker, Assert-DestUntouched, Assert-DrillRefused) ----

    private static void NoSecret(SnapshotRun run)
    {
        var blob = run.Out + "\n" + run.Error;
        blob.ShouldNotContain(SnapshotWorld.Canary, Case.Sensitive, "recipient bytes were written");
        blob.ShouldNotContain(SnapshotWorld.IdentityCanary, Case.Sensitive, "identity bytes were written");
        Regex.IsMatch(blob, @"(?i)password\s*=").ShouldBeFalse("password= present");
        Regex.IsMatch(blob, @"(?i)host\s*=").ShouldBeFalse("Host= present");
    }

    private static void NoDocker(SnapshotRun run) => run.Tools.ShouldBeEmpty(run.ToString());

    private static void DestUntouched(SnapshotWorld world)
    {
        world.Dumps(world.Dest).ShouldBeEmpty();
        File.Exists(Path.Combine(world.Dest, "snapshot-status.json")).ShouldBeFalse();
    }

    private static void DrillRefused(SnapshotRun run, string guard)
    {
        run.Exit.ShouldBe(9, run.ToString());
        run.Out.ShouldContain("reason=drill-refused");
        run.Out.ShouldContain("guard=" + guard);
        run.ToolText.ShouldNotContain("pg_restore");
        run.ToolText.ShouldNotContain("createdb");
        run.ToolText.ShouldNotContain("rm -f");
        NoSecret(run);
    }

    private static void VolumeUntouched(SnapshotRun run)
    {
        run.ToolText.ShouldNotContain("volume rm");
        run.ToolText.ShouldNotContain("volume create");
        run.ToolText.ShouldNotContain("run -d");
    }

    // Invoke-DrillCase: the cipher in the destination, a fresh docker state, the drill scenario.
    private static SnapshotRun Drill(SnapshotWorld world, string scenario, string[] extra, long plainBytes = 32, string[]? volumes = null,
        IReadOnlyDictionary<string, string>? env = null, bool noSnapshot = false)
    {
        world.Fake = new SnapshotDockerFake { Scenario = scenario, DumpBytes = 1048576, HasTableData = true, PlainBytes = plainBytes };
        foreach (var volume in volumes ?? []) world.Fake.Volumes.Add(volume);
        world.Environment.Clear();
        foreach (var pair in env ?? new Dictionary<string, string>()) world.Environment[pair.Key] = pair.Value;
        var cipher = Path.Combine(world.Dest, "antiphon-20261021T120000Z.dump.age");
        if (noSnapshot) File.Delete(cipher); else File.WriteAllText(cipher, "cipher");
        return world.Run(["--execute", "--restore-drill", "--identity-file", world.Identity, .. world.Common, .. extra]);
    }

    // Invoke-PublishCase.
    private static SnapshotRun Publish(SnapshotWorld world, long bytes, bool tableData, IReadOnlyDictionary<string, string>? env = null, int ageExit = 0)
    {
        world.Fake = new SnapshotDockerFake { Scenario = "publish", DumpBytes = bytes, HasTableData = tableData, PlainBytes = 32, AgeExit = ageExit };
        world.Environment.Clear();
        foreach (var pair in env ?? new Dictionary<string, string>()) world.Environment[pair.Key] = pair.Value;
        return world.Run(["--execute", "--recipient-file", world.Recipient, .. world.Common]);
    }

    // ---- the options and the sources ----

    [Test]
    public void Options_take_the_scripts_parameters_in_kebab_case()
    {
        var defaults = PostgresSnapshotOptions.Parse([]);
        (defaults.WhatIf, defaults.Execute, defaults.Plain, defaults.OneDriveCopy, defaults.OneDriveRetain, defaults.RestoreDrill).ShouldBe((false, false, false, false, 7, false));
        (defaults.Container, defaults.ContainerGiven, defaults.Database, defaults.Retain, defaults.MinimumDumpBytes, defaults.StaleRunMinutes)
            .ShouldBe(("antiphon-postgres", false, "antiphon", 14, 1048576L, 360));
        (defaults.ScratchContainer, defaults.ScratchDatabase, defaults.ScratchVolume, defaults.SourceVolume, defaults.ScratchImage, defaults.Target)
            .ShouldBe(("antiphon-postgres-drill", "antiphon_drill", "antiphon-postgres-drill-pgdata", "antiphon_pgdata", "postgres:16", "desktop"));
        (defaults.StagingPath, defaults.Destination, defaults.SyncRoot, defaults.RecipientFile, defaults.RecipientRef, defaults.IdentityFile, defaults.IdentityRef)
            .ShouldBe(("", "", "", "", "", "", ""));

        var every = PostgresSnapshotOptions.Parse(["--what-if", "--execute", "--plain", "--one-drive-copy", "--one-drive-retain", "3", "--restore-drill", "--container", "",
            "--database", "db", "--staging-path", "s", "--destination", "d", "--sync-root", "o", "--recipient-file", "r", "--recipient-ref", "R", "--identity-file", "i",
            "--identity-ref", "I", "--retain", "2", "--minimum-dump-bytes", "5000000000", "--keep-scratch", "--scratch-container", "sc", "--scratch-database", "sd",
            "--scratch-volume", "sv", "--source-volume", "so", "--scratch-image", "img", "--allow-remote-docker", "--stale-run-minutes", "9", "--target", "server2"]);
        (every.WhatIf, every.Execute, every.Plain, every.OneDriveCopy, every.OneDriveRetain, every.RestoreDrill, every.KeepScratch, every.AllowRemoteDocker)
            .ShouldBe((true, true, true, true, 3, true, true, true));
        (every.Container, every.ContainerGiven, every.Database, every.StagingPath, every.Destination, every.SyncRoot).ShouldBe(("", true, "db", "s", "d", "o"));
        (every.RecipientFile, every.RecipientRef, every.IdentityFile, every.IdentityRef, every.Retain, every.MinimumDumpBytes).ShouldBe(("r", "R", "i", "I", 2, 5000000000L));
        (every.ScratchContainer, every.ScratchDatabase, every.ScratchVolume, every.SourceVolume, every.ScratchImage, every.StaleRunMinutes, every.Target)
            .ShouldBe(("sc", "sd", "sv", "so", "img", 9, "server2"));

        // ValidateSet('desktop', 'server2', IgnoreCase = $false): the launcher keeps it; the command refuses the same words.
        Should.Throw<NightlyUsageException>(() => PostgresSnapshotOptions.Parse(["--target", "Server2"])).Message.ShouldBe("--target must be desktop or server2, not 'Server2'");
        Should.Throw<NightlyUsageException>(() => PostgresSnapshotOptions.Parse(["--retain", "x"])).Message.ShouldBe("--retain expects an integer, not 'x'");
        Should.Throw<NightlyUsageException>(() => PostgresSnapshotOptions.Parse(["--bogus"])).Message.ShouldBe("unknown flag --bogus");
        Should.Throw<NightlyUsageException>(() => PostgresSnapshotOptions.Parse(["-WhatIf"])).Message.ShouldBe("unexpected argument '-WhatIf'");
    }

    [Test]
    public void Sources_are_ascii_and_carry_no_recipient_or_password_literal()
    {
        // WhatIfPlan's script checks, now over the command's sources and the launcher.
        var files = new[] { "Antiphon.Nightly", "Antiphon.Scripts" }.SelectMany(project => Directory.GetFiles(Path.Combine(Repo, "scripts", "lib", project), "*PostgresSnapshot*.cs"))
            .Append(Path.Combine(Repo, "scripts", "postgres-snapshot.ps1")).ToArray();
        files.Length.ShouldBeGreaterThanOrEqualTo(5);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            bytes.ShouldAllBe(b => b < 128, file + " is ASCII only");
            var text = Encoding.ASCII.GetString(bytes);
            Regex.IsMatch(text, "age1[0-9a-z]{10,}").ShouldBeFalse(file + " has no age recipient literal");
            Regex.IsMatch(text, @"(?i)password\s*=").ShouldBeFalse(file + " has no password literal");
        }
        File.Exists(Path.Combine(Repo, "scripts", "test-postgres-snapshot.ps1")).ShouldBeFalse("the driver's cases are this class");
    }

    // ---- the age path's plan and refusals ----

    [Test]
    public void WhatIf_plan_lists_the_age_steps_from_a_file_or_a_reference()
    {
        using var world = new SnapshotWorld();
        var plan = world.Run(["--what-if", "--recipient-file", world.Recipient, .. world.Common]);
        plan.Exit.ShouldBe(0, plan.ToString());
        plan.Lines.ShouldBe(AgePlan);
        NoSecret(plan);
        NoDocker(plan);
        DestUntouched(world);
        Directory.Exists(world.Stage).ShouldBeTrue("the plan keeps staging");

        world.Environment["CARD1405_RECIPIENT_REF"] = world.Recipient;
        var reference = world.Run(["--what-if", "--recipient-ref", "CARD1405_RECIPIENT_REF", .. world.Common]);
        reference.Exit.ShouldBe(0, reference.ToString());
        reference.Lines.ShouldBe(AgePlan);
        NoSecret(reference);
        NoDocker(reference);
        DestUntouched(world);

        // PlainOffByDefault: without --plain the plan still encrypts and names no plain decision.
        plan.Out.ShouldNotContain("PLAIN UNENCRYPTED");
        plan.Out.ShouldNotContain("operator-decision=2026-11-10");
        // The default mode (neither --what-if nor --execute) is the plan.
        world.Run(["--recipient-file", world.Recipient, .. world.Common]).Lines.ShouldBe(AgePlan);
    }

    [Test]
    public void Age_refusals_name_their_reasons_and_exit_codes()
    {
        using var world = new SnapshotWorld();
        void Refused(string[] arguments, int exit, string line)
        {
            var run = world.Run(arguments);
            run.Exit.ShouldBe(exit, run.ToString());
            run.Lines.ShouldBe([line]);
            NoSecret(run);
            NoDocker(run);
            DestUntouched(world);
        }

        Refused(["--what-if", .. world.Common], 2, "SNAPSHOT fail exit=2 reason=missing-recipient");
        var inside = Path.Combine(world.Dest, "recipient.txt");
        File.WriteAllText(inside, SnapshotWorld.Canary);
        Refused(["--what-if", "--recipient-file", inside, .. world.Common], 3, "SNAPSHOT fail exit=3 reason=recipient-inside-sync");
        File.Delete(inside);
        Refused(["--what-if", "--execute", "--recipient-file", world.Recipient, .. world.Common], 2, "SNAPSHOT fail exit=2 reason=ambiguous-mode");
        Refused(["--what-if", "--retain", "0", "--recipient-file", world.Recipient, .. world.Common], 2, "SNAPSHOT fail exit=2 reason=bad-name");
        Refused(["--what-if", "--scratch-image", "postgres:16-alpine", "--recipient-file", world.Recipient, .. world.Common], 2, "SNAPSHOT fail exit=2 reason=bad-name");
        Refused(["--what-if", "--recipient-file", world.Recipient, "--destination", world.Dest, "--sync-root", world.PathOf("nosync"), "--staging-path", world.Stage], 3,
            "SNAPSHOT fail exit=3 reason=missing-sync-root");
        Refused(["--what-if", "--recipient-file", world.Recipient, "--destination", world.PathOf("dest"), "--sync-root", world.Sync, "--staging-path",
            Path.Combine(world.Sync, "stage")], 3, "SNAPSHOT fail exit=3 reason=staging-inside-sync");
        Refused(["--what-if", "--target", "server2", "--recipient-file", world.Recipient, .. world.Common], 2, "SNAPSHOT fail exit=2 reason=ambiguous-mode");
    }

    [Test]
    public void Staging_inside_the_destination_is_refused_and_created_nowhere()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("offsync-dest");
        var stage = Path.Combine(dest, "nested-stage");
        Directory.CreateDirectory(dest);
        var run = world.Run(["--what-if", "--recipient-file", world.Recipient, "--destination", dest, "--sync-root", world.Sync, "--staging-path", stage]);
        run.Exit.ShouldBe(3, run.ToString());
        run.Lines.ShouldBe(["SNAPSHOT fail exit=3 reason=staging-inside-destination"]);
        NoDocker(run);
        NoSecret(run);
        Directory.Exists(stage).ShouldBeFalse();
    }

    [Test]
    public void Drill_plan_and_the_static_drill_guards()
    {
        using var world = new SnapshotWorld();
        var plan = world.Run(["--what-if", "--restore-drill", "--identity-file", world.Identity, .. world.Common, "--scratch-container", "antiphon-postgres-drill",
            "--scratch-database", "antiphon_drill", "--scratch-volume", "antiphon-postgres-drill-pgdata"]);
        plan.Exit.ShouldBe(0, plan.ToString());
        plan.Lines.ShouldBe(DrillPlan);
        plan.Out.ShouldNotContain("docker exec antiphon-postgres ");
        NoSecret(plan);
        NoDocker(plan);
        DestUntouched(world);

        // DrillRefusesProduction: refused before any docker call, trailing slashes included.
        string[] common = ["--execute", "--restore-drill", "--identity-file", world.Identity, .. world.Common];
        foreach (var (extra, guard) in new[]
                 {
                     (new[] { "--scratch-container", "antiphon-postgres" }, "production-container"),
                     (new[] { "--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon_pgdata" }, "production-volume"),
                     (new[] { "--scratch-container", "antiphon-postgres/" }, "production-container"),
                     (new[] { "--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon_pgdata/" }, "production-volume"),
                     (new[] { "--scratch-database", "Antiphon" }, "database"),
                 })
        {
            var run = world.Run([.. common, .. extra]);
            run.Exit.ShouldBe(9, run.ToString());
            run.Lines.ShouldBe(["SNAPSHOT fail exit=9 reason=drill-refused guard=" + guard]);
            NoDocker(run);
            NoSecret(run);
            DestUntouched(world);
        }

        var noIdentity = world.Run(["--what-if", "--restore-drill", .. world.Common]);
        (noIdentity.Exit, noIdentity.Lines.Single()).ShouldBe((2, "SNAPSHOT fail exit=2 reason=missing-identity"));
    }

    [Test]
    public void Missing_age_and_missing_docker_refuse_before_staging()
    {
        using var world = new SnapshotWorld { NoAge = true };
        var publish = world.Run(["--execute", "--recipient-file", world.Recipient, .. world.Common]);
        (publish.Exit, publish.Lines.Single()).ShouldBe((3, "SNAPSHOT fail exit=3 reason=missing-age"));
        NoDocker(publish);
        DestUntouched(world);
        world.RunDirectories().ShouldBeEmpty();
        NoSecret(publish);
        var drill = world.Run(["--execute", "--restore-drill", "--identity-file", world.Identity, .. world.Common]);
        (drill.Exit, drill.Lines.Single()).ShouldBe((3, "SNAPSHOT fail exit=3 reason=missing-age"));
        NoDocker(drill);
        NoSecret(drill);

        world.NoAge = false;
        world.NoDocker = true;
        var noDocker = world.Run(["--execute", "--recipient-file", world.Recipient, .. world.Common]);
        (noDocker.Exit, noDocker.Lines.Single()).ShouldBe((4, "SNAPSHOT fail exit=4 reason=missing-docker"));
        world.RunDirectories().ShouldBeEmpty();
    }

    // ---- retention ----

    [Test]
    public void Retention_keeps_fourteen_only_when_verified_and_never_drops_the_new_file()
    {
        var names = Enumerable.Range(1, 16).Select(day => "antiphon-202610" + day.ToString("00") + "T000000Z.dump.age").ToArray();
        const string newest = "antiphon-20261016T000000Z.dump.age";
        var kept = PostgresSnapshot.PruneSet(names, 14, newest, true);
        kept.Ok.ShouldBeTrue(kept.Reason);
        kept.Drop.ShouldBe(["antiphon-20261002T000000Z.dump.age", "antiphon-20261001T000000Z.dump.age"]);
        PostgresSnapshot.PruneSet(names, 14, newest, false).ShouldSatisfyAllConditions(s => s.Ok.ShouldBeTrue(), s => s.Drop.ShouldBeEmpty(), s => s.Reason.ShouldBe("unverified"));
        PostgresSnapshot.PruneSet(names.Take(3), 14, names[2], true).ShouldSatisfyAllConditions(s => s.Ok.ShouldBeTrue(), s => s.Drop.ShouldBeEmpty(),
            s => s.Reason.ShouldBe("within-retain"));
        PostgresSnapshot.PruneSet(names, 14, names[0], true).ShouldSatisfyAllConditions(s => s.Ok.ShouldBeFalse(), s => s.Drop.ShouldBeEmpty(),
            s => s.Reason.ShouldBe("would-drop-new"));
        PostgresSnapshot.PruneSet(names, 0, newest, true).ShouldSatisfyAllConditions(s => s.Ok.ShouldBeFalse(), s => s.Reason.ShouldBe("retain-below-one"));
        PostgresSnapshot.PruneSet(names, 14, "notes.txt", true).Reason.ShouldBe("bad-name");
    }

    [Test]
    public void Retention_ignores_a_conflict_copy_on_the_list_and_on_disk()
    {
        var valid = Enumerable.Range(1, 16).Select(day => "antiphon-202610" + day.ToString("00") + "T000000Z.dump.age").ToArray();
        const string newest = "antiphon-20261016T000000Z.dump.age";
        const string conflict = "antiphon-20261001T000000Z (conflicted copy).dump.age";
        var kept = PostgresSnapshot.PruneSet([.. valid, conflict], 14, newest, true);
        kept.Ok.ShouldBeTrue();
        kept.Drop.ShouldBe(["antiphon-20261002T000000Z.dump.age", "antiphon-20261001T000000Z.dump.age"]);

        using var world = new SnapshotWorld();
        var dir = world.PathOf("retain");
        Directory.CreateDirectory(dir);
        foreach (var name in valid.Append(conflict)) File.WriteAllText(Path.Combine(dir, name), "x");
        var applied = world.Session().UpdateRetention(dir, 14, newest);
        applied.ShouldBe(new SnapshotRetention(true, "prune", 14));
        File.Exists(Path.Combine(dir, conflict)).ShouldBeTrue();
        File.Exists(Path.Combine(dir, newest)).ShouldBeTrue();
        File.Exists(Path.Combine(dir, "antiphon-20261001T000000Z.dump.age")).ShouldBeFalse();
        Directory.GetFiles(dir).Count(file => Regex.IsMatch(Path.GetFileName(file), PostgresSnapshot.DumpPattern)).ShouldBe(14);
    }

    [Test]
    public void Plain_retention_and_verification_gate_the_prune()
    {
        var names = Enumerable.Range(1, 16).Select(day => "antiphon-202610" + day.ToString("00") + "T000000Z.dump").ToArray();
        const string newest = "antiphon-20261016T000000Z.dump";
        var pattern = PostgresSnapshot.PlainDumpPattern;
        var kept = PostgresSnapshot.PruneSet(names, 14, newest, true, pattern);
        (kept.Ok, kept.Drop.Count).ShouldBe((true, 2));
        kept.Drop.ShouldNotContain(newest);
        PostgresSnapshot.PruneSet(names, 14, newest, false, pattern).Drop.ShouldBeEmpty();
        var ignored = PostgresSnapshot.PruneSet(names.Select(name => name + ".age"), 14, newest + ".age", true, pattern);
        (ignored.Ok, ignored.Drop.Count).ShouldBe((false, 0), "plain retention ignores ciphertext names");

        PostgresSnapshot.PlainVerified(["10; 1259 1 TABLE public item owner"], 2000000, 1048576).ShouldBeFalse("no TABLE DATA");
        PostgresSnapshot.PlainVerified(["10; 0 1 TABLE DATA public item owner"], 10, 1048576).ShouldBeFalse("tiny dump");
        PostgresSnapshot.PlainVerified(["10; 0 1 TABLE DATA public item owner", "11; 0 2 TABLE DATA public other owner"], 2000000, 1048576).ShouldBeTrue();
        PostgresSnapshot.PlainTableCount(["10; 0 1 TABLE DATA public item owner", "11; 0 2 TABLE DATA public other owner", "12; 1259 1 TABLE public item owner"]).ShouldBe(2);
        PostgresSnapshot.HasTableData(["x TABLE DATAX"]).ShouldBeFalse("a word boundary is required");
        PostgresSnapshot.HasTableData(["table data public"]).ShouldBeTrue("-match ignores case");
        OneDriveRetainDefault().ShouldBe(7);
    }

    private static int OneDriveRetainDefault() => PostgresSnapshot.OneDriveRetainDefault;

    // ---- the drill guards on the docker fake ----

    [Test]
    public void Drill_refuses_production_ids_aliases_unlabelled_mounts_names_and_databases()
    {
        using var world = new SnapshotWorld();
        DrillRefused(Drill(world, "prefix", ["--scratch-container", "0123abcd9f"]), "id-prefix");
        DrillRefused(Drill(world, "alias", ["--scratch-container", "snapshot-alias"]), "alias");
        DrillRefused(Drill(world, "unlabelled", ["--scratch-container", "antiphon-postgres-drill"]), "unlabelled");
        DrillRefused(Drill(world, "mount", ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon-postgres-drill-pgdata"]), "production-mount");
        var custom = Drill(world, "labelled-source", ["--container", "custom-pg", "--source-volume", "antiphon_pgdata", "--scratch-container", "antiphon-postgres-drill",
            "--scratch-volume", "antiphon-postgres-drill-pgdata"], 1048576);
        DrillRefused(custom, "source-mount");
        custom.ToolText.ShouldNotContain("volume rm");
        custom.ToolText.ShouldNotContain("run -d");
        DrillRefused(Drill(world, "production-name", ["--scratch-container", "antiphon-server2-postgres-postgres-1"]), "production-name");
        DrillRefused(Drill(world, "production-name", ["--scratch-container", "ANTIPHON-SERVER2-POSTGRES-POSTGRES-1"]), "production-name");
        DrillRefused(Drill(world, "database", ["--scratch-database", "antiphon"]), "database");
        DrillRefused(Drill(world, "database", ["--scratch-database", "Antiphon"]), "database");
        DrillRefused(Drill(world, "database", ["--database", "CustomDb", "--scratch-database", "customdb"]), "database");

        // The captured sequence of the alias refusal: the daemon, the source, both production lookups, then the scratch name.
        var alias = Drill(world, "alias", ["--scratch-container", "snapshot-alias"]);
        alias.Lines.ShouldBe(["SNAPSHOT fail exit=9 reason=drill-refused guard=alias"]);
        alias.Tools.ShouldBe(["docker context inspect --format {{.Endpoints.docker.Host}}", "docker inspect --format {{json .}} antiphon-postgres",
            "docker inspect --format {{json .}} antiphon-server2-postgres-postgres-1", "docker inspect --format {{json .}} antiphon-server2-postgres-postgres-1",
            "docker inspect --format {{json .}} snapshot-alias"]);
    }

    [Test]
    public void Drill_refuses_its_own_foreign_unlabelled_and_mismatched_volumes()
    {
        using var world = new SnapshotWorld();
        var own = Drill(world, "own-volume", ["--container", "custom-pg", "--source-volume", "antiphon_pgdata", "--scratch-container", "antiphon-postgres-drill",
            "--scratch-volume", "custom_pgdata"], 1048576);
        DrillRefused(own, "source-mount");
        VolumeUntouched(own);
        var foreign = Drill(world, "foreign-volume", ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "windmill_db"], 1048576);
        DrillRefused(foreign, "foreign-mount");
        VolumeUntouched(foreign);
        var plain = Drill(world, "plain-volume", ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "windmill_db"], 1048576);
        DrillRefused(plain, "unlabelled-volume");
        VolumeUntouched(plain);
        var mismatch = Drill(world, "mismatch", ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "windmill_db"], 1048576);
        DrillRefused(mismatch, "volume-mismatch");
        VolumeUntouched(mismatch);
        var pghost = Drill(world, "pghost", ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon-postgres-drill-pgdata"], 1048576);
        DrillRefused(pghost, "pghost");
        pghost.ToolText.ShouldNotContain("volume rm");
    }

    [Test]
    public void Drill_refuses_a_tainted_scratch_container_and_reuses_an_image_env_one()
    {
        using var world = new SnapshotWorld();
        const string volume = "antiphon-postgres-drill-pgdata";
        string[] common = ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", volume];
        foreach (var (scenario, guard) in new[]
                 {
                     ("pghostaddr", "pghost"), ("pgenv", "pg-env"), ("pgpassfile", "pg-env"), ("pgssl", "pg-env"), ("bind", "bind-mount"), ("hostnet", "host-network"),
                     ("sharednet", "shared-network"),
                 })
        {
            var run = Drill(world, scenario, common, 1048576, [volume]);
            DrillRefused(run, guard);
            run.ToolText.ShouldNotContain("volume rm", Case.Sensitive, scenario);
        }

        // CARD-1413 D-1: the image's PG_MAJOR, PG_VERSION and PGDATA are not libpq variables.
        var reused = Drill(world, "reuse", common, 1048576, [volume]);
        reused.Exit.ShouldBe(0, reused.ToString());
        reused.Lines.ShouldBe(["SNAPSHOT ok exit=0 reason=ok drill=1 bytes=1048576"]);
        reused.ToolText.ShouldNotContain("run -d");
        reused.ToolText.ShouldNotContain("volume create");
        reused.ToolText.ShouldContain("exec " + SnapshotDockerFake.ScratchId + " createdb");
        reused.ToolText.ShouldContain("exec " + SnapshotDockerFake.ScratchId + " pg_restore --no-owner");
        reused.ToolText.ShouldContain("rm -f " + SnapshotDockerFake.ScratchId);
        NoSecret(reused);
    }

    [Test]
    public void Drill_creates_labels_restores_and_removes_its_scratch_unless_kept()
    {
        using var world = new SnapshotWorld();
        var removed = Drill(world, "clean", ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon-postgres-drill-pgdata"], 1048576);
        removed.Exit.ShouldBe(0, removed.ToString());
        removed.Lines.ShouldBe(["SNAPSHOT ok exit=0 reason=ok drill=1 bytes=1048576"]);
        var run = world.RunDirectories();
        run.ShouldBeEmpty("the run directory goes");
        // The captured order (Invoke-SnapshotDrill on the clean scenario), the run directory shown as <run>.
        var stage = world.Stage;
        removed.Tools.Select(line => Regex.Replace(line, Regex.Escape(stage) + @"[\\/]run-[0-9a-f]{32}", "<run>")).ToArray().ShouldBe(
        [
            "docker context inspect --format {{.Endpoints.docker.Host}}",
            "docker inspect --format {{json .}} antiphon-postgres",
            "docker inspect --format {{json .}} antiphon-server2-postgres-postgres-1",
            "docker inspect --format {{json .}} antiphon-server2-postgres-postgres-1",
            "docker inspect --format {{json .}} antiphon-postgres-drill",
            "docker ps -a --no-trunc --filter volume=antiphon-postgres-drill-pgdata --format {{.ID}}",
            "docker volume inspect --format {{json .}} antiphon-postgres-drill-pgdata",
            "docker volume create --label antiphon.snapshot-drill=1 antiphon-postgres-drill-pgdata",
            "docker volume inspect --format {{json .}} antiphon-postgres-drill-pgdata",
            "docker run -d --name antiphon-postgres-drill --label antiphon.snapshot-drill=1 --network none -v antiphon-postgres-drill-pgdata:/var/lib/postgresql/data -e POSTGRES_HOST_AUTH_METHOD=trust -e POSTGRES_USER=antiphon postgres:16",
            "docker inspect --format {{json .}} antiphon-postgres-drill",
            "age -d -i " + world.Identity + " -o " + Path.Combine("<run>", "antiphon-drill-plain.dump")
                + " " + Path.Combine(world.Dest, "antiphon-20261021T120000Z.dump.age"),
            "docker run --rm --network none -v <run>:/work:ro --entrypoint pg_restore postgres:16 --list /work/antiphon-drill-plain.dump",
            "docker exec " + SnapshotDockerFake.ScratchId + " pg_isready -U antiphon",
            "docker exec " + SnapshotDockerFake.ScratchId + " dropdb --if-exists -U antiphon antiphon_drill",
            "docker exec " + SnapshotDockerFake.ScratchId + " createdb -U antiphon antiphon_drill",
            "docker cp " + Path.Combine("<run>", "antiphon-drill-plain.dump") + " " + SnapshotDockerFake.ScratchId + ":/tmp/antiphon-drill.dump",
            "docker exec " + SnapshotDockerFake.ScratchId + " pg_restore --no-owner --no-acl --exit-on-error -U antiphon -d antiphon_drill /tmp/antiphon-drill.dump",
            "docker exec " + SnapshotDockerFake.ScratchId + " rm -f /tmp/antiphon-drill.dump",
            "docker inspect --format {{json .}} " + SnapshotDockerFake.ScratchId,
            "docker rm -f " + SnapshotDockerFake.ScratchId,
            "docker volume inspect --format {{json .}} antiphon-postgres-drill-pgdata",
            "docker volume rm antiphon-postgres-drill-pgdata",
        ]);
        NoSecret(removed);

        var keep = Drill(world, "clean", ["--keep-scratch"], 1048576);
        keep.Exit.ShouldBe(0, keep.ToString());
        keep.ToolText.ShouldContain("antiphon.snapshot-drill=1");
        keep.ToolText.ShouldNotContain("rm -f " + SnapshotDockerFake.ScratchId);
        keep.ToolText.ShouldNotContain("volume rm");
        NoSecret(keep);

        // The script's drill returned its cleanup warnings on the result's own output stream, so the caller never printed them:
        // a scratch container whose id no longer answers is left in place silently (captured).
        world.Fake = new SnapshotDockerFake { Scenario = "clean", DumpBytes = 1048576, HasTableData = true, PlainBytes = 1048576, IdInspectFails = true };
        var silent = world.Run(["--execute", "--restore-drill", "--identity-file", world.Identity, .. world.Common]);
        silent.Lines.ShouldBe(["SNAPSHOT ok exit=0 reason=ok drill=1 bytes=1048576"]);
        silent.ToolText.ShouldNotContain("rm -f " + SnapshotDockerFake.ScratchId);
    }

    [Test]
    public void Drill_picks_the_exact_newest_snapshot_name()
    {
        using var world = new SnapshotWorld();
        var manual = Path.Combine(world.Dest, "antiphon-20261022T000000Z-manual.dump.age");
        File.WriteAllText(manual, "not a snapshot");
        string[] common = ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon-postgres-drill-pgdata"];
        var picked = Drill(world, "clean", common, 1048576);
        picked.Exit.ShouldBe(0, picked.ToString());
        Regex.IsMatch(picked.ToolText, @"age -d .*antiphon-20261021T120000Z\.dump\.age").ShouldBeTrue(picked.ToolText);
        picked.ToolText.ShouldNotContain("antiphon-20261022T000000Z-manual");
        File.Exists(manual).ShouldBeTrue();
        var only = Drill(world, "clean", common, 1048576, noSnapshot: true);
        only.Exit.ShouldBe(9, only.ToString());
        only.Lines.ShouldBe(["SNAPSHOT fail exit=9 reason=missing-snapshot"]);
        only.Tools.ShouldNotContain(line => line.StartsWith("age -d", StringComparison.Ordinal));
        only.ToolText.ShouldNotContain("pg_restore");
        File.Exists(manual).ShouldBeTrue();
        NoSecret(only);
    }

    // ---- the age publish on the docker fake ----

    [Test]
    public void Publish_rejects_a_tiny_dump_missing_table_data_and_an_age_failure()
    {
        using var world = new SnapshotWorld();
        foreach (var (bytes, table) in new[] { (100L, true), (1048576L, false) })
        {
            var run = Publish(world, bytes, table);
            run.Exit.ShouldBe(5, run.ToString());
            run.Lines.ShouldBe(["SNAPSHOT fail exit=5 reason=verify-failed"]);
            DestUntouched(world);
            NoSecret(run);
        }

        var failed = Publish(world, 1048576, true, ageExit: 1);
        failed.Exit.ShouldBe(6, failed.ToString());
        failed.Lines.ShouldBe(["SNAPSHOT fail exit=6 reason=encrypt-failed"]);
        failed.Tools.ShouldContain(line => line.StartsWith("age -R ", StringComparison.Ordinal));
        DestUntouched(world);
        world.RunDirectories().ShouldBeEmpty();
        Directory.GetFiles(world.Stage, "*", SearchOption.AllDirectories).Where(file => Regex.IsMatch(file, @"\.dump(\.age)?$")).ShouldBeEmpty();
        failed.ToolText.ShouldContain("rm -f /tmp/antiphon-snapshot-");
        NoSecret(failed);
    }

    [Test]
    public void Publish_encrypts_moves_and_records_the_status()
    {
        using var world = new SnapshotWorld();
        var run = Publish(world, 1048576, true);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(["SNAPSHOT ok exit=0 reason=ok bytes=32 kept=1"]);
        world.Dumps(world.Dest).ShouldBe(["antiphon-20261125T022622Z.dump.age"]);
        File.ReadAllText(Path.Combine(world.Dest, "snapshot-status.json")).ShouldBe("{\"utc\":\"20261125T022622Z\",\"exit\":0,\"bytes\":32}");
        world.RunDirectories().ShouldBeEmpty();
        run.Tools.Select(line => Regex.Replace(line, Regex.Escape(world.Stage) + @"[\\/]run-[0-9a-f]{32}", "<run>")).ToArray().ShouldBe(
        [
            "docker context inspect --format {{.Endpoints.docker.Host}}",
            "docker exec antiphon-postgres pg_dump -Fc --no-owner --no-acl -U antiphon -d antiphon -f /tmp/antiphon-snapshot-20261125T022622Z.dump",
            "docker exec antiphon-postgres pg_restore --list /tmp/antiphon-snapshot-20261125T022622Z.dump",
            "docker exec antiphon-postgres wc -c /tmp/antiphon-snapshot-20261125T022622Z.dump",
            "docker cp antiphon-postgres:/tmp/antiphon-snapshot-20261125T022622Z.dump " + Path.Combine("<run>", "antiphon-20261125T022622Z.dump"),
            "age -R " + world.Recipient + " -o " + Path.Combine("<run>", "antiphon-20261125T022622Z.dump.age") + " " + Path.Combine("<run>", "antiphon-20261125T022622Z.dump"),
            "docker exec antiphon-postgres rm -f /tmp/antiphon-snapshot-20261125T022622Z.dump",
        ]);
        NoSecret(run);
    }

    [Test]
    public void Docker_daemon_is_pinned_to_a_local_endpoint_unless_allowed()
    {
        using var world = new SnapshotWorld();
        string[] common = ["--scratch-container", "antiphon-postgres-drill", "--scratch-volume", "antiphon-postgres-drill-pgdata"];
        var tcp = Drill(world, "clean", common, 1048576, env: new Dictionary<string, string> { ["DOCKER_HOST"] = "tcp://10.0.0.9:2375" });
        (tcp.Exit, tcp.Lines.Single()).ShouldBe((4, "SNAPSHOT fail exit=4 reason=docker-host"));
        tcp.Out.ShouldNotContain("10.0.0.9");
        NoDocker(tcp);
        NoSecret(tcp);
        world.RunDirectories().ShouldBeEmpty();

        world.Environment.Clear();
        world.Fake = new SnapshotDockerFake { Scenario = "clean", Endpoint = "tcp://10.0.0.9:2376" };
        var context = world.Run(["--execute", "--restore-drill", "--identity-file", world.Identity, .. world.Common, .. common]);
        (context.Exit, context.Lines.Single()).ShouldBe((4, "SNAPSHOT fail exit=4 reason=docker-context"));
        context.Out.ShouldNotContain("10.0.0.9");
        context.Tools.ShouldBe(["docker context inspect --format {{.Endpoints.docker.Host}}"]);
        NoSecret(context);
        world.Fake = new SnapshotDockerFake { Scenario = "clean", Endpoint = "" };
        var empty = world.Run(["--execute", "--restore-drill", "--identity-file", world.Identity, .. world.Common, .. common]);
        (empty.Exit, empty.Lines.Single()).ShouldBe((4, "SNAPSHOT fail exit=4 reason=docker-context"));
        empty.ToolText.ShouldNotContain("pg_restore");

        var unix = Drill(world, "clean", common, 1048576, env: new Dictionary<string, string> { ["DOCKER_HOST"] = "unix:///var/run/docker.sock" });
        unix.Exit.ShouldBe(0, unix.ToString());
        unix.ToolText.ShouldContain("context inspect");
        var allowed = Drill(world, "clean", [.. common, "--allow-remote-docker"], 1048576, env: new Dictionary<string, string> { ["DOCKER_HOST"] = "tcp://10.0.0.9:2375" });
        allowed.Exit.ShouldBe(0, allowed.ToString());
        allowed.Lines.ShouldBe(["warning: remote docker endpoint allowed by -AllowRemoteDocker", "SNAPSHOT ok exit=0 reason=ok drill=1 bytes=1048576"]);
        allowed.ToolText.ShouldNotContain("context inspect");

        var before = world.Dumps(world.Dest).Length;
        var publish = Publish(world, 1048576, true, new Dictionary<string, string> { ["DOCKER_HOST"] = "tcp://10.0.0.9:2375" });
        (publish.Exit, publish.Lines.Single()).ShouldBe((4, "SNAPSHOT fail exit=4 reason=docker-host"));
        NoDocker(publish);
        world.Dumps(world.Dest).Length.ShouldBe(before);
        File.Exists(Path.Combine(world.Dest, "snapshot-status.json")).ShouldBeFalse();
        world.Environment.Clear();
        world.Fake = new SnapshotDockerFake { Scenario = "publish", DumpBytes = 1048576, HasTableData = true, Endpoint = "tcp://10.0.0.9:2376" };
        var publishContext = world.Run(["--execute", "--recipient-file", world.Recipient, .. world.Common]);
        (publishContext.Exit, publishContext.Lines.Single()).ShouldBe((4, "SNAPSHOT fail exit=4 reason=docker-context"));
        publishContext.ToolText.ShouldNotContain("pg_dump");
        File.Exists(Path.Combine(world.Dest, "snapshot-status.json")).ShouldBeFalse();
    }

    // ---- staging ----

    [Test]
    public void Staging_keeps_operator_files_and_removes_only_its_run_directory()
    {
        using var world = new SnapshotWorld();
        var stale = Path.Combine(world.Stage, "leftover.dump");
        var notes = Path.Combine(world.Stage, "notes.txt");
        var backup = Path.Combine(world.Stage, "dev-backup-20261020.dump");
        File.WriteAllText(stale, "PLAINTEXT-STALE-1405");
        File.WriteAllText(notes, "operator notes");
        File.WriteAllText(backup, "operator dump");

        var refused = Drill(world, "prefix", ["--scratch-container", "0123abcd9f"]);
        DrillRefused(refused, "id-prefix");
        var published = Publish(world, 1048576, true);
        published.Exit.ShouldBe(0, published.ToString());
        foreach (var file in new[] { stale, notes, backup }) File.Exists(file).ShouldBeTrue(file);
        Directory.Exists(world.Stage).ShouldBeTrue();
        world.RunDirectories().ShouldBeEmpty();
        (published.Out + published.Error).ShouldNotContain("PLAINTEXT-STALE-1405");
        NoSecret(published);
    }

    [Test]
    public void Staging_sweeps_only_stale_unlocked_owned_run_files()
    {
        using var world = new SnapshotWorld();
        var dir = world.PathOf("stage-sweep");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "old.dump"), "x");
        File.WriteAllText(Path.Combine(dir, "keep.dump.age"), "y");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "z");
        const string dumpName = "antiphon-20261021T010101Z.dump";
        var old = DateTime.UtcNow.AddHours(-8);
        string Run(string pair, string content, bool aged, params string[] extra)
        {
            var path = Path.Combine(dir, "run-" + string.Concat(Enumerable.Repeat(pair, 16)));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, dumpName), content);
            foreach (var name in extra) File.WriteAllText(Path.Combine(path, name), "keep");
            if (aged) Directory.SetLastWriteTimeUtc(path, old);
            return path;
        }

        var stale = Run("aa", "stale", true);
        var live = Run("bb", "live", false);
        using (File.Open(Path.Combine(live, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Directory.SetLastWriteTimeUtc(live, old);
            var fresh = Run("cc", "fresh", false);
            var mixed = Run("dd", "mixed", true, "notes.txt", "dev-backup-20261020.dump");
            var session = world.Session();
            session.InitializeStaging(dir).ShouldBeTrue();
            File.Exists(Path.Combine(dir, "old.dump")).ShouldBeTrue();
            File.Exists(Path.Combine(dir, "keep.dump.age")).ShouldBeTrue();
            File.Exists(Path.Combine(dir, "notes.txt")).ShouldBeTrue();
            Directory.Exists(stale).ShouldBeFalse("an old empty run directory is removed");
            File.Exists(Path.Combine(live, dumpName)).ShouldBeTrue("a locked run keeps its plaintext");
            File.Exists(Path.Combine(live, "run.lock")).ShouldBeTrue("a locked run keeps its lock");
            File.Exists(Path.Combine(fresh, dumpName)).ShouldBeTrue("a fresh run keeps its plaintext");
            File.Exists(Path.Combine(mixed, dumpName)).ShouldBeFalse("an old run's plaintext is swept");
            File.Exists(Path.Combine(mixed, "notes.txt")).ShouldBeTrue();
            File.Exists(Path.Combine(mixed, "dev-backup-20261020.dump")).ShouldBeTrue();
            var run = session.RunStaging;
            Regex.IsMatch(Path.GetFileName(run), "^run-[0-9a-f]{32}$").ShouldBeTrue(run);
            Directory.GetDirectories(dir).Length.ShouldBe(4, "live, fresh, mixed and this run");
            var lockPath = Path.Combine(run, "run.lock");
            File.Exists(lockPath).ShouldBeTrue();
            PostgresSnapshotSession.RunLockFree(lockPath).ShouldBeFalse("this run's lock is held against another open");
            session.RemoveStaging(run).ShouldBeTrue();
            Directory.Exists(run).ShouldBeFalse();
        }

        // A run directory that is not the caller's is left alone (the sweep checks ownership first).
        var foreign = Run("ee", "foreign", true);
        world.FakePlatform!.Open.Add(Path.GetFullPath(foreign));
        world.Session().InitializeStaging(dir).ShouldBeTrue();
        File.Exists(Path.Combine(foreign, dumpName)).ShouldBeTrue("a foreign run keeps its files");
    }

    [Test]
    public void Staging_link_is_refused_and_the_outside_tree_is_untouched()
    {
        using var world = new SnapshotWorld();
        var outside = world.PathOf("outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "other.dump"), "outside dump");
        File.WriteAllText(Path.Combine(outside, "notes.txt"), "outside notes");
        var link = world.PathOf("stage-link");
        Directory.CreateSymbolicLink(link, outside);
        var run = world.Run(["--execute", "--recipient-file", world.Recipient, "--destination", world.Dest, "--sync-root", world.Sync, "--staging-path", link]);
        (run.Exit, run.Lines.Single()).ShouldBe((3, "SNAPSHOT fail exit=3 reason=staging-link"));
        File.Exists(Path.Combine(outside, "other.dump")).ShouldBeTrue();
        File.Exists(Path.Combine(outside, "notes.txt")).ShouldBeTrue();
        NoDocker(run);
        NoSecret(run);

        var plainLink = world.Run(["--what-if", "--plain", "--destination", world.PathOf("backups"), "--sync-root", world.Sync, "--staging-path", link]);
        plainLink.Lines.ShouldBe([Banner, "step destination: " + world.PathOf("backups") + " outside-onedrive=yes", "SNAPSHOT fail exit=3 reason=staging-link"]);
        var destLink = world.Run(["--what-if", "--plain", "--destination", Path.Combine(link, "backups"), "--sync-root", world.Sync, "--staging-path", world.Stage]);
        destLink.Lines.ShouldBe([Banner, "SNAPSHOT fail exit=3 reason=destination-link"]);
        destLink.Exit.ShouldBe(3);
    }

    [Test]
    public void Posix_staging_refuses_a_foreign_root_a_loose_mode_and_an_open_parent()
    {
        // StagingRefusesForeignRoot on the POSIX rules (the driver ran it on Linux only; the fake platform runs it anywhere).
        using var world = new SnapshotWorld(new FakeSnapshotPlatform { IsWindows = false });
        var platform = world.FakePlatform!;
        string[] common = ["--execute", "--recipient-file", world.Recipient, "--destination", world.Dest, "--sync-root", world.Sync];
        world.Fake = new SnapshotDockerFake { Scenario = "publish", DumpBytes = 1048576, HasTableData = true, PlainBytes = 32 };

        var foreign = world.PathOf("usr-share");
        Directory.CreateDirectory(foreign);
        platform.Stats[Path.GetFullPath(foreign)] = new SnapshotUnixStat(0, Convert.ToInt32("755", 8));
        var owner = world.Run([.. common, "--staging-path", foreign]);
        (owner.Exit, owner.Lines.Single()).ShouldBe((3, "SNAPSHOT fail exit=3 reason=staging-owner"));
        NoDocker(owner);
        NoSecret(owner);
        world.RunDirectories(foreign).ShouldBeEmpty();

        var loose = world.PathOf("loose");
        Directory.CreateDirectory(loose);
        platform.Open.Add(Path.GetFullPath(loose));
        var mode = world.Run([.. common, "--staging-path", loose]);
        (mode.Exit, mode.Lines.Single()).ShouldBe((3, "SNAPSHOT fail exit=3 reason=staging-mode"));
        NoDocker(mode);
        platform.Restricted.ShouldBeEmpty("the mode is unchanged");
        Directory.GetDirectories(loose).ShouldBeEmpty();

        var open = world.PathOf("open");
        var under = Path.Combine(open, "stage");
        Directory.CreateDirectory(under);
        platform.Stats[Path.GetFullPath(open)] = new SnapshotUnixStat(1000, Convert.ToInt32("777", 8));
        var parent = world.Run([.. common, "--staging-path", under]);
        (parent.Exit, parent.Lines.Single()).ShouldBe((3, "SNAPSHOT fail exit=3 reason=staging-parent"));
        NoDocker(parent);

        var sticky = world.PathOf("sticky");
        var underSticky = Path.Combine(sticky, "stage");
        Directory.CreateDirectory(underSticky);
        platform.Stats[Path.GetFullPath(sticky)] = new SnapshotUnixStat(0, Convert.ToInt32("1777", 8));
        world.Run([.. common, "--staging-path", underSticky]).Out.ShouldNotContain("reason=staging-");

        var otherParent = world.PathOf("other");
        var underOther = Path.Combine(otherParent, "stage");
        Directory.CreateDirectory(underOther);
        platform.Stats[Path.GetFullPath(otherParent)] = new SnapshotUnixStat(4242, Convert.ToInt32("755", 8));
        world.Run([.. common, "--staging-path", underOther]).Lines.Single().ShouldBe("SNAPSHOT fail exit=3 reason=staging-owner");

        var fresh = world.PathOf("fresh");
        var created = world.Run([.. common, "--staging-path", fresh]);
        created.Out.ShouldNotContain("reason=staging-");
        Directory.Exists(fresh).ShouldBeTrue("an absent root is created");
        platform.Created.ShouldContain(Path.GetFullPath(fresh), "created owner-only in one step");
    }

    [Test]
    public void Linux_staging_uses_the_real_owner_and_mode()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var world = new SnapshotWorld(new PostgresSnapshotOs());
        var session = world.Session();
        session.StagingRoot("/usr/share").ShouldBe("staging-owner");
        var fresh = world.PathOf("fresh");
        File.SetUnixFileMode(world.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        session.StagingRoot(fresh).ShouldBeOneOf("", "staging-parent");
        File.GetUnixFileMode(fresh).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, "created 0700");
        var loose = world.PathOf("loose");
        Directory.CreateDirectory(loose);
        File.SetUnixFileMode(loose, (UnixFileMode)Convert.ToInt32("755", 8));
        new PostgresSnapshotSession(world.Host(new StringWriter(), new StringWriter()), world.Seams).OwnedDirectory(loose).ShouldBeFalse();
        File.GetUnixFileMode(loose).ShouldBe((UnixFileMode)Convert.ToInt32("755", 8), "the check changes no mode");
    }

    // ---- CARD-1413 D-2: the Windows ACL rules ----

    [Test]
    public void Windows_acl_rules_refuse_foreign_rights_owners_and_unreadable_security()
    {
        const string me = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        const string other = "S-1-5-21-1111111111-2222222222-3333333333-1002";
        const string system = "S-1-5-18", admins = "S-1-5-32-544", users = "S-1-5-32-545", everyone = "S-1-1-0", authenticated = "S-1-5-11";
        const string trusted = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        const long full = 2032127, rx = 1179817, modify = 1245631, delete = 65536, deleteChild = 64, writeDac = 262144, takeOwner = 524288, appendData = 4, genericAll = 268435456;
        static SnapshotAclRule Rule(string sid, long rights, bool inheritOnly = false, string type = "Allow", bool inherited = false) => new(sid, type, rights, inheritOnly, inherited);
        SnapshotAclRule[] profile = [Rule(system, full, inherited: true), Rule(admins, full, inherited: true), Rule(me, full, inherited: true)];
        PostgresSnapshot.WindowsRules([Rule(me, full)], me, true).ShouldBeTrue("caller-only DACL is strict ok");
        PostgresSnapshot.WindowsRules(profile, me, true).ShouldBeTrue("profile default DACL is strict ok");
        PostgresSnapshot.WindowsRules([.. profile, Rule(users, rx, inherited: true)], me, true).ShouldBeFalse("inherited Users read is strict refused");
        PostgresSnapshot.WindowsRules([.. profile, Rule(users, rx, inherited: true)], me, false).ShouldBeTrue("inherited Users read on a parent is allowed");
        PostgresSnapshot.WindowsRules([.. profile, Rule(everyone, delete)], me, false).ShouldBeFalse("Everyone Delete on a parent");
        PostgresSnapshot.WindowsRules([.. profile, Rule(users, deleteChild)], me, false).ShouldBeFalse("Users DeleteChild on a parent");
        PostgresSnapshot.WindowsRules([.. profile, Rule(everyone, writeDac)], me, false).ShouldBeFalse("Everyone ChangePermissions on a parent");
        PostgresSnapshot.WindowsRules([.. profile, Rule(authenticated, takeOwner)], me, false).ShouldBeFalse("Authenticated Users TakeOwnership on a parent");
        PostgresSnapshot.WindowsRules([.. profile, Rule(other, full, inherited: true)], me, false).ShouldBeFalse("another account FullControl on a parent");
        PostgresSnapshot.WindowsRules([.. profile, Rule(users, genericAll)], me, false).ShouldBeFalse("generic all on a parent");
        SnapshotAclRule[] driveStyle = [.. profile, Rule(authenticated, modify, inheritOnly: true), Rule(authenticated, appendData), Rule(users, rx)];
        PostgresSnapshot.WindowsRules(driveStyle, me, false).ShouldBeTrue("drive-root create and inherit-only modify on a parent");
        PostgresSnapshot.WindowsRules(driveStyle, me, true).ShouldBeFalse("drive-root style DACL on the root");
        PostgresSnapshot.WindowsRules([Rule(me, full), Rule(everyone, full, type: "Deny")], me, true).ShouldBeTrue("a Deny entry grants nothing");
        PostgresSnapshot.WindowsRules([Rule(me, full)], "", true).ShouldBeFalse("an unknown caller");
        PostgresSnapshot.WindowsRules(null, me, true).ShouldBeFalse("an unreadable DACL");
        PostgresSnapshot.WindowsRules([Rule(other, full)], me, true).ShouldBeFalse("another account alone");
        PostgresSnapshot.WindowsRules([Rule("", full)], me, true).ShouldBeFalse("an entry without a SID");

        var sep = Path.DirectorySeparatorChar.ToString();
        var root = Path.GetFullPath(sep + "fakewin" + sep + "Users" + sep + "me" + sep + "AppData" + sep + "Local" + sep + "antiphon" + sep + "postgres-snapshot-staging");
        var chain = new List<string>();
        for (var cursor = root; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor)!)
        {
            chain.Add(cursor);
            if (Path.GetDirectoryName(cursor) is not { Length: > 0 } up || up == cursor) break;
        }
        chain.Count.ShouldBeGreaterThanOrEqualTo(4);
        var last = chain.Count - 1;
        Dictionary<string, SnapshotSecurity> Chain(string rootOwner, SnapshotAclRule[] rootRules)
        {
            var map = new Dictionary<string, SnapshotSecurity>(StringComparer.Ordinal);
            for (var i = 0; i <= last; i++)
            {
                map[chain[i]] = i == 0 ? new SnapshotSecurity(rootOwner, rootRules)
                    : i == last ? new SnapshotSecurity(trusted, driveStyle)
                    : i == last - 1 ? new SnapshotSecurity(system, [.. profile, Rule(users, rx, inherited: true), Rule(everyone, rx)])
                    : new SnapshotSecurity(me, profile);
            }
            return map;
        }
        static Func<string, SnapshotSecurity> Reader(Dictionary<string, SnapshotSecurity> map) => path => map.TryGetValue(path, out var security) ? security : null!;
        SnapshotAclRule[] callerOnly = [Rule(me, full)];
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(Chain(me, callerOnly)), false).ShouldBe("");
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(Chain(me, [.. profile, Rule(users, rx, inherited: true)])), false).ShouldBe("staging-mode");
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(Chain(other, callerOnly)), false).ShouldBe("staging-owner");
        var openParent = Chain(me, callerOnly);
        openParent[chain[3]] = new SnapshotSecurity(me, [.. profile, Rule(everyone, delete)]);
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(openParent), false).ShouldBe("staging-parent");
        var foreignParent = Chain(me, callerOnly);
        foreignParent[chain[2]] = new SnapshotSecurity(other, profile);
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(foreignParent), false).ShouldBe("staging-owner");
        var inheritOnly = Chain(me, callerOnly);
        inheritOnly[chain[1]] = new SnapshotSecurity(me, [.. profile, Rule(authenticated, modify, inheritOnly: true), Rule(authenticated, appendData)]);
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(inheritOnly), false).ShouldBe("");
        var elevated = Chain(admins, callerOnly);
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(elevated), true).ShouldBe("");
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(elevated), false).ShouldBe("staging-owner");
        var unreadable = Chain(me, callerOnly);
        unreadable.Remove(chain[0]);
        PostgresSnapshot.WindowsStagingRoot(root, me, Reader(unreadable), false).ShouldBe("staging-owner");
        var run = Path.Combine(root, "run-" + string.Concat(Enumerable.Repeat("ab", 16)));
        var runMap = new Dictionary<string, SnapshotSecurity> { [Path.GetFullPath(run)] = new(me, callerOnly) };
        PostgresSnapshot.WindowsOwnedDirectory(run, me, Reader(runMap), false).ShouldBeTrue();
        runMap[Path.GetFullPath(run)] = new SnapshotSecurity(me, [.. callerOnly, Rule(users, rx, inherited: true)]);
        PostgresSnapshot.WindowsOwnedDirectory(run, me, Reader(runMap), false).ShouldBeFalse();
        runMap[Path.GetFullPath(run)] = new SnapshotSecurity(other, callerOnly);
        PostgresSnapshot.WindowsOwnedDirectory(run, me, Reader(runMap), false).ShouldBeFalse();
    }

    [Test]
    public void Windows_private_directory_is_created_with_a_protected_caller_only_dacl()
    {
        if (!OperatingSystem.IsWindows()) return;
        LiveAcl();
    }

    [SupportedOSPlatform("windows")]
    private static void LiveAcl()
    {
        using var world = new SnapshotWorld(new PostgresSnapshotOs());
        var session = world.Session();
        var privateDir = world.PathOf("private");
        session.NewPrivateDirectory(privateDir).ShouldBeTrue();
        var acl = new DirectoryInfo(privateDir).GetAccessControl(AccessControlSections.Access);
        var caller = WindowsIdentity.GetCurrent().User!.Value;
        var names = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(rule => rule.IdentityReference.Value).ToArray();
        acl.AreAccessRulesProtected.ShouldBeTrue("the DACL is protected");
        names.ShouldNotBeEmpty();
        names.ShouldAllBe(name => name == caller, "only the caller");
        session.OwnedDirectory(privateDir).ShouldBeTrue();
        GrantUsersRead(privateDir);
        session.OwnedDirectory(privateDir).ShouldBeFalse("Users read on a run directory is refused");
        session.StagingRoot(privateDir).ShouldBe("staging-mode");
        session.RestrictDirectory(privateDir).ShouldBeTrue("Set-SnapshotPlainDirectoryAcl tightens it back");
        session.OwnedDirectory(privateDir).ShouldBeTrue();
        session.NewPrivateDirectory(privateDir).ShouldBeFalse("an existing path is never adopted");
    }

    [SupportedOSPlatform("windows")]
    private static void GrantUsersRead(string path)
    {
        var info = new DirectoryInfo(path);
        var acl = info.GetAccessControl(AccessControlSections.Access);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
    }

    // ---- the plain path's plans and refusals ----

    [Test]
    public void Plain_plan_names_the_decision_and_writes_nothing()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        var plan = world.Run(["--what-if", "--plain", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        plan.Exit.ShouldBe(0, plan.ToString());
        plan.Lines.ShouldBe([.. PlainSteps(dest), PlanOk]);
        plan.Out.ShouldNotContain("step encrypt:");
        NoSecret(plan);
        NoDocker(plan);
        Directory.Exists(dest).ShouldBeFalse();
        DestUntouched(world);
        // The default mode is the plan; --target desktop is the default target (TargetDesktopUnchanged).
        world.Run(["--plain", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]).Lines.ShouldBe([.. PlainSteps(dest), PlanOk]);
        var desktop = world.Run(["--what-if", "--plain", "--target", "desktop", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        desktop.Lines.ShouldBe([.. PlainSteps(dest), PlanOk]);
        desktop.Out.ShouldNotContain("--context");
        var named = world.Run(["--what-if", "--plain", "--target", "desktop", "--container", "antiphon-postgres", "--destination", dest, "--sync-root", world.Sync,
            "--staging-path", world.Stage]);
        named.Exit.ShouldBe(0, named.ToString()); named.Lines.ShouldBe([.. PlainSteps(dest), PlanOk]);

        var missing = world.PathOf("missing-onedrive");
        var noCloud = world.Run(["--what-if", "--plain", "--destination", dest, "--sync-root", missing, "--staging-path", world.Stage]);
        noCloud.Exit.ShouldBe(0, noCloud.ToString()); noCloud.Lines.ShouldBe([.. PlainSteps(dest), PlanOk]);
        Directory.Exists(missing).ShouldBeFalse("a missing OneDrive is not created");
    }

    [Test]
    public void Plain_refusals_inside_onedrive_an_open_acl_a_file_and_staging()
    {
        using var world = new SnapshotWorld();
        var inside = world.Run(["--what-if", "--plain", "--one-drive-copy", .. world.Common]);
        inside.Exit.ShouldBe(3, inside.ToString());
        inside.Lines.ShouldBe([Banner, "step destination: " + world.Dest + " outside-onedrive=no", "SNAPSHOT fail exit=3 reason=destination-inside-onedrive"]);
        Directory.Exists(Path.Combine(world.Sync, "Backups")).ShouldBeFalse();
        NoDocker(inside);

        var open = world.PathOf("open-backups");
        Directory.CreateDirectory(open);
        world.FakePlatform!.Open.Add(Path.GetFullPath(open));
        var acl = world.Run(["--what-if", "--plain", "--destination", open, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        acl.Exit.ShouldBe(3, acl.ToString()); acl.Lines.ShouldBe([Banner, "step destination: " + open + " outside-onedrive=yes", "step acl: user-only acl=open", "SNAPSHOT fail exit=3 reason=destination-acl"]);
        NoDocker(acl);
        world.FakePlatform.Restricted.ShouldBeEmpty("a plan does not tighten");

        var file = world.PathOf("dest-file");
        File.WriteAllText(file, "x");
        world.Run(["--what-if", "--plain", "--destination", file, "--sync-root", world.Sync, "--staging-path", world.Stage]).Lines
            .ShouldBe([Banner, "step destination: " + file + " outside-onedrive=yes", "SNAPSHOT fail exit=3 reason=destination-acl"]);
        var bk = world.PathOf("bk");
        world.Run(["--what-if", "--plain", "--destination", bk, "--sync-root", world.Sync, "--staging-path", Path.Combine(bk, "stage")]).Lines
            .ShouldBe([Banner, "step destination: " + bk + " outside-onedrive=yes", "SNAPSHOT fail exit=3 reason=staging-inside-destination"]);
        world.Run(["--what-if", "--plain", "--destination", bk, "--sync-root", world.Sync, "--staging-path", Path.Combine(world.Sync, "stage")]).Lines
            .ShouldBe([Banner, "step destination: " + bk + " outside-onedrive=yes", "SNAPSHOT fail exit=3 reason=staging-inside-sync"]);

        foreach (var arguments in new[]
                 {
                     new[] { "--what-if", "--plain", "--restore-drill" }, ["--what-if", "--plain", "--restore-drill", "--target", "desktop"],
                     ["--execute", "--plain", "--restore-drill", "--target", "desktop"],
                 })
        {
            var drill = world.Run([.. arguments, "--destination", world.PathOf("backups"), "--sync-root", world.Sync, "--staging-path", world.Stage, "--identity-file", world.Identity]);
            drill.Exit.ShouldBe(2, drill.ToString()); drill.Lines.ShouldBe([Banner, "SNAPSHOT fail exit=2 reason=plain-drill-unsupported"]);
            NoDocker(drill);
            Directory.Exists(world.PathOf("backups")).ShouldBeFalse();
        }
    }

    [Test]
    public void Plain_open_acl_is_detected_on_the_real_system_and_left_on_a_plan()
    {
        using var world = new SnapshotWorld(new PostgresSnapshotOs());
        var open = world.PathOf("open-backups");
        Directory.CreateDirectory(open);
        if (OperatingSystem.IsWindows()) GrantUsersRead(open);
        else File.SetUnixFileMode(open, (UnixFileMode)Convert.ToInt32("755", 8));
        var plan = world.Run(["--what-if", "--plain", "--destination", open, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        plan.Exit.ShouldBe(3, plan.ToString());
        plan.Out.ShouldContain("step acl: user-only acl=open");
        plan.Out.ShouldContain("reason=destination-acl");
        Directory.Exists(open).ShouldBeTrue();
        if (OperatingSystem.IsWindows()) UsersAceRemains(open).ShouldBeTrue("a plan does not tighten");
        else File.GetUnixFileMode(open).ShouldBe((UnixFileMode)Convert.ToInt32("755", 8));
    }

    [SupportedOSPlatform("windows")]
    private static bool UsersAceRemains(string path) =>
        new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference.Value == "S-1-5-32-545");

    [Test]
    public void Plain_onedrive_plan_names_the_copy_and_creates_nothing()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        var hermes = Path.Combine(world.Sync, "Backups", "Hermes");
        Directory.CreateDirectory(hermes);
        File.WriteAllText(Path.Combine(hermes, "keep.txt"), "keep-hermes");
        var on = world.Run(["--what-if", "--plain", "--one-drive-copy", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        on.Exit.ShouldBe(0, on.ToString());
        on.Lines.ShouldBe([.. PlainSteps(dest, oneDrive: world.Sync), PlanOk]);
        on.Out.ShouldContain("unencrypted");
        Directory.Exists(Path.Combine(world.Sync, "Backups", "antiphon-postgres")).ShouldBeFalse();
        Directory.Exists(dest).ShouldBeFalse();
        File.Exists(Path.Combine(hermes, "keep.txt")).ShouldBeTrue();
        NoSecret(on);
        NoDocker(on);
        var off = world.Run(["--what-if", "--plain", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        off.Out.ShouldNotContain("step onedrive:");
        off.Out.ShouldNotContain("keep 7");
        File.ReadAllText(Path.Combine(Repo, "scripts", "postgres-snapshot.ps1")).ShouldContain("[switch]$OneDriveCopy");
    }

    // ---- the plain status ----

    [Test]
    public void Plain_status_is_written_through_a_partial_and_carries_no_secret()
    {
        using var world = new SnapshotWorld();
        var dir = world.PathOf("status");
        Directory.CreateDirectory(dir);
        PostgresSnapshotSession.WritePlainStatus(dir, "20261110T075000Z", 0, 1234, "abcdef123456", 7, 4).ShouldBeTrue();
        var final = Path.Combine(dir, "snapshot-status.json");
        File.Exists(final + ".partial").ShouldBeFalse();
        var json = File.ReadAllText(final);
        json.ShouldBe("{\"utc\":\"20261110T075000Z\",\"exit\":0,\"bytes\":1234,\"sha256\":\"abcdef123456\",\"duration\":7,\"tables\":4,\"mode\":\"plain\",\"encryption\":\"none\","
            + "\"operatorDecision\":\"2026-11-10\",\"target\":\"desktop\",\"serverVersion\":\"unknown\"}");
        Regex.IsMatch(json, @"(?i)password\s*=|host\s*=|postgres://|CANARY").ShouldBeFalse();

        // D-8: the target and the server version close the record.
        foreach (var (target, version, expected) in new[]
                 {
                     ("server2", "16.14", "\"target\":\"server2\",\"serverVersion\":\"16.14\""),
                     ("server2", "16.14 (Debian 16.14-1.pgdg13+1)", "\"target\":\"server2\",\"serverVersion\":\"16.14\""),
                     ("server2", "postgres (PostgreSQL) 16.14 (Debian 16.14-1.pgdg13+1)", "\"target\":\"server2\",\"serverVersion\":\"16.14\""),
                     ("server2", "not a version", "\"target\":\"server2\",\"serverVersion\":\"unknown\""),
                     ("server2", "16.14.1", "\"target\":\"server2\",\"serverVersion\":\"unknown\""),
                     ("desktop", "16.14", "\"target\":\"desktop\",\"serverVersion\":\"16.14\""),
                 })
        {
            PostgresSnapshotSession.WritePlainStatus(dir, "20261118T075000Z", 0, 1234, "abcdef123456", 7, 4, target: target, serverVersion: version).ShouldBeTrue(version);
            var text = File.ReadAllText(final);
            text.ShouldEndWith(expected + "}", Case.Sensitive, version);
            text.ShouldContain("\"mode\":\"plain\"");
        }
        PostgresSnapshotSession.WritePlainStatus(dir, "20261118T075000Z", 0, 1, "abcdef123456", 1, 1, target: "server3", serverVersion: "16.14").ShouldBeFalse();
        PostgresSnapshotSession.WritePlainStatus(dir, "2026-11-18", 0, 1, "abcdef123456", 1, 1).ShouldBeFalse("utc shape");
        PostgresSnapshotSession.WritePlainStatus(world.PathOf("nowhere"), "20261118T075000Z", 0, 1, "abcdef123456", 1, 1).ShouldBeFalse("missing directory");
        PostgresSnapshotSession.WritePlainStatus(dir, "20261118T075000Z", 0, 1, "nothex", -3, -4).ShouldBeTrue();
        File.ReadAllText(final).ShouldContain("\"sha256\":\"000000000000\",\"duration\":0,\"tables\":0");
    }

    // ---- the OneDrive copy ----

    [Test]
    public void OneDrive_copy_is_missing_failed_or_ok_and_keeps_seven()
    {
        using var world = new SnapshotWorld();
        var session = world.Session();
        var sync = world.PathOf("OneDrive");
        var local = world.PathOf("antiphon-backups");
        var outside = world.PathOf("outside");
        var missingRoot = world.PathOf("missing-onedrive");
        var statusDir = world.PathOf("status");
        foreach (var dir in new[] { sync, local, outside, statusDir }) Directory.CreateDirectory(dir);
        const string localName = "antiphon-20261110T104159Z.dump";
        var localFile = Path.Combine(local, localName);
        File.WriteAllText(localFile, "PLAIN-DUMP-BYTES\r\n");
        var canary = Path.Combine(outside, "canary.txt");
        File.WriteAllText(canary, "CANARY password=secret postgres://user Host=db");

        var missing = session.OneDriveCopy(localFile, missingRoot, 7);
        (missing.State, missing.Loud).ShouldBe(("missing", "ONEDRIVE COPY oneDrive=missing"));
        Directory.Exists(missingRoot).ShouldBeFalse();
        File.Exists(localFile).ShouldBeTrue();
        File.Exists(canary).ShouldBeTrue();

        var noBackups = session.OneDriveCopy(localFile, sync, 7);
        noBackups.State.ShouldBe("missing");
        Directory.GetFileSystemEntries(sync).ShouldBeEmpty("a missing Backups writes nothing under the sync root");

        var backups = Path.Combine(sync, "Backups");
        Directory.CreateDirectory(Path.Combine(backups, "Hermes"));
        Directory.CreateDirectory(Path.Combine(backups, "openclaw"));
        File.WriteAllText(Path.Combine(backups, "Hermes", "keep.txt"), "keep-hermes\n");
        File.WriteAllText(Path.Combine(backups, "openclaw", "keep.txt"), "keep-openclaw");
        var child = Path.Combine(backups, "antiphon-postgres");
        Directory.CreateDirectory(child);
        var badPartial = Path.Combine(child, localName + ".partial");
        File.WriteAllText(badPartial, "DIFFERENT-BYTES");
        PostgresSnapshotSession.ConfirmOneDriveFile(localFile, badPartial, Path.Combine(child, localName)).ShouldBeFalse("a hash mismatch does not publish");
        File.Exists(badPartial).ShouldBeFalse("the mismatched partial goes");
        File.Exists(Path.Combine(child, localName)).ShouldBeFalse();

        var copied = session.OneDriveCopy(localFile, sync, 7);
        copied.State.ShouldBe("ok");
        var final = Path.Combine(child, localName);
        File.Exists(final).ShouldBeTrue();
        File.Exists(final + ".partial").ShouldBeFalse();
        File.ReadAllBytes(final).ShouldBe(File.ReadAllBytes(localFile), "on-disk digest recheck: the copy's bytes are the local dump's");
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(final)))
            .ShouldBe(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(localFile))));
        copied.Bytes.ShouldBe(new FileInfo(final).Length);
        copied.Loud.ShouldBe("ONEDRIVE COPY oneDrive=ok bytes=" + new FileInfo(final).Length);
        Path.GetFullPath(copied.Folder).ShouldBe(Path.GetFullPath(child));
        File.ReadAllText(Path.Combine(backups, "Hermes", "keep.txt")).ShouldBe("keep-hermes\n");
        File.ReadAllText(Path.Combine(backups, "openclaw", "keep.txt")).ShouldBe("keep-openclaw");
        File.Exists(canary).ShouldBeTrue();
        Directory.GetFileSystemEntries(sync).Select(Path.GetFileName).ShouldBe(["Backups"]);
        Regex.IsMatch(copied.Loud, @"(?i)password\s*=|postgres://|Host\s*=|CANARY").ShouldBeFalse();
        // A second copy of the same bytes is ok without rewriting.
        session.OneDriveCopy(localFile, sync, 7).State.ShouldBe("ok");

        File.Delete(final);
        for (var day = 1; day <= 8; day++) File.WriteAllText(Path.Combine(child, "antiphon-202611" + day.ToString("00") + "T000000Z.dump"), "old");
        for (var day = 1; day <= 14; day++) File.WriteAllText(Path.Combine(local, "antiphon-202610" + day.ToString("00") + "T000000Z.dump"), "local-old");
        var newest = Path.Combine(local, "antiphon-20261110T120000Z.dump");
        File.WriteAllText(newest, "NEW-DUMP");
        var localBefore = Directory.GetFiles(local).Count(file => Regex.IsMatch(Path.GetFileName(file), PostgresSnapshot.PlainDumpPattern));
        session.OneDriveCopy(newest, sync, 7).State.ShouldBe("ok");
        var cloudLeft = Directory.GetFiles(child).Select(Path.GetFileName).Where(name => Regex.IsMatch(name!, PostgresSnapshot.PlainDumpPattern)).ToArray();
        cloudLeft.Length.ShouldBe(7);
        cloudLeft.ShouldContain("antiphon-20261110T120000Z.dump");
        cloudLeft.ShouldNotContain("antiphon-20261101T000000Z.dump");
        cloudLeft.ShouldNotContain("antiphon-20261102T000000Z.dump");
        File.Exists(Path.Combine(child, "antiphon-20261110T120000Z.dump.partial")).ShouldBeFalse();
        Directory.GetFiles(local).Count(file => Regex.IsMatch(Path.GetFileName(file), PostgresSnapshot.PlainDumpPattern)).ShouldBe(localBefore, "OneDrive's prune leaves the local folder");

        // The status fields of the copy.
        PostgresSnapshotSession.WritePlainStatus(statusDir, "20261110T120000Z", 0, 18, "abcdef123456", 3, 2, "ok", 18, child).ShouldBeTrue();
        var status = File.ReadAllText(Path.Combine(statusDir, "snapshot-status.json"));
        status.ShouldContain("\"oneDrive\":\"ok\",\"oneDriveBytes\":18,\"oneDriveFolder\":\"" + child.Replace("\\", "\\\\") + "\"");
        System.Text.Json.JsonDocument.Parse(status).RootElement.GetProperty("oneDriveFolder").GetString().ShouldBe(child);
        foreach (var state in new[] { "missing", "failed" })
        {
            PostgresSnapshotSession.WritePlainStatus(statusDir, "20261110T120002Z", 0, 18, "abcdef123456", 3, 2, state, 0, child).ShouldBeTrue();
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(statusDir, "snapshot-status.json"))).RootElement.GetProperty("oneDrive").GetString().ShouldBe(state);
        }
        PostgresSnapshotSession.WritePlainStatus(statusDir, "20261110T120004Z", 0, 18, "abcdef123456", 3, 2).ShouldBeTrue();
        File.ReadAllText(Path.Combine(statusDir, "snapshot-status.json")).ShouldNotContain("oneDrive");
    }

    private static string Stamp(string path)
    {
        // Get-PlainSnapshotStamp: every entry under path with its kind and bytes (links named, not followed).
        if (!Directory.Exists(path)) return "ABSENT";
        var lines = new List<string>();
        var pending = new Queue<string>([path]);
        while (pending.Count > 0)
        {
            var dir = pending.Dequeue();
            foreach (var entry in new DirectoryInfo(dir).GetFileSystemInfos().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                var rel = entry.FullName.Substring(path.Length).TrimStart('/', '\\');
                if (entry.LinkTarget != null) { lines.Add("link " + rel + " -> " + entry.LinkTarget); continue; }
                if (entry is DirectoryInfo) { lines.Add("dir " + rel); pending.Enqueue(entry.FullName); continue; }
                lines.Add("file " + rel + " " + File.ReadAllText(entry.FullName));
            }
        }
        return string.Join("\n", lines);
    }

    private static void OutsideTree(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "NOTES");
        File.WriteAllText(Path.Combine(folder, "hermes-backup.dump"), "HERMES");
        for (var day = 1; day <= 8; day++) File.WriteAllText(Path.Combine(folder, "antiphon-202611" + day.ToString("00") + "T000000Z.dump"), "old" + day);
    }

    private static void LinkRefused(SnapshotOneDriveResult result, string before, string after, string localFile)
    {
        result.State.ShouldBe("failed", result.Loud);
        result.Loud.ShouldBe("ONEDRIVE COPY oneDrive=failed reason=link");
        after.ShouldBe(before, "the outside tree is untouched");
        File.Exists(localFile).ShouldBeTrue();
    }

    [Test]
    public void OneDrive_links_on_the_root_backups_child_or_a_dump_are_refused()
    {
        PostgresSnapshot.ForbiddenLink("", true).ShouldBeFalse("a cloud-filter reparse point is not a link");
        PostgresSnapshot.ForbiddenLink("SymbolicLink", true).ShouldBeTrue();
        PostgresSnapshot.ForbiddenLink("Junction", true).ShouldBeTrue();
        using var world = new SnapshotWorld();
        var session = world.Session();
        var local = world.PathOf("local");
        Directory.CreateDirectory(local);
        const string localName = "antiphon-20261110T150000Z.dump";
        var localFile = Path.Combine(local, localName);
        File.WriteAllText(localFile, "NEW-DUMP");

        var outside = world.PathOf("backups-link", "outside");
        var sync = world.PathOf("backups-link", "OneDrive");
        Directory.CreateDirectory(sync);
        OutsideTree(Path.Combine(outside, "antiphon-postgres"));
        File.WriteAllText(Path.Combine(outside, "keep-root.txt"), "ROOT");
        Directory.CreateSymbolicLink(Path.Combine(sync, "Backups"), outside);
        var before = Stamp(outside);
        LinkRefused(session.OneDriveCopy(localFile, sync, 7), before, Stamp(outside), localFile);

        var real = world.PathOf("root-link", "real-onedrive");
        OutsideTree(Path.Combine(real, "Backups", "antiphon-postgres"));
        var holder = world.PathOf("root-link", "holder");
        Directory.CreateDirectory(holder);
        var linkedSync = Path.Combine(holder, "OneDrive");
        Directory.CreateSymbolicLink(linkedSync, real);
        before = Stamp(real);
        LinkRefused(session.OneDriveCopy(localFile, linkedSync, 7), before, Stamp(real), localFile);

        var childSync = world.PathOf("child-link", "OneDrive");
        var childOutside = world.PathOf("child-link", "outside");
        Directory.CreateDirectory(Path.Combine(childSync, "Backups"));
        OutsideTree(Path.Combine(childOutside, "antiphon-postgres"));
        Directory.CreateSymbolicLink(Path.Combine(childSync, "Backups", "antiphon-postgres"), Path.Combine(childOutside, "antiphon-postgres"));
        before = Stamp(childOutside);
        LinkRefused(session.OneDriveCopy(localFile, childSync, 7), before, Stamp(childOutside), localFile);

        var fileSync = world.PathOf("file-link", "OneDrive");
        var fileChild = Path.Combine(fileSync, "Backups", "antiphon-postgres");
        var fileOutside = world.PathOf("file-link", "outside");
        Directory.CreateDirectory(fileChild);
        Directory.CreateDirectory(fileOutside);
        var escaped = Path.Combine(fileOutside, "escaped.dump");
        File.WriteAllText(escaped, "OUTSIDE-DUMP");
        var dumpLink = Path.Combine(fileChild, "antiphon-20261101T000000Z.dump");
        File.CreateSymbolicLink(dumpLink, escaped);
        for (var day = 2; day <= 8; day++) File.WriteAllText(Path.Combine(fileChild, "antiphon-202611" + day.ToString("00") + "T000000Z.dump"), "old" + day);
        File.WriteAllText(Path.Combine(fileChild, "notes.txt"), "NOTES");
        before = Stamp(fileOutside);
        LinkRefused(session.OneDriveCopy(localFile, fileSync, 7), before, Stamp(fileOutside), localFile);
        new FileInfo(dumpLink).LinkTarget.ShouldNotBeNull("the linked dump stays");
        File.Exists(Path.Combine(fileChild, localName)).ShouldBeFalse();
        File.ReadAllText(escaped).ShouldBe("OUTSIDE-DUMP");
    }

    // ---- the plain publish ----

    [Test]
    public void Plain_publish_prunes_only_after_the_second_list_and_the_hash_match()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("local");
        var stage = world.PathOf("stage");
        var sync = world.PathOf("OneDrive");
        var cloud = Path.Combine(sync, "Backups", "antiphon-postgres");
        foreach (var dir in new[] { dest, stage, cloud }) Directory.CreateDirectory(dir);
        for (var day = 1; day <= 15; day++) File.WriteAllText(Path.Combine(dest, "antiphon-202610" + day.ToString("00") + "T000000Z.dump"), "local" + day);
        for (var day = 1; day <= 8; day++) File.WriteAllText(Path.Combine(cloud, "antiphon-202611" + day.ToString("00") + "T000000Z.dump"), "cloud" + day);
        const int dumpBytes = 4096;
        var listExits = new List<int>();
        var failSecondList = true;
        var expectedHash = "";
        var pruneCount = 0;
        var pruneTooEarly = false;
        world.Fake.Override = args =>
        {
            var joined = string.Join(" ", args);
            if (joined.Contains("pg_restore") && joined.Contains("--list"))
            {
                var code = failSecondList && listExits.Count >= 1 ? 1 : 0;
                listExits.Add(code);
                return code != 0 ? SnapshotDockerFake.Answer(1, "verify failed") : SnapshotDockerFake.Answer(0, "3000; 0 12345 TABLE DATA public cards antiphon");
            }
            if (Regex.IsMatch(joined, "(^| )wc( |$)")) return SnapshotDockerFake.Answer(0, dumpBytes + " /tmp/antiphon-snapshot.dump");
            if (args[0] == "cp")
            {
                var target = args[^1];
                if (Regex.IsMatch(target, @"^[A-Za-z]:[\\/]") || !target.Contains(':'))
                {
                    File.WriteAllBytes(target, new byte[dumpBytes]);
                    expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target)));
                }
            }
            return SnapshotDockerFake.Answer(0);
        };
        var session = world.Session();
        session.PlainRetention = (dir, keep, name) =>
        {
            pruneCount++;
            var file = Path.Combine(dir, name);
            var hashOk = File.Exists(file) && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))) == expectedHash;
            var secondOk = listExits.Count >= 2 && listExits[^1] == 0;
            if (!secondOk || !hashOk) pruneTooEarly = true;
            return session.UpdatePlainRetentionCore(dir, keep, name);
        };
        var failed = PostgresSnapshot.PlainPublish(session, "antiphon-postgres", "antiphon", stage, dest, 14, world.Seams.Resolve!("docker")!, 4096, true, sync, 7);
        (failed.Code, failed.Reason).ShouldBe((5, "verify-failed"));
        pruneCount.ShouldBe(0, "a failed verify prunes nothing");
        Directory.GetFiles(dest).Count(file => Regex.IsMatch(Path.GetFileName(file), PostgresSnapshot.PlainDumpPattern)).ShouldBe(15);
        Directory.GetFiles(cloud).Length.ShouldBe(8);
        JsonExit(Path.Combine(dest, "snapshot-status.json")).ShouldBe(5, "the failure is recorded first");

        listExits.Clear();
        failSecondList = false;
        var ok = PostgresSnapshot.PlainPublish(session, "antiphon-postgres", "antiphon", stage, dest, 14, world.Seams.Resolve!("docker")!, 4096, true, sync, 7);
        (ok.Code, ok.Reason).ShouldBe((0, "ok"));
        pruneTooEarly.ShouldBeFalse("prune runs only after the verify and the hash match");
        pruneCount.ShouldBeGreaterThanOrEqualTo(1);
        listExits.Count.ShouldBeGreaterThanOrEqualTo(2, "the dump is listed twice");
        Directory.GetFiles(dest).Count(file => Regex.IsMatch(Path.GetFileName(file), PostgresSnapshot.PlainDumpPattern)).ShouldBe(14);
        File.Exists(Path.Combine(dest, "antiphon-20261001T000000Z.dump")).ShouldBeFalse();
        var cloudAfter = Directory.GetFiles(cloud).Select(Path.GetFileName).Where(name => Regex.IsMatch(name!, PostgresSnapshot.PlainDumpPattern)).ToArray();
        cloudAfter.Length.ShouldBe(7);
        cloudAfter.ShouldNotContain("antiphon-20261101T000000Z.dump");
        cloudAfter.ShouldNotContain("antiphon-20261102T000000Z.dump");
        (ok.Detail, ok.Loud).ShouldBe(("bytes=4096 kept=14 utc=20261125T022622Z", "ONEDRIVE COPY oneDrive=ok bytes=4096"));
    }

    private static int JsonExit(string path) => System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("exit").GetInt32();

    [Test]
    public void Plain_execute_on_the_desktop_copies_to_onedrive_with_the_captured_lines()
    {
        using var world = new SnapshotWorld();
        var sync = world.PathOf("OneDrive");
        Directory.CreateDirectory(Path.Combine(sync, "Backups"));
        var dest = world.PathOf("backups");
        world.Fake = new SnapshotDockerFake { Server2 = true, Scenario = "good", Endpoint = "npipe:////./pipe/docker_engine" };
        var run = world.Run(["--plain", "--execute", "--one-drive-copy", "--destination", dest, "--sync-root", sync, "--staging-path", world.Stage]);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe([.. PlainSteps(dest, oneDrive: sync), "step acl: user-only created", "ONEDRIVE COPY oneDrive=ok bytes=1048576",
            "SNAPSHOT ok exit=0 reason=ok bytes=1048576 kept=1 utc=20261125T022622Z"]);
        world.Dumps(dest, "*.dump").ShouldBe(["antiphon-20261125T022622Z.dump"]);
        File.ReadAllBytes(Path.Combine(dest, "antiphon-20261125T022622Z.dump")).ShouldBe(File.ReadAllBytes(Path.Combine(sync, "Backups", "antiphon-postgres", "antiphon-20261125T022622Z.dump")));
        var status = File.ReadAllText(Path.Combine(dest, "snapshot-status.json"));
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[1048576])).ToLowerInvariant().Substring(0, 12);
        status.ShouldBe("{\"utc\":\"20261125T022622Z\",\"exit\":0,\"bytes\":1048576,\"sha256\":\"" + sha + "\",\"duration\":0,\"tables\":2,\"mode\":\"plain\",\"encryption\":\"none\","
            + "\"operatorDecision\":\"2026-11-10\",\"oneDrive\":\"ok\",\"oneDriveBytes\":1048576,\"oneDriveFolder\":\"" + Path.Combine(sync, "Backups", "antiphon-postgres").Replace("\\", "\\\\")
            + "\",\"target\":\"desktop\",\"serverVersion\":\"16.14\"}");
        world.FakePlatform!.Created.ShouldContain(Path.GetFullPath(dest), "the destination is created owner-only");
        world.RunDirectories().ShouldBeEmpty();

        // An open destination is tightened on an execute (a plan only reports it).
        var open = world.PathOf("open-dest");
        Directory.CreateDirectory(open);
        world.FakePlatform.Open.Add(Path.GetFullPath(open));
        world.Fake = new SnapshotDockerFake { Server2 = true, Scenario = "good", Endpoint = "npipe:////./pipe/docker_engine" };
        var tightened = world.Run(["--plain", "--execute", "--destination", open, "--sync-root", sync, "--staging-path", world.Stage]);
        tightened.Exit.ShouldBe(0, tightened.ToString());
        tightened.Lines.ShouldContain("step acl: user-only acl=open");
        tightened.Lines.ShouldContain("step acl: tightened to user-only");
        world.FakePlatform.Restricted.ShouldContain(Path.GetFullPath(open));

        // A missing Backups folder is a warning, not a failure (captured on server2).
        var bare = world.PathOf("bare-onedrive");
        Directory.CreateDirectory(bare);
        world.Fake = new SnapshotDockerFake { Server2 = true, Scenario = "good" };
        var missing = world.Run(["--plain", "--execute", "--one-drive-copy", "--target", "server2", "--destination", world.PathOf("backups2"), "--sync-root", bare,
            "--staging-path", world.Stage]);
        missing.Exit.ShouldBe(0, missing.ToString());
        missing.Lines[^2..].ShouldBe(["ONEDRIVE COPY oneDrive=missing", "SNAPSHOT ok exit=0 reason=ok bytes=1048576 kept=1 utc=20261125T022622Z"]);
        Directory.GetFileSystemEntries(bare).ShouldBeEmpty();

        // Verify failure on the desktop records exit 5 (captured).
        world.Fake = new SnapshotDockerFake { Scenario = "publish", DumpBytes = 1048576, HasTableData = false };
        var verify = world.Run(["--plain", "--execute", "--destination", world.PathOf("backups3"), "--sync-root", world.Sync, "--staging-path", world.Stage]);
        verify.Exit.ShouldBe(5, verify.ToString());
        verify.Lines[^1].ShouldBe("SNAPSHOT fail exit=5 reason=verify-failed");
        verify.Tools.ShouldBe(["docker context inspect --format {{.Endpoints.docker.Host}}", "docker exec antiphon-postgres postgres --version",
            "docker exec antiphon-postgres pg_dump -Fc --no-owner --no-acl -U antiphon -d antiphon -f /tmp/antiphon-snapshot-20261125T022622Z.dump",
            "docker exec antiphon-postgres pg_restore --list /tmp/antiphon-snapshot-20261125T022622Z.dump",
            "docker exec antiphon-postgres wc -c /tmp/antiphon-snapshot-20261125T022622Z.dump",
            "docker exec antiphon-postgres rm -f /tmp/antiphon-snapshot-20261125T022622Z.dump /tmp/antiphon-snapshot-verify-20261125T022622Z.dump"]);
        JsonExit(Path.Combine(world.PathOf("backups3"), "snapshot-status.json")).ShouldBe(5);
    }

    // ---- -Target server2 ----

    [Test]
    public void Server2_plan_names_the_source_and_the_context_and_never_the_endpoint()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        var plan = world.Run(["--what-if", "--plain", "--target", "server2", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        plan.Exit.ShouldBe(0, plan.ToString());
        plan.Lines.ShouldBe([.. PlainSteps(dest, "antiphon-server2-postgres-postgres-1", server2: true), PlanOk]);
        plan.Out.ShouldNotContain("docker exec antiphon-postgres ");
        plan.Out.ShouldNotContain("ssh://");
        plan.Out.ShouldNotContain("@");
        plan.Out.ShouldNotContain("postgres://");
        NoDocker(plan);
        foreach (var name in new[] { "antiphon-postgres", "antiphon-postgres-drill", "antiphon-server2-postgres-postgres-1", "" })
        {
            var refused = world.Run(["--what-if", "--plain", "--target", "server2", "--container", name, "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
            refused.Exit.ShouldBe(2, refused.ToString()); refused.Lines.ShouldBe([Banner, "SNAPSHOT fail exit=2 reason=ambiguous-mode"], name);
            NoDocker(refused);
        }
    }

    [Test]
    public void Server2_execute_names_the_context_on_every_call_and_records_target_and_version()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        world.Fake = new SnapshotDockerFake { Server2 = true, Scenario = "good" };
        var run = world.Run(["--execute", "--plain", "--target", "server2", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage]);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe([.. PlainSteps(dest, "antiphon-server2-postgres-postgres-1", server2: true), "step acl: user-only created",
            "SNAPSHOT ok exit=0 reason=ok bytes=1048576 kept=1 utc=20261125T022622Z"]);
        run.Tools.Length.ShouldBeGreaterThanOrEqualTo(6);
        run.Tools.ShouldAllBe(line => line.StartsWith("docker --context antiphon-server2 ", StringComparison.Ordinal));
        run.Tools.Count(line => line.Contains(" context inspect antiphon-server2 ")).ShouldBe(1);
        run.Tools.Count(line => line.Contains(" exec antiphon-server2-postgres-postgres-1 pg_dump ")).ShouldBe(1);
        run.Tools.Count(line => line.Contains(" exec antiphon-server2-postgres-postgres-1 pg_restore --list ")).ShouldBe(2);
        run.Tools.Count(line => line.Contains("antiphon-postgres ") || line.EndsWith("antiphon-postgres", StringComparison.Ordinal)).ShouldBe(0);
        world.Dumps(dest, "*.dump").Length.ShouldBe(1);
        File.ReadAllText(Path.Combine(dest, "snapshot-status.json")).ShouldContain("\"exit\":0");
        File.ReadAllText(Path.Combine(dest, "snapshot-status.json")).ShouldEndWith("\"target\":\"server2\",\"serverVersion\":\"16.14\"}");
        (run.Out + run.Error).ShouldNotContain("ssh://");
        (run.Out + run.Error).ShouldNotContain("fake@server2");
    }

    [Test]
    public void Server2_refuses_a_tcp_context_any_docker_host_and_a_mismatched_source()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        string[] arguments = ["--execute", "--plain", "--target", "server2", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage];
        foreach (var (endpoint, host, reason) in new[] { ("tcp://10.0.0.1:2375", (string?)null, "docker-context"), (null, "ssh://x", "docker-host"), (null, "unix:///var/run/docker.sock", "docker-host") })
        {
            world.Fake = new SnapshotDockerFake { Server2 = true, Endpoint = endpoint };
            world.Environment.Clear();
            if (host != null) world.Environment["DOCKER_HOST"] = host;
            var run = world.Run(arguments);
            run.Exit.ShouldBe(4, run.ToString());
            run.Lines[^1].ShouldBe("SNAPSHOT fail exit=4 reason=" + reason);
            run.ToolText.ShouldNotContain(" exec ");
            run.Out.ShouldNotContain("10.0.0.1");
            run.Out.ShouldNotContain("ssh://");
            world.Dumps(dest, "*.dump").ShouldBeEmpty();
        }
        world.Environment.Clear();
        foreach (var scenario in new[] { "drill-project", "drill-volume", "wrong-dest", "stopped" })
        {
            world.Fake = new SnapshotDockerFake { Server2 = true, Scenario = scenario };
            var run = world.Run(arguments);
            run.Exit.ShouldBe(9, run.ToString());
            run.Lines[^1].ShouldBe("SNAPSHOT fail exit=9 reason=source-mismatch");
            run.ToolText.ShouldNotContain("pg_dump");
            run.Tools.ShouldBe(["docker --context antiphon-server2 context inspect antiphon-server2 --format {{.Endpoints.docker.Host}}",
                "docker --context antiphon-server2 inspect --format {{json .}} antiphon-server2-postgres-postgres-1"]);
            world.Dumps(dest, "*.dump").ShouldBeEmpty();
        }
    }

    [Test]
    public void Server2_source_check_is_the_d5_evidence()
    {
        // Plan D-5: the running production container of the compose project with its pinned volume at the data path.
        SnapshotContainerFact Fact(string name = "antiphon-server2-postgres-postgres-1", bool running = true, string project = "antiphon-server2-postgres",
            string mount = "antiphon-server2-postgres_pgdata:/var/lib/postgresql/data") =>
            new() { Found = true, Parse = true, Name = name, Running = running, ComposeProject = project, VolumeMounts = [mount] };
        PostgresSnapshot.IsServer2Source(Fact()).ShouldBeTrue();
        PostgresSnapshot.IsServer2Source(Fact(name: "Antiphon-server2-postgres-postgres-1")).ShouldBeFalse("the name is case-sensitive");
        PostgresSnapshot.IsServer2Source(Fact(running: false)).ShouldBeFalse();
        PostgresSnapshot.IsServer2Source(Fact(project: "antiphon-server2-postgres-drill")).ShouldBeFalse();
        PostgresSnapshot.IsServer2Source(Fact(mount: "antiphon-server2-postgres_pgdata:/data")).ShouldBeFalse();
        PostgresSnapshot.IsServer2Source(new SnapshotContainerFact { Found = true, Parse = false }).ShouldBeFalse();
        PostgresSnapshot.IsServer2Source(SnapshotContainerFact.Missing).ShouldBeFalse();
    }

    [Test]
    public void Server2_plain_drill_restores_into_the_scratch_with_the_source_image()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        string[] arguments = ["--plain", "--restore-drill", "--target", "server2", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage];
        world.Fake = new SnapshotDockerFake { Server2 = true };
        var plan = world.Run(["--what-if", .. arguments]);
        plan.Exit.ShouldBe(0, plan.ToString()); plan.Lines.ShouldBe([.. Server2DrillPlan(dest), "SNAPSHOT plan exit=0 reason=ok drill=1 scratch=antiphon-postgres-drill"]);
        NoDocker(plan);
        var missing = world.Run(["--execute", .. arguments]);
        missing.Exit.ShouldBe(9, missing.ToString()); missing.Lines.ShouldBe([.. Server2DrillPlan(dest), "SNAPSHOT fail exit=9 reason=missing-snapshot"]);
        missing.ToolText.ShouldNotContain(" run ");

        Directory.CreateDirectory(dest);
        var dump = Path.Combine(dest, "antiphon-20261118T075000Z.dump");
        File.WriteAllBytes(dump, new byte[1048576]);
        File.WriteAllText(Path.Combine(dest, "antiphon-20261117T075000Z.dump.partial"), "partial");
        File.WriteAllBytes(Path.Combine(dest, "antiphon-20261117T075000Z.dump"), new byte[2048]);
        world.Fake = new SnapshotDockerFake { Server2 = true };
        var run = world.Run(["--execute", .. arguments]);
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe([.. Server2DrillPlan(dest), "SNAPSHOT ok exit=0 reason=ok drill=1 bytes=1048576 tables=139 seconds=0"]);
        const string c = "docker --context antiphon-server2 ";
        var id = SnapshotDockerFake.ScratchId;
        run.Tools.ShouldBe(
        [
            c + "context inspect antiphon-server2 --format {{.Endpoints.docker.Host}}",
            c + "inspect --format {{json .}} antiphon-server2-postgres-postgres-1",
            c + "inspect --format {{json .}} antiphon-server2-postgres-postgres-1",
            c + "inspect --format {{json .}} antiphon-postgres",
            c + "inspect --format {{json .}} antiphon-postgres",
            c + "inspect --format {{json .}} antiphon-postgres-drill",
            c + "ps -a --no-trunc --filter volume=antiphon-postgres-drill-pgdata --format {{.ID}}",
            c + "volume inspect --format {{json .}} antiphon-postgres-drill-pgdata",
            c + "volume create --label antiphon.snapshot-drill=1 antiphon-postgres-drill-pgdata",
            c + "volume inspect --format {{json .}} antiphon-postgres-drill-pgdata",
            c + "run -d --name antiphon-postgres-drill --label antiphon.snapshot-drill=1 --network none -v antiphon-postgres-drill-pgdata:/var/lib/postgresql/data -e POSTGRES_HOST_AUTH_METHOD=trust -e POSTGRES_USER=antiphon " + SnapshotDockerFake.Server2ImageId,
            c + "inspect --format {{json .}} antiphon-postgres-drill",
            c + "exec " + id + " pg_isready -U antiphon",
            c + "exec " + id + " dropdb --if-exists -U antiphon antiphon_drill",
            c + "exec " + id + " createdb -U antiphon antiphon_drill",
            c + "cp " + dump + " " + id + ":/tmp/antiphon-drill.dump",
            c + "exec " + id + " pg_restore --list /tmp/antiphon-drill.dump",
            c + "exec " + id + " pg_restore --no-owner --no-acl --exit-on-error -U antiphon -d antiphon_drill /tmp/antiphon-drill.dump",
            c + "exec " + id + " psql -U antiphon -d antiphon_drill -tAc select count(*) from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE'",
            c + "exec " + id + " rm -f /tmp/antiphon-drill.dump",
            c + "inspect --format {{json .}} " + id,
            c + "rm -f " + id,
            c + "volume inspect --format {{json .}} antiphon-postgres-drill-pgdata",
            c + "volume rm antiphon-postgres-drill-pgdata",
        ]);
        run.ToolText.ShouldNotContain(":/work");
        run.Tools.Count(line => line.Contains(" exec antiphon-server2-postgres-postgres-1 ")).ShouldBe(0, "nothing runs inside production");
        (run.Out + run.Error).ShouldNotContain("ssh://");

        // The drill's elapsed seconds come from the clock.
        world.Fake = new SnapshotDockerFake { Server2 = true, Override = args => { if (args.Contains("pg_isready")) world.Clock.Advance(TimeSpan.FromSeconds(42)); return null; } };
        world.Run(["--execute", .. arguments]).Lines[^1].ShouldBe("SNAPSHOT ok exit=0 reason=ok drill=1 bytes=1048576 tables=139 seconds=42");
    }

    [Test]
    public void Server2_plain_drill_refuses_production_names_volumes_and_databases()
    {
        using var world = new SnapshotWorld();
        var dest = world.PathOf("backups");
        Directory.CreateDirectory(dest);
        File.WriteAllBytes(Path.Combine(dest, "antiphon-20261118T075000Z.dump"), new byte[1048576]);
        foreach (var (extra, guard) in new[]
                 {
                     (new[] { "--scratch-container", "antiphon-server2-postgres-postgres-1" }, "production-container"),
                     (new[] { "--scratch-volume", "antiphon-server2-postgres_pgdata" }, "production-volume"),
                     (new[] { "--scratch-database", "antiphon" }, "database"),
                 })
        {
            world.Fake = new SnapshotDockerFake { Server2 = true };
            var run = world.Run(["--execute", "--plain", "--restore-drill", "--target", "server2", "--destination", dest, "--sync-root", world.Sync, "--staging-path", world.Stage, .. extra]);
            run.Exit.ShouldBe(9, run.ToString()); run.Lines.ShouldBe([Banner, "SNAPSHOT fail exit=9 reason=drill-refused guard=" + guard]);
            NoDocker(run);
        }
        var named = world.Run(["--what-if", "--plain", "--restore-drill", "--target", "server2", "--container", "x", "--destination", dest]);
        named.Lines.ShouldBe([Banner, "SNAPSHOT fail exit=2 reason=ambiguous-mode"]);
    }

    [Test]
    public void An_unexpected_io_failure_is_one_stderr_line_and_exit_1()
    {
        using var world = new SnapshotWorld();
        world.Fake = new SnapshotDockerFake { Server2 = true, Override = args => args[0] == "--context" && args.Contains("pg_dump") ? throw new IOException("disk gone") : null };
        var run = world.Run(["--execute", "--plain", "--target", "server2", "--destination", world.PathOf("backups"), "--sync-root", world.Sync, "--staging-path", world.Stage]);
        run.Exit.ShouldBe(1);
        run.Error.Trim().ShouldBe("postgres-snapshot: disk gone");
        world.RunDirectories().ShouldBeEmpty("the run directory is still removed");
    }
}
