using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Antiphon.Checkpoints.Coverage;

/// <summary>Read-only CLI adapter, deliberately independent of CheckpointApp and its runtime.</summary>
public sealed class CoverageCommand
{
    public int Run(string root, string plan, IReadOnlyList<string>? tests = null, string format = "text", string? checklist = null, TextWriter? output = null)
    {
        output ??= Console.Out;
        var report = new PlanCoverageReport { Plan = SafeDisplay(plan) };
        var inputPath = SafeDisplay(plan);
        try
        {
            if (format is not ("text" or "json")) throw new InvalidDataException("unsupported format");
            root = CanonicalRoot(root);
            var contents = new Dictionary<string, (string Text, int Bytes)>(PathComparer());
            var planPath = SelectPath(plan); var planText = Read(planPath, ConfinedFileReader.DocumentLimit);
            report.Plan = Relative(root, planPath); report.PlanSha256 = PlanCoverageReport.Hash(planText);
            var imported = PlanTableImporter.ImportMarkdown(planText, planPath);
            if (imported.Manifest is null) throw new InvalidDataException("invalid checkpoint manifest");
            var checklistText = checklist is null ? null : Read(SelectPath(checklist), ConfinedFileReader.DocumentLimit);
            var contract = new PlanCoverageReader().Read(report.Plan, planText, checklistText);
            if (contract.Invalid) { report = contract; output.Write(format == "json" ? report.Json() : report.Text()); return report.ExitCode; }
            var census = contract.SelectedClassCensus ? new ClassCensusSelection() : null;
            var selected = new Dictionary<string, CoverageSource>(PathComparer());
            var explicitSet = new HashSet<string>(PathComparer());
            foreach (var path in tests ?? [])
            {
                var canonical = SelectPath(path);
                if (!explicitSet.Add(canonical)) throw new InvalidDataException("duplicate explicit source");
                Add(canonical);
            }
            // Read literal test paths only in Scope/implementation sections, never command/code cells elsewhere.
            var scope = false; var fence = false;
            foreach (var line in planText.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fence = !fence; continue; }
                if (fence) continue;
                if (Regex.IsMatch(line, @"^#{1,3} ")) scope = Regex.IsMatch(line, @"scope|implementation footprint", RegexOptions.IgnoreCase);
                if (!scope) continue;
                foreach (Match match in Regex.Matches(line, @"(?:tests/[A-Za-z0-9._/-]+\.cs)\b")) Add(SelectPath(match.Value));
                // Cells listing a directory then short filenames inherit that directory.
                var prefix = Regex.Match(line, @"tests/[A-Za-z0-9._/-]+/[A-Za-z0-9_]+\.cs");
                if (prefix.Success)
                    foreach (Match shortName in Regex.Matches(line, @"`([A-Za-z_][A-Za-z0-9_]+\.cs)`"))
                        Add(SelectPath(Path.Combine(Path.GetDirectoryName(prefix.Value)!, shortName.Groups[1].Value)));
            }
            foreach (var row in imported.Manifest.Checkpoints)
            {
                var supported = ClassCensusSelection.Supports(row, out _);
                if (census is not null && !supported) census.Add(row, planText, null);
                if (row.IsCommand)
                {
                    if (explicitSet.Count == 0 || checklist is null && !planText.Contains("```plan-coverage-v1", StringComparison.Ordinal))
                        throw new InvalidDataException("command selection requires explicit tests and bindings");
                    continue;
                }
                var segments = row.Filter?.Split('/');
                if (segments is null || segments.Length != 5 || segments[1] != "*" || segments[2].Contains('[')
                    || segments[3] == "*" || segments[3].Contains('['))
                {
                    if (explicitSet.Count == 0 || checklist is null && !planText.Contains("```plan-coverage-v1", StringComparison.Ordinal)) throw new InvalidDataException("unsupported class selection requires explicit tests and bindings");
                    continue;
                }
                var classOperands = segments[3].Split('|').Select(s => s.Trim('(', ')')).ToArray();
                if (classOperands.Any(s => !Regex.IsMatch(s, @"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\*?$")))
                {
                    if (census is not null && explicitSet.Count > 0) continue;
                    throw new InvalidDataException("unsupported class filter");
                }
                if (census is not null && !supported) continue;
                var build = imported.Manifest.Builds.Single(b => b.Id == row.Build);
                var project = SelectPath(build.Project, true);
                var directory = Directory.Exists(project) ? project : Path.GetDirectoryName(project)!;
                var candidates = ProjectSources(root, directory, selected.Values, path => Read(path, ConfinedFileReader.SourceLimit));
                var index = new TestAssertionIndex(candidates);
                if (index.Diagnostics.Any()) throw new InvalidDataException("invalid selected project syntax");
                census?.Add(row, planText, index);
                foreach (var operand in classOperands)
                {
                    var token = operand.TrimEnd('*'); var suffix = operand.EndsWith('*');
                    var matches = candidates.Where(s => index.Classes(s.Path).Any(c => MatchesClass(c, token, suffix, segments[2]))).ToArray();
                    if (matches.Length == 0) throw new InvalidDataException("unresolved selected class");
                    foreach (var source in matches)
                    {
                        var path = SelectPath(source.Path);
                        if (!path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && !selected.ContainsKey(path))
                            throw new InvalidDataException("linked source requires explicit scope or tests");
                        Add(path);
                    }
                }
            }
            if (selected.Count == 0) throw new InvalidDataException("empty source selection");
            report = new PlanCoverageAnalyzer().Analyze(report.Plan, planText, selected.Values.OrderBy(s => s.Path, StringComparer.Ordinal).ToArray(), checklistText, census);
            string Read(string path, int limit)
            {
                inputPath = Relative(root, path);
                if (!contents.TryGetValue(path, out var content))
                {
                    var text = ConfinedFileReader.Read(root, path, limit, out var bytes);
                    content = (text, bytes);
                    contents.Add(path, content);
                }
                if (content.Bytes > limit) throw new InvalidDataException("selected file exceeds size limit");
                return content.Text;
            }
            string SelectPath(string path, bool directoryAllowed = false)
            {
                var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
                inputPath = full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? Relative(root, full) : SafeDisplay(path);
                return Confined(root, path, directoryAllowed);
            }
            void Add(string path)
            {
                inputPath = Relative(root, path);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("source must be C#");
                if (!selected.ContainsKey(path)) selected.Add(path, new(Relative(root, path), Read(path, ConfinedFileReader.SourceLimit)));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                   or System.Xml.XmlException or NotSupportedException)
        {
            report.Invalid = true;
            report.Diagnostics.Add(new("INPUT_INVALID", TestPath: inputPath, Detail: ex is InvalidDataException ? ex.Message : "unreadable or invalid selected path"));
        }
        catch (Exception)
        {
            report.Invalid = true; report.Diagnostics.Add(new("ANALYSIS_INVALID", Detail: "internal analysis failure"));
        }
        output.Write(format == "json" ? report.Json() : report.Text());
        return report.ExitCode;
    }
    private static bool MatchesClass(string name, string token, bool suffix, string ns)
    {
        var simple = name[(name.LastIndexOf('.') + 1)..];
        var match = token.Contains('.') ? (suffix ? name.StartsWith(token, StringComparison.Ordinal) || name.Contains("." + token, StringComparison.Ordinal) : name == token || name.EndsWith("." + token, StringComparison.Ordinal))
            : suffix ? simple.StartsWith(token, StringComparison.Ordinal) : simple == token;
        return match && (ns == "*" || name.StartsWith(ns + ".", StringComparison.Ordinal));
    }
    private static List<CoverageSource> ProjectSources(string root, string directory, IEnumerable<CoverageSource> explicitSources, Func<string, string> read)
    {
        var paths = new HashSet<string>(PathComparer());
        var visited = new HashSet<string>(PathComparer());
        void Walk(string dir)
        {
            if (!visited.Add(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs")) paths.Add(Confined(root, file));
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(child);
                if (name is "bin" or "obj" or "workspace" || name.StartsWith("bin-", StringComparison.Ordinal)) continue;
                Walk(Confined(root, child, true));
            }
        }
        Walk(directory);
        foreach (var project in Directory.EnumerateFiles(directory, "*.csproj"))
        {
            foreach (var include in XDocument.Parse(read(Confined(root, project))).Descendants("Compile").Attributes("Include"))
            {
                var value = include.Value.Replace('\\', Path.DirectorySeparatorChar);
                if (value.Contains('*') || value.Contains('$')) throw new InvalidDataException("unsupported linked source requires literal scope");
                var linked = Confined(root, Path.Combine(directory, value));
                if (!explicitSources.Any(s => Confined(root, s.Path) == linked))
                {
                    // In-root linked files may declare a filter-selected class. External links are never admitted.
                    paths.Add(linked);
                }
            }
        }
        return paths.Order(StringComparer.Ordinal).Select(path => new CoverageSource(Relative(root, path), read(path))).ToList();
    }
    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string SafeDisplay(string path) => Path.IsPathRooted(path) ? Path.GetFileName(path) : path.Replace('\\', '/');
    private static string CanonicalRoot(string root)
    {
        var full = Path.GetFullPath(root);
        return ResolveComponents(full);
    }
    private static string Confined(string root, string path, bool directoryAllowed = false)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('*') || path.Contains('?')) throw new InvalidDataException("literal path required");
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        var canonical = ResolveComponents(full);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!canonical.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)
            || !File.Exists(canonical) && !(directoryAllowed && Directory.Exists(canonical))) throw new InvalidDataException("missing or unconfined selected path");
        return canonical;
    }
    private static string ResolveComponents(string path)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget is not null || (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0))
                current = entry.ResolveLinkTarget(true)?.FullName ?? throw new InvalidDataException("unresolvable link");
        }
        return Path.GetFullPath(current);
    }
}
