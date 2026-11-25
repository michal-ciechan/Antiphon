using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Runtime.InteropServices;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1776, unit 52 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the repository mirrors
/// (Antiphon.Nightly.ServerRepositoryMirrors, the server2-repository-mirrors command that replaced
/// scripts/server2-repository-mirrors.ps1) in process. These are the cases of the deleted ServerRepositoryMirrorScriptTests, which
/// ran the script in pwsh, plus the execute pass: git is a recording launcher, the work volume and the mounts a mock file system
/// (the real disk for the plan of a temp table and for the link tests), so nothing is cloned or fetched and no real token is read.
/// The lines and exits are the ones the script printed (captured from it before its deletion). It plans and refuses without
/// cloning, it does not start docker, and it does not print a token.
/// </summary>
[Category("Unit")]
public sealed class ServerRepositoryMirrorsTests
{
    private const string Canary = "CANARY-TOKEN-VALUE-1776";
    private const string Antiphon = "antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git\n";
    private const string Three = "antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git\n"
        + "gym-stat|C:/src/gym-stat|https://github.com/example/gym-stat.git\n"
        + "markdown-package|C:\\src\\markdown-package|https://github.com/example/markdown-package.git\n";
    private const string Planned = "SERVER2-MIRRORS plan exit=0 reason=planned";
    private const string Rollback = "rollback restores the before column";

    // ---- the options ----

