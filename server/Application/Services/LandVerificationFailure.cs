using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1995: the last lines of a failed land build, the projects that output names, and whether a
/// diff of the same target touches one of those projects, the projects they reference, or a build
/// input every project reads. This is not the warm-artifact cache.
/// </summary>
internal static partial class LandVerificationFailure
{
    internal const int KeptLines = 40;
    internal const int StoredChars = 12000;
    internal const int EventDetailChars = 3500;
    internal const int ProjectListChars = 2000;
    internal const int MaxClosureProjects = 256;

    // Only compiler errors name a broken project. An MSB error (a copy lock, a node exit) is not one.
    [GeneratedRegex(@"error\s+CS\d+\b[^\r\n]*\[(?<project>[^\]]+\.csproj)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectError();

    /// <summary>The last <paramref name="lines"/> lines, keeping the end when that slice is still too long.</summary>
    internal static string LastLines(string? text, int lines = KeptLines, int maxChars = StoredChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var parts = normalized.Split('\n');
        var start = Math.Max(0, parts.Length - lines);
        var slice = string.Join('\n', parts[start..]);
        return slice.Length <= maxChars ? slice : slice[^maxChars..];
    }

    /// <summary>
    /// <paramref name="header"/>, then as much of the end of <paramref name="output"/> as fits in
    /// <paramref name="limit"/> characters, so the header survives a long output.
    /// </summary>
    internal static string WithHeader(string header, string? output, int limit)
    {
        if (header.Length >= limit) return header[..limit];
        if (string.IsNullOrEmpty(output)) return header;
        var room = limit - header.Length - 1;
        return room <= 0 ? header : header + "\n" + (output.Length <= room ? output : output[^room..]);
    }

    /// <summary>The end of the saved output, short enough for an event detail of 4000 characters.</summary>
    internal static string? EventDetail(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        return output.Length <= EventDetailChars ? output : output[^EventDetailChars..];
    }

    /// <summary>Distinct repo-relative csproj paths named by build errors, or null when none can be kept.</summary>
    internal static string? ProjectList(string? output, string? worktree)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var found = new List<string>();
        foreach (Match match in ProjectError().Matches(output))
        {
            var relative = ToRepoRelative(match.Groups["project"].Value, worktree);
            if (relative is null || !relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;
            if (!found.Contains(relative, StringComparer.Ordinal)) found.Add(relative);
        }
        if (found.Count == 0) return null;
        var joined = string.Join(';', found);
        return joined.Length <= ProjectListChars ? joined : null;
    }

    internal static string? ToRepoRelative(string raw, string? worktree)
    {
        var path = raw.Replace('\\', '/').Trim();
        if (!string.IsNullOrEmpty(worktree))
        {
            var prefix = worktree.Replace('\\', '/').TrimEnd('/') + "/";
            if (path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                path = path[prefix.Length..];
        }
        if (path.StartsWith('/') || (path.Length >= 2 && path[1] == ':')) return null;
        return NormalizeRepoPath(path);
    }

    internal static string? NormalizeRepoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var stack = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..")
            {
                if (stack.Count == 0) return null;
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            if (part.Contains(':') || part.Any(char.IsControl)) return null;
            stack.Add(part);
        }
        return stack.Count == 0 ? null : string.Join('/', stack);
    }

    /// <summary>True when <paramref name="changed"/> edits the project, a file under it, or one of its includes.</summary>
    internal static bool Touches(IReadOnlyList<string> changed, string project, IReadOnlyList<string> includes)
    {
        var slash = project.LastIndexOf('/');
        var dir = slash > 0 ? project[..(slash + 1)] : null;
        foreach (var path in changed)
        {
            if (path == project) return true;
            if (dir is not null && path.StartsWith(dir, StringComparison.Ordinal)) return true;
            if (includes.Contains(path, StringComparer.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// True for a file that can change how every project builds: MSBuild props/targets (including
    /// Directory.Build.* and Directory.Packages.props), response files, solutions, global.json,
    /// nuget.config, and analyzer configuration.
    /// </summary>
    internal static bool IsBuildInput(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Equals("global.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("nuget.config", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase))
            return true;
        var extension = Path.GetExtension(name);
        return extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".globalconfig", StringComparison.OrdinalIgnoreCase)
            || extension.StartsWith(".sln", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CARD-1995 D-1. False only when the diff was read, it changes no build input, and every named
    /// project and every project they reference (transitively) could be read and none of them, their
    /// files or their includes changed. True when the diff touches that closure. Null when anything
    /// could not be read or decided, so the caller neither records nor refuses.
    /// </summary>
    internal static async Task<bool?> DiffTouchesClosureAsync(ILandingGit git, string repository,
        string targetSha, string candidateSha, IReadOnlyList<string> projects, CancellationToken ct)
    {
        if (projects.Count == 0) return null;
        var diff = await git.RunAsync(repository,
            ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", targetSha, candidateSha, "--"], ct);
        if (!diff.Succeeded) return null;
        var changed = diff.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRepoPath)
            .Where(path => path is not null)
            .Cast<string>()
            .ToArray();
        if (changed.Any(IsBuildInput)) return true;
        var pending = new Queue<string>(projects);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var project))
        {
            if (!seen.Add(project)) continue;
            if (seen.Count > MaxClosureProjects) return null;
            if (Touches(changed, project, [])) return true;
            var shown = await git.RunAsync(repository, ["show", targetSha + ":" + project], ct);
            if (!shown.Succeeded) return null;
            var inputs = ProjectInputs(shown.Output, project);
            if (inputs is null) return null;
            if (Touches(changed, project, inputs.Value.Includes)) return true;
            foreach (var reference in inputs.Value.References) pending.Enqueue(reference);
        }
        return false;
    }

    /// <summary>Resolved include paths, or null when the project cannot be read well enough to decide.</summary>
    internal static IReadOnlyList<string>? CompileIncludes(string xml, string project) =>
        ProjectInputs(xml, project)?.Includes;

    // Item types whose Include is a name, not a repository path.
    private static readonly HashSet<string> NonPathItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "PackageReference", "PackageVersion", "GlobalPackageReference", "PackageDownload", "FrameworkReference",
        "InternalsVisibleTo", "Using", "ProjectCapability", "SupportedPlatform",
    };

    /// <summary>
    /// The repository paths a project reads beside its own directory (every item include and import)
    /// and the projects it references. Null when the XML does not parse, an include is a glob or an
    /// MSBuild expression, or a path leaves the repository.
    /// </summary>
    internal static (IReadOnlyList<string> Includes, IReadOnlyList<string> References)? ProjectInputs(string xml, string project)
    {
        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }
        var slash = project.LastIndexOf('/');
        var dir = slash > 0 ? project[..slash] : "";
        var includes = new List<string>();
        var references = new List<string>();
        foreach (var element in document.Descendants())
        {
            var name = element.Name.LocalName;
            var raw = name == "Import" ? (string?)element.Attribute("Project")
                : element.Parent?.Name.LocalName == "ItemGroup" && !NonPathItems.Contains(name) ? (string?)element.Attribute("Include")
                : null;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var include in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (include.IndexOfAny(['*', '?', '$', '@', '%']) >= 0) return null;
                var normalized = NormalizeRepoPath(string.IsNullOrEmpty(dir) ? include : dir + "/" + include);
                if (normalized is null) return null;
                includes.Add(normalized);
                if (name == "ProjectReference") references.Add(normalized);
            }
        }
        return (includes, references);
    }
}
