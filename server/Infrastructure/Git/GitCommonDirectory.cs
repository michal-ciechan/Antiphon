using System.Collections.Concurrent;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Reuses git's answer to <c>rev-parse --path-format=absolute --git-common-dir</c> for a repository
/// or worktree root while the on-disk layout that decides it is unchanged. The value always comes
/// from git (so it keeps git's real-path spelling: on-disk case, the real drive behind a subst);
/// only the repeat is saved. Every mutating land command journals itself under the common dir and
/// asked git for it each time: 21 of the ~174 git processes in one post-land delivery test.
/// <para>
/// What decides the answer for a root, per gitrepository-layout(5): whether <c>&lt;root&gt;/.git</c>
/// is a directory or a <c>gitdir:</c> pointer file, the pointer's text, and a <c>commondir</c> file in
/// the git dir. The stamp holds all of them plus the <c>.git</c> entry's creation time, so a
/// re-created or re-pointed checkout misses. No <c>.git</c> at the path (a subdirectory, a bare
/// repository), an unreadable entry, or <c>GIT_DIR</c>/<c>GIT_COMMON_DIR</c>/<c>GIT_WORK_TREE</c> in
/// this process's environment (git children inherit it) are never cached.
/// </para>
/// </summary>
internal static class GitCommonDirectory
{
    private const int Limit = 1024;
    private static readonly ConcurrentDictionary<string, (string Stamp, string Common)> Cache = new(StringComparer.Ordinal);

    public static async Task<string> ResolveAsync(string repository, Func<Task<string>> askGit)
    {
        var key = KeyFor(repository);
        var stamp = key is null ? null : Stamp(key);
        if (stamp is not null && Cache.TryGetValue(key!, out var hit) && hit.Stamp == stamp && Directory.Exists(hit.Common))
            return hit.Common;
        var common = await askGit().ConfigureAwait(false);
        if (stamp is not null)
        {
            if (Cache.Count >= Limit)
                Cache.Clear();
            Cache[key!] = (stamp, common);
        }
        return common;
    }

    private static string? KeyFor(string repository)
    {
        try { return string.IsNullOrWhiteSpace(repository) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>The layout that decides the common dir, or null when it must not be cached.</summary>
    internal static string? Stamp(string root)
    {
        if (HasOverride())
            return null;
        try
        {
            var dotGit = Path.Combine(root, ".git");
            // Where the path really leads: a junction or symlink above the checkout re-pointed at
            // another tree (even one with identical timestamps) is a different layout.
            var real = ResolveLinks(dotGit);
            if (real is null)
                return null;
            if (Directory.Exists(dotGit))
            {
                var commondir = Path.Combine(dotGit, "commondir");
                var named = File.Exists(commondir) ? File.ReadAllText(commondir) : "";
                return $"dir|{real}|{Directory.GetCreationTimeUtc(dotGit).Ticks}|{named}";
            }
            if (File.Exists(dotGit))
                return $"file|{real}|{File.GetCreationTimeUtc(dotGit).Ticks}|{File.GetLastWriteTimeUtc(dotGit).Ticks}|{File.ReadAllText(dotGit)}";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The full path with every reparse point (junction, symlink) on it resolved, or null
    /// when a component is missing or a link cannot be resolved.</summary>
    private static string? ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return null;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists)
                return null;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null)
                    return null;
                current = Path.GetFullPath(target.FullName);
            }
        }
        return current;
    }

    private static bool HasOverride() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_DIR"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_COMMON_DIR"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_WORK_TREE"));
}
