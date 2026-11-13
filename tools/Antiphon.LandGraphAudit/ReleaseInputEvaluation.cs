using System.Diagnostics;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace Antiphon.LandGraphAudit;

/// <summary>Actual evaluated, surviving items; separate from the monotone source-only graph.</summary>
internal static class ReleaseInputEvaluation
{
    internal static ReleaseEvaluationReceipt Run(string repo, string solution, ReleaseInputCommand.SourceInput[] inventory,
        string sdk, IReadOnlyDictionary<string, string> options)
    {
        var tracked = inventory.ToDictionary(input => input.Path, StringComparer.Ordinal);
        var unknown = new SortedSet<string>(StringComparer.Ordinal)
        {
            "runtime-closure-unavailable", "generated-provenance-unavailable",
            "platform-evaluation-unproven", "evaluation-environment-unbound",
            "evaluation-probes-unbound", "assembly-metadata-unavailable",
        };
        var sdkInputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var rows = new SortedDictionary<string, object>(StringComparer.Ordinal);
        // No directory is created. Never read local restore/generator outputs as committed truth.
        var absent = Path.Combine(repo, ".antiphon", "release-evaluation-absent-" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TargetFramework"] = options["--framework"], ["Configuration"] = options["--configuration"],
            ["Platform"] = options["--platform"], ["RuntimeIdentifier"] = options["--rid"],
            ["BaseIntermediateOutputPath"] = absent, ["MSBuildProjectExtensionsPath"] = absent,
        };
        using var collection = new ProjectCollection(globals);
        var parsed = SolutionFile.Parse(Path.Combine(repo, solution));
        foreach (var entry in parsed.ProjectsInOrder)
        {
            if (entry.ProjectType == SolutionProjectType.SolutionFolder) continue;
            if (entry.ProjectType != SolutionProjectType.KnownToBeMSBuildFormat)
                throw new InvalidDataException("unsupported-solution-project");
            Visit(entry.AbsolutePath);
        }
        sdkInputs[typeof(Project).Assembly.Location] = ReleaseInputCommand.HashFile(typeof(Project).Assembly.Location);
        foreach (var (path, hash) in sdkInputs)
            if (ReleaseInputCommand.HashFile(path) != hash) throw new InvalidDataException("sdk-drift");
        return new(ReleaseInputCommand.Digest(sdkInputs), unknown.ToArray(), rows.Values.ToArray());

        void Visit(string projectPath)
        {
            var relative = ReleaseInputCommand.Relative(repo, Path.GetFullPath(projectPath));
            if (rows.ContainsKey(relative)) return;
            Bound(projectPath);
            var watch = Stopwatch.StartNew();
            var project = new Project(projectPath, globals, toolsVersion: null, collection,
                ProjectLoadSettings.RecordDuplicateButNotCircularImports | ProjectLoadSettings.RecordEvaluatedItemElements);
            string[] references;
            try
            {
                var imports = project.ImportsIncludingDuplicates.Select(import =>
                {
                    var path = import.ImportedProject.FullPath;
                    var local = Path.GetRelativePath(repo, path).Replace('\\', '/');
                    string hash;
                    if (local != ".." && !local.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(local))
                    {
                        hash = Bound(path);
                    }
                    else
                    {
                        hash = ReleaseInputCommand.HashFile(path);
                        sdkInputs[path] = hash;
                        var sdkRelative = Path.GetRelativePath(sdk, path).Replace('\\', '/');
                        if (sdkRelative == ".." || sdkRelative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(sdkRelative))
                            unknown.Add("external-import-unbound:" + relative);
                        local = "sdk:" + sdkRelative;
                    }
                    return new { path = local, sha256 = hash, condition = import.ImportingElement.Condition };
                }).OrderBy(import => import.path, StringComparer.Ordinal).ToArray();

                var kinds = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Compile", "ProjectReference", "Content", "None", "EmbeddedResource", "AdditionalFiles", "Analyzer",
                };
                var items = project.Items.Where(item => kinds.Contains(item.ItemType)).Select(item =>
                {
                    var full = item.GetMetadataValue("FullPath");
                    var path = ReleaseInputCommand.Relative(repo, full);
                    var hash = Bound(full);
                    if (item.ItemType == "ProjectReference" && new[] { "SetTargetFramework", "SetConfiguration",
                        "SetPlatform", "AdditionalProperties", "GlobalPropertiesToRemove" }.Any(name => item.GetMetadataValue(name).Length > 0))
                        unknown.Add("reference-properties-uncomposed:" + relative);
                    return new { kind = item.ItemType, path, sha256 = hash, link = item.GetMetadataValue("Link"),
                        condition = item.Xml.Condition };
                }).OrderBy(item => item.kind, StringComparer.Ordinal).ThenBy(item => item.path, StringComparer.Ordinal).ToArray();
                references = items.Where(item => item.kind == "ProjectReference").Select(item => item.path).Distinct(StringComparer.Ordinal).ToArray();
                if (project.GetItems("PackageReference").Any()) unknown.Add("package-provenance-unavailable:" + relative);
                rows.Add(relative, new { path = relative, imports, items, references,
                    targetFramework = project.GetPropertyValue("TargetFramework"),
                    targetFrameworks = project.GetPropertyValue("TargetFrameworks"),
                    configuration = project.GetPropertyValue("Configuration"), platform = project.GetPropertyValue("Platform"),
                    runtimeIdentifier = project.GetPropertyValue("RuntimeIdentifier") });
            }
            finally
            {
                collection.UnloadProject(project);
                Console.Error.WriteLine($"release-input phase=evaluate project={relative} elapsedMs={watch.ElapsedMilliseconds}");
            }
            foreach (var reference in references) Visit(Path.Combine(repo, reference));
        }

        string Bound(string full)
        {
            var path = ReleaseInputCommand.Relative(repo, Path.GetFullPath(full));
            if (!tracked.TryGetValue(path, out var input)) throw new InvalidDataException("unbound-input:" + path);
            ReleaseInputCommand.CheckRegularPath(repo, full);
            if (ReleaseInputCommand.HashFile(full) != input.Sha256) throw new InvalidDataException("source-drift");
            return input.Sha256;
        }
    }
}
