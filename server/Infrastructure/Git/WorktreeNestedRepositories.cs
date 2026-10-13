using System.Collections.Immutable;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Filesystem-only preservation evidence; never enter a private repository or run its Git.</summary>
internal static class WorktreeNestedRepositories
{
    public static ImmutableArray<string> Find(string root, bool bareOnly = false)
    {
        var repositories = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            // Never follow a directory link, including one swapped in since its parent was listed.
            var attributes = Attributes(directory);
            if (attributes is null || attributes.Value.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (!LandingGit.PathsEqual(directory, root) && HasMarker(directory))
            {
                if (!bareOnly || HasBareMarker(directory))
                    repositories.Add(Relative(root, directory));
                continue; // Do not enumerate administrative or private worktree bytes.
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                // The outer checkout's administrative entry is not a nested repository.
                if (LandingGit.PathsEqual(directory, root)
                    && Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                var child = Attributes(entry);
                if (child is not null && child.Value.HasFlag(FileAttributes.Directory)
                    && !child.Value.HasFlag(FileAttributes.ReparsePoint)) pending.Push(entry);
            }
        }
        return [.. repositories];
    }

    // Any marker is preservation evidence, including a gitdir file or a reparse entry.
    // Unknown/access errors propagate; File.Exists would turn them into false absence.
    public static bool HasMarker(string directory) =>
        Attributes(Path.Combine(directory, ".git")) is not null || HasBareMarker(directory);

    // Bare administration lives directly in the directory (HEAD, objects/, refs/).
    // HEAD alone is ambiguous, so preserve even damaged layouts and case twins. Do not
    // read private config, follow an administrative link, or execute private Git.
    private static bool HasBareMarker(string directory) =>
        Directory.EnumerateFileSystemEntries(directory)
            .Any(entry => Path.GetFileName(entry).Equals("HEAD", StringComparison.OrdinalIgnoreCase));

    public static void ThrowIfRepository(string directory, string? root)
    {
        if (root is not null && !LandingGit.PathsEqual(directory, root) && HasMarker(directory))
            throw new NestedRepositoryPreservedException([Relative(root, directory)]);
    }

    private static string Relative(string root, string directory) =>
        Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/') + "/";

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

internal sealed class NestedRepositoryPreservedException(ImmutableArray<string> paths)
    : IOException("ignored_content_preserved")
{
    public ImmutableArray<string> Paths { get; } = paths;
}
