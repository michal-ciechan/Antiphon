using System.Text;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// The work-tree conversions git applies before it compares a tracked file with the index and libgit2 does not:
/// an external clean/smudge driver (<c>filter=</c>; git-lfs is one) and <c>working-tree-encoding</c>. With either in
/// play libgit2 hashes the raw bytes, so it can call a file git sees as clean modified (a false <c>park_dirty</c>),
/// or a file git sees as modified clean (a false clean, the direction that loses work). Measured against git by
/// <c>LibGit2VsGitWorkingTreeVerdictTests</c> (the filter-driver-* and working-tree-encoding-* scenarios); libgit2's
/// own conversions (text, eol, core.autocrlf, ident, core.symlinks=false links) agree with git there and stay in process.
/// <para>
/// A status is git's whenever an attribute file the git child could apply to a tracked file names either: the
/// <c>.gitattributes</c> of every directory holding a tracked file (the work-tree copy, else the staged blob; an
/// untracked <c>.gitattributes</c> elsewhere applies to no tracked file), <c>info/attributes</c> of the common
/// directory, the global file (core.attributesFile, else the child's and libgit2's XDG defaults) and the child's
/// system file (<see cref="GitStatusConfigAgreement.AttributeFiles"/>). The test is textual and errs towards git:
/// any <c>filter=</c> or <c>working-tree-encoding</c> in one of those files counts, in a macro, a pattern that
/// matches nothing, or a comment. A driver configured with no attribute naming it (Git for Windows configures
/// git-lfs system-wide) changes nothing and is not looked at.
/// </para>
/// </summary>
internal static class WorkTreeConversion
{
    private const int MaxAttributeFileBytes = 1 << 20;

    /// <summary>Null when no conversion libgit2 lacks can apply here, else why git must answer.</summary>
    internal static string? GitOnly(Repository repo, Configuration config, IReadOnlyDictionary<string, string?>? childEnvironment)
    {
        if (GitStatusConfigAgreement.AttributeFiles(childEnvironment) is not { } outside) return "the child's attribute files are unknown";
        var files = new List<string>();
        if (outside.System is { } system) files.Add(system);
        if (config.Get<string>("core.attributesfile")?.Value is { Length: > 0 } global)
        {
            if (global.StartsWith('~')) return "core.attributesFile is relative to a home directory";
            files.Add(global);
        }
        else
        {
            if (outside.DefaultGlobal is { } childDefault) files.Add(childDefault);
            files.AddRange(GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.Xdg)
                .Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.Combine(p, "attributes")));
        }
        if (InProcessGitReads.CommonDirectory(repo.Info.Path) is not { } common) return "no common directory";
        files.Add(Path.Combine(common, "info", "attributes"));
        foreach (var file in files)
            if (Names(file) is { } named) return named + " in " + file;

        var root = repo.Info.WorkingDirectory;
        var tracked = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal) { "" };
        foreach (var entry in repo.Index)
        {
            var path = entry.Path.Replace('\\', '/');
            if (path == ".gitattributes" || path.EndsWith("/.gitattributes", StringComparison.Ordinal)) tracked[path] = entry;
            for (var slash = path.LastIndexOf('/'); slash > 0 && directories.Add(path[..slash]); slash = path.LastIndexOf('/', slash - 1)) { }
        }
        foreach (var directory in directories)
        {
            var relative = directory.Length == 0 ? ".gitattributes" : directory + "/.gitattributes";
            var onDisk = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            string? named;
            if (File.Exists(onDisk) || Directory.Exists(onDisk)) named = Names(onDisk);
            else if (tracked.TryGetValue(relative, out var staged)) named = Names(repo.Lookup<Blob>(staged.Id));
            else continue;
            if (named is not null) return named + " in " + relative;
        }
        return null;
    }

    /// <summary>The conversion a file names, "unreadable" for one that exists but cannot be read, null otherwise.</summary>
    private static string? Names(string file)
    {
        try
        {
            if (Directory.Exists(file)) return "unreadable";
            var info = new FileInfo(file);
            if (!info.Exists) return null;
            if (info.Length > MaxAttributeFileBytes) return "unreadable";
            return NamedIn(File.ReadAllText(file, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "unreadable"; }
    }

    private static string? Names(Blob? blob)
    {
        if (blob is null) return "unreadable";
        if (blob.Size > MaxAttributeFileBytes) return "unreadable";
        return NamedIn(blob.GetContentText(Encoding.UTF8));
    }

    private static string? NamedIn(string text) =>
        text.Contains("working-tree-encoding", StringComparison.OrdinalIgnoreCase) ? "working-tree-encoding"
        : text.Contains("filter=", StringComparison.OrdinalIgnoreCase) ? "filter="
        : null;
}
