using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1704 F-1: real scratch repositories built once per class, and a direct git oracle (no
/// <see cref="LandingGit"/>). Every git child here and in <see cref="Landing"/> sees the same isolated config.
/// </summary>
internal sealed class C1704Corpus : IAsyncDisposable
{
    public string Root { get; }
    public string Home => Path.Combine(Root, "home");
    public string Path_(string name) => Path.Combine(Root, name);

    private C1704Corpus(string root) => Root = root;

    public static async Task<C1704Corpus> CreateAsync()
    {
        var corpus = new C1704Corpus(Path.Combine(Path.GetTempPath(), "antiphon-c1704-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(corpus.Home);
        await File.WriteAllTextAsync(Path.Combine(corpus.Home, "empty-config"), "");
        await corpus.BuildAsync();
        return corpus;
    }

    private async Task BuildAsync()
    {
        // empty: init only, unborn master.
        Required(Root, "init", "-b", "master", "empty");

        // plain: 3 commits, annotated v1, lightweight l1, feat/x, an unrelated orphan commit, config keys.
        var plain = Path_("plain");
        Required(Root, "init", "-b", "master", "plain");
        for (var i = 1; i <= 3; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(plain, "a.txt"), $"line {i}\n");
            Required(plain, "add", "a.txt");
            Required(plain, "commit", "-m", $"c{i}");
        }
        Required(plain, "tag", "-a", "v1", "-m", "v1", "HEAD~1");
        Required(plain, "tag", "l1", "HEAD~2");
        Required(plain, "branch", "feat/x", "HEAD~1");
        var tree = Required(plain, "rev-parse", "HEAD^{tree}").Trim();
        var orphan = Required(plain, "commit-tree", tree, "-m", "orphan").Trim();
        Required(plain, "update-ref", "refs/heads/orphan", orphan);
        Required(plain, "config", "c1704.key", "value one");
        Required(plain, "config", "--add", "c1704.multi", "a");
        Required(plain, "config", "--add", "c1704.multi", "b");

        // detached: HEAD at a commit.
        Required(Root, "clone", "--quiet", "--shared", plain, "detached");
        Required(Path_("detached"), "checkout", "--quiet", "--detach", "HEAD~1");

        // packed: refs packed, then feat/x rewritten loose with another oid.
        Required(Root, "clone", "--quiet", "--shared", plain, "packed");
        var packed = Path_("packed");
        Required(packed, "branch", "feat/x", "origin/feat/x");
        Required(packed, "branch", "packed-only", "HEAD~2");
        Required(packed, "pack-refs", "--all");
        Required(packed, "update-ref", "refs/heads/feat/x", Required(packed, "rev-parse", "HEAD").Trim());

        // refs5k: 5,000 refs under refs/heads/bulk/ in one update-ref --stdin.
        var bulk = Path_("refs5k");
        Required(Root, "clone", "--quiet", "--shared", plain, "refs5k");
        var head = Required(bulk, "rev-parse", "HEAD").Trim();
        var input = new StringBuilder();
        for (var i = 0; i < 5000; i++) input.Append($"create refs/heads/bulk/r{i:D4} {head}\n");
        Git(bulk, Encoding.UTF8.GetBytes(input.ToString()), "update-ref", "--stdin").Exit.ShouldBeZero("refs5k");

        // worktrees: live, locked, prunable (directory deleted), relative-commondir pointer.
        var wt = Path_("worktrees");
        Required(Root, "clone", "--quiet", "--shared", plain, "worktrees");
        Required(wt, "worktree", "add", "--quiet", "-b", "live", Path_("wt-live"), "HEAD");
        Required(wt, "worktree", "add", "--quiet", "-b", "locked", Path_("wt-locked"), "HEAD");
        Required(wt, "worktree", "lock", Path_("wt-locked"));
        Required(wt, "worktree", "add", "--quiet", "-b", "prunable", Path_("wt-prunable"), "HEAD");
        Directory.Delete(Path_("wt-prunable"), true);
        Required(wt, "worktree", "add", "--quiet", "-b", "relative", Path_("wt-relative"), "HEAD");
        var relativeAdmin = Required(Path_("wt-relative"), "rev-parse", "--absolute-git-dir").Trim();
        await File.WriteAllTextAsync(Path.Combine(Path_("wt-relative"), ".git"),
            "gitdir: " + Path.GetRelativePath(Path_("wt-relative"), relativeAdmin).Replace('\\', '/') + "\n");
        File.WriteAllText(Path.Combine(wt, "dirty.txt"), "untracked\n");
        File.WriteAllText(Path.Combine(Path_("wt-locked"), "a.txt"), "changed in locked\n");

        // shallow: depth 1 of plain.
        Required(Root, "clone", "--quiet", "--depth", "1", "file://" + plain.Replace('\\', '/'), "shallow");

        // odd-names: unusual branch names and -z-only file names, over two commits.
        var odd = Path_("odd-names");
        Required(Root, "init", "-b", "master", "odd-names");
        await File.WriteAllTextAsync(Path.Combine(odd, "a.txt"), "a\n");
        Required(odd, "add", "a.txt");
        Required(odd, "commit", "-m", "first");
        var blob = Git(odd, Encoding.UTF8.GetBytes("odd\n"), "hash-object", "-w", "--stdin").Text.Trim();
        Git(odd, Encoding.UTF8.GetBytes($"100644 {blob} 0\ttrailing\n\0100644 {blob} 0\ttab\tname\0"),
            "update-index", "-z", "--index-info").Exit.ShouldBeZero("odd index");
        Required(odd, "commit", "-m", "odd");
        Required(odd, "branch", "ü/ñ");
        Required(odd, "branch", "a-b_c.d");
        Required(odd, "branch", "x/@");

        // ref-lock: a feat/x.lock beside feat/x.
        Required(Root, "clone", "--quiet", "--shared", plain, "ref-lock");
        Required(Path_("ref-lock"), "branch", "feat/x", "origin/feat/x");
        await File.WriteAllTextAsync(Path.Combine(Path_("ref-lock"), ".git", "refs", "heads", "feat", "x.lock"), "");

        // corrupt: a truncated loose object and a broken loose ref.
        var corrupt = Path_("corrupt");
        Required(Root, "init", "-b", "master", "corrupt");
        await File.WriteAllTextAsync(Path.Combine(corrupt, "b.txt"), "b\n");
        Required(corrupt, "add", "b.txt");
        Required(corrupt, "commit", "-m", "b");
        var blobSha = Required(corrupt, "rev-parse", "HEAD:b.txt").Trim();
        var objectPath = Path.Combine(corrupt, ".git", "objects", blobSha[..2], blobSha[2..]);
        File.SetAttributes(objectPath, FileAttributes.Normal);
        await File.WriteAllBytesAsync(objectPath, [0x78]);
        await File.WriteAllTextAsync(Path.Combine(corrupt, ".git", "refs", "heads", "broken"), "not-an-object-id\n");

        // ext: format v1 with an unknown extension (git refuses the repository).
        var ext = Path_("ext");
        Required(Root, "init", "-b", "master", "ext");
        await File.WriteAllTextAsync(Path.Combine(ext, "c.txt"), "c\n");
        Required(ext, "add", "c.txt");
        Required(ext, "commit", "-m", "c");
        Required(ext, "config", "core.repositoryformatversion", "1");
        Required(ext, "config", "extensions.unknownfoo", "true");

        // partial: a blob:none clone of a file-protocol bare remote; older blobs are absent.
        Required(Root, "clone", "--quiet", "--bare", plain, "partial-remote.git");
        Required(Path_("partial-remote.git"), "config", "uploadpack.allowfilter", "true");
        Required(Root, "clone", "--quiet", "--filter=blob:none", "file://" + Path_("partial-remote.git").Replace('\\', '/'), "partial");
    }

    /// <summary>A shared clone of a template, for a test that writes.</summary>
    public string CloneShared(string template)
    {
        var name = template + "-" + Guid.NewGuid().ToString("N")[..8];
        Required(Root, "clone", "--quiet", "--shared", Path_(template), name);
        return Path_(name);
    }

    public string Required(string directory, params string[] arguments)
    {
        var result = Git(directory, null, arguments);
        if (result.Exit != 0) throw new InvalidOperationException($"corpus_git_failed:{string.Join(' ', arguments)}:{result.Exit}:{result.Error}");
        return result.Text;
    }

    public (int Exit, byte[] Stdout, string Text, string Error) Git(string directory, params string[] arguments) =>
        Git(directory, null, arguments);

    /// <summary>The oracle: git itself, run directly with the corpus environment.</summary>
    public (int Exit, byte[] Stdout, string Text, string Error) Git(string directory, byte[]? input, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = input is not null, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        Configure(start);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("git_start_failed");
        var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var error = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            process.StandardInput.BaseStream.Write(input);
            process.StandardInput.Close();
        }
        copy.Wait();
        process.WaitForExit();
        var bytes = stdout.ToArray();
        return (process.ExitCode, bytes, Encoding.UTF8.GetString(bytes), error.Result);
    }

    public void Configure(ProcessStartInfo start)
    {
        start.Environment["HOME"] = Home;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Home, "empty-config");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_AUTHOR_NAME"] = "C1704";
        start.Environment["GIT_AUTHOR_EMAIL"] = "c1704@example.invalid";
        start.Environment["GIT_COMMITTER_NAME"] = "C1704";
        start.Environment["GIT_COMMITTER_EMAIL"] = "c1704@example.invalid";
        start.Environment["GIT_CONFIG_COUNT"] = "2";
        start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
        start.Environment["GIT_CONFIG_VALUE_0"] = "false";
        start.Environment["GIT_CONFIG_KEY_1"] = "protocol.file.allow";
        start.Environment["GIT_CONFIG_VALUE_1"] = "always";
    }

    /// <summary>A real <see cref="LandingGit"/> whose children see the corpus environment.</summary>
    public CorpusGit Landing(IGitOperationObserver? observer = null, LandGitBackend? backend = null) => new(this, observer, backend);

    internal class CorpusGit(C1704Corpus corpus, IGitOperationObserver? observer, LandGitBackend? backend)
        : LandingGit(observer: observer, backend: backend)
    {
        private int _started;
        public int Started => Volatile.Read(ref _started);
        public Func<ProcessStartInfo, ProcessStartInfo>? SwapStart { get; set; }

        protected override void ConfigureProcess(ProcessStartInfo start) => corpus.Configure(start);

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            Interlocked.Increment(ref _started);
            return base.StartProcess(SwapStart is null ? start : SwapStart(start));
        }

        public Task<(int ExitCode, byte[] Output)> StdinAsync(string path, IReadOnlyList<string> arguments, byte[] input, CancellationToken ct)
            => RunGitInputBytesAsync(path, arguments, input, ct);
    }

    public ValueTask DisposeAsync()
    {
        var full = Path.GetFullPath(Root);
        if (!Path.GetFileName(full).StartsWith("antiphon-c1704-", StringComparison.Ordinal))
            throw new InvalidOperationException("corpus disposal escaped its root");
        try
        {
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return ValueTask.CompletedTask;
    }
}

internal static class C1704CorpusAssertions
{
    public static void ShouldBeZero(this int exit, string what)
    {
        if (exit != 0) throw new InvalidOperationException($"corpus_step_failed:{what}:{exit}");
    }
}

/// <summary>CARD-1704 F-3: thread-safe recording observer; <see cref="Throw"/> makes every call throw.</summary>
internal sealed class RecordingObserver : IGitOperationObserver
{
    private readonly List<GitOperation> _records = [];
    public bool Throw { get; set; }
    public IReadOnlyList<GitOperation> Records { get { lock (_records) return _records.ToList(); } }
    public void Completed(GitOperation operation)
    {
        lock (_records) _records.Add(operation);
        if (Throw) throw new InvalidOperationException("observer_failed");
    }
}

/// <summary>Forwards to a settable target, so a fixture git can be built before its observer is chosen.</summary>
internal sealed class GitObserverRelay : IGitOperationObserver
{
    public IGitOperationObserver? Target { get; set; }
    public void Completed(GitOperation operation) => Target?.Completed(operation);
}
