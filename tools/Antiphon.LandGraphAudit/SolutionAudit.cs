using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Evaluation.Context;
using Microsoft.Build.Framework;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// Plan D-2/D-3: evaluate each MSBuild project of the solution the way the land's
/// `dotnet build --artifacts-path &lt;tmp&gt; --configuration Debug` would see it, with no restore,
/// and refuse every evaluated item whose FullPath is docs/**/*.md or the root README.md.
/// </summary>
internal static class SolutionAudit
{
    private const string SolutionConfiguration = "Debug";
    private const string SolutionPlatform = "Any CPU";

    public static int Run(string repo, string solution, string sdkDirectory, TextWriter output)
    {
        // Each pass has its own budget. The old single wait covered both passes and the copy.
        var passBudget = EvaluationBudget.PassTimeout;
        var copyBudget = EvaluationBudget.CopyTimeout;
        string[]? tracked = null;
        string? artifacts = null;
        EvaluationSnapshot? baseline = null;
        CanaryRepository? canary = null;
        using var diagnostics = new StringWriter();
        try
        {
            return AuditPhases.Run(
                solution,
                output,
                new WallClock(),
                passBudget,
                copyBudget,
                pass1: () =>
                {
                    // Locator has registered the SDK before this method is entered. Inspect every
                    // repository evaluation expression, including inactive branches and metadata.
                    var expressionRefusals = new SortedSet<string>(StringComparer.Ordinal);
                    EvaluationExpressionPolicy.Check(repo, expressionRefusals);
                    if (expressionRefusals.Count > 0)
                    {
                        foreach (var refusal in expressionRefusals) output.WriteLine(refusal);
                        return Program.Refused;
                    }

                    tracked = BuildInputPolicy.TrackedFiles(repo);
                    artifacts = Path.Combine(Path.GetTempPath(), "antiphon-landgraph-artifacts-" + Guid.NewGuid().ToString("N"));
                    baseline = new EvaluationSnapshot(repo, repo);
                    return RunPass(repo, solution, sdkDirectory, output, tracked, artifacts, baseline);
                },
                copy: () =>
                {
                    canary = CanaryRepository.Create(repo);
                    return Program.Approved;
                },
                pass2: () =>
                {
                    output.WriteLine("phase=pass2");
                    output.Flush();
                    if (canary is null || baseline is null || tracked is null || artifacts is null)
                        return Program.Fail("evaluation produced no baseline");
                    var changed = new EvaluationSnapshot(canary.Root, repo);
                    try
                    {
                        var second = RunPass(canary.Root, solution, sdkDirectory, diagnostics, tracked, artifacts, changed);
                        if (second != Program.Approved)
                        {
                            output.WriteLine($"refused: differential canary evaluation refused: {Program.OneLine(diagnostics.ToString())}");
                            return Program.Refused;
                        }
                    }
                    catch (Exception ex)
                    {
                        // A previously successful evaluation that fails after changing only documentation
                        // is itself a dependency witness (not an approval and not a baseline load failure).
                        output.WriteLine($"refused: differential canary evaluation failed: {Program.OneLine(Program.Describe(ex))}");
                        return Program.Refused;
                    }

                    var difference = baseline.Difference(changed);
                    if (difference is not null)
                    {
                        output.WriteLine($"refused: differential {difference}");
                        return Program.Refused;
                    }

                    return Program.Approved;
                },
                onTimeout: () =>
                {
                    try { canary?.Dispose(); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"canary delete failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    CanaryRepository.DeleteActive();
                },
                copyClock: new WallClock(CanaryRepository.CancelCopy));
        }
        finally
        {
            canary?.Dispose();
        }
    }

    private static int RunPass(string repo, string solution, string sdkDirectory, TextWriter output,
        string[] tracked, string scratch, EvaluationSnapshot snapshot)
    {
        var solutionPath = Path.GetFullPath(Path.Combine(repo, solution));
        var parsed = SolutionFile.Parse(solutionPath);
        var configuration = parsed.SolutionConfigurations.FirstOrDefault(c =>
                c.ConfigurationName == SolutionConfiguration && c.PlatformName == SolutionPlatform)
            ?? parsed.SolutionConfigurations.FirstOrDefault(c => c.ConfigurationName == SolutionConfiguration);

        // ArtifactsPath points at a directory that is never created: evaluation then finds no
        // restore output (D-12) and nothing under the repository's obj/ is read or written.
        var errors = new ErrorLogger();
        using var collection = new ProjectCollection(
            new Dictionary<string, string>(), [errors], ToolsetDefinitionLocations.Default);
        var fileSystem = new RecordingFileSystem(repo);
        var context = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared, fileSystem);
        var refusals = new SortedSet<string>(StringComparer.Ordinal);
        var evaluated = 0;
        var realRepo = RealPath(repo);
        var realDirectories = new Dictionary<string, string>(StringComparer.Ordinal);
        var inputs = new BuildInputPolicy(repo, sdkDirectory, tracked);
        var evaluatedProjects = new HashSet<string>(BuildInputPolicy.PathComparer);
        var evaluations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in parsed.ProjectsInOrder)
        {
            if (entry.ProjectType == SolutionProjectType.SolutionFolder) continue;
            // The solution build also builds an entry of any other type it can load as a project
            // (a .dcproj, an .esproj); one the audit does not evaluate is a failed evaluation, never a pass.
            if (entry.ProjectType != SolutionProjectType.KnownToBeMSBuildFormat)
                return Program.Fail($"{solution} entry {entry.RelativePath} has project type {entry.ProjectType}, which the audit does not evaluate");

            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = SolutionConfiguration,
                ["ArtifactsPath"] = scratch,
                ["SolutionDir"] = Path.GetDirectoryName(solutionPath) + Path.DirectorySeparatorChar,
                ["SolutionExt"] = Path.GetExtension(solutionPath),
                ["SolutionFileName"] = Path.GetFileName(solutionPath),
                ["SolutionName"] = Path.GetFileNameWithoutExtension(solutionPath),
                ["SolutionPath"] = solutionPath,
            };
            if (configuration is not null && entry.ProjectConfigurations.TryGetValue(configuration.FullName, out var mapped))
            {
                globals["Configuration"] = mapped.ConfigurationName;
                globals["Platform"] = mapped.PlatformName == "Any CPU" ? "AnyCPU" : mapped.PlatformName;
            }

