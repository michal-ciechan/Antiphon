using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1229: no PackageReference, PackageVersion or GlobalPackageReference in a .csproj, .props
/// or .targets file has a Version or VersionOverride (attribute or child element) containing a
/// literal <c>*</c>. A floating version (<c>9.*</c>, <c>9.0.*</c>, <c>4.*</c>) made restore depend
/// on the feed's publication timing: nuget.org listed Microsoft.Extensions.Logging.Console 9.0.21
/// before Microsoft.Extensions.Options.ConfigurationExtensions 9.0.21, the transitive floor then
/// resolved 10.0.0, and server-project restore failed with NU1605.
/// This is not an exact-version check. A bracketed range without a star passes (Confluent.Kafka
/// <c>[2.6.0,3.0.0)</c> resolves to its lowest applicable version, 2.6.0, while that is on the
/// feed), and MSBuild properties are read as raw text, not evaluated: <c>$(Name)</c> passes unless
/// its own text has a star, so a property defined as <c>9.*</c> is not caught. The boundary and the
/// deliberate bump procedure are in docs/testing-and-build.md, "NuGet version pins (CARD-1229)".
/// </summary>
[Category("Unit")]
public sealed class PackageVersionPinContractTests
{
    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules", ".git", ".antiphon", "workspace"];

    [Test]
    public void No_project_props_or_targets_file_uses_a_floating_package_version()
    {
        var files = MsBuildFiles(DockerStackDocuments.RepoRoot).ToList();
        files.Count.ShouldBeGreaterThan(20, "the scan must see the repo's project files");

        var floating = files
            .SelectMany(f => FloatingVersions(
                File.ReadAllText(f),
                Path.GetRelativePath(DockerStackDocuments.RepoRoot, f).Replace('\\', '/')))
            .ToList();

        floating.ShouldBeEmpty("pin an exact version; see docs/testing-and-build.md \"NuGet version pins (CARD-1229)\"");
    }

    [Test]
    public void Detector_flags_star_versions_in_attributes_overrides_and_child_elements()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Exact" Version="9.0.21" />
                <PackageReference Include="Range" Version="[2.6.0,3.0.0)" />
                <PackageReference Include="Major" Version="9.*" />
                <PackageReference Include="Patch" Version="9.0.*" />
                <PackageReference Include="Any" Version="*" />
                <PackageReference Include="Override" VersionOverride="4.*" />
                <PackageReference Include="Child"><Version>1.*</Version></PackageReference>
                <PackageVersion Include="Central" Version="10.*" />
                <GlobalPackageReference Include="Global" Version="1.0.*" />
              </ItemGroup>
            </Project>
            """;

        FloatingVersions(project, "sample.csproj").ShouldBe(
        [
            "sample.csproj: PackageReference Major Version=9.*",
            "sample.csproj: PackageReference Patch Version=9.0.*",
            "sample.csproj: PackageReference Any Version=*",
            "sample.csproj: PackageReference Override VersionOverride=4.*",
            "sample.csproj: PackageReference Child Version=1.*",
            "sample.csproj: PackageVersion Central Version=10.*",
            "sample.csproj: GlobalPackageReference Global Version=1.0.*",
        ]);
    }

    internal static IEnumerable<string> FloatingVersions(string xml, string relativePath)
    {
        var references = XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion" or "GlobalPackageReference");
        foreach (var reference in references)
        {
            var id = (string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update") ?? "?";
            var versions = reference.Attributes()
                .Where(a => a.Name.LocalName is "Version" or "VersionOverride")
                .Select(a => (a.Name.LocalName, a.Value))
                .Concat(reference.Elements()
                    .Where(c => c.Name.LocalName is "Version" or "VersionOverride")
                    .Select(c => (c.Name.LocalName, c.Value)));
            foreach (var (name, value) in versions)
            {
                if (value.Contains('*'))
                    yield return $"{relativePath}: {reference.Name.LocalName} {id} {name}={value.Trim()}";
            }
        }
    }

    private static IEnumerable<string> MsBuildFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Path.GetExtension(file) is ".csproj" or ".props" or ".targets")
                yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var file in MsBuildFiles(child))
                yield return file;
        }
    }
}
