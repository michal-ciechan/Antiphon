using System.Diagnostics;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Globbing;

namespace Antiphon.LandGraphAudit;

/// <summary>D-4 and D-7: inspect MSBuild's parsed globs and resolved imports, never today's glob matches.</summary>
internal sealed class BuildInputPolicy
{
    internal static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly string _repo;
    private readonly string _realRepo;
    private readonly string _sdk;
    private readonly string _packages;
    private readonly HashSet<string> _tracked;
    private readonly HashSet<string> _realTrackedNames;

    public BuildInputPolicy(string repo, string sdkDirectory)
    {
        _repo = repo;
        _realRepo = SolutionAudit.RealPath(repo);
        _sdk = SolutionAudit.RealPath(sdkDirectory);
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        _packages = SolutionAudit.RealPath(string.IsNullOrWhiteSpace(packages)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : Path.GetFullPath(packages, repo));
        var paths = TrackedFiles(repo);
        _tracked = paths.Select(p => Path.GetFullPath(Path.Combine(repo, p))).ToHashSet(PathComparer);
        // Keep the tracked names, not their resolved link targets: a tracked alias must not make
        // an untracked target trusted. Account for the repository root itself being a link.
        _realTrackedNames = paths.Select(p => Path.GetFullPath(Path.Combine(_realRepo, p))).ToHashSet(PathComparer);
    }

    public IEnumerable<string> TrackedProjects => _tracked.Where(p =>
        Path.GetExtension(p).EndsWith("proj", StringComparison.OrdinalIgnoreCase));

    public IReadOnlySet<string> TrackedPaths => _tracked;

