using System.Collections.Concurrent;
using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Reuses git's own answer to <c>remote get-url --push --all origin</c> while every config file that
/// could change it is byte-for-byte unchanged. The land re-reads the push URL before every remote
/// step (24 times in one land), always from the same config.
/// <para>
/// The answer is never computed here; only git's last answer is replayed. The files stamped are the
/// ones git itself names in <c>git config --list --show-origin --show-scope</c>, plus the global and
/// system locations a git child would read even when they are empty or absent today (a newly created
/// ~/.gitconfig with an insteadOf rewrite is a change). Each stamp is the file's full content hash,
/// so a rewrite inside the same timestamp tick is still a change.
/// </para>
/// <para>
/// Config injected for the instance's git children (GIT_CONFIG_COUNT/KEY_n, -c, GIT_CONFIG_PARAMETERS)
/// is fixed for the instance; it rules caching out only when a key is remote.*, url.* or include*.
/// Nothing is cached when config comes from another non-file origin (a blob), when any file uses
/// include/includeIf (whose targets and conditions, such as onbranch, are git's to evaluate), or when
/// the system file cannot be located. A fill hashes the
/// files before and after git answers and keeps the answer only if nothing changed in between.
/// </para>
/// </summary>
internal sealed class GitPushUrlCache
{
    internal static readonly string[] Command = ["remote", "get-url", "--push", "--all", "origin"];
    internal static readonly string[] Listing = ["config", "--list", "--show-origin", "--show-scope"];

    private sealed record Entry(LandingGitResult Result, (string Path, string Hash)[] Files);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static bool IsCommand(IReadOnlyList<string> arguments) => arguments.SequenceEqual(Command);

    /// <summary>git's last answer when every stamped file still hashes the same; otherwise null.</summary>
    public LandingGitResult? TryGet(string repository)
    {
        if (!_entries.TryGetValue(repository, out var entry)) return null;
        foreach (var (path, hash) in entry.Files)
            if (Hash(path) != hash)
            {
                _entries.TryRemove(repository, out _);
                return null;
            }
        return entry.Result;
    }

