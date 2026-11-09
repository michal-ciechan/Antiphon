using Antiphon.GitInProcess;
using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <see cref="InProcessGitStatus"/> (libgit2) against the real git CLI for the working-tree questions the
/// product asks: <c>git status</c> and <c>git ls-files</c> in the argument forms it actually runs (park,
/// land inspection, land worktree, guarded removals, recovery checkout, worktree manager). Unlike
/// <see cref="LibGit2VsGitDifferentialTests"/> this compares verdicts, not bytes: the clean/dirty answer
/// and the path set for status and <c>ls-files --others</c>, and the (tag, path) list for
/// <c>ls-files -v</c>. Each shape either agrees with git or falls back (null), and the shapes that must
/// stay with git are pinned as fallbacks. Real temp repositories, git on PATH, and git children isolated the
/// way libgit2 is in this process (<see cref="LibGit2ConfigIsolation"/>), so both read only the repository's config.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LibGit2VsGitWorkingTreeVerdictTests
{
    /// <summary>The status argument forms in product code, with the callers that run them.</summary>
    private static readonly (string Name, string[] Args, bool Untracked, bool Ignored)[] StatusForms =
    [
        // LocalTaskParkPublisher, LandWorkspace, GuardedWorktreeRemoval, RemoteWorkspaceService, DesktopLegacyRelease
        ("all", ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], true, false),
        // LandingGit.InspectAsync (CARD-1714)
        ("all+longpaths", ["-c", "core.longpaths=true", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], true, false),
        // AgentTaskLandingProtocol, DelegationWorktreeService, GuardedVerificationRemoval
        ("tracked", ["status", "--porcelain=v1", "-z", "--untracked-files=no", "--ignore-submodules=none"], false, false),
        ("tracked-plain", ["status", "--porcelain=v1", "-z", "--untracked-files=no"], false, false),
        // GitWorkspaceService, AgentTaskDispatcher
        ("all-plain", ["status", "--porcelain", "-z", "--untracked-files=all"], true, false),
        // WorktreeManager removal checks
        ("all+ignored", ["status", "--porcelain", "-z", "--untracked-files=all", "--ignored"], true, true),
    ];

    /// <summary>
    /// Which answers must stay with git: none; Walk, the ones that walk untracked or ignored files (status
    /// with untracked or ignored files, ls-files --others); WorkTree, every status form and ls-files --others;
    /// or every command.
    /// </summary>
    private enum Falls { None, Walk, WorkTree, All, KnownRacyDivergence }

    private sealed record Scenario(string Name, Func<Repo, Task> Arrange, Falls Fallback = Falls.None, bool Linked = false);

    private static readonly Scenario[] Scenarios =
    [
        new("clean", _ => Task.CompletedTask),
        new("clean-linked-worktree", _ => Task.CompletedTask, Linked: true),
        new("modified", r => r.WriteAsync("a.txt", "changed\n")),
        new("modified-linked-worktree", r => r.WriteAsync("a.txt", "changed\n"), Linked: true),
        new("same-size-edit-mtime-restored-settled", async r =>
        {
            // Committed with an old mtime, so the index entry is not racy: git and libgit2 both trust the
            // matching stat and both call this clean. Equally blind, pinned so neither drifts.
            await SameSizeEditAsync(r, DateTime.UtcNow.AddHours(-1));
        }),
        new("same-size-edit-mtime-restored-inside-the-index-second", async r =>
        {
            // The accepted divergence (see InProcessGitStatus): the entry's mtime shares the index write's
            // second but is earlier by nanoseconds, so git re-reads the content (dirty) and libgit2 trusts
            // the stat (clean). Only an mtime put back by hand gets here; pinned so a change is noticed.
            var second = DateTime.UtcNow.AddMinutes(-10);
            second = second.AddTicks(-(second.Ticks % TimeSpan.TicksPerSecond));
            await r.WriteAsync("s.txt", "alpha\n");
            File.SetLastWriteTimeUtc(r.Full("s.txt"), second.AddMilliseconds(100));
            await r.CommitAllAsync();                                    // the entry records second+0.1s
            File.SetLastWriteTimeUtc(Path.Combine(r.GitDir, "index"), second.AddMilliseconds(500));
            await r.WriteAsync("s.txt", "AAAAA\n");
            File.SetLastWriteTimeUtc(r.Full("s.txt"), second.AddMilliseconds(100));
        }, Fallback: Falls.KnownRacyDivergence),
        new("touched-only", r => { File.SetLastWriteTimeUtc(r.Full("a.txt"), DateTime.UtcNow.AddMinutes(5)); return Task.CompletedTask; }),
        new("deleted", r => { File.Delete(r.Full("a.txt")); return Task.CompletedTask; }),
        new("staged-new", async r => { await r.WriteAsync("new.txt", "n\n"); await r.GitAsync("add", "new.txt"); }),
        new("staged-then-reverted", async r =>
        {
            await r.WriteAsync("a.txt", "staged\n");
            await r.GitAsync("add", "a.txt");
            await r.WriteAsync("a.txt", "alpha\n");
        }),
        new("removed-from-index", r => r.GitAsync("rm", "--cached", "-q", "a.txt")),
        new("staged-rename", r => r.GitAsync("mv", "a.txt", "renamed.txt")),
        new("untracked-root", r => r.WriteAsync("loose.txt", "x\n")),
        new("untracked-deep", r => r.WriteAsync("d1/d2/d3/loose.txt", "x\n")),
        new("untracked-empty-dir", r => { Directory.CreateDirectory(r.Full("empty/inner")); return Task.CompletedTask; }),
        new("untracked-unicode-and-spaces", r => r.WriteAsync("dír with space/naïve 'q'.txt", "x\n")),
        new("ignored-gitignore", async r => { await r.WriteAsync(".gitignore", "*.log\nbin/\n"); await r.CommitAllAsync(); await r.WriteAsync("x.log", "l\n"); await r.WriteAsync("bin/out.dll", "b\n"); }),
        new("ignored-info-exclude", r => { File.AppendAllText(Path.Combine(r.GitDir, "info", "exclude"), "secret.txt\n"); return r.WriteAsync("secret.txt", "s\n"); }),
        new("ignored-excludesfile", async r =>
        {
            var file = Path.Combine(r.Root, "..", Path.GetFileName(r.Root) + ".excludes");
            await File.WriteAllTextAsync(file, "*.tmp\n");
            await r.GitAsync("config", "core.excludesFile", Path.GetFullPath(file).Replace('\\', '/'));
            await r.WriteAsync("scratch.tmp", "t\n");
        }),
        // The repository's own config, read from a linked worktree. LibGit2Sharp's Repository.Config reads
        // <common>/worktrees/<name>/config there (a file git never reads) and installs it into libgit2, so once
        // touched, the status lost this rule and listed scratch.tmp; config is read through ConfigOf instead.
        new("ignored-excludesfile-linked-worktree", async r =>
        {
            var file = Path.Combine(r.Root, "..", Path.GetFileName(r.Root) + ".excludes");
            await File.WriteAllTextAsync(file, "*.tmp\n");
            await r.GitAsync("config", "core.excludesFile", Path.GetFullPath(file).Replace('\\', '/'));
            await r.WriteAsync("scratch.tmp", "t\n");
        }, Linked: true),
        new("ignore-negated", async r => { await r.WriteAsync(".gitignore", "*.log\n!keep.log\n"); await r.CommitAllAsync(); await r.WriteAsync("drop.log", "d\n"); await r.WriteAsync("keep.log", "k\n"); }),
        new("ignored-dir-with-tracked-file", async r =>
        {
            await r.WriteAsync("out/tracked.txt", "t\n");
            await r.CommitAllAsync();
            await r.WriteAsync(".gitignore", "out/\n");
            await r.CommitAllAsync();
            await r.WriteAsync("out/generated.txt", "g\n");
        }),
        new("tracked-file-matching-ignore", async r => { await r.WriteAsync(".gitignore", "a.txt\n"); await r.CommitAllAsync(); await r.WriteAsync("a.txt", "changed\n"); }),
        new("nested-repository-untracked", r => InitNestedAsync(r, "nested")),
        new("nested-repository-ignored", async r => { await r.WriteAsync(".gitignore", "nested/\n"); await r.CommitAllAsync(); await InitNestedAsync(r, "nested"); }),
        new("nested-bare-repository-ignored", async r =>
        {
            await r.WriteAsync(".gitignore", "*.git/\n");
            await r.CommitAllAsync();
            await Repo.RunGitAsync(r.Root, "init", "-q", "--bare", "exposed.git");
        }),
        new("crlf-autocrlf-true-clean", async r =>
        {
            await r.GitAsync("config", "core.autocrlf", "true");
            await r.WriteAsync("crlf.txt", "one\ntwo\n");
            await r.CommitAllAsync();
            File.Delete(r.Full("crlf.txt"));
            await r.GitAsync("checkout", "--", "crlf.txt"); // checked out CRLF
        }),
        new("crlf-autocrlf-true-after-commit-linked-worktree", async r =>
        {
            // The guarded removal's CRLF case (CARD-1717 B-7): an LF blob committed first, autocrlf turned on
            // afterwards, and the file checked out again in a linked worktree. git: clean.
            await r.WriteAsync("feature.txt", "feature\n");
            await r.CommitAllAsync();
            await r.GitAsync("config", "core.autocrlf", "true");
            File.Delete(r.Full("feature.txt"));
            await r.GitAsync("checkout", "--", "feature.txt");
        }, Linked: true),
        new("crlf-autocrlf-false-rewritten", async r =>
        {
            await r.GitAsync("config", "core.autocrlf", "false");
            await r.WriteAsync("crlf.txt", "one\ntwo\n");
            await r.CommitAllAsync();
            await File.WriteAllTextAsync(r.Full("crlf.txt"), "one\r\ntwo\r\n");
        }),
        new("gitattributes-eol-crlf", async r =>
        {
            await r.GitAsync("config", "core.autocrlf", "false");
            await r.WriteAsync(".gitattributes", "*.txt text eol=crlf\n");
            await r.WriteAsync("eol.txt", "one\ntwo\n");
            await r.CommitAllAsync();
            File.Delete(r.Full("eol.txt"));
            await r.GitAsync("checkout", "--", "eol.txt");
        }),
        new("gitattributes-added-after-commit", async r =>
        {
            // A .gitattributes that renormalises committed bytes: git calls it dirty only once the stat changes.
            await r.GitAsync("config", "core.autocrlf", "false");
            await File.WriteAllTextAsync(r.Full("mixed.txt"), "one\r\ntwo\r\n");
            await r.CommitAllAsync();
            await r.WriteAsync(".gitattributes", "*.txt text\n");
            await r.CommitAllAsync();
            File.SetLastWriteTimeUtc(r.Full("mixed.txt"), DateTime.UtcNow.AddMinutes(5));
        }),
        new("chmod-in-index", r => r.GitAsync("update-index", "--chmod=+x", "a.txt")),
        new("filemode-false-chmod-on-disk", async r =>
        {
            if (OperatingSystem.IsWindows()) return;
            await r.GitAsync("config", "core.filemode", "false");
            File.SetUnixFileMode(r.Full("a.txt"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }),
        new("case-only-rename-on-disk", r =>
        {
            File.Move(r.Full("a.txt"), r.Full("tmp.txt"));
            File.Move(r.Full("tmp.txt"), r.Full("A.TXT"));
            return Task.CompletedTask;
        }),
        new("file-replaced-by-directory", async r => { File.Delete(r.Full("a.txt")); await r.WriteAsync("a.txt/inner.txt", "x\n"); }),
        new("assume-unchanged-modified", async r => { await r.GitAsync("update-index", "--assume-unchanged", "a.txt"); await r.WriteAsync("a.txt", "changed\n"); }),
        new("unmerged-conflict", async r =>
        {
            await r.GitAsync("checkout", "-q", "-b", "side");
            await r.WriteAsync("a.txt", "side\n");
            await r.CommitAllAsync();
            await r.GitAsync("checkout", "-q", "master");
            await r.WriteAsync("a.txt", "main\n");
            await r.CommitAllAsync();
            await Repo.RunGitAsync(r.Root, "merge", "-q", "side"); // conflicts, exit 1
        }),
        new("long-path-untracked", async r =>
        {
            // libgit2 cannot open a path past MAX_PATH here and throws for every work-tree read: git's.
            var deep = string.Join('/', Enumerable.Repeat("segment-of-thirty-characters-x", 10));
            var full = r.Full(deep + "/long.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, "deep\n");
        }, Fallback: Falls.WorkTree),
        // Shapes LibGit2Sharp cannot read the way git does: these must fall back.
        new("unreadable-directory", async r =>
        {
            // git warns "could not open directory" and lists nothing under it (CARD-1714): libgit2 must not call it clean.
            await r.WriteAsync("locked/inside.txt", "x\n");
            if (OperatingSystem.IsWindows())
            {
                var user = Environment.UserDomainName + "\\" + Environment.UserName;
                await Repo.RunToolAsync("icacls", r.Full("locked"), "/deny", user + ":(OI)(CI)(RD)");
                r.OnDispose(() => Repo.RunToolAsync("icacls", r.Full("locked"), "/remove:d", user));
            }
            else File.SetUnixFileMode(r.Full("locked"), UnixFileMode.None);
        }, Fallback: Falls.Walk),
        new("skip-worktree", async r => { await r.GitAsync("update-index", "--skip-worktree", "a.txt"); File.Delete(r.Full("a.txt")); }, Fallback: Falls.All),
        new("intent-to-add", async r => { await r.WriteAsync("ita.txt", "i\n"); await r.GitAsync("add", "-N", "ita.txt"); }, Fallback: Falls.All),
        new("index-version-4", r => r.GitAsync("update-index", "--index-version", "4"), Fallback: Falls.All),
        new("split-index", r => r.GitAsync("update-index", "--split-index"), Fallback: Falls.All),
        new("sparse-checkout", r => r.GitAsync("config", "core.sparseCheckout", "true"), Fallback: Falls.All),
        new("submodule", async r =>
        {
            await InitNestedAsync(r, "sub");
            await r.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "-q", "./sub", "sub");
            await r.CommitAllAsync();
            await File.WriteAllTextAsync(Path.Combine(r.Full("sub"), "inside.txt"), "dirty\n");
        }, Fallback: Falls.All),
    ];

    public static IEnumerable<string> ScenarioNames() => Scenarios.Select(s => s.Name);

    [Test]
    [MethodDataSource(nameof(ScenarioNames))]
    public async Task Status_and_ls_files_verdicts_match_git(string name)
    {
        var scenario = Scenarios.Single(s => s.Name == name);
        await using var repo = await Repo.CreateAsync(scenario.Linked);
        await scenario.Arrange(repo);
        var checkout = repo.Root;
        var mismatches = new List<string>();

        foreach (var (form, args, untracked, ignored) in StatusForms)
        {
            var mine = InProcessGitStatus.StatusPaths(checkout, untracked, ignored);
            if (scenario.Fallback == Falls.KnownRacyDivergence)
            {
                // libgit2 first: git's status rewrites the index and settles the entry.
                var dirtyToGit = (await Repo.RunGitAsync(checkout, args)).Output.Length != 0;
                if (!(mine is { Count: 0 } && dirtyToGit))
                    mismatches.Add($"status {form}: expected libgit2 clean and git dirty, got libgit2 [{string.Join(", ", mine ?? new HashSet<string>())}] git dirty={dirtyToGit}");
                break;
            }
            var git = await Repo.RunGitAsync(checkout, args);
            Check(mismatches, scenario.Fallback is Falls.All or Falls.WorkTree || scenario.Fallback == Falls.Walk && (untracked || ignored), "status " + form, mine, git, StatusPaths);
            // A verdict caller's dirty answer (GitTreeVerdict): porcelain-shaped rows over the same paths, so
            // emptiness is git's wherever the path set is.
            // The verdict's in-process dirty answer must never call a git-clean tree dirty.
            if (InProcessGitStatus.VerdictAnswersDirty(checkout, untracked, ignored) == true && git.Output.Length == 0)
                mismatches.Add($"status {form}: VERDICT answered dirty in process, git clean");
            var rows = InProcessGitStatus.StatusRows(checkout, untracked, ignored);
            if (rows is null != mine is null) mismatches.Add($"status {form}: rows {(rows is null ? "fell back" : "answered")}, paths did not");
            else if (rows is not null && mine is not null)
            {
                var listed = StatusPaths(string.Concat(rows.Select(r => r + "\0"))).ToHashSet(StringComparer.Ordinal);
                if (!listed.SetEquals(mine) || rows.Any(r => r.Length < 4 || r[2] != ' ' || r[..2] == "  "))
                    mismatches.Add($"status {form}: verdict rows [{string.Join(", ", rows)}] paths [{string.Join(", ", mine.Order())}]");
            }
        }
        foreach (var ignored in new[] { false, true })
        {
            var mine = InProcessGitStatus.OtherPaths(checkout, ignored);
            string[] args = ignored ? ["ls-files", "--others", "--ignored", "--exclude-standard", "-z"] : ["ls-files", "--others", "--exclude-standard", "-z"];
            var git = await Repo.RunGitAsync(checkout, args);
            Check(mismatches, scenario.Fallback is Falls.All or Falls.Walk or Falls.WorkTree, string.Join(' ', args), mine, git, Records);
        }
        {
            var mine = InProcessGitStatus.IndexTags(checkout);
            var git = await Repo.RunGitAsync(checkout, "ls-files", "-v", "-z");
            if (scenario.Fallback == Falls.All) { if (mine is not null) mismatches.Add("ls-files -v: answered, must fall back"); }
            else if (mine is null) mismatches.Add("ls-files -v: fell back");
            else
            {
                var expected = Records(git.Output).Select(r => r[0] + " " + r[2..]).ToList();
                var actual = mine.Select(t => t.Tag + " " + t.Path).ToList();
                if (git.ExitCode != 0 || !expected.SequenceEqual(actual))
                    mismatches.Add($"ls-files -v: git [{string.Join(", ", expected)}] libgit2 [{string.Join(", ", actual)}]");
            }
        }
        {
            var mine = InProcessGitStatus.HasUnmerged(checkout);
            var git = await Repo.RunGitAsync(checkout, "ls-files", "--unmerged", "-z");
            if (scenario.Fallback == Falls.All) { if (mine is not null) mismatches.Add($"ls-files --unmerged: answered {mine}, must fall back"); }
            else if (mine is null) mismatches.Add("ls-files --unmerged: fell back");
            else if (mine != (git.Output.Length != 0)) mismatches.Add($"ls-files --unmerged: git {git.Output.Length != 0} libgit2 {mine}");
        }

        // ls-files -z and ls-files --stage -z: git's bytes, read from the index file; an unmerged index and every
        // shape that falls back for all reads stay with git. ls-tree of HEAD (both forms) is the same in every shape.
        foreach (var args in IndexForms)
        {
            var mine = InProcessGitStatus.TryAnswer(checkout, args, ChildEnvironment());
            var fallback = scenario.Fallback == Falls.All || name == "unmerged-conflict";
            await SameBytesAsync(mismatches, checkout, args, mine, fallback);
        }
        var head = (await Repo.RunGitAsync(checkout, "rev-parse", "HEAD")).Output.Trim();
        foreach (var args in TreeForms(head))
            await SameBytesAsync(mismatches, checkout, args, InProcessGitReads.TryAnswer(checkout, args), fallback: false);

        mismatches.ShouldBeEmpty($"{name}:\n  " + string.Join("\n  ", mismatches));
    }

    /// <summary>The index listings GuardedWorktreeRemoval, GuardedVerificationRemoval and LandingGit run.</summary>
    private static readonly string[][] IndexForms = [["ls-files", "-z"], ["ls-files", "--stage", "-z"]];

    /// <summary>The tree listings LandingGit (recovery checkout) and GuardedWorktreeRemoval run.</summary>
    private static string[][] TreeForms(string oid) => [["ls-tree", "-r", "-z", oid], ["ls-tree", "-r", "-z", "--full-tree", oid]];

    /// <summary>Exactly git's exit code and output, or null where <paramref name="fallback"/> says git must answer.</summary>
    private static async Task SameBytesAsync(List<string> mismatches, string cwd, string[] args, LandingGitResult? mine, bool fallback)
    {
        var what = string.Join(' ', args);
        if (fallback)
        {
            if (mine is not null) mismatches.Add($"{what}: answered, must fall back");
            return;
        }
        if (mine is null) { mismatches.Add(what + ": fell back"); return; }
        var git = await Repo.RunGitAsync(cwd, args);
        if (git.ExitCode != 0 || git.Error.Length != 0) mismatches.Add($"{what}: git exit {git.ExitCode}: {git.Error.Trim()}");
        if (mine.ExitCode != git.ExitCode || mine.Output != git.Output)
            mismatches.Add($"{what}: git [{git.ExitCode}] {Visible(git.Output)} in process [{mine.ExitCode}] {Visible(mine.Output)}");
    }

    private static string Visible(string output) => output.Replace("\0", "\\0").Replace("\t", "\\t");

    /// <summary>
    /// A repository shape for the index and tree listings. <c>Index</c>/<c>Tree</c>: whether ls-files and ls-tree
    /// must match git byte for byte (true) or fall back (false). <c>Moved</c>: the linked worktree is renamed on disk
    /// afterwards (as the guarded removal moves a tree aside) and both read it at the new path.
    /// </summary>
    private sealed record ListingShape(string Name, Func<Repo, Task> Arrange, bool Index = true, bool Tree = true, bool Linked = false,
        bool Moved = false);

    private static readonly ListingShape[] ListingShapes =
    [
        new("seed", _ => Task.CompletedTask),
        new("empty-index-and-empty-tree", async r =>
        {
            await r.GitAsync("rm", "-q", "--cached", "a.txt", "b.txt");
            await r.GitAsync("commit", "-q", "-m", "empty");
        }),
        new("rich-tree", ArrangeRichAsync),
        new("rich-tree-linked-worktree", ArrangeRichAsync, Linked: true),
        new("rich-tree-moved-linked-worktree", ArrangeRichAsync, Linked: true, Moved: true),
        // Any core.worktree in the config git reads: git's (the main checkout exists, so git's listing still works).
        new("moved-linked-worktree-with-core-worktree", r => r.GitAsync("config", "core.worktree",
            Path.GetFullPath(Path.Combine(r.Root, "..", "main")).Replace('\\', '/')), Index: false, Linked: true, Moved: true),
        // A .git file that names a main git directory (a submodule-style git file): git and libgit2 both take the
        // directory holding the file as the work tree and read that git directory's index.
        new("moved-linked-worktree-whose-git-file-names-the-main-repository", r =>
        {
            var dotGit = Path.Combine(r.Root, ".git");
            File.SetAttributes(dotGit, FileAttributes.Normal); // git hides it on Windows
            return File.WriteAllTextAsync(dotGit, "gitdir: " + Path.GetFullPath(Path.Combine(r.Root, "..", "main", ".git")).Replace('\\', '/') + "\n");
        }, Linked: true, Moved: true),
        new("assume-unchanged", r => r.GitAsync("update-index", "--assume-unchanged", "a.txt")),
        new("long-name-past-the-12-bit-length", async r =>
        {
            // A name of 4095+ bytes stores 0xFFF as its length and is found by its NUL.
            var name = "long/" + new string('n', 4200) + ".txt";
            await r.GitAsync("update-index", "--add", "--cacheinfo", "100644," + await BlobAsync(r, "long\n") + "," + name);
            await r.GitAsync("commit", "-q", "-m", "long");
        }),
        new("non-utf8-name", async r =>
        {
            var tree = await MkTreeAsync(r, "100644", [.. "bad"u8, 0xFF]);
            await r.GitAsync("read-tree", tree);
            await r.GitAsync("commit", "-q", "-m", "odd");
        }, Index: false, Tree: false),
        new("control-character-in-tree", async r =>
        {
            // git refuses such a path in the index on Windows; the tree alone is enough for ls-tree.
            var tree = await MkTreeAsync(r, "100644", [.. "ct"u8, 0x01, .. "rl"u8]);
            await r.GitAsync("reset", "-q", "--soft", await CommitTreeAsync(r, tree));
        }, Tree: false),
        new("non-canonical-tree-mode", async r =>
        {
            // mktree keeps 100664 as written; git prints it canonicalised, libgit2 by its own rules: git's.
            var tree = await MkTreeAsync(r, "100664", [.. "group-writable"u8]);
            await r.GitAsync("reset", "-q", "--soft", await CommitTreeAsync(r, tree));
        }, Tree: false),
        new("gitlink", async r =>
        {
            var head = (await Repo.RunGitAsync(r.Root, "rev-parse", "HEAD")).Output.Trim();
            await r.GitAsync("update-index", "--add", "--cacheinfo", "160000," + head + ",sub");
            await r.GitAsync("commit", "-q", "-m", "gitlink");
        }, Index: false),
        new("unmerged", async r =>
        {
            await r.GitAsync("checkout", "-q", "-b", "side");
            await r.WriteAsync("a.txt", "side\n");
            await r.CommitAllAsync();
            await r.GitAsync("checkout", "-q", "master");
            await r.WriteAsync("a.txt", "main\n");
            await r.CommitAllAsync();
            await Repo.RunGitAsync(r.Root, "merge", "-q", "side"); // conflicts, exit 1
        }, Index: false),
        new("skip-worktree", r => r.GitAsync("update-index", "--skip-worktree", "a.txt"), Index: false),
        new("intent-to-add", async r => { await r.WriteAsync("ita.txt", "i\n"); await r.GitAsync("add", "-N", "ita.txt"); }, Index: false),
        new("index-version-4", r => r.GitAsync("update-index", "--index-version", "4"), Index: false),
        new("split-index", r => r.GitAsync("update-index", "--split-index"), Index: false),
        new("sparse-checkout", r => r.GitAsync("config", "core.sparseCheckout", "true"), Index: false),
    ];

    /// <summary>Nested directories, names that sort differently by byte and by case, spaces, unicode, an
    /// executable file and a symlink (staged by id, so no link is needed on disk), committed.</summary>
    private static async Task ArrangeRichAsync(Repo r)
    {
        foreach (var path in new[] { "d1/d2/d3/deep.txt", "d1/z.txt", "a-b.txt", "a/b.txt", "a0.txt", "B.txt", "dír with space/naïve 'q'.txt", "run.sh" })
            await r.WriteAsync(path, path + "\n");
        await r.GitAsync("add", "-A");
        await r.GitAsync("update-index", "--chmod=+x", "run.sh");
        await r.GitAsync("update-index", "--add", "--cacheinfo", "120000," + await BlobAsync(r, "a.txt") + ",link");
        await r.GitAsync("commit", "-q", "-m", "rich");
    }

    public static IEnumerable<string> ListingShapeNames() => ListingShapes.Select(s => s.Name);

    [Test]
    [MethodDataSource(nameof(ListingShapeNames))]
    public async Task Index_and_tree_listings_match_git_bytes(string name)
    {
        var shape = ListingShapes.Single(s => s.Name == name);
        await using var repo = await Repo.CreateAsync(shape.Linked);
        await shape.Arrange(repo);
        var root = repo.Root;
        if (shape.Moved)
        {
            root = repo.Root + ".moved";
            Directory.Move(repo.Root, root);
        }
        var mismatches = new List<string>();
        foreach (var args in IndexForms)
            await SameBytesAsync(mismatches, root, args, InProcessGitStatus.TryAnswer(root, args, ChildEnvironment()), !shape.Index);
        var commit = (await Repo.RunGitAsync(root, "rev-parse", "HEAD")).Output.Trim();
        var tree = (await Repo.RunGitAsync(root, "rev-parse", "HEAD^{tree}")).Output.Trim();
        foreach (var oid in new[] { commit, tree })
            foreach (var args in TreeForms(oid))
                await SameBytesAsync(mismatches, root, args, InProcessGitReads.TryAnswer(root, args), !shape.Tree);
        mismatches.ShouldBeEmpty($"{name}:\n  " + string.Join("\n  ", mismatches));
    }

    [Test]
    public async Task Index_and_tree_listings_leave_other_shapes_and_arguments_to_git()
    {
        await using var repo = await Repo.CreateAsync(linked: false);
        var head = (await Repo.RunGitAsync(repo.Root, "rev-parse", "HEAD")).Output.Trim();
        var blob = (await Repo.RunGitAsync(repo.Root, "rev-parse", "HEAD:a.txt")).Output.Trim();
        await repo.GitAsync("tag", "-a", "-m", "t", "annotated");
        var tag = (await Repo.RunGitAsync(repo.Root, "rev-parse", "annotated")).Output.Trim();
        // Not a full id, an id of no object, of a blob or of a tag; arguments the product never runs.
        foreach (var args in new[]
                 {
                     new[] { "ls-tree", "-r", "-z", head[..12] }, ["ls-tree", "-r", "-z", "HEAD"], ["ls-tree", "-r", "-z", head.ToUpperInvariant()],
                     ["ls-tree", "-r", "-z", new string('1', 40)], ["ls-tree", "-r", "-z", blob], ["ls-tree", "-r", "-z", tag],
                     ["ls-tree", "-r", "-z", "--full-tree", head[..12]], ["ls-tree", "-z", head], ["ls-tree", "-r", head],
                     ["ls-tree", "-r", "-z", "--full-tree", head, "--", "a.txt"], ["ls-tree", "-r", "-z", "--name-only", head],
                 })
            InProcessGitReads.TryAnswer(repo.Root, args).ShouldBeNull(string.Join(' ', args));
        foreach (var args in new[]
                 {
                     new[] { "ls-files" }, ["ls-files", "-z", "--", "a.txt"], ["ls-files", "-s", "-z"], ["ls-files", "--stage"],
                     ["-c", "core.quotePath=false", "ls-files", "-z"], ["ls-files", "--cached", "-z"],
                 })
            InProcessGitStatus.TryAnswer(repo.Root, args, ChildEnvironment()).ShouldBeNull(string.Join(' ', args));

        // Below the checkout root git lists relative to the directory: git's. So is a git directory.
        Directory.CreateDirectory(repo.Full("sub"));
        foreach (var args in IndexForms)
        {
            InProcessGitStatus.TryAnswer(repo.Full("sub"), args, ChildEnvironment()).ShouldBeNull("subdirectory " + string.Join(' ', args));
            InProcessGitStatus.TryAnswer(repo.GitDir, args, ChildEnvironment()).ShouldBeNull("git dir " + string.Join(' ', args));
        }
        foreach (var args in TreeForms(head))
            InProcessGitReads.TryAnswer(repo.Full("sub"), args).ShouldBeNull("subdirectory " + string.Join(' ', args));

        // No index file yet (a fresh repository): git prints nothing, this leaves it to git.
        var fresh = Path.Combine(repo.Root, "..", "fresh");
        Directory.CreateDirectory(fresh);
        await Repo.RunGitAsync(fresh, "init", "-q", "-b", "master");
        foreach (var args in IndexForms) InProcessGitStatus.TryAnswer(fresh, args, ChildEnvironment()).ShouldBeNull("no index");

        // A control character in an index path (git refuses to write one on Windows, so the name is patched in with a
        // fresh checksum): git prints it raw, this leaves it to git.
        var index = Path.Combine(repo.GitDir, "index");
        var bytes = await File.ReadAllBytesAsync(index);
        var at = bytes.AsSpan().IndexOf("a.txt\0"u8);
        at.ShouldBeGreaterThan(0);
        bytes[at + 1] = 0x01;
        System.Security.Cryptography.SHA1.HashData(bytes.AsSpan(0, bytes.Length - 20)).CopyTo(bytes, bytes.Length - 20);
        await File.WriteAllBytesAsync(index, bytes);
        (await Repo.RunGitAsync(repo.Root, "ls-files", "-z")).Output.ShouldStartWith("a\u0001txt\0");
        foreach (var args in IndexForms) InProcessGitStatus.TryAnswer(repo.Root, args, ChildEnvironment()).ShouldBeNull("control character");
        InProcessGitStatus.ListIndex(bytes, stage: false).ShouldBeNull("the index reader itself refuses it");

        // A damaged index (checksum) is git's error to report.
        bytes[at + 1] = (byte)'.';
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(index, bytes);
        foreach (var args in IndexForms) InProcessGitStatus.TryAnswer(repo.Root, args, ChildEnvironment()).ShouldBeNull("bad checksum");
    }

    [Test]
    public async Task The_runner_answers_the_landing_listings_without_a_git_process()
    {
        await using var repo = await Repo.CreateAsync(linked: true);
        await ArrangeRichAsync(repo);
        var runner = new SpawnCountingGit();
        var head = (await Repo.RunGitAsync(repo.Root, "rev-parse", "HEAD")).Output.Trim();
        foreach (var args in IndexForms.Concat(TreeForms(head)))
        {
            var answer = await runner.RunAsync(repo.Root, args, CancellationToken.None);
            var git = await Repo.RunGitAsync(repo.Root, args);
            (answer.ExitCode, answer.Output, answer.WroteStandardError).ShouldBe((git.ExitCode, git.Output, false), string.Join(' ', args));
        }
        runner.Spawns.ShouldBe(0);
        // An unmerged-free index that turns version 3 (skip-worktree) runs git again.
        await repo.GitAsync("update-index", "--skip-worktree", "a.txt");
        (await runner.RunAsync(repo.Root, ["ls-files", "-z"], CancellationToken.None)).Output
            .ShouldBe((await Repo.RunGitAsync(repo.Root, "ls-files", "-z")).Output);
        runner.Spawns.ShouldBe(1);
    }

    private static async Task<string> BlobAsync(Repo r, string content)
    {
        var path = Path.Combine(r.Root, "..", "blob-" + Guid.NewGuid().ToString("N")[..8]);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        var hashed = await Repo.RunGitAsync(r.Root, "hash-object", "-w", Path.GetFullPath(path));
        hashed.ExitCode.ShouldBe(0, hashed.Error);
        return hashed.Output.Trim();
    }

    /// <summary>A tree of one blob entry ("x\n") with this mode and raw name bytes, through git mktree.</summary>
    private static async Task<string> MkTreeAsync(Repo r, string mode, byte[] name)
    {
        var blob = await BlobAsync(r, "x\n");
        byte[] input = [.. Encoding.ASCII.GetBytes(mode + " blob " + blob + "\t"), .. name, (byte)'\n'];
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = r.Root, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var (key, value) in Isolation) start.Environment[key] = value;
        start.ArgumentList.Add("mktree");
        using var p = Process.Start(start)!;
        await p.StandardInput.BaseStream.WriteAsync(input);
        p.StandardInput.Close();
        var output = await p.StandardOutput.ReadToEndAsync();
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        p.ExitCode.ShouldBe(0, "mktree: " + error);
        return output.Trim();
    }

    private static async Task<string> CommitTreeAsync(Repo r, string tree)
    {
        var committed = await Repo.RunGitAsync(r.Root, "commit-tree", tree, "-p", "HEAD", "-m", "tree");
        committed.ExitCode.ShouldBe(0, committed.Error);
        return committed.Output.Trim();
    }

    private static void Check(List<string> mismatches, bool fallback, string what, IReadOnlySet<string>? mine,
        (int ExitCode, string Output, string Error) git, Func<string, IEnumerable<string>> parse)
    {
        if (fallback)
        {
            if (mine is not null) mismatches.Add($"{what}: answered [{string.Join(", ", mine.Order())}], must fall back");
            return;
        }
        if (mine is null) { mismatches.Add(what + ": fell back"); return; }
        if (git.ExitCode != 0) { mismatches.Add($"{what}: git exit {git.ExitCode}: {git.Error.Trim()}"); return; }
        var expected = parse(git.Output).ToHashSet(StringComparer.Ordinal);
        if (expected.Count == 0 != (mine.Count == 0))
            mismatches.Add($"{what}: VERDICT git {(expected.Count == 0 ? "clean" : "dirty")} libgit2 {(mine.Count == 0 ? "clean" : "dirty")}");
        if (!expected.SetEquals(mine))
            mismatches.Add($"{what}: paths git [{string.Join(", ", expected.Order())}] libgit2 [{string.Join(", ", mine.Order())}]");
        if (git.Error.Length != 0) mismatches.Add($"{what}: git warned: {git.Error.Trim()}");
    }

    private static IEnumerable<string> Records(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Paths in <c>status --porcelain -z</c>: "XY path", renames and copies followed by the old path.</summary>
    private static IEnumerable<string> StatusPaths(string output)
    {
        var records = Records(output).ToArray();
        for (var i = 0; i < records.Length; i++)
        {
            var status = records[i][..2];
            yield return records[i][3..];
            if (status[0] is 'R' or 'C') yield return records[++i];
        }
    }

    /// <summary>s.txt committed with <paramref name="mtime"/>, then rewritten to other bytes of the same size with that mtime put back.</summary>
    private static async Task SameSizeEditAsync(Repo r, DateTime mtime)
    {
        await r.WriteAsync("s.txt", "alpha\n");
        File.SetLastWriteTimeUtc(r.Full("s.txt"), mtime);
        await r.CommitAllAsync();
        await r.WriteAsync("s.txt", "AAAAA\n");
        File.SetLastWriteTimeUtc(r.Full("s.txt"), mtime);
    }

    /// <summary>What the suite's isolated git children see: no system, global or XDG config.</summary>
    private static readonly (string Name, string? Value)[] Isolation =
    [
        ("GIT_CONFIG_NOSYSTEM", "1"),
        ("GIT_CONFIG_GLOBAL", OperatingSystem.IsWindows() ? "NUL" : "/dev/null"),
        ("XDG_CONFIG_HOME", LibGit2ConfigIsolation.EmptyDirectory),
    ];

    /// <summary>This process's environment, isolated as <see cref="Isolation"/>, with <paramref name="changes"/> applied.</summary>
    private static Dictionary<string, string?> ChildEnvironment(params (string Name, string? Value)[] changes)
        => MachineEnvironment([.. Isolation, .. changes]);

    private static Dictionary<string, string?> MachineEnvironment(params (string Name, string? Value)[] changes)
    {
        var env = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string?)e.Value;
        foreach (var (name, value) in changes) env[name] = value;
        return env;
    }

    [Test]
    public async Task The_runner_answers_clean_and_empty_reads_with_git_bytes_and_leaves_dirty_ones_to_git()
    {
        await using var repo = await Repo.CreateAsync(linked: false);
        string[][] forms =
        [
            .. StatusForms.Select(f => f.Args),
            ["ls-files", "--others", "--exclude-standard", "-z"],
            ["ls-files", "--others", "--ignored", "--exclude-standard", "-z"],
            ["ls-files", "--unmerged", "-z"],
        ];
        foreach (var args in forms)
        {
            var answer = InProcessGitStatus.TryAnswer(repo.Root, args, ChildEnvironment());
            answer.ShouldNotBeNull(string.Join(' ', args) + " on a clean tree");
            var git = await Repo.RunGitAsync(repo.Root, args);
            (answer.ExitCode, answer.Output).ShouldBe((git.ExitCode, git.Output), string.Join(' ', args));
        }
        await repo.WriteAsync("dír/naïve.txt", "x\n");
        await repo.GitAsync("add", "-A");
        await repo.GitAsync("update-index", "--assume-unchanged", "b.txt");
        var tags = InProcessGitStatus.TryAnswer(repo.Root, ["ls-files", "-v", "-z"], ChildEnvironment()).ShouldNotBeNull();
        tags.Output.ShouldBe((await Repo.RunGitAsync(repo.Root, "ls-files", "-v", "-z")).Output);

        await repo.WriteAsync("a.txt", "dirty\n");
        await repo.WriteAsync("loose.txt", "u\n");
        foreach (var args in forms.Where(f => f[0] is "status" or "-c" || f.Contains("--others") && !f.Contains("--ignored")))
            InProcessGitStatus.TryAnswer(repo.Root, args, ChildEnvironment()).ShouldBeNull(string.Join(' ', args) + " on a dirty tree is git's");
    }

    [Test]
    public async Task A_verdict_caller_gets_a_dirty_tree_in_process_and_every_other_caller_gets_git()
    {
        await using var repo = await Repo.CreateAsync(linked: false);
        // Certain entries: a staged change and an untracked file.
        await repo.WriteAsync("a.txt", "dirty\n");
        await repo.GitAsync("add", "a.txt");
        await repo.WriteAsync("loose.txt", "u\n");
        var runner = new SpawnCountingGit();
        string[] status = ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"];

        var plain = await runner.RunAsync(repo.Root, status, CancellationToken.None);
        runner.StatusSpawns.ShouldBe(1, "an unmarked dirty status is git's bytes");
        plain.Output.ShouldBe((await Repo.RunGitAsync(repo.Root, status)).Output);

        foreach (var args in StatusForms.Select(f => f.Args))
        {
            var before = runner.StatusSpawns;
            var verdict = await ((ILandingGit)runner).StatusVerdictAsync(repo.Root, args, CancellationToken.None);
            runner.StatusSpawns.ShouldBe(before, string.Join(' ', args) + " is answered in process");
            verdict.Succeeded.ShouldBeTrue();
            var git = await Repo.RunGitAsync(repo.Root, args);
            (verdict.Output.Length != 0).ShouldBe(git.Output.Length != 0, string.Join(' ', args));
        }
        // Only a work-tree edit of a tracked file: libgit2's reading of the bytes is not trusted, git runs.
        await repo.GitAsync("reset", "-q", "a.txt");
        File.Delete(repo.Full("loose.txt"));
        var workTreeOnly = await ((ILandingGit)runner).StatusVerdictAsync(repo.Root, status, CancellationToken.None);
        runner.StatusSpawns.ShouldBe(2, "a work-tree-only modification is git's verdict");
        workTreeOnly.Output.ShouldBe((await Repo.RunGitAsync(repo.Root, status)).Output);
        // Clean again: the verdict is git's empty bytes.
        await repo.GitAsync("checkout", "--", "a.txt");
        (await ((ILandingGit)runner).StatusVerdictAsync(repo.Root, status, CancellationToken.None)).Output.ShouldBe("");
        // A shape libgit2 does not read (an unreadable-or-unusual tree is covered by the scenarios): git runs.
        (await ((ILandingGit)runner).StatusVerdictAsync(repo.Root, ["status", "--porcelain", "--untracked-files=all"], CancellationToken.None))
            .Succeeded.ShouldBeTrue();
        runner.StatusSpawns.ShouldBe(3);
    }

    [Test]
    public async Task A_runner_double_sees_the_verdict_call_unchanged_and_the_mark_ends_with_it()
    {
        var double_ = new RecordingDouble();
        string[] status = ["status", "--porcelain=v1", "-z", "--untracked-files=no"];
        var result = await ((ILandingGit)double_).StatusVerdictAsync("C:/nowhere", status, CancellationToken.None);
        result.Output.ShouldBe(" M faked\0");
        double_.Seen.ShouldHaveSingleItem().ShouldBeSameAs(status);
        double_.MarkedWhileRunning.ShouldBeTrue();
        GitTreeVerdict.IsVerdictOnly(status).ShouldBeFalse("the mark is scoped to the call");
        await double_.RunAsync("C:/nowhere", status, CancellationToken.None);
        double_.MarkedWhileRunning.ShouldBeFalse("an ordinary call with the same list is unmarked");
    }

    private sealed class RecordingDouble : LandingGit
    {
        public List<IReadOnlyList<string>> Seen { get; } = [];
        public bool MarkedWhileRunning { get; private set; }

        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Seen.Add(arguments);
            MarkedWhileRunning = GitTreeVerdict.IsVerdictOnly(arguments);
            return Task.FromResult(new LandingGitResult(0, " M faked\0", ""));
        }
    }

    /// <summary>The real runner with the suite's isolated children, counting the status children it starts.</summary>
    private sealed class SpawnCountingGit : LandingGit
    {
        public int StatusSpawns;
        public int Spawns;

        protected override void ConfigureProcess(System.Diagnostics.ProcessStartInfo start)
        {
            foreach (var (name, value) in Isolation) start.Environment[name] = value;
        }

        protected override System.Diagnostics.Process? StartProcess(System.Diagnostics.ProcessStartInfo start)
        {
            Interlocked.Increment(ref Spawns);
            if (start.ArgumentList.Contains("status")) Interlocked.Increment(ref StatusSpawns);
            return base.StartProcess(start);
        }
    }

    [Test]
    public async Task A_status_is_answered_only_where_the_git_child_reads_the_same_config()
    {
        await using var repo = await Repo.CreateAsync(linked: false);
        string[] status = ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"];
        InProcessGitStatus.TryAnswer(repo.Root, status, ChildEnvironment()).ShouldNotBeNull("isolated child, isolated libgit2: same config");

        // A command-level value libgit2 cannot see: from the environment, or from -c on the command line.
        var countEnv = ChildEnvironment(("GIT_CONFIG_COUNT", "1"), ("GIT_CONFIG_KEY_0", "core.autocrlf"), ("GIT_CONFIG_VALUE_0", "input"));
        InProcessGitStatus.TryAnswer(repo.Root, status, countEnv).ShouldBeNull("GIT_CONFIG_COUNT changes core.autocrlf");
        InProcessGitStatus.TryAnswer(repo.Root, ["-c", "core.autocrlf=input", .. status], ChildEnvironment()).ShouldBeNull("-c changes core.autocrlf");
        // The same value set locally: libgit2 reads it too, so both agree again.
        await repo.GitAsync("config", "core.autocrlf", "input");
        InProcessGitStatus.TryAnswer(repo.Root, status, countEnv).ShouldNotBeNull("local and command values agree");
        // Land inspection's -c core.longpaths=true needs no agreement: libgit2 falls back on any long path.
        InProcessGitStatus.TryAnswer(repo.Root, ["-c", "core.longpaths=true", .. status], countEnv).ShouldNotBeNull();

        // Another HOME whose default ignore file libgit2 would not read.
        var home = Path.Combine(repo.Root, "..", "other-home");
        Directory.CreateDirectory(Path.Combine(home, ".config", "git"));
        await File.WriteAllTextAsync(Path.Combine(home, ".config", "git", "ignore"), "*.log\n");
        var otherHome = ChildEnvironment(("HOME", home), ("XDG_CONFIG_HOME", null), ("GIT_CONFIG_GLOBAL", Path.Combine(home, "empty")));
        InProcessGitStatus.TryAnswer(repo.Root, status, otherHome).ShouldBeNull("the child reads another default ignore file");

        // A child that reads the machine's own config sees what isolated libgit2 does not (on Windows, Git for
        // Windows' system file sets core.autocrlf): it must disagree whenever git lists such a key.
        var machine = await Repo.RunGitAsync(Path.GetTempPath(), isolated: false, "config", "--get-regexp", @"^core\.(autocrlf|eol|excludesfile|ignorecase|filemode|symlinks)$");
        if (machine.ExitCode == 0 && machine.Output.Length != 0)
        {
            await Repo.RunGitAsync(repo.Root, "config", "--unset", "core.autocrlf");
            using var opened = new LibGit2Sharp.Repository(repo.Root);
            GitStatusConfigAgreement.Disagreement(opened, MachineEnvironment(), []).ShouldNotBeNull(machine.Output);
        }

        // Parameters git reads that this check does not model are git's.
        InProcessGitStatus.TryAnswer(repo.Root, status, ChildEnvironment(("GIT_CONFIG_PARAMETERS", "'core.autocrlf'='true'"))).ShouldBeNull();
    }

    private static async Task InitNestedAsync(Repo r, string relative)
    {
        var path = r.Full(relative);
        Directory.CreateDirectory(path);
        await Repo.RunGitAsync(path, "init", "-q", "-b", "master");
        await File.WriteAllTextAsync(Path.Combine(path, "n.txt"), "n\n");
        await Repo.RunGitAsync(path, "add", "n.txt");
        await Repo.RunGitAsync(path, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "n");
    }

    internal sealed class Repo : IAsyncDisposable
    {
        private readonly string _home;
        public string Root { get; }
        public string GitDir { get; }

        private Repo(string home, string root, string gitDir) { _home = home; Root = root; GitDir = gitDir; }

        public static async Task<Repo> CreateAsync(bool linked)
        {
            var home = Path.Combine(Path.GetTempPath(), "antiphon-verdict-" + Guid.NewGuid().ToString("N")[..12]);
            var main = Path.Combine(home, "main");
            Directory.CreateDirectory(main);
            await RunGitAsync(main, "init", "-q", "-b", "master");
            await RunGitAsync(main, "config", "user.name", "Verdict Test");
            await RunGitAsync(main, "config", "user.email", "verdict@test");
            await File.WriteAllTextAsync(Path.Combine(main, "a.txt"), "alpha\n");
            await File.WriteAllTextAsync(Path.Combine(main, "b.txt"), "beta\n");
            await RunGitAsync(main, "add", "-A");
            await RunGitAsync(main, "commit", "-q", "-m", "seed");
            if (!linked) return new Repo(home, main, Path.Combine(main, ".git"));
            var tree = Path.Combine(home, "linked");
            await RunGitAsync(main, "worktree", "add", "-q", "-b", "linked", tree);
            return new Repo(home, tree, Path.Combine(main, ".git", "worktrees", "linked"));
        }

        public string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

        public async Task WriteAsync(string relative, string content)
        {
            var full = Full(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content, new UTF8Encoding(false));
        }

        public async Task GitAsync(params string[] args)
        {
            var result = await RunGitAsync(Root, args);
            result.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {result.Error}");
        }

        public async Task CommitAllAsync()
        {
            await GitAsync("add", "-A");
            await GitAsync("commit", "-q", "-m", "step");
        }

        public static Task<(int ExitCode, string Output, string Error)> RunGitAsync(string cwd, params string[] args)
            => RunGitAsync(cwd, isolated: true, args);

        public static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(string cwd, bool isolated, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            };
            if (isolated) foreach (var (name, value) in Isolation) start.Environment[name] = value;
            foreach (var a in args) start.ArgumentList.Add(a);
            using var p = Process.Start(start)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, await output, await error);
        }

        private readonly List<Func<Task>> _cleanups = [];
        public void OnDispose(Func<Task> cleanup) => _cleanups.Add(cleanup);

        public static async Task RunToolAsync(string tool, params string[] args)
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var p = Process.Start(start)!;
            await p.StandardOutput.ReadToEndAsync();
            await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            p.ExitCode.ShouldBe(0, tool + " " + string.Join(' ', args));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var cleanup in _cleanups) await cleanup();
            if (!OperatingSystem.IsWindows() && Directory.Exists(Full("locked")))
                File.SetUnixFileMode(Full("locked"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                foreach (var f in Directory.EnumerateFiles(_home, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_home, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
