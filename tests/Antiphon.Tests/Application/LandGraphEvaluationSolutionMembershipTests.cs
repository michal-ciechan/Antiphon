using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    // CARD-1780: a solution build unsets Configuration and Platform on a ProjectReference
    // whose target is not in the solution. An explicit SetConfiguration still applies after that.
    [Test]
    [Arguments("outside", "configuration=unset platform=unset")]
    [Arguments("member", "")]
    [Arguments("set-configuration", "configuration=kept platform=unset")]
    [Timeout(300_000)]
    public async Task C1780_OutsideSolutionReferenceClearsParentConfiguration(string variant, string expectedFlag)
    {
        var reference = variant == "set-configuration"
            ? """
                <ItemGroup>
                  <ProjectReference Include="../Outside/Outside.csproj">
                    <SetConfiguration>Configuration=Debug</SetConfiguration>
                  </ProjectReference>
                </ItemGroup>
                """
            : """
                <ItemGroup><ProjectReference Include="../Outside/Outside.csproj" /></ItemGroup>
                """;
        var solution = variant == "member"
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
            "outside-solution: tests/Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj configuration=unset platform=unset",
            "outside-solution: tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj configuration=unset platform=unset",
            "outside-solution: tools/Antiphon.TimeoutAudit/Antiphon.TimeoutAudit.csproj configuration=unset platform=unset",
        });
    }
}