            evaluated += Evaluate(entry.AbsolutePath, globals);
        }

        int Evaluate(string projectPath, Dictionary<string, string> globals)
        {
            projectPath = Path.GetFullPath(projectPath);
            // A cycle or a second solution entry must not repeat the same evaluation. Different
            // global properties still need their own evaluation (including each target framework).
            var key = projectPath + "\0" + string.Join('\0', globals.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Key.ToUpperInvariant() + "=" + p.Value));
            if (!evaluations.Add(key)) return 0;
            evaluatedProjects.Add(projectPath);
            Project? project;
            fileSystem.Owner = Display(repo, projectPath);
            try
            {
                project = Project.FromFile(projectPath, new ProjectOptions
                {
                    GlobalProperties = globals,
                    ProjectCollection = collection,
                    EvaluationContext = context,
                    LoadSettings = ProjectLoadSettings.RecordDuplicateButNotCircularImports
                                   | ProjectLoadSettings.RecordEvaluatedItemElements,
                });
            }
            finally { fileSystem.Owner = null; }
            try
            {
                errors.ThrowIfAny(projectPath);
                inputs.Check(project, refusals);
                snapshot.Add(project, globals);
                foreach (var item in project.AllEvaluatedItems)
                {
                    var fullPath = SkippablePath(repo, realRepo, item.GetMetadataValue("FullPath"),
                        Path.Combine(project.DirectoryPath, item.EvaluatedInclude), Real);
                    if (fullPath is null) continue;
                    var definedIn = item.Xml?.ContainingProject?.FullPath ?? project.FullPath;
                    refusals.Add($"refused: {Display(repo, definedIn)} {item.ItemType} {fullPath}");
                }

                // A multi-targeting outer evaluation hides items conditioned on one framework.
                var frameworks = project.GetPropertyValue("TargetFramework").Length == 0
                    ? project.GetPropertyValue("TargetFrameworks")
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];
                // GetItems is the evaluated, surviving reference list: false Conditions and
                // removed references do not expand the solution build's project closure.
                var references = project.GetItems("ProjectReference")
                    .Select(item => (Path: item.GetMetadataValue("FullPath"), Globals: ReferenceGlobals(globals, item)))
                    .ToArray();
                collection.UnloadProject(project);
                project = null;
                var count = 1;
                foreach (var framework in frameworks.Distinct(StringComparer.OrdinalIgnoreCase))
                    count += Evaluate(projectPath, new(globals, StringComparer.OrdinalIgnoreCase) { ["TargetFramework"] = framework });
                foreach (var reference in references)
                    count += Evaluate(reference.Path, reference.Globals);
                return count;
            }
            finally
            {
                if (project is not null) collection.UnloadProject(project);
            }
        }

        // The real path of a file through the cached real path of its directory: items share few directories.
        string Real(string path)
        {
            var parent = Path.GetDirectoryName(path);
            if (parent is null) return RealPath(path);
            if (!realDirectories.TryGetValue(parent, out var realParent))
                realDirectories[parent] = realParent = RealPath(parent);
            return Walk(realParent, [Path.GetFileName(path)]);
        }

        if (evaluated == 0)
            return Program.Fail($"{solution} names no MSBuild project");

        // Keep established item/import/glob diagnostics stable when those checks already deny.
        // The recording verdict remains independently decisive for otherwise-approved graphs.
        if (refusals.Count == 0)
            foreach (var refusal in fileSystem.Refusals) refusals.Add(refusal);

        foreach (var path in inputs.TrackedProjects.Where(path => !evaluatedProjects.Contains(path)).Order(StringComparer.Ordinal))
            output.WriteLine($"not-built: {Display(repo, path)}");

        if (refusals.Count > 0)
        {
            foreach (var refusal in refusals)
                output.WriteLine(refusal);
            return Program.Refused;
        }

        return Program.Approved;
    }

    private static Dictionary<string, string> ReferenceGlobals(Dictionary<string, string> globals, ProjectItem reference)
    {
        var result = new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase);
        // Let the referenced project choose its own frameworks; an explicit SetTargetFramework
        // below selects the requested one. Otherwise Evaluate visits all its inner evaluations.
        result.Remove("TargetFramework");
        foreach (var name in Project.GetMetadataValueEscaped(reference, "GlobalPropertiesToRemove")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.Remove(ProjectCollection.Unescape(name));
        foreach (var metadata in new[] { "SetConfiguration", "SetPlatform", "SetTargetFramework", "AdditionalProperties" })
        {
            foreach (var assignment in Project.GetMetadataValueEscaped(reference, metadata)
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var equals = assignment.IndexOf('=');
                if (equals <= 0)
                    throw new InvalidOperationException($"{reference.EvaluatedInclude}: invalid {metadata} assignment");
                result[ProjectCollection.Unescape(assignment[..equals].Trim())] =
                    ProjectCollection.Unescape(assignment[(equals + 1)..].Trim());
            }
        }
        return result;
    }

    /// <summary>
    /// The skippable path (D-3) the item reads, otherwise null. MSBuild reports a lexical FullPath, but the
    /// build opens the path through any links in it, so the lexical path, its real path and the real path
    /// of the include as written (where <c>link/..</c> leaves the link target) are all checked; a link
    /// match is shown under <paramref name="repo"/>.
    /// </summary>
    internal static string? SkippablePath(string repo, string realRepo, string fullPath, string rawPath, Func<string, string> real)
    {
        if (string.IsNullOrEmpty(fullPath)) return null;
        // MSBuild treats '\' as a separator on every OS; so does this rule (default-deny).
        if (Path.DirectorySeparatorChar == '/')
        {
            fullPath = fullPath.Replace('\\', '/');
            rawPath = rawPath.Replace('\\', '/');
        }
        var normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        return Skippable(repo, normalised, repo)
               ?? Skippable(realRepo, real(normalised), repo)
               ?? Skippable(realRepo, real(rawPath), repo);
    }

    private static string? Skippable(string root, string path, string display)
    {
        var readme = Path.Combine(root, "README.md");
        var docs = Path.Combine(root, "docs") + Path.DirectorySeparatorChar;
        var skippable = string.Equals(path, readme, StringComparison.OrdinalIgnoreCase)
                        || (path.StartsWith(docs, StringComparison.OrdinalIgnoreCase)
                            && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        return skippable ? Path.Join(display, path[root.Length..]) : null;
    }

    private const int MaxLinks = 40;

    /// <summary>
    /// realpath: walks an absolute path from its root and replaces every existing symbolic link or
    /// junction by its target before the next component, so <c>..</c> after a link leaves the target.
    /// Missing components are kept as written; one that cannot be read throws. Windows folds <c>..</c> lexically before the file system
    /// sees it, so there this only over-approximates (another candidate, never fewer refusals).
    /// </summary>
    internal static string RealPath(string path)
    {
        var combined = Path.Combine(Environment.CurrentDirectory, path);
        var root = Path.GetPathRoot(combined)!;
        return Walk(Path.GetFullPath(root), Components(combined[root.Length..]));
    }

    private static string Walk(string current, IEnumerable<string> components)
    {
        var pending = new List<string>(components);
        var links = 0;
        for (var i = 0; i < pending.Count; i++)
        {
            var component = pending[i];
            if (component is "" or ".") continue;
            if (component == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(next);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                current = next;
                continue;
            }

            // Ordinary components need no link-target handle. Keep the attribute read on
            // every walk so access failures still refuse and no directory state is cached.
            var target = (attributes & FileAttributes.ReparsePoint) != 0
                ? new FileInfo(next).LinkTarget
                : null;
            if (target is null)
            {
                current = next;
                continue;
            }

            if (++links > MaxLinks)
                throw new IOException($"{next}: more than {MaxLinks} links");
            // The target replaces the link and is walked itself: it may hold links or '..'.
            var resolved = Path.Combine(current, target);
            var root = Path.GetPathRoot(resolved)!;
            pending.RemoveRange(0, i + 1);
            pending.InsertRange(0, Components(resolved[root.Length..]));
            current = Path.GetFullPath(root);
            i = -1;
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static string[] Components(string relative) => relative.Split(['/', '\\']);

    private static string Display(string repo, string path)
    {
        var relative = Path.GetRelativePath(repo, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Any error MSBuild logs while evaluating is a failed evaluation (D-8), never an approval.</summary>
    private sealed class ErrorLogger : ILogger
    {
        private readonly List<string> _errors = [];

        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;
        public string? Parameters { get; set; }

        public void Initialize(IEventSource eventSource) =>
            eventSource.ErrorRaised += (_, e) => { lock (_errors) _errors.Add($"{e.File}({e.LineNumber}): {e.Code} {e.Message}"); };

        public void Shutdown() { }

        public void ThrowIfAny(string projectPath)
        {
            lock (_errors)
            {
                if (_errors.Count > 0)
                    throw new InvalidOperationException($"{projectPath}: {string.Join("; ", _errors)}");
            }
        }
    }
}
