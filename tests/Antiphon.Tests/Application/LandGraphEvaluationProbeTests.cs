using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    // V-11: recording catches an existence dependency even when the file does not exist yet.
    [Test]
    [Arguments("docs/flag.md")]
    [Arguments("docs/missing.md")]
    [Arguments("docs/new")]
    [Arguments("README.md")]
    [Arguments("alias/flag.md")]
    [Timeout(300_000)]
    public async Task C1483_S4_ExistsProbeOfDocsRefuses(string path)
    {
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "root",
            ["docs/flag.md"] = "flag",
            ["App/App.csproj"] = SdkProject($"""
                <PropertyGroup Condition="Exists('$(MSBuildThisFileDirectory)../{path}')"><X>y</X></PropertyGroup>
                <ItemGroup Condition="'$(X)' == 'y'"><ProbeInput Include="safe.txt" /></ItemGroup>
                """),
        }, links: path.StartsWith("alias", StringComparison.Ordinal) ? new() { ["alias"] = "docs" } : null);
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(1, run.Text);
        run.Text.ShouldContain("file-probe", Case.Sensitive, run.Text);
        run.Text.ShouldContain("App/App.csproj", Case.Sensitive, run.Text);
        run.Text.ShouldContain(path, Case.Sensitive, run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();
    }

    // V-12: direct property functions bypass MSBuild's recording file system.
    [Test]
    [Arguments("README.md", false)]
    [Arguments("README.md", true)]
    [Arguments("docs/flag.md", false)]
    [Arguments("docs/flag.md", true)]
    [Timeout(300_000)]
    public async Task C1483_S4_FileReadingPropertyFunctionRefuses(string path, bool condition)
    {
        var spelling = path == "README.md" && condition ? "%52EADME.md" : path;
        var read = $"$([System.IO.File]::ReadAllText(&quot;$(MSBuildThisFileDirectory)../{spelling}&quot;))";
        var body = condition
            ? $"<PropertyGroup Condition=\"HasTrailingSlash('{read}')\"><ProbeValue>changed</ProbeValue></PropertyGroup>"
            : $"<PropertyGroup><ProbeValue>{read}</ProbeValue></PropertyGroup><ItemGroup><ProbeInput Include=\"$(ProbeValue)\" /></ItemGroup>";
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["README.md"] = "safe/",
            ["docs/flag.md"] = "safe/",
            ["App/App.csproj"] = SdkProject(body),
        });
        var run = await repo.RunToolAsync();
        run.ExitCode.ShouldBe(1, run.Text);
        run.Text.ShouldContain("differential", Case.Sensitive, run.Text);
        run.Lines.ShouldNotContain("approved", run.Text);
        repo.AssertUntouched();

        // Matched allowance: non-skippable reads and lexical HasTrailingSlash remain legal.
        await using var safe = await AuditRepo.CreateAsync(new()
        {
            ["App/value.txt"] = "safe/",
            ["App/App.csproj"] = SdkProject("""
                <PropertyGroup Condition="Exists('$(MSBuildThisFileDirectory)value.txt') And HasTrailingSlash('docs/')">
                  <ProbeValue>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)value.txt'))</ProbeValue>
                </PropertyGroup>
                """),
        });
        var safeRun = await safe.RunToolAsync();
        safeRun.ExitCode.ShouldBe(0, safeRun.Text);
        safeRun.Lines.ShouldContain("approved", safeRun.Text);
        safe.AssertUntouched();
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1483_S4_RealRepositoryApproves()
    {
        var run = await AuditRepo.RunToolAtAsync(RepoRoot());
        run.ExitCode.ShouldBe(0, run.Text);
        run.Lines.ShouldContain("approved", run.Text);
        run.Text.ShouldContain("not-built: docs/investigations/evidence/card-0462/Reproduce.csproj");
    }
}
