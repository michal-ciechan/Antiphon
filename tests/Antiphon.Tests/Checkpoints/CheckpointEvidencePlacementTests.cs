using System.Xml.Linq;
using Antiphon.Checkpoints;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[NativeLinuxOnly("The server2 evidence root policy applies to Linux paths.")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointEvidencePlacementTests
{
    [Test]
    public async Task ReferenceSpecificPropertiesAreCheckedForEveryOccurrenceOfTheSameProject()
    {
        using var repo = new ScratchGitRepo("disk-reference-placement");
        var protectedPath = "/work/evidence/reference-witness-" + Guid.NewGuid().ToString("N") + "/";
        await repo.CommitFileAsync("child/Child.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <IntermediateOutputPath Condition="'$(PlacementVariant)' == 'unsafe'">REPLACE</IntermediateOutputPath>
              </PropertyGroup>
            </Project>
            """.Replace("REPLACE", protectedPath, StringComparison.Ordinal));
        await repo.CommitFileAsync("Main.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="child/Child.csproj" AdditionalProperties="PlacementVariant=safe" />
                <ProjectReference Include="child/Child.csproj" AdditionalProperties="PlacementVariant=unsafe" />
              </ItemGroup>
            </Project>
            """);

        var prepared = BuildConfinement.Prepare(repo.Path,
            BuildStep.BuildArguments("Main.csproj", "bin-placement/", [], 2), null, CancellationToken.None);

        prepared.Detail.ShouldContain("workspace_evidence_write");
        prepared.Detail.ShouldContain("IntermediateOutputPath");
        prepared.Arguments.ShouldBeEmpty();
        Directory.Exists(protectedPath).ShouldBeFalse();
        Directory.Exists(Path.Combine(repo.Path, "child", "obj")).ShouldBeFalse();
    }

    [Test]
    [Arguments("IntermediateOutputPath", false)]
    [Arguments("MSBuildProjectExtensionsPath", false)]
    [Arguments("MSBuildProjectExtensionsPath", true)]
    [Arguments("PublishDir", false)]
    public async Task HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore(string property, bool global)
    {
        using var repo = new ScratchGitRepo("disk-placement");
        var protectedPath = "/work/evidence/placement-witness-" + Guid.NewGuid().ToString("N") + "/";
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net9.0")));
        await repo.CommitFileAsync("sample.csproj", project.ToString());
        if (!global)
            await repo.CommitFileAsync("Directory.Build.props",
                new XElement("Project", new XElement("PropertyGroup", new XElement(property, protectedPath))).ToString());
        File.Exists(Path.Combine(repo.Path, "Directory.Build.targets")).ShouldBeFalse("historical checkout has no policy");

        var prepared = BuildConfinement.Prepare(repo.Path,
            BuildStep.BuildArguments("sample.csproj", "bin-placement/",
                global ? ["--property:" + property + "=" + protectedPath] : [], 2), null, CancellationToken.None);

        prepared.Detail.ShouldContain("workspace_evidence_write");
        prepared.Detail.ShouldContain(property);
        prepared.Arguments.ShouldBeEmpty("no restore/build argv leaves the preflight");
        Directory.Exists(protectedPath).ShouldBeFalse();
        Directory.Exists(Path.Combine(repo.Path, "obj")).ShouldBeFalse("MSBuild only evaluated the project");
    }
}
