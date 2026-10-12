using System.Xml;
using System.Xml.Linq;

namespace Antiphon.Checkpoints;

// Static XML closure of ProjectReference elements in this project file only.
// Imports and Directory.Build.props are outside this preflight. BuildConfinement evaluates that
// graph and redirects a real dotnet build. This scan stays XML-only because invoking MSBuild here
// would write intermediates before the defence-in-depth check. A relative bin-<id>/ beside each
// statically visible project is the predicted write pair and is not a complete prediction of the build.
// Stamps and dotnet run --no-build still use that relative token.
public static class ProjectWriteRoots
{
    public const string ClosureRefusal = "project-closure";

    public static string DirectoryOf(string projectFull)
    {
        if (!IsProjectFile(projectFull))
            return projectFull;
        var parent = Path.GetDirectoryName(projectFull);
        return string.IsNullOrEmpty(parent) ? projectFull : parent;
    }

    public static string? Preflight(string workingDirectory, string project, string outputPath, string? gitExecutable = null)
    {
        var refusal = TryCollect(workingDirectory, project, outputPath, out var directories);
        if (refusal is not null)
            return refusal;
        return SafeWrite.Preflight(workingDirectory, directories, gitExecutable);
    }

    public static string? TryCollect(string workingDirectory, string project, string outputPath, out List<string> directories)
    {
        directories = [];
        var projectFull = Path.GetFullPath(Path.IsPathRooted(project) ? project : Path.Combine(workingDirectory, project));
        if (!TryFindProjectFile(projectFull, out var entry, out var failed))
        {
            if (failed)
                return ClosureRefusal;
            AddPair(directories, DirectoryOf(projectFull), outputPath);
            return null;
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(entry!);
        while (queue.Count > 0)
        {
            var current = Path.GetFullPath(queue.Dequeue());
            if (!seen.Add(current))
                continue;
            AddPair(directories, DirectoryOf(current), outputPath);
            List<string> references;
            try
            {
                if (!TryReadReferences(current, out references))
                    return ClosureRefusal;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
            {
                return ClosureRefusal;
            }

            foreach (var include in references)
            {
                var resolved = Path.GetFullPath(Path.Combine(DirectoryOf(current), include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolved))
                    return ClosureRefusal;
                queue.Enqueue(resolved);
            }
        }

        return null;
    }

    private static void AddPair(List<string> directories, string projectDirectory, string outputPath)
    {
        directories.Add(Path.IsPathRooted(outputPath)
            ? Path.GetFullPath(outputPath)
            : Path.GetFullPath(Path.Combine(projectDirectory, outputPath)));
        directories.Add(Path.GetFullPath(Path.Combine(projectDirectory, "obj")));
    }

    private static bool TryFindProjectFile(string projectFull, out string? file, out bool failed)
    {
        file = null;
        failed = false;
        if (File.Exists(projectFull))
        {
            if (!IsProjectFile(projectFull))
            {
                failed = true;
                return false;
            }

            file = projectFull;
            return true;
        }

        if (!Directory.Exists(projectFull))
            return false;

        try
        {
            var matches = new List<string>();
            foreach (var candidate in Directory.GetFiles(projectFull))
            {
                if (IsProjectFile(candidate))
                    matches.Add(candidate);
            }

            if (matches.Count == 0)
                return false;
            if (matches.Count != 1)
            {
                failed = true;
                return false;
            }

            file = matches[0];
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed = true;
            return false;
        }
    }

    private static bool TryReadReferences(string projectFile, out List<string> includes)
    {
        includes = [];
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(projectFile, settings);
        var document = XDocument.Load(reader);
        foreach (var element in document.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                continue;
            var include = element.Attribute("Include")?.Value?.Trim();
            if (string.IsNullOrEmpty(include) || include.IndexOfAny(['$', '*', '?']) >= 0)
                return false;
            includes.Add(include);
        }

        return true;
    }

    private static bool IsProjectFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }
}
