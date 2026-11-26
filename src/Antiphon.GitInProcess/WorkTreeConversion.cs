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
/// <para>
/// Also git's: attributes read from a tree (attr.tree, GIT_ATTR_SOURCE), a relative core.attributesFile (resolved from
/// the child's working directory), and system attribute files that differ between libgit2 and the child when either
/// sets an attribute a status reads.
/// </para>
/// </summary>
internal static class WorkTreeConversion
{
    private const int MaxAttributeFileBytes = 1 << 20;

    /// <summary>Null when no conversion libgit2 lacks can apply here, else why git must answer.</summary>
    internal static string? GitOnly(Repository repo, Configuration config, IReadOnlyDictionary<string, string?>? childEnvironment)
    {
        if (GitStatusConfigAgreement.AttributeFiles(childEnvironment) is not { } outside) return "the child's attribute files are unknown";
        // git 2.40+ can read attributes from a tree instead of the work tree and index (attr.tree, GIT_ATTR_SOURCE).
        var attributeSource = childEnvironment is null ? Environment.GetEnvironmentVariable("GIT_ATTR_SOURCE")
            : childEnvironment.TryGetValue("GIT_ATTR_SOURCE", out var source) ? source : null;
        if (!string.IsNullOrEmpty(attributeSource)) return "GIT_ATTR_SOURCE is set";
        if (config.Get<string>("attr.tree") is not null) return "attr.tree is set";
        var files = new List<string>();
        if (outside.System is { } system) files.Add(system);
        if (SystemFilesDiffer(outside.System) is { } differ) return differ;
        files.AddRange(LibGit2SystemFiles());
        if (config.Get<string>("core.attributesfile")?.Value is { Length: > 0 } global)
        {
            if (global.StartsWith('~')) return "core.attributesFile is relative to a home directory";
            // A relative path is the git child's to resolve (from its own working directory), not this process's.
            if (!Path.IsPathFullyQualified(global)) return "core.attributesFile is not an absolute path";
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

    /// <summary>The gitattributes files in libgit2's own system search path that exist.</summary>
    private static IEnumerable<string> LibGit2SystemFiles() =>
        GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.System)
            .Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.GetFullPath(Path.Combine(p, "gitattributes")))
            .Where(File.Exists).Distinct();

    /// <summary>
    /// Null when libgit2 and the git child apply the same system attributes to a status: the same file, or no system
    /// file that sets an attribute a status reads (<see cref="StatusAttributes"/>). Git for Windows' own
    /// <c>etc/gitattributes</c> sets only <c>diff=</c>, so a libgit2 isolated from it still agrees.
    /// </summary>
    private static string? SystemFilesDiffer(string? childSystem)
    {
        var child = childSystem is not null && File.Exists(childSystem) ? childSystem : null;
        var mine = LibGit2SystemFiles().ToArray();
        if (child is not null && mine.Length == 1 && GitText.PathsEqual(mine[0], child)) return null;
        foreach (var file in mine.Append(child).OfType<string>())
            if (StatusAttributes(file) is { } reason) return "system attributes differ: " + reason + " in " + file;
        return null;
    }

    private static readonly HashSet<string> StatusAttributeNames = new(StringComparer.Ordinal)
    {
        "text", "eol", "crlf", "ident", "binary", "filter", "working-tree-encoding",
    };

    /// <summary>
    /// The first attribute in <paramref name="file"/> that a status reads, "unreadable" when it cannot be read, null
    /// when none. Tokens after each pattern (and in an <c>[attr]</c> macro line) are attributes: <c>name</c>,
    /// <c>-name</c>, <c>!name</c> or <c>name=value</c>.
    /// </summary>
    private static string? StatusAttributes(string file)
    {
        string text;
        try
        {
            if (new FileInfo(file).Length > MaxAttributeFileBytes) return "unreadable";
            text = File.ReadAllText(file, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "unreadable"; }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '"') return "a quoted pattern"; // not split here: counted as setting something
            foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var name = token.TrimStart('-', '!');
                var equals = name.IndexOf('=');
                if (equals >= 0) name = name[..equals];
                if (StatusAttributeNames.Contains(name)) return name;
            }
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