    [Test]
    public void Options_take_the_scripts_parameters_in_kebab_case()
    {
        var environment = new Dictionary<string, string> { ["PATH"] = "p" };
        var options = ServerRepositoryMirrorsOptions.Parse([], environment);
        (options.WhatIf, options.Execute, options.TableFile, options.TokenFile, options.PolicyFile, options.ReposRoot)
            .ShouldBe((false, false, "", "/run/antiphon/github-token/token", "/run/antiphon/push-allow-list", "/work/repos"));
        options.Environment.ShouldBeSameAs(environment);
        options = ServerRepositoryMirrorsOptions.Parse(["--what-if", "--execute", "--table-file", "t", "--token-file", "k", "--policy-file", "p", "--repos-root", ""], null);
        (options.WhatIf, options.Execute, options.TableFile, options.TokenFile, options.PolicyFile, options.ReposRoot).ShouldBe((true, true, "t", "k", "p", ""));

        Should.Throw<NightlyUsageException>(() => ServerRepositoryMirrorsOptions.Parse(["--bogus"], null)).Message.ShouldBe("unknown flag --bogus");
        Should.Throw<NightlyUsageException>(() => ServerRepositoryMirrorsOptions.Parse(["-TableFile", "t"], null)).Message.ShouldBe("unexpected argument '-TableFile'");
        Should.Throw<NightlyUsageException>(() => ServerRepositoryMirrorsOptions.Parse(["--table-file"], null)).Message.ShouldBe("missing value for --table-file");
        ServerRepositoryMirrorsOptions.Usage.ShouldBe("server2-repository-mirrors --table-file <file> [--what-if | --execute]"
            + " [--token-file /run/antiphon/github-token/token] [--policy-file /run/antiphon/push-allow-list] [--repos-root /work/repos]");
        File.Exists(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "server2-repository-mirrors.ps1")).ShouldBeFalse();
    }

    // ---- the plan (the default) ----

    [Test]
    public async Task WhatIf_maps_desktop_paths_and_does_not_create_them()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            var repos = Path.Combine(root, "repos");
            await File.WriteAllTextAsync(table, """
                antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git
                gym-stat|C:/src/gym-stat|https://github.com/example/gym-stat.git
                markdown-package|C:\src\markdown-package|https://github.com/example/markdown-package.git
                """);
            var scriptRepos = ScriptRoot(repos, OperatingSystem.IsWindows());
            string[] expected =
            [
                Planned,
                "mirror name=antiphon before=C:/src/Antiphon after=" + scriptRepos + "/antiphon remote=https://github.com/michal-ciechan/Antiphon.git",
                "mirror name=gym-stat before=C:/src/gym-stat after=" + scriptRepos + "/gym-stat remote=https://github.com/example/gym-stat.git",
                "mirror name=markdown-package before=C:\\src\\markdown-package after=" + scriptRepos + "/markdown-package remote=https://github.com/example/markdown-package.git",
                Rollback,
            ];
            foreach (var mode in new[] { "--what-if", null })
            {
                var launcher = new RecordingLauncher();
                string[] arguments = mode == null ? ["--table-file", table, "--repos-root", scriptRepos] : [mode!, "--table-file", table, "--repos-root", scriptRepos];
                var run = Invoke(new FileSystem(), null, launcher, null, arguments);
                run.Exit.ShouldBe(0, run.Text);
                run.Lines.ShouldBe(expected, mode ?? "default");
                run.Error.ShouldBeEmpty();
                launcher.Launches.ShouldBeEmpty("the plan starts no git");
            }
            Directory.Exists(repos).ShouldBeFalse();
            Directory.Exists(Path.Combine(repos, "antiphon")).ShouldBeFalse();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void Plan_rows_keep_the_scripts_trimming_defaults_and_case_rules()
    {
        // The default root, comments, blank lines, padding and the four-column form.
        var world = new World();
        world.Table("# operator table\n\n  antiphon | C:/src/Antiphon/ | /work/repos/antiphon | https://github.com/michal-ciechan/Antiphon  \n");
        var run = world.Run("--table-file", World.TablePath);
        run.Exit.ShouldBe(0, run.Text);
        run.Lines.ShouldBe([Planned, "mirror name=antiphon before=C:/src/Antiphon/ after=/work/repos/antiphon remote=https://github.com/michal-ciechan/Antiphon", Rollback]);

        // PowerShell compared the name and the after column with -ne and the remote with -match: case-insensitive, kept as written.
        world.Table("Antiphon|C:/src/Antiphon|/WORK/REPOS/ANTIPHON|HTTPS://GITHUB.COM/Michal-Ciechan/Antiphon\n");
        run = world.Run("--table-file", World.TablePath);
        run.Exit.ShouldBe(0, run.Text);
        run.Lines.ShouldBe([Planned, "mirror name=Antiphon before=C:/src/Antiphon after=/WORK/REPOS/ANTIPHON remote=HTTPS://GITHUB.COM/Michal-Ciechan/Antiphon", Rollback]);

        // Trailing slashes leave the root.
        world.Table(Antiphon);
        run = world.Run("--table-file", World.TablePath, "--repos-root", "/work/repos//");
        run.Lines.ShouldBe([Planned, "mirror name=antiphon before=C:/src/Antiphon after=/work/repos/antiphon remote=https://github.com/michal-ciechan/Antiphon.git", Rollback]);
        world.Launcher.Launches.ShouldBeEmpty();
    }

    [Test]
    public async Task Windows_temp_root_is_passed_drive_relative_and_planned()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            await File.WriteAllTextAsync(table, Antiphon);
            var repos = ScriptRoot(@"C:\Users\runner\AppData\Local\Temp\card-1776-mirrors-0\repos", windows: true);
            var run = Invoke(new FileSystem(), null, new RecordingLauncher(), null, ["--table-file", table, "--repos-root", repos]);
            run.Exit.ShouldBe(0, run.Text);
            run.Text.ShouldContain("after=" + repos + "/antiphon");
            repos.ShouldBe("/Users/runner/AppData/Local/Temp/card-1776-mirrors-0/repos");
        }
        finally { DeleteRoot(root); }
    }

    // ---- the refusals before any git ----

    [Test]
    public void Empty_antiphon_remote_is_unrecorded()
    {
        var world = new World();
        world.Table("antiphon|C:/src/Antiphon|\n");
        var run = world.Run("--table-file", World.TablePath);
        run.Exit.ShouldBe(2, run.Text);
        run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=remote-unrecorded"]);
    }

    [Test]
    public void Both_switches_are_ambiguous_and_secrets_and_volumes_are_refused()
    {
        var world = new World();
        world.Table(Antiphon);
        var both = world.Run("--what-if", "--execute", "--table-file", World.TablePath);
        both.Exit.ShouldBe(3, both.Text);
        both.Text.ShouldContain("reason=ambiguous-mode");

        world.Table("antiphon|C:/src/antiphon_pgdata|https://github.com/michal-ciechan/Antiphon.git\n");
        var volume = world.Run("--table-file", World.TablePath);
        volume.Exit.ShouldBe(2, volume.Text);
        volume.Text.ShouldContain("reason=volume-forbidden");

        var secret = "ghp_" + new string('a', 20);
        world.Table("antiphon|C:/src/Antiphon|https://user:" + secret + "@github.com/michal-ciechan/Antiphon.git\n");
        var leaked = world.Run("--table-file", World.TablePath);
        leaked.Exit.ShouldBe(2, leaked.Text);
        leaked.Text.ShouldContain("reason=secret-in-table");
        leaked.Text.ShouldNotContain(secret);

        world.Table("antiphon|C:/src/../Antiphon|https://github.com/michal-ciechan/Antiphon.git\n");
        var escape = world.Run("--table-file", World.TablePath);
        escape.Exit.ShouldBe(2, escape.Text);
        escape.Text.ShouldContain("reason=path-forbidden");
        world.Launcher.Launches.ShouldBeEmpty();
    }

    // Each row: the table (null: no table file), the arguments after --table-file, the exit and the reason the script printed.
    private static readonly (string Case, string? Table, string[] Arguments, int Exit, string Reason)[] Refusals =
    [
        ("ambiguous", Antiphon, ["--what-if", "--execute"], 3, "ambiguous-mode"),
        ("table-absent", null, [], 2, "table-missing"),
        ("table-comments-only", "# nothing\n\n", [], 2, "table-missing"),
        ("root-relative", Antiphon, ["--repos-root", "work/repos"], 2, "path-forbidden"),
        ("root-empty", Antiphon, ["--repos-root", "/"], 2, "path-forbidden"),
        ("root-unset", Antiphon, ["--repos-root", ""], 2, "path-forbidden"),
        ("remote-unrecorded", "antiphon|C:/src/Antiphon|\n", [], 2, "remote-unrecorded"),
        ("volume", "antiphon|C:/src/antiphon_pgdata|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "volume-forbidden"),
        ("dind", "antiphon|C:/src/DIND-DATA/antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "volume-forbidden"),
        ("volume-and-escape", "antiphon|C:/src/../windmill|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "volume-forbidden"),
        ("secret", "antiphon|C:/src/Antiphon|https://user:ghp_aaaaaaaaaaaaaaaaaaaa@github.com/michal-ciechan/Antiphon.git\n", [], 2, "secret-in-table"),
        ("secret-prefix", "antiphon|C:/src/GHS_x/antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "secret-in-table"),
        ("url-credential", "antiphon|C:/src/Antiphon|https://user:pass@example.invalid/x\n", [], 2, "secret-in-table"),
        ("escape", "antiphon|C:/src/../Antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("escape-backslash", "antiphon|C:\\src\\..\\Antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("two-columns", "antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("five-columns", "antiphon|C:/src/Antiphon|/work/repos/antiphon|x|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("name-mismatch", "other|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("after-mismatch", "antiphon|C:/src/Antiphon|/elsewhere/antiphon|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("slug-bad", "-x|C:/src/-x|https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("before-empty", "|  |https://github.com/michal-ciechan/Antiphon.git\n", [], 2, "path-forbidden"),
        ("remote-ssh", "antiphon|C:/src/Antiphon|git@github.com:michal-ciechan/Antiphon.git\n", [], 2, "remote-not-https"),
        ("remote-other-host", "antiphon|C:/src/Antiphon|https://gitlab.com/michal-ciechan/Antiphon.git\n", [], 2, "remote-not-https"),
        ("remote-deep", "antiphon|C:/src/Antiphon|https://github.com/michal-ciechan/Antiphon/tree.git\n", [], 2, "remote-not-https"),
        ("second-row", Antiphon + "gym-stat|C:/src/gym-stat|\n", [], 2, "remote-unrecorded"),
    ];

    [Test]
    public void Refusals_print_the_scripts_line_and_start_no_git()
    {
        foreach (var row in Refusals)
        {
            foreach (var execute in new[] { false, true })
            {
                var world = new World();
                if (row.Table != null) world.Table(row.Table);
                string[] arguments = [.. execute && !row.Arguments.Contains("--execute") ? new[] { "--execute" } : Array.Empty<string>(), "--table-file", World.TablePath, .. row.Arguments];
                var run = world.Run(arguments);
                run.Exit.ShouldBe(row.Exit, row.Case + " " + run.Text);
                run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=" + row.Exit + " reason=" + row.Reason], row.Case);
                run.Error.ShouldBeEmpty(row.Case);
                world.Launcher.Launches.ShouldBeEmpty(row.Case);
                world.Files.File.Exists(World.Policy).ShouldBeFalse(row.Case);
            }
        }

        var unset = new World();
        unset.Table(Antiphon);
        unset.Run().Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=table-missing"]);
        unset.Run("--table-file", " ").Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=table-missing"]);
        unset.Run("--table-file", "/srv").Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=table-missing"], "a directory is not the table");
        // The mode is refused before the table is looked for.
        unset.Run("--what-if", "--execute").Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=3 reason=ambiguous-mode"]);
    }

    // ---- the credential and the allow-list before any git ----

    [Test]
    public void Execute_without_a_token_refuses_before_clone()
    {
        foreach (var (label, prepare) in new (string, Action<World>)[]
        {
            ("missing", world => world.Files.File.Delete(World.Token)),
            ("empty", world => world.Files.File.WriteAllText(World.Token, "")),
            ("directory", world => { world.Files.File.Delete(World.Token); world.Files.Directory.CreateDirectory(World.Token); }),
            ("link", world => world.Links.Add(World.Token)),
        })
        {
            var world = new World();
            world.Table(Antiphon);
            prepare(world);
            var run = world.Run("--execute", "--table-file", World.TablePath);
            run.Exit.ShouldBe(2, label + " " + run.Text);
            run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=credential-absent"], label);
            world.Launcher.Launches.ShouldBeEmpty(label);
            world.Files.Directory.Exists("/work/repos/antiphon").ShouldBeFalse(label);
            world.Files.File.Exists(World.Policy).ShouldBeFalse(label);
        }

        var relative = new World();
        relative.Table(Antiphon);
        relative.Files.AddFile("relative-token", new MockFileData(Canary));
        relative.Run("--execute", "--table-file", World.TablePath, "--token-file", "relative-token").Lines
            .ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=credential-absent"]);
        relative.Run("--execute", "--table-file", World.TablePath, "--token-file", " ").Lines
            .ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=credential-absent"]);
        relative.Launcher.Launches.ShouldBeEmpty();
    }

    [Test]
    public void Present_token_still_refuses_before_git_when_the_mirror_parent_is_missing()
    {
        var world = new World();
        world.Table(Antiphon);
        world.Files.Directory.Delete("/work/repos");
        var run = world.Run("--execute", "--table-file", World.TablePath);
        run.Exit.ShouldBe(2, run.Text);
        run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=mirror-parent-missing"]);
        run.Text.ShouldNotContain(Canary);
        world.Files.File.Exists(World.Policy).ShouldBeFalse();
        world.Launcher.Launches.ShouldBeEmpty();
    }

    [Test]
    public void The_allow_list_path_must_be_rooted_and_its_parent_present()
    {
        foreach (var (policy, reason) in new[] { ("push-allow-list", "credential-absent"), (" ", "credential-absent"), ("/run/absent/push-allow-list", "policy-unwritable") })
        {
            var world = new World();
            world.Table(Antiphon);
            var run = world.Run("--execute", "--table-file", World.TablePath, "--policy-file", policy);
            run.Exit.ShouldBe(2, policy + " " + run.Text);
            run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=" + reason], policy);
            world.Launcher.Launches.ShouldBeEmpty(policy);
        }

        // Every row's parent is checked before the allow-list's.
        var both = new World();
        both.Table(Antiphon);
        both.Files.Directory.Delete("/work/repos");
        both.Run("--execute", "--table-file", World.TablePath, "--policy-file", "/run/absent/push-allow-list").Lines
            .ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=mirror-parent-missing"]);
    }

    // ---- the execute pass ----

    [Test]
    public void Execute_writes_the_allow_list_then_fetches_or_clones_each_row_in_order()
    {
        var world = new World();
        world.Table(Three);
        world.Files.Directory.CreateDirectory("/work/repos/antiphon/.git");
        world.Files.AddFile("/work/repos/markdown-package/.git", new MockFileData("gitdir: /elsewhere\n"));
        world.Files.AddFile(World.Policy, new MockFileData("https://github.com/stale/stale.git\n"));
        var policyAtFirstGit = (string?)null;
        world.OnLaunch = _ => policyAtFirstGit ??= world.Files.File.ReadAllText(World.Policy);

        var run = world.Run("--execute", "--table-file", World.TablePath);
        run.Exit.ShouldBe(0, run.Text);
        run.Lines.ShouldBe(["SERVER2-MIRRORS ok exit=0 reason=mirrored", Rollback]);
        run.Error.ShouldBeEmpty();

        // The allow-list: each remote's identity, lower case with .git, one per line, written before the first git.
        const string allowList = "https://github.com/michal-ciechan/antiphon.git\nhttps://github.com/example/gym-stat.git\nhttps://github.com/example/markdown-package.git\n";
        policyAtFirstGit.ShouldBe(allowList);
        world.Files.File.ReadAllText(World.Policy).ShouldBe(allowList);
        world.Files.Directory.GetFiles("/run/antiphon").Select(file => Path.GetFileName(file)).ShouldBe(["push-allow-list"], ignoreOrder: true, "no temporary allow-list is left");

        // A .git directory or file is a mirror to fetch; no .git is a clone.
        var launches = world.Launcher.Launches;
        launches.Select(launch => (launch.FileName, string.Join(" ", launch.Arguments))).ShouldBe([
            ("/usr/bin/git", "-C /work/repos/antiphon fetch --prune origin"),
            ("/usr/bin/git", "clone -- https://github.com/example/gym-stat.git /work/repos/gym-stat"),
            ("/usr/bin/git", "-C /work/repos/markdown-package fetch --prune origin"),
        ]);
        foreach (var launch in launches)
        {
            // On this console, waited for, in the caller's directory, with GIT_TERMINAL_PROMPT=0 over the caller's environment.
            (launch.Custody, launch.CaptureOutput, launch.WorkingDirectory, launch.TimeoutMilliseconds, launch.LogPath).ShouldBe((NightlyCustody.Watched, false, "", 0, (string?)null));
            launch.Environment.ShouldNotBeNull();
            launch.Environment!["GIT_TERMINAL_PROMPT"].ShouldBe("0");
            launch.Environment["PATH"].ShouldBe("/usr/local/bin:/usr/bin");
            launch.Environment["HOME"].ShouldBe("/home/app");
            launch.Environment.Count.ShouldBe(3);
            // The token is never in argv or the environment.
            launch.Arguments.ShouldAllBe(argument => !argument.Contains(Canary) && !argument.Contains("github-token"));
            launch.Environment.Values.ShouldAllBe(value => !value.Contains(Canary));
        }
        run.Text.ShouldNotContain(Canary);
    }

    [Test]
    public void A_failed_fetch_or_clone_refuses_and_starts_no_later_git()
    {
        var fetch = new World { Exit = _ => 128 };
        fetch.Table(Three);
        fetch.Files.Directory.CreateDirectory("/work/repos/antiphon/.git");
        var run = fetch.Run("--execute", "--table-file", World.TablePath);
        run.Exit.ShouldBe(2, run.Text);
        run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=fetch-failed"]);
        fetch.Launcher.Launches.Count.ShouldBe(1);
        fetch.Files.File.Exists(World.Policy).ShouldBeTrue("the script wrote the allow-list before any git");

        var clone = new World { Exit = launch => launch.Arguments[0] == "clone" && launch.Arguments[2].Contains("gym-stat") ? 1 : 0 };
        clone.Table(Three);
        run = clone.Run("--execute", "--table-file", World.TablePath);
        run.Exit.ShouldBe(2, run.Text);
        run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=clone-failed"]);
        clone.Launcher.Launches.Select(launch => launch.Arguments[launch.Arguments.Count - 1]).ShouldBe(["/work/repos/antiphon", "/work/repos/gym-stat"]);
    }

    [Test]
    public void Git_missing_from_path_fails_after_the_allow_list_as_the_call_operator_did()
    {
        var world = new World { Git = null };
        world.Table(Antiphon);
        var run = world.Run("--execute", "--table-file", World.TablePath);
        run.Exit.ShouldBe(1, run.Text);
        run.Lines.ShouldBeEmpty();
        run.Error.ShouldContain("'git' is not recognized");
        world.Launcher.Launches.ShouldBeEmpty();
        world.Files.File.ReadAllText(World.Policy).ShouldBe("https://github.com/michal-ciechan/antiphon.git\n");
    }

    [Test]
    public void An_allow_list_path_that_is_a_directory_with_entries_fails_before_git()
    {
        var world = new World();
        world.Table(Antiphon);
        world.Files.AddFile(World.Policy + "/entry", new MockFileData("x"));
        var run = world.Run("--execute", "--table-file", World.TablePath);
        run.Exit.ShouldBe(1, run.Text);
        run.Error.ShouldNotBeEmpty();
        world.Launcher.Launches.ShouldBeEmpty();
        world.Files.File.Exists(World.Policy + "/entry").ShouldBeTrue("Remove-Item without -Recurse removed nothing");
    }

    // ---- the identity and the link test ----

    [Test]
    public void The_identity_is_the_scripts()
    {
        ServerRepositoryMirrors.Identity("https://github.com/michal-ciechan/Antiphon.git").ShouldBe("https://github.com/michal-ciechan/antiphon.git");
        ServerRepositoryMirrors.Identity("https://github.com/michal-ciechan/Antiphon").ShouldBe("https://github.com/michal-ciechan/antiphon.git");
        ServerRepositoryMirrors.Identity("HTTPS://GITHUB.COM/Michal-Ciechan/Antiphon").ShouldBe("https://github.com/michal-ciechan/antiphon.git");
        // String.EndsWith('.git') is case-sensitive.
        ServerRepositoryMirrors.Identity("https://github.com/x/Repo.GIT").ShouldBe("https://github.com/x/repo.git.git");
        ServerRepositoryMirrors.SlugOf("C:\\src\\Markdown-Package\\\\").ShouldBe("markdown-package");
        ServerRepositoryMirrors.SlugOf("//").ShouldBe("");
    }

    [Test]
    public async Task A_symbolic_or_hard_link_token_is_absent_on_the_real_disk()
    {
        var root = NewRoot();
        try
        {
            var table = Path.Combine(root, "table.txt");
            var token = Path.Combine(root, "token");
            await File.WriteAllTextAsync(table, Antiphon);
            await File.WriteAllTextAsync(token, Canary + "\n");
            var symbolic = Path.Combine(root, "symbolic");
            File.CreateSymbolicLink(symbolic, token);
            var hard = Path.Combine(root, "hard");
            CreateHardLink(hard, token);
            var repos = ScriptRoot(Path.Combine(root, "absent-repos"), OperatingSystem.IsWindows());
            foreach (var (path, reason) in new[] { (symbolic, "credential-absent"), (hard, "credential-absent"), (token, "credential-absent") })
            {
                var launcher = new RecordingLauncher();
                // The plain file's other name (the hard link) makes it a hard link too, as Get-Item's LinkType read it.
                var run = Invoke(new FileSystem(), new ServerRepositoryMirrorsSeams { Resolve = _ => "git" }, launcher, null,
                    ["--execute", "--table-file", table, "--token-file", path, "--policy-file", Path.Combine(root, "policy"), "--repos-root", repos]);
                run.Exit.ShouldBe(2, path + " " + run.Text);
                run.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=" + reason], path);
                run.Text.ShouldNotContain(Canary);
                launcher.Launches.ShouldBeEmpty();
            }
            File.Delete(hard);
            File.Delete(symbolic);
            var single = Invoke(new FileSystem(), new ServerRepositoryMirrorsSeams { Resolve = _ => "git" }, new RecordingLauncher(), null,
                ["--execute", "--table-file", table, "--token-file", token, "--policy-file", Path.Combine(root, "policy"), "--repos-root", repos]);
            single.Lines.ShouldBe(["SERVER2-MIRRORS refuse exit=2 reason=mirror-parent-missing"], "one name, readable: the credential is present");
            File.Exists(Path.Combine(root, "policy")).ShouldBeFalse();
        }
        finally { DeleteRoot(root); }
    }

    // ---- helpers ----

    private sealed record Result(int Exit, string[] Lines, string Error)
    {
        public string Text => string.Join("\n", Lines) + "\n" + Error;
    }

    // The production layout on a mock disk: the table, the token mount, the allow-list's directory and the work volume.
    private sealed class World
    {
        public const string TablePath = "/srv/mirrors.txt";
        public const string Token = "/run/antiphon/github-token/token";
        public const string Policy = "/run/antiphon/push-allow-list";

        public World()
        {
            Files.AddDirectory("/srv");
            Files.AddDirectory("/work/repos");
            Files.AddFile(Token, new MockFileData(Canary + "\n"));
            Launcher = new RecordingLauncher(launch =>
            {
                OnLaunch?.Invoke(launch);
                return new NightlyLaunchResult { ExitCode = Exit(launch) };
            });
        }

        public MockFileSystem Files { get; } = new();

        public HashSet<string> Links { get; } = new(StringComparer.Ordinal);

        public RecordingLauncher Launcher { get; }

        public Func<NightlyLaunch, int> Exit { get; init; } = _ => 0;

        public Action<NightlyLaunch>? OnLaunch { get; set; }

        public string? Git { get; init; } = "/usr/bin/git";

        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal)
        {
            ["PATH"] = "/usr/local/bin:/usr/bin",
            ["HOME"] = "/home/app",
            ["GIT_TERMINAL_PROMPT"] = "1",
        };

        public void Table(string text) => Files.AddFile(TablePath, new MockFileData(text));

        public Result Run(params string[] arguments) => Invoke(Files, new ServerRepositoryMirrorsSeams
        {
            IsLink = Links.Contains,
            Resolve = name => name == "git" ? Git : null,
            NewId = () => "0123456789abcdef0123456789abcdef",
        }, Launcher, Environment, arguments);
    }

    private static Result Invoke(IFileSystem files, ServerRepositoryMirrorsSeams? seams, RecordingLauncher launcher,
        IReadOnlyDictionary<string, string>? environment, string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = TimeProvider.System,
            Git = (_, _) => throw new InvalidOperationException("the mirrors run git on the launcher only"),
            Launcher = launcher,
            Processes = new NightlyProcessTable(),
            Probe = new NightlyHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
            Variables = _ => null,
        };
        var exit = ServerRepositoryMirrors.Run(ServerRepositoryMirrorsOptions.Parse(arguments, environment), host, files, seams);
        var lines = output.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new Result(exit, lines, error.ToString());
    }

    // The command takes only a POSIX-rooted --repos-root (server2 /work/repos). A Windows temp root goes in drive-relative, which
    // names the same directory from the temp drive.
    private static string ScriptRoot(string hostPath, bool windows)
    {
        if (!windows)
            return hostPath;
        var path = hostPath.Replace('\\', '/');
        if (path.Length >= 2 && path[1] == ':')
            path = path.Substring(2);
        return path;
    }

    private static void CreateHardLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(link, target, IntPtr.Zero)) throw new IOException("CreateHardLink failed: " + Marshal.GetLastWin32Error());
        }
        else if (LinkUnix(target, link) != 0)
        {
            throw new IOException("link failed: " + Marshal.GetLastWin32Error());
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkUnix(string existing, string link);

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "card-1776-mirrors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root) => GitFixtureCleanup.Delete(root);
}
