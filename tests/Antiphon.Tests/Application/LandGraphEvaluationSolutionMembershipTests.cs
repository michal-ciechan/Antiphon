using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    // CARD-1780: a solution build unsets Configuration and Platform on a ProjectReference
    // whose target is not in the solution. Set* and AdditionalProperties are applied first
    // and do not restore them. An in-solution reference keeps the solution mapping, and
    // GlobalPropertiesToRemove is applied last.
    [Test]
    [Arguments("outside", "configuration=unset platform=unset")]
    [Arguments("member", "")]
    [Arguments("set-configuration", "configuration=unset platform=unset")]
    [Arguments("additional-properties", "configuration=unset platform=unset")]
    [Arguments("member-set-configuration", "")]
    [Arguments("remove-after-properties", "configuration=unset platform=unset")]
    [Timeout(300_000)]
    public async Task C1780_OutsideSolutionReferenceClearsParentConfiguration(string variant, string expectedFlag)
    {
        var reference = variant switch
        {
            "set-configuration" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <SetConfiguration>Configuration=Debug</SetConfiguration>
                  </ProjectReference>
                </ItemGroup>
                """,
            "additional-properties" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <AdditionalProperties>Configuration=Release</AdditionalProperties>
                  </ProjectReference>
                </ItemGroup>
                """,
            // Solution mapping is Debug. A Release-only docs item is included only when
            // SetConfiguration survives that mapping.
            "member-set-configuration" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <SetConfiguration>Configuration=Release</SetConfiguration>
                  </ProjectReference>
                </ItemGroup>
                """,
            // The marker item is included only when AdditionalProperties survives GlobalPropertiesToRemove.
            "remove-after-properties" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <AdditionalProperties>CustomMarker=1</AdditionalProperties>
                    <GlobalPropertiesToRemove>CustomMarker</GlobalPropertiesToRemove>
                  </ProjectReference>
                </ItemGroup>
                """,
            _ => """
                <ItemGroup><ProjectReference Include="../Outside/Outside.csproj" /></ItemGroup>
                """,
        };
        var outside = variant switch
        {
            "member-set-configuration" => """
                <ItemGroup>
                  <None Include="../docs/release-only.md" Condition="'$(Configuration)' == 'Release'" />
                </ItemGroup>
                """,
            "remove-after-properties" => """
                <ItemGroup>
                  <None Include="../docs/marker.md" Condition="'$(CustomMarker)' == '1'" />
                </ItemGroup>
                """,
            _ => "",
        };
        var solution = variant is "member" or "member-set-configuration"
            ? new[] { "App/App.csproj", "Outside/Outside.csproj" }
            : new[] { "App/App.csproj" };
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject(reference),
            ["Outside/Outside.csproj"] = SdkProject(outside),
        }, solutionProjects: solution);
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(0, run.Text);
        run.Lines.ShouldContain("approved", run.Text);
        const string prefix = "outside-solution: Outside/Outside.csproj ";
        if (expectedFlag.Length == 0)
            run.Lines.ShouldNotContain(line => line.StartsWith("outside-solution:", StringComparison.Ordinal), run.Text);
        else
            run.Lines.ShouldContain(prefix + expectedFlag, run.Text);
        repo.AssertUntouched();
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1780_RealRepositoryFlagsReferencesOutsideTheSolution()
    {
        var run = await AuditRepo.RunToolAtAsync(RepoRoot());
        run.ExitCode.ShouldBe(0, run.Text);
        var flags = run.Lines.Where(line => line.StartsWith("outside-solution:", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        flags.ShouldBe(new[]
        {
            // Plain ProjectReference from Antiphon.Tests; not a StagedHelper, so nothing calls GetTargetPath with Configuration=$(Configuration).
            "outside-solution: tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj configuration=unset platform=unset",
            // Plain ProjectReference from LandGraphAudit; not a StagedHelper, so a Release solution build leaves it at its own Debug output.
            "outside-solution: tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj configuration=unset platform=unset",
            // Plain ProjectReference from Antiphon.Tests. TimeoutAudit.Tests is outside the solution closure. Not a StagedHelper.
            "outside-solution: tools/Antiphon.TimeoutAudit/Antiphon.TimeoutAudit.csproj configuration=unset platform=unset",
        });
    }
}
