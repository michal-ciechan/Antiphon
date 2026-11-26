using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1744 DW-3: the operator-only desktop-win phase 1 secret helper. Unit 51 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md ported scripts/desktop-win-phase1-secrets.ps1 to the
/// desktop-win-phase1-secrets command (Antiphon.Nightly.DesktopWinPhase1Secrets); these are the deleted driver's DW-3 witnesses
/// (scripts/test-desktop-win-phase1-secrets.ps1, removed with the unreferenced batch on 2026-11-23, which ran on Linux only) in
/// process. Each case has its own temporary runner root, user-secrets directory, profile home and scripts directory; chmod, stat
/// and icacls are fakes (a per-path mode table), the Windows ACLs a fake (DesktopWinAclOsTests covers the real one), the
/// environment and the clock the test's own, and the DW3_* seams DesktopWinPhase1Faults. No real user-secret, runner secret,
/// AppHost or ACL outside the root is read or written, and no generated secret ever reaches the output or a tool's argv.
/// </summary>
[Category("Unit")]
public sealed class DesktopWinPhase1SecretsTests
{
    private const string Owner = "operator";
    private const string SeedKeep = "Host=sentinel-keep-7f3a";
    private const string Prefix = "PhoneHomeRunner:Runners:desktop-win:";

    private static readonly string[] KeyNames =
    [
        Prefix + "Enabled", Prefix + "AllowDelegatedTasks", Prefix + "DisplayName", Prefix + "HostWorkspaceRoot", Prefix + "RunnerWorkspace",
        Prefix + "RunnerRepository", Prefix + "MaxCapacity", Prefix + "RawExeAllowList:0", Prefix + "CallbackOrigin", Prefix + "SharedSecret",
        Prefix + "ChildClaudeHome", Prefix + "ChildCodexHome", Prefix + "ChildGrokHome", Prefix + "ClaudeAuthProbeEnabled", Prefix + "CodexAuthProbeEnabled",
    ];

    // ---- refusals before anything ----

