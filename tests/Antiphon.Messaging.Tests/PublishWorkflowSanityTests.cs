using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// File-pin of CARD-0150 S6: every <c>dotnet pack</c> line in publish-nuget.yml must
/// actually produce a nupkg (the FakeGateway silent-no-op was Sdk.Web with no
/// IsPackable), and the nuget.org push list must be a closed ProjectReference set.
/// </summary>
public sealed class PublishWorkflowSanityTests
{
    private static readonly Regex PackLine = new(
        @"dotnet pack\s+(?<path>src[/\\][^\s]+\.csproj)",
        RegexOptions.CultureInvariant);

    private static readonly Regex NugetOrgIdLoop = new(
        @"for id in (?<ids>[^;]+);",
        RegexOptions.CultureInvariant);

    private static readonly Regex WebSdk = new(
        @"Sdk\s*=\s*[""']Microsoft\.NET\.Sdk\.Web[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IsPackableTrue = new(
        @"<IsPackable>\s*true\s*</IsPackable>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IsPackableFalse = new(
        @"<IsPackable>\s*false\s*</IsPackable>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Test]
    public void Packed_projects_are_actually_packable()
    {
        var yaml = File.ReadAllText(WorkflowPath);
        var packPaths = PackLine.Matches(yaml).Select(m => m.Groups["path"].Value.Replace('\\', '/')).ToArray();
        packPaths.ShouldNotBeEmpty("publish-nuget.yml must pack at least one project");

        foreach (var relative in packPaths)
        {
            var csprojPath = Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(csprojPath).ShouldBeTrue($"packed project does not exist: {relative}");

            var csproj = File.ReadAllText(csprojPath);
            IsPackableFalse.IsMatch(csproj).ShouldBeFalse(
                $"{relative} is packed but IsPackable=false — dotnet pack would be a silent no-op");
            if (WebSdk.IsMatch(csproj))
            {
                IsPackableTrue.IsMatch(csproj).ShouldBeTrue(
                    $"{relative} uses Microsoft.NET.Sdk.Web without <IsPackable>true</IsPackable> — " +
                    "the FakeGateway silent-no-op shape. Either set IsPackable or drop the pack line.");
            }
        }
    }

    [Test]
    public void Nuget_org_push_list_is_a_closed_project_reference_set()
    {
        var yaml = File.ReadAllText(WorkflowPath);
        var packPaths = PackLine.Matches(yaml)
            .Select(m => m.Groups["path"].Value.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        packPaths.ShouldNotBeEmpty();

        var idMatch = NugetOrgIdLoop.Match(yaml);
        idMatch.Success.ShouldBeTrue(
            "publish-nuget.yml must push nuget.org by an explicit `for id in …;` list, never a wildcard");
        var nugetOrgIds = idMatch.Groups["ids"].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        nugetOrgIds.ShouldNotBeEmpty();

        var packedById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packedFullPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in packPaths)
        {
            var full = Path.GetFullPath(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var id = ReadPackageId(full);
            packedById[id] = full;
            packedFullPaths[full] = id;
        }

        foreach (var id in nugetOrgIds)
        {
            packedById.ContainsKey(id).ShouldBeTrue(
                $"nuget.org push id '{id}' is not in the pack list — push of a missing nupkg would fail, " +
                "or a wildcard would have leaked a GH-only package");
        }

        foreach (var id in nugetOrgIds)
        {
            var csprojPath = packedById[id];
            foreach (var referenced in ReadPackedProjectReferences(csprojPath, packedFullPaths))
            {
                nugetOrgIds.ShouldContain(referenced,
                    $"nuget.org package '{id}' ProjectReferences packed '{referenced}', " +
                    "which is not on the nuget.org push list — a nuget.org-only restore would fail");
            }
        }
    }

    private static string ReadPackageId(string csprojPath)
    {
        var doc = XDocument.Parse(File.ReadAllText(csprojPath));
        var packageId = ElementValue(doc, "PackageId");
        if (!string.IsNullOrWhiteSpace(packageId))
            return packageId;
        var assemblyName = ElementValue(doc, "AssemblyName");
        if (!string.IsNullOrWhiteSpace(assemblyName))
            return assemblyName;
        return Path.GetFileNameWithoutExtension(csprojPath);
    }

    private static IEnumerable<string> ReadPackedProjectReferences(
        string csprojPath,
        IReadOnlyDictionary<string, string> packedFullPaths)
    {
        var doc = XDocument.Parse(File.ReadAllText(csprojPath));
        var dir = Path.GetDirectoryName(csprojPath)!;
        foreach (var include in doc.Descendants("ProjectReference")
                     .Select(e => (string?)e.Attribute("Include"))
                     .Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var resolved = Path.GetFullPath(Path.Combine(dir, include!));
            if (packedFullPaths.TryGetValue(resolved, out var id))
                yield return id;
        }
    }

    private static string? ElementValue(XDocument doc, string name) =>
        doc.Descendants(name).Select(e => e.Value?.Trim()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string WorkflowPath
    {
        get
        {
            var path = Path.Combine(RepoRoot, ".github", "workflows", "publish-nuget.yml");
            File.Exists(path).ShouldBeTrue("could not find .github/workflows/publish-nuget.yml");
            return path;
        }
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
