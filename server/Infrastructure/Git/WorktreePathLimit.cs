namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1714 (Debug 52410842 item 2): the longest path <see cref="WorktreeNoFollowDelete"/> can open. On Windows
/// it opens each entry with a plain <c>CreateFileW</c>, without the <c>\\?\</c> prefix, so an entry whose full
/// path is <see cref="WindowsMaxPath"/> characters or longer cannot be deleted. A removal that had set the tree
/// aside and dropped its registration would stop there, Partial, and every retry would stop at the same entry.
/// Guarded removal measures the tree under the name it will have once set aside (longer than the original by the
/// <c>.removing-</c> suffix) and refuses while nothing has moved. The no-follow delete on Linux has no such limit
/// (PATH_MAX 4096 is never approached), so <see cref="System"/> measures nothing there.
/// </summary>
public sealed class WorktreePathLimit(int? limit)
{
    /// <summary>MAX_PATH: a path of this many characters or more does not fit with its terminator.</summary>
    public const int WindowsMaxPath = 260;

    public static WorktreePathLimit System { get; } = new(OperatingSystem.IsWindows() ? WindowsMaxPath : null);

    public int? Limit => limit;

    /// <summary>
    /// The first entry of <paramref name="tree"/>, named as it will be under <paramref name="aside"/>, whose full
    /// path reaches the limit; null when every entry fits or there is no limit. Read-only and never follows a link.
    /// The walk itself is long-path capable: it enumerates through the extended-length form on Windows.
    /// </summary>
    public string? FirstTooLong(string tree, string aside)
    {
        if (limit is not { } max) return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tree));
        var asideRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(aside));
        if (asideRoot.Length >= max) return asideRoot;
        var start = ExtendedForEnumeration(root);
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count != 0)
        {
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos("*", ListOnly))
            {
                // Children are named under the string the walk started from, so the tail is the relative name.
                var renamed = asideRoot + entry.FullName[start.Length..];
                if (renamed.Length >= max) return renamed;
                if ((entry.Attributes & FileAttributes.Directory) != 0 && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    pending.Push(entry.FullName);
            }
        }
        return null;
    }

    private static string ExtendedForEnumeration(string path)
    {
        if (!OperatingSystem.IsWindows() || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    private static readonly EnumerationOptions ListOnly = new()
    {
        RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false,
    };
}
