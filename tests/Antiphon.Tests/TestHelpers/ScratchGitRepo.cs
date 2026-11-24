using System.Diagnostics;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A real git repo plus an isolated worktree root, both deleted on dispose. Worktree/merge-back
/// behaviour is tested against REAL git because that is where its bugs live — "refusing to fetch
/// into checked-out branch" is not something a fake would ever say.
/// </summary>
public sealed class ScratchGitRepo : IDisposable
{
    public string Path { get; }
    public string WorktreeRoot { get; }
    private string? _bareOrigin;
    // The folder a seeded template copy lives in (repo and origin under it); deleted on dispose.
    private readonly string? _cloneRoot;

    /// <summary>The bare origin added by <see cref="AddBareOriginAsync"/> or <see cref="CreateSeededWithOriginAsync"/>,
    /// spelled as <c>git remote get-url origin</c> prints it; null before one is added.</summary>
    public string? BareOrigin => _bareOrigin;

    public ScratchGitRepo(string prefix = "antiphon-deleg-wt")
    {
        using var span = TestTrace.Span("git-repo create");
        Path = Directory.CreateTempSubdirectory($"{prefix}-repo").FullName;
        WorktreeRoot = Directory.CreateTempSubdirectory($"{prefix}-trees").FullName;
        if (TryCloneTemplate(Path)) return;
        InitAsync(Path).GetAwaiter().GetResult();
    }

    private ScratchGitRepo(string path, string worktreeRoot, string bareOrigin, string cloneRoot)
    {
        Path = path;
        WorktreeRoot = worktreeRoot;
        _bareOrigin = bareOrigin;
        _cloneRoot = cloneRoot;
    }

    /// <summary>
    /// The same repository as <c>new ScratchGitRepo(prefix)</c> followed by
    /// <see cref="CommitFileAsync"/>(<paramref name="relativePath"/>, <paramref name="content"/>) and
    /// <see cref="AddBareOriginAsync"/>: one commit on master, pushed with <c>-u</c> to a bare origin with
    /// <c>receive.autogc=false</c>. It is copied from a <see cref="GitTreeTemplate"/> built once per process (per file
    /// and content) by those same steps, which saves their seven git processes per repository. The copy has the
    /// template's commit id (same author, committer, times and message), and its origin remote names the copy's own
    /// bare origin (the template rewrites its root path). The repository is <c>&lt;temp&gt;/&lt;prefix&gt;-seeded-*/repo</c>
    /// with the origin beside it at <c>origin</c>. <c>ANTIPHON_TEST_GIT_TEMPLATE=off</c>, or a template that cannot be
    /// built or cloned, runs the git steps for this repository instead.
    /// </summary>
    public static async Task<ScratchGitRepo> CreateSeededWithOriginAsync(string prefix, string relativePath, string content)
    {
        if (GitTreeTemplate.Enabled)
        {
            string? cloneRoot = null;
            try
            {
                var template = await GitTreeTemplate.BuildOnceAsync($"scratch-git-repo-seeded-origin|{relativePath}|{content}", [],
                    async (root, _) =>
                    {
                        var repo = System.IO.Path.Combine(root, "repo");
                        var origin = System.IO.Path.Combine(root, "origin");
                        Directory.CreateDirectory(repo);
                        Directory.CreateDirectory(origin);
                        await InitAsync(repo);
                        await CommitFileInAsync(repo, relativePath, content);
                        await AddBareOriginInAsync(repo, origin);
                        return [];
                    });
                using var span = TestTrace.Span("git-repo create seeded");
                cloneRoot = Directory.CreateTempSubdirectory($"{prefix}-seeded-").FullName;
                template.CloneTo(cloneRoot);
                var worktreeRoot = Directory.CreateTempSubdirectory($"{prefix}-trees").FullName;
                return new ScratchGitRepo(System.IO.Path.Combine(cloneRoot, "repo"), worktreeRoot,
                    System.IO.Path.Combine(cloneRoot, "origin"), cloneRoot);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException
                or UnauthorizedAccessException or ShouldAssertException)
            {
                Console.Error.WriteLine($"git-template disabled for this seeded scratch repo: {ex.GetType().Name}: {ex.Message}");
                if (cloneRoot is not null) GitTreeTemplate.DeleteTree(cloneRoot);
            }
        }
        var git = new ScratchGitRepo(prefix);
        try
        {
            await git.CommitFileAsync(relativePath, content);
            await git.AddBareOriginAsync();
            return git;
        }
        catch
        {
            git.Dispose();
            throw;
        }
    }

    /// <summary>The starting repository: <c>git init -b master</c> plus the test identity.</summary>
    private static async Task InitAsync(string path)
    {
        foreach (var args in new[]
                 {
                     new[] { "init", "-b", "master" },
                     ["config", "user.email", "test@antiphon.local"],
                     ["config", "user.name", "Delegation Tests"],
                 })
            (await GitInAsync(path, args)).Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");
    }

