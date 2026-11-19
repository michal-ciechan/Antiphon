using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class LandVerificationFailureTests
{
    [Test]
    public async Task C1995_LastLinesKeepsTheErrorAndDropsTheEarlyMarker()
    {
        await Task.CompletedTask;
        const string early = "C1995-DROP-THIS-EARLY-LINE";
        const string marker = "C1995-KEEP-THIS-LINE";
        var lines = new List<string> { early };
        for (var i = 0; i < 45; i++) lines.Add("noise " + i);
        lines.Add("error CS0103: The name 'Missing' does not exist in the current context [tests/Failing/Failing.csproj]");
        lines.Add(marker);
        var kept = LandVerificationFailure.LastLines(string.Join("\n", lines));
        kept.ShouldContain(marker);
        kept.ShouldContain("tests/Failing/Failing.csproj");
        kept.ShouldNotContain(early);
        kept.Split('\n').Length.ShouldBe(40);
    }

    [Test]
    public async Task C1995_ProjectListAndIncludesTreatALinkAsTheProject()
    {
        await Task.CompletedTask;
        var output = "error CS0103: The name 'Missing' does not exist in the current context [tests/Failing/Failing.csproj]\n"
            + "error MSB3073: The command failed [tests/Other/Other.csproj]";
        LandVerificationFailure.ProjectList(output, null).ShouldBe("tests/Failing/Failing.csproj",
            "an MSB infrastructure error names no broken project; only CS compiler errors do");
        var includes = LandVerificationFailure.CompileIncludes(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="Broken.cs" />
                <Compile Include="../Shared/Broken.cs" />
              </ItemGroup>
            </Project>
            """,
            "tests/Failing/Failing.csproj");
        includes.ShouldNotBeNull();
        LandVerificationFailure.Touches(["feature.txt"], "tests/Failing/Failing.csproj", includes!).ShouldBeFalse();
        LandVerificationFailure.Touches(["tests/Shared/Broken.cs"], "tests/Failing/Failing.csproj", includes!).ShouldBeTrue();
        LandVerificationFailure.Touches(["tests/Failing/Broken.cs"], "tests/Failing/Failing.csproj", includes!).ShouldBeTrue();
        LandVerificationFailure.CompileIncludes(
            """<Project><ItemGroup><Compile Include="../Shared/**/*.cs" /></ItemGroup></Project>""",
            "tests/Failing/Failing.csproj").ShouldBeNull();
    }
}
