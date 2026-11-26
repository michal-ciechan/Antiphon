// N0 (unit 57): the build inputs of antiphon-scripts as files of a checkout, read from the disk (tracked, dirty and
// untracked alike; git is not asked). Unit 59: the same rules list another product's inputs from its own entry project
// (CliProduct; the checkpoint tool's tools/Antiphon.Checkpoints). The inputs are:
//  - the project graph from scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj through every ProjectReference
//    (Antiphon.Nightly, Antiphon.LiveLane, Antiphon.CliLauncher, src/Antiphon.IO.WriteBehind today): every file under
//    each project's folder, except what the SDK's default globs leave out there (the top-level bin, obj, bin-* and
//    workspace folders, Directory.Build.props' DefaultItemExcludes, and any folder whose name starts with a dot);
//  - every existing file a project or build file names in an Include or Project attribute (a Compile link such as
//    ../CheckpointProcessRunner.cs, an Import), with $(MSBuildThisFileDirectory) and $(MSBuildProjectDirectory)
//    resolved; a wildcard names every file under the folder before it, a value with any other property is skipped;
//  - in each project's folder and every folder above it up to the checkout: Directory.Build.props, .targets and .rsp,
//    Directory.Packages.props, global.json, NuGet.config, .editorconfig and .globalconfig (names compared ignoring
//    case), and what the .props and .targets among them import.
// Package versions are in the project files, so they are covered by them. The listing gives each file's size and last
// write time; CliIdentity hashes the bytes. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Antiphon.CliLauncher
{
    /// <summary>One build input: its name (relative to the checkout, '/' separators), its full path, size and last write time.</summary>
    public sealed class CliInput
    {
        /// <summary>A listed input.</summary>
        public CliInput(string name, string fullPath, long size, long ticks)
        {
            Name = name;
            FullPath = fullPath;
            Size = size;
            Ticks = ticks;
        }

        /// <summary>The checkout-relative name with '/' separators; a file outside the checkout keeps its full path.</summary>
        public string Name { get; }

        /// <summary>The full path.</summary>
        public string FullPath { get; }

        /// <summary>The size in bytes.</summary>
        public long Size { get; }

        /// <summary>The last write time, UTC ticks.</summary>
        public long Ticks { get; }
    }

    /// <summary>The checkout has no CLI project: nothing can be built or checked.</summary>
    public sealed class CliInputException : Exception
    {
        /// <summary>The reason, one line.</summary>
        public CliInputException(string message) : base(message) { }
    }

    /// <summary>The build inputs of antiphon-scripts in a checkout.</summary>
    public static class CliInputs
    {
        /// <summary>The CLI's project, relative to a checkout.</summary>
        public const string EntryProject = "scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj";

        /// <summary>The build files read from a project's folder and every folder above it (case ignored).</summary>
        public static readonly IReadOnlyList<string> AncestorFiles = Array.AsReadOnly(new[]
        {
            "Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp", "Directory.Packages.props", "global.json",
            "NuGet.config", ".editorconfig", ".globalconfig",
        });

        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>The inputs, ordered by name (ordinal). CliInputException when the checkout has no CLI project.</summary>
        public static IReadOnlyList<CliInput> Discover(string checkout) => Discover(checkout, EntryProject);

        /// <summary>
        /// The inputs of another entry project (a CliProduct's, relative to the checkout), ordered by name (ordinal), by the
        /// same rules. CliInputException when the checkout has no such project.
        /// </summary>
        public static IReadOnlyList<CliInput> Discover(string checkout, string entryProject)
        {
            if (string.IsNullOrWhiteSpace(checkout)) throw new CliInputException("no checkout was named");
            if (string.IsNullOrWhiteSpace(entryProject)) throw new CliInputException("no entry project was named");
            var root = Path.GetFullPath(checkout).TrimEnd('\\', '/');
            var entry = Path.GetFullPath(Path.Combine(root, entryProject));
            if (!File.Exists(entry)) throw new CliInputException(root + " has no " + entryProject);
            var walk = new Walk(root);
            var projects = new Queue<string>();
            projects.Enqueue(entry);
            var seen = new HashSet<string>(PathComparer);
            var folders = new List<string>();
            var probed = new HashSet<string>(PathComparer);
            while (projects.Count > 0)
            {
                var project = projects.Dequeue();
                if (!seen.Add(project) || !File.Exists(project)) continue;
                var directory = Path.GetDirectoryName(project);
                walk.Tree(directory, directory);
                walk.Scan(project, directory, projects);
                for (var folder = directory; folder != null && Within(folder, root); folder = Path.GetDirectoryName(folder))
                {
                    if (probed.Add(folder)) folders.Add(folder);
                    if (string.Equals(folder.TrimEnd('\\', '/'), root, PathComparison)) break;
                }
            }
            foreach (var folder in folders)
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
                {
                    if (!AncestorFiles.Contains(file.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    walk.Add(file);
                    if (file.Name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
                        walk.Scan(file.FullName, null, null);
                }
            }
            var found = new List<CliInput>(walk.Found.Values);
            found.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return found;
        }

        /// <summary>Whether a name is a C# source (the only kind MSBuild picks up again by itself when it is added or removed).</summary>
        public static bool IsSource(string name) => name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

        private static bool Within(string path, string root)
        {
            var full = path.TrimEnd('\\', '/');
            return string.Equals(full, root, PathComparison) || full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
                || full.StartsWith(root + "/", PathComparison);
        }

        private sealed class Walk
        {
            private readonly string _root;
            private readonly HashSet<string> _scanned = new HashSet<string>(PathComparer);

            public Walk(string root) => _root = root;

            public Dictionary<string, CliInput> Found { get; } = new Dictionary<string, CliInput>(PathComparer);

            public void Add(FileInfo file)
            {
                if (Found.ContainsKey(file.FullName)) return;
                Found[file.FullName] = new CliInput(Name(file.FullName), file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
            }

            // Every file under the folder: at the top, the SDK's own output folders and the repository's DefaultItemExcludes
            // are left out; at any depth, folders whose name starts with a dot and folder links.
            public void Tree(string top, string folder)
            {
                foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if (item is FileInfo file)
                    {
                        Add(file);
                        continue;
                    }
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || item.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                    if (string.Equals(folder, top, PathComparison) && Excluded(item.Name)) continue;
                    Tree(top, item.FullName);
                }
            }

            // The Include and Project attributes of a project or build file: files are inputs, ProjectReferences are queued,
            // and imported .props and .targets are scanned in turn.
            public void Scan(string path, string projectDirectory, Queue<string> projects)
            {
                if (!_scanned.Add(path)) return;
                var directory = Path.GetDirectoryName(path);
                foreach (var (element, raw) in Attributes(File.ReadAllText(path)))
                {
                    foreach (var part in raw.Split(';'))
                    {
                        var value = part.Trim();
                        if (value.Length == 0) continue;
                        value = value.Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar);
                        if (projectDirectory != null) value = value.Replace("$(MSBuildProjectDirectory)", projectDirectory);
                        if (value.Contains("$(", StringComparison.Ordinal) || value.Contains("@(", StringComparison.Ordinal) || value.Contains("%(", StringComparison.Ordinal)) continue;
                        value = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                        if (value.IndexOfAny(new[] { '*', '?' }) >= 0)
                        {
                            Glob(directory, value);
                            continue;
                        }
                        string full;
                        try { full = Path.GetFullPath(Path.Combine(directory, value)); }
                        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { continue; }
                        if (string.Equals(element, "ProjectReference", StringComparison.OrdinalIgnoreCase) && projects != null)
                            projects.Enqueue(full);
                        if (!File.Exists(full)) continue;
                        Add(new FileInfo(full));
                        if (full.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
                            Scan(full, projectDirectory, projects);
                    }
                }
            }

            // Each Include and Project attribute (any quote, the name ignoring case) with its element's name. Comments,
            // end tags, declarations and processing instructions are skipped; a '>' inside a quoted value does not end a tag.
            private static List<(string Element, string Value)> Attributes(string text)
            {
                var found = new List<(string, string)>();
                var i = 0;
                while ((i = text.IndexOf('<', i)) >= 0)
                {
                    if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
                    {
                        var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                        i = end < 0 ? text.Length : end + 3;
                        continue;
                    }
                    i++;
                    if (i < text.Length && (text[i] == '/' || text[i] == '!' || text[i] == '?'))
                    {
                        var end = text.IndexOf('>', i);
                        i = end < 0 ? text.Length : end + 1;
                        continue;
                    }
                    var start = i;
                    while (i < text.Length && IsNameChar(text[i])) i++;
                    var element = text.Substring(start, i - start);
                    while (i < text.Length && text[i] != '>')
                    {
                        if (char.IsWhiteSpace(text[i]) || text[i] == '/')
                        {
                            i++;
                            continue;
                        }
                        var nameStart = i;
                        while (i < text.Length && IsNameChar(text[i])) i++;
                        var name = text.Substring(nameStart, i - nameStart);
                        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                        if (i >= text.Length || text[i] != '=')
                        {
                            if (name.Length == 0) i++;
                            continue;
                        }
                        i++;
                        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                        if (i >= text.Length || (text[i] != '"' && text[i] != '\'')) continue;
                        var quote = text[i];
                        var close = text.IndexOf(quote, i + 1);
                        if (close < 0) return found;
                        if (string.Equals(name, "Include", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Project", StringComparison.OrdinalIgnoreCase))
                            found.Add((element, text.Substring(i + 1, close - i - 1)));
                        i = close + 1;
                    }
                }
                return found;
            }

            private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' || c == ':';

            // A wildcard: every file under the folder before the first wildcard segment (more than MSBuild would take).
            private void Glob(string directory, string pattern)
            {
                var separator = pattern.Substring(0, pattern.IndexOfAny(new[] { '*', '?' })).LastIndexOf(Path.DirectorySeparatorChar);
                var baseFolder = separator < 0 ? directory : Path.Combine(directory, pattern.Substring(0, separator));
                string full;
                try { full = Path.GetFullPath(baseFolder); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return; }
                if (Directory.Exists(full)) Tree(full, full);
            }

            private string Name(string fullPath) =>
                Within(fullPath, _root) && fullPath.Length > _root.Length
                    ? fullPath.Substring(_root.Length + 1).Replace('\\', '/')
                    : fullPath.Replace('\\', '/');

            private static bool Excluded(string name) =>
                string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "workspace", StringComparison.OrdinalIgnoreCase) || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
