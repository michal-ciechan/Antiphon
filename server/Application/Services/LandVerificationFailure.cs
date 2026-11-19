using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1995: the last lines of a failed land build, the projects that output names, and whether a
/// later diff of the same target touches one of those projects. This is not the warm-artifact cache.
/// </summary>
internal static partial class LandVerificationFailure
{
    internal const int KeptLines = 40;
    internal const int StoredChars = 12000;
    internal const int EventDetailChars = 3500;
    internal const int ProjectListChars = 2000;

    [GeneratedRegex(@"error\s+(?:CS|MSB)\d+\b[^\r\n]*\[(?<project>[^\]]+\.csproj)\]",
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
    /// False only when the diff was read, every named project could be read, and none of them changed.
    /// Null means the diff or a project could not be read, so the caller builds.
    /// </summary>
    internal static async Task<bool?> DiffTouchesProjectsAsync(ILandingGit git, string repository,
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
        foreach (var project in projects)
        {
            if (Touches(changed, project, [])) return true;
            var shown = await git.RunAsync(repository, ["show", targetSha + ":" + project], ct);
            if (!shown.Succeeded) return null;
            var includes = CompileIncludes(shown.Output, project);
            if (includes is null) return null;
            if (Touches(changed, project, includes)) return true;
        }
        return false;
    }

    /// <summary>Resolved include paths, or null when the project cannot be read well enough to decide.</summary>
    internal static IReadOnlyList<string>? CompileIncludes(string xml, string project)
    {
        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }
        var includes = new List<string>();
        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName is not ("Compile" or "Content" or "None" or "EmbeddedResource")) continue;
            var include = (string?)element.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include)) continue;
            if (include.Contains('*') || include.Contains('?')) return null;
            var slash = project.LastIndexOf('/');
            var dir = slash > 0 ? project[..slash] : "";
            var combined = string.IsNullOrEmpty(dir) ? include : dir + "/" + include;
            var normalized = NormalizeRepoPath(combined);
            if (normalized is null) return null;
            includes.Add(normalized);
        }
        return includes;
    }
}
