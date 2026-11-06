using System.Diagnostics;
using System.Text;
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
/// stay with git are pinned as fallbacks. Real temp repositories, git on PATH, inherited git config.
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

        mismatches.ShouldBeEmpty($"{name}:\n  " + string.Join("\n  ", mismatches));
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

        public static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(string cwd, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            };
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
