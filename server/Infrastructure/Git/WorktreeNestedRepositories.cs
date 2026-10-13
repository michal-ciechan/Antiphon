using System.Collections.Immutable;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Filesystem-only census; stops at recognized repository boundaries without running private Git.</summary>
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
            if (!LandingGit.PathsEqual(directory, root))
            {
                // A .git entry is the existing nested-worktree boundary. Its private
                // worktree may contain HEAD (V-9), or another bare repository (V-10).
                if (HasGitMarker(directory))
                {
                    if (!bareOnly) repositories.Add(Relative(root, directory));
                    continue;
                }
                if (bareOnly ? HasStrictBareMarker(directory) : HasHeadPreservationMarker(directory))
                {
                    repositories.Add(Relative(root, directory));
                    continue;
                }
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
        HasGitMarker(directory) || HasHeadPreservationMarker(directory);

    private static bool HasGitMarker(string directory) =>
        Attributes(Path.Combine(directory, ".git")) is not null;

    // Bare administration lives directly in the directory (HEAD, objects/, refs/).
    // HEAD alone is ambiguous, so preserve even damaged layouts and case twins. Do not
    // read private config, follow an administrative link, or execute private Git.
    private static bool HasHeadPreservationMarker(string directory) =>
        Directory.EnumerateFileSystemEntries(directory)
            .Any(entry => Path.GetFileName(entry).Equals("HEAD", StringComparison.OrdinalIgnoreCase));

    // Proof uses the complete exposed layout, separately from cleanup's conservative
    // HEAD-only evidence (V-7 / V-11). Inspect entry kinds without following links.
    private static bool HasStrictBareMarker(string directory)
    {
        var head = false;
        var objects = false;
        var refs = false;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(entry);
            if (!name.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("objects", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("refs", StringComparison.OrdinalIgnoreCase)) continue;
            var attributes = Attributes(entry);
            if (attributes is null) continue;
            var isDirectory = attributes.Value.HasFlag(FileAttributes.Directory);
            if (name.Equals("HEAD", StringComparison.OrdinalIgnoreCase) && !isDirectory) head = true;
            if (name.Equals("objects", StringComparison.OrdinalIgnoreCase) && isDirectory) objects = true;
            if (name.Equals("refs", StringComparison.OrdinalIgnoreCase) && isDirectory) refs = true;
        }
        return head && objects && refs;
    }

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