    [Test]
    [Arguments("CLAUDECODE")]
    [Arguments("ANTIPHON_ORCHESTRATOR")]
    public void An_agent_shell_is_refused_before_anything(string variable)
    {
        using var c = new Case();
        c.World.Variables[variable] = "1";
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["REFUSED: agent-shell", "ANTIPHON_ORCHESTRATOR or CLAUDECODE is set. This helper is operator-only."]);
        c.World.Tools.Calls.ShouldBeEmpty();
        File.Exists(c.SecretFile).ShouldBeFalse();
        File.ReadAllText(c.UserSecrets).ShouldBe(c.Seed);
    }

    [Test]
    public void Off_windows_both_paths_must_be_given()
    {
        using var c = new Case();
        c.Run(c.Options("--execute") with { UserSecretsPath = "" }).ShouldBe(2);
        c.World.Lines.ShouldBe(["REFUSED: not-windows", "Pass --user-secrets-path and --secret-file. The operator runs this helper on Windows."]);
    }

    [Test]
    public void The_operator_must_own_the_secrets_directory_and_name_a_user()
    {
        using (var c = new Case())
        {
            c.StatOwner = "root";
            c.Run("--execute").ShouldBe(2);
            c.World.Lines[0].ShouldBe("REFUSED: secrets-dir-owner");
            c.World.Lines[1].ShouldStartWith("USERNAME does not match the secrets directory owner.");
            File.Exists(c.SecretFile).ShouldBeFalse();
        }
        using (var c = new Case())
        {
            // The owner check reads the first existing ancestor of a missing secrets directory.
            Directory.Delete(Path.GetDirectoryName(c.SecretFile)!);
            c.Run().ShouldBe(0, c.World.Out);
            c.World.Tools.Calls.First().Arguments.ShouldBe(["-c", "%U", "--", c.RunnerRoot]);
        }
        using (var c = new Case())
        {
            c.World.Variables.Remove("USERNAME");
            c.Run().ShouldBe(2);
            c.World.Lines.ShouldBe(["REFUSED: username-missing", "USERNAME is not set."]);
        }
        using (var c = new Case(windows: true))
        {
            c.Acl.OwnerAnswer = "BUILTIN\\Administrators";
            c.Run().ShouldBe(2);
            c.World.Lines[0].ShouldBe("REFUSED: secrets-dir-owner");
        }
    }

    [Test]
    public void Missing_paths_and_homes_are_refused()
    {
        using (var c = new Case())
        {
            c.Run(c.Options("--execute") with { ProfileHome = "", Home = "" }).ShouldBe(2);
            c.World.Lines.ShouldBe(["REFUSED: profile-home-missing", "Profile home is empty."]);
        }
        using (var c = new Case())
        {
            c.Run(c.Options() with { SecretFile = "phone-home", Phase1ConfigPath = "" }).ShouldBe(2);
            c.World.Lines.ShouldBe(["REFUSED: phase1-path", "Pass --phase1-config-path. The secret path has no runner root."]);
        }
        using (var c = new Case())
        {
            c.Run(c.Options() with { SecretFile = "phone-home", Phase1ConfigPath = c.Phase1 }).ShouldBe(2);
            c.World.Lines.ShouldBe(["REFUSED: secret-path", "The secret file path has no directory."]);
        }
        using (var c = new Case(windows: true))
        {
            c.Run(c.Options() with { UserSecretsPath = "" }).ShouldBe(2);
            c.World.Lines.ShouldBe(["REFUSED: user-secrets-path-missing", "APPDATA is not set. Pass --user-secrets-path."]);
        }
    }

    // ---- the dry run ----

    [Test]
    public void The_dry_run_lists_the_plan_and_writes_nothing()
    {
        using var c = new Case();
        c.Run().ShouldBe(0, c.World.Out);
        c.World.Lines.ShouldBe([
            "mode: dry-run",
            "secret-file: " + c.SecretFile + " absent",
            "user-secrets: " + c.UserSecrets + " present",
            "secrets-dir: " + Path.GetDirectoryName(c.SecretFile) + " present",
            "phase1-config: " + c.Phase1 + " absent",
            "phase1-example: " + c.Example + " present",
            "shared-secret-key: absent",
            "secret-file-matches-map: yes",
            "standing-agent: unset",
            "keys:",
            .. KeyNames.Select(key => "  " + key),
            "changes: none",
        ]);
        File.Exists(c.SecretFile).ShouldBeFalse();
        File.Exists(c.Phase1).ShouldBeFalse();
        File.ReadAllText(c.UserSecrets).ShouldBe(c.Seed);
        c.World.Tools.Calls.ShouldAllBe(call => call.FileName == "stat", "the dry run changes no permission");

        // --what-if wins over --execute; --rotate is reported.
        c.World.ClearOutput();
        c.Run("--execute", "--what-if", "--rotate").ShouldBe(0);
        c.World.Lines[0].ShouldBe("mode: dry-run");
        c.World.Lines[^2].ShouldBe("rotate: requested");
        File.Exists(c.SecretFile).ShouldBeFalse();
    }

    [Test]
    public void The_dry_run_compares_the_secret_file_with_the_map()
    {
        using var c = new Case();
        File.WriteAllText(c.SecretFile, "abc\r\n");
        c.Run().ShouldBe(0);
        c.World.Lines.ShouldContain("shared-secret-key: absent");
        c.World.Lines.ShouldContain("secret-file-matches-map: no", "a file without the map entry");

        c.World.ClearOutput();
        File.WriteAllText(c.UserSecrets, "{\"" + Prefix + "SharedSecret\": \"abc\"}");
        c.Run().ShouldBe(0);
        c.World.Lines.ShouldContain("shared-secret-key: present");
        c.World.Lines.ShouldContain("secret-file-matches-map: yes", "the file's trailing line end is ignored");

        c.World.ClearOutput();
        File.WriteAllText(c.SecretFile, "ABC\n");
        c.Run().ShouldBe(0);
        c.World.Lines.ShouldContain("secret-file-matches-map: no", "the comparison is exact");
    }

    // ---- the apply ----

    [Test]
    public void Execute_writes_both_halves_owner_only_and_never_prints_the_secret()
    {
        using var c = new Case();
        c.Run("--execute").ShouldBe(0, c.World.Out);
        c.World.Lines.ShouldBe([
            "mode: execute",
            "secret-file: wrote",
            "user-secrets: updated",
            "phase1-config: wrote",
            "keys-set: 15",
            "standing-agent: unset",
            "NEXT 1: restart AppHost from C:\\src\\Antiphon",
            "pwsh -NoProfile -File C:\\src\\Antiphon\\scripts\\restart-apphost.ps1",
            "NEXT 2: start the phase 1 runner from the side clone in a normal terminal",
            "pwsh -NoProfile -File C:\\Antiphon\\runner\\app\\scripts\\autostart-session-runner.ps1 -PhoneHomeConfigPath " + c.Phase1,
            "NEXT 3: after the restart, verify",
            "GET http://localhost:17202/api/session-runners/desktop-win/status",
        ]);
        var secretText = File.ReadAllText(c.SecretFile);
        Regex.IsMatch(secretText, "^[0-9a-f]{64}\n$").ShouldBeTrue("64 lowercase hex characters and a line feed");
        var secret = secretText.TrimEnd('\n');
        var map = Dw3Json.Read(File.ReadAllText(c.UserSecrets));
        map.Keys.ShouldBe(["ConnectionStrings:DefaultConnection", .. KeyNames], "existing keys first, then the 15 in order");
        Dw3Json.StringValue(map, "ConnectionStrings:DefaultConnection").ShouldBe(SeedKeep);
        Dw3Json.StringValue(map, Prefix + "SharedSecret").ShouldBe(secret);
        Dw3Json.StringValue(map, Prefix + "MaxCapacity").ShouldBe("1");
        Dw3Json.StringValue(map, Prefix + "ChildClaudeHome").ShouldBe(WindowsPhoneHome.ChildPath(c.ProfileHome, ".claude"));
        File.ReadAllText(c.UserSecrets).ShouldEndWith("}\n");
        File.ReadAllText(c.Phase1).ShouldBe(File.ReadAllText(c.Example), "the example is copied when the config is absent");

        c.Mode(c.SecretFile).ShouldBe("600", "the secret file is owner-only");
        c.Mode(c.UserSecrets).ShouldBe("644", "the user-secrets file keeps its saved mode");
        (c.World.Out + c.World.Error).ShouldNotContain(secret);
        c.World.Tools.Calls.ShouldAllBe(call => call.Arguments.All(argument => !argument.Contains(secret)), "no secret in a tool's argv");
        c.World.Tools.Calls.ShouldAllBe(call => call.FileName == "stat" || call.FileName == "chmod");
        c.Strays().ShouldBeEmpty();

        // A second run refuses without --rotate and changes nothing.
        var before = File.ReadAllBytes(c.UserSecrets);
        c.World.ClearOutput();
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["REFUSED: shared-secret-present", Prefix + "SharedSecret is already set. Re-run with --rotate to replace it. Nothing was changed."]);
        File.ReadAllBytes(c.UserSecrets).ShouldBe(before);

        // --rotate replaces both halves with a new secret; the phase 1 config stays.
        c.World.ClearOutput();
        c.Run("--execute", "--rotate").ShouldBe(0, c.World.Out);
        c.World.Lines.ShouldContain("phase1-config: present");
        var rotated = File.ReadAllText(c.SecretFile).TrimEnd('\n');
        rotated.ShouldNotBe(secret);
        Dw3Json.StringValue(Dw3Json.Read(File.ReadAllText(c.UserSecrets)), Prefix + "SharedSecret").ShouldBe(rotated);
        c.Strays().ShouldBeEmpty();
    }

    [Test]
    public void A_secret_file_without_the_map_entry_is_refused_without_rotate()
    {
        using var c = new Case();
        File.WriteAllText(c.SecretFile, "old\n");
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["REFUSED: secret-file-present", "The secret file already exists. Re-run with --rotate to replace it. Nothing was changed."]);
        File.ReadAllText(c.SecretFile).ShouldBe("old\n");
        File.ReadAllText(c.UserSecrets).ShouldBe(c.Seed);
    }

    [Test]
    [Arguments("{\"phonehomerunner:Runners:desktop-win:SharedSecret\": \"x\"}")]
    [Arguments("{\"PhoneHomeRunner:Runners:desktop-win:enabled\": \"true\"}")]
    [Arguments("{\"PhoneHomeRunner\": {\"Runners\": {}}}")]
    [Arguments("{\"phonehomerunner\": {}}")]
    public void A_case_variant_key_or_a_nested_runner_object_is_refused(string seed)
    {
        using var c = new Case();
        File.WriteAllText(c.UserSecrets, seed);
        c.Run().ShouldBe(0);
        c.World.Lines.ShouldContain("shared-secret-key: ambiguous");
        c.World.ClearOutput();
        c.Run("--execute").ShouldBe(2);
        c.World.Lines[0].ShouldBe("REFUSED: shared-secret-ambiguous");
        File.ReadAllText(c.UserSecrets).ShouldBe(seed);
        File.Exists(c.SecretFile).ShouldBeFalse();
    }

    [Test]
    [Arguments("{\"a\": \"\\x\"}")]
    [Arguments("{\"a\": 1,}")]
    [Arguments("{\"a\": 1, \"a\": 2}")]
    [Arguments("[1, 2]")]
    [Arguments("{\"a\": \"unterminated}")]
    [Arguments("{\"a\": 01}")]
    [Arguments("{\"a\": True}")]
    [Arguments("{\"a\": 1.}")]
    [Arguments("{\"a\": \"tab\tinside\"}")]
    [Arguments("{\"a\": 1} trailing")]
    public void A_malformed_user_secrets_file_is_refused_and_kept(string seed)
    {
        using var c = new Case();
        File.WriteAllText(c.UserSecrets, seed);
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["REFUSED: user-secrets-malformed", "The user-secrets file could not be read as a JSON object."]);
        File.ReadAllText(c.UserSecrets).ShouldBe(seed);
        File.Exists(c.SecretFile).ShouldBeFalse();
    }

    [Test]
    public void Strings_numbers_and_nesting_round_trip_as_written()
    {
        using var c = new Case();
        var seed = "{\"when\": \"2026-11-25T10:00:00Z\", \"n\": -1.50e+3, \"t\": true, \"f\": false, \"z\": null, \"u\": \"caf\\u00e9 \\/ \\\"q\\\"\", "
            + "\"arr\": [1, [], {}], \"obj\": {\"inner\": {}}}";
        File.WriteAllText(c.UserSecrets, seed);
        c.Run("--execute").ShouldBe(0, c.World.Out);
        var text = File.ReadAllText(c.UserSecrets);
        text.ShouldStartWith(string.Join("\n",
            "{",
            "  \"when\": \"2026-11-25T10:00:00Z\",",
            "  \"n\": -1.50e+3,",
            "  \"t\": true,",
            "  \"f\": false,",
            "  \"z\": null,",
            "  \"u\": \"caf\\u00e9 / \\\"q\\\"\",",
            "  \"arr\": [",
            "    1,",
            "    [],",
            "    {",
            "    }",
            "  ],",
            "  \"obj\": {",
            "    \"inner\": {",
            "    }",
            "  },",
            "  \"" + Prefix + "Enabled\": \"true\","));
        text.ShouldNotContain("\r");
        // A blank file is an empty object.
        using var blank = new Case();
        File.WriteAllText(blank.UserSecrets, "  \n");
        blank.Run("--execute").ShouldBe(0);
        Dw3Json.Read(File.ReadAllText(blank.UserSecrets)).Keys.ShouldBe(KeyNames);
    }

    // ---- failures and the restore ----

    [Test]
    public void A_failed_staged_write_changes_no_target()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { FailStep = "user-secrets-write" });
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["step=user-secrets-write", "changed: phase1-config", "REFUSED: failed",
            "The helper stopped before finishing. No secret was written to the console."]);
        File.ReadAllText(c.UserSecrets).ShouldBe(c.Seed);
        File.Exists(c.SecretFile).ShouldBeFalse();
        c.Strays().ShouldBeEmpty();
    }

    [Test]
    public void A_failed_secret_commit_restores_the_published_user_secrets()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { FailStep = "secret-file-commit" });
        File.WriteAllText(c.Phase1, "{}");
        var before = File.ReadAllBytes(c.UserSecrets);
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["step=secret-file-commit", "changed: none", "REFUSED: failed", "The helper stopped before finishing. No secret was written to the console."]);
        File.ReadAllBytes(c.UserSecrets).ShouldBe(before, "the saved bytes are back");
        c.Mode(c.UserSecrets).ShouldBe("644", "with the saved mode");
        File.Exists(c.SecretFile).ShouldBeFalse();
        c.Strays().ShouldBeEmpty();
    }

    [Test]
    public void A_partial_replace_of_the_secret_file_is_restored_from_its_saved_bytes()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { ReplaceFault = "secret-file" });
        File.WriteAllText(c.SecretFile, "oldsecret\n");
        File.WriteAllText(c.UserSecrets, "{\"" + Prefix + "SharedSecret\": \"oldsecret\"}");
        File.WriteAllText(c.Phase1, "{}");
        var json = File.ReadAllBytes(c.UserSecrets);
        c.Run("--execute", "--rotate").ShouldBe(2);
        c.World.Lines.ShouldBe(["step=secret-file-commit", "changed: none", "REFUSED: failed", "The helper stopped before finishing. No secret was written to the console."]);
        File.ReadAllText(c.SecretFile).ShouldBe("oldsecret\n");
        File.ReadAllBytes(c.UserSecrets).ShouldBe(json);
        c.Mode(c.SecretFile).ShouldBe("600");
        c.Strays().ShouldBeEmpty("the backup and the temps are removed once both halves match");
    }

    [Test]
    public void A_restore_that_fails_keeps_and_names_the_residue()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { FailStep = "secret-file-commit", RestoreFault = "user-secrets" });
        c.Run("--execute").ShouldBe(2);
        c.World.Lines.ShouldBe(["step=restore", "differs: user-secrets", "changed: phase1-config", "REFUSED: failed",
            "The helper stopped before finishing. No secret was written to the console."]);
        File.Exists(c.SecretFile).ShouldBeFalse("the secret half matched its saved state (absent)");
        c.Strays().ShouldBeEmpty("the secret half's staged temp is removed");
    }

    [Test]
    public void A_permission_that_does_not_apply_stops_before_the_write()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { OwnerFail = "temp" });
        c.Run("--execute").ShouldBe(2);
        c.World.Lines[0].ShouldStartWith("owner-only-failed: " + Path.GetDirectoryName(c.SecretFile));
        c.World.Lines[0].ShouldEndWith(".dw3part");
        c.World.Lines.Skip(1).ShouldBe(["step=secret-acl", "changed: phase1-config", "REFUSED: failed",
            "The helper stopped before finishing. No secret was written to the console."]);
        File.Exists(c.SecretFile).ShouldBeFalse();
        File.ReadAllText(c.UserSecrets).ShouldBe(c.Seed);
        c.Strays().ShouldBeEmpty();

        using var chmod = new Case();
        chmod.ChmodExit = 1;
        chmod.Run("--execute").ShouldBe(2);
        chmod.World.Lines[0].ShouldBe("step=secret-acl");
        chmod.Strays().ShouldBeEmpty();
    }

    [Test]
    public void A_delete_that_leaves_a_file_prints_kept()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { FailStep = "secret-file-commit", DeleteFault = "1" });
        c.Run("--execute").ShouldBe(2);
        var staged = c.Strays().ShouldHaveSingleItem("the secret half's staged temp stays");
        c.World.Lines.Count(line => line == "kept: " + staged).ShouldBe(2, "named as the staged temp and as a temp of its directory");
        c.World.Lines[^2].ShouldBe("REFUSED: failed");
    }

    [Test]
    public void The_probes_see_only_modes_and_counts()
    {
        using var world = new OperatorToolWorld("dw3-probe");
        var modeProbe = world.PathOf("mode.txt");
        var clearProbe = world.PathOf("clear.txt");
        using var c = new Case(faults: new DesktopWinPhase1Faults { ModeAtWrite = modeProbe, ClearProbe = clearProbe });
        c.Run("--execute").ShouldBe(0, c.World.Out);
        File.ReadAllText(modeProbe).ShouldBe("mode=600\nmode=600\n", "both staged halves are owner-only before a byte is written");
        Regex.IsMatch(File.ReadAllText(clearProbe), "^held=[1-9][0-9]* cleared=yes$").ShouldBeTrue(File.ReadAllText(clearProbe));
        var secret = File.ReadAllText(c.SecretFile).TrimEnd('\n');
        (File.ReadAllText(modeProbe) + File.ReadAllText(clearProbe)).ShouldNotContain(secret);
    }

    // ---- stranded files ----

    [Test]
    public void Stranded_files_are_listed_and_swept_only_when_asked()
    {
        using var c = new Case();
        var secretsDir = Path.GetDirectoryName(c.SecretFile)!;
        var stalePart = c.Stranded(secretsDir, "dw3part", minutes: 20);
        var staleBak = c.Stranded(Path.GetDirectoryName(c.UserSecrets)!, "dw3bak", minutes: 20);
        var freshPart = c.Stranded(secretsDir, "dw3part", minutes: 1);
        var decoys = new[] { Path.Combine(secretsDir, new string('A', 32) + ".dw3part"), Path.Combine(secretsDir, new string('a', 31) + ".dw3part"),
            Path.Combine(secretsDir, new string('a', 32) + ".dw3partx") };
        foreach (var decoy in decoys) File.WriteAllText(decoy, "decoy");

        // Listed, never read or deleted without --sweep-stranded.
        c.Run().ShouldBe(0);
        c.World.Lines.Where(line => line.StartsWith("stranded: ", StringComparison.Ordinal)).Select(line => line["stranded: ".Length..])
            .ShouldBe([staleBak, stalePart, freshPart], ignoreOrder: true);
        c.World.Lines.ShouldContain("recovery: rename the .dw3bak back over a missing target, or re-run --execute --rotate");
        c.World.Lines.ShouldContain("sweep: refused");

        // A dry run, or --what-if, would sweep.
        foreach (var flags in new string[][] { ["--sweep-stranded"], ["--execute", "--what-if", "--sweep-stranded"] })
        {
            c.World.ClearOutput();
            c.Run(flags).ShouldBe(0);
            c.World.Lines.ShouldContain("would-sweep: " + stalePart);
            c.World.Lines.ShouldContain("kept: " + freshPart + " fresh");
            c.World.Lines.ShouldContain("kept: " + staleBak + " backup-of-secrets-keep-until-confirmed");
            File.Exists(stalePart).ShouldBeTrue();
        }

        // --execute --sweep-stranded removes the stale part; the backup needs --sweep-backups; then the apply runs.
        c.World.ClearOutput();
        c.Run("--execute", "--sweep-stranded").ShouldBe(0, c.World.Out);
        c.World.Lines.ShouldContain("swept: " + stalePart);
        c.World.Lines.ShouldContain("kept: " + freshPart + " fresh");
        File.Exists(stalePart).ShouldBeFalse();
        File.Exists(staleBak).ShouldBeTrue();
        File.Exists(freshPart).ShouldBeTrue();
        foreach (var decoy in decoys) File.Exists(decoy).ShouldBeTrue(decoy);
        c.World.Lines.ShouldContain("secret-file: wrote");
    }

    [Test]
    public void Backups_are_swept_with_sweep_backups_and_the_age_is_a_seam()
    {
        using var c = new Case(faults: new DesktopWinPhase1Faults { StrandedMaxAgeMinutes = "0" });
        var secretsDir = Path.GetDirectoryName(c.SecretFile)!;
        var bak = c.Stranded(secretsDir, "dw3bak", minutes: 1);
        var part = c.Stranded(secretsDir, "dw3part", minutes: 0);
        c.Run("--execute", "--sweep-stranded", "--sweep-backups").ShouldBe(0, c.World.Out);
        c.World.Lines.ShouldContain("swept: " + bak);
        c.World.Lines.ShouldContain("swept: " + part, "an age of 0 minutes makes every file stale");
        File.Exists(bak).ShouldBeFalse();

        using var negative = new Case(faults: new DesktopWinPhase1Faults { StrandedMaxAgeMinutes = "-5" });
        var fresh = negative.Stranded(Path.GetDirectoryName(negative.SecretFile)!, "dw3part", minutes: 5);
        negative.Run("--sweep-stranded").ShouldBe(0);
        negative.World.Lines.ShouldContain("kept: " + fresh + " fresh", "a negative override keeps the 15-minute default");
    }

    // ---- the Windows branch ----

    [Test]
    public void On_windows_the_halves_swap_through_owner_only_backups_and_icacls_never_sees_the_secret()
    {
        using var c = new Case(windows: true);
        c.Run("--execute").ShouldBe(0, c.World.Out);
        var secret = File.ReadAllText(c.SecretFile).TrimEnd('\n');
        c.World.Tools.Calls.ShouldAllBe(call => call.FileName == "icacls");
        c.World.Tools.Calls.ShouldAllBe(call => call.Arguments.All(argument => !argument.Contains(secret)));
        var grants = c.World.Tools.Calls.Where(call => call.Arguments.Contains("/grant:r")).ToList();
        grants.ShouldNotBeEmpty();
        grants.ShouldAllBe(call => call.Arguments[2] == "FIXTURE\\operator:(F)");
        c.World.Tools.Calls.Where(call => call.Arguments.Contains("/inheritance:r")).Count().ShouldBe(grants.Count);
        c.Acl.Protected.ShouldContain(c.SecretFile);
        c.Acl.Restored.ShouldBe([c.UserSecrets], "the user-secrets file gets its saved ACL back");
        c.Acl.Protected.ShouldContain(path => path.EndsWith(".dw3bak", StringComparison.Ordinal), "the replaced user-secrets passed through an owner-only backup");
        c.Strays().ShouldBeEmpty("the backup is deleted after the swap");
        Dw3Json.StringValue(Dw3Json.Read(File.ReadAllText(c.UserSecrets)), Prefix + "SharedSecret").ShouldBe(secret);
        (c.World.Out + c.World.Error).ShouldNotContain(secret);
    }

    [Test]
    public void On_windows_the_user_secrets_default_to_appdata()
    {
        using var c = new Case(windows: true);
        var appData = c.World.PathOf("appdata");
        c.World.Variables["APPDATA"] = appData;
        c.Run(c.Options() with { UserSecretsPath = "" }).ShouldBe(0);
        c.World.Lines.ShouldContain("user-secrets: " + PsPath.Join(appData, "Microsoft\\UserSecrets\\antiphon-server\\secrets.json", OperatingSystem.IsWindows()) + " absent");

        c.World.ClearOutput();
        c.World.Variables.Remove("APPDATA");
        c.World.Variables["USERPROFILE"] = c.World.PathOf("profile");
        c.Run(c.Options() with { UserSecretsPath = "" }).ShouldBe(0);
        c.World.Lines.ShouldContain(line => line.StartsWith("user-secrets: ", StringComparison.Ordinal) && line.Contains("Roaming") && line.EndsWith("secrets.json absent", StringComparison.Ordinal));
    }

    // ---- the reader, the writer and the flags ----

    [Test]
    public void The_json_writer_and_reader_keep_the_scripts_format()
    {
        Dw3Json.Quote("a\"b\\c\nd\re\tf\u0001g\u00e9h\u2028").ShouldBe("\"a\\\"b\\\\c\\nd\\re\\tf\\u0001g\\u00e9h\\u2028\"");
        Dw3Json.Write(Dw3Node.EmptyObject(), 0).ShouldBe("{\n}");
        Dw3Json.Write(Dw3Json.Read("{\"a\":[]}"), 0).ShouldBe("{\n  \"a\": []\n}");
        Dw3Json.Read(" { \"k\" : \"\\u 041\" } ").Members["k"].Text.ShouldBe("A", "HexNumber allows white space, as the script's TryParse did");
        Dw3Json.Read("{\"a\":1,\"A\":2}").Keys.ShouldBe(["a", "A"], "a case-variant key is kept for the ambiguity check");
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 32)) + "{}" + new string('}', 32);
        Dw3Json.Write(Dw3Json.Read(deep), 0).ShouldNotBeEmpty();
        var deeper = string.Concat(Enumerable.Repeat("{\"a\":", 33)) + "1" + new string('}', 33);
        Should.Throw<FormatException>(() => Dw3Json.Read(deeper)).Message.ShouldBe(Dw3Json.Malformed);
    }

    [Test]
    public void The_flags_are_the_scripts_parameters_in_kebab_case()
    {
        var options = DesktopWinPhase1Options.Parse(["--execute", "--rotate", "--what-if", "--sweep-stranded", "--sweep-backups", "--user-secrets-path", "u",
            "--secret-file", "s", "--profile-home", "p", "--phase1-config-path", "c"], "home", "scripts");
        (options.Execute, options.Rotate, options.WhatIf, options.SweepStranded, options.SweepBackups).ShouldBe((true, true, true, true, true));
        (options.UserSecretsPath, options.SecretFile, options.ProfileHome, options.Phase1ConfigPath, options.Home, options.ScriptsDirectory)
            .ShouldBe(("u", "s", "p", "c", "home", "scripts"));
        var defaults = DesktopWinPhase1Options.Parse([], "home", "scripts");
        (defaults.Execute, defaults.UserSecretsPath, defaults.SecretFile).ShouldBe((false, "", ""));
        Should.Throw<NightlyUsageException>(() => DesktopWinPhase1Options.Parse(["-Execute"], "home", "scripts"));
        Should.Throw<NightlyUsageException>(() => DesktopWinPhase1Options.Parse(["--secret-file"], "home", "scripts"));
    }

    /// <summary>The Windows ACLs as a test sees them: the owner, the owner-only files and the restores.</summary>
    internal sealed class FakeAcl : IDesktopWinAcl
    {
        public string OwnerAnswer { get; set; } = "FIXTURE\\" + DesktopWinPhase1SecretsTests.Owner;

        public HashSet<string> Protected { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Restored { get; } = [];

        public string Owner(string path) => OwnerAnswer;

        public void ProtectOwnerOnly(string path, string identity) => Protected.Add(path);

        public bool IsOwnerOnly(string path, string identity) => Protected.Contains(path) && identity == "FIXTURE\\" + DesktopWinPhase1SecretsTests.Owner;

        public object Save(string path) => "saved " + path;

        public void Restore(string path, object saved) => Restored.Add(path);
    }

    /// <summary>
    /// One case: a runner root with its secrets directory, a user-secrets directory holding a seed map, a profile home and a
    /// scripts directory holding the phase 1 example; the operator's USERNAME; chmod, stat and icacls fakes over a mode table
    /// (644 unless chmod set it).
    /// </summary>
    internal sealed class Case : IDisposable
    {
        private readonly Dictionary<string, string> _modes = new(StringComparer.Ordinal);
        private readonly bool _windows;
        private readonly DesktopWinPhase1Faults _faults;

        public Case(bool windows = false, DesktopWinPhase1Faults? faults = null)
        {
            _windows = windows;
            _faults = faults ?? new DesktopWinPhase1Faults();
            RunnerRoot = Directory.CreateDirectory(World.PathOf("runner")).FullName;
            Directory.CreateDirectory(Path.Combine(RunnerRoot, "secrets"));
            Directory.CreateDirectory(World.PathOf("user-secrets"));
            ProfileHome = Directory.CreateDirectory(World.PathOf("home")).FullName;
            var scripts = Directory.CreateDirectory(World.PathOf("scripts")).FullName;
            File.WriteAllText(Path.Combine(scripts, "phone-home.phase1.example.json"), "{ \"runnerId\": \"desktop-win\", \"port\": 17214 }\n");
            File.WriteAllText(UserSecrets, Seed);
            World.Variables["USERNAME"] = Owner;
            World.Variables["USERDOMAIN"] = "FIXTURE";
            World.Tools.Behaviour = Tool;
        }

        public OperatorToolWorld World { get; } = new("dw3");

        public FakeAcl Acl { get; } = new();

        public string StatOwner { get; set; } = Owner;

        public int ChmodExit { get; set; }

        public string Seed => "{\n  \"ConnectionStrings:DefaultConnection\": \"" + SeedKeep + "\"\n}\n";

        public string RunnerRoot { get; }

        public string ProfileHome { get; }

        public string SecretFile => Path.Combine(RunnerRoot, "secrets", "phone-home");

        public string UserSecrets => World.PathOf("user-secrets", "secrets.json");

        public string Phase1 => Path.Combine(RunnerRoot, "phone-home.phase1.json");

        public string Example => World.PathOf("scripts", "phone-home.phase1.example.json");

        public DesktopWinPhase1Options Options(params string[] flags)
        {
            var parsed = DesktopWinPhase1Options.Parse(flags, ProfileHome, World.PathOf("scripts"));
            return parsed with { UserSecretsPath = UserSecrets, SecretFile = SecretFile, ProfileHome = ProfileHome };
        }

        public int Run(params string[] flags) => Run(Options(flags));

        public int Run(DesktopWinPhase1Options options) =>
            DesktopWinPhase1Secrets.Run(options, World.Host(), new FileSystem(), new DesktopWinSecretSeams
            {
                Windows = _windows,
                Tools = World.Tools,
                Acl = Acl,
                Faults = _faults,
            });

        /// <summary>The mode chmod last gave the path (644 when none).</summary>
        public string Mode(string path) => _modes.TryGetValue(path, out var mode) ? mode : "644";

        /// <summary>The .dw3part and .dw3bak files left under the case's directories.</summary>
        public List<string> Strays() =>
            Directory.EnumerateFiles(World.Root, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".dw3part", StringComparison.Ordinal) || path.EndsWith(".dw3bak", StringComparison.Ordinal)).ToList();

        /// <summary>A stranded file named like the helper's, written the given minutes before the clock.</summary>
        public string Stranded(string directory, string extension, int minutes)
        {
            var path = Path.Combine(directory, Guid.NewGuid().ToString("n") + "." + extension);
            File.WriteAllText(path, "stranded");
            File.SetLastWriteTimeUtc(path, World.Clock.GetUtcNow().UtcDateTime.AddMinutes(-minutes));
            return path;
        }

        private OperatorToolResult Tool(OperatorToolCall call)
        {
            var args = call.Arguments;
            switch (call.FileName)
            {
                case "chmod":
                    if (ChmodExit != 0) return OperatorToolResult.Of(ChmodExit, error: ["chmod: fixture failure"]);
                    _modes[args[1]] = args[0];
                    return OperatorToolResult.Of(0);
                case "stat":
                {
                    var path = args[3];
                    if (!File.Exists(path) && !Directory.Exists(path)) return OperatorToolResult.Of(1, error: ["stat: cannot statx '" + path + "'"]);
                    return OperatorToolResult.Of(0, [args[1] == "%U" ? StatOwner : Mode(path)]);
                }
                case "icacls":
                    return OperatorToolResult.Of(0, ["processed file: " + args[0], "Successfully processed 1 files; Failed processing 0 files"]);
                default:
                    throw new InvalidOperationException("unexpected tool " + call.FileName);
            }
        }

        public void Dispose() => World.Dispose();
    }
}
