using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.LiveLane;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S21 (the owner, 2026-11-26: "the source files for this digest should all be in an isolated project dedicated
/// to running real Claude"). The live lane's C# lives in scripts/lib/Antiphon.LiveLane, which references no project or
/// package outside an allow-list (empty: the framework only), and the lane's contractFiles in tests/live-lane-inputs.json
/// are exactly that project's input set: its directory, the repository build files that apply to it, and the lane
/// manifests (S22 moved the receipt, attestation, fingerprint and status PowerShell into the project and deleted it). No
/// contract pattern covers Antiphon.Nightly or Antiphon.Scripts, so the lane contract moves only when live-lane code or its
/// build moves.
/// </summary>
[Category("Unit")]
public sealed class LiveLaneProjectIsolationTests
{
    private const string Lane = "live-claude-core-windows";
    private const string ProjectDirectory = "scripts/lib/Antiphon.LiveLane";
    private const string ProjectFile = ProjectDirectory + "/Antiphon.LiveLane.csproj";
    private static readonly string Repo = DelegateScriptRunner.RepoRoot;

    // The references the project may carry. Empty: anything it needs is copied in (LiveLaneValues, LiveLaneArgs, the
    // build-driver policy) or bound by the CLI through a seam (ILiveLaneNativeLock, LiveLaneCensusReader).
    private static readonly string[] AllowedProjectReferences = [];
    private static readonly string[] AllowedPackageReferences = [];

    // The lane manifests the declaration always lists.
    private static readonly string[] LaneManifests = ["tests/live-lanes.json", "tests/live-lane-inputs.json"];

    // The repository files MSBuild reads for a project from its directory upwards. NuGet.config and
    // Directory.Packages.props do not exist in this repository today; a new one on the path joins the input set.
    private static readonly string[] AncestorBuildFiles =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp", "Directory.Packages.props", "global.json", "NuGet.config", "nuget.config", "NuGet.Config", ".editorconfig"];

    [Test]
    public void Project_references_nothing_outside_the_allow_list()
    {
        var project = XDocument.Load(Path.Combine(Repo, ProjectFile));
        project.Root!.Attribute("Sdk")?.Value.ShouldBe("Microsoft.NET.Sdk");
        Items(project, "ProjectReference").Where(r => !AllowedProjectReferences.Contains(r)).ShouldBeEmpty("ProjectReference outside the allow-list");
        Items(project, "PackageReference").Where(r => !AllowedPackageReferences.Contains(r)).ShouldBeEmpty("PackageReference outside the allow-list");
        project.Descendants().Where(e => e.Name.LocalName == "Import").ShouldBeEmpty("an explicit Import");
        // Every source is the SDK's default glob of the project's own directory: no item reaches outside it.
        foreach (var kind in new[] { "Compile", "None", "Content", "EmbeddedResource", "AdditionalFiles", "Analyzer", "Reference" })
            Items(project, kind).ShouldBeEmpty(kind + " item");

        // The built assembly binds the framework only.
        var references = typeof(LiveLaneRun).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        references.Where(name => !(name == "netstandard" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal))).ShouldBeEmpty("non-framework assembly reference");

        // The dependency goes Scripts -> LiveLane, never the reverse, and the nightly core does not reference the lane.
        Items(XDocument.Load(Path.Combine(Repo, "scripts/lib/Antiphon.Scripts/Antiphon.Scripts.csproj")), "ProjectReference")
            .ShouldContain("../Antiphon.LiveLane/Antiphon.LiveLane.csproj");
        Items(XDocument.Load(Path.Combine(Repo, "scripts/lib/Antiphon.Nightly/Antiphon.Nightly.csproj")), "ProjectReference")
            .ShouldNotContain(r => r.Contains("Antiphon.LiveLane", StringComparison.Ordinal));
    }

    [Test]
    public void Contract_files_are_the_project_input_set()
    {
        var declaration = JsonNode.Parse(File.ReadAllText(Path.Combine(Repo, "tests/live-lane-inputs.json")))!;
        var contract = declaration["lanes"]![Lane]!["contractFiles"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        contract.Distinct(StringComparer.Ordinal).Count().ShouldBe(contract.Length, "a repeated contract entry");

        var inputs = ProjectInputs();
        inputs.ShouldContain(ProjectDirectory + "/**");
        inputs.ShouldContain("tests/Shared/CheckpointRepeat.targets", "Directory.Build.targets imports it");
        var expected = LaneManifests.Concat(inputs).Order(StringComparer.Ordinal).ToArray();
        contract.Order(StringComparer.Ordinal).ToArray().ShouldBe(expected);

        // Each pattern names files that exist (the fingerprint's declared-root-empty), and none covers the nightly core or
        // the CLI.
        var files = Directory.EnumerateFiles(Path.Combine(Repo, "scripts"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Repo, "tests", "Shared")))
            .Concat(Directory.EnumerateFiles(Repo))
            .Concat(Directory.EnumerateFiles(Path.Combine(Repo, "tests"), "live-lane*.json"))
            .Select(f => Path.GetRelativePath(Repo, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').Any(part => part is "bin" or "obj" || part.StartsWith("bin-", StringComparison.Ordinal)))
            .ToArray();
        foreach (var pattern in contract)
        {
            var glob = Glob(pattern);
            files.Any(f => glob.IsMatch(f)).ShouldBeTrue("contract pattern " + pattern + " matches no file");
            files.Where(f => f.StartsWith("scripts/lib/Antiphon.Nightly/", StringComparison.Ordinal) || f.StartsWith("scripts/lib/Antiphon.Scripts/", StringComparison.Ordinal))
                .Where(f => glob.IsMatch(f)).ShouldBeEmpty("contract pattern " + pattern + " covers the nightly core or the CLI");
        }
        // Every source the project builds is under its directory, so the one directory pattern covers them all.
        Directory.EnumerateFiles(Path.Combine(Repo, ProjectDirectory), "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Repo, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal))
            .ShouldAllBe(f => Glob(ProjectDirectory + "/**").IsMatch(f));
    }

    /// <summary>
    /// The project's input set: its directory, the ancestor build files MSBuild and the compiler read, and the files those
    /// import (resolved from $(MSBuildThisFileDirectory)), as repository-relative paths. CheckpointRepeat.targets compiles
    /// tests/Shared/CheckpointRepeatEvidenceAttribute.cs only into a selected TUnit project, and errors without TUnit, so
    /// that file cannot change this project's output and is not an input.
    /// </summary>
    private static string[] ProjectInputs()
    {
        var inputs = new SortedSet<string>(StringComparer.Ordinal) { ProjectDirectory + "/**" };
        var queue = new Queue<string>();
        var directory = new DirectoryInfo(Path.Combine(Repo, ProjectDirectory));
        while (directory != null)
        {
            foreach (var name in AncestorBuildFiles)
            {
                var path = Path.Combine(directory.FullName, name);
                if (File.Exists(path) && inputs.Add(Path.GetRelativePath(Repo, path).Replace('\\', '/'))) queue.Enqueue(path);
            }
            if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), Repo.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
            directory = directory.Parent;
        }
        while (queue.Count > 0)
        {
            var file = queue.Dequeue();
            if (!file.EndsWith(".props", StringComparison.Ordinal) && !file.EndsWith(".targets", StringComparison.Ordinal)) continue;
            foreach (var import in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "Import"))
            {
                var target = (import.Attribute("Project")?.Value ?? "").Replace("$(MSBuildThisFileDirectory)", Path.GetDirectoryName(file) + Path.DirectorySeparatorChar);
                if (target.Contains("$(", StringComparison.Ordinal)) continue;
                var full = Path.GetFullPath(target);
                if (File.Exists(full) && inputs.Add(Path.GetRelativePath(Repo, full).Replace('\\', '/'))) queue.Enqueue(full);
            }
        }
        return inputs.ToArray();
    }

    private static IEnumerable<string> Items(XDocument project, string kind) =>
        project.Descendants().Where(e => e.Name.LocalName == kind).Select(e => e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? "");

    /// <summary>The live-lane fingerprint's glob (ConvertTo-LiveLaneGlobRegex): ** crosses directories, * and ? do not.</summary>
    private static Regex Glob(string pattern)
    {
        var builder = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*') { builder.Append(".*"); i++; }
            else if (ch == '*') builder.Append("[^/]*");
            else if (ch == '?') builder.Append("[^/]");
            else builder.Append(Regex.Escape(ch.ToString()));
        }
        return new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant);
    }
}