    /// <summary>
    /// The files that decide the answer, from git's own listing plus the child's global/system
    /// locations; null when the answer must not be cached.
    /// </summary>
    public static string[]? Files(string repository, string listing, IReadOnlyDictionary<string, string?> childEnvironment)
    {
        string? Env(string name) => childEnvironment.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v : null;
        static bool CanChangeUrl(string key) =>
            key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("include", StringComparison.OrdinalIgnoreCase);

        var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? systemFile = null;
        foreach (var raw in listing.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var tab1 = line.IndexOf('\t');
            if (tab1 <= 0) continue; // a continuation of a multi-line value
            var scope = line[..tab1];
            if (scope is not ("system" or "global" or "local" or "worktree" or "command")) continue;
            var tab2 = line.IndexOf('\t', tab1 + 1);
            if (tab2 < 0) return null;
            var origin = line[(tab1 + 1)..tab2];
            if (scope == "command")
            {
                // Injected for this instance's git children (GIT_CONFIG_COUNT/KEY_n, -c, GIT_CONFIG_PARAMETERS):
                // fixed for the instance, so only a key that can change the URL rules caching out.
                var entry = line[(tab2 + 1)..];
                var key = entry.Split('=', 2)[0];
                if (CanChangeUrl(key)) return null;
                continue;
            }
            if (!origin.StartsWith("file:", StringComparison.Ordinal)) return null;
            var path = origin["file:".Length..];
            if (path.StartsWith('"')) return null; // quoted (unusual characters): git's to read
            var full = Path.GetFullPath(path, repository);
            files.Add(full);
            if (scope == "system") systemFile = full;
        }

        // The repository itself: its .git entry (directory, or the gitfile's pointer) and, through it,
        // the git dir and common dir. A removed, re-pointed or re-created checkout is a new layout.
        var dotGit = Path.Combine(repository, ".git");
        files.Add(dotGit);
        if (GitDirectories(dotGit) is not { } dirs) return null;
        var (gitDir, common) = dirs;
        files.Add(Path.Combine(gitDir, "commondir"));
        files.Add(Path.Combine(common, "config"));
        // Read even when empty or absent today: worktree config (extensions.worktreeConfig, which
        // git sparse-checkout turns on) and the legacy remote files git still honours.
        files.Add(Path.Combine(gitDir, "config.worktree"));
        files.Add(Path.Combine(common, "config.worktree"));
        files.Add(Path.Combine(common, "remotes", "origin"));
        files.Add(Path.Combine(common, "branches", "origin"));

        // Global and system locations the git child reads even when they hold nothing today.
        if (Env("GIT_CONFIG_GLOBAL") is { } global) files.Add(Path.GetFullPath(global, repository));
        else
        {
            // Git for Windows: HOME, else HOMEDRIVE+HOMEPATH, else USERPROFILE. Every candidate is
            // stamped; an extra file only costs a miss.
            string?[] homes = [Env("HOME"),
                Env("HOMEDRIVE") is { } drive && Env("HOMEPATH") is { } homePath ? drive + homePath : null,
                Env("USERPROFILE")];
            var any = false;
            foreach (var home in homes.OfType<string>().Distinct())
            {
                any = true;
                files.Add(Path.Combine(home, ".gitconfig"));
                files.Add(Path.Combine(home, ".config", "git", "config"));
            }
            if (!any) return null;
            if (Env("XDG_CONFIG_HOME") is { } xdg) files.Add(Path.Combine(xdg, "git", "config"));
        }
        if (Env("GIT_CONFIG_SYSTEM") is { } system) files.Add(Path.GetFullPath(system, repository));
        else if (!IsTrue(Env("GIT_CONFIG_NOSYSTEM")) && systemFile is null) return null; // cannot locate it

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            if (ReadShared(file) is not { } bytes) return null;
            if (System.Text.Encoding.UTF8.GetString(bytes).Contains("[include", StringComparison.OrdinalIgnoreCase)) return null;
        }
        return [.. files];
    }

    /// <summary>git's boolean reading of an environment value (true/yes/on/1, case-insensitive).</summary>
    private static bool IsTrue(string? value) =>
        value is not null && (value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on");

    /// <summary>The git dir and common dir behind a checkout's .git entry; null when it is neither a
    /// directory nor a well-formed gitfile (git's to judge).</summary>
    private static (string GitDir, string Common)? GitDirectories(string dotGit)
    {
        string gitDir;
        if (Directory.Exists(dotGit)) gitDir = dotGit;
        else if (File.Exists(dotGit))
        {
            var text = ReadShared(dotGit) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n') : null;
            if (text is null || !text.StartsWith("gitdir: ", StringComparison.Ordinal) || text.Contains('\n')) return null;
            gitDir = Path.GetFullPath(text["gitdir: ".Length..], Path.GetDirectoryName(dotGit)!);
        }
        else return null;
        var commondir = Path.Combine(gitDir, "commondir");
        if (!File.Exists(commondir)) return (gitDir, gitDir);
        var line = ReadShared(commondir) is { } raw ? System.Text.Encoding.UTF8.GetString(raw).TrimEnd('\r', '\n') : null;
        if (string.IsNullOrEmpty(line) || line.Contains('\n') || line != line.Trim()) return null;
        return (gitDir, Path.GetFullPath(line, gitDir));
    }

    public static (string Path, string Hash)[] Stamp(string[] files) => [.. files.Select(f => (f, Hash(f)))];

    public void Store(string repository, LandingGitResult result, (string Path, string Hash)[] before)
    {
        if (result.ExitCode != 0) return;
        foreach (var (path, hash) in before)
            if (Hash(path) != hash) return; // config changed while git answered: do not keep it
        _entries[repository] = new Entry(result, before);
    }

    /// <summary>The raw bytes' hash; a directory's existence and creation time; "absent".</summary>
    private static string Hash(string path)
    {
        if (Directory.Exists(path)) return "dir:" + Directory.GetCreationTimeUtc(path).Ticks;
        if (!File.Exists(path)) return "absent";
        var bytes = ReadShared(path);
        return bytes is null ? "unreadable:" + Guid.NewGuid() : Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Reads without blocking git's rename-over-write (FileShare.Delete), or null.</summary>
    private static byte[]? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
