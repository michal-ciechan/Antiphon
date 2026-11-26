using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0824 S2: the Windows desktop hot-slot root. Unit 51 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md ported
/// scripts/desktop-slot-provision.ps1 to the desktop-slot-provision command (Antiphon.Nightly.DesktopSlotProvision); these are the
/// deleted driver's C0824S2 cases (scripts/test-desktop-slot-provision.ps1, removed with the unreferenced batch on 2026-11-23) in
/// process. Roots are temporary directories (or text the command refuses before any disk access), the free space is the seam, and
/// git is a fake answering as a clone at the expected place (DesktopSlotProvisionGitTests runs real git). The script never cloned
/// and never ran dotnet; neither does the command.
/// </summary>
[Category("Unit")]
public sealed class DesktopSlotProvisionTests
{
    private const long OneGiB = 1024L * 1024 * 1024;
    private const long DefaultBudget = 32 * OneGiB;
    private const long Ample = 64 * OneGiB;

    [Test]
    [Arguments("C:\\src\\Antiphon")]
    [Arguments("C:\\src\\Antiphon\\server")]
    [Arguments("C:/src/Antiphon/client")]
    [Arguments("C:\\Antiphon\\releases")]
    [Arguments("C:\\Antiphon\\releases\\checkout-1")]
    [Arguments("C:/Antiphon/releases/candidates/rc")]
    [Arguments("C:\\Antiphon\\worktrees")]
    [Arguments("C:\\Antiphon\\worktrees\\card-task-x")]
    [Arguments("C:/Antiphon/worktrees/ws-01")]
    [Arguments("C:\\Antiphon")]
    [Arguments("C:\\Antiphon\\")]
    [Arguments("C:/ANTIPHON")]
    [Arguments("C:\\src")]
    [Arguments("C:\\src\\")]
    [Arguments("C:/src")]
    [Arguments("C:\\Antiphon\\worktrees\\..")]
    [Arguments("C:\\src\\Antiphon\\..")]
    public void Forbidden_roots_and_their_parents_are_refused_before_the_disk(string root)
    {
        DesktopSlotProvision.TextRefusal(root).ShouldBe("forbidden-root", root);
        DesktopSlotProvision.ForbiddenRoot(DesktopSlotProvision.SlotPath(root)).ShouldBeTrue(root);
        foreach (var removeEmpty in new[] { false, true })
        {
            using var world = new OperatorToolWorld("c0824s2");
            var (exit, lines) = Provision(world, root, execute: true, removeEmpty: removeEmpty, budget: 1);
            exit.ShouldBe(2, string.Join("\n", lines));
            lines.ShouldHaveSingleItem().ShouldStartWith("refused: forbidden-root ");
            world.Tools.Calls.ShouldBeEmpty();
        }
        DesktopSlotProvision.TextRefusal(DesktopSlotProvisionOptions.DefaultRoot).ShouldBe("");
        DesktopSlotProvision.ForbiddenRoot(DesktopSlotProvision.SlotPath(DesktopSlotProvisionOptions.DefaultRoot)).ShouldBeFalse();
        DesktopSlotProvision.SlotPath("C:/Antiphon/worktrees/../slots/./a/").ShouldBe("C:\\Antiphon\\slots\\a");
    }