    /// <summary>
    /// Copies the starting repository from a template built once per process by <see cref="InitAsync"/>
    /// (<see cref="GitTreeTemplate"/>), which saves its three git processes. The empty repository holds no
    /// path or token, so the clone is a plain copy (less the sample hooks, which git never runs).
    /// <c>ANTIPHON_TEST_GIT_TEMPLATE=off</c>, or a template that cannot be built, runs git every time.
    /// </summary>
    private static bool TryCloneTemplate(string path)
    {
        if (!GitTreeTemplate.Enabled) return false;
        GitTreeTemplate template;
        try
        {
            template = GitTreeTemplate.BuildOnceAsync("scratch-git-repo", [], async (root, _) =>
            {
                await InitAsync(root);
                return [];
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git-template disabled for this scratch repo: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        template.CloneTo(path);
        return true;
    }

    public Task CommitFileAsync(string relativePath, string content) => CommitFileInAsync(Path, relativePath, content);

    private static async Task CommitFileInAsync(string repo, string relativePath, string content)
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, relativePath), content);
        foreach (var args in new[] { new[] { "add", "." }, ["commit", "-m", $"edit {relativePath}"] })
            (await GitInAsync(repo, args)).Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");
    }

    public async Task GitAsync(params string[] args) =>
        (await GitInAsync(Path, args)).Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");

    public async Task<string> GitReadAsync(params string[] args)
    {
        var result = await GitInAsync(Path, args);
        result.Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");
        return result.StdOut;
    }

    public sealed record GitResult(bool Ok, string StdOut, string StdErr = "");

    public static async Task<GitResult> GitInAsync(string dir, params string[] args) =>
        await GitInAsync(dir, env: null, args);

    public static async Task<GitResult> GitInAsync(string dir, IReadOnlyDictionary<string, string>? env, params string[] args)
    {
        // The verb may follow -c/-C pairs.
        var verb = 0;
        while (verb + 1 < args.Length && args[verb] is "-c" or "-C") verb += 2;
        // The git verb only: arguments can carry paths and messages.
        using var span = TestTrace.Span("git " + (verb < args.Length ? args[verb] : ""), "antiphon.phase", "process");
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // A fixture's commit, merge or fetch is setup, not the code under test: no auto-maintenance child after it (a
        // git process of its own, about 0.18 s on the HP laptop; 165 a run of AgentTaskReplyIntegrationTests). It is
        // set on this command line, after which git hands it to the hooks and child gits this command starts
        // (GIT_CONFIG_PARAMETERS); no fixture hook reads it, and no other command or test sees it. A push's gc runs in
        // the receiving repository, which inherits none of this: AddBareOriginAsync turns it off there.
        if (verb < args.Length && args[verb] is "commit" or "merge" or "cherry-pick" or "revert" or "am" or "rebase" or "fetch" or "pull")
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("maintenance.auto=false");
        }
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null)
        {
            foreach (var (key, value) in env)
                psi.Environment[key] = value;
        }
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        // A stuck git (credential prompt, lock) must not hang the fixture. 60s covers init,
        // commit and a local push; a timeout is a failed command, not a silent success.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
            return new GitResult(false, "", "git timed out after 60s");
        }
        return new GitResult(p.ExitCode == 0, await stdout, await stderr);
    }

    public async Task AddBareOriginAsync()
    {
        _bareOrigin = Directory.CreateTempSubdirectory("c527-origin").FullName;
        await AddBareOriginInAsync(Path, _bareOrigin);
    }

    private static async Task AddBareOriginInAsync(string repo, string origin)
    {
        (await GitInAsync(origin, "init", "--bare")).Ok.ShouldBeTrue();
        // A push into the fixture's origin starts no gc there (receive-pack's own, as LandingGitFixture's remote).
        (await GitInAsync(origin, "config", "receive.autogc", "false")).Ok.ShouldBeTrue();
        foreach (var args in new[] { new[] { "remote", "add", "origin", origin }, ["push", "-u", "origin", "master"] })
            (await GitInAsync(repo, args)).Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");
    }

    public async Task InstallFailingPreCommitHookAsync(string message)
    {
        var hooks = System.IO.Path.Combine(Path, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        var hook = System.IO.Path.Combine(hooks, "pre-commit");
        var escaped = message.Replace("\"", "\\\"");
        var script = "#!/bin/sh\necho \"" + escaped + "\" >&2\nexit 1\n";
        await WriteExecutableHookAsync(hook, script.Replace("\r\n", "\n"));
    }

    public static async Task WriteExecutableHookAsync(string hook, string script)
    {
        await File.WriteAllTextAsync(hook, script);
        // Linux git ignores a hook that is not executable. Windows git runs it without that bit,
        // and SetUnixFileMode is unsupported there.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hook,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    public void Dispose()
    {
        using var span = TestTrace.Span("git-repo delete");
        // A seeded copy's repo and origin are both under its clone root.
        foreach (var dir in _cloneRoot is null ? new[] { WorktreeRoot, Path, _bareOrigin } : [WorktreeRoot, _cloneRoot])
        {
            if (dir is null) continue;
            try
            {
                // git object files are read-only on Windows; strip attributes or Delete throws.
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
