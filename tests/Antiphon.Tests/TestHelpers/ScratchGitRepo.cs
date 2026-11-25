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
    /// <c>receive.autogc=false</c>. The seed commit has fixed author and committer dates (<see cref="SeedDate"/>), so
    /// its id depends only on the file and content. The repository is copied from a <see cref="GitTreeTemplate"/>
    /// built once per process (per file and content) by those same steps, which saves their seven git processes per
    /// repository; the copy has the template's commit id, and its origin remote names the copy's own bare origin (the
    /// template rewrites its root path; the copy's <c>remote.origin.url</c> is checked against
    /// <see cref="BareOrigin"/>). A template copy is <c>&lt;temp&gt;/&lt;prefix&gt;-seeded-*/repo</c> with the origin
    /// beside it at <c>origin</c>. <c>ANTIPHON_TEST_GIT_TEMPLATE=off</c>, or an expected failure to build, copy or check
    /// the template (I/O, access, git or process start, the manifest, the copy check; a build or check failure is
    /// remembered for the rest of the process), runs the git steps instead, which make
    /// <c>&lt;temp&gt;/&lt;prefix&gt;-repo*</c> and a separate origin. Any other exception is a bug and is thrown.
    /// </summary>
    public static Task<ScratchGitRepo> CreateSeededWithOriginAsync(string prefix, string relativePath, string content) =>
        CreateSeededWithOriginAsync(prefix, relativePath, content, GitTreeTemplate.Enabled);

    /// <summary>The seed commit's author and committer date, the same with or without a template.</summary>
    internal const string SeedDate = "2026-01-01T00:00:00+00:00";

    private static readonly Dictionary<string, string> SeedDateEnv = new(StringComparer.Ordinal)
    {
        ["GIT_AUTHOR_DATE"] = SeedDate,
        ["GIT_COMMITTER_DATE"] = SeedDate,
    };

    // Seeded-template keys whose build or copy check failed: later repositories go straight to the git steps.
    // The failures the template can meet as a matter of course; anything else is a bug and must not hide behind the
    // slower git steps.
    private static bool ExpectedTemplateFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException
        or System.ComponentModel.Win32Exception or LibGit2Sharp.LibGit2SharpException or Shouldly.ShouldAssertException;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> SeededTemplateFailed =
        new(StringComparer.Ordinal);

    /// <summary><see cref="CreateSeededWithOriginAsync(string,string,string)"/> with the template switch explicit
    /// (a test compares both paths in one process).</summary>
    internal static async Task<ScratchGitRepo> CreateSeededWithOriginAsync(string prefix, string relativePath, string content,
        bool useTemplate)
    {
        // A hash of both parts joined by NUL, so no two (file, content) pairs share a key.
        var key = "scratch-git-repo-seeded-origin|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(relativePath + "\0" + content)));
        if (useTemplate && !SeededTemplateFailed.ContainsKey(key))
        {
            GitTreeTemplate? template = null;
            string? builtRoot = null;
            try
            {
                template = await GitTreeTemplate.BuildOnceAsync(key, [], async (root, _) =>
                {
                    builtRoot = root;
                    var repo = System.IO.Path.Combine(root, "repo");
                    var origin = System.IO.Path.Combine(root, "origin");
                    Directory.CreateDirectory(repo);
                    Directory.CreateDirectory(origin);
                    await InitAsync(repo);
                    await CommitFileInAsync(repo, relativePath, content, SeedDateEnv);
                    await AddBareOriginInAsync(repo, origin);
                    return [];
                });
            }
            catch (Exception ex) when (ExpectedTemplateFailure(ex))
            {
                SeededTemplateFailed[key] = true;
                if (builtRoot is not null) GitTreeTemplate.DeleteTree(builtRoot);
                Console.Error.WriteLine($"git-template disabled for seeded scratch repos: {ex.GetType().Name}: {ex.Message}");
            }
            if (template is not null)
            {
                string? cloneRoot = null;
                try
                {
                    using var span = TestTrace.Span("git-repo create seeded");
                    cloneRoot = Directory.CreateTempSubdirectory($"{prefix}-seeded-").FullName;
                    template.CloneTo(cloneRoot);
                    var repo = System.IO.Path.Combine(cloneRoot, "repo");
                    var origin = System.IO.Path.Combine(cloneRoot, "origin");
                    // The origin URL must be the copy's own origin as git stores it, or the copy would push into the
                    // template (or nowhere) while BareOrigin names another place.
                    var url = ReadOriginUrl(repo);
                    if (!string.Equals(url, origin, StringComparison.Ordinal))
                    {
                        SeededTemplateFailed[key] = true;
                        throw new InvalidOperationException(
                            $"the copied remote.origin.url is '{url}', not the copy's own origin '{origin}'");
                    }
                    var worktreeRoot = Directory.CreateTempSubdirectory($"{prefix}-trees").FullName;
                    return new ScratchGitRepo(repo, worktreeRoot, origin, cloneRoot);
                }
                catch (Exception ex) when (ExpectedTemplateFailure(ex))
                {
                    Console.Error.WriteLine(SeededTemplateFailed.ContainsKey(key)
                        ? $"git-template disabled for seeded scratch repos for the rest of this process: {ex.GetType().Name}: {ex.Message}"
                        : $"git-template disabled for this seeded scratch repo: {ex.GetType().Name}: {ex.Message}");
                    if (cloneRoot is not null) GitTreeTemplate.DeleteTree(cloneRoot);
                }
            }
        }
        var git = new ScratchGitRepo(prefix);
        try
        {
            await CommitFileInAsync(git.Path, relativePath, content, SeedDateEnv);
            await git.AddBareOriginAsync();
            return git;
        }
        catch
        {
            git.Dispose();
            throw;
        }
    }

    /// <summary>
    /// <c>remote.origin.url</c> as git stores it in <paramref name="repo"/>'s <c>.git/config</c>, read from the file
    /// (no git process): the value in the <c>[remote "origin"]</c> section with git's quotes and escapes undone;
    /// null when there is none.
    /// </summary>
    private static string? ReadOriginUrl(string repo)
    {
        var inOrigin = false;
        foreach (var raw in File.ReadLines(System.IO.Path.Combine(repo, ".git", "config")))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inOrigin = line == "[remote \"origin\"]";
                continue;
            }
            if (!inOrigin) continue;
            var eq = line.IndexOf('=');
            if (eq < 0 || !string.Equals(line[..eq].Trim(), "url", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(eq + 1)..].Trim();
            var text = new System.Text.StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '"') continue;
                if (c == '\\' && i + 1 < value.Length)
                {
                    var next = value[++i];
                    text.Append(next switch { 'n' => '\n', 't' => '\t', 'b' => '\b', _ => next });
                    continue;
                }
                text.Append(c);
            }
            return text.ToString();
        }
        return null;
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

    private static async Task CommitFileInAsync(string repo, string relativePath, string content,
        IReadOnlyDictionary<string, string>? env = null)
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, relativePath), content);
        foreach (var args in new[] { new[] { "add", "." }, ["commit", "-m", $"edit {relativePath}"] })
            (await GitInAsync(repo, env, args)).Ok.ShouldBeTrue($"git {string.Join(' ', args)} must succeed");
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
        // A seeded copy's repo and origin go one by one, then their clone root (an origin added later by
        // AddBareOriginAsync is outside that root and goes too).
        foreach (var dir in new[] { WorktreeRoot, Path, _bareOrigin, _cloneRoot })
        {
            if (dir is null || !Directory.Exists(dir)) continue;
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
