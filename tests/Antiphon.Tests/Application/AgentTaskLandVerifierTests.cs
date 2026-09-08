using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandVerifierTests
{
    [Test]
    [Arguments("SelectedPass", true)]
    [Arguments("SelectedFailure", false)]
    [Arguments("DoesNotExist", false)]
    public async Task C448_V34_RealTUnitSelectionRequiresExecutedPassingTests(string method, bool expected)
    {
        using var fixture = new ScratchGitRepo("antiphon-land-real-verifier");
        var project = Path.Combine(fixture.Path, "tests", "Antiphon.Tests");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(project, "Antiphon.Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>
            <OutputType>Exe</OutputType><IsTestProject>true</IsTestProject>
            <EnableMicrosoftTestingPlatformRunner>true</EnableMicrosoftTestingPlatformRunner>
            <ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
            <PackageReference Include="TUnit" Version="1.44.0" /></ItemGroup></Project>
            """);
        var marker = Path.Combine(fixture.Path, "selected.txt");
        // A test outside the requested filter must never execute. A real selected method
        // writes a marker independently of the verifier's exit-code/TRX interpretation.
        await File.WriteAllTextAsync(Path.Combine(project, "Tests.cs"), $$"""
            using TUnit.Core;
            [Category("Integration")]
            public sealed class VerificationProbe
            {
                [Test] public void SelectedPass() => File.WriteAllText(@"{{marker}}", "selected");
                [Test] public void SelectedFailure() => throw new Exception("selected failure");
                [Test] public void NeverSelected() => throw new Exception("filter was ignored");
            }
            """);
        var protectedPath = Path.Combine(fixture.Path, "bin-land", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedPath)!);
        await File.WriteAllTextAsync(protectedPath, "pre-existing private bytes");
        var result = await new Antiphon.Server.Infrastructure.Git.LandingVerifier().VerifyAsync(fixture.Path,
            $"/*/*/VerificationProbe/{method}", CancellationToken.None);
        result.Passed.ShouldBe(expected, result.Description);
        var common = await new Antiphon.Server.Infrastructure.Git.LandingGit().CommonDirectoryAsync(fixture.Path, CancellationToken.None);
        Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children")).ShouldBeEmpty("both real verifier children must be acknowledged exited");
        File.Exists(marker).ShouldBe(expected);
        if (expected) result.Description.ShouldContain("tests 1/1");
        (await File.ReadAllTextAsync(protectedPath)).ShouldBe("pre-existing private bytes");
        Directory.Exists(Path.Combine(fixture.Path, "obj")).ShouldBeFalse();
        Directory.Exists(Path.Combine(project, "obj")).ShouldBeFalse();
    }
}