    [Test]
    [Arguments("C:\\src\\ANTIPH~1\\pool", "alias-segment", true)]
    [Arguments("C:\\src\\Antiphon.\\pool", "alias-segment", true)]
    [Arguments("C:\\Antiphon\\releases.\\pool", "alias-segment", true)]
    [Arguments("C:\\Antiphon\\worktrees.", "alias-segment", true)]
    [Arguments("C:\\src\\Antiphon \\pool", "alias-segment", true)]
    [Arguments("\\\\server\\share\\releases\\pool", "unsupported-root", false)]
    [Arguments("//server/share/releases/pool", "unsupported-root", true)]
    [Arguments("\\\\localhost\\C$\\src\\Antiphon\\pool", "unsupported-root", false)]
    [Arguments("\\\\?\\C:\\Antiphon\\releases\\x", "unsupported-root", false)]
    [Arguments("\\\\.\\C:\\src\\Antiphon", "unsupported-root", false)]
    public void Path_aliases_and_device_roots_are_refused(string root, string reason, bool entry)
    {
        DesktopSlotProvision.TextRefusal(root).ShouldBe(reason, root);
        if (!entry) return;
        foreach (var removeEmpty in new[] { false, true })
        {
            using var world = new OperatorToolWorld("c0824s2");
            var (exit, lines) = Provision(world, root, execute: true, removeEmpty: removeEmpty, budget: 1);
            exit.ShouldBe(2);
            lines.ShouldHaveSingleItem().ShouldStartWith("refused: " + reason + " ");
        }
        // The unsupported root is named as given (trimmed); the others by their normalised path.
        using var named = new OperatorToolWorld("c0824s2");
        Provision(named, "  \\\\server\\share\\x  ").Lines.ShouldBe(["refused: unsupported-root \\\\server\\share\\x"]);
    }

    [Test]
    [Arguments("/")]
    [Arguments("C:\\")]
    [Arguments("C:/")]
    [Arguments("C:")]
    [Arguments("   ")]
    public void A_filesystem_root_is_refused(string root)
    {
        using var world = new OperatorToolWorld("c0824s2");
        var (exit, lines) = Provision(world, root, execute: true, budget: 1);
        exit.ShouldBe(2);
        lines.ShouldHaveSingleItem().ShouldStartWith("refused: filesystem-root");
    }

