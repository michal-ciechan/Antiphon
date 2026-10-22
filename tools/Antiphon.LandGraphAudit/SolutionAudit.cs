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

    public static int Run(string repo, string solution, TextWriter output)
    {
        var solutionPath = Path.GetFullPath(Path.Combine(repo, solution));
        var parsed = SolutionFile.Parse(solutionPath);
        var configuration = parsed.SolutionConfigurations.FirstOrDefault(c =>
                c.ConfigurationName == SolutionConfiguration && c.PlatformName == SolutionPlatform)
            ?? parsed.SolutionConfigurations.FirstOrDefault(c => c.ConfigurationName == SolutionConfiguration);

        // ArtifactsPath points at a directory that is never created: evaluation then finds no
        // restore output (D-12) and nothing under the repository's obj/ is read or written.
        var scratch = Path.Combine(Path.GetTempPath(), "antiphon-landgraph-" + Guid.NewGuid().ToString("N"));
        var errors = new ErrorLogger();
        using var collection = new ProjectCollection(
            new Dictionary<string, string>(), [errors], ToolsetDefinitionLocations.Default);
        var context = EvaluationContext.Create(EvaluationContext.SharingPolicy.Shared);
        var refusals = new SortedSet<string>(StringComparer.Ordinal);
        var evaluated = 0;

        foreach (var entry in parsed.ProjectsInOrder.Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat))
        {
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
            var project = Project.FromFile(projectPath, new ProjectOptions
            {
                GlobalProperties = globals,
                ProjectCollection = collection,
                EvaluationContext = context,
                LoadSettings = ProjectLoadSettings.Default,
            });
            try
            {
                errors.ThrowIfAny(projectPath);
                foreach (var item in project.AllEvaluatedItems)
                {
                    var fullPath = SkippablePath(repo, item.GetMetadataValue("FullPath"));
                    if (fullPath is null) continue;
                    var definedIn = item.Xml?.ContainingProject?.FullPath ?? project.FullPath;
                    refusals.Add($"refused: {Display(repo, definedIn)} {item.ItemType} {fullPath}");
                }

                // A multi-targeting outer evaluation hides items conditioned on one framework.
                var frameworks = project.GetPropertyValue("TargetFramework").Length == 0
                    ? project.GetPropertyValue("TargetFrameworks")
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];
                collection.UnloadProject(project);
                project = null;
                var count = 1;
                foreach (var framework in frameworks.Distinct(StringComparer.OrdinalIgnoreCase))
                    count += Evaluate(projectPath, new(globals, StringComparer.OrdinalIgnoreCase) { ["TargetFramework"] = framework });
                return count;
            }
            finally
            {
                if (project is not null) collection.UnloadProject(project);
            }
        }

        if (evaluated == 0)
            return Program.Fail($"{solution} names no MSBuild project");

        if (refusals.Count > 0)
        {
            foreach (var refusal in refusals)
                output.WriteLine(refusal);
            return Program.Refused;
        }

        output.WriteLine("approved");
        return Program.Approved;
    }

    /// <summary>The normalised path when it is skippable (D-3), otherwise null.</summary>
    internal static string? SkippablePath(string repo, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return null;
        // MSBuild treats '\' as a separator on every OS; so does this rule (default-deny).
        if (Path.DirectorySeparatorChar == '/')
            fullPath = fullPath.Replace('\\', '/');
        var normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        var readme = Path.Combine(repo, "README.md");
        var docs = Path.Combine(repo, "docs") + Path.DirectorySeparatorChar;
        if (string.Equals(normalised, readme, StringComparison.OrdinalIgnoreCase))
            return normalised;
        return normalised.StartsWith(docs, StringComparison.OrdinalIgnoreCase)
               && normalised.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalised
            : null;
    }

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
