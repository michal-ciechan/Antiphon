using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    // CARD-1780: a solution build unsets Configuration and Platform on a ProjectReference
    // whose target is not in the solution; neither Set* nor AdditionalProperties restores them.
    // An in-solution reference takes the solution mapping over Set*, AdditionalProperties
    // overrides that mapping, and GlobalPropertiesToRemove is applied last.
    [Test]
    [Arguments("outside", "configuration=unset platform=unset")]
    [Arguments("member", "")]
    [Arguments("set-configuration", "configuration=unset platform=unset")]
    [Arguments("additional-properties", "configuration=unset platform=unset")]
    [Arguments("member-set-configuration", "")]
    [Arguments("remove-configuration", "")]
    [Arguments("member-additional-properties", "")]
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
            // Solution mapping is Debug|Any CPU. SetConfiguration=Release must not survive it.
            "member-set-configuration" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <SetConfiguration>Configuration=Release</SetConfiguration>
                  </ProjectReference>
                </ItemGroup>
                """,
            // Solution mapping is Debug|Any CPU. AdditionalProperties=Release is applied after it.
            "member-additional-properties" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <AdditionalProperties>Configuration=Release</AdditionalProperties>
                  </ProjectReference>
                </ItemGroup>
                """,
            // GlobalPropertiesToRemove is applied after the solution mapping.
            "remove-configuration" => """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <GlobalPropertiesToRemove>Configuration</GlobalPropertiesToRemove>
                  </ProjectReference>
                </ItemGroup>
                """,
            _ => """
                <ItemGroup><ProjectReference Include="../Outside/Outside.csproj" /></ItemGroup>
                """,
        };
        var solution = variant is "member" or "member-set-configuration" or "remove-configuration" or "member-additional-properties"
            ? new[] { "App/App.csproj", "Outside/Outside.csproj" }
            : new[] { "App/App.csproj" };
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["App/App.csproj"] = SdkProject(reference),
            ["Outside/Outside.csproj"] = SdkProject(""),
        }, solutionProjects: solution);
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(0, run.Text);
        run.Lines.ShouldContain("approved", run.Text);
        const string prefix = "outside-solution: Outside/Outside.csproj ";
        if (expectedFlag.Length == 0)
            run.Lines.ShouldNotContain(line => line.StartsWith("outside-solution:", StringComparison.Ordinal), run.Text);
        else
            run.Lines.ShouldContain(prefix + expectedFlag, run.Text);
        if (variant == "member")
            run.Lines.ShouldNotContain(line => line.StartsWith("member-configuration:", StringComparison.Ordinal), run.Text);
        if (variant == "member-set-configuration")
            run.Lines.ShouldContain("member-configuration: Outside/Outside.csproj configuration=Debug platform=AnyCPU", run.Text);
        if (variant == "member-additional-properties")
            run.Lines.ShouldContain("member-configuration: Outside/Outside.csproj configuration=Release platform=AnyCPU", run.Text);
        if (variant == "remove-configuration")
            run.Lines.ShouldContain("member-configuration: Outside/Outside.csproj configuration=unset platform=AnyCPU", run.Text);
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
