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
    [Arguments("IntermediateOutputPath", false)]
    [Arguments("MSBuildProjectExtensionsPath", false)]
    [Arguments("MSBuildProjectExtensionsPath", true)]
    [Arguments("PublishDir", false)]
    public async Task HistoricalProjectEvidenceDestinationsAreRefusedBeforeRestore(string property, bool global)
    {
        using var repo = new ScratchGitRepo("disk-placement");
        var protectedPath = "/work/evidence/placement-witness-" + Guid.NewGuid().ToString("N") + "/";
        await repo.CommitFileAsync("sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        if (!global)
            await repo.CommitFileAsync("Directory.Build.props",
                $"<Project><PropertyGroup><{property}>{protectedPath}</{property}></PropertyGroup></Project>");
        File.Exists(Path.Combine(repo.Path, "Directory.Build.targets")).ShouldBeFalse("historical checkout has no policy");

        var prepared = BuildConfinement.Prepare(repo.Path,
            BuildStep.BuildArguments("sample.csproj", "bin-placement/",
                global ? [$"--property:{property}={protectedPath}"] : [], 2), null, CancellationToken.None);

        prepared.Detail.ShouldContain("workspace_evidence_write");
        prepared.Detail.ShouldContain(property);
        prepared.Arguments.ShouldBeEmpty("no restore/build argv leaves the preflight");
        Directory.Exists(protectedPath).ShouldBeFalse();
        Directory.Exists(Path.Combine(repo.Path, "obj")).ShouldBeFalse("MSBuild only evaluated the project");
    }
}