    [Test]
    public void A_root_under_a_releases_parent_is_refused_and_nothing_is_created()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var releases = Directory.CreateDirectory(world.PathOf("rel-parent", "releases")).FullName;
        var root = Path.Combine(releases, "pool");
        var (exit, lines) = Provision(world, root, execute: true, budget: 1);
        exit.ShouldBe(2);
        lines.ShouldHaveSingleItem().ShouldStartWith("refused: forbidden-root");
        Directory.Exists(root).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(releases).ShouldBeEmpty();
    }

    [Test]
    public void The_default_is_a_dry_run_that_prints_the_clone_commands_and_changes_nothing()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var root = world.PathOf("dry");
        var (exit, lines) = Provision(world, root);
        var clone = Path.Combine(root, "repos", "antiphon").Replace('\\', '/');
        exit.ShouldBe(0, string.Join("\n", lines));
        Directory.Exists(root).ShouldBeFalse();
        lines.ShouldBe([
            "mode=dry-run",
            "root=" + root,
            "free-bytes=" + Ample,
            "budget-bytes=" + DefaultBudget,
            .. DesktopSlotProvision.Children.Select(name => "would-create: " + Child(root, name)),
            "next-command: git clone https://github.com/michal-ciechan/Antiphon " + clone,
            "next-command: git -C " + clone + " config --local core.longpaths true",
        ]);
        world.Tools.Calls.ShouldBeEmpty();

        // --what-if wins over --execute.
        using var whatIf = new OperatorToolWorld("c0824s2");
        var whatIfRoot = whatIf.PathOf("whatif");
        var forced = Provision(whatIf, whatIfRoot, execute: true, whatIf: true);
        forced.Exit.ShouldBe(0);
        forced.Lines[0].ShouldBe("mode=dry-run");
        Directory.Exists(whatIfRoot).ShouldBeFalse();
    }

    [Test]
    public void A_dry_run_over_an_existing_clone_would_set_longpaths_without_git()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var root = world.PathOf("dry-git");
        Directory.CreateDirectory(Path.Combine(root, "repos", "antiphon", ".git"));
        var (exit, lines) = Provision(world, root);
        exit.ShouldBe(0);
        lines[^1].ShouldBe("would-set-longpaths=true");
        lines.ShouldNotContain(line => line.Contains("git clone"));
        world.Tools.Calls.ShouldBeEmpty("a dry run never runs git");
    }

    [Test]
    public void Execute_creates_the_four_directories_and_sets_longpaths_on_the_clone_only()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var root = world.PathOf("execute");
        var clone = Path.Combine(root, "repos", "antiphon");
        Directory.CreateDirectory(Path.Combine(clone, ".git"));
        CloneGit(world, clone);

        var (exit, lines) = Provision(world, root, execute: true);
        exit.ShouldBe(0, string.Join("\n", lines));
        lines.ShouldBe([
            "mode=execute",
            "root=" + root,
            "free-bytes=" + Ample,
            "budget-bytes=" + DefaultBudget,
            "created: " + Child(root, "worktrees"),
            "created: " + Child(root, "workspace-slots"),
            "created: " + Child(root, "retained"),
            "unchanged: " + Child(root, "repos"),
            "longpaths=true",
        ]);
        foreach (var name in DesktopSlotProvision.Children) Directory.Exists(Path.Combine(root, name)).ShouldBeTrue(name);
        File.Exists(Path.Combine(root, "workspace-slots", "ledger.json")).ShouldBeFalse("no ledger is written");
        Directory.EnumerateFileSystemEntries(Path.Combine(root, "worktrees")).ShouldBeEmpty("no ws-NN is made");

        var gitDir = Path.Combine(clone, ".git");
        var ceiling = Path.GetDirectoryName(clone)!;
        var calls = world.Tools.Calls;
        calls.Select(call => string.Join(" ", call.Arguments)).ShouldBe([
            "-C " + clone + " rev-parse --show-toplevel",
            "-C " + clone + " rev-parse --absolute-git-dir",
            "-C " + clone + " --git-dir " + gitDir + " --work-tree " + clone + " config --local core.longpaths true",
            "-C " + clone + " --git-dir " + gitDir + " --work-tree " + clone + " config --local --get core.longpaths",
        ]);
        calls.ShouldAllBe(call => call.FileName == "git" && call.Environment!["GIT_CEILING_DIRECTORIES"] == ceiling && call.Input == null);

        // A second run changes nothing and says so.
        world.ClearOutput();
        var second = Provision(world, root, execute: true);
        second.Exit.ShouldBe(0);
        second.Lines.Where(line => line.StartsWith("unchanged: ", StringComparison.Ordinal)).Count().ShouldBe(4);
        second.Lines[^1].ShouldBe("longpaths=true");
    }

    [Test]
    public void The_clone_must_resolve_to_itself_and_keep_longpaths_true()
    {
        foreach (var (name, answer, expected) in new (string, Func<string, string, OperatorToolResult?>, string)[]
                 {
                     ("unresolved-top", (args, _) => args.Contains("--show-toplevel") ? OperatorToolResult.Of(128, error: ["fatal: not a git repository"]) : null,
                         "refused: clone-unresolved "),
                     ("unresolved-git-dir", (args, _) => args.Contains("--absolute-git-dir") ? OperatorToolResult.Of(128) : null, "refused: clone-unresolved "),
                     ("enclosing-repository", (args, clone) => args.Contains("--show-toplevel") ? OperatorToolResult.Of(0, [Path.GetDirectoryName(Path.GetDirectoryName(clone))!]) : null,
                         "refused: clone-mismatch "),
                     ("borrowed-git-dir", (args, clone) => args.Contains("--absolute-git-dir") ? OperatorToolResult.Of(0, ["/elsewhere/.git"]) : null, "refused: clone-mismatch "),
                     ("stderr-noise", (args, clone) => args.Contains("--show-toplevel") ? OperatorToolResult.Of(0, [clone], ["warning: noise"]) : null, "refused: clone-mismatch "),
                     ("set-fails", (args, _) => args.EndsWith("core.longpaths true") ? OperatorToolResult.Of(1) : null, "refused: longpaths-not-true"),
                     ("reads-false", (args, _) => args.Contains("--get") ? OperatorToolResult.Of(0, ["false"]) : null, "refused: longpaths-not-true"),
                     ("reads-upper", (args, _) => args.Contains("--get") ? OperatorToolResult.Of(0, ["TRUE"]) : null, "longpaths=true"),
                     ("cannot-start", (args, _) => throw new System.ComponentModel.Win32Exception("git did not start"), "refused: clone-unresolved "),
                 })
        {
            using var world = new OperatorToolWorld("c0824s2");
            var root = world.PathOf("clone");
            var clone = Path.Combine(root, "repos", "antiphon");
            Directory.CreateDirectory(Path.Combine(clone, ".git"));
            CloneGit(world, clone, (args, path) => answer(args, path));
            var (exit, lines) = Provision(world, root, execute: true);
            lines[^1].ShouldStartWith(expected, Case.Sensitive, name);
            exit.ShouldBe(expected == "longpaths=true" ? 0 : 2, name);
        }

        // No git on PATH: refused before anything runs.
        using var missing = new OperatorToolWorld("c0824s2");
        var missingRoot = missing.PathOf("no-git");
        Directory.CreateDirectory(Path.Combine(missingRoot, "repos", "antiphon", ".git"));
        var refused = Provision(missing, missingRoot, execute: true);
        refused.Exit.ShouldBe(2);
        refused.Lines[^1].ShouldBe("refused: git-missing");
        missing.Tools.Calls.ShouldBeEmpty();
    }

    [Test]
    public void Free_space_below_the_budget_is_refused_and_nothing_is_created()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var root = world.PathOf("low");
        var low = Provision(world, root, execute: true, free: DefaultBudget - 1);
        low.Exit.ShouldBe(2);
        low.Lines[^1].ShouldBe("refused: low-free-space free=" + (DefaultBudget - 1) + " budget=" + DefaultBudget);
        Directory.Exists(root).ShouldBeFalse();
        Provision(world, root, free: DefaultBudget - 1).Exit.ShouldBe(2, "the dry run refuses too");

        var boundary = Provision(world, root, execute: true, free: DefaultBudget);
        boundary.Exit.ShouldBe(0, "free space equal to the budget is enough");
        Directory.Exists(Path.Combine(root, "worktrees")).ShouldBeTrue();

        var unknown = Provision(world, world.PathOf("unknown"), execute: true, free: -1);
        unknown.Lines.TakeLast(3).ShouldBe(["free-bytes=-1", "budget-bytes=" + DefaultBudget, "refused: free-space-unknown"]);
    }

    [Test]
    public void The_disk_budget_parameter_sets_the_floor()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var lowRoot = world.PathOf("param-low");
        Provision(world, lowRoot, execute: true, budget: 1, free: 1).Exit.ShouldBe(2);
        Directory.Exists(lowRoot).ShouldBeFalse();
        var okRoot = world.PathOf("param-ok");
        var ok = Provision(world, okRoot, execute: true, budget: 1, free: OneGiB);
        ok.Exit.ShouldBe(0);
        ok.Lines.ShouldContain("budget-bytes=" + OneGiB);
        ok.Lines.ShouldContain("next-command: git clone https://github.com/michal-ciechan/Antiphon " + Path.Combine(okRoot, "repos", "antiphon").Replace('\\', '/'));
        Directory.Exists(Path.Combine(okRoot, "retained")).ShouldBeTrue();

        var zero = Provision(world, world.PathOf("param-zero"), execute: true, budget: 0);
        zero.Exit.ShouldBe(2);
        zero.Lines.ShouldBe(["mode=execute", "root=" + world.PathOf("param-zero"), "refused: disk-budget"]);
    }

    [Test]
    public void Remove_empty_removes_only_empty_directories_without_a_ledger()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var root = world.PathOf("remove");
        Provision(world, root, execute: true).Exit.ShouldBe(0);

        var dry = Provision(world, root, removeEmpty: true);
        dry.Exit.ShouldBe(0);
        dry.Lines.ShouldBe(["mode=dry-run", "root=" + root, .. DesktopSlotProvision.Children.Select(name => "would-remove: " + Child(root, name))]);
        Directory.Exists(Path.Combine(root, "worktrees")).ShouldBeTrue("the dry run keeps them");

        var gone = Provision(world, root, execute: true, removeEmpty: true);
        gone.Exit.ShouldBe(0);
        gone.Lines.ShouldBe(["mode=execute", "root=" + root, .. DesktopSlotProvision.Children.Select(name => "removed: " + Child(root, name))]);
        foreach (var name in DesktopSlotProvision.Children) Directory.Exists(Path.Combine(root, name)).ShouldBeFalse(name);
    }

    [Test]
    public void Remove_empty_is_blocked_by_a_child_a_ledger_or_a_racing_writer()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var childRoot = world.PathOf("remove-child");
        Provision(world, childRoot, execute: true).Exit.ShouldBe(0);
        var leaf = Path.Combine(childRoot, "worktrees", "leaf.txt");
        File.WriteAllText(leaf, "stay");
        var blocked = Provision(world, childRoot, execute: true, removeEmpty: true);
        blocked.Exit.ShouldBe(2);
        blocked.Lines[^1].ShouldBe("refused: remove-empty-blocked reason=worktrees-child");
        File.Exists(leaf).ShouldBeTrue();
        Directory.Exists(Path.Combine(childRoot, "repos")).ShouldBeTrue();

        var ledgerRoot = world.PathOf("remove-ledger");
        Provision(world, ledgerRoot, execute: true).Exit.ShouldBe(0);
        var ledger = Path.Combine(ledgerRoot, "workspace-slots", "ledger.json");
        File.WriteAllText(ledger, "{}");
        var ledgerBlocked = Provision(world, ledgerRoot, execute: true, removeEmpty: true);
        ledgerBlocked.Exit.ShouldBe(2);
        ledgerBlocked.Lines[^1].ShouldBe("refused: remove-empty-blocked reason=ledger");
        File.Exists(ledger).ShouldBeTrue();
        Directory.Exists(Path.Combine(ledgerRoot, "retained")).ShouldBeTrue();

        // A child that appears between the check and the removal: refused, and the child stays.
        var raceRoot = world.PathOf("race");
        Provision(world, raceRoot, execute: true).Exit.ShouldBe(0);
        var racer = Path.Combine(raceRoot, "worktrees", "racer.txt");
        var raced = DesktopSlotProvision.Provision(Options(raceRoot, execute: true, removeEmpty: true), world.Host(), new FileSystem(), new DesktopSlotSeams
        {
            Tools = world.Tools,
            FreeBytes = _ => Ample,
            BeforeRemove = path =>
            {
                if (path.EndsWith("worktrees", StringComparison.Ordinal) && !File.Exists(racer)) File.WriteAllText(racer, "stay");
            },
        });
        raced.ExitCode.ShouldBe(2);
        raced.Lines[^1].ShouldBe("refused: remove-empty-blocked reason=not-empty");
        File.Exists(racer).ShouldBeTrue();
        Directory.Exists(Path.Combine(raceRoot, "repos")).ShouldBeTrue("nothing after the refusal is removed");
    }

    [Test]
    public void A_reparse_point_ancestor_is_refused()
    {
        var root = OperatingSystem.IsWindows() ? "C:\\fixture\\link\\pool" : "/fixture/link/pool";
        var link = OperatingSystem.IsWindows() ? "C:\\fixture\\link" : "/fixture/link";
        var files = new MockFileSystem();
        files.Directory.CreateDirectory(link);
        files.File.SetAttributes(link, FileAttributes.Directory | FileAttributes.ReparsePoint);
        using var world = new OperatorToolWorld("c0824s2");
        var result = DesktopSlotProvision.Provision(Options(root, execute: true), world.Host(), files, new DesktopSlotSeams { Tools = world.Tools, FreeBytes = _ => Ample });
        result.ExitCode.ShouldBe(2);
        result.Lines.ShouldBe(["refused: reparse-ancestor " + root]);
        files.Directory.Exists(root).ShouldBeFalse();
    }

    [Test]
    public void A_drive_that_does_not_exist_is_refused_before_anything_is_created()
    {
        var letter = Enumerable.Range('D', 'Z' - 'D' + 1).Select(code => (char)code).Reverse().First(drive => !Directory.Exists(drive + ":\\"));
        var root = letter + ":\\Antiphon\\slots";
        using var world = new OperatorToolWorld("c0824s2");
        var (exit, lines) = Provision(world, root, execute: true);
        exit.ShouldBe(2);
        lines.ShouldBe(["refused: root-unavailable " + root]);
        DesktopSlotProvision.DriveFreeBytes(new FileSystem(), root, OperatingSystem.IsWindows()).ShouldBe(-1);
    }

    [Test]
    public void The_drive_free_space_is_read_from_the_nearest_existing_ancestor()
    {
        using var world = new OperatorToolWorld("c0824s2");
        DesktopSlotProvision.DriveFreeBytes(new FileSystem(), world.PathOf("absent", "deeper"), OperatingSystem.IsWindows()).ShouldBeGreaterThan(0);
    }

    [Test]
    public void Run_prints_the_lines_and_the_flags_parse()
    {
        using var world = new OperatorToolWorld("c0824s2");
        var exit = DesktopSlotProvision.Run(Options("C:\\src\\Antiphon"), world.Host(), new FileSystem(), new DesktopSlotSeams { Tools = world.Tools });
        exit.ShouldBe(2);
        world.Out.ShouldBe("refused: forbidden-root C:\\src\\Antiphon\n");

        var defaults = DesktopSlotProvisionOptions.Parse([]);
        (defaults.Root, defaults.Execute, defaults.WhatIf, defaults.RemoveEmpty, defaults.DiskBudgetGiB).ShouldBe(("C:\\Antiphon\\slots", false, false, false, 32));
        var given = DesktopSlotProvisionOptions.Parse(["--root", "D:\\slots", "--execute", "--what-if", "--remove-empty", "--disk-budget-gib", "8"]);
        (given.Root, given.Execute, given.WhatIf, given.RemoveEmpty, given.DiskBudgetGiB).ShouldBe(("D:\\slots", true, true, true, 8));
        Should.Throw<NightlyUsageException>(() => DesktopSlotProvisionOptions.Parse(["--disk-budget-gib", "lots"]));
        Should.Throw<NightlyUsageException>(() => DesktopSlotProvisionOptions.Parse(["-Execute"]));
    }

    private static DesktopSlotProvisionOptions Options(string root, bool execute = false, bool whatIf = false, bool removeEmpty = false, int budget = 32) => new()
    {
        Root = root,
        Execute = execute,
        WhatIf = whatIf,
        RemoveEmpty = removeEmpty,
        DiskBudgetGiB = budget,
    };

    private static (int Exit, IReadOnlyList<string> Lines) Provision(OperatorToolWorld world, string root, bool execute = false, bool whatIf = false,
        bool removeEmpty = false, int budget = 32, long free = Ample)
    {
        var result = DesktopSlotProvision.Provision(Options(root, execute, whatIf, removeEmpty, budget), world.Host(), new FileSystem(),
            new DesktopSlotSeams { Tools = world.Tools, FreeBytes = _ => free });
        return (result.ExitCode, result.Lines);
    }

    private static string Child(string root, string name) => root.TrimEnd('\\', '/') + (OperatingSystem.IsWindows() ? "\\" : "/") + name;

    // git on PATH, answering as the clone at its place: the toplevel and the git dir in git's forward-slash form, the setting true.
    private static void CloneGit(OperatorToolWorld world, string clone, Func<string, string, OperatorToolResult?>? answer = null)
    {
        world.Tools.Found["git"] = "/fake/git";
        world.Tools.Behaviour = call =>
        {
            var args = string.Join(" ", call.Arguments);
            var given = answer?.Invoke(args, clone);
            if (given != null) return given;
            if (args.EndsWith("--show-toplevel", StringComparison.Ordinal)) return OperatorToolResult.Of(0, [clone.Replace('\\', '/')]);
            if (args.EndsWith("--absolute-git-dir", StringComparison.Ordinal)) return OperatorToolResult.Of(0, [Path.Combine(clone, ".git").Replace('\\', '/')]);
            if (args.EndsWith("--get core.longpaths", StringComparison.Ordinal)) return OperatorToolResult.Of(0, ["true"]);
            return OperatorToolResult.Of(0);
        };
    }
}
