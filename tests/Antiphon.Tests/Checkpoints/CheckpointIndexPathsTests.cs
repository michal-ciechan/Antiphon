using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CheckpointIndexPaths (scripts/lib/CheckpointIndexPaths.cs) answers run-checkpoint.ps1's tracked-path guards from the
/// index file. Wherever it answers, the answer is git's (`git ls-files -z -- path` printed anything); where git must
/// decide, it answers null.
/// </summary>
[Category("Integration")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointIndexPathsTests : CheckpointTestBase
{
    [Test]
    [Timeout(120_000)]
    public void Index_answer_matches_git_ls_files_wherever_it_answers(CancellationToken cancellationToken)
    {
        var created = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(created, "init", "-q");
        // Paths built from git's own toplevel: a short (8.3) or symlinked temp path would not match it otherwise.
        var root = Path.GetFullPath(CheckpointFixtureProcess.Git(created, "rev-parse", "--show-toplevel").Trim());
        Write(root, "tracked.txt");
        Write(root, "src/app/Program.cs");
        Write(root, "src/app/bin/output.dll");
        Write(root, "loose/untracked.txt");
        Write(root, "src/app/new.txt");
        CheckpointFixtureProcess.Git(root, "add", "tracked.txt", "src/app/Program.cs");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        // The toplevel git itself reports: the guard only trusts the index for that checkout.
        var top = CheckpointFixtureProcess.Git(root, "rev-parse", "--show-toplevel").Trim();
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], null!).ShouldBeNull("git has not vouched");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], Path.GetDirectoryName(root)!)
            .ShouldBeNull("git reports a different toplevel");

        foreach (var relative in new[] { "tracked.txt", "src", "src/app", "src/app/Program.cs", "src/app/bin",
                     "src/app/bin/output.dll", "loose", "loose/untracked.txt", "src/app/new.txt", "missing", "missing/deeper.txt" })
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var answer = CheckpointIndexPaths.AnyTracked(root, [full], top);
            answer.ShouldNotBeNull(relative + " is a plain index read");
            answer.ShouldBe(GitTracks(root, full), relative + " matches git ls-files");
        }
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "loose"), Path.Combine(root, "tracked.txt")], top)
            .ShouldBe(true, "any of several paths");
        CheckpointIndexPaths.AnyTracked(root, [root], top).ShouldBe(true, "the checkout root holds tracked files");
        if (OperatingSystem.IsWindows())
            CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "TRACKED.TXT")], top)
                .ShouldBe(true, "case-insensitive on Windows can only call more paths tracked");

        // Git decides these.
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "src", "*.cs")], top).ShouldBeNull("pathspec glob");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(Path.GetDirectoryName(root)!, "elsewhere.txt")], top)
            .ShouldBeNull("outside the checkout");
        if (OperatingSystem.IsWindows())
            CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt.")], top)
                .ShouldBe(true, "Windows opens tracked.txt for tracked.txt. (git's pathspec would say untracked)");

        // A repository with no index yet is git's to answer.
        var fresh = Directory.CreateDirectory(Path.Combine(TempDir(), "fresh")).FullName;
        CheckpointFixtureProcess.Git(fresh, "init", "-q");
        CheckpointIndexPaths.AnyTracked(fresh, [Path.Combine(fresh, "a.txt")], fresh).ShouldBeNull("no index file");

        // A linked worktree's .git file names its gitdir.
        var linked = Path.Combine(Path.GetDirectoryName(root)!, "linked");
        CheckpointFixtureProcess.Git(root, "worktree", "add", "-q", "--detach", linked);
        var linkedTop = CheckpointFixtureProcess.Git(linked, "rev-parse", "--show-toplevel").Trim();
        var linkedTracked = Path.Combine(linked, "src", "app", "Program.cs");
        CheckpointIndexPaths.AnyTracked(linked, [linkedTracked], linkedTop).ShouldBe(true, "linked worktree index");
        CheckpointIndexPaths.AnyTracked(linked, [Path.Combine(linked, "loose")], linkedTop).ShouldBe(false);

        // A version 3 index (skip-worktree) is git's.
        CheckpointFixtureProcess.Git(root, "update-index", "--skip-worktree", "tracked.txt");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], top).ShouldBeNull("version 3 index");
        CheckpointFixtureProcess.Git(root, "update-index", "--no-skip-worktree", "tracked.txt");
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], top).ShouldBe(true, "version 2 again");

        // core.worktree elsewhere moves the work tree: git reports that toplevel, and the index is not trusted here.
        var elsewhere = Directory.CreateDirectory(Path.Combine(TempDir(), "elsewhere")).FullName;
        CheckpointFixtureProcess.Git(root, "config", "core.worktree", elsewhere);
        var moved = CheckpointFixtureProcess.Git(root, "rev-parse", "--show-toplevel").Trim();
        CheckpointIndexPaths.AnyTracked(root, [Path.Combine(root, "tracked.txt")], moved).ShouldBeNull("core.worktree");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Script_guards_vouch_once_per_run_and_never_trust_an_index_git_refuses(CancellationToken cancellationToken)
    {
        var created = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(created, "init", "-q");
        var root = Path.GetFullPath(CheckpointFixtureProcess.Git(created, "rev-parse", "--show-toplevel").Trim());
        Write(root, "src/kept.txt");
        Write(root, "loose/new.txt");
        CheckpointFixtureProcess.Git(root, "add", "src/kept.txt");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        var broken = Directory.CreateDirectory(Path.Combine(TempDir(), "broken")).FullName;
        CheckpointFixtureProcess.Git(broken, "init", "-q");
        Write(broken, "src/kept.txt");
        CheckpointFixtureProcess.Git(broken, "add", "src/kept.txt");
        CheckpointFixtureProcess.Git(broken, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        // A HEAD git cannot read: git refuses the repository, though its index is a valid plain index.
        File.WriteAllText(Path.Combine(broken, ".git", "HEAD"), "garbage\n");
        var trace = Directory.CreateDirectory(Path.Combine(TempDir(), "trace2")).FullName;

        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var lib = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "lib");
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . {{Quote(Path.Combine(lib, "checkpoint-process-runner.ps1"))}}
            . {{Quote(Path.Combine(lib, "checkpoint-index-paths.ps1"))}}
            if (-not ('Antiphon.Checkpoints.CheckpointIndexPaths' -as [type])) { throw 'index paths type not loaded' }
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile({{Quote(script)}}, [ref]$tokens, [ref]$errors)
            foreach ($name in 'Find-CheckpointGitDir', 'Invoke-CheckpointGit', 'Test-CheckpointComponentLink', 'Test-CheckpointStampRefusal', 'Test-CheckpointPreflight') {
                $fn = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object Name -eq $name | Select-Object -First 1
                if ($null -eq $fn) { throw "missing $name" }
                Invoke-Expression $fn.Extent.Text
            }
            $env:GIT_TRACE2_EVENT = {{Quote(trace)}}
            'STAMP_TRACKED=' + (Test-CheckpointStampRefusal {{Quote(Path.Combine(root, "src", "kept.txt"))}})
            'STAMP_NEW=' + (Test-CheckpointStampRefusal {{Quote(Path.Combine(root, "loose", "stamp.json"))}})
            'PREFLIGHT_TRACKED=' + (Test-CheckpointPreflight {{Quote(root)}} @({{Quote(Path.Combine(root, "src"))}}))
            'PREFLIGHT_NEW=' + (Test-CheckpointPreflight {{Quote(root)}} @({{Quote(Path.Combine(root, "loose"))}}))
            Remove-Item Env:GIT_TRACE2_EVENT
            'BROKEN_STAMP=' + (Test-CheckpointStampRefusal {{Quote(Path.Combine(broken, "src", "other.json"))}})
            'BROKEN_PREFLIGHT=' + (Test-CheckpointPreflight {{Quote(broken)}} @({{Quote(Path.Combine(broken, "loose"))}}))
            """;
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
            start.ArgumentList.Add(argument);
        var run = await CheckpointFixtureProcess.RunAsync(start, cancellationToken);
        run.Exit.ShouldBe(0, run.Stdout + run.Stderr);
        var lines = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('=')).ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..]);
        lines["STAMP_TRACKED"].ShouldBe("tracked");
        lines["STAMP_NEW"].ShouldBe("");
        lines["PREFLIGHT_TRACKED"].ShouldBe("tracked");
        lines["PREFLIGHT_NEW"].ShouldBe("");
        var events = Directory.GetFiles(trace).Select(File.ReadAllText).ToList();
        events.Count(text => text.Contains("\"rev-parse\"", StringComparison.Ordinal)).ShouldBe(1, "git vouches once for the run");
        events.Count(text => text.Contains("\"ls-files\"", StringComparison.Ordinal)).ShouldBe(0, "the index answered every guard");
        lines["BROKEN_STAMP"].ShouldBeOneOf("not-a-repository", "git-failed");
        lines["BROKEN_PREFLIGHT"].ShouldBe("git-failed", "a repository git refuses is never answered from its index");
    }

    [Test]
    [Timeout(60_000)]
    public void Hidden_entry_answer_comes_only_from_the_git_dir_git_reports(CancellationToken cancellationToken)
    {
        var created = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(created, "init", "-q");
        var root = Path.GetFullPath(CheckpointFixtureProcess.Git(created, "rev-parse", "--show-toplevel").Trim());
        Write(root, "a.txt");
        CheckpointFixtureProcess.Git(root, "add", "a.txt");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        var gitDir = CheckpointFixtureProcess.Git(root, "rev-parse", "--absolute-git-dir").Trim();

        CheckpointIndexPaths.AnyHiddenEntry(root, gitDir).ShouldBe(false, "plain index, no flags");
        CheckpointIndexPaths.AnyHiddenEntry(root, Path.Combine(TempDir(), "other.git")).ShouldBeNull("not the git dir git reported");
        CheckpointIndexPaths.AnyHiddenEntry(root, "").ShouldBeNull("git reported no git dir");

        CheckpointFixtureProcess.Git(root, "update-index", "--assume-unchanged", "a.txt");
        CheckpointIndexPaths.AnyHiddenEntry(root, gitDir).ShouldBe(true, "assume-unchanged is the version-2 flag ls-files -v prints lower case");
        CheckpointFixtureProcess.Git(root, "update-index", "--no-assume-unchanged", "a.txt");

        CheckpointFixtureProcess.Git(root, "update-index", "--skip-worktree", "a.txt");
        CheckpointIndexPaths.AnyHiddenEntry(root, gitDir).ShouldBeNull("version 3 index: ls-files -v decides");
        CheckpointFixtureProcess.Git(root, "update-index", "--no-skip-worktree", "a.txt");
        CheckpointIndexPaths.AnyHiddenEntry(root, gitDir).ShouldBe(false);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Head_and_stage_listing_read_from_files_are_git_s(CancellationToken cancellationToken)
    {
        var created = Directory.CreateDirectory(Path.Combine(TempDir(), "repo")).FullName;
        CheckpointFixtureProcess.Git(created, "init", "-q", "-b", "main");
        var root = Path.GetFullPath(CheckpointFixtureProcess.Git(created, "rev-parse", "--show-toplevel").Trim());
        Write(root, "a.txt");
        Write(root, "dir/b.txt");
        Write(root, "dir/caf" + (char)0xE9 + ".txt");
        CheckpointFixtureProcess.Git(root, "add", "-A");
        // An executable and a symlink entry on every platform: both are set in the index alone.
        CheckpointFixtureProcess.Git(root, "update-index", "--chmod=+x", "a.txt");
        var blob = CheckpointFixtureProcess.Git(root, "hash-object", "-w", "a.txt").Trim();
        CheckpointFixtureProcess.Git(root, "update-index", "--add", "--cacheinfo", "120000," + blob + ",link");
        CheckpointFixtureProcess.Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "seed");
        static string GitStageListing(string at) =>
            CheckpointFixtureProcess.Git(at, "-c", "core.quotepath=false", "ls-files", "-z", "-s");
        var seeded = GitStageListing(root);
        seeded.ShouldContain("100755 ");
        seeded.ShouldContain("120000 ");
        string GitHead(string at) => CheckpointFixtureProcess.Git(at, "rev-parse", "HEAD").Trim();
        void Agrees(string at, string label)
        {
            CheckpointProcessRunner.ReadHead(at).ShouldBe(GitHead(at), label + " HEAD");
            var found = CheckpointProcessRunner.TopLevelRepository(at);
            found.ShouldNotBeNull(label);
            CheckpointIndexPaths.StageListing(found[1]).ShouldBe(GitStageListing(at), label + " stage listing");
        }

        Agrees(root, "loose branch ref");
        CheckpointFixtureProcess.Git(root, "pack-refs", "--all");
        File.Exists(Path.Combine(root, ".git", "refs", "heads", "main")).ShouldBeFalse("the branch is packed");
        Agrees(root, "packed branch ref");
        var linked = Path.Combine(TempDir(), "linked");
        CheckpointFixtureProcess.Git(root, "worktree", "add", "-q", "-b", "side", linked);
        Agrees(Path.GetFullPath(CheckpointFixtureProcess.Git(linked, "rev-parse", "--show-toplevel").Trim()), "linked worktree");
        // Its checkout made the symlink entry a real link on Linux; the fixture teardown refuses a root holding one.
        File.Delete(Path.Combine(linked, "link"));
        CheckpointFixtureProcess.Git(root, "checkout", "-q", "--detach");
        Agrees(root, "detached HEAD");

        // Where the files cannot say it as git does, git answers.
        var sub = Directory.CreateDirectory(Path.Combine(root, "dir")).FullName;
        CheckpointProcessRunner.ReadHead(sub).ShouldBeNull("not the top level");
        CheckpointProcessRunner.TopLevelRepository(sub).ShouldBeNull("not the top level");
        var fresh = Directory.CreateDirectory(Path.Combine(TempDir(), "fresh")).FullName;
        CheckpointFixtureProcess.Git(fresh, "init", "-q");
        CheckpointProcessRunner.ReadHead(fresh).ShouldBeNull("an unborn branch");
        CheckpointFixtureProcess.Git(root, "update-index", "--skip-worktree", "a.txt");
        CheckpointIndexPaths.StageListing(Path.Combine(root, ".git")).ShouldBeNull("a version 3 index");
        var config = Path.Combine(root, ".git", "config");
        var original = File.ReadAllText(config);
        try
        {
            File.WriteAllText(config, original + "[extensions]\n\trefStorage = files\n");
            CheckpointProcessRunner.ReadHead(root).ShouldBeNull("a repository extension");
            CheckpointProcessRunner.TopLevelRepository(root).ShouldBeNull("a repository extension");
        }
        finally { File.WriteAllText(config, original); }

        // run-checkpoint.ps1's own Invoke-CheckpointGit (extracted, with the source library loaded) starts no git for
        // either read and returns what git prints.
        CheckpointFixtureProcess.Git(root, "update-index", "--no-skip-worktree", "a.txt");
        var trace = Directory.CreateDirectory(Path.Combine(TempDir(), "trace-fileread")).FullName;
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var source = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "lib", "checkpoint-source.ps1");
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1");
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", $$"""
            $ErrorActionPreference = 'Stop'
            . {{Quote(source)}}
            $tokens = $null; $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile({{Quote(script)}}, [ref]$tokens, [ref]$parseErrors)
            $fn = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-CheckpointGit' }, $true)
            if (@($parseErrors).Count -gt 0 -or $null -eq $fn) { throw 'Invoke-CheckpointGit could not be extracted from run-checkpoint.ps1' }
            Invoke-Expression $fn.Extent.Text
            $head = Invoke-CheckpointGit {{Quote(root)}} @('rev-parse', 'HEAD')
            $listing = Invoke-CheckpointGit {{Quote(root)}} @('ls-files', '-z', '-s')
            'HEAD=' + $head.Exit + ':' + ([string]$head.Stdout).Trim()
            # As UTF-8 hex: a console code page cannot change the bytes compared (the listing names caf<e9>.txt).
            'LISTING=' + $listing.Exit + ':' + [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes([string]$listing.Stdout))
            """ })
            start.ArgumentList.Add(argument);
        start.Environment["GIT_TRACE2_EVENT"] = trace;
        // The file reads under test, not a named tool build's, and git confined to this repository.
        foreach (var name in new[] { "ANTIPHON_CHECKPOINT_TOOL_ASSEMBLY", "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR",
                     "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM" })
            start.Environment.Remove(name);
        var run = await CheckpointFixtureProcess.RunAsync(start, cancellationToken);
        run.Exit.ShouldBe(0, run.Stdout + run.Stderr);
        run.Stdout.ShouldContain("HEAD=0:" + GitHead(root));
        run.Stdout.ShouldContain("LISTING=0:" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(GitStageListing(root))));
        Directory.GetFiles(trace).ShouldBeEmpty("Invoke-CheckpointGit answered both reads from the repository's files");
    }

    private static bool GitTracks(string root, string full) =>
        CheckpointFixtureProcess.Git(root, "ls-files", "-z", "--", full).Length != 0;

    private static void Write(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative + "\n");
    }
}