    public void Check(Project project, ISet<string> refusals)
    {
        if (!TrustedDefinition(project.FullPath, allowDocs: true))
            refusals.Add($"refused: {Display(project.FullPath)} project outside trusted definition set");

        foreach (var import in project.Imports.Concat(project.ImportsIncludingDuplicates))
        {
            var text = import.ImportingElement.Project;
            var path = import.ImportedProject.FullPath;
            var owner = import.ImportingElement.ContainingProject.FullPath;
            // The installed SDK itself uses ImportAfter/* for its extension targets. That
            // exception requires BOTH ends inside the resolved SDK, never a repository target.
            var sdkExtension = Within(_sdk, SolutionAudit.RealPath(owner))
                               && Within(_sdk, SolutionAudit.RealPath(path));
            var wildcard = text.IndexOfAny(['*', '?']) >= 0 && !sdkExtension;
            if (wildcard || (!TrustedDefinition(path) && !SdkWorkloadManifest(import)))
            {
                refusals.Add($"refused: {Display(owner)} import {text} => {Display(path)} ({(wildcard ? "wildcard" : "outside trusted definition set")})");
            }
        }

        // An empty wildcard has no ResolvedImport. Inspect declarations in the evaluated
        // repository definition closure as well. SDK/package extension hooks are owned by
        // those trusted roots; actual resolved imports from them still pass the checks above.
        foreach (var root in project.ImportsIncludingDuplicates.Select(i => i.ImportedProject)
                     .Append(project.Xml).DistinctBy(r => r.FullPath, PathComparer)
                     .Where(r => Within(_repo, r.FullPath)))
        {
            foreach (var import in root.Imports)
            {
                if (import.Project.IndexOfAny(['*', '?']) >= 0)
                    refusals.Add($"refused: {Display(root.FullPath)} import {import.Project} (wildcard)");
            }
        }

        foreach (var result in project.GetAllGlobs())
        {
            // MsBuildGlob can be a composite or a glob with gaps. Only its positive branches
            // contribute: D-4 deliberately ignores every Exclude and Remove.
            foreach (var glob in Includes(result.MsBuildGlob))
            {
                if (!glob.IsLegal)
                    throw new InvalidOperationException($"{project.FullPath}: invalid glob {result.ItemElement.Include}");
                var fixedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(glob.FixedDirectoryPart));
                if (!ReachesDocs(_repo, fixedDirectory, glob)
                    && !ReachesDocs(_realRepo, SolutionAudit.RealPath(fixedDirectory), glob)) continue;
                refusals.Add($"refused: {Display(result.ItemElement.ContainingProject.FullPath)} glob {result.ItemElement.Include} ({glob.FixedDirectoryPart}{glob.WildcardDirectoryPart}{glob.FilenamePart})");
            }
        }
    }

    private bool SdkWorkloadManifest(ResolvedImport import)
    {
        // Workload manifests ship next to sdk/, not beneath sdk/<version>/. Admit only
        // the SDK's known workload-locator import and its manifest file in that installation.
        var owner = Path.Combine(_sdk, "Sdks", "Microsoft.NET.Sdk", "targets", "Microsoft.NET.Sdk.ImportWorkloads.targets");
        var manifests = SolutionAudit.RealPath(Path.Combine(_sdk, "..", "..", "sdk-manifests"));
        var target = SolutionAudit.RealPath(import.ImportedProject.FullPath);
        return PathComparer.Equals(SolutionAudit.RealPath(import.ImportingElement.ContainingProject.FullPath), owner)
               && import.ImportingElement.Sdk == "Microsoft.NET.SDK.WorkloadManifestTargetsLocator"
               && import.ImportingElement.Project == "WorkloadManifest.targets"
               && Path.GetFileName(target) == "WorkloadManifest.targets"
               && Within(manifests, target);
    }

    private bool TrustedDefinition(string path, bool allowDocs = false)
    {
        path = Path.GetFullPath(path);
        var real = SolutionAudit.RealPath(path);
        // Even a tracked .props under docs is disallowed as an import (D-7).
        if (!allowDocs && (Within(Path.Combine(_repo, "docs"), path) || Within(Path.Combine(_realRepo, "docs"), real)))
            return false;
        if (Within(_sdk, real) || Within(_packages, real)) return true;
        return _tracked.Contains(path) && Definition(path)
               && _realTrackedNames.Contains(real) && Definition(real);
    }

    private static bool ReachesDocs(string repo, string fixedDirectory, MSBuildGlob glob)
    {
        if (!CouldNameMarkdown(glob.FilenamePart)) return false;
        var docs = Path.Combine(repo, "docs");
        return Within(docs, fixedDirectory)
               || (Within(fixedDirectory, docs) && glob.WildcardDirectoryPart.Length > 0)
               || Within(fixedDirectory, repo); // root README, conservatively using the same filename rule
    }

    private static bool CouldNameMarkdown(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length == 0 || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                                     || extension.IndexOfAny(['*', '?']) >= 0;
    }

    private static IEnumerable<MSBuildGlob> Includes(IMSBuildGlob glob)
    {
        switch (glob)
        {
            case MSBuildGlob parsed:
                yield return parsed;
                break;
            case CompositeGlob composite:
                foreach (var child in composite.Globs)
                    foreach (var part in Includes(child)) yield return part;
                break;
            case MSBuildGlobWithGaps gaps:
                foreach (var part in Includes(gaps.MainGlob)) yield return part;
                break;
            default:
                throw new InvalidOperationException($"unsupported MSBuild glob {glob.GetType().FullName}");
        }
    }

    private static bool Within(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    // Same definition inventory as LandVerificationBuildGraph.Includes; do not trust an arbitrary
    // tracked XML/text file merely because MSBuild successfully parsed it as a project.
    internal static bool Definition(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension.EndsWith("proj", StringComparison.Ordinal)
               || extension is ".props" or ".targets" or ".sln" or ".slnx" or ".slnf" or ".rsp"
               || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith("proj.user", StringComparison.OrdinalIgnoreCase)
               || name.ToLowerInvariant() is "global.json" or "nuget.config" or "packages.lock.json"
                   or "packages.config" or "directory.packages.props";
    }

    private string Display(string path) => Path.GetRelativePath(_repo, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string[] TrackedFiles(string repo)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "ls-files", "--cached", "-z" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("git tracked-file inventory timed out");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git tracked-file inventory failed: {stderr.GetAwaiter().GetResult()}");
        return stdout.GetAwaiter().GetResult().Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
